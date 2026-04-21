namespace NestorBridge.SignalR;

public interface ISignalRBridgeClient
{
  /// <summary>Raised when a command is received from a mobile client via the hub.</summary>
  event Func<string, string, Task>? CommandReceived;

  /// <summary>Returns true when the SignalR feature is configured (hub URL set).</summary>
  bool IsEnabled { get; }

  /// <summary>Connect to the SignalR hub and register as a Bridge.</summary>
  Task ConnectAsync(CancellationToken cancellationToken);

  /// <summary>Relay a state event to all mobile clients via the hub.</summary>
  Task RelayToClientsAsync(string eventType, string payload, CancellationToken cancellationToken);

  /// <summary>Disconnect cleanly.</summary>
  Task DisconnectAsync(CancellationToken cancellationToken);
}
