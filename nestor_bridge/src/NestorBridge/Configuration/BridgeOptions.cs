using Microsoft.Extensions.Configuration;

namespace NestorBridge.Configuration;

public sealed class TelemetryFilterOptions
{
  [ConfigurationKeyName("domains")]
  public List<string> Domains { get; set; } = new();
}

public sealed class BridgeOptions
{
  [ConfigurationKeyName("mqtt_host")]
  public string MqttHost { get; set; } = string.Empty;

  [ConfigurationKeyName("mqtt_port")]
  public int MqttPort { get; set; } = 8883;

  [ConfigurationKeyName("mqtt_client_id")]
  public string MqttClientId { get; set; } = string.Empty;

  [ConfigurationKeyName("box_id")]
  public string BoxId { get; set; } = string.Empty;

  [ConfigurationKeyName("cert_path")]
  public string CertPath { get; set; } = "/ssl/nestor/device.pem";

  [ConfigurationKeyName("key_path")]
  public string KeyPath { get; set; } = "/ssl/nestor/device.key";

  [ConfigurationKeyName("ca_path")]
  public string CaPath { get; set; } = "/ssl/nestor/ca.pem";

  [ConfigurationKeyName("log_level")]
  public string LogLevel { get; set; } = "info";

  [ConfigurationKeyName("auth_mode")]
  public string AuthMode { get; set; } = "sas"; // "sas" | "x509"

  [ConfigurationKeyName("sas_username")]
  public string SasUsername { get; set; } = string.Empty;

  [ConfigurationKeyName("sas_password")]
  public string SasPassword { get; set; } = string.Empty;

  /// <summary>Disable TLS for local test against plain Mosquitto (never use in production).</summary>
  [ConfigurationKeyName("no_tls")]
  public bool NoTls { get; set; } = false;

  /// <summary>Override HA WebSocket endpoint (default: ws://supervisor/core/websocket).</summary>
  [ConfigurationKeyName("ha_ws_endpoint")]
  public string HaWsEndpoint { get; set; } = string.Empty;

  /// <summary>SignalR hub URL (e.g. https://your-server.com/hub/nestor).</summary>
  [ConfigurationKeyName("signalr_hub_url")]
  public string SignalrHubUrl { get; set; } = string.Empty;

  /// <summary>API key for authenticating as a Bridge on the SignalR hub.</summary>
  [ConfigurationKeyName("signalr_api_key")]
  public string SignalrApiKey { get; set; } = string.Empty;

  [ConfigurationKeyName("telemetry_filter")]
  public TelemetryFilterOptions TelemetryFilter { get; set; } = new();

  /// <summary>Whether MQTT is configured (mqtt_host is set).</summary>
  public bool IsMqttEnabled => !string.IsNullOrWhiteSpace(MqttHost);

  /// <summary>Whether SignalR is configured (hub URL is set).</summary>
  public bool IsSignalREnabled => !string.IsNullOrWhiteSpace(SignalrHubUrl);

  /// <summary>
  /// Validates required configuration fields. Throws if invalid.
  /// At least one transport (MQTT or SignalR) must be configured.
  /// box_id is always required.
  /// </summary>
  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(BoxId))
      throw new InvalidOperationException("box_id is required in options.json");

    if (!IsMqttEnabled && !IsSignalREnabled)
      throw new InvalidOperationException(
          "At least one transport must be configured: set mqtt_host (MQTT) or signalr_hub_url (SignalR) in options.json");

    // When MQTT is enabled, mqtt_client_id is required
    if (IsMqttEnabled && string.IsNullOrWhiteSpace(MqttClientId))
      throw new InvalidOperationException("mqtt_client_id is required when mqtt_host is configured");
  }
}
