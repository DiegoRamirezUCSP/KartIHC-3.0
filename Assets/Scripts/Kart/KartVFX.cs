using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.Kart
{
    /// <summary>
    /// Kart visual effects, all built in code (no prefabs):
    /// - dust under the rear wheels, scaled by speed and sideways slip, only when grounded,
    /// - turbo: two exhaust trails while it lasts plus a flame burst on activation,
    /// - shield: glossy translucent bubble on the existing shield visual ("EscudoVisual"),
    ///   gently pulsing, plus a sparkle burst on activation,
    /// - a flash burst when KartRecovery repositions the kart.
    /// Materials come from KartIHC/Environment/7 Kart VFX (assets, safe for builds);
    /// if missing they are created at runtime.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartVFX : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureOnKart()
        {
            KartController kart = FindFirstObjectByType<KartController>();
            if (kart != null && kart.GetComponent<KartVFX>() == null)
            {
                kart.gameObject.AddComponent<KartVFX>();
            }
        }

        [Header("Materials (assigned by KartIHC/Environment/7 Kart VFX)")]
        public Material dustMaterial;
        public Material glowMaterial;
        public Material shieldBubbleMaterial;

        [Header("Dust")]
        [SerializeField] private float maxDustRate = 45f;
        [SerializeField] private float speedForMaxDust = 20f;
        [SerializeField] private Color dustColor = new Color(0.78f, 0.66f, 0.5f, 0.45f);

        [Header("Shield")]
        [SerializeField] private string shieldVisualName = "EscudoVisual";
        [SerializeField] private float shieldPulse = 0.04f;

        private KartController kart;
        private Rigidbody rb;
        private KartRecovery recovery;

        private ParticleSystem dustLeft;
        private ParticleSystem dustRight;
        private ParticleSystem turboBurst;
        private ParticleSystem shieldSparkles;
        private ParticleSystem respawnFlash;
        private TrailRenderer[] turboTrails;
        private Transform shieldVisual;
        private Vector3 shieldBaseScale;

        private void Awake()
        {
            kart = GetComponent<KartController>();
            rb = GetComponent<Rigidbody>();
            EnsureMaterials();

            dustLeft = CreateDust("Dust_RL");
            dustRight = CreateDust("Dust_RR");
            turboBurst = CreateBurst("TurboBurst", new Color(1f, 0.55f, 0.15f), 0.35f, 6f, 0.5f, 0.25f, 0.6f);
            shieldSparkles = CreateBurst("ShieldSparkles", new Color(0.4f, 0.9f, 1f), 2.6f, 2f, 0.7f, 0.15f, 0.35f);
            respawnFlash = CreateBurst("RespawnFlash", new Color(0.85f, 0.97f, 1f), 1.4f, 7f, 0.45f, 0.35f, 0.9f);
            turboTrails = new[] { CreateTrail("TurboTrail_L", -1f), CreateTrail("TurboTrail_R", 1f) };

            shieldVisual = FindChild(transform, shieldVisualName);
            if (shieldVisual != null)
            {
                shieldBaseScale = shieldVisual.localScale;
                Renderer shieldRenderer = shieldVisual.GetComponent<Renderer>();
                if (shieldRenderer != null && shieldBubbleMaterial != null)
                {
                    shieldRenderer.sharedMaterial = shieldBubbleMaterial;
                    shieldRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private void OnEnable()
        {
            kart.TurboActivated += OnTurbo;
            kart.ShieldActivated += OnShield;
        }

        private void Start()
        {
            // In Start: KartRecovery may be added at runtime after this component.
            recovery = GetComponent<KartRecovery>();
            if (recovery != null)
            {
                recovery.Respawned += OnRespawned;
            }
        }

        private void OnDisable()
        {
            kart.TurboActivated -= OnTurbo;
            kart.ShieldActivated -= OnShield;
        }

        private void OnDestroy()
        {
            if (recovery != null)
            {
                recovery.Respawned -= OnRespawned;
            }
        }

        private void Update()
        {
            float speed = rb.linearVelocity.magnitude;
            UpdateDust(dustLeft, kart.rearLeft, speed);
            UpdateDust(dustRight, kart.rearRight, speed);

            foreach (TrailRenderer trail in turboTrails)
            {
                trail.emitting = kart.IsTurboActive && speed > 2f;
            }

            if (shieldVisual != null && shieldVisual.gameObject.activeInHierarchy)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 6f) * shieldPulse;
                shieldVisual.localScale = shieldBaseScale * pulse;
            }
        }

        private void UpdateDust(ParticleSystem dust, WheelCollider wheel, float speed)
        {
            ParticleSystem.EmissionModule emission = dust.emission;
            if (wheel == null || !wheel.GetGroundHit(out WheelHit hit))
            {
                emission.rateOverTime = 0f;
                return;
            }

            dust.transform.position = hit.point + Vector3.up * 0.1f;
            float speed01 = Mathf.Clamp01((speed - 1f) / speedForMaxDust);
            float slip = Mathf.Clamp01(Mathf.Abs(hit.sidewaysSlip) * 2f + Mathf.Abs(hit.forwardSlip));
            emission.rateOverTime = maxDustRate * Mathf.Max(speed01, slip * 0.8f);
        }

        private void OnTurbo()
        {
            turboBurst.transform.position = transform.position - transform.forward * 2.5f + Vector3.up * 0.6f;
            turboBurst.Emit(30);
        }

        private void OnShield()
        {
            shieldSparkles.transform.position = transform.position + Vector3.up * 1.2f;
            shieldSparkles.Emit(40);
        }

        private void OnRespawned()
        {
            respawnFlash.transform.position = transform.position + Vector3.up * 1f;
            respawnFlash.Emit(60);
            dustLeft.Clear();
            dustRight.Clear();
            foreach (TrailRenderer trail in turboTrails)
            {
                trail.Clear();
            }
        }

        // ---------- Construction ----------

        private ParticleSystem CreateSystem(string systemName, Material material, int maxParticles)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(transform, false);
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.playOnAwake = false;
            main.loop = true;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private ParticleSystem CreateDust(string systemName)
        {
            ParticleSystem dust = CreateSystem(systemName, dustMaterial, 120);
            ParticleSystem.MainModule main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = dustColor;
            main.gravityModifier = -0.03f;

            ParticleSystem.EmissionModule emission = dust.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            ParticleSystem.SizeOverLifetimeModule size = dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));

            ParticleSystem.ColorOverLifetimeModule color = dust.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(Color.white, 0.1f);

            dust.Play();
            return dust;
        }

        private ParticleSystem CreateBurst(string systemName, Color tint, float radius, float speed, float lifetime, float minSize, float maxSize)
        {
            ParticleSystem burst = CreateSystem(systemName, glowMaterial, 100);
            ParticleSystem.MainModule main = burst.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = tint;

            ParticleSystem.EmissionModule emission = burst.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = burst.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            ParticleSystem.ColorOverLifetimeModule color = burst.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(Color.white, 0.05f);

            ParticleSystem.SizeOverLifetimeModule size = burst.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            burst.Play();
            return burst;
        }

        private TrailRenderer CreateTrail(string trailName, float side)
        {
            var go = new GameObject(trailName);
            go.transform.SetParent(transform, false);

            // Behind the rear wheels, near the bumper.
            Vector3 rear = kart.rearLeft != null ? transform.InverseTransformPoint(kart.rearLeft.transform.position) : new Vector3(-1f, 0.6f, -1.7f);
            float radius = kart.rearLeft != null ? kart.rearLeft.radius : 0.6f;
            go.transform.localPosition = new Vector3(Mathf.Abs(rear.x) * 0.55f * side, rear.y, rear.z - radius - 0.4f);

            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = glowMaterial;
            trail.time = 0.35f;
            trail.minVertexDistance = 0.2f;
            trail.widthCurve = AnimationCurve.Linear(0f, 0.45f, 1f, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.6f, 0.95f, 1f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.4f), new GradientColorKey(new Color(1f, 0.2f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            return trail;
        }

        private static ParticleSystem.MinMaxGradient FadeGradient(Color color, float fadeIn)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static Transform FindChild(Transform parent, string childName)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }
            return null;
        }

        // ---------- Materials ----------

        private void EnsureMaterials()
        {
            if (dustMaterial != null && glowMaterial != null)
            {
                return;
            }

            Texture2D soft = CreateSoftCircle(64);
            if (dustMaterial == null) dustMaterial = CreateParticleMaterial(soft, additive: false);
            if (glowMaterial == null) glowMaterial = CreateParticleMaterial(soft, additive: true);
        }

        /// <summary>Radial soft dot, white with alpha falloff.</summary>
        public static Texture2D CreateSoftCircle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, d * d * (3f - 2f * d));
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>URP Particles/Unlit, transparent; alpha-blended or additive.</summary>
        public static Material CreateParticleMaterial(Texture2D texture, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            ConfigureParticleMaterial(material, texture, additive);
            return material;
        }

        public static void ConfigureParticleMaterial(Material material, Texture2D texture, bool additive)
        {
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
