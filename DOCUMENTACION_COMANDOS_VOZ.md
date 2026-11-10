# Comandos activos de Kart IHC

Esta guía refleja las frases registradas por `VoicePowerUpInput.cs` y las acciones conectadas en los controladores actuales. Las frases son valores predeterminados del código; si se modificaron los campos desde el Inspector, Unity puede estar usando esas otras frases.

## Disponibilidad del reconocimiento

- El reconocimiento por voz usa `UnityEngine.Windows.Speech.KeywordRecognizer` y solo se activa en Windows o en el Editor de Unity ejecutado en Windows.
- En otras plataformas no hay actualmente un reconocedor de voz integrado en este proyecto.
- El reconocimiento busca frases completas, no una conversación libre. Debe pronunciarse una de las expresiones listadas.
- Una frase puede estar registrada, pero solo ejecutar su acción en el contexto indicado (por ejemplo, iniciar Carrera requiere estar en el menú).

## Menú principal y selección de modo

### Elegir Carrera

- **«empezar»**, **«iniciar»**, **«comenzar»**, **«iniciar carrera»**, **«empezar carrera»**, **«modo carrera»**: carga la pista normal desde el menú y empieza a jugar.

### Elegir Escape Solo

- **«escape solo»**, **«modo escape solo»**, **«iniciar escape solo»**: carga EscapeCourse como conductor local, crea las patrullas y permite probar la salida. Solo se acepta desde la escena `ModeMenu`.

### Abrir las opciones cooperativas de Escape

- **«escape»**, **«modo escape»**, **«abrir escape»**: abre la selección de opciones del Modo Escape desde el menú principal.

### Entrar a prueba o tutorial Hacker

- **«prueba hacker»**, **«probar hacker»**: carga EscapeCourse en práctica individual para controlar el dron y probar las puertas sin conductor conectado.
- **«tutorial hacker»**, **«tutorial cuatro»**: inicia el tutorial Hacker en EscapeCourse. En el código actual, estas frases pueden iniciar EscapeCourse incluso si se dicen desde otra escena.

### Tutoriales del kart

Estos comandos se aceptan desde el menú inicial:

- **«tutorial uno»**: tutorial de piernas.
- **«tutorial dos»**: tutorial de mando.
- **«tutorial tres»**: tutorial de comandos.

## Conexión cooperativa de Escape

El jugador que conduce crea la sesión. El jugador Hacker busca una sesión ya creada.

### Internet (Unity Relay)

- **Conductor — «activar modo escape»**: crea la sala cooperativa y carga EscapeCourse como conductor.
- **Hacker — «buscar modo escape»**: busca una sala existente durante hasta 15 segundos; no crea una sala ni cambia a conductor si no encuentra host.
- **«salir modo escape»**: cierra la sesión Relay y vuelve al menú de modos.

### Red local (servidor Python y Wi-Fi)

- **Conductor — «activar modo escape local»**, **«activar escape local»**, **«crear escape local»**: inicia el servidor Python local, conecta al conductor y carga EscapeCourse.
- **Hacker — «buscar modo escape local»**, **«buscar escape local»**, **«unirse escape local»**: busca y se une al servidor en la red local.
- **«salir modo escape local»**, **«salir escape local»**: cierra la sesión local.

## Controles del conductor

### Potenciadores

Las frases activan la acción correspondiente si el kart y el receptor de potenciadores están activos:

- **«escudo»**: activa el escudo del kart.
- **«turbo»**, **«nitro»**, **«tubo»**: activa el turbo.
- **«trampa»**: suelta una trampa detrás del kart.

En Escape Solo, el escudo destruye una patrulla al chocar con ella; una trampa del kart también la elimina al tocarla.

### Cámara y control por piernas

- **«activar cámara»**, **«encender la cámara»**: inicia la cámara configurada para captar las piernas.
- **«cambiar cámara»**: cambia a otra cámara conectada, si hay más de una disponible.
- **«calibrar»**: inicia la calibración del control de piernas.

Estas órdenes requieren que `LegMotionInputReceiver` esté activo y que el componente pueda iniciar/comunicar con el proceso Python de cámara.

## Controles del Hacker

No hay frases de voz registradas para mover el dron, cambiar la cámara del dron, hacer zoom o hackear. Esas acciones se realizan con el mando PS4 o con los controles alternativos de teclado y ratón:

- **WASD**: desplazar el dron.
- **Q / E**: descender / ascender.
- **Mantener botón derecho del ratón y moverlo**: mirar y girar.
- **Rueda del ratón** o **+ / −**: zoom.
- **Mantener H**, **mantener □ en el mando** o **mantener clic izquierdo**: iniciar el minijuego de hackeo de la puerta cercana, dentro del alcance.
- **Cruceta o flechas del teclado**: repetir el patrón del minijuego.
- **Options en el mando** o **R en teclado**: recalibrar el giroscopio si hay uno disponible.

El minijuego muestra un patrón aleatorio de seis direcciones. Hay que repetirlo en menos de 10 segundos; si se falla, el dron queda bloqueado durante 15 segundos.

## Pausa, ayuda y navegación

- **«pausa»**, **«pausar»**: pausa la partida mientras está jugando.
- **«reanudar»**, **«continuar»**: reanuda si está pausada.
- **«controles»**, **«ayuda»**: muestra u oculta el panel de controles durante la pausa.
- **«reiniciar»**, **«reinicio»**: devuelve el estado de juego al menú inicial mientras se está jugando o pausado; no reinicia instantáneamente la pista actual.
- **«cancelar»**, **«salir»**: sale del tutorial activo. En el tutorial Hacker vuelve a `ModeMenu`.
- **«volver menú»**, **«regresar menú»**: vuelve atrás en el selector de modos o cierra el modo activo según la escena actual.

## Teclas de respaldo generales

Además de la voz, `VoicePowerUpInput` escucha estas teclas de respaldo:

- **Enter**: solicitar inicio de Carrera.
- **1 / 2 / 3**: iniciar los tutoriales uno, dos o tres.
- **Z / X / C**: escudo / turbo / trampa.
- **P / O**: pausar / reanudar.
- **I**: mostrar u ocultar controles durante la pausa.
- **Backspace**: volver al estado de menú inicial durante juego o pausa.
- **Escape**: cancelar el tutorial.

Estas teclas generales no incluyen una tecla para crear o buscar partidas, iniciar Escape Solo ni controlar el dron; los botones del menú y las teclas específicas del dron cubren esas acciones.
