namespace NestorBridge.Mqtt;

/// <summary>
/// Helper methods for constructing MQTT topic strings following Nestor convention.
/// </summary>
public static class Topics
{
  public static string Commands(string boxId) =>
      $"devices/{boxId}/commands/#";

  public static string CommandAck(string boxId, string commandId) =>
      $"devices/{boxId}/commands/{commandId}/ack";

  public static string TelemetryState(string boxId, string entityId) =>
      $"devices/{boxId}/telemetry/state/{entityId}";

  public static string TelemetryEvent(string boxId, string eventType) =>
      $"devices/{boxId}/telemetry/event/{eventType}";

  public static string Heartbeat(string boxId) =>
      $"devices/{boxId}/heartbeat";
}
