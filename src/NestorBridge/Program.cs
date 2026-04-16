using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant;
using NestorBridge.Mqtt;
using NestorBridge.Services;
using NestorBridge.Translation;

var builder = Host.CreateApplicationBuilder(args);

// Config from /data/options.json (injected by HA Supervisor)
builder.Configuration.AddHaOptionsJson();

// Bind options and validate
builder.Services.Configure<BridgeOptions>(builder.Configuration);
var options = builder.Configuration.Get<BridgeOptions>();
if (options is null)
  throw new InvalidOperationException("Failed to load BridgeOptions from /data/options.json");
options.Validate();

// Structured JSON logging for Supervisor / Azure ingestion
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
  o.IncludeScopes = true;
  o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
  o.UseUtcTimestamp = true;
});

// Set log level from config
if (Enum.TryParse<LogLevel>(options.LogLevel, ignoreCase: true, out var logLevel))
{
  builder.Logging.SetMinimumLevel(logLevel);
}

// Singleton clients
builder.Services.AddSingleton<IMqttBridge, MqttBridge>();
builder.Services.AddSingleton<IHaWebSocketClient, HaWebSocketClient>();
builder.Services.AddSingleton<HaServiceCaller>();
builder.Services.AddSingleton<CommandTranslator>();
builder.Services.AddSingleton<TelemetryTranslator>();

// Bootstrap FIRST — Generic Host starts IHostedService in registration order.
// Connections must be established before workers subscribe to events.
builder.Services.AddHostedService<BootstrapService>();

// Workers start after bootstrap
builder.Services.AddHostedService<DownlinkWorker>();
builder.Services.AddHostedService<UplinkWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();

var app = builder.Build();
await app.RunAsync();

/// <summary>
/// Ensures MQTT and HA WebSocket are connected before workers start processing.
/// Registered first so it starts first.
/// </summary>
file sealed class BootstrapService : IHostedService
{
  private readonly IMqttBridge _mqtt;
  private readonly IHaWebSocketClient _haClient;
  private readonly ILogger<BootstrapService> _logger;

  public BootstrapService(IMqttBridge mqtt, IHaWebSocketClient haClient, ILogger<BootstrapService> logger)
  {
    _mqtt = mqtt;
    _haClient = haClient;
    _logger = logger;
  }

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Nestor Bridge starting — connecting to HA WebSocket and MQTT...");

    await _haClient.ConnectAsync(cancellationToken);
    _logger.LogInformation("HA WebSocket connected");

    await _mqtt.ConnectAsync(cancellationToken);
    _logger.LogInformation("MQTT connected");
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Nestor Bridge shutting down...");
    await _mqtt.DisconnectAsync(cancellationToken);
    await _haClient.DisconnectAsync(cancellationToken);
  }
}
