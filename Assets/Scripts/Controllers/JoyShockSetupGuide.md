# Guia: DualShock 4 como volante con giroscopio

## Archivos agregados

- `JoyShockLibraryNative.cs`: wrapper C# de JoyShockLibrary.
- `JoyShockGyroSteeringInput.cs`: lee la inclinacion del mando y entrega `Steering` entre `-1` y `1`.

## Instalar JoyShockLibrary

1. Descarga JoyShockLibrary desde:
   `https://github.com/JibbSmart/JoyShockLibrary/releases`
2. Crea esta carpeta en Unity:
   `Assets/Plugins/x86_64`
3. Copia ahi la DLL de 64 bits:
   `JoyShockLibrary.dll`
4. En Unity, selecciona la DLL y revisa en el Inspector:
   - Platform: Windows
   - CPU: x86_64
   - Editor: activo
   - Standalone: activo

## Conectar el mando PS4

### Por USB

1. Usa un cable USB que transmita datos, no solo carga.
2. Conecta el DualShock 4 a la PC.
3. Windows debe detectarlo como `Wireless Controller`.

### Por Bluetooth

1. Mantén presionados `Share + PS` hasta que la luz parpadee rapido.
2. En Windows: `Bluetooth y dispositivos > Agregar dispositivo`.
3. Selecciona `Wireless Controller`.

## Configurar en la escena

1. Crea un GameObject vacio llamado `DS4GyroInput`.
2. Agrega el componente `JoyShockGyroSteeringInput`.
3. Selecciona `KartBody`.
4. En `KartMobileInputBridge`, arrastra `DS4GyroInput` al campo `Joy Shock Steering Input`.
5. En Play Mode, mira el panel `JoyShock / DS4 Gyro`.
6. Mantén el mando en posicion neutral y pulsa `Calibrar mando`.

## Uso

- Inclina el mando como volante.
- Si gira al reves, activa `Invert`.
- Ajusta `Max Tilt Degrees`, `Sensitivity` y `Dead Zone Degrees`.
- Si no detecta el mando, cierra Steam/DS4Windows temporalmente y vuelve a entrar a Play Mode.
