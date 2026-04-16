# Spec technique — Custom Add-on Home Assistant `nestor-bridge`

> **Pour l'agent IDE** : ce document est le contexte complet du composant à développer. Lis-le intégralement avant de générer du code. En cas d'ambiguïté, privilégie ce qui est écrit ici plutôt que les conventions génériques HA/MQTT.

---

## 1. Vue d'ensemble du projet Nestor

Trecobat (constructeur de maisons individuelles, ~1500 maisons/an) équipe chaque maison d'une **NestorBox** — une passerelle domotique basée sur un Raspberry Pi Compute Module 4 qui exécute :

- **Home Assistant OS** (HAOS) en tant que socle
- **Agent Nestor** (service Go maison, provisioning + monitoring)
- **Zigbee2MQTT** + **Mosquitto** local (radio Zigbee/Thread/Matter via chip Silicon Labs ZBT-2)
- **Custom Add-ons HA maison** — dont celui que nous développons ici

Le cloud tourne sur **Azure** : backend .NET (ASP.NET Core), Event Grid Namespace en mode **broker MQTT v5**, SignalR pour le temps réel vers l'app mobile, Cosmos DB pour le digital twin.

## 2. Rôle de l'add-on `nestor-bridge`

L'add-on est le **pont protocolaire bidirectionnel** entre le cloud Azure et le core Home Assistant local. Il remplit deux fonctions :

### 2.1 Downlink (cloud → HA)
1. S'abonne en **MQTT v5** à Event Grid Namespace (topics `devices/{boxId}/commands/#`)
2. Reçoit les commandes métier (ex: `{"action":"turn_on","entity_id":"light.salon","brightness":200}`)
3. Les traduit en appels `call_service` via le **WebSocket API de Home Assistant**
4. Publie un ack de résultat sur `devices/{boxId}/commands/{commandId}/ack`

### 2.2 Uplink (HA → cloud)
1. S'abonne aux events HA (`state_changed`, `zha_event`, etc.) via WebSocket
2. Filtre / transforme selon une policy (toutes les entités ne remontent pas)
3. Publie la télémétrie en MQTT v5 sur `devices/{boxId}/telemetry/{entityId}`

## 3. Stack technique imposée

| Composant | Choix | Raison |
|---|---|---|
| Langage | **C# / .NET 8** | Alignement avec le backend Azure (`apinestor-poc`) et la compétence équipe |
| Runtime add-on | **Container Linux ARM64** (RPi CM4) | HAOS tourne les add-ons en conteneurs |
| Client MQTT | **MQTTnet** (v4.x) | Support natif MQTT v5, mature, maintenu |
| Client HA WebSocket | **Custom** (via `System.Net.WebSockets.ClientWebSocket`) | Pas de lib .NET officielle pour HA, le protocole est simple |
| Sérialisation | **System.Text.Json** | Perf + natif .NET 8 |
| Logging | **Microsoft.Extensions.Logging** + console structurée JSON | Ingestion facile par Supervisor / Azure |
| Config | **IOptions<T>** + binding depuis `options.json` (injecté par Supervisor) | Pattern HA add-on standard |

**Interdits explicites** : pas de `Newtonsoft.Json`, pas de Paho, pas de blocking calls (`.Result`, `.Wait()`), pas de `Thread.Sleep` dans les boucles — tout doit être `async/await` idiomatique.

## 4. Architecture d'un Home Assistant Add-on

Un add-on HA est un **container Docker** orchestré par le Supervisor. Structure minimale :

```
nestor-bridge/
├── Dockerfile              # Multi-stage, base image arm64v8
├── config.yaml             # Manifest de l'add-on (lu par Supervisor)
├── build.yaml              # Hints de build multi-arch
├── icon.png / logo.png     # 128x128 / 250x100
├── README.md
├── DOCS.md                 # Doc montrée dans l'UI HA
└── rootfs/                 # Overlay du container (s6-overlay)
    └── etc/services.d/nestor-bridge/
        ├── run             # Script de lancement (appelle dotnet NestorBridge.dll)
        └── finish
```

