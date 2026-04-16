# Nestor Bridge — Home Assistant Add-on

Pont protocolaire bidirectionnel entre **Azure Event Grid Namespace (MQTT v5)** et **Home Assistant** via WebSocket API.

## Architecture

```
Azure Event Grid (MQTT v5)  <──TLS──>  [nestor-bridge]  <──WS──>  Home Assistant Core
            │                              │
     commands/#              state_changed events
     ack publish             call_service
     telemetry               heartbeat
```

## Prérequis

- .NET 8 SDK
- Docker (pour build ARM64)
- Un broker MQTT v5 (Event Grid ou Mosquitto local pour les tests)
- Home Assistant OS avec Supervisor

## Build local

```bash
cd nestor-bridge

# Restaurer et compiler
dotnet restore src/NestorBridge/NestorBridge.csproj
dotnet build src/NestorBridge/NestorBridge.csproj -c Release

# Publier pour ARM64
dotnet publish src/NestorBridge/NestorBridge.csproj -c Release -r linux-arm64 --self-contained false -o ./publish
```

## Tests

```bash
dotnet test src/NestorBridge.Tests/
```

## Test local avec Mosquitto (stub Event Grid)

### 1. Lancer Mosquitto en local

```bash
docker run -d --name mosquitto -p 1883:1883 eclipse-mosquitto:2
```

### 2. Créer un fichier `options.json` de test

```json
{
  "mqtt_host": "localhost",
  "mqtt_port": 1883,
  "mqtt_client_id": "nestor-local-test",
  "box_id": "nestor-local-test",
  "auth_mode": "sas",
  "sas_username": "",
  "sas_password": "",
  "cert_path": "/ssl/nestor/device.pem",
  "key_path": "/ssl/nestor/device.key",
  "ca_path": "/ssl/nestor/ca.pem",
  "log_level": "debug",
  "telemetry_filter": {
    "domains": ["light", "switch", "sensor", "climate", "binary_sensor"]
  }
}
```

> **Note** : En local sans TLS, vous devrez temporairement ajuster `MqttBridge.cs` pour désactiver TLS, ou configurer Mosquitto avec des certificats.

### 3. Tester le downlink

```bash
# Publier une commande test
mosquitto_pub -h localhost -t "devices/nestor-local-test/commands/test-cmd-1" -m '{
  "commandId": "test-cmd-1",
  "targetEntityId": "light.salon",
  "action": "turn_on",
  "parameters": {"brightness_pct": 80}
}'

# Écouter l'ack
mosquitto_sub -h localhost -t "devices/nestor-local-test/commands/+/ack"
```

### 4. Observer le heartbeat

```bash
mosquitto_sub -h localhost -t "devices/nestor-local-test/heartbeat"
```

## Installation comme Add-on HA

1. Copier le dossier `nestor-bridge/` dans `/addons/` sur votre instance HAOS
2. Dans HA → Paramètres → Modules complémentaires → Boutique → ⋮ → Vérifier les mises à jour
3. Installer "Nestor Bridge"
4. Configurer les options (mqtt_host, box_id, etc.)
5. Démarrer l'add-on

## Configuration

| Option | Type | Défaut | Description |
|---|---|---|---|
| `mqtt_host` | string | `""` | Hostname Event Grid Namespace |
| `mqtt_port` | int | `8883` | Port MQTT (TLS) |
| `mqtt_client_id` | string | `""` | Client ID MQTT (= box_id en prod) |
| `box_id` | string | `""` | Identifiant unique de la NestorBox |
| `auth_mode` | `sas`/`x509` | `sas` | Mode d'authentification MQTT |
| `sas_username` | string | `""` | Username SAS (mode SAS uniquement) |
| `sas_password` | string | `""` | Password/token SAS |
| `cert_path` | string | `/ssl/nestor/device.pem` | Chemin certificat client X.509 |
| `key_path` | string | `/ssl/nestor/device.key` | Chemin clé privée X.509 |
| `ca_path` | string | `/ssl/nestor/ca.pem` | Chemin CA root |
| `log_level` | enum | `info` | Niveau de log |
| `telemetry_filter.domains` | string[] | `[light, switch, ...]` | Domaines HA à remonter au cloud |

## Structure du projet

```
nestor-bridge/
├── config.yaml                 # Manifest add-on HA
├── build.yaml                  # Hints de build
├── Dockerfile                  # Multi-stage ARM64
├── rootfs/                     # s6-overlay services
│   └── etc/services.d/nestor-bridge/
│       ├── run
│       └── finish
└── src/
    ├── NestorBridge/           # Application principale
    │   ├── Program.cs
    │   ├── Configuration/
    │   ├── Mqtt/
    │   ├── HomeAssistant/
    │   ├── Translation/
    │   └── Services/
    └── NestorBridge.Tests/     # Tests unitaires
```
