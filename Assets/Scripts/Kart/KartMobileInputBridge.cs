using KartIHC.Gameplay;
using KartIHC.Kart;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Connects the real inputs (PS4/DS4 gyro steering, leg-motion throttle/brake,
/// voice power-ups) to the project's WheelCollider kart.
/// Add it to the same GameObject as KartController, or assign the references manually.
/// </summary>
[RequireComponent(typeof(KartController))]
public class KartMobileInputBridge : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private KartController kartController;
    [SerializeField] private GyroSteeringInput gyroSteeringInput;
    [SerializeField] private JoyShockGyroSteeringInput joyShockSteeringInput;
    [SerializeField] private LegMotionInputReceiver legMotionInput;
    [SerializeField] private VoicePowerUpInput voiceInput;
    [SerializeField] private KartItemInventory inventory;
    [SerializeField] private GameStateManager gameStateManager;

    [Header("Driving")]
    [Tooltip("When enabled, the kart accelerates once the race has started. Turn it off to use W/S or arrow keys.")]
    [SerializeField] private bool autoAccelerate = true;
    [SerializeField] private float autoThrottle = 1f;
    [SerializeField] private float manualThrottle = 1f;
    [SerializeField] private float manualBrake = 1f;

    private void Awake()
    {
        if (kartController == null)
        {
            kartController = GetComponent<KartController>();
        }

        if (gyroSteeringInput == null)
        {
            gyroSteeringInput = FindFirstObjectByType<GyroSteeringInput>();
        }

        if (joyShockSteeringInput == null)
        {
            joyShockSteeringInput = FindFirstObjectByType<JoyShockGyroSteeringInput>();
        }

        if (legMotionInput == null)
        {
            legMotionInput = FindFirstObjectByType<LegMotionInputReceiver>();
        }

        if (voiceInput == null)
        {
            voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        }

        if (inventory == null)
        {
            inventory = GetComponent<KartItemInventory>();
        }

        if (gameStateManager == null)
        {
            gameStateManager = FindFirstObjectByType<GameStateManager>();
        }
    }

    private void OnEnable()
    {
        if (voiceInput != null)
        {
            voiceInput.PowerUpRequested += UsePowerUp;
        }
    }

    private void OnDisable()
    {
        if (voiceInput != null)
        {
            voiceInput.PowerUpRequested -= UsePowerUp;
        }
    }

    private void FixedUpdate()
    {
        if (kartController == null)
        {
            return;
        }

        kartController.SetSteer(ReadSteering());
        kartController.SetAccelerate(ReadThrottle());
        kartController.SetBrake(ReadBrake());
    }

    private float ReadSteering()
    {
        // El mando PS4/DS4 (giroscopio) es el control real del proyecto.
        if (joyShockSteeringInput != null && joyShockSteeringInput.IsConnected)
        {
            return joyShockSteeringInput.Steering;
        }

        // Si el mando no esta conectado, GyroSteeringInput cae solo al
        // teclado A/D (via DeviceTiltReader), asi que sirve como respaldo
        // de pruebas sin necesidad de logica adicional aqui.
        if (gyroSteeringInput != null)
        {
            return gyroSteeringInput.Steering;
        }

        return DeviceTiltReader.ReadKeyboardAxis();
    }

    private float ReadThrottle()
    {
        if (legMotionInput != null && legMotionInput.IsConnected)
        {
            return legMotionInput.Throttle;
        }

        if (autoAccelerate)
        {
            return Mathf.Clamp01(autoThrottle);
        }

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed))
        {
            return Mathf.Clamp01(manualThrottle);
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            return Mathf.Clamp01(manualThrottle);
        }
#endif

        return 0f;
    }

    private float ReadBrake()
    {
        if (legMotionInput != null && legMotionInput.IsConnected)
        {
            return legMotionInput.Brake;
        }

        if (autoAccelerate)
        {
            return 0f;
        }

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed))
        {
            return Mathf.Clamp01(manualBrake);
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            return Mathf.Clamp01(manualBrake);
        }
#endif

        return 0f;
    }

    private void UsePowerUp(string powerUpId)
    {
        if (kartController == null)
        {
            return;
        }

        // Los power-ups solo se usan en carrera o tutorial: no en el menu de
        // inicio ni en pausa.
        if (gameStateManager != null)
        {
            GameState state = gameStateManager.CurrentState;
            if (state != GameState.Playing && state != GameState.Tutorial)
            {
                return;
            }
        }

        // Hay que tener el item (o estar en el tutorial). Si no, el inventario
        // muestra "ITEM NO DISPONIBLE" y no se activa nada.
        if (inventory != null && !inventory.TryUse(powerUpId))
        {
            return;
        }

        switch (powerUpId)
        {
            case "turbo":
                kartController.ActivateTurbo();
                break;
            case "escudo":
                kartController.ActivateShield();
                break;
            case "trampa":
                kartController.DropTrap();
                break;
            default:
                Debug.Log($"Power-up no configurado: {powerUpId}");
                break;
        }
    }
}
