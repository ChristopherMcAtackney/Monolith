using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Placeholder ships for the flight playtest, built from code in FS2's design language (design section 9): long,
    /// layered hulls of distinct plated segments joined by recessed necks; a spine, fins and an asymmetric side pod; rows
    /// of lit windows, light strips and small red/green/white running lights that give the scale away; engines as hot,
    /// glowing recessed exhausts. Materials are plated PBR (panel layout, coloured trim, wear, specular variation, panel-line
    /// normals) generated at boot. Cheap stand-ins: the real ships are Blender models.
    /// </summary>
    public static class PlaceholderShips
    {
        public sealed class Materials
        {
            public Material Hull, Plates, Dark, Trim, Windows, Strips, RunRed, RunGreen, RunWhite, Exhaust, FighterBody, FighterTrim;
        }

        /// <summary>Instances of the HDRP/Lit assets made by Setup.Project (Resources/Flight), with textures generated here.</summary>
        public static Materials MakeMaterials()
        {
            var lit = Load("Flight/MN_FlightLit");
            var plated = Load("Flight/MN_FlightPlated");
            var glow = Load("Flight/MN_FlightGlow");
            PlateTextures(1024, 7, out var albedo, out var normal, out var mask);
            var exhaust = ExhaustTexture(256);

            Material Plated(string name, Color tint, float normalScale, float smoothMax)
            {
                var m = new Material(plated) { name = name };
                m.SetColor("_BaseColor", tint);
                m.SetTexture("_BaseColorMap", albedo);
                m.SetTexture("_NormalMap", normal);
                m.SetTexture("_MaskMap", mask);
                m.SetFloat("_NormalScale", normalScale);
                m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 1f);
                m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", smoothMax);
                m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
                return m;
            }
            // Emissive in nits: at the scene's EV100 14, ~20,000 nits shows as full white, more blooms.
            Material Light(string name, Color c, float nits)
            {
                var m = new Material(lit) { name = name };
                m.SetColor("_BaseColor", Color.black);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.6f);
                m.SetColor("_EmissiveColor", c * nits);
                return m;
            }
            var ex = new Material(glow) { name = "MN_Exhaust" };
            ex.SetColor("_BaseColor", Color.black);
            ex.SetTexture("_EmissiveColorMap", exhaust);
            ex.SetColor("_EmissiveColor", Color.white * 90000f);

            return new Materials
            {
                Hull = Plated("MN_Hull", new Color(0.78f, 0.80f, 0.84f), 1f, 0.95f),
                Plates = Plated("MN_HullPlates", new Color(0.56f, 0.60f, 0.68f), 1f, 1f),
                Dark = Plated("MN_HullDark", new Color(0.22f, 0.22f, 0.24f), 0.8f, 0.8f),
                Trim = Plated("MN_HullTrim", new Color(0.66f, 0.58f, 0.44f), 0.8f, 0.9f),
                Windows = Light("MN_Windows", new Color(1f, 0.84f, 0.58f), 26000f),
                Strips = Light("MN_LightStrips", new Color(1f, 0.95f, 0.84f), 34000f),
                RunRed = Light("MN_RunRed", new Color(1f, 0.06f, 0.04f), 400000f),
                RunGreen = Light("MN_RunGreen", new Color(0.08f, 1f, 0.25f), 300000f),
                RunWhite = Light("MN_RunWhite", new Color(1f, 1f, 1f), 260000f),
                Exhaust = ex,
                FighterBody = Plated("MN_FighterBody", new Color(0.62f, 0.64f, 0.70f), 0.6f, 1f),
                FighterTrim = Plated("MN_FighterTrim", new Color(0.70f, 0.24f, 0.14f), 0.6f, 1f),
            };
        }

        static Material Load(string path) => Resources.Load<Material>(path)
            ?? throw new InvalidOperationException($"Resources/{path} is missing: run tools\\unity.ps1 Monolith.EditorTools.Setup.Project, then build");

        // ------------------------------------------------------------------ textures

        struct Plate
        {
            public RectInt Rect;
            public Color Paint;
            public float Height, Smooth, Metal;
            public bool Bare, Stripe;
        }

        /// <summary>
        /// Plated hull surface, one 24 m tile: plates of many sizes from a recursive split, each with its own paint shade
        /// (cool greys, a few blue-grey and olive plates, the odd bare-metal or painted-trim plate), recessed panel lines,
        /// rivet rows, grime streaks and chipped edges. Albedo (sRGB), normal (tangent space, from a height field) and HDRP
        /// mask (metallic, AO, -, smoothness) share the layout so paint, dirt and shine agree.
        /// </summary>
        public static void PlateTextures(int size, int seed, out Texture2D albedo, out Texture2D normal, out Texture2D mask)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var plates = new List<Plate>();
            void Split(RectInt r, int depth)
            {
                bool small = r.width < 200 && r.height < 200;
                if (depth > 7 || r.width < 112 || r.height < 112 || (small && R() < 0.5f))
                {
                    float shade = 0.93f + 0.07f * R();
                    float hueRoll = R();
                    Color paint = hueRoll < 0.12f ? new Color(0.86f, 0.89f, 0.96f) : hueRoll < 0.2f ? new Color(0.89f, 0.9f, 0.84f) : new Color(0.9f, 0.91f, 0.93f);
                    var p = new Plate
                    {
                        Rect = r, Paint = paint * shade, Height = R() * 0.6f,
                        Smooth = 0.42f + 0.18f * R(), Metal = 0.15f + 0.1f * R(),
                        Bare = R() < 0.05f, Stripe = R() < 0.025f,
                    };
                    plates.Add(p);
                    return;
                }
                bool vertical = r.width > r.height ? R() < 0.8f : R() < 0.2f;
                float at = 0.3f + 0.4f * R();
                if (vertical)
                {
                    int w = Mathf.Clamp(Mathf.RoundToInt(r.width * at / 8f) * 8, 56, r.width - 56);
                    Split(new RectInt(r.x, r.y, w, r.height), depth + 1);
                    Split(new RectInt(r.x + w, r.y, r.width - w, r.height), depth + 1);
                }
                else
                {
                    int h = Mathf.Clamp(Mathf.RoundToInt(r.height * at / 8f) * 8, 56, r.height - 56);
                    Split(new RectInt(r.x, r.y, r.width, h), depth + 1);
                    Split(new RectInt(r.x, r.y + h, r.width, r.height - h), depth + 1);
                }
            }
            Split(new RectInt(0, 0, size, size), 0);

            var id = new int[size * size];
            for (int k = 0; k < plates.Count; k++)
            {
                var r = plates[k].Rect;
                for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++) id[y * size + x] = k;
            }

            var height = new float[size * size];
            var col = new Color32[size * size];
            var msk = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                var p = plates[id[i]];
                var r = p.Rect;
                int dx = Mathf.Min(x - r.xMin, r.xMax - 1 - x), dy = Mathf.Min(y - r.yMin, r.yMax - 1 - y);
                int d = Mathf.Min(dx, dy);
                float h = p.Height;
                float ao = 1f;
                Color c = p.Paint;
                float smooth = p.Smooth, metal = p.Metal;
                if (p.Stripe && ((x + y) / 24) % 2 == 0) c = new Color(0.95f, 0.66f, 0.18f) * 0.9f;   // painted hazard band
                // Bare metal stays only half metallic: with no reflections of a sky yet, fully metallic plates render black.
                if (p.Bare) { c = new Color(0.8f, 0.81f, 0.82f); metal = 0.5f; smooth = 0.68f; }
                // Panel lines: a groove round every plate, with a little bevel.
                if (d < 2) { h = -0.6f; c *= 0.45f; ao = 0.55f; smooth *= 0.6f; }
                else if (d < 5) { h = Mathf.Lerp(-0.2f, h, (d - 2) / 3f); ao = 0.85f; }
                // Rivet rows just inside the larger plates' edges.
                if (r.width > 96 && r.height > 96 && d == 7 && ((dx == 7 ? y : x) % 12) < 2) { h += 0.5f; c *= 1.08f; }
                // Grime streaks running down the plates, darker and duller.
                float streak = Noise(x * 0.06f, 3.1f, seed) * Noise(x * 0.013f, y * 0.004f, seed + 5);
                float grime = Mathf.Clamp01((streak - 0.35f) * 1.6f) * 0.32f + (d < 8 ? (8 - d) / 8f * 0.12f : 0f);
                c *= 1f - grime;
                smooth *= 1f - grime * 1.2f;
                // Chipped paint near the edges: bare, brighter, shinier metal.
                if (!p.Bare && d < 9 && Noise(x * 0.3f, y * 0.3f, seed + 9) > 0.84f) { c = Color.Lerp(c, new Color(0.82f, 0.82f, 0.84f), 0.8f); metal = 0.4f; smooth = 0.66f; }
                height[i] = h;
                col[i] = new Color(c.r, c.g, c.b, 1f);
                msk[i] = new Color(Mathf.Clamp01(metal), ao, 0f, Mathf.Clamp01(smooth));
            }

            var nrm = new Color32[size * size];
            const float strength = 2.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float hl = height[y * size + (x + size - 1) % size], hr = height[y * size + (x + 1) % size];
                float hd = height[((y + size - 1) % size) * size + x], hu = height[((y + 1) % size) * size + x];
                var n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                nrm[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }

            albedo = Tex("MN_PlateAlbedo", size, col, false);
            normal = Tex("MN_PlateNormal", size, nrm, true);
            mask = Tex("MN_PlateMask", size, msk, true);
        }

        /// <summary>Radial exhaust glow: white-hot core, blue body, a dim violet edge fading to nothing at the rim.</summary>
        public static Texture2D ExhaustTexture(int size)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                Color c;
                if (r < 0.42f) c = Color.Lerp(new Color(1f, 0.98f, 0.94f), new Color(0.82f, 0.92f, 1f), r / 0.42f);
                else if (r < 0.78f) c = Color.Lerp(new Color(0.82f, 0.92f, 1f), new Color(0.35f, 0.55f, 1f), (r - 0.42f) / 0.36f);
                else c = Color.Lerp(new Color(0.35f, 0.55f, 1f), new Color(0.12f, 0.08f, 0.35f), Mathf.Clamp01((r - 0.78f) / 0.2f));
                if (r > 0.98f) c = Color.black;
                px[y * size + x] = c;
            }
            var t = Tex("MN_ExhaustGlow", size, px, false);
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        static Texture2D Tex(string name, int size, Color32[] px, bool linear)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = name, anisoLevel = 8, wrapMode = TextureWrapMode.Repeat };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>Smooth value noise in 0..1 (repeatable, no Random).</summary>
        static float Noise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
        }

        // ------------------------------------------------------------------ capital hull

        struct Segment
        {
            public float Z0, Z1, Hx, Hy, Y;
        }

        /// <summary>
        /// A capital-ship hull <paramref name="length"/> metres long, nose along +z: an engine block with recessed exhausts,
        /// then plated segments joined by darker recessed necks, the front one blunt. Seeded, so the same numbers always
        /// build the same hull. One GameObject per material.
        /// </summary>
        public static GameObject Hull(string name, float length, float width, float height, int seed, Materials mats, Transform parent)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            float L = length, W = width, H = height;
            var hull = new MeshKit { UvMetres = 40f };
            var plates = new MeshKit { UvMetres = 40f };
            var dark = new MeshKit { UvMetres = 40f };
            var trim = new MeshKit { UvMetres = 40f };
            var windows = new MeshKit();
            var strips = new MeshKit();
            var red = new MeshKit(); var green = new MeshKit(); var white = new MeshKit();
            var exhaust = new MeshKit();
            var id = Quaternion.identity;
            var segs = new List<Segment>();

            // Engine block and its exhausts.
            float z = -0.5f * L;
            float engLen = 0.09f * L, ex = 0.4f * W, ey = 0.36f * H;
            hull.Extrude(MeshKit.Chamfered(ex, ey, 0.25f * ey), z, z + engLen, Vector3.zero, id);
            int cols = W > 1.3f * H ? 3 : 2, rows = 2;
            float rOut = Mathf.Min(ex / cols, ey / rows) * 0.82f, tubeLen = 0.045f * L;
            for (int cx = 0; cx < cols; cx++)
            for (int cy = 0; cy < rows; cy++)
            {
                var at = new Vector3((cx - (cols - 1) * 0.5f) * 2f * ex / cols, (cy - (rows - 1) * 0.5f) * 2f * ey / rows, 0f);
                dark.Tube(at, id, rOut, rOut * 0.78f, z - tubeLen, z + 1f, 12, exhaust);
            }
            // A ring of light strips round the engine block's back edge.
            strips.Box(new Vector3(0f, ey + 0.4f, z + 3f), new Vector3(1.4f * ex, 0.7f, 1.2f));
            z += engLen;

            // Segments, back to front, each after a recessed neck.
            int n = 4 + rng.Next(3);
            float neck = 0.02f * L;
            var weights = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++) { weights[i] = 0.7f + 0.6f * R(); sum += weights[i]; }
            float avail = 0.5f * L - z - neck * n;
            for (int i = 0; i < n; i++)
            {
                bool nose = i == n - 1;
                dark.Extrude(MeshKit.Chamfered(0.3f * W, 0.27f * H, 0.08f * H), z, z + neck, Vector3.zero, id);
                z += neck;
                float t = (i + 0.5f) / n;
                float prof = nose ? 0.66f : Mathf.Lerp(0.86f, 1f, Mathf.Sin(t * Mathf.PI));
                float hx = 0.5f * W * prof * (0.92f + 0.1f * R()), hy = 0.5f * H * prof * (0.85f + 0.15f * R());
                float y = (R() - 0.5f) * 0.08f * H;
                float len = avail * weights[i] / sum;
                var c = new Vector3(0f, y, 0f);
                hull.Extrude(MeshKit.Chamfered(hx, hy, (nose ? 0.35f : 0.18f) * hy), z, z + len, c, id, nose ? 0.82f : 1f);
                segs.Add(new Segment { Z0 = z, Z1 = z + len, Hx = hx, Hy = hy, Y = y });

                if (!nose)
                {
                    // Layered: an upper deck on some segments, armour plates on the flanks and top.
                    if (R() < 0.65f)
                    {
                        float dl = len * (0.5f + 0.35f * R()), dz = z + (len - dl) * R();
                        plates.Extrude(MeshKit.Chamfered(hx * 0.62f, hy * 0.2f, hy * 0.08f), dz, dz + dl, new Vector3(0f, y + hy + hy * 0.18f, 0f), id);
                    }
                    foreach (float side in new[] { -1f, 1f })
                    {
                        int k = 1 + rng.Next(2);
                        for (int j = 0; j < k; j++)
                        {
                            float pl = len * (0.3f + 0.45f * R()), pz = z + (len - pl) * R() + pl * 0.5f;
                            float ph = hy * (0.35f + 0.4f * R()), py = y + (R() - 0.5f) * (hy - ph);
                            plates.Box(new Vector3(side * (hx + 0.8f), py, pz), new Vector3(1.6f, ph * 2f * 0.9f, pl));
                        }
                        // Windows: regular blocks in one or two bands, the scale cue.
                        int bands = 1 + rng.Next(2);
                        for (int b = 0; b < bands; b++)
                        {
                            float wy = y + hy * (0.55f - 0.35f * b);
                            for (float wz = z + 6f; wz < z + len - 6f; wz += 3.6f)
                            {
                                if (Mathf.Repeat((wz - z) / 3.6f, 14f) > 10f) continue;      // blocks of ten with gaps
                                float wx = side * (hx + 0.06f);
                                Vector3 a = new Vector3(wx, wy - 0.4f, wz), bb = new Vector3(wx, wy + 0.4f, wz), cc = new Vector3(wx, wy + 0.4f, wz + 1.8f), d = new Vector3(wx, wy - 0.4f, wz + 1.8f);
                                if (side > 0f) windows.Quad(a, bb, cc, d); else windows.Quad(d, cc, bb, a);
                            }
                        }
                        // Light strip along the top edge of the flank, and running lights at the front corners.
                        if (R() < 0.6f) strips.Box(new Vector3(side * (hx + 0.2f), y + hy * 0.82f, z + len * 0.5f), new Vector3(0.5f, 0.5f, len * (0.4f + 0.4f * R())));
                        var lamp = new Vector3(side * (hx + 0.9f), y + hy - 1f, z + len - 2f);
                        (side < 0f ? red : green).Box(lamp, Vector3.one * 1.8f);
                    }
                    white.Box(new Vector3(0f, y + hy + 1f, z + len * 0.5f), Vector3.one * 1.6f);
                }
                else
                {
                    // The blunt nose: a lit command band across its face and lamps on its corners.
                    float fz = z + len + 0.06f, bx = hx * 0.82f * 0.7f, by = hy * 0.82f * 0.15f;
                    windows.Quad(new Vector3(bx, by - 1f, fz), new Vector3(bx, by + 1f, fz), new Vector3(-bx, by + 1f, fz), new Vector3(-bx, by - 1f, fz));
                    red.Box(new Vector3(-hx * 0.82f, y, z + len), Vector3.one * 2f);
                    green.Box(new Vector3(hx * 0.82f, y, z + len), Vector3.one * 2f);
                }
                z += len;
            }

            // Ribs round one mid segment.
            var rib = segs[Mathf.Min(1, segs.Count - 1)];
            for (float rz = rib.Z0 + 10f; rz < rib.Z1 - 10f; rz += 20f)
                dark.Extrude(MeshKit.Chamfered(rib.Hx + 1.5f, rib.Hy + 1.5f, rib.Hy * 0.2f), rz, rz + 5f, new Vector3(0f, rib.Y, 0f), id, 1f, true);

            // Dorsal spine over the middle, two fins on it near the back, a keel fin below the engines.
            var first = segs[0];
            var last = segs[segs.Count - 2 >= 0 ? segs.Count - 2 : 0];
            float topY = 0f;
            foreach (var s in segs) topY = Mathf.Max(topY, s.Y + s.Hy);
            trim.Box(new Vector3(0f, topY + 0.04f * H, 0.5f * (first.Z0 + last.Z1)), new Vector3(0.05f * W, 0.08f * H, last.Z1 - first.Z0));
            for (int f = 0; f < 2; f++)
            {
                float fz = first.Z0 + (0.1f + 0.15f * f) * (last.Z1 - first.Z0);
                trim.Extrude(MeshKit.Chamfered(0.012f * W, 0.18f * H, 0.01f * W), fz, fz + 0.05f * L, new Vector3(0f, topY + 0.26f * H, 0f), id, 0.85f);
            }
            dark.Box(new Vector3(0f, -ey - 0.14f * H, -0.5f * L + 0.06f * L), new Vector3(0.015f * W, 0.28f * H, 0.1f * L));

            // Asymmetry: a long pod on one flank on struts, an antenna boom on the other near the front.
            float podSide = R() < 0.5f ? -1f : 1f;
            var host = segs[Mathf.Min(1, segs.Count - 1)];
            float podX = podSide * (host.Hx + 0.16f * W), podLen = 0.24f * L, podZ = host.Z0 + 0.1f * L;
            plates.Extrude(MeshKit.Chamfered(0.08f * W, 0.14f * H, 0.04f * H), podZ, podZ + podLen, new Vector3(podX, host.Y - 0.05f * H, 0f), id, 0.9f);
            for (int s = 0; s < 2; s++)
                dark.Box(new Vector3(podSide * (host.Hx + 0.04f * W), host.Y - 0.05f * H, podZ + podLen * (0.25f + 0.5f * s)), new Vector3(0.16f * W, 0.04f * H, 0.03f * L));
            (podSide < 0f ? red : green).Box(new Vector3(podX + podSide * 0.08f * W, host.Y - 0.05f * H, podZ + podLen), Vector3.one * 1.8f);
            for (float wz = podZ + 5f; wz < podZ + podLen - 5f; wz += 3.6f)
            {
                float wx = podX + podSide * (0.08f * W + 0.06f), wy = host.Y - 0.05f * H;
                Vector3 a = new Vector3(wx, wy - 0.4f, wz), b = new Vector3(wx, wy + 0.4f, wz), cq = new Vector3(wx, wy + 0.4f, wz + 1.8f), d = new Vector3(wx, wy - 0.4f, wz + 1.8f);
                if (podSide > 0f) windows.Quad(a, b, cq, d); else windows.Quad(d, cq, b, a);
            }
            var front = segs[segs.Count - 1];
            dark.Box(new Vector3(-podSide * (front.Hx + 0.12f * W), front.Y + front.Hy * 0.5f, front.Z0 - 0.02f * L), new Vector3(0.24f * W, 0.012f * W, 0.012f * W));
            white.Box(new Vector3(-podSide * (front.Hx + 0.24f * W), front.Y + front.Hy * 0.5f, front.Z0 - 0.02f * L), Vector3.one * 1.6f);

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            Part(root.transform, "hull", hull, name, mats.Hull);
            Part(root.transform, "plates", plates, name, mats.Plates);
            Part(root.transform, "dark", dark, name, mats.Dark);
            Part(root.transform, "trim", trim, name, mats.Trim);
            Part(root.transform, "windows", windows, name, mats.Windows, false);
            Part(root.transform, "strips", strips, name, mats.Strips, false);
            Part(root.transform, "lamps_red", red, name, mats.RunRed, false);
            Part(root.transform, "lamps_green", green, name, mats.RunGreen, false);
            Part(root.transform, "lamps_white", white, name, mats.RunWhite, false);
            Part(root.transform, "exhaust", exhaust, name, mats.Exhaust, false);
            return root;
        }

        /// <summary>A small placeholder fighter about <paramref name="size"/> (width, height, length) metres, nose along +z.</summary>
        public static GameObject Fighter(string name, Vector3 size, Materials mats, Transform parent)
        {
            float w = size.x, h = size.y, l = size.z;
            var body = new MeshKit { UvMetres = 3f };
            var trim = new MeshKit { UvMetres = 3f };
            var exhaust = new MeshKit();
            var red = new MeshKit(); var green = new MeshKit();
            var id = Quaternion.identity;
            // Fuselage in three plated sections, a cockpit block, wings as plates, two recessed exhausts.
            body.Extrude(MeshKit.Chamfered(0.16f * w, 0.3f * h, 0.1f * h), -0.45f * l, -0.1f * l, Vector3.zero, id);
            body.Extrude(MeshKit.Chamfered(0.12f * w, 0.26f * h, 0.1f * h), -0.08f * l, 0.3f * l, Vector3.zero, id);
            body.Extrude(MeshKit.Chamfered(0.08f * w, 0.18f * h, 0.08f * h), 0.32f * l, 0.5f * l, new Vector3(0f, -0.04f * h, 0f), id, 0.8f);
            trim.Extrude(MeshKit.Chamfered(0.07f * w, 0.1f * h, 0.05f * h), 0.0f, 0.24f * l, new Vector3(0f, 0.3f * h, 0f), id, 0.9f);
            foreach (float side in new[] { -1f, 1f })
            {
                body.Box(new Vector3(side * 0.32f * w, -0.05f * h, -0.18f * l), new Vector3(0.36f * w, 0.06f * h, 0.36f * l));
                trim.Box(new Vector3(side * 0.5f * w, -0.05f * h, -0.18f * l), new Vector3(0.03f * w, 0.4f * h, 0.3f * l));
                body.Tube(new Vector3(side * 0.12f * w, -0.02f * h, 0f), id, 0.14f * h + 0.05f * w, (0.14f * h + 0.05f * w) * 0.75f, -0.56f * l, -0.44f * l, 10, exhaust);
                (side < 0f ? red : green).Box(new Vector3(side * 0.52f * w, -0.05f * h, -0.04f * l), Vector3.one * 0.25f);
            }
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            Part(root.transform, "body", body, name, mats.FighterBody);
            Part(root.transform, "trim", trim, name, mats.FighterTrim);
            Part(root.transform, "exhaust", exhaust, name, mats.Exhaust, false);
            Part(root.transform, "lamp_red", red, name, mats.RunRed, false);
            Part(root.transform, "lamp_green", green, name, mats.RunGreen, false);
            return root;
        }

        static void Part(Transform parent, string part, MeshKit kit, string owner, Material mat, bool shadows = true)
        {
            if (kit.VertexCount == 0) return;
            var go = new GameObject(part);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh(owner + "_" + part);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
