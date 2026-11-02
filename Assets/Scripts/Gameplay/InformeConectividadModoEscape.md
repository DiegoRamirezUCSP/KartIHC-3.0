# Informe breve de conectividad — Modo Escape

**Fecha:** 30 de octubre de 2026  
**Estado:** conexión entre dos computadoras implementada; la sesión de dos PC ya fue probada manualmente según el registro del proyecto.

## Arquitectura

El Modo Escape usa el modelo **host y cliente**. El conductor crea una sala pública y es el host autoritativo de la partida; el hacker se une como cliente. La sala admite hasta dos participantes y se filtra por tipo de sesión `kartihc.escape.v1` y propiedad `mode=escape`, para que la búsqueda encuentre una sala compatible.

Unity Services gestiona autenticación anónima y sesiones/matchmaking. La sesión se configura con Relay (`WithRelayNetwork()`), que permite conectar las PC por Internet sin que el jugador configure manualmente puertos del router. Netcode for GameObjects transporta los mensajes del juego usando Unity Transport.

## Flujo de conexión

1. El conductor dice **«activar modo escape»**. El cliente inicializa Unity Services, inicia sesión anónimamente y crea la sala.
2. El hacker dice **«buscar modo escape»**. Quick Join busca una sala Escape pública con espacio disponible; reintenta durante un máximo de 15 segundos.
3. Si encuentra sala, ambos cargan `EscapeCourse`. Si no, la búsqueda termina con **«No se encontró ningún conductor»**; el hacker no crea una sala ni pasa a ser conductor.
4. Si el hacker se desconecta, intenta reconectarse a la sesión anterior y luego vuelve a buscar conductor si esa sesión ya no está disponible.

## Datos intercambiados

| Dato | Dirección | Transporte |
|---|---|---|
| Posición, rotación y velocidad del kart | Host → hacker, hasta 20 veces por segundo | No confiable, secuenciado (prioriza el estado reciente) |
| Solicitud de abrir/cerrar puerta | Hacker → host | Confiable, secuenciado |
| Estado de puertas | Host → participantes | Confiable, secuenciado |
| Victoria al llegar a meta | Host → participantes | Confiable, secuenciado |
| Sincronización inicial de kart y puertas | Cliente → host (solicitud) y host → cliente (estado) | Mensajes personalizados de Netcode |

La selección y la vista de las cámaras del hacker son locales; **no se envía video por la red**. Las cámaras son puntos de vista de la pista dentro del juego. El mando PS4 y su giroscopio controlan esa vista en el cliente hacker.

## Paquetes usados

Versiones declaradas en `Packages/manifest.json` y `Packages/packages-lock.json`:

- **Unity Services Multiplayer 2.4.1** — sesiones, matchmaking y Relay.
- **Netcode for GameObjects 2.7.0** — mensajes y sincronización del juego.
- **Unity Transport 2.7.4** — transporte que utiliza Netcode.
- **Authentication 3.8.0** — dependencia de Multiplayer; el código inicia sesión anónimamente.

No hace falta instalar paquetes adicionales para el flujo actual. Unity Services debe estar vinculado al proyecto correcto y habilitado desde Unity Dashboard; ambas compilaciones deben usar el mismo proyecto de servicios y una versión compatible del juego.

Para leer el giroscopio del DualShock 4, el proyecto también incluye `Assets/Plugins/x86_64/JoyShockLibrary.dll`. Es un requisito local del mando, no un paquete de red.

## Límites conocidos

- El conductor es el host y mantiene la autoridad de la partida; no hay migración automática de host. Si se cierra el juego del conductor, la sesión y la partida pueden dejar de estar disponibles.
- La reconexión implementada corresponde al hacker mientras la sesión del conductor siga disponible. No garantiza restaurar una partida después de que el host haya terminado.
- El matchmaking sólo busca salas públicas compatibles; el nombre de sala no es un código que el usuario tenga que introducir.
- La victoria conjunta depende del mensaje de meta del host.

## Archivos principales

- `Assets/Scripts/Gameplay/EscapeModeSessionController.cs` — inicialización, autenticación, crear/buscar sala, Relay, reconexión y carga de escena.
- `Assets/Scripts/Gameplay/EscapeModeNetworkBridge.cs` — mensajes de kart, puertas, cámaras locales y meta.
- `Packages/manifest.json` — dependencias directas del proyecto.
