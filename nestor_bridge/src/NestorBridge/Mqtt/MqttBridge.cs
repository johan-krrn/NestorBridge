using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using NestorBridge.Configuration;

namespace NestorBridge.Mqtt;

public sealed class MqttBridge : IMqttBridge, IAsyncDisposable
{
  private readonly IMqttClient _client;
  private readonly BridgeOptions _options;
  private readonly ILogger<MqttBridge> _logger;
  private int _reconnectDelayMs = 1000;
  private const int MaxReconnectDelayMs = 60_000;

  public event Func<string, byte[], Task>? MessageReceived;

  public MqttBridge(IOptions<BridgeOptions> options, ILogger<MqttBridge> logger)
  {
    _options = options.Value;
    _logger = logger;
    var factory = new MqttFactory();
    _client = factory.CreateMqttClient();

    _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    _client.DisconnectedAsync += OnDisconnectedAsync;
  }

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    var optionsBuilder = new MqttClientOptionsBuilder()
        .WithProtocolVersion(MqttProtocolVersion.V500)
        .WithClientId(_options.MqttClientId)
        .WithTcpServer(_options.MqttHost, _options.MqttPort)
        .WithCleanSession(false)
        .WithSessionExpiryInterval(3600)
        .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

    if (string.Equals(_options.AuthMode, "x509", StringComparison.OrdinalIgnoreCase))
    {
      ConfigureX509(optionsBuilder);
    }
    else
    {
      ConfigureSas(optionsBuilder);
    }

    var mqttOptions = optionsBuilder.Build();

    _logger.LogInformation("Connecting to MQTT broker {Host}:{Port} (auth={AuthMode})",
        _options.MqttHost, _options.MqttPort, _options.AuthMode);

    await _client.ConnectAsync(mqttOptions, cancellationToken);

    // Subscribe to commands topic
    var commandTopic = Topics.Commands(_options.BoxId);
    await _client.SubscribeAsync(new MqttTopicFilterBuilder()
        .WithTopic(commandTopic)
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build(), cancellationToken);

    _logger.LogInformation("Subscribed to {Topic}", commandTopic);
    _reconnectDelayMs = 1000; // Reset on successful connect
  }

  public async Task PublishAsync(string topic, byte[] payload, MqttQualityOfServiceLevel qos,
      CancellationToken cancellationToken)
  {
    var message = new MqttApplicationMessageBuilder()
        .WithTopic(topic)
        .WithPayload(payload)
        .WithQualityOfServiceLevel(qos)
        .Build();

    await _client.PublishAsync(message, cancellationToken);
  }

  public async Task DisconnectAsync(CancellationToken cancellationToken)
  {
    if (_client.IsConnected)
    {
      await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
          .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
          .Build(), cancellationToken);
    }
  }

  private void ConfigureX509(MqttClientOptionsBuilder builder)
  {
    if (!File.Exists(_options.CertPath))
      throw new FileNotFoundException($"Client certificate not found: {_options.CertPath}");
    if (!File.Exists(_options.KeyPath))
      throw new FileNotFoundException($"Client key not found: {_options.KeyPath}");

    var cert = X509Certificate2.CreateFromPemFile(_options.CertPath, _options.KeyPath);

    var tlsOptions = new MqttClientTlsOptionsBuilder()
        .UseTls()
        .WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13)
        .WithClientCertificates(new List<X509Certificate2> { cert })
        .WithCertificateValidationHandler(ctx => ctx.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None);

    builder.WithTlsOptions(tlsOptions.Build());
  }

  private void ConfigureSas(MqttClientOptionsBuilder builder)
  {
    if (!string.IsNullOrEmpty(_options.SasUsername))
      builder.WithCredentials(_options.SasUsername, _options.SasPassword);

    if (_options.NoTls)
    {
      _logger.LogWarning("TLS disabled (no_tls=true) — FOR LOCAL TESTING ONLY");
      return;
    }

    var tlsOptions = new MqttClientTlsOptionsBuilder()
        .UseTls()
        .WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13)
        .WithCertificateValidationHandler(ctx => ctx.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None);

    if (File.Exists(_options.CaPath))
    {
      var caCert = new X509Certificate2(_options.CaPath);
      tlsOptions.WithTrustChain(new X509Certificate2Collection { caCert });
    }

    builder.WithTlsOptions(tlsOptions.Build());
  }

  private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
  {
    var topic = args.ApplicationMessage.Topic;
    var payload = args.ApplicationMessage.PayloadSegment.ToArray();

    _logger.LogDebug("MQTT message received on {Topic} ({Bytes} bytes)", topic, payload.Length);

    if (MessageReceived is not null)
    {
      await MessageReceived.Invoke(topic, payload);
    }
  }

  private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
  {
    _logger.LogWarning("MQTT disconnected (reason={Reason}). Reconnecting in {Delay}ms...",
        args.Reason, _reconnectDelayMs);

    await Task.Delay(_reconnectDelayMs);
    _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, MaxReconnectDelayMs);

    try
    {
      await ConnectAsync(CancellationToken.None);
      _logger.LogInformation("MQTT reconnected successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "MQTT reconnection failed, will retry");
    }
  }

  public async ValueTask DisposeAsync()
  {
    await DisconnectAsync(CancellationToken.None);
    _client.Dispose();
  }
}
