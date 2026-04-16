using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant;
using NestorBridge.HomeAssistant.Models;
using NestorBridge.Mqtt;
using NestorBridge.Translation;

namespace NestorBridge.Services;

/// <summary>
/// Uplink worker: subscribes to HA state_changed events via WebSocket,
/// translates them to telemetry payloads, and publishes to cloud MQTT.
/// </summary>
public sealed class UplinkWorker : IHostedService
{
  private readonly IHaWebSocketClient _haClient;
  private readonly IMqttBridge _mqtt;
  private readonly TelemetryTranslator _translator;
  private readonly BridgeOptions _options;
  private readonly ILogger<UplinkWorker> _logger;

  public UplinkWorker(
      IHaWebSocketClient haClient,
      IMqttBridge mqtt,
      TelemetryTranslator translator,
      IOptions<BridgeOptions> options,
      ILogger<UplinkWorker> logger)
  {
    _haClient = haClient;
    _mqtt = mqtt;
    _translator = translator;
    _options = options.Value;
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
    var topic = Topics.TelemetryState(_options.BoxId, entityId);

    try
    {
      await _mqtt.PublishAsync(topic, payload,
          MqttQualityOfServiceLevel.AtMostOnce, CancellationToken.None);

      _logger.LogDebug("Telemetry published for {EntityId}", entityId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to publish telemetry for {EntityId}", entityId);
    }
  }
}
