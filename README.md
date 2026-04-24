# Runner Game 3D Online

Juego de carreras 3D multijugador en línea donde dos jugadores compiten en una pista con obstáculos hasta llegar primero a la meta.

## Descripción

Runner Game 3D Online es un proyecto hecho en Unity que adapta una experiencia de carrera tipo runner a partidas privadas online. Un jugador crea una sala, comparte el código y otro jugador se une para competir en tiempo real.

El juego usa Photon Fusion para sincronizar la sesión, los jugadores, el avance de carrera, los obstáculos y los cambios de ronda.

## Características

- Partidas privadas para 2 jugadores mediante código de sala.
- Modo online con Photon Fusion en `GameMode.Shared`.
- Escena de inicio para crear o unirse a una partida.
- Escena de carrera con jugadores rojo y azul.
- Progresión por rondas y detección de ganador.
- Obstáculos, física de impacto y reinicio de ronda.
- Soporte para teclado y gamepad.
- Perfiles de build WebGL para pruebas y publicación.

## Controles

- Avanzar: `W`, flecha arriba o `Espacio`.
- Gamepad: botón inferior o stick izquierdo hacia arriba.

## Requisitos

- Unity `6000.4.0f1`.
- Photon Fusion importado y configurado con un App ID válido.
- Las escenas deben estar incluidas en Build Settings en este orden:
  1. `Assets/Scenes/Bootstrap.unity`
  2. `Assets/Scenes/Joc.unity`

## Cómo ejecutar el proyecto

1. Abrir el proyecto desde Unity Hub.
2. Verificar que Photon Fusion tenga un App ID configurado.
3. Abrir la escena `Assets/Scenes/Bootstrap.unity`.
4. Entrar en Play Mode.
5. Crear una partida con `Host Private Match`.
6. Copiar el código de sala.
7. Abrir una segunda instancia, navegador o perfil de prueba.
8. Usar `Join Match` con el código generado.

Cuando ambos jugadores estén conectados, la partida carga la escena `Joc` y comienza la carrera.

## Estructura principal

- `Assets/Scenes/Bootstrap.unity`: menú y arranque de sesión online.
- `Assets/Scenes/Joc.unity`: escena principal de carrera.
- `Assets/Scripts/Online/`: scripts de red, sesión, jugadores y rondas.
- `Assets/Resources/RunnerNetworkPlayer.prefab`: prefab del jugador online.
- `Assets/Resources/NetworkRaceManager.prefab`: administrador de carrera en red.
- `Docs/WebGL-UnityPlay.md`: notas de build WebGL y publicación en Unity Play.

## Build WebGL

El proyecto incluye perfiles de build para WebGL:

- `Assets/Settings/BuildProfiles/Web-Test.asset`
- `Assets/Settings/BuildProfiles/Web-Release.asset`

Desde Unity se pueden usar las opciones:

- `Tools > Codex > WebGL > Activate Web-Test Profile`
- `Tools > Codex > WebGL > Activate Web-Release Profile`
- `Tools > Codex > WebGL > Build Web-Test`
- `Tools > Codex > WebGL > Build Web-Release`

Para publicar en Unity Play, se recomienda usar el build `Web-Release` y ejecutar la versión subida desde una URL HTTPS.

## Notas

- El host usa el jugador azul y el invitado usa el jugador rojo.
- El flujo online está diseñado para partidas privadas de máximo 2 jugadores.
- Para pruebas locales con varias instancias, revisar la configuración de Photon Fusion Multi-Peer.
