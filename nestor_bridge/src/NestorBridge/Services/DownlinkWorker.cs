using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant;
using NestorBridge.Mqtt;
using NestorBridge.SignalR;
using NestorBridge.Translation;
using NestorBridge.Web;

namespace NestorBridge.Services;

/// <summary>
/// Downlink worker: receives MQTT commands from cloud, translates them to HA service calls,
/// and publishes ack results back to cloud.
/// </summary>
public sealed class DownlinkWorker : IHostedService
{
  private readonly IMqttBridge _mqtt;
  private readonly ISignalRBridgeClient _signalR;
  private readonly HaServiceCaller _serviceCaller;
  private readonly CommandTranslator _translator;
  private readonly BridgeOptions _options;
  private readonly MessageLog _messageLog;
  private readonly ILogger<DownlinkWorker> _logger;

  public DownlinkWorker(
      IMqttBridge mqtt,
      ISignalRBridgeClient signalR,
      HaServiceCaller serviceCaller,
      CommandTranslator translator,
      IOptions<BridgeOptions> options,
      MessageLog messageLog,
      ILogger<DownlinkWorker> logger)
  {
    _mqtt = mqtt;
    _signalR = signalR;
    _serviceCaller = serviceCaller;
    _translator = translator;
    _options = options.Value;
    _messageLog = messageLog;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (_mqtt.IsEnabled)
      _mqtt.MessageReceived += OnMqttMessageAsync;
    if (_signalR.IsEnabled)
      _signalR.CommandReceived += OnSignalRCommandAsync;
    _logger.LogInformation("DownlinkWorker started (MQTT={MqttEnabled}, SignalR={SignalREnabled})",
        _mqtt.IsEnabled, _signalR.IsEnabled);
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    if (_mqtt.IsEnabled)
      _mqtt.MessageReceived -= OnMqttMessageAsync;
    if (_signalR.IsEnabled)
      _signalR.CommandReceived -= OnSignalRCommandAsync;
    _logger.LogInformation("DownlinkWorker stopped");
    return Task.CompletedTask;
  }

  private async Task OnMqttMessageAsync(string topic, byte[] payload)
  {
    var payloadStr = System.Text.Encoding.UTF8.GetString(payload);
    _logger.LogDebug("Downlink command received on {Topic}", topic);

    // Log inbound command
    _messageLog.Add(new MessageLogEntry(
        DateTime.UtcNow, MessageDirection.Inbound, topic, payloadStr));

    var command = _translator.Translate(payload);
    if (command is null)
    {
      // Not a CloudCommand envelope — try MQTT passthrough:
      // extract the HA MQTT sub-topic from the MQTT topic path and forward the raw payload.
      var subTopic = Topics.ExtractSubTopic(_options.BoxId, topic);
      if (subTopic is null)
      {
        _logger.LogWarning("Malformed command on {Topic} and no sub-topic extractable, dropping", topic);
        return;
      }

      _logger.LogInformation("No CloudCommand parsed; forwarding raw payload to HA MQTT topic {SubTopic}", subTopic);

      var (ptSuccess, ptError) = await _serviceCaller.PublishMqttAsync(
          subTopic, payloadStr, CancellationToken.None);

      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound, subTopic, payloadStr,
          ptSuccess ? "mqtt-passthrough" : $"error: {ptError}"));
      return;
    }

    var (success, contextId, error) = await _serviceCaller.ExecuteCommandAsync(
        command, CancellationToken.None);

    var ackPayload = _translator.BuildAck(command.CommandId, success, error, contextId);
    var ackTopic = Topics.CommandAck(_options.BoxId, command.CommandId);

    await _mqtt.PublishAsync(ackTopic, ackPayload,
        MqttQualityOfServiceLevel.AtLeastOnce, CancellationToken.None);

    // Log outbound ack
    _messageLog.Add(new MessageLogEntry(
        DateTime.UtcNow, MessageDirection.Outbound, ackTopic,
        System.Text.Encoding.UTF8.GetString(ackPayload),
        success ? "success" : "error"));

    _logger.LogInformation("Command {CommandId} processed: {Status}",
        command.CommandId, success ? "success" : "error");
  }

  /// <summary>
  /// Handle commands arriving from a mobile client via SignalR hub.
  /// Supports two commandTypes:
  ///   - "call_service": standard CloudCommand envelope  
  ///   - "mqtt_publish": raw topic+message to forward to HA local MQTT
  /// </summary>
  private async Task OnSignalRCommandAsync(string commandType, string json)
  {
    _logger.LogDebug("SignalR downlink received: type={CommandType}", commandType);

    _messageLog.Add(new MessageLogEntry(
        DateTime.UtcNow, MessageDirection.Inbound, $"signalr/{commandType}", json));

    try
    {
      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;

      // The hub may wrap payload inside an object with eventType/payload fields,
      // or it may send the command object directly.
      var payload = root.TryGetProperty("payload", out var p) ? p : root;

      if (commandType == "ReceiveFromClient" && payload.TryGetProperty("commandType", out var ct))
      {
        // Unwrap the inner commandType (call_service, mqtt_publish)
        commandType = ct.GetString() ?? commandType;
        payload = payload.TryGetProperty("payload", out var inner) ? inner : payload;
      }

      if (string.Equals(commandType, "mqtt_publish", StringComparison.OrdinalIgnoreCase))
      {
        var topic = payload.GetProperty("topic").GetString()!;
        var message = payload.TryGetProperty("message", out var m) ? m.GetRawText().Trim('"') : "{}";

        _logger.LogInformation("SignalR mqtt_publish to {Topic}", topic);
        var (success, error) = await _serviceCaller.PublishMqttAsync(topic, message, CancellationToken.None);

        _messageLog.Add(new MessageLogEntry(
            DateTime.UtcNow, MessageDirection.Outbound, topic, message,
            success ? "signalr-mqtt" : $"error: {error}"));
        return;
      }

      // Default: treat as call_service — deserialize as CloudCommand
      var command = JsonSerializer.Deserialize<HomeAssistant.Models.CloudCommand>(payload.GetRawText());
      if (command is null || string.IsNullOrWhiteSpace(command.Action))
      {
        _logger.LogWarning("Malformed SignalR call_service command, ignoring");
        return;
      }

      // Generate a commandId if missing
      if (string.IsNullOrWhiteSpace(command.CommandId))
        command.CommandId = Guid.NewGuid().ToString("N")[..12];

      var (svcSuccess, contextId, svcError) = await _serviceCaller.ExecuteCommandAsync(
          command, CancellationToken.None);

      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound,
          $"ha/{command.TargetEntityId}/{command.Action}",
          json,
          svcSuccess ? "signalr-ok" : $"error: {svcError}"));

      _logger.LogInformation("SignalR command {CommandId} processed: {Status}",
          command.CommandId, svcSuccess ? "success" : "error");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing SignalR command");
    }
  }
}
