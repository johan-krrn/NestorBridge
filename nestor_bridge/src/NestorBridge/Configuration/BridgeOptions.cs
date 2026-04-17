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

  [ConfigurationKeyName("telemetry_filter")]
  public TelemetryFilterOptions TelemetryFilter { get; set; } = new();

  /// <summary>Optional: hostname of local HA MQTT broker (e.g. core-mosquitto) for direct MQTT passthrough.
  /// When set, passthrough commands are published directly to this broker instead of via mqtt.publish WebSocket service.</summary>
  [ConfigurationKeyName("local_mqtt_host")]
  public string? LocalMqttHost { get; set; }

  [ConfigurationKeyName("local_mqtt_port")]
  public int LocalMqttPort { get; set; } = 1883;

  [ConfigurationKeyName("local_mqtt_user")]
  public string? LocalMqttUser { get; set; }

  [ConfigurationKeyName("local_mqtt_password")]
  public string? LocalMqttPassword { get; set; }

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
