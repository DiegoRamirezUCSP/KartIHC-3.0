using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Texturas procedurales y materiales compartidos (URP Lit) de la ciudad.
    /// Las fachadas son tiles que se repiten (4 vanos × 4 plantas = 12 m × 14 m), así las
    /// ventanas no cuestan geometría; las encendidas van en un mapa de emisión que el bloom
    /// del ambiente (umbral 1) hace brillar. La suavidad va en el alfa del albedo
    /// (vidrio brillante, muro mate).
    /// </summary>
    public static class CityMaterials
    {
        public enum Id
        {
            FacadeConcrete, FacadeBrick, FacadeGlass, FacadeStucco, Storefront,
            Roof, Trim, Sidewalk, Asphalt,
            PoliceBlue, Garage, PoliceSign, EntranceGlass, BeaconRed, BeaconBlue,
        }

        // Escalas UV (metros → repeticiones del tile).
        public const float FacadeTileWidth = 12f;   // 4 vanos de 3 m
        public const float FacadeTileHeight = 14f;  // 4 plantas de 3.5 m
        public const float FloorHeight = 3.5f;
        public const float BayWidth = 3f;
        public const float StorefrontTileWidth = 6f; // 2 locales de 3 m
        public const float StorefrontHeight = 4.2f;
        public const float SidewalkTile = 2f;
        public const float AsphaltTile = 6f;
        public const float RoofTile = 4f;
        public const float TrimTile = 2f;

        // ===== Valores para ajustar rápido (vuelve a correr "2 Blocks and Police Station") =====
        private const float WindowEmission = 1.6f;      // HDR: >1 entra en el bloom
        private const float StorefrontEmission = 1.2f;  // locales de planta baja: -40 % (con 2.0 se quemaban con el bloom)
        private const float BeaconEmission = 4f;
        private const float SignEmission = 2.2f;
        private static readonly float[] LitFraction = { 0.35f, 0.4f, 0.3f, 0.4f }; // por estilo de fachada

        private const int Seed = 2711;
        private static readonly Dictionary<Id, Material> cache = new Dictionary<Id, Material>();

        /// <summary>Genera (o regenera) todas las texturas y materiales. Llamar una vez por construcción.</summary>
        public static void BuildAll()
        {
            cache.Clear();
            CityGen.EnsureFolders();

            string[] facadeNames = { "Concrete", "Brick", "Glass", "Stucco" };
            for (int style = 0; style < 4; style++)
            {
                GenerateFacade(style, out Color[] albedo, out Color[] emission);
                Texture2D a = SaveAlbedo("Facade_" + facadeNames[style], albedo, 512, 512);
                Texture2D e = SaveEmission("Facade_" + facadeNames[style] + "_Emission", emission, 512, 512);
                cache[(Id)style] = Lit("Facade_" + facadeNames[style], a, e, Color.white * WindowEmission);
            }

            GenerateStorefront(out Color[] shopAlbedo, out Color[] shopEmission);
            cache[Id.Storefront] = Lit("Storefront",
                SaveAlbedo("Storefront", shopAlbedo, 512, 256), SaveEmission("Storefront_Emission", shopEmission, 512, 256),
                Color.white * StorefrontEmission);

            cache[Id.Roof] = Lit("Roof", SaveAlbedo("Roof", Noise(256, CityGen.Hex("#55565B"), 0.10f, 0.1f, 8, 11), 256, 256), null, Color.black);
            cache[Id.Trim] = Lit("Trim", SaveAlbedo("Trim", Noise(128, CityGen.Hex("#9C9893"), 0.06f, 0.15f, 4, 12), 128, 128), null, Color.black);
            cache[Id.Sidewalk] = Lit("Sidewalk", SaveAlbedo("Sidewalk", GenerateSidewalk(), 256, 256), null, Color.black);
            cache[Id.Asphalt] = Lit("Asphalt", SaveAlbedo("Asphalt", GenerateAsphalt(), 512, 512), null, Color.black);
            cache[Id.Garage] = Lit("Garage", SaveAlbedo("Garage", GenerateGarage(), 256, 256), null, Color.black);

            GenerateSign("POLICÍA", out Color[] signAlbedo, out Color[] signEmission);
            cache[Id.PoliceSign] = Lit("PoliceSign",
                SaveAlbedo("PoliceSign", signAlbedo, 512, 128, TextureWrapMode.Clamp),
                SaveEmission("PoliceSign_Emission", signEmission, 512, 128, TextureWrapMode.Clamp),
                Color.white * SignEmission);

            cache[Id.PoliceBlue] = Plain("PoliceBlue", CityGen.Hex("#1F3F8F"), 0.35f, Color.black);
            cache[Id.EntranceGlass] = Plain("EntranceGlass", CityGen.Hex("#3A3020"), 0.8f, CityGen.Hex("#FFD7A0") * 1.4f);
            cache[Id.BeaconRed] = Plain("BeaconRed", CityGen.Hex("#5A0A06"), 0.6f, CityGen.Hex("#FF2A1A") * BeaconEmission);
            cache[Id.BeaconBlue] = Plain("BeaconBlue", CityGen.Hex("#0A1A5A"), 0.6f, CityGen.Hex("#2A5BFF") * BeaconEmission);
            AssetDatabase.SaveAssets();
        }

        public static Material Get(Id id)
        {
            if (!cache.TryGetValue(id, out Material material) || material == null)
            {
                throw new System.InvalidOperationException("[City] Llama a CityMaterials.BuildAll() antes de Get().");
            }
            return material;
        }

        public static Material[] Get(params Id[] ids)
        {
            var result = new Material[ids.Length];
            for (int i = 0; i < ids.Length; i++) result[i] = Get(ids[i]);
            return result;
        }

        // ---------- Materiales ----------

        private static Material Lit(string name, Texture2D albedo, Texture2D emission, Color emissionColor)
        {
            Material material = CityGen.GetOrCreateMaterial($"{CityGen.MaterialsFolder}/Mat_City{name}.mat",
                "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", albedo);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 1f); // escala: la suavidad real va en el alfa del albedo
            material.SetFloat("_SmoothnessTextureChannel", 1f);
            material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            SetEmission(material, emission, emissionColor);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material Plain(string name, Color color, float smoothness, Color emissionColor)
        {
            Material material = CityGen.GetOrCreateMaterial($"{CityGen.MaterialsFolder}/Mat_City{name}.mat",
                "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", null);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            SetEmission(material, null, emissionColor);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetEmission(Material material, Texture2D emission, Color emissionColor)
        {
            bool on = emissionColor.maxColorComponent > 0f;
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", on ? emissionColor : Color.black);
            if (on)
            {
                material.EnableKeyword("_EMISSION");
                // URP vuelve a calcular _EMISSION a partir de esta bandera al validar el material.
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        private static Texture2D SaveAlbedo(string name, Color[] pixels, int width, int height,
            TextureWrapMode wrap = TextureWrapMode.Repeat) =>
            CityGen.SaveTexture(pixels, width, height, "City_" + name, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = wrap;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.Compressed;
            });

        private static Texture2D SaveEmission(string name, Color[] pixels, int width, int height,
            TextureWrapMode wrap = TextureWrapMode.Repeat) =>
            CityGen.SaveTexture(pixels, width, height, "City_" + name, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.wrapMode = wrap;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.Compressed;
            });

        // ---------- Texturas ----------

        private static readonly Color GlassDark = CityGen.Hex("#1A222E");
        private static readonly Color GlassSky = CityGen.Hex("#3A4760");
        private static readonly Color[] LitColors =
        {
            CityGen.Hex("#FFD49A"), CityGen.Hex("#FFC98A"), CityGen.Hex("#FFE2B8"), CityGen.Hex("#D2E4FF"), CityGen.Hex("#9FC2FF"),
        };

        /// <summary>Tile de fachada 512×512 = 4×4 celdas de 128 px (vano de 3 m × planta de 3.5 m).</summary>
        private static void GenerateFacade(int style, out Color[] albedo, out Color[] emission)
        {
            const int size = 512, cell = 128;
            albedo = new Color[size * size];
            emission = new Color[size * size];
            var random = new System.Random(Seed + style * 31);
            Color wall = style switch
            {
                0 => CityGen.Hex("#8E8B86"),
                1 => CityGen.Hex("#7B4836"),
                2 => CityGen.Hex("#4E555E"),
                _ => CityGen.Hex("#B5A07E"),
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = EnvGen.TileFbm(x / (float)size, y / (float)size, 8, 4, 40 + style) - 0.5f;
                    Color c = wall * (1f + n * 0.18f);
                    if (style == 1)
                    {
                        // Ladrillo: hiladas de 8 px, juntas claras, aparejo alterno.
                        int row = y / 8;
                        int offset = row % 2 == 0 ? 0 : 8;
                        bool mortar = y % 8 == 0 || (x + offset) % 16 == 0;
                        if (mortar) c = CityGen.Hex("#A89C8E") * (1f + n * 0.1f);
                        else c *= 0.92f + 0.16f * Hash01(row, (x + offset) / 16, 7);
                    }
                    c.a = 0.15f;
                    albedo[y * size + x] = c;
                }
            }

            for (int cy = 0; cy < 4; cy++)
            {
                for (int cx = 0; cx < 4; cx++)
                {
                    int x0 = cx * cell, y0 = cy * cell;
                    bool lit = random.NextDouble() < LitFraction[style];
                    Color litColor = LitColors[random.Next(LitColors.Length)] * (0.65f + 0.35f * (float)random.NextDouble());
                    float blind = random.NextDouble() < 0.35 ? 0.25f + 0.4f * (float)random.NextDouble() : 0f;

                    switch (style)
                    {
                        case 0: // Hormigón: ventana con marco claro y alféizar.
                            FillRect(albedo, size, x0 + 20, y0 + 24, x0 + 108, y0 + 104, CityGen.Hex("#B9B5AE"), 0.2f);
                            Window(albedo, emission, size, x0 + 26, y0 + 30, x0 + 102, y0 + 100, lit, litColor, blind);
                            FillRect(albedo, size, x0 + 16, y0 + 18, x0 + 112, y0 + 24, CityGen.Hex("#C8C3BB"), 0.2f);
                            break;
                        case 1: // Ladrillo: ventana alta con marco blanco y dintel.
                            FillRect(albedo, size, x0 + 28, y0 + 20, x0 + 100, y0 + 108, CityGen.Hex("#E6E1D8"), 0.25f);
                            Window(albedo, emission, size, x0 + 34, y0 + 26, x0 + 94, y0 + 102, lit, litColor, blind);
                            FillRect(albedo, size, x0 + 30, y0 + 39 + 24, x0 + 98, y0 + 41 + 24, CityGen.Hex("#E6E1D8"), 0.25f);
                            FillRect(albedo, size, x0 + 24, y0 + 108, x0 + 104, y0 + 116, CityGen.Hex("#6A3A2A"), 0.15f);
                            break;
                        case 2: // Vidrio: muro cortina con montantes, antepecho oscuro.
                            Window(albedo, emission, size, x0, y0 + 26, x0 + cell, y0 + cell, lit, litColor, blind * 0.5f);
                            for (int m = 0; m < cell; m += 32)
                                FillRect(albedo, size, x0 + m, y0 + 26, x0 + m + 3, y0 + cell, CityGen.Hex("#2A2F36"), 0.5f);
                            FillRect(albedo, size, x0, y0 + 22, x0 + cell, y0 + 26, CityGen.Hex("#2A2F36"), 0.5f);
                            break;
                        default: // Estuco: ventana con balcón de barandilla oscura.
                            Window(albedo, emission, size, x0 + 28, y0 + 30, x0 + 100, y0 + 104, lit, litColor, blind);
                            FillRect(albedo, size, x0 + 14, y0 + 18, x0 + 114, y0 + 22, CityGen.Hex("#3B3A3A"), 0.3f);
                            FillRect(albedo, size, x0 + 14, y0 + 44, x0 + 114, y0 + 47, CityGen.Hex("#3B3A3A"), 0.3f);
                            for (int b = 16; b < 114; b += 8)
                                FillRect(albedo, size, x0 + b, y0 + 22, x0 + b + 2, y0 + 44, CityGen.Hex("#3B3A3A"), 0.3f);
                            break;
                    }
                }
            }
        }

        /// <summary>Vidrio con reflejo de cielo (apagado) o interior iluminado con estor opcional (encendido).</summary>
        private static void Window(Color[] albedo, Color[] emission, int size, int x0, int y0, int x1, int y1,
            bool lit, Color litColor, float blind)
        {
            for (int y = y0; y < y1; y++)
            {
                float t = (y - y0) / (float)Mathf.Max(1, y1 - y0 - 1);
                for (int x = x0; x < x1; x++)
                {
                    int i = y * size + x;
                    if (lit)
                    {
                        bool covered = t > 1f - blind;
                        float falloff = Mathf.Lerp(1f, 0.7f, t);
                        Color glow = covered ? litColor * 0.45f : litColor * falloff;
                        albedo[i] = new Color(glow.r * 0.55f, glow.g * 0.55f, glow.b * 0.55f, 0.6f);
                        emission[i] = new Color(glow.r, glow.g, glow.b, 1f);
                    }
                    else
                    {
                        Color c = Color.Lerp(GlassDark, GlassSky, t * t);
                        c.a = 0.82f;
                        albedo[i] = c;
                    }
                }
            }
        }

        /// <summary>Tile de bajos comerciales 512×256 = 2 locales de 3 m × 4.2 m.</summary>
        private static void GenerateStorefront(out Color[] albedo, out Color[] emission)
        {
            const int width = 512, height = 256, bay = 256;
            albedo = new Color[width * height];
            emission = new Color[width * height];
            var random = new System.Random(Seed + 99);
            Color[] fascia =
            {
                CityGen.Hex("#B23A2E"), CityGen.Hex("#2F7A4F"), CityGen.Hex("#2C5A9A"), CityGen.Hex("#D9A520"), CityGen.Hex("#2E8C8C"),
            };

            FillRect(albedo, width, 0, 0, width, height, CityGen.Hex("#6E6A66"), 0.15f);
            for (int b = 0; b < 2; b++)
            {
                int x0 = b * bay;
                // Zócalo, escaparate, rótulo y franja de muro superior.
                FillRect(albedo, width, x0, 0, x0 + bay, 14, CityGen.Hex("#3A3836"), 0.1f);
                FillRect(albedo, width, x0 + 12, 14, x0 + bay - 12, 180, CityGen.Hex("#2B2B2B"), 0.3f);
                bool open = random.NextDouble() < 0.75;
                Color inside = LitColors[random.Next(3)] * (0.75f + 0.25f * (float)random.NextDouble());
                Window(albedo, emission, width, x0 + 18, 18, x0 + bay - 18, 176, open, inside, 0f);
                // Puerta del local.
                FillRect(albedo, width, x0 + bay - 70, 18, x0 + bay - 66, 150, CityGen.Hex("#2B2B2B"), 0.3f);
                Color sign = fascia[random.Next(fascia.Length)];
                FillRect(albedo, width, x0 + 6, 186, x0 + bay - 6, 236, sign, 0.4f);
                if (random.NextDouble() < 0.6)
                {
                    // Rótulo luminoso: banda de "letras" claras.
                    for (int l = x0 + 40; l < x0 + bay - 40; l += 22)
                    {
                        FillRect(albedo, width, l, 200, l + 14, 222, Color.white * 0.9f, 0.4f);
                        FillRect(emission, width, l, 200, l + 14, 222, Color.Lerp(sign, Color.white, 0.6f), 1f);
                    }
                }
            }
        }

        private static Color[] GenerateSidewalk()
        {
            const int size = 256; // 2 m → 128 px/m, losas de 1 m
            var pixels = new Color[size * size];
            Color baseColor = CityGen.Hex("#8C8983");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = EnvGen.TileFbm(x / (float)size, y / (float)size, 16, 4, 51) - 0.5f;
                    float slab = 0.94f + 0.12f * Hash01(x / 128, y / 128, 3);
                    bool joint = x % 128 < 2 || y % 128 < 2;
                    Color c = joint ? baseColor * 0.62f : baseColor * slab * (1f + n * 0.14f);
                    c.a = joint ? 0.1f : 0.2f;
                    pixels[y * size + x] = c;
                }
            }
            return pixels;
        }

        private static Color[] GenerateAsphalt()
        {
            const int size = 512;
            var pixels = new Color[size * size];
            Color baseColor = CityGen.Hex("#3B3C41");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = EnvGen.TileFbm(x / (float)size, y / (float)size, 12, 5, 61) - 0.5f;
                    float speck = Hash01(x, y, 5) > 0.985f ? 1.25f : 1f;
                    Color c = baseColor * (1f + n * 0.25f) * speck;
                    c.a = 0.28f;
                    pixels[y * size + x] = c;
                }
            }
            return pixels;
        }

        private static Color[] GenerateGarage()
        {
            const int size = 256;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float slat = (y % 16) / 16f;
                    Color c = Color.Lerp(CityGen.Hex("#C9CDD2"), CityGen.Hex("#8A8F96"), slat > 0.8f ? 1f : slat * 0.3f);
                    if (x < 6 || x > size - 7 || y > size - 8) c = CityGen.Hex("#2E3440");
                    c.a = 0.35f;
                    pixels[y * size + x] = c;
                }
            }
            return pixels;
        }

        private static Color[] Noise(int size, Color baseColor, float amount, float smoothness, int period, int seed)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = EnvGen.TileFbm(x / (float)size, y / (float)size, period, 4, seed) - 0.5f;
                    Color c = baseColor * (1f + n * amount * 2f);
                    c.a = smoothness;
                    pixels[y * size + x] = c;
                }
            }
            return pixels;
        }

        // ---------- Rótulo (fuente de píxeles 5×7) ----------

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
            ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
            ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            ['I'] = new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" },
            ['C'] = new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" },
            ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
        };

        /// <summary>Panel azul 512×128 con el texto en blanco emisivo ('Í' = I con tilde).</summary>
        private static void GenerateSign(string text, out Color[] albedo, out Color[] emission)
        {
            const int width = 512, height = 128;
            albedo = new Color[width * height];
            emission = new Color[width * height];
            FillRect(albedo, width, 0, 0, width, height, CityGen.Hex("#1D3E8F"), 0.4f);
            FillRect(albedo, width, 0, 0, width, 6, CityGen.Hex("#E8ECF5"), 0.4f);
            FillRect(albedo, width, 0, height - 6, width, height, CityGen.Hex("#E8ECF5"), 0.4f);

            int columns = text.Length * 6 - 1;
            int scale = Mathf.Min((width - 40) / columns, (height - 24) / 9);
            int startX = (width - columns * scale) / 2;
            int startY = (height - 9 * scale) / 2;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                bool accent = ch == 'Í';
                string[] glyph = Glyphs[accent ? 'I' : ch];
                int gx = startX + i * 6 * scale;
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (glyph[row][col] != '1') continue;
                        int px = gx + col * scale;
                        int py = startY + (6 - row) * scale; // fila 0 arriba
                        FillRect(albedo, width, px, py, px + scale, py + scale, Color.white, 0.4f);
                        FillRect(emission, width, px, py, px + scale, py + scale, Color.white, 1f);
                    }
                }
                if (accent)
                {
                    // Tilde: dos píxeles en diagonal sobre la I.
                    int ay = startY + 7 * scale + scale / 2;
                    FillRect(albedo, width, gx + 2 * scale, ay, gx + 3 * scale, ay + scale, Color.white, 0.4f);
                    FillRect(emission, width, gx + 2 * scale, ay, gx + 3 * scale, ay + scale, Color.white, 1f);
                    FillRect(albedo, width, gx + 3 * scale, ay + scale, gx + 4 * scale, ay + scale * 3 / 2, Color.white, 0.4f);
                    FillRect(emission, width, gx + 3 * scale, ay + scale, gx + 4 * scale, ay + scale * 3 / 2, Color.white, 1f);
                }
            }
        }

        // ---------- Utilidades ----------

        private static void FillRect(Color[] pixels, int width, int x0, int y0, int x1, int y1, Color color, float alpha)
        {
            int height = pixels.Length / width;
            x0 = Mathf.Clamp(x0, 0, width); x1 = Mathf.Clamp(x1, 0, width);
            y0 = Mathf.Clamp(y0, 0, height); y1 = Mathf.Clamp(y1, 0, height);
            color.a = alpha;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    pixels[y * width + x] = color;
                }
            }
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
