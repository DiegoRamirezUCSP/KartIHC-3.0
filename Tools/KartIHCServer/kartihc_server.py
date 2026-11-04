#!/usr/bin/env python3
"""
KartIHC - servidor de sesion para el modo cooperativo conductor + hacker.

* Solo libreria estandar (Python 3.8+). Corre igual en Arch Linux y Windows.
* Transporte: TCP con mensajes JSON, uno por linea (terminados en "\n").
  En una LAN con router local el retraso extra frente a UDP es minimo y TCP
  garantiza que los eventos (hackeo, choque con cable, poderes) nunca se pierdan.
* El servidor recibe el estado de cada jugador, aplica las reglas compartidas
  y difunde una foto del mundo ("snap") a TICK_RATE veces por segundo.

Reglas que controla el servidor
  - Conductor: item que tiene (escudo/turbo/trampa), turbo 1.5 s, escudo 10 s,
    trampas soltadas, aturdimiento de 1 s al pisar una trampa (el escudo protege).
  - Hacker: dron quieto al chocar con un cable, minijuego con limite de tiempo,
    bloqueo de 10 s sin poder hackear tras equivocarse, obstaculos hackeados.
  - Fase de la partida (waiting / playing / finished) y victoria compartida.

Uso:  python kartihc_server.py [--port 7777] [--tick-rate 30] ...
"""
import argparse
import asyncio
import json
import logging
import math
import random
import socket
import time
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

PROTOCOL_VERSION = 1
ITEMS = ("escudo", "turbo", "trampa")
ROLES = ("driver", "hacker")
MAX_LINE = 8192
log = logging.getLogger("kartihc")


# --------------------------------------------------------------------------- #
# Configuracion
# --------------------------------------------------------------------------- #
@dataclass
class Config:
    tick_rate: int = 30
    obstacles: int = 3            # cantidad de obstaculos hackeables (ids 0..n-1)
    hack_time: float = 8.0        # segundos para resolver el minijuego [por definir]
    lock_time: float = 10.0       # bloqueo tras equivocarse (definido por el equipo)
    timeout_is_fail: bool = True  # si se acaba el tiempo cuenta como error
    cable_freeze: float = 3.0     # dron quieto tras chocar con un cable [por definir]
    hack_duration: float = 0.0    # 0 = el obstaculo queda hackeado para siempre
    turbo_time: float = 1.5       # igual que KartController.turboDuration
    shield_time: float = 10.0     # igual que KartController.shieldDuration
    trap_stun: float = 1.0        # igual que KartController.trapStunDuration
    max_traps: int = 5


# --------------------------------------------------------------------------- #
# Logica del juego (sin red, con reloj inyectable para poder probarla)
# --------------------------------------------------------------------------- #
Out = Tuple[str, dict]  # (destino, mensaje). destino: driver | hacker | all | sender


@dataclass
class Driver:
    p: List[float] = field(default_factory=lambda: [0.0, 0.0, 0.0])
    r: List[float] = field(default_factory=lambda: [0.0, 0.0, 0.0, 1.0])
    v: List[float] = field(default_factory=lambda: [0.0, 0.0, 0.0])
    item: str = ""
    turbo_until: float = 0.0
    shield_until: float = 0.0
    stun_until: float = 0.0


@dataclass
class Hacker:
    p: List[float] = field(default_factory=lambda: [0.0, 20.0, 0.0])
    r: List[float] = field(default_factory=lambda: [0.0, 0.0, 0.0, 1.0])
    freeze_until: float = 0.0
    lock_until: float = 0.0


def _vec(value, n: int) -> Optional[List[float]]:
    if not isinstance(value, (list, tuple)) or len(value) != n:
        return None
    out = []
    for x in value:
        if isinstance(x, bool) or not isinstance(x, (int, float)) or not math.isfinite(x):
            return None
        out.append(float(x))
    return out


