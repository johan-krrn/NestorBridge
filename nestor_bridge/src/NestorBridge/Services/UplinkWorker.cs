using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant;
using NestorBridge.HomeAssistant.Models;
using NestorBridge.Mqtt;
using NestorBridge.SignalR;
using NestorBridge.Translation;
using NestorBridge.Web;

namespace NestorBridge.Services;

public sealed class UplinkWorker : IHostedService
{
  private readonly IHaWebSocketClient _haClient;
  private readonly IMqttBridge _mqtt;
  private readonly ISignalRBridgeClient _signalR;
  private readonly TelemetryTranslator _translator;
  private readonly BridgeOptions _options;
  private readonly MessageLog _messageLog;
  private readonly ILogger<UplinkWorker> _logger;

  public UplinkWorker(
      IHaWebSocketClient haClient,
      IMqttBridge mqtt,
      ISignalRBridgeClient signalR,
      TelemetryTranslator translator,
      IOptions<BridgeOptions> options,
      MessageLog messageLog,
      ILogger<UplinkWorker> logger)
  {
    _haClient = haClient;
    _mqtt = mqtt;
    _signalR = signalR;
    _translator = translator;
    _options = options.Value;
    _messageLog = messageLog;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _haClient.StateChanged += OnStateChangedAsync;
    _logger.LogInformation("UplinkWorker started");
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _haClient.StateChanged -= OnStateChangedAsync;
    _logger.LogInformation("UplinkWorker stopped");
    return Task.CompletedTask;
  }

  private async Task OnStateChangedAsync(HaEvent haEvent)
  {
    var result = _translator.Translate(haEvent);
    if (result is null)
      return;

    var (entityId, payload) = result.Value;
    var payloadStr = System.Text.Encoding.UTF8.GetString(payload);

    // Publish via MQTT if enabled
    if (_mqtt.IsEnabled)
    {
      var topic = Topics.TelemetryState(_options.BoxId, entityId);
      try
      {
        await _mqtt.PublishAsync(topic, payload,
            MqttQualityOfServiceLevel.AtMostOnce, CancellationToken.None);

        _messageLog.Add(new MessageLogEntry(
            DateTime.UtcNow, MessageDirection.Outbound, topic, payloadStr));

        _logger.LogDebug("Telemetry published via MQTT for {EntityId}", entityId);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to publish telemetry via MQTT for {EntityId}", entityId);
      }
    }

    // Relay to mobile clients via SignalR if enabled
    if (_signalR.IsEnabled)
    {
      try
      {
        await _signalR.RelayToClientsAsync("state_changed", payloadStr, CancellationToken.None);
        _logger.LogDebug("Telemetry relayed via SignalR for {EntityId}", entityId);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to relay telemetry via SignalR for {EntityId}", entityId);
      }
    }
  }
}
