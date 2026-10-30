import os
import time
import socket
import urllib.request
import argparse

import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision as mp_vision


# ------------------- CONFIGURACION -------------------

# En Windows normalmente:
# 0 = primera camara
# 1 = segunda camara
# 2 = tercera camara, etc.
CAM_INDEX = 2

CAM_WIDTH = 260
CAM_HEIGHT = 140

MODEL_VARIANT = "full"   # "lite" = rapido, "full" = mas preciso

MODEL_PATH = f"pose_landmarker_{MODEL_VARIANT}.task"

MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/"
    "pose_landmarker/"
    f"pose_landmarker_{MODEL_VARIANT}/float16/latest/"
    f"pose_landmarker_{MODEL_VARIANT}.task"
)

MIN_POSE_DETECTION_CONFIDENCE = 0.3
MIN_TRACKING_CONFIDENCE = 0.3
VISIBILITY_THRESHOLD = 0.35

# Cuantas personas puede detectar MediaPipe
NUM_POSES = 3


# ------------------- CALIBRACION -------------------

COUNTDOWN_SECONDS = 3.0
CALIBRATION_SECONDS = 1.0


# ------------------- DETECCION DE PIERNAS -------------------

RAISE_ENTER_PX = 35
RAISE_EXIT_PX = 15

DEBOUNCE_FRAMES = 3


# ------------------- SEGUIMIENTO DE PERSONA -------------------

MAX_JUMP_PX = 180


# ------------------- UDP / UNITY -------------------

# IP DE LA PC DONDE ESTA UNITY
UNITY_IP = "127.0.0.1"

UNITY_PORT = 5555
PREVIEW_PORT = 5556
CONTROL_PORT = 5557

SEND_ONLY_ON_CHANGE = False


# ------------------------------------------------------------

LEGS = {
    "izq": {
        "hip": 23,
        "knee": 25,
        "ankle": 27
    },

    "der": {
        "hip": 24,
        "knee": 26,
        "ankle": 28
    },
}


MODE_IDLE = "idle"
MODE_COUNTDOWN = "countdown"
MODE_CALIBRATING = "calibrating"
MODE_RUNNING = "running"


# ------------------------------------------------------------
# DESCARGAR MODELO
# ------------------------------------------------------------

def ensure_model():

    if os.path.exists(MODEL_PATH):
        return

    print(
        f"Descargando modelo de pose ({MODEL_PATH})..."
    )

    urllib.request.urlretrieve(
        MODEL_URL,
        MODEL_PATH
    )

    print("Modelo descargado.")


# ------------------------------------------------------------
# ESTADO DE CADA PIERNA
# ------------------------------------------------------------

class LegState:

    def __init__(self, name):

        self.name = name

        self.baseline_y = None

        self.raised = False

        self._pending_state = None

        self._pending_count = 0


    def calibrate(self, ankle_y):

        self.baseline_y = ankle_y

        self.raised = False

        self._pending_state = None

        self._pending_count = 0


    def reset(self):

        self.baseline_y = None

        self.raised = False

        self._pending_state = None

        self._pending_count = 0


    def update(self, ankle_y):

        if self.baseline_y is None:
            return self.raised


        # En  el eje Y aumenta hacia abajo.
        # Por eso, si el tobillo tiene un Y menor,
        # significa que subio.

        diff = self.baseline_y - ankle_y


        if not self.raised and diff > RAISE_ENTER_PX:

            candidate = True

        elif self.raised and diff < RAISE_EXIT_PX:

            candidate = False

        else:

            candidate = self.raised


        # Debounce para evitar cambios por ruido

        if candidate != self.raised:

            if candidate == self._pending_state:

                self._pending_count += 1

            else:

                self._pending_state = candidate
                self._pending_count = 1


            if self._pending_count >= DEBOUNCE_FRAMES:

                self.raised = candidate

                self._pending_state = None

                self._pending_count = 0

        else:

            self._pending_state = None

            self._pending_count = 0


        return self.raised


# ------------------------------------------------------------
# CENTRO DE CADERA
# ------------------------------------------------------------

def hip_center_px(pose_landmarks, w, h):

    left_hip = pose_landmarks[23]
    right_hip = pose_landmarks[24]


    cx = (
        (left_hip.x + right_hip.x) / 2.0
    ) * w


    cy = (
        (left_hip.y + right_hip.y) / 2.0
    ) * h


    return cx, cy


# ------------------------------------------------------------
# VISIBILIDAD DE LAS PIERNAS
# ------------------------------------------------------------

