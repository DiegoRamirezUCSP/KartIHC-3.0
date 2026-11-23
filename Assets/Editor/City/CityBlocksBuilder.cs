using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using KartIHC.UI;
using Id = KartIHC.EditorTools.CityMaterials.Id;
using Random = System.Random;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Etapa 2 de la ciudad: manzanas procedurales alrededor del anillo, una plaza baja en
    /// el centro, un skyline lejano y la comisaría (City_PoliceStation). El suelo urbano es de P3.
    ///
    /// Respeta CityZones: edificios solo a r ≥ 39.5 (calle de las patrullas en r 36), nada en
    /// el carril norte ni en la zona de la comisaría, y piezas del centro de ≤ 4.5 m.
    /// Nada lleva Collider. EscapePoliceStationGenerator detecta City_PoliceStation por nombre
    /// y no crea sus cajas de comisaría (solo conserva carril, muros y meta de Solo).
    ///
    /// Rendimiento: todas las cáscaras de edificio en una malla (una submalla por material),
    /// detalles (cornisas, petos, equipos de azotea) por manzana con LOD de recorte (~80 m)
    /// y sin sombras, skyline sin sombras.
    /// </summary>
    public static class CityBlocksBuilder
    {
        public const string BlocksSection = "City_Blocks";
        public const string CenterSection = "City_Center";
        public const string SkylineSection = "City_Skyline";
        public const string StationSection = "City_PoliceStation"; // lo busca EscapePoliceStationGenerator
        public const string MapSection = "City_Map";                // huellas para el fondo de EscapeMinimap

        // ===== Valores para ajustar rápido (vuelve a correr "2 Blocks and Police Station") =====
        private const int Seed = 1127;
        private const float StreetPitch = 34f;      // manzana 24 m + calle 10 m
        private const float BlockSize = 24f;
        private const float StreetHalfWidth = 5f;
        private const int BlockSlots = 4;            // manzanas por lado y cuadrante (hasta ~131 m)
        private const float SidewalkWidth = 1.5f;
        private const float InnerGap = 0.5f;         // medio callejón entre edificios de una manzana
        private const float PlinthHeight = 0.15f;
        private const float GroundY = -0.06f;        // base enterrada de muros y zócalos (el suelo urbano está en P3, y 0.03)
        private const float DetailCullHeight = 0.25f; // ~80 m para una manzana de 24 m
        private const int SkylineCount = 70;
        private const float SkylineMinDistance = 150f;
        private const float SkylineMaxDistance = 240f;

        private static readonly Id[] ShellMaterials =
            { Id.FacadeConcrete, Id.FacadeBrick, Id.FacadeGlass, Id.FacadeStucco, Id.Storefront, Id.Roof, Id.Sidewalk };
        private const int SubStorefront = 4, SubRoof = 5, SubSidewalk = 6;

        [MenuItem(CityGen.MenuRoot + "2 Blocks and Police Station", priority = 302)]
        public static void Build()
        {
            if (!CityGen.EnsureEscapeScene())
            {
                return;
            }

            EnvGen.BeginUndo("City Blocks and Police Station");
            CityZones.RefreshRoute(); // si hay ruta por la ciudad, sus calles quedan libres
            var stats = new Stats();
            try
            {
                CityMaterials.BuildAll();
                CityGen.ReplacePrefabSections(
                    (BlocksSection, section => BuildBlocks(section, stats)),
                    (CenterSection, section => BuildCenter(section, stats)),
                    (SkylineSection, section => BuildSkyline(section, stats)),
                    (StationSection, section => BuildStation(section, stats)),
                    (MapSection, section => BuildMap(section, stats)));
                CityGen.EnsureSingleSceneInstance();
            }
            finally
            {
                EnvGen.EndUndo();
            }

            CityGen.MarkSceneDirty();
            AssetDatabase.SaveAssets();
            Debug.Log($"[City] Manzanas: {stats.Blocks} manzanas, {stats.Buildings} edificios, " +
                      $"{stats.Skyline} torres de skyline, {stats.Triangles} triángulos (LOD0). " +
                      "Guarda la escena y corre 'Validate Drone Sightlines'.");
        }

        private sealed class Stats
        {
            public int Blocks, Buildings, Skyline, Triangles;
            public readonly List<CityFootprints.Footprint> Footprints = new List<CityFootprints.Footprint>();
        }

        // ---------- Manzanas ----------

        private static void BuildBlocks(Transform section, Stats stats)
        {
            var random = new Random(Seed);
            var shells = new CityMeshBuilder(ShellMaterials.Length);
            var detailsRoot = new GameObject("City_BlockDetails");
            detailsRoot.transform.SetParent(section, false);

            for (int bx = -BlockSlots; bx < BlockSlots; bx++)
            {
                for (int bz = -BlockSlots; bz < BlockSlots; bz++)
                {
                    float x0 = bx * StreetPitch + StreetHalfWidth;
                    float z0 = bz * StreetPitch + StreetHalfWidth;
                    var block = new Rect(x0, z0, BlockSize, BlockSize);
                    var details = new CityMeshBuilder(1);
                    if (BuildBlock(block, random, shells, details, stats))
                    {
                        stats.Blocks++;
                        if (!details.IsEmpty)
                        {
                            string name = $"City_BlockDetail_{bx + BlockSlots}_{bz + BlockSlots}";
                            stats.Triangles += details.TriangleCount;
                            Mesh detailMesh = CityGen.SaveMesh(details.ToMesh(name), name);
                            GameObject go = CityGen.CreateRenderer(detailsRoot.transform, name, detailMesh,
                                new[] { CityMaterials.Get(Id.Trim) }, ShadowCastingMode.Off);
                            CityGen.AddCullingLod(go, DetailCullHeight);
                        }
                    }
                }
            }

            stats.Triangles += shells.TriangleCount;
            Mesh shellMesh = CityGen.SaveMesh(shells.ToMesh("City_BuildingShells"), "City_BuildingShells");
            CityGen.CreateRenderer(section, "City_BuildingShells", shellMesh, CityMaterials.Get(ShellMaterials),
                ShadowCastingMode.On);
        }

        /// <summary>Una manzana de 2×2 solares de 12 m. Devuelve false si ningún solar cumple las zonas libres.</summary>
        private static bool BuildBlock(Rect block, Random random, CityMeshBuilder shells, CityMeshBuilder details, Stats stats)
        {
            float half = BlockSize * 0.5f;
            var lots = new Rect[2, 2];
            var allowed = new bool[2, 2];
            int allowedCount = 0;
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    lots[i, j] = new Rect(block.xMin + i * half, block.yMin + j * half, half, half);
                    allowed[i, j] = CityZones.BuildingLotAllowed(lots[i, j]);
                    if (allowed[i, j]) allowedCount++;
                }
            }
            if (allowedCount == 0)
            {
                return false;
            }

            // Aceras (zócalo) bajo cada solar permitido.
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    if (allowed[i, j])
                    {
                        shells.Box(SubSidewalk, lots[i, j], GroundY - 0.02f, PlinthHeight, 1f / CityMaterials.SidewalkTile);
                        stats.Footprints.Add(new CityFootprints.Footprint { Area = lots[i, j], Kind = CityFootprints.Kind.Block });
                    }

            float distance = block.center.magnitude;
            var footprints = new List<Rect>();
            if (allowedCount == 4)
            {
                double layout = random.NextDouble();
                if (layout < 0.15)
                {
                    footprints.Add(block);
                }
                else if (layout < 0.45)
                {
                    bool alongX = random.NextDouble() < 0.5;
                    footprints.Add(alongX ? new Rect(block.xMin, block.yMin, BlockSize, half) : new Rect(block.xMin, block.yMin, half, BlockSize));
                    footprints.Add(alongX ? new Rect(block.xMin, block.yMin + half, BlockSize, half) : new Rect(block.xMin + half, block.yMin, half, BlockSize));
                }
                else
                {
                    foreach (Rect lot in lots) footprints.Add(lot);
                }
            }
            else
            {
                for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                        if (allowed[i, j]) footprints.Add(lots[i, j]);
            }

            foreach (Rect area in footprints)
            {
                Rect footprint = Inset(area, block);
                if (footprint.width < 6f || footprint.height < 6f)
                {
                    continue;
                }
                BuildBuilding(footprint, distance, area.width >= BlockSize - 0.01f && area.height >= BlockSize - 0.01f,
                    random, shells, details);
                stats.Buildings++;
            }
            return true;
        }

        /// <summary>Retira la acera en los bordes de la manzana y medio callejón en los interiores; ancho múltiplo de 3 m.</summary>
        private static Rect Inset(Rect area, Rect block)
        {
            float xMin = area.xMin + (Mathf.Approximately(area.xMin, block.xMin) ? SidewalkWidth : InnerGap);
            float xMax = area.xMax - (Mathf.Approximately(area.xMax, block.xMax) ? SidewalkWidth : InnerGap);
            float zMin = area.yMin + (Mathf.Approximately(area.yMin, block.yMin) ? SidewalkWidth : InnerGap);
            float zMax = area.yMax - (Mathf.Approximately(area.yMax, block.yMax) ? SidewalkWidth : InnerGap);
            float width = Mathf.Floor((xMax - xMin) / CityMaterials.BayWidth) * CityMaterials.BayWidth;
            float depth = Mathf.Floor((zMax - zMin) / CityMaterials.BayWidth) * CityMaterials.BayWidth;
            float cx = (xMin + xMax) * 0.5f, cz = (zMin + zMax) * 0.5f;
            return new Rect(cx - width * 0.5f, cz - depth * 0.5f, width, depth);
        }

        private static void BuildBuilding(Rect footprint, float distance, bool wholeBlock, Random random,
            CityMeshBuilder shells, CityMeshBuilder details)
        {
            // Más bajos junto al anillo, más altos hacia fuera.
            int floors = distance < 60f ? random.Next(1, 4)
                : distance < 90f ? random.Next(2, 7)
                : random.Next(4, 10);
            if (wholeBlock) floors += 2;

            int style = random.Next(4);
            float baseY = PlinthHeight;
            float shopTop = baseY + CityMaterials.StorefrontHeight;
            float top = shopTop + floors * CityMaterials.FloorHeight;
            float uOffset = random.Next(4) * 0.25f;
            float vOffset = random.Next(4) * 0.25f;

            shells.Walls(SubStorefront, footprint, baseY, shopTop,
                1f / CityMaterials.StorefrontTileWidth, 1f / CityMaterials.StorefrontHeight, random.Next(2) * 0.5f, 0f);
            shells.Walls(style, footprint, shopTop, top,
                1f / CityMaterials.FacadeTileWidth, 1f / CityMaterials.FacadeTileHeight, uOffset, vOffset);
            shells.Top(SubRoof, footprint, top, 1f / CityMaterials.RoofTile);

            AddCornice(details, footprint, shopTop);
            AddParapet(details, footprint, top, 0.9f, 0);
            AddRoofProps(details, footprint, top, floors, random);
        }

        private static void AddCornice(CityMeshBuilder details, Rect footprint, float y)
        {
            Rect ledge = Expand(footprint, 0.18f);
            details.Box(0, ledge, y, y + 0.28f, 1f / CityMaterials.TrimTile);
        }

        private static void AddRoofProps(CityMeshBuilder details, Rect footprint, float y, int floors, Random random)
        {
            float uv = 1f / CityMaterials.TrimTile;
            Rect usable = Expand(footprint, -1.5f);
            if (usable.width < 2.5f || usable.height < 2.5f) return;

            int units = random.Next(1, 3);
            for (int i = 0; i < units; i++)
            {
                float x = Mathf.Lerp(usable.xMin, usable.xMax - 1.6f, (float)random.NextDouble());
                float z = Mathf.Lerp(usable.yMin, usable.yMax - 1.2f, (float)random.NextDouble());
                details.Box(0, new Rect(x, z, 1.6f, 1.2f), y, y + 1.0f, uv); // climatización
            }
            if (floors >= 4 && random.NextDouble() < 0.5)
            {
                float x = Mathf.Lerp(usable.xMin, usable.xMax - 2.2f, (float)random.NextDouble());
                float z = Mathf.Lerp(usable.yMin, usable.yMax - 2.2f, (float)random.NextDouble());
                details.Box(0, new Rect(x, z, 2.2f, 2.2f), y + 1.2f, y + 3.4f, uv); // depósito de agua
                details.Box(0, new Rect(x + 0.2f, z + 0.2f, 0.3f, 0.3f), y, y + 1.2f, uv);
                details.Box(0, new Rect(x + 1.7f, z + 1.7f, 0.3f, 0.3f), y, y + 1.2f, uv);
            }
        }

        // ---------- Centro ----------

        private static void BuildCenter(Transform section, Stats stats)
        {
            Id[] materials = { Id.Sidewalk, Id.Storefront, Id.Trim };
            var builder = new CityMeshBuilder(materials.Length);
            builder.Disc(0, new Vector3(0f, 0.02f, 0f), CityZones.CenterFreeRadius, 48, 1f / CityMaterials.SidewalkTile);

            // Cuatro quioscos bajos en las diagonales.
            const float kioskHeight = 4.0f, capHeight = 0.3f;
            foreach (Vector2 corner in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(-1, -1), new Vector2(1, -1) })
            {
                Vector2 c = corner * 7.5f;
                var footprint = new Rect(c.x - 2.5f, c.y - 2.5f, 5f, 5f);
                Rect cap = Expand(footprint, 0.25f);
                RequireCenter(cap, kioskHeight + capHeight);
                builder.Walls(1, footprint, 0.02f, kioskHeight,
                    1f / CityMaterials.StorefrontTileWidth, 1f / CityMaterials.StorefrontHeight);
                builder.Box(2, cap, kioskHeight, kioskHeight + capHeight, 1f / CityMaterials.TrimTile);
                builder.Bottom(2, cap, kioskHeight, 1f / CityMaterials.TrimTile);
            }

            // Monumento: pedestal y obelisco.
            var plinth = new Rect(-2f, -2f, 4f, 4f);
            var obelisk = new Rect(-0.45f, -0.45f, 0.9f, 0.9f);
            RequireCenter(plinth, 0.62f);
            RequireCenter(obelisk, 4.4f);
            builder.Box(2, plinth, 0.02f, 0.62f, 1f / CityMaterials.TrimTile);
            builder.Box(2, obelisk, 0.62f, 4.4f, 1f / CityMaterials.TrimTile);

            stats.Triangles += builder.TriangleCount;
            Mesh mesh = CityGen.SaveMesh(builder.ToMesh("City_Center"), "City_Center");
            CityGen.CreateRenderer(section, "City_CenterPlaza", mesh, CityMaterials.Get(materials), ShadowCastingMode.On);
        }

        private static void RequireCenter(Rect footprint, float height)
        {
            if (!CityZones.CenterPieceAllowed(footprint, height))
            {
                throw new InvalidOperationException(
                    $"[City] Pieza del centro fuera de límites: {footprint} alto {height} (máx. r {CityZones.CenterFreeRadius}, h {CityZones.CenterMaxHeight}).");
            }
        }

        // ---------- Skyline ----------

        private static void BuildSkyline(Transform section, Stats stats)
        {
            Id[] materials = { Id.FacadeConcrete, Id.FacadeGlass, Id.Roof };
            var builder = new CityMeshBuilder(materials.Length);
            var random = new Random(Seed + 7);
            var placed = new List<Rect>();
            for (int attempt = 0; attempt < SkylineCount * 6 && placed.Count < SkylineCount; attempt++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                float distance = Mathf.Lerp(SkylineMinDistance, SkylineMaxDistance, (float)random.NextDouble());
                float width = 3f * random.Next(5, 11);
                float depth = 3f * random.Next(5, 11);
                Vector2 c = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                var footprint = new Rect(c.x - width * 0.5f, c.y - depth * 0.5f, width, depth);
                if (placed.Exists(r => r.Overlaps(Expand(footprint, 4f))))
                {
                    continue;
                }
                placed.Add(footprint);

                int floors = random.Next(6, 20);
                float top = floors * CityMaterials.FloorHeight;
                int style = random.NextDouble() < 0.5 ? 0 : 1;
                builder.Walls(style, footprint, GroundY - 0.02f, top,
                    1f / CityMaterials.FacadeTileWidth, 1f / CityMaterials.FacadeTileHeight, random.Next(4) * 0.25f, 0f);
                builder.Top(2, footprint, top, 1f / CityMaterials.RoofTile);
            }

            stats.Skyline = placed.Count;
            stats.Triangles += builder.TriangleCount;
            Mesh mesh = CityGen.SaveMesh(builder.ToMesh("City_Skyline"), "City_Skyline");
            CityGen.CreateRenderer(section, "City_SkylineTowers", mesh, CityMaterials.Get(materials), ShadowCastingMode.Off);
        }

        // ---------- Comisaría ----------

        /// <summary>
        /// Misma ubicación que la de EscapePoliceStationGenerator: fachada sur en z 59.5, cocheras
        /// en x ±6.2 (las patrullas aparecen en z 56 y salen hacia el sur por el carril x ±4.5).
        /// </summary>
        private static void BuildStation(Transform section, Stats stats)
        {
            Id[] materials =
            {
                Id.FacadeConcrete, Id.FacadeGlass, Id.Roof, Id.Trim, Id.Sidewalk,
                Id.PoliceBlue, Id.Garage, Id.PoliceSign, Id.EntranceGlass, Id.BeaconRed, Id.BeaconBlue,
            };
            const int concrete = 0, glass = 1, roof = 2, trim = 3, sidewalk = 4, blue = 5, garage = 6, sign = 7,
                entrance = 8, beaconRed = 9, beaconBlue = 10;
            var b = new CityMeshBuilder(materials.Length);
            float fu = 1f / CityMaterials.FacadeTileWidth, fv = 1f / CityMaterials.FacadeTileHeight;
            float trimUv = 1f / CityMaterials.TrimTile;

            // Explanada delante, por encima del suelo urbano de P3 (y 0.03).
            b.Top(sidewalk, Rect.MinMaxRect(-14f, 49.9f, 14f, 59.5f), 0.045f, 1f / CityMaterials.SidewalkTile);

            // Edificio principal de 2 plantas.
            var main = Rect.MinMaxRect(-13f, 59.5f, 13f, 75f);
            stats.Footprints.Add(new CityFootprints.Footprint { Area = main, Kind = CityFootprints.Kind.Station });
            const float mainTop = 8f;
            RequireStation(main);
            b.Walls(concrete, main, GroundY - 0.02f, mainTop, fu, fv, 0f, 0.5f);
            b.Top(roof, main, mainTop, 1f / CityMaterials.RoofTile);
            AddParapet(b, main, mainTop, 0.8f, trim);

            // Fachada sur: banda azul con cocheras, entrada iluminada y marquesina.
            const float front = 59.5f;
            b.Wall(blue, new Vector3(-13f, 0f, front - 0.03f), new Vector3(13f, 0f, front - 0.03f), 4.2f, Vector2.zero, Vector2.one);
            b.Wall(blue, new Vector3(-13f, 7.2f, front - 0.03f), new Vector3(13f, 7.2f, front - 0.03f), 0.8f, Vector2.zero, Vector2.one);
            b.Wall(garage, new Vector3(-9.8f, 0f, front - 0.06f), new Vector3(-2.6f, 0f, front - 0.06f), 3.6f, Vector2.zero, Vector2.one);
            b.Wall(garage, new Vector3(2.6f, 0f, front - 0.06f), new Vector3(9.8f, 0f, front - 0.06f), 3.6f, Vector2.zero, Vector2.one);
            b.Wall(entrance, new Vector3(-1.6f, 0f, front - 0.06f), new Vector3(1.6f, 0f, front - 0.06f), 2.8f, Vector2.zero, Vector2.one);
            b.Wall(sign, new Vector3(-4.8f, 4.5f, front - 0.06f), new Vector3(4.8f, 4.5f, front - 0.06f), 2.4f, Vector2.zero, Vector2.one);
            var canopy = Rect.MinMaxRect(-2.4f, 57.8f, 2.4f, front);
            b.Box(trim, canopy, 3.1f, 3.35f, trimUv);
            b.Bottom(trim, canopy, 3.1f, trimUv);

            // Torre con rótulo y balizas roja/azul.
            var tower = Rect.MinMaxRect(-4f, 61f, 4f, 69f);
            const float towerTop = 14f;
            b.Walls(glass, tower, mainTop, towerTop, fu, fv, 0.25f, 0f);
            b.Top(roof, tower, towerTop, 1f / CityMaterials.RoofTile);
            AddParapet(b, tower, towerTop, 0.6f, trim);
            b.Wall(sign, new Vector3(-3.6f, 9.2f, 61f - 0.03f), new Vector3(3.6f, 9.2f, 61f - 0.03f), 1.8f, Vector2.zero, Vector2.one);
            b.Box(beaconRed, Rect.MinMaxRect(-1.8f, 64.7f, -0.6f, 65.3f), towerTop, towerTop + 0.7f, 1f);
            b.Box(beaconBlue, Rect.MinMaxRect(0.6f, 64.7f, 1.8f, 65.3f), towerTop, towerTop + 0.7f, 1f);

            stats.Triangles += b.TriangleCount;
            Mesh mesh = CityGen.SaveMesh(b.ToMesh("City_PoliceStation"), "City_PoliceStation");
            CityGen.CreateRenderer(section, "City_PoliceStationBuilding", mesh, CityMaterials.Get(materials), ShadowCastingMode.On);
        }

        private static void AddParapet(CityMeshBuilder builder, Rect footprint, float y, float height, int submesh)
        {
            const float t = 0.3f;
            float uv = 1f / CityMaterials.TrimTile;
            builder.Box(submesh, Rect.MinMaxRect(footprint.xMin, footprint.yMin, footprint.xMax, footprint.yMin + t), y, y + height, uv);
            builder.Box(submesh, Rect.MinMaxRect(footprint.xMin, footprint.yMax - t, footprint.xMax, footprint.yMax), y, y + height, uv);
            builder.Box(submesh, Rect.MinMaxRect(footprint.xMin, footprint.yMin + t, footprint.xMin + t, footprint.yMax - t), y, y + height, uv);
            builder.Box(submesh, Rect.MinMaxRect(footprint.xMax - t, footprint.yMin + t, footprint.xMax, footprint.yMax - t), y, y + height, uv);
        }

        private static void RequireStation(Rect footprint)
        {
            if (footprint.Overlaps(CityZones.NorthCorridor) || !CityZones.StationArea.Contains(footprint.min) ||
                !CityZones.StationArea.Contains(footprint.max))
            {
                throw new InvalidOperationException($"[City] La comisaría {footprint} invade el carril norte o sale de su zona.");
            }
        }

        // ---------- Minimapa ----------

        /// <summary>Huellas de manzanas y comisaría para EscapeMinimap (sin renderer ni collider).</summary>
        private static void BuildMap(Transform section, Stats stats)
        {
            section.gameObject.AddComponent<CityFootprints>().SetFootprints(stats.Footprints);
        }

        // ---------- Utilidades ----------

        private static Rect Expand(Rect rect, float amount) =>
            Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
    }
}