class Game:
    def __init__(self, cfg: Config, clock=time.monotonic, rng: Optional[random.Random] = None):
        self.cfg = cfg
        self.clock = clock
        self.rng = rng or random.Random()
        self.t0 = clock()
        self.tick_n = 0
        self.connected = {"driver": False, "hacker": False}
        self.reset_world()

    # ---- estado ---------------------------------------------------------- #
    def reset_world(self):
        self.driver = Driver()
        self.hacker = Hacker()
        self.finished = False
        self.hacked_until = [0.0] * self.cfg.obstacles
        self.doors = [False] * self.cfg.obstacles
        self.hack_obstacle = -1
        self.hack_deadline = 0.0
        self.traps: Dict[int, List[float]] = {}
        self.next_trap_id = 1

    @property
    def phase(self) -> str:
        if self.finished:
            return "finished"
        return "playing" if all(self.connected.values()) else "waiting"

    def _left(self, until: float, now: float) -> float:
        return round(until - now, 3) if until > now else 0.0

    def _hack_active(self) -> bool:
        return self.hack_obstacle >= 0

    def can_hack(self, now: float) -> bool:
        return (self.phase == "playing" and not self._hack_active()
                and now >= self.hacker.lock_until and now >= self.hacker.freeze_until)

    def snapshot(self) -> dict:
        now = self.clock()
        d, h = self.driver, self.hacker
        return {
            "t": "snap", "tick": self.tick_n, "time": round(now - self.t0, 3),
            "phase": self.phase,
            "driver": {
                "connected": self.connected["driver"], "p": d.p, "r": d.r, "v": d.v,
                "item": d.item,
                "turbo": self._left(d.turbo_until, now),
                "shield": self._left(d.shield_until, now),
                "stun": self._left(d.stun_until, now),
            },
            "hacker": {
                "connected": self.connected["hacker"], "p": h.p, "r": h.r,
                "frozen": now < h.freeze_until, "freeze": self._left(h.freeze_until, now),
                "can_hack": self.can_hack(now), "lock": self._left(h.lock_until, now),
            },
            "hack": {
                "active": self._hack_active(), "obstacle": self.hack_obstacle,
                "left": self._left(self.hack_deadline, now) if self._hack_active() else 0.0,
            },
            "hacked": [now < u for u in self.hacked_until],
            "doors": self.doors,
            "traps": [{"id": i, "p": p} for i, p in self.traps.items()],
        }

    def welcome(self, role: str) -> dict:
        c = self.cfg
        return {"t": "welcome", "v": PROTOCOL_VERSION, "role": role, "tick_rate": c.tick_rate,
                "phase": self.phase, "obstacles": c.obstacles, "hack_time": c.hack_time,
                "lock_time": c.lock_time, "cable_freeze": c.cable_freeze}

    # ---- conexion -------------------------------------------------------- #
    def join(self, role: str) -> List[Out]:
        before = self.phase
        self.connected[role] = True
        out: List[Out] = [("all", {"t": "peer", "role": role, "connected": True})]
        out += self._phase_change(before)
        return out

    def leave(self, role: str) -> List[Out]:
        before = self.phase
        self.connected[role] = False
        if self._hack_active() and role == "hacker":
            self.hack_obstacle = -1  # un hackeo a medias se cancela sin penalizar
        out: List[Out] = [("all", {"t": "peer", "role": role, "connected": False})]
        out += self._phase_change(before)
        return out

    def _phase_change(self, before: str) -> List[Out]:
        return [("all", {"t": "phase", "phase": self.phase})] if self.phase != before else []

    # ---- mensajes de los clientes --------------------------------------- #
    def handle(self, role: str, msg: dict) -> List[Out]:
        kind = msg.get("t")
        now = self.clock()
        handler = getattr(self, "_on_" + kind, None) if isinstance(kind, str) else None
        if handler is None or kind.startswith("_"):
            return [("sender", {"t": "error", "msg": f"mensaje desconocido: {kind!r}"})]
        return handler(role, msg, now)

    def _deny(self, kind: str, reason: str, **extra) -> List[Out]:
        return [("sender", {"t": kind, "reason": reason, **extra})]

    def _on_ping(self, role, msg, now):
        return [("sender", {"t": "pong", "c": msg.get("c"), "time": round(now - self.t0, 3)})]

    def _on_state(self, role, msg, now):
        p, r = _vec(msg.get("p"), 3), _vec(msg.get("r"), 4)
        if role == "driver":
            v = _vec(msg.get("v"), 3)
            if p: self.driver.p = p
            if r: self.driver.r = r
            if v: self.driver.v = v
        else:
            if now < self.hacker.freeze_until:
                return []  # dron inmovilizado: el servidor ignora el movimiento
            if p: self.hacker.p = p
            if r: self.hacker.r = r
        return []

    # -- conductor
    def _on_item_pickup(self, role, msg, now):
        if role != "driver":
            return self._deny("error", "solo el conductor recoge items")
        self.driver.item = self.rng.choice(ITEMS)  # igual que ItemPickup: aleatorio
        return [("all", {"t": "item_granted", "item": self.driver.item})]

    def _on_use_item(self, role, msg, now):
        if role != "driver":
            return self._deny("error", "solo el conductor usa items")
        item = msg.get("item")
        d = self.driver
        if item not in ITEMS or d.item != item:
            return self._deny("item_denied", "no tienes ese item", item=item)
        d.item = ""
        out: List[Out] = []
        if item == "turbo":
            d.turbo_until = now + self.cfg.turbo_time
        elif item == "escudo":
            d.shield_until = now + self.cfg.shield_time
        else:  # trampa
            pos = _vec(msg.get("p"), 3) or list(d.p)
            if len(self.traps) >= self.cfg.max_traps:
                self.traps.pop(next(iter(self.traps)))
            tid = self.next_trap_id
            self.next_trap_id += 1
            self.traps[tid] = pos
            out.append(("all", {"t": "trap_dropped", "id": tid, "p": pos}))
        out.append(("all", {"t": "power_used", "item": item}))
        return out

    def _on_trap_hit(self, role, msg, now):
        if role != "driver":
            return self._deny("error", "solo el conductor pisa trampas")
        tid = msg.get("id")
        if tid not in self.traps:
            return []
        del self.traps[tid]
        out: List[Out] = [("all", {"t": "trap_removed", "id": tid})]
        if now < self.driver.shield_until:
            out.append(("all", {"t": "trap_blocked", "id": tid}))  # el escudo protege
        else:
            self.driver.stun_until = now + self.cfg.trap_stun
            out.append(("all", {"t": "driver_stunned", "seconds": self.cfg.trap_stun}))
        return out

    def _on_finish(self, role, msg, now):
        if role != "driver" or self.phase != "playing":
            return []
        self.finished = True
        return [("all", {"t": "finish"}), ("all", {"t": "phase", "phase": "finished"})]

    # -- hacker
    def _on_cable_hit(self, role, msg, now):
        if role != "hacker" or self.phase != "playing":
            return []
        self.hacker.freeze_until = now + self.cfg.cable_freeze
        out: List[Out] = []
        if self._hack_active():  # el choque cancela el hackeo, sin bloqueo extra
            out.append(("all", {"t": "hack_result", "ok": False, "obstacle": self.hack_obstacle,
                                "reason": "cable", "lock": 0.0}))
            self.hack_obstacle = -1
        out.append(("all", {"t": "drone_frozen", "seconds": self.cfg.cable_freeze}))
        return out

    def _on_hack_start(self, role, msg, now):
        if role != "hacker":
            return self._deny("error", "solo el hacker hackea")
        ob = msg.get("obstacle")
        if isinstance(ob, bool) or not isinstance(ob, int) or not 0 <= ob < self.cfg.obstacles:
            return self._deny("hack_denied", "obstaculo invalido", obstacle=ob)
        if self.phase != "playing":
            return self._deny("hack_denied", "la partida no esta en curso", obstacle=ob)
        if now < self.hacker.freeze_until:
            return self._deny("hack_denied", "dron inmovilizado", obstacle=ob,
                              left=self._left(self.hacker.freeze_until, now))
        if now < self.hacker.lock_until:
            return self._deny("hack_denied", "bloqueado", obstacle=ob,
                              left=self._left(self.hacker.lock_until, now))
        if self._hack_active():
            return self._deny("hack_denied", "ya hay un hackeo en curso", obstacle=ob)
        if now < self.hacked_until[ob]:
            return self._deny("hack_denied", "ya esta hackeado", obstacle=ob)
        self.hack_obstacle, self.hack_deadline = ob, now + self.cfg.hack_time
        return [("all", {"t": "hack_started", "obstacle": ob, "time": self.cfg.hack_time})]

    def _on_door_toggle(self, role, msg, now):
        if role != "hacker":
            return self._deny("error", "solo el hacker controla las puertas")
        door_id = msg.get("id")
        if isinstance(door_id, bool) or not isinstance(door_id, int) or not 0 <= door_id < len(self.doors):
            return self._deny("error", "puerta invalida", id=door_id)
        if self.phase != "playing":
            return self._deny("error", "la partida no esta en curso")
        self.doors[door_id] = not self.doors[door_id]
        return [("all", {"t": "door_state", "id": door_id, "open": self.doors[door_id]})]

    def _on_hack_result(self, role, msg, now):
        if role != "hacker" or not self._hack_active():
            return []
        return self._finish_hack(bool(msg.get("ok")) and now <= self.hack_deadline + 0.25,
                                 "tiempo" if now > self.hack_deadline + 0.25 else "", now)

    def _finish_hack(self, ok: bool, reason: str, now: float) -> List[Out]:
        ob, self.hack_obstacle = self.hack_obstacle, -1
        out: List[Out] = []
        lock = 0.0
        if ok:
            self.hacked_until[ob] = (now + self.cfg.hack_duration
                                     if self.cfg.hack_duration > 0 else float("inf"))
            out.append(("all", {"t": "obstacle_hacked", "obstacle": ob,
                                "seconds": self.cfg.hack_duration}))
        else:
            lock = self.cfg.lock_time
            self.hacker.lock_until = now + lock
        out.append(("all", {"t": "hack_result", "ok": ok, "obstacle": ob,
                            "reason": reason, "lock": lock}))
        return out

    def _on_reset(self, role, msg, now):
        self.reset_world()
        return [("all", {"t": "reset"}), ("all", {"t": "phase", "phase": self.phase})]

    # ---- reloj del servidor --------------------------------------------- #
    def tick(self) -> List[Out]:
        """Procesa vencimientos de temporizadores. Se llama en cada tick."""
        now = self.clock()
        self.tick_n += 1
        out: List[Out] = []
        if self._hack_active() and now >= self.hack_deadline:
            if self.cfg.timeout_is_fail:
                out += self._finish_hack(False, "tiempo", now)
            else:
                self.hack_obstacle = -1
                out.append(("all", {"t": "hack_result", "ok": False, "obstacle": -1,
                                    "reason": "tiempo", "lock": 0.0}))
        for i, until in enumerate(self.hacked_until):  # re-armar obstaculos temporales
            if until != float("inf") and 0 < until <= now:
                self.hacked_until[i] = 0.0
                out.append(("all", {"t": "obstacle_rearmed", "obstacle": i}))
        return out