def leg_visibility_score(pose_landmarks):

    idxs = [
        23, 24,
        25, 26,
        27, 28
    ]


    vis = [
        pose_landmarks[i].visibility
        for i in idxs
    ]


    return sum(vis) / len(vis)


# ------------------------------------------------------------
# DISTANCIA ENTRE DOS PUNTOS
# ------------------------------------------------------------

def dist(p1, p2):

    return (
        (p1[0] - p2[0]) ** 2 +
        (p1[1] - p2[1]) ** 2
    ) ** 0.5


# ------------------------------------------------------------
# PROGRAMA PRINCIPAL
# ------------------------------------------------------------

def main():

    parser = argparse.ArgumentParser()
    parser.add_argument("--unity-control", action="store_true")
    args = parser.parse_args()

    ensure_model()

    global CAM_INDEX

    CAM_INDEX = detectar_camara_externa()

    if CAM_INDEX is None:
        print("No se pudo encontrar la camara externa.")
        return

    cap = abrir_camara(CAM_INDEX)

    # Intentamos usar MJPG
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))


    cap.set(
        cv2.CAP_PROP_FRAME_WIDTH,
        CAM_WIDTH
    )


    cap.set(
        cv2.CAP_PROP_FRAME_HEIGHT,
        CAM_HEIGHT
    )


    if not cap.isOpened():

        print(
            f"No se pudo abrir la camara con indice "
            f"{CAM_INDEX}."
        )

        print(
            "Prueba CAM_INDEX = 0, 1 o 2."
        )

        return


    print(
        f"Camara abierta correctamente: indice {CAM_INDEX}"
    )


    # --------------------------------------------------------
    # MEDIAPIPE
    # --------------------------------------------------------

    base_options = mp_python.BaseOptions(
        model_asset_path=MODEL_PATH
    )


    options = mp_vision.PoseLandmarkerOptions(

        base_options=base_options,

        running_mode=mp_vision.RunningMode.VIDEO,

        num_poses=NUM_POSES,

        min_pose_detection_confidence=
            MIN_POSE_DETECTION_CONFIDENCE,

        min_tracking_confidence=
            MIN_TRACKING_CONFIDENCE,
    )


    landmarker = (
        mp_vision.PoseLandmarker
        .create_from_options(options)
    )


    # --------------------------------------------------------
    # VARIABLES
    # --------------------------------------------------------

    leg_states = {
        name: LegState(name)
        for name in LEGS
    }


    start_time = time.time()

    prev_frame_time = time.time()

    fps_smooth = 0.0


    udp_sock = None
    control_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    control_sock.bind(("127.0.0.1", CONTROL_PORT))
    control_sock.setblocking(False)
    preview_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    control_sock.sendto(f"CAMARA {CAM_INDEX}".encode("utf-8"), (UNITY_IP, UNITY_PORT))

    last_sent_comando = None


    mode = MODE_IDLE

    mode_start_t = 0.0


    calib_samples = {
        "izq": [],
        "der": []
    }


    calib_hip_ref = None

    target_hip_ref = None

    target_visible = False


    print("Cámara lista. Usa Calibrar desde Unity.")


    try:

        while cap.isOpened():

            try:
                control_data, _ = control_sock.recvfrom(1024)
                control_command = control_data.decode("utf-8", errors="ignore").strip().upper()
                if control_command == "CAMBIAR_CAMARA":
                    cap, CAM_INDEX = cambiar_camara(cap, CAM_INDEX)
                    control_sock.sendto(f"CAMARA {CAM_INDEX}".encode("utf-8"), (UNITY_IP, UNITY_PORT))
                    mode = MODE_IDLE
                    for name in LEGS:
                        leg_states[name].reset()
                    target_hip_ref = None
                    target_visible = False
                    calib_hip_ref = None
                elif control_command == "CALIBRAR":
                    for name in LEGS:
                        leg_states[name].reset()
                    target_hip_ref = None
                    target_visible = False
                    mode = MODE_COUNTDOWN
                    mode_start_t = time.time()
                    print("\\nCalibración solicitada desde Unity.")
            except BlockingIOError:
                pass

            # ------------------------------------------------
            # LEER CAMARA
            # ------------------------------------------------

            ok, frame = cap.read()


            if not ok:

                print(
                    "No se recibio frame de la camara."
                )

                break


            # Imagen espejo

            frame = cv2.flip(
                frame,
                1
            )


            h, w = frame.shape[:2]


            # ------------------------------------------------
            # CONVERTIR A RGB
            # ------------------------------------------------

            rgb = cv2.cvtColor(
                frame,
                cv2.COLOR_BGR2RGB
            )


            mp_image = mp.Image(
                image_format=mp.ImageFormat.SRGB,
                data=rgb
            )


            # ------------------------------------------------
            # MEDIAPIPE
            # ------------------------------------------------

            timestamp_ms = int(
                (time.time() - start_time) * 1000
            )


            result = landmarker.detect_for_video(
                mp_image,
                timestamp_ms
            )


            now = time.time()


            candidatos = (
                result.pose_landmarks
                or []
            )


            info_lines = []

            target_visible = False

            matched = None


            # =================================================
            # ELEGIR PERSONA
            # =================================================

            if mode == MODE_CALIBRATING:

                if candidatos:

                    if calib_hip_ref is None:

                        best = max(
                            candidatos,
                            key=leg_visibility_score
                        )

                        matched = best

                    else:

                        best = min(
                            candidatos,
                            key=lambda c:
                                dist(
                                    hip_center_px(
                                        c,
                                        w,
                                        h
                                    ),
                                    calib_hip_ref
                                )
                        )

                        matched = best


                    calib_hip_ref = hip_center_px(
                        matched,
                        w,
                        h
                    )


            elif (
                mode == MODE_RUNNING
                and target_hip_ref is not None
            ):

                if candidatos:

                    best = min(
                        candidatos,
                        key=lambda c:
                            dist(
                                hip_center_px(
                                    c,
                                    w,
                                    h
                                ),
                                target_hip_ref
                            )
                    )


                    d = dist(
                        hip_center_px(
                            best,
                            w,
                            h
                        ),
                        target_hip_ref
                    )


                    if d <= MAX_JUMP_PX:

                        matched = best

                        target_hip_ref = (
                            hip_center_px(
                                matched,
                                w,
                                h
                            )
                        )

                        target_visible = True


            else:

                if candidatos:

                    matched = max(
                        candidatos,
                        key=leg_visibility_score
                    )


            # =================================================
            # PROCESAR PIERNAS
            # =================================================

            if matched is not None:

                for name, idx in LEGS.items():

                    hip_lm = matched[
                        idx["hip"]
                    ]

                    knee_lm = matched[
                        idx["knee"]
                    ]

                    ankle_lm = matched[
                        idx["ankle"]
                    ]


                    if (
                        hip_lm.visibility >
                        VISIBILITY_THRESHOLD

                        and

                        knee_lm.visibility >
                        VISIBILITY_THRESHOLD

                        and

                        ankle_lm.visibility >
                        VISIBILITY_THRESHOLD
                    ):

                        hip = (
                            int(hip_lm.x * w),
                            int(hip_lm.y * h)
                        )


                        knee = (
                            int(knee_lm.x * w),
                            int(knee_lm.y * h)
                        )


                        ankle = (
                            int(ankle_lm.x * w),
                            int(ankle_lm.y * h)
                        )


                        # Lineas de la pierna

                        cv2.line(
                            frame,
                            hip,
                            knee,
                            (0, 255, 0),
                            3
                        )


                        cv2.line(
                            frame,
                            knee,
                            ankle,
                            (0, 255, 0),
                            3
                        )


                        # Puntos

                        for p in (
                            hip,
                            knee,
                            ankle
                        ):

                            cv2.circle(
                                frame,
                                p,
                                6,
                                (0, 140, 255),
                                -1
                            )


                        # ------------------------------------
                        # CALIBRACION
                        # ------------------------------------

                        if mode == MODE_CALIBRATING:

                            calib_samples[
                                name
                            ].append(
                                ankle[1]
                            )


                        # ------------------------------------
                        # EJECUCION
                        # ------------------------------------

                        elif (
                            mode == MODE_RUNNING
                            and target_visible
                        ):

                            leg_states[
                                name
                            ].update(
                                ankle[1]
                            )


                        base = (
                            leg_states[name]
                            .baseline_y
                        )


                        base_txt = (
                            f"{base:.0f}"
                            if base is not None
                            else "-"
                        )


                        estado_txt = (
                            "ARRIBA"
                            if leg_states[name].raised
                            else "abajo"
                        )


                        info_lines.append(
                            f"{name}: "
                            f"y={ankle[1]} "
                            f"base={base_txt} "
                            f"-> {estado_txt}"
                        )


                    else:

                        info_lines.append(
                            f"{name}: pierna dudosa"
                        )


            # =================================================
            # MOSTRAR OTRAS PERSONAS
            # =================================================

            for c in candidatos:

                if c is matched:
                    continue


                hip = hip_center_px(
                    c,
                    w,
                    h
                )


                cv2.circle(
                    frame,
                    (
                        int(hip[0]),
                        int(hip[1])
                    ),
                    10,
                    (120, 120, 120),
                    2
                )


                cv2.putText(
                    frame,
                    "ignorado",
                    (
                        int(hip[0]) - 20,
                        int(hip[1]) - 15
                    ),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.45,
                    (120, 120, 120),
                    1
                )


            # =================================================
            # MAQUINA DE ESTADOS
            # =================================================

            if mode == MODE_COUNTDOWN:

                if (
                    now - mode_start_t
                ) >= COUNTDOWN_SECONDS:

                    mode = MODE_CALIBRATING

                    mode_start_t = now

                    calib_samples = {
                        "izq": [],
                        "der": []
                    }

                    calib_hip_ref = None


                    print(
                        "\nCalibrando... "
                        "mantén AMBAS piernas abajo."
                    )


            elif mode == MODE_CALIBRATING:

                if (
                    now - mode_start_t
                ) >= CALIBRATION_SECONDS:

                    ok_calib = (
                        all(
                            len(
                                calib_samples[n]
                            ) > 0
                            for n in LEGS
                        )

                        and

                        calib_hip_ref is not None
                    )


                    if ok_calib:

                        for name in LEGS:

                            avg_y = (
                                sum(
                                    calib_samples[name]
                                )
                                /
                                len(
                                    calib_samples[name]
                                )
                            )


                            leg_states[
                                name
                            ].calibrate(
                                avg_y
                            )


                        target_hip_ref = (
                            calib_hip_ref
                        )


                        mode = MODE_RUNNING

                        try:
                            control_sock.sendto(b"CALIBRACION_LISTA", (UNITY_IP, UNITY_PORT))
                        except OSError:
                            pass


                        if udp_sock is None:

                            udp_sock = socket.socket(
                                socket.AF_INET,
                                socket.SOCK_DGRAM
                            )


                        print(
                            f"Calibración lista."
                            f" Siguiendo a esa persona."
                            f" Enviando UDP a "
                            f"{UNITY_IP}:{UNITY_PORT}"
                        )


                    else:

                        print(
                            "Calibración fallida."
                            " No se vio bien a la persona."
                        )


                        mode = MODE_IDLE
                        try:
                            control_sock.sendto(b"CALIBRACION_FALLIDA", (UNITY_IP, UNITY_PORT))
                        except OSError:
                            pass


            # =================================================
            # COMANDO
            # =================================================

            comando = "NADA"

            color_cmd = (
                200,
                200,
                200
            )


            if mode == MODE_RUNNING:

                if not target_visible:

                    comando = "NADA"

                    color_cmd = (
                        0,
                        165,
                        255
                    )


                else:

                    izq_arriba = (
                        leg_states["izq"].raised
                    )

                    der_arriba = (
                        leg_states["der"].raised
                    )


                    if der_arriba:

                        comando = "ACELERA"

                        color_cmd = (
                            0,
                            255,
                            0
                        )


                    elif izq_arriba:

                        comando = "FRENA"

                        color_cmd = (
                            0,
                            0,
                            255
                        )


                    else:

                        comando = "NADA"

                        color_cmd = (
                            200,
                            200,
                            200
                        )


                # --------------------------------------------
                # UDP
                # --------------------------------------------

                debe_enviar = (
                    (not SEND_ONLY_ON_CHANGE)
                    or
                    (
                        comando !=
                        last_sent_comando
                    )
                )


                if debe_enviar:

                    try:

                        udp_sock.sendto(
                            comando.encode("utf-8"),
                            (
                                UNITY_IP,
                                UNITY_PORT
                            )
                        )


                    except OSError as e:

                        print(
                            f"\nError enviando UDP: {e}"
                        )


                    last_sent_comando = comando

            # =================================================
            # FPS
            # =================================================

            dt = (
                now -
                prev_frame_time
            )


            prev_frame_time = now


            if dt > 0:

                fps_smooth = (
                    0.9 * fps_smooth
                    +
                    0.1 * (1.0 / dt)
                )


            # =================================================
            # OVERLAY
            # =================================================

            if mode == MODE_IDLE:

                cv2.putText(
                    frame,
                    "Presiona 'c' para calibrar",
                    (10, 40),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.9,
                    (0, 165, 255),
                    2
                )


            elif mode == MODE_COUNTDOWN:

                restante = max(
                    0,
                    COUNTDOWN_SECONDS -
                    (now - mode_start_t)
                )


                cv2.putText(
                    frame,
                    f"Preparáte: piernas abajo. "
                    f"Calibra en {restante:0.1f}s",
                    (10, 40),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.8,
                    (0, 165, 255),
                    2
                )


            elif mode == MODE_CALIBRATING:

                cv2.putText(
                    frame,
                    "CALIBRANDO...",
                    (10, 40),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    1.1,
                    (0, 255, 255),
                    3
                )


            elif (
                mode == MODE_RUNNING
                and not target_visible
            ):

                cv2.putText(
                    frame,
                    "OBJETIVO NO ENCONTRADO",
                    (10, 40),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.9,
                    (0, 165, 255),
                    2
                )


            else:

                cv2.putText(
                    frame,
                    comando,
                    (10, 40),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    1.3,
                    color_cmd,
                    4
                )


            # FPS

            cv2.putText(
                frame,
                f"FPS: {fps_smooth:4.1f}",
                (10, h - 10),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (255, 255, 255),
                1
            )


            # Informacion de piernas

            y0 = 100


            for i, line in enumerate(
                info_lines
            ):

                cv2.putText(
                    frame,
                    line,
                    (
                        10,
                        y0 + i * 20
                    ),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (255, 255, 0),
                    1
                )


            # Consola

            print(
                f"[{time.strftime('%H:%M:%S')}] "
                f"modo={mode:11s} "
                f"comando={comando:8s} "
                f"objetivo_visible={target_visible}",
                end="\r"
            )


            # Mostrar ventana

            if args.unity_control:
                preview = cv2.resize(frame, (CAM_WIDTH, CAM_HEIGHT))
                encoded, jpeg = cv2.imencode(".jpg", preview, [cv2.IMWRITE_JPEG_QUALITY, 35])
                if encoded:
                    try:
                        packet = jpeg.tobytes()
                        if len(packet) <= 60000:
                            preview_sock.sendto(packet, (UNITY_IP, PREVIEW_PORT))
                    except OSError:
                        pass
            else:
                cv2.imshow("Control por piernas", frame)
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
                elif key == ord("c"):
                    for name in LEGS:
                        leg_states[name].reset()
                    target_hip_ref = None
                    target_visible = False
                    mode = MODE_COUNTDOWN
                    mode_start_t = time.time()


    finally:

        landmarker.close()

        cap.release()

        cv2.destroyAllWindows()
        control_sock.close()
        preview_sock.close()


        if udp_sock is not None:

            udp_sock.close()