### Points critiques du `config.yaml`

```yaml
name: "Nestor Bridge"
version: "0.1.0"
slug: "nestor_bridge"
description: "Bridge MQTT v5 <-> Home Assistant WebSocket for Nestor cloud"
arch:
  - aarch64
startup: services
boot: auto
hassio_api: true          # accès à l'API Supervisor
homeassistant_api: true   # accès à l'API REST HA (optionnel mais utile)
auth_api: true
host_network: false
options:
  mqtt_host: ""
  mqtt_port: 8883
  mqtt_client_id: ""
  box_id: ""
  cert_path: "/ssl/nestor/device.pem"
  key_path: "/ssl/nestor/device.key"
  ca_path: "/ssl/nestor/ca.pem"
  log_level: "info"
  telemetry_filter:
    domains: ["light", "switch", "sensor", "climate", "binary_sensor"]
schema:
  mqtt_host: str
  mqtt_port: port
  mqtt_client_id: str
  box_id: str
  cert_path: str
  key_path: str
  ca_path: str
  log_level: list(trace|debug|info|warning|error)
  telemetry_filter:
    domains: ["str"]
map:
  - ssl:ro                 # accès aux certs montés
```

### Variables d'env injectées automatiquement par Supervisor

- **`SUPERVISOR_TOKEN`** : JWT long-lived pour appeler l'API Supervisor et HA
- **`HASSIO_TOKEN`** : alias historique du précédent
- Les champs de `options` sont disponibles via le fichier `/data/options.json`

## 5. Protocole Home Assistant WebSocket

**Endpoint interne** (depuis un add-on avec `homeassistant_api: true`) :
```
ws://supervisor/core/websocket
```

**Séquence de handshake** (obligatoire, dans cet ordre) :

```
← {"type":"auth_required","ha_version":"2026.x.x"}
→ {"type":"auth","access_token":"<SUPERVISOR_TOKEN>"}
← {"type":"auth_ok","ha_version":"2026.x.x"}
```

Si `auth_invalid`, le socket est fermé par HA.

**Après auth, chaque message a un `id` incrémental côté client**. Exemples utiles :

### S'abonner aux state_changed
```json
{"id": 1, "type": "subscribe_events", "event_type": "state_changed"}
```
→ réponse `{"id":1,"type":"result","success":true}`
→ puis flux `{"id":1,"type":"event","event":{...}}`

### Appeler un service
```json
{
  "id": 42,
  "type": "call_service",
  "domain": "light",
  "service": "turn_on",
  "service_data": {"brightness": 200},
  "target": {"entity_id": "light.salon"}
}
```

### Récupérer les states
```json
{"id": 2, "type": "get_states"}
```

**Points d'attention** :
- Le WebSocket peut se couper (restart HA, update). Prévoir **reconnexion exponentielle backoff** avec re-souscription automatique.
- Les `id` doivent être strictement croissants sur une même connexion.
- HA ferme la connexion si on dépasse ~30s sans pong — gérer le ping/pong au niveau WebSocket.

## 6. Protocole Event Grid Namespace (MQTT v5)

Event Grid impose **MQTT v5 uniquement**, TLS 1.2+, port **8883**.

### Auth : deux modes

**Mode POC (actuel)** : SAS token en username/password MQTT
**Mode cible (ZTP)** : certificat client X.509 provisionné par DPS (chemin `/ssl/nestor/device.{pem,key}`)

Le code doit gérer **les deux** via un flag de config (`auth_mode: "sas" | "x509"`).

### Topic Spaces

Côté Azure, un admin configure des **Topic Spaces** avec des permissions. Côté client, on souscrit avec des patterns autorisés. Convention Nestor :

