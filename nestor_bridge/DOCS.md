# Nestor Bridge — Documentation

## Fonctionnement

Nestor Bridge est un add-on Home Assistant qui agit comme pont bidirectionnel entre le cloud Azure Nestor et le core Home Assistant local. Il supporte deux transports : **MQTT v5** (Azure Event Grid) et **SignalR** (Azure SignalR Service). Les deux peuvent fonctionner ensemble ou indépendamment.

### Transports

| Transport | Downlink (commandes) | Uplink (télémétrie) | Heartbeat |
|---|---|---|---|
| **MQTT** (Event Grid) | `devices/{boxId}/commands/#` | `devices/{boxId}/telemetry/state/{entityId}` | `devices/{boxId}/heartbeat` |
| **SignalR** | `ReceiveFromClient` hub event | `RelayToClients` hub method | `RelayToClients` (heartbeat) |

Au minimum, **un transport doit être configuré** (MQTT ou SignalR). Les deux peuvent être actifs simultanément.

### Downlink (Cloud → HA)

1. L'add-on reçoit les commandes via MQTT (`devices/{boxId}/commands/#`) et/ou SignalR (`ReceiveFromClient`)
2. Chaque commande reçue est désérialisée et traduite en appel `call_service` via le WebSocket HA
3. Un accusé de réception (ack) est publié sur MQTT `devices/{boxId}/commands/{commandId}/ack`

### Uplink (HA → Cloud)

1. L'add-on écoute les événements `state_changed` via le WebSocket HA
2. Les entités sont filtrées par domaine (configurable)
3. Les changements d'état sont publiés via MQTT (`devices/{boxId}/telemetry/state/{entityId}`) et/ou relayés via SignalR (`RelayToClients`)

### Heartbeat

Un message de heartbeat est publié toutes les 60 secondes via MQTT (`devices/{boxId}/heartbeat`) et/ou SignalR.

## Configuration SignalR

Pour connecter l'addon au hub SignalR :

```yaml
signalr_hub_url: "https://your-signalr-instance.azurewebsites.net/hub/devices"
signalr_api_key: "<votre BridgeApiKey>"
```

Le client SignalR effectuera automatiquement la négociation via `POST /hub/devices/negotiate?apiKey=<key>`.

### Mode SignalR seul (sans MQTT)

Il suffit de laisser `mqtt_host` vide et de configurer uniquement `signalr_hub_url` et `signalr_api_key`. Le `box_id` reste obligatoire.

## Résilience

- **Reconnexion automatique** : en cas de perte de connexion MQTT, WebSocket HA ou SignalR, l'add-on tente de se reconnecter avec un backoff exponentiel
- **MQTT** : backoff 1s → 2s → 4s → ... → 60s max
- **SignalR** : reconnexion automatique (0s, 2s, 5s, 15s, 30s, 60s)
- **HA WebSocket** : backoff 1s → 2s → 4s → ... → 60s max
- **Commandes malformées** : un ack `status=error` est renvoyé au cloud avec le détail de l'erreur
- **Erreurs HA** : les erreurs de `call_service` sont capturées et remontées dans l'ack

## Sécurité

- Authentification MQTT via SAS token ou certificat X.509 (mTLS)
- TLS 1.2+ obligatoire pour la connexion Event Grid
- Authentification SignalR via API key (query parameter)
- Le token Supervisor est injecté automatiquement par HAOS (variable `SUPERVISOR_TOKEN`)
- Aucun credential n'est hardcodé — tout passe par `options.json`
