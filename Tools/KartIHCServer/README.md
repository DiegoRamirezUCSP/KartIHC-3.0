# Servidor alternativo Python para Modo Escape

Archivos integrados:

- `Assets/Scripts/Networking/PythonRelayClient.cs`: cliente TCP para Unity; usa JSON por líneas.
- `Tools/KartIHCServer/kartihc_server.py`: servidor Python, sin dependencias externas.

## Ejecución manual de diagnóstico

El conductor normalmente lo inicia automáticamente al decir «activar modo escape local». Para diagnóstico manual, desde la raíz del proyecto:

```powershell
python Tools/KartIHCServer/kartihc_server.py
```

El servidor escucha por defecto en TCP `7777` y hace descubrimiento LAN por UDP `7778`. No lo inicies manualmente mientras el modo local ya está activo, porque el juego habrá ocupado esos puertos.

## Elegir el transporte

Unity Relay sigue disponible con los comandos existentes:

- `activar modo escape`
- `buscar modo escape`

Para jugar en la misma Wi-Fi con el servidor Python:

- Conductor: `activar modo escape local`
- Hacker: `buscar modo escape local`
- Salir: `salir modo escape local`

El conductor inicia el servidor Python local y se conecta como cliente. El hacker descubre ese servidor por broadcast UDP y se conecta como cliente. Ambos equipos deben estar en la misma Wi-Fi. Python debe estar instalado en la PC del conductor y autorizado en redes privadas por Windows Firewall; la PC del hacker no necesita Python. El cliente Python sincroniza las posiciones del kart y del dron, el estado de las puertas, la meta y las mediciones RTT.

La sonda RTT viaja por TCP; mide el tiempo de ida y vuelta de la aplicación. El contador «sin respuesta» indica sondas que superaron el tiempo de espera, no la pérdida bruta de paquetes IP, porque TCP retransmite los paquetes perdidos.

El descubrimiento automático es para la misma red local; no atraviesa Internet ni sustituye Relay para jugar desde redes distintas. El servidor enlaza todas las interfaces (`0.0.0.0`) y no autentica más allá del rol declarado por el cliente. Úsalo en una red de confianza; no lo expongas directamente a Internet sin añadir autenticación y protección apropiadas.