| Usage | Topic | Sens | QoS |
|---|---|---|---|
| Commandes descendantes | `devices/{boxId}/commands/#` | subscribe | 1 |
| Ack de commande | `devices/{boxId}/commands/{cmdId}/ack` | publish | 1 |
| Télémétrie state | `devices/{boxId}/telemetry/state/{entityId}` | publish | 0 |
| Événements discrets | `devices/{boxId}/telemetry/event/{eventType}` | publish | 1 |
| Heartbeat box | `devices/{boxId}/heartbeat` | publish | 0 (retain=false, toutes les 60s) |

**Client ID MQTT** = `box_id` (doit matcher l'identité du certificat en mode X.509).

### MQTTnet v4 — points clés

```csharp
var factory = new MqttFactory();
var client = factory.CreateMqttClient();

var tlsOptions = new MqttClientTlsOptionsBuilder()
    .UseTls()
    .WithClientCertificates(new[] { cert })  // X509Certificate2
    .WithCertificateValidationHandler(ctx => ctx.SslPolicyErrors == SslPolicyErrors.None)
    .Build();

var options = new MqttClientOptionsBuilder()
    .WithProtocolVersion(MqttProtocolVersion.V500)
    .WithClientId(boxId)
    .WithTcpServer(mqttHost, 8883)
    .WithTlsOptions(tlsOptions)
    .WithCleanSession(false)
    .WithSessionExpiryInterval(3600)
    .Build();
```

**⚠️ Piège connu** : Event Grid exige que le `ClientId` MQTT soit **identique** au nom de l'authentication identity configuré dans le namespace. Toute divergence = disconnect immédiat avec reason code `0x87` (Not Authorized).

## 7. Structure du code .NET attendue

```
src/
├── NestorBridge/
│   ├── Program.cs                          # Generic Host + DI
│   ├── Configuration/
│   │   ├── BridgeOptions.cs                # Bind options.json
│   │   └── OptionsJsonLoader.cs
│   ├── Mqtt/
│   │   ├── IMqttBridge.cs
│   │   ├── MqttBridge.cs                   # Wrapper MQTTnet, subscribe/publish
│   │   └── Topics.cs                       # Helpers de construction de topics
│   ├── HomeAssistant/
│   │   ├── IHaWebSocketClient.cs
│   │   ├── HaWebSocketClient.cs            # Handshake + message loop + reconnect
│   │   ├── Models/                         # DTOs state_changed, call_service, etc.
│   │   └── HaServiceCaller.cs              # API haut niveau (TurnOn, SetTemp, ...)
│   ├── Translation/
│   │   ├── CommandTranslator.cs            # MQTT payload -> HA call_service
│   │   └── TelemetryTranslator.cs          # HA event -> MQTT payload
│   ├── Services/
│   │   ├── DownlinkWorker.cs               # IHostedService: MQTT->HA
│   │   ├── UplinkWorker.cs                 # IHostedService: HA->MQTT
│   │   └── HeartbeatWorker.cs              # IHostedService: heartbeat périodique
│   └── NestorBridge.csproj
└── NestorBridge.Tests/
    └── ...
```

### Program.cs (squelette attendu)

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Config depuis /data/options.json (injecté par Supervisor)
builder.Configuration.AddJsonFile("/data/options.json", optional: false, reloadOnChange: false);
builder.Services.Configure<BridgeOptions>(builder.Configuration);

// Logging JSON structuré
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

// Clients singletons
builder.Services.AddSingleton<IMqttBridge, MqttBridge>();
builder.Services.AddSingleton<IHaWebSocketClient, HaWebSocketClient>();
builder.Services.AddSingleton<CommandTranslator>();
builder.Services.AddSingleton<TelemetryTranslator>();

// Hosted services
builder.Services.AddHostedService<DownlinkWorker>();
builder.Services.AddHostedService<UplinkWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();

var app = builder.Build();
await app.RunAsync();
```

## 8. Payloads de référence

### Commande descendante (cloud → box)
```json
{
  "commandId": "c7f3-...",
  "issuedAt": "2026-04-16T10:12:34Z",
  "targetEntityId": "light.salon",
  "action": "turn_on",
  "parameters": {
    "brightness_pct": 80,
    "color_temp": 300
  }
}
```
→ doit devenir un `call_service` HA avec `domain="light"`, `service="turn_on"`, `target.entity_id="light.salon"`, `service_data` issu du mapping.

### Ack (box → cloud)
```json
{
  "commandId": "c7f3-...",
  "status": "success" | "error",
  "error": null,
  "completedAt": "2026-04-16T10:12:35Z",
  "haResultContextId": "01HW..."
}
```

### Télémétrie state (box → cloud)
```json
{
  "entityId": "sensor.salon_temperature",
  "state": "21.4",
  "attributes": {
    "unit_of_measurement": "°C",
    "device_class": "temperature"
  },
  "lastChanged": "2026-04-16T10:10:02Z",
  "boxId": "nestor-0a1b2c3d"
}
```

## 9. Gestion des erreurs & résilience

| Scénario | Comportement attendu |
|---|---|
| WebSocket HA déconnecté | Reconnexion exponentielle (1s, 2s, 4s... cap 60s) + re-subscribe |
| MQTT déconnecté | Idem, + buffer local des derniers N messages de télémétrie (file bornée in-memory, pas de persistance POC) |
| Commande MQTT malformée | Ack `status=error` avec détail, log warning, pas de crash |
| `call_service` HA échoue | Ack `status=error` avec l'erreur HA, log error |
| `SUPERVISOR_TOKEN` manquant | Fail fast au boot avec log explicite |
| Certif X.509 expiré | Fail fast, ne pas tenter de connexion en boucle |

## 10. Livrables attendus de ce premier sprint

1. **Solution .NET 8** compilable en ARM64 (`dotnet publish -r linux-arm64 -c Release`)
2. **Dockerfile multi-stage** (SDK pour build, `mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim-arm64v8` pour runtime)
3. **`config.yaml`** HA add-on valide
4. **Handshake WS HA fonctionnel** + souscription `state_changed` + log des events reçus
5. **Connexion MQTT v5** à Event Grid en mode SAS (X.509 en phase 2)
6. **Chemin downlink complet** : `call_service` `light.turn_on` déclenché par un message MQTT sur `devices/{boxId}/commands/test`
7. **Heartbeat** publié toutes les 60s
8. **README.md** avec instructions de test local (mosquitto local en stub d'Event Grid)

Le reste (translator complet, filtres, uplink complet, X.509) viendra dans les sprints suivants.

## 11. Ce qu'il ne faut **pas** faire

- ❌ Ne pas recréer un broker MQTT embarqué dans l'add-on — on **utilise** Mosquitto local et Event Grid distant, on ne remplace pas
- ❌ Ne pas écrire de logique métier ZCL dans l'add-on pour le POC — le add-on est un pipe protocolaire, la normalisation ZCL (OWON vs Wiser) viendra plus tard dans un module dédié
- ❌ Ne pas hardcoder d'URL ou de creds — tout passe par `options.json`
- ❌ Ne pas faire de long-polling sur l'API REST HA — c'est le WebSocket qui est la source de vérité
- ❌ Ne pas bufferiser de télémétrie sur disque pour ce sprint (viendra avec la stratégie store-and-forward)

## 12. Ressources utiles

- HA WebSocket API : https://developers.home-assistant.io/docs/api/websocket
- HA Add-on config : https://developers.home-assistant.io/docs/add-ons/configuration
- MQTTnet docs : https://github.com/dotnet/MQTTnet/wiki
- Event Grid MQTT : https://learn.microsoft.com/azure/event-grid/mqtt-overview
- Base images .NET ARM64 : https://mcr.microsoft.com/product/dotnet/runtime/tags

---

**Si un point reste ambigu au moment de coder, générer un `TODO(ben):` inline plutôt que d'inventer une convention.**
