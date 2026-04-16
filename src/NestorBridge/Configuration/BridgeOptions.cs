namespace NestorBridge.Configuration;

public sealed class TelemetryFilterOptions
{
  public List<string> Domains { get; set; } = new();
}

public sealed class BridgeOptions
{
  public string MqttHost { get; set; } = string.Empty;
  public int MqttPort { get; set; } = 8883;
  public string MqttClientId { get; set; } = string.Empty;
  public string BoxId { get; set; } = string.Empty;
  public string CertPath { get; set; } = "/ssl/nestor/device.pem";
  public string KeyPath { get; set; } = "/ssl/nestor/device.key";
  public string CaPath { get; set; } = "/ssl/nestor/ca.pem";
  public string LogLevel { get; set; } = "info";
  public string AuthMode { get; set; } = "sas"; // "sas" | "x509"
  public string SasUsername { get; set; } = string.Empty;
  public string SasPassword { get; set; } = string.Empty;
  /// <summary>Disable TLS for local test against plain Mosquitto (never use in production).</summary>
  public bool NoTls { get; set; } = false;
  /// <summary>Override HA WebSocket endpoint (default: ws://supervisor/core/websocket).</summary>
  public string HaWsEndpoint { get; set; } = string.Empty;
  public TelemetryFilterOptions TelemetryFilter { get; set; } = new();

  /// <summary>
  /// Validates required configuration fields. Throws if invalid.
  /// </summary>
  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(MqttHost))
      throw new InvalidOperationException("mqtt_host is required in options.json");
    if (string.IsNullOrWhiteSpace(BoxId))
      throw new InvalidOperationException("box_id is required in options.json");
    if (string.IsNullOrWhiteSpace(MqttClientId))
      throw new InvalidOperationException("mqtt_client_id is required in options.json");
  }
}
