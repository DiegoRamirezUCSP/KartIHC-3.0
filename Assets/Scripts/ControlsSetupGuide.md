# Guia rapida: volante con giroscopio y power-ups por voz

## 1. Objeto de entrada

1. Crea un GameObject vacio llamado `InputController`.
2. Agrega `GyroSteeringInput`.
3. Agrega `VoicePowerUpInput`.
4. Agrega `MobileInputDiagnostics` para ver si el celular y microfono responden.
5. En `MobileInputDiagnostics`, arrastra el mismo `InputController` a `Steering Input` y `Voice Input`.

## 2. Integracion con el camion de este proyecto

La escena `SampleScene` ya usa `KartBody` con `KartController`, asi que no agregues
`SimpleVehicleController` al camion principal. En su lugar:

1. Selecciona `KartBody`.
2. Agrega `KartMobileInputBridge`.
3. Crea un GameObject vacio llamado `InputController`.
4. Agrega `GyroSteeringInput`.
5. Agrega `VoicePowerUpInput`.
6. Opcional: agrega `MobileInputDiagnostics` al mismo `InputController`.
7. En `KartMobileInputBridge`, arrastra:
   - `KartBody` al campo `Kart Controller`.
   - `InputController` al campo `Gyro Steering Input`.
   - `InputController` al campo `Voice Input`.
8. Deja `Auto Accelerate` activo si quieres que el camion avance solo al iniciar la carrera. Apagalo si quieres acelerar con `W`/flecha arriba y frenar con `S`/flecha abajo.

`GameStateManager` sigue controlando cuando el camion acepta input: en menu no se mueve,
y al entrar a `Playing` el puente empieza a alimentar `SetSteer`, `SetAccelerate` y
`SetBrake` del `KartController`.

## 3. Vehiculo de prueba alternativo

1. Crea un cubo o modelo simple para el vehiculo.
2. Agrega un `Rigidbody`.
3. Agrega `SimpleVehicleController`.
4. Arrastra el `InputController` al campo `Steering Input`.
5. Arrastra el `InputController` al campo `Voice Input`.

Usa este camino solo para probar sensores en una escena vacia; no lo mezcles con
`KartController` en el camion principal.

## 4. Pruebas rapidas

- En Editor puedes girar con `A/D` o flechas.
- En Editor/Windows puedes decir `empezar` o `iniciar` para comenzar la carrera.
- Tambien puedes presionar `Enter` para comenzar sin microfono.
- En Editor/Windows puedes decir `escudo`, `turbo` o `trampa`.
- Tambien puedes probar power-ups con `Z`, `X` y `C`.
- `Z` activa `escudo`, `X` activa `turbo` y `C` activa `trampa`.
- En telefono, el volante usa el giroscopio. Ajusta `Max Tilt Degrees`, `Sensitivity` e `Invert` segun como sostengas el celular.
- En telefono, el panel de `MobileInputDiagnostics` debe mostrar `Giroscopio activo: True`.
- Si el microfono funciona, `Microfono` debe pasar a `activo` y `Entrada de audio` debe subir cuando hablas.

## 5. Importante sobre voz en celular

Unity no trae un reconocedor de voz movil universal listo para Android/iOS. Para el prototipo puedes probar voz en Windows con `KeywordRecognizer`; para Android/iOS conviene conectar un plugin de reconocimiento de voz o un puente nativo.

El panel de diagnostico comprueba permiso y nivel de audio del microfono, pero no convierte voz a texto en Android/iOS. Eso confirma que hay microfono; el reconocimiento de palabras necesita el plugin o puente nativo.

## 6. Celular como control remoto

Si quieres que el juego corra en la PC y el celular sea solo volante:

1. En la escena de la PC, crea un GameObject llamado `PhoneReceiver`.
2. Agrega `DesktopPhoneControlReceiver`.
3. Deja el puerto en `47777`. No uses `9000`: ese puerto ya lo usa `NetworkReceiver` para comandos JSON.
4. En `KartMobileInputBridge`, arrastra `PhoneReceiver` al campo `Phone Receiver`.
5. Crea otra escena para Android, por ejemplo `PhoneControllerScene`.
6. En esa escena crea un GameObject llamado `PhoneController`.
7. Agrega `PhoneControllerSender`.
8. En el campo `Computer Ip`, escribe la IP local de tu PC.
9. Compila esa escena al OPPO con `Build And Run`.
10. Abre la escena del juego en la PC y presiona `Play`.

La PC y el celular deben estar en la misma red Wi-Fi. Si no conecta, revisa el firewall de Windows y permite Unity en redes privadas.

Si usas datos moviles, no basta con que el celular tenga internet: la PC debe estar
conectada al hotspot del celular o al anclaje USB. En algunos hotspots el boton
`Buscar PC automaticamente` no funciona porque bloquean broadcast. En ese caso:

1. Mira en la escena de PC el panel `Receptor del celular`.
2. Copia una de las `IPs de esta PC`.
3. Escribe esa IP manualmente en `PhoneControllerSender`, en el campo `IP de la PC`.
4. Mantén el puerto `47777` en ambos lados. Si cambias el puerto, debe ser el mismo en `DesktopPhoneControlReceiver` y `PhoneControllerSender`, y no debe estar ocupado por otro componente.

## 7. Modo por cable con ADB

Si Wi-Fi, hotspot o anclaje USB no detectan la red, usa el modo `USB ADB`:

1. Activa `Opciones de desarrollador` y `Depuracion USB` en el celular.
2. Conecta el celular por cable y acepta la autorizacion RSA si Android la muestra.
3. En la PC ejecuta:

   `adb reverse tcp:47777 tcp:47777`

   Si usas el adb incluido con Unity, la ruta suele ser:

   `C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe reverse tcp:47777 tcp:47777`

4. En la escena de PC, deja `DesktopPhoneControlReceiver` escuchando en `47777`.
5. En la app del celular, toca `USB ADB`.
6. El celular se conectara a `127.0.0.1:47777`, y ADB lo redirigira a la PC.

En este modo no necesitas que el celular y la PC esten en la misma red Wi-Fi.
