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

  /// <summary>
  /// Extract the HA MQTT sub-topic from a full downlink topic.
  /// e.g. "devices/mybox/commands/zigbee2mqtt/prise/set" → "zigbee2mqtt/prise/set"
  /// Returns null if the topic does not match the expected prefix.
  /// </summary>
  public static string? ExtractSubTopic(string boxId, string fullTopic)
  {
    var prefix = $"devices/{boxId}/commands/";
    return fullTopic.StartsWith(prefix, StringComparison.Ordinal) && fullTopic.Length > prefix.Length
        ? fullTopic[prefix.Length..]
        : null;
  }
}
