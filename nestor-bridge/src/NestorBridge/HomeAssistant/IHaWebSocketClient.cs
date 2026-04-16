using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.HomeAssistant;

public interface IHaWebSocketClient
{
  /// <summary>Raised when a state_changed event is received.</summary>
  event Func<HaEvent, Task>? StateChanged;

  /// <summary>Connect, authenticate, and subscribe to events.</summary>
  Task ConnectAsync(CancellationToken cancellationToken);

  /// <summary>Call a service on HA (e.g. light.turn_on).</summary>
  Task<HaMessage> CallServiceAsync(string domain, string service, string entityId,
      Dictionary<string, object>? serviceData, CancellationToken cancellationToken);

  /// <summary>Disconnect cleanly.</summary>
  Task DisconnectAsync(CancellationToken cancellationToken);
}
