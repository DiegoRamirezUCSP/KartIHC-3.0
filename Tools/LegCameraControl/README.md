# Control por piernas con cámara

Este módulo usa Python + OpenCV + MediaPipe para detectar si levantas la pierna izquierda o derecha.
Luego envía comandos UDP a Unity:

- `ACELERA`: pierna izquierda arriba.
- `FRENA`: pierna derecha arriba.
- `NADA`: ambas piernas abajo.

Unity recibe esos comandos con `LegMotionInputReceiver.cs` en el puerto `5555`.

## Instalar dependencias

En una terminal:

```powershell
python -m pip install opencv-python mediapipe
```

## Configurar Unity

1. En `SampleScene`, crea un GameObject vacío llamado `LegMotionInput`.
2. Agrega el componente `LegMotionInputReceiver`.
3. Deja el puerto en `5555`.
4. Selecciona `KartBody`.
5. En `KartMobileInputBridge`, arrastra `LegMotionInput` al campo `Leg Motion Input`.

Cuando `LegMotionInputReceiver` esté conectado, controla acelerar/frenar aunque `Auto Accelerate` esté activo.

## Ejecutar

1. En Unity, entra a Play Mode.
2. En terminal, desde la raíz del proyecto:

```powershell
python Tools\LegCameraControl\leg_camera_control.py
```

3. Quédate con ambas piernas abajo durante la calibración.
4. Pierna izquierda arriba = acelera.
5. Pierna derecha arriba = frena.
6. Tecla `r` = recalibrar.
7. Tecla `q` = salir.

## Si no detecta la cámara

Cambia `CAM_INDEX` en `leg_camera_control.py`:

```python
CAM_INDEX = 0
```

Prueba `0`, `1`, `2`, etc.
