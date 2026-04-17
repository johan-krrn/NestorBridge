using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using NestorBridge.Configuration;

namespace NestorBridge.Services;

/// <summary>
/// Optional direct publisher to the local HA MQTT broker (Mosquitto add-on).
/// Activated when local_mqtt_host is set in options. Bypasses the dependency on HA's
/// mqtt.publish WebSocket service, so passthrough commands reach zigbee2mqtt reliably.
/// </summary>
public sealed class LocalMqttPublisher : IAsyncDisposable
{
  private readonly BridgeOptions _options;
  private readonly ILogger<LocalMqttPublisher> _logger;
  private readonly IMqttClient _client;
  private readonly SemaphoreSlim _connectLock = new(1, 1);

  public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.LocalMqttHost);

  public LocalMqttPublisher(IOptions<BridgeOptions> options, ILogger<LocalMqttPublisher> logger)
  {
    _options = options.Value;
    _logger = logger;
    _client = new MqttFactory().CreateMqttClient();
  }

  public async Task PublishAsync(string topic, string payload, CancellationToken cancellationToken)
  {
    if (!IsEnabled)
      throw new InvalidOperationException("LocalMqttPublisher is not enabled (local_mqtt_host not set)");

    await EnsureConnectedAsync(cancellationToken);

    var message = new MqttApplicationMessageBuilder()
        .WithTopic(topic)
        .WithPayload(payload)
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build();

    await _client.PublishAsync(message, cancellationToken);
    _logger.LogDebug("Published to local MQTT topic {Topic}", topic);
  }

  private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
  {
    if (_client.IsConnected) return;

    await _connectLock.WaitAsync(cancellationToken);
    try
    {
      if (_client.IsConnected) return;

      var builder = new MqttClientOptionsBuilder()
          .WithTcpServer(_options.LocalMqttHost, _options.LocalMqttPort)
          .WithClientId($"nestor-bridge-local-{Environment.MachineName}");

      if (!string.IsNullOrWhiteSpace(_options.LocalMqttUser))
        builder.WithCredentials(_options.LocalMqttUser, _options.LocalMqttPassword ?? string.Empty);

      _logger.LogInformation("Connecting to local MQTT broker {Host}:{Port}",
          _options.LocalMqttHost, _options.LocalMqttPort);

      await _client.ConnectAsync(builder.Build(), cancellationToken);
      _logger.LogInformation("Connected to local MQTT broker");
    }
    finally
    {
      _connectLock.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_client.IsConnected)
    {
      try { await _client.DisconnectAsync(); }
      catch { /* best effort */ }
    }
    _client.Dispose();
    _connectLock.Dispose();
  }
}
