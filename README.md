# Runner Game Online

Runner Game Online es un runner 3D multijugador hecho en Unity, orientado a partidas privadas de 2 jugadores mediante codigo de sala. El flujo soportado actualmente esta centrado en el modo online sobre Photon Fusion y en la publicacion WebGL.

## Estado actual del proyecto

- `Assets/Scenes/Bootstrap.unity` es la entrada oficial del juego.
- `Assets/Scenes/Joc.unity` es la escena de carrera, pero no esta pensada para abrirse como standalone. Si se carga sin una sesion Fusion activa, el runtime redirige de vuelta a `Bootstrap`.
- El runtime local legacy sigue presente en el repositorio y se reutiliza como base de escena, paths, obstaculos y visuales, pero no es el flujo oficialmente soportado hoy.

## Arquitectura tecnica

### Flujo de sesion

- `SessionBootstrapper` crea o une partidas privadas, mantiene vivo el `NetworkRunner`, controla el estado de la sesion y gestiona la transicion entre `Bootstrap` y `Joc`.
- `SessionRuntime` conserva la sesion activa, el codigo de la sala y el ultimo `ShutdownReason` reportado por Fusion.

### Inicializacion de escena online

- `OnlineSceneRuntime` registra hooks de carga de escena, valida que exista una sesion activa y asegura que `Joc` solo se use dentro del flujo online.
- `LegacySceneAdapter` prepara la escena para multiplayer, desactiva el runtime local legacy y reutiliza recursos existentes como paths, camaras, iluminacion y prototipos visuales.

### Logica de carrera

- `NetworkRaceManager` sincroniza el estado de rondas, el ganador de cada tramo, el avance entre niveles y el retorno al menu al terminar la partida.
- `RunnerNetworkPlayer` representa a cada jugador en red, resuelve spawn, slot visual, colisiones, animacion, respawn y reporte de meta.
- `RunnerMotor` mueve al jugador sobre los recorridos, resuelve soporte contra el suelo y controla caida, escalada y reposicionamiento.
- `RunnerInputAdapter` captura la entrada local y la serializa como `RunnerInputState` para Fusion.

## Gameplay tecnico actual

- Limite de partida: `2` jugadores.
- Modo de red: `GameMode.Shared`.
- Slots replicados: `Red` y `Blue`.
- Controles online:
  - teclado: mantener `W`, flecha arriba o `Space`
  - gamepad: boton sur o stick izquierdo hacia arriba
- `Esc` abre una pausa local desde el HUD, pero no pausa la simulacion de red.
- La carrera recorre `5` niveles secuenciales con estas distancias de meta:
  - nivel 1: `480`
  - nivel 2: `309`
  - nivel 3: `372`
  - nivel 4: `387`
  - nivel 5: `632`
- El nivel `4` incluye un tramo de escalada que inicia en la distancia `286`.
- Tiempo de respawn tras caida: `2s`.
- Tiempo de espera para avanzar al siguiente round: `3s`.
- Tiempo de espera para volver al menu al finalizar la partida: `4s`.
- `ObstacleManager` activa obstaculos por nivel y define respuestas online para hazards y colisiones.

## Stack y requisitos

- Unity: `6000.4.0f1`
- Render pipeline: `com.unity.render-pipelines.universal` `17.4.0`
- Input System: `com.unity.inputsystem` `1.19.0`
- Cinemachine: `com.unity.cinemachine` `2.10.6`
- Networking: Photon Fusion 2 importado en `Assets/Photon/Fusion`
- Escenas requeridas en build:
  - `Assets/Scenes/Bootstrap.unity`
  - `Assets/Scenes/Joc.unity`
- Prefabs runtime en `Assets/Resources`:
  - `RunnerNetworkPlayer.prefab`
  - `NetworkRaceManager.prefab`

## Build y publicacion

- El proyecto mantiene dos perfiles WebGL rastreados: `Web-Test` y `Web-Release`.
- La guia detallada de build, validacion y publicacion en Unity Play vive en [Docs/WebGL-UnityPlay.md](C:/Runner-Game-3D-Online/Docs/WebGL-UnityPlay.md).
- Cuando sea necesario regenerar assets online base, se puede usar el menu `Codex/Build Online Multiplayer Assets`.

## Limitaciones actuales

- El modo oficial documentado es unicamente multiplayer online privado para `2` jugadores.
- `RedPlayerMovement` y `BluePlayerMovement` siguen en el proyecto como base legacy reutilizada, no como punto de entrada principal del juego soportado hoy.
- La configuracion sensible de Photon no debe duplicarse en el README; solo debe referenciarse su ubicacion en recursos y settings del proyecto.