# --------------------------------------------------------------------------- #
# Red (asyncio)
# --------------------------------------------------------------------------- #
class Server:
    def __init__(self, cfg: Config, host="0.0.0.0", port=7777, discovery_port=7778, name="KartIHC"):
        self.cfg, self.host, self.port = cfg, host, port
        self.discovery_port, self.name = discovery_port, name
        self.game = Game(cfg)
        self.writers: Dict[str, asyncio.StreamWriter] = {}
        self._server: Optional[asyncio.base_events.Server] = None
        self._tasks: List[asyncio.Task] = []
        self._udp = None

    async def start(self):
        self._server = await asyncio.start_server(self._client, self.host, self.port, limit=MAX_LINE)
        self.port = self._server.sockets[0].getsockname()[1]  # por si se pidio el puerto 0
        self._tasks.append(asyncio.create_task(self._tick_loop()))
        if self.discovery_port:
            try:
                loop = asyncio.get_running_loop()
                self._udp, _ = await loop.create_datagram_endpoint(
                    lambda: _Discovery(self), local_addr=(self.host, self.discovery_port),
                    allow_broadcast=True)
            except OSError as exc:
                log.warning("Sin descubrimiento automatico (UDP %s): %s", self.discovery_port, exc)
        log.info("Servidor listo en %s:%d  (tick %d Hz)", self.host, self.port, self.cfg.tick_rate)

    async def stop(self):
        for t in self._tasks:
            t.cancel()
        if self._udp:
            self._udp.close()
        for w in list(self.writers.values()):
            w.close()
        if self._server:
            self._server.close()
            await self._server.wait_closed()

    # ---- envio ----------------------------------------------------------- #
    @staticmethod
    def _encode(msg: dict) -> bytes:
        return (json.dumps(msg, separators=(",", ":")) + "\n").encode()

    def _send(self, role: str, data: bytes, droppable=False):
        w = self.writers.get(role)
        if w is None or w.is_closing():
            return
        if droppable and w.transport.get_write_buffer_size() > 64 * 1024:
            return  # cliente lento: se salta esta foto, llegara la siguiente
        w.write(data)

    def _dispatch(self, outs: List[Out], sender: Optional[str] = None):
        for target, msg in outs:
            data = self._encode(msg)
            if target == "all":
                for role in list(self.writers):
                    self._send(role, data)
            elif target == "sender" and sender:
                self._send(sender, data)
            elif target in ROLES:
                self._send(target, data)

    async def _tick_loop(self):
        dt = 1.0 / self.cfg.tick_rate
        nxt = time.monotonic()
        while True:
            self._dispatch(self.game.tick())
            if self.writers:
                data = self._encode(self.game.snapshot())
                for role in list(self.writers):
                    self._send(role, data, droppable=True)
            nxt += dt
            delay = nxt - time.monotonic()
            if delay < -1.0:      # nos atrasamos mucho (suspension): no acumular
                nxt = time.monotonic()
            await asyncio.sleep(max(0.0, delay))

    # ---- clientes -------------------------------------------------------- #
    async def _client(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        peer = writer.get_extra_info("peername")
        sock = writer.get_extra_info("socket")
        if sock is not None:
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        role = None
        try:
            hello = await asyncio.wait_for(self._read(reader), timeout=5.0)
            role = hello.get("role") if hello and hello.get("t") == "hello" else None
            if role not in ROLES:
                writer.write(self._encode({"t": "error", "msg": "primer mensaje: hello con role driver|hacker"}))
                return
            if hello.get("v", PROTOCOL_VERSION) != PROTOCOL_VERSION:
                writer.write(self._encode({"t": "error", "msg": "version de protocolo incompatible"}))
                return
            old = self.writers.get(role)
            if old is not None:  # reconexion: la conexion anterior queda reemplazada
                log.info("%s se reconecta desde %s", role, peer)
                self.writers.pop(role, None)
                old.close()
            self.writers[role] = writer
            log.info("%s conectado desde %s", role, peer)
            writer.write(self._encode(self.game.welcome(role)))
            self._dispatch(self.game.join(role))
            while True:
                msg = await self._read(reader)
                if msg is None:
                    break
                self._dispatch(self.game.handle(role, msg), sender=role)
        except (asyncio.TimeoutError, ConnectionError, asyncio.IncompleteReadError):
            pass
        except Exception:
            log.exception("error con el cliente %s", role)
        finally:
            if role in ROLES and self.writers.get(role) is writer:
                del self.writers[role]
                log.info("%s desconectado", role)
                self._dispatch(self.game.leave(role))
            writer.close()

    async def _read(self, reader: asyncio.StreamReader) -> Optional[dict]:
        """Lee una linea JSON. None = conexion cerrada. Las lineas invalidas se ignoran."""
        while True:
            try:
                line = await reader.readline()
            except (ValueError, asyncio.LimitOverrunError):
                return None  # linea demasiado larga: se corta la conexion
            if not line:
                return None
            try:
                msg = json.loads(line)
            except ValueError:
                continue
            if isinstance(msg, dict):
                return msg


class _Discovery(asyncio.DatagramProtocol):
    """Responde a 'KARTIHC_DISCOVER' (broadcast UDP) con la direccion del servidor."""
    def __init__(self, server: Server):
        self.server = server

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, addr):
        if data.strip() == b"KARTIHC_DISCOVER":
            reply = {"t": "server", "name": self.server.name, "port": self.server.port, "v": PROTOCOL_VERSION}
            self.transport.sendto(json.dumps(reply).encode(), addr)


