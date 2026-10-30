using UnityEngine;

namespace KartIHC.Environment
{
    /// <summary>
    /// Light water animation: scrolls the material's base/normal map offset (URP Lit
    /// uses one tiling/offset for both) in two slowly changing directions.
    /// Works on a runtime copy of the material, so the asset is never modified.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class WaterRipples : MonoBehaviour
    {
        [SerializeField] private Vector2 flow = new Vector2(0.015f, 0.01f);
        [SerializeField] private float wobble = 0.006f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private Material material;

        private void Start()
        {
            material = GetComponent<Renderer>().material;   // instance
        }

        private void Update()
        {
            if (material == null)
            {
                return;
            }

            float t = Time.time;
            Vector2 offset = flow * t + new Vector2(Mathf.Sin(t * 0.37f), Mathf.Cos(t * 0.29f)) * wobble;
            material.SetTextureOffset(BaseMapId, offset);
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
