using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NestorBridge.Configuration;

namespace NestorBridge.SignalR;

public sealed class SignalRBridgeClient : ISignalRBridgeClient, IAsyncDisposable
{
  private HubConnection? _connection;
  private readonly BridgeOptions _options;
  private readonly ILogger<SignalRBridgeClient> _logger;

  public event Func<string, string, Task>? CommandReceived;

  public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.SignalrHubUrl);

  public SignalRBridgeClient(IOptions<BridgeOptions> options, ILogger<SignalRBridgeClient> logger)
  {
    _options = options.Value;
    _logger = logger;
  }

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    if (!IsEnabled)
    {
      _logger.LogInformation("SignalR is disabled (no hub URL configured)");
      return;
    }

    var hubUrl = _options.SignalrHubUrl.TrimEnd('/');
    var separator = hubUrl.Contains('?') ? '&' : '?';
    var urlWithKey = string.IsNullOrWhiteSpace(_options.SignalrApiKey)
        ? hubUrl
        : $"{hubUrl}{separator}apiKey={Uri.EscapeDataString(_options.SignalrApiKey)}";

    _connection = new HubConnectionBuilder()
        .WithUrl(urlWithKey)
        .WithAutomaticReconnect(new[]
        {
          TimeSpan.FromSeconds(0),
          TimeSpan.FromSeconds(2),
          TimeSpan.FromSeconds(5),
          TimeSpan.FromSeconds(15),
          TimeSpan.FromSeconds(30),
          TimeSpan.FromSeconds(60)
        })
        .Build();

    // Server confirms connection
    _connection.On<object>("Connected", info =>
    {
      _logger.LogInformation("SignalR hub confirmed connection: {Info}", info);
    });

    // Commands from mobile clients
    _connection.On<object>("ReceiveFromClient", async message =>
    {
      _logger.LogDebug("SignalR command received from client: {Message}", message);
      if (CommandReceived is not null)
      {
        try
        {
          // The hub sends a message object; extract commandType and payload
          var json = message?.ToString() ?? "{}";
          await CommandReceived.Invoke("ReceiveFromClient", json);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error processing SignalR command");
        }
      }
    });

    // Pong response
    _connection.On<object>("Pong", info =>
    {
      _logger.LogDebug("SignalR Pong: {Info}", info);
    });

    _connection.Reconnecting += error =>
    {
      _logger.LogWarning("SignalR reconnecting... {Error}", error?.Message);
      return Task.CompletedTask;
    };

    _connection.Reconnected += connectionId =>
    {
      _logger.LogInformation("SignalR reconnected with ID: {ConnectionId}", connectionId);
      return Task.CompletedTask;
    };

    _connection.Closed += error =>
    {
      _logger.LogWarning("SignalR connection closed: {Error}", error?.Message);
      return Task.CompletedTask;
    };

    _logger.LogInformation("Connecting to SignalR hub at {Url}", hubUrl);
    await _connection.StartAsync(cancellationToken);
    _logger.LogInformation("SignalR connected (state={State})", _connection.State);
  }

  public async Task RelayToClientsAsync(string eventType, string payload, CancellationToken cancellationToken)
  {
    if (_connection?.State != HubConnectionState.Connected)
    {
      _logger.LogDebug("SignalR not connected, skipping relay for {EventType}", eventType);
      return;
    }

    await _connection.InvokeAsync("RelayToClients", eventType, payload, cancellationToken);
  }

  public async Task DisconnectAsync(CancellationToken cancellationToken)
  {
    if (_connection is not null)
    {
      await _connection.StopAsync(cancellationToken);
      _logger.LogInformation("SignalR disconnected");
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_connection is not null)
    {
      await _connection.DisposeAsync();
    }
  }
}