# --------------------------------------------------------------------------- #
# Programa principal
# --------------------------------------------------------------------------- #
def lan_addresses() -> List[str]:
    found = set()
    for target in ("10.255.255.255", "192.168.255.255", "172.31.255.255"):
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect((target, 1))  # no envia nada: solo elige la interfaz
            found.add(s.getsockname()[0])
            s.close()
        except OSError:
            pass
    return sorted(ip for ip in found if not ip.startswith("127."))


def parse_args(argv=None):
    p = argparse.ArgumentParser(description="Servidor de sesion KartIHC (conductor + hacker)")
    d = Config()
    p.add_argument("--host", default="0.0.0.0", help="interfaz donde escuchar (por defecto todas)")
    p.add_argument("--port", type=int, default=7777, help="puerto TCP del juego")
    p.add_argument("--discovery-port", type=int, default=7778, help="puerto UDP de descubrimiento (0 = apagado)")
    p.add_argument("--tick-rate", type=int, default=d.tick_rate, help="fotos del mundo por segundo")
    p.add_argument("--obstacles", type=int, default=d.obstacles)
    p.add_argument("--hack-time", type=float, default=d.hack_time)
    p.add_argument("--lock-time", type=float, default=d.lock_time)
    p.add_argument("--cable-freeze", type=float, default=d.cable_freeze)
    p.add_argument("--hack-duration", type=float, default=d.hack_duration, help="0 = permanente")
    p.add_argument("--timeout-not-fail", action="store_true", help="agotar el tiempo no bloquea al hacker")
    p.add_argument("-v", "--verbose", action="store_true")
    a = p.parse_args(argv)
    cfg = Config(tick_rate=max(1, min(120, a.tick_rate)), obstacles=max(1, a.obstacles),
                 hack_time=a.hack_time, lock_time=a.lock_time, cable_freeze=a.cable_freeze,
                 hack_duration=a.hack_duration, timeout_is_fail=not a.timeout_not_fail)
    return a, cfg


async def amain(a, cfg):
    server = Server(cfg, a.host, a.port, a.discovery_port)
    await server.start()
    for ip in lan_addresses():
        log.info("Conectar los clientes a  %s:%d", ip, server.port)
    try:
        await asyncio.Event().wait()
    finally:
        await server.stop()


def main():
    a, cfg = parse_args()
    logging.basicConfig(level=logging.DEBUG if a.verbose else logging.INFO,
                        format="%(asctime)s %(levelname)s %(message)s", datefmt="%H:%M:%S")
    try:
        asyncio.run(amain(a, cfg))
    except KeyboardInterrupt:
        log.info("Servidor detenido")


if __name__ == "__main__":
    main()