def abrir_camara(index):
    cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAM_WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAM_HEIGHT)
    return cap


def detectar_camara_externa():

    print("Buscando camaras...")

    for index in range(1,10):

        cap = cv2.VideoCapture(
            index,
            cv2.CAP_DSHOW
        )

        if not cap.isOpened():
            cap.release()
            continue

        # Intentamos obtener una imagen
        ok, frame = cap.read()

        if ok:
            print(f"Camara encontrada en indice {index}")

            cap.release()

            # Normalmente:
            # 0 = camara integrada
            # 1 = camara USB
            #
            # Como buscamos la externa, continuamos
            # buscando si encontramos el indice 0.

            if index > 0:
                print(
                    f"Seleccionando camara externa: indice {index}"
                )
                return index

        cap.release()

    print(
        "No se encontro una camara externa."
    )

    return None


def cambiar_camara(cap_actual, indice_actual):
    indices = []
    for index in range(0, 10):
        prueba = abrir_camara(index)
        if prueba.isOpened():
            ok, _ = prueba.read()
            prueba.release()
            if ok:
                indices.append(index)
    candidatos = [index for index in indices if index != indice_actual]
    if not candidatos:
        print("No se encontró otra cámara disponible.")
        return cap_actual, indice_actual
    siguientes = sorted(index for index in candidatos if index > indice_actual)
    siguiente = siguientes[0] if siguientes else min(candidatos)
    cap_actual.release()
    nueva = abrir_camara(siguiente)
    if nueva.isOpened():
        print(f"Cambiando a cámara {siguiente}.")
        return nueva, siguiente
    nueva.release()
    return abrir_camara(indice_actual), indice_actual

# ------------------------------------------------------------
# EJECUTAR
# ------------------------------------------------------------

if __name__ == "__main__":

    main()

