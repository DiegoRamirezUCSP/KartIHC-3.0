using System;
using System.Collections.Generic;
using UnityEngine;

namespace KartIHC.UI
{
    /// <summary>
    /// Huellas en planta (XZ) de la ciudad generada, para el fondo de EscapeMinimap.
    /// Lo rellena KartIHC/City/2 Blocks and Police Station; sin renderer ni collider.
    /// Rect: x = X del mundo, y = Z del mundo.
    /// </summary>
    public sealed class CityFootprints : MonoBehaviour
    {
        public enum Kind { Block, Station }

        [Serializable]
        public struct Footprint
        {
            public Rect Area;
            public Kind Kind;
        }

        [SerializeField] private List<Footprint> footprints = new List<Footprint>();

        public IReadOnlyList<Footprint> Footprints => footprints;

        public void SetFootprints(List<Footprint> values) => footprints = new List<Footprint>(values);
    }
}
