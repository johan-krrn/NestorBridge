using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant;
using NestorBridge.Mqtt;
using NestorBridge.Translation;

namespace NestorBridge.Services;

/// <summary>
/// Downlink worker: receives MQTT commands from cloud, translates them to HA service calls,
/// and publishes ack results back to cloud.
/// </summary>
public sealed class DownlinkWorker : IHostedService
{
  private readonly IMqttBridge _mqtt;
  private readonly HaServiceCaller _serviceCaller;
  private readonly CommandTranslator _translator;
  private readonly BridgeOptions _options;
  private readonly ILogger<DownlinkWorker> _logger;

  public DownlinkWorker(
      IMqttBridge mqtt,
      HaServiceCaller serviceCaller,
      CommandTranslator translator,
      IOptions<BridgeOptions> options,
      ILogger<DownlinkWorker> logger)
  {
    _mqtt = mqtt;
    _serviceCaller = serviceCaller;
    _translator = translator;
    _options = options.Value;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _mqtt.MessageReceived += OnMqttMessageAsync;
    _logger.LogInformation("DownlinkWorker started");
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _mqtt.MessageReceived -= OnMqttMessageAsync;
    _logger.LogInformation("DownlinkWorker stopped");
    return Task.CompletedTask;
  }

  private async Task OnMqttMessageAsync(string topic, byte[] payload)
  {
    _logger.LogDebug("Downlink command received on {Topic}", topic);

    var command = _translator.Translate(payload);
    if (command is null)
    {
      _logger.LogWarning("Malformed command on {Topic}, sending error ack", topic);
      // Can't ack without commandId — just log
      return;
    }

    var (success, contextId, error) = await _serviceCaller.ExecuteCommandAsync(
        command, CancellationToken.None);

    var ackPayload = _translator.BuildAck(command.CommandId, success, error, contextId);
    var ackTopic = Topics.CommandAck(_options.BoxId, command.CommandId);

    await _mqtt.PublishAsync(ackTopic, ackPayload,
        MqttQualityOfServiceLevel.AtLeastOnce, CancellationToken.None);

    _logger.LogInformation("Command {CommandId} processed: {Status}",
        command.CommandId, success ? "success" : "error");
  }
}
