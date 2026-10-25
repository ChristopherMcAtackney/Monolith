using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The Oort ice field's comets: the Blender nuclei (art_src/comets; Resources/CometArt/&lt;id&gt;.prefab and comets.json, made by CometArtSetup) scattered as a mission's scenery. A mission rock
    /// says only where it is, how big (its radius), how stretched and how it spins (<see cref="Monolith.Mission.MissionFile.SceneryDef"/>); what it LOOKS like is chosen here, by a random stream of
    /// its own so the layout never moves: which of the five models (the two large, distinctive nuclei for big rocks, the three plainer ones for small), a turn of the model inside the rock's own
    /// stretch (so the same model is stretched along other features each time), a mirror, a few percent of extra stretch and size, and one of a dozen tints of the surface material. Models are
    /// centred on their volume and sized by their bound, so a rock of radius R fits a sphere of about <c>Fit</c> x R, as the old generated rocks did (lanes kept clear for them stay clear).
    /// Drawn only: no collider. Draws go through the models' LOD groups (the farthest are a few hundred triangles) and one shared material with GPU instancing on.
    /// </summary>
    public static class CometField
    {
        public sealed class Model
        {
            public string Id;
            public GameObject Prefab;
            public float Bound, Fit;
            public bool Hero;
        }

        static List<Model> _models;
        static Material _surface;
        static readonly List<Material> Tints = new List<Material>();
        const int TintCount = 14;

        /// <summary>Off, a scene draws its rocks the old way (the generated icospheres): the self-test builds a field both ways to show the layout does not depend on it.</summary>
        public static bool Enabled = true;

        public static IReadOnlyList<Model> Models { get { Load(); return _models; } }
        public static bool Available { get { Load(); return Enabled && _models.Count > 0 && _surface != null; } }

        static void Load()
        {
            if (_models != null) return;
            _models = new List<Model>();
            var manifest = Resources.Load<TextAsset>("CometArt/comets");
            if (manifest == null) return;
            var doc = JObject.Parse(manifest.text);
            _surface = Resources.Load<Material>("CometArt/Materials/MN_CometSurface");
            if (_surface == null) return;
            foreach (var m in (JArray)doc["models"])
            {
                string id = (string)m["id"];
                var prefab = Resources.Load<GameObject>("CometArt/" + id);
                if (prefab == null) continue;
                _models.Add(new Model { Id = id, Prefab = prefab, Bound = (float)m["bound"], Fit = (float)m["fit"], Hero = (bool?)m["hero"] == true });
            }
            // A dozen tints of the one surface: a few percent of brightness and a touch of warm or cool, the same for every model (the layers' colours are multipliers on the textures).
            var rng = new System.Random(4417);
            for (int i = 0; i < TintCount; i++)
            {
                float lum = Mathf.Lerp(0.8f, 1.18f, (float)rng.NextDouble());
                float warm = Mathf.Lerp(-0.035f, 0.045f, (float)rng.NextDouble());
                var c = new Color(lum * (1f + warm), lum, lum * (1f - warm), 1f);
                var mat = new Material(_surface) { name = "MN_CometSurface_t" + i, enableInstancing = true };
                mat.SetColor("_BaseColor0", c);
                mat.SetColor("_BaseColor1", c);
                mat.SetColor("_BaseColor2", Color.Lerp(Color.white, c, 0.5f));
                Tints.Add(mat);
            }
        }

        /// <summary>Which model a rock of this radius is: the big rocks get the two distinctive nuclei, the small rocks mostly the plain ones (with now and then a small copy of a big one).</summary>
        public static Model Pick(float radius, System.Random rng)
        {
            Load();
            var heroes = _models.FindAll(m => m.Hero);
            var plain = _models.FindAll(m => !m.Hero);
            if (heroes.Count == 0) return plain[rng.Next(plain.Count)];
            if (plain.Count == 0) return heroes[rng.Next(heroes.Count)];
            float pHero = radius >= 420f ? 1f : radius >= 100f ? 0.4f : 0.14f;
            return rng.NextDouble() < pHero ? heroes[rng.Next(heroes.Count)] : plain[rng.Next(plain.Count)];
        }

        static float Lerp(System.Random rng, float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        static Quaternion RandomRotation(System.Random rng)
        {
            // uniform on the sphere of rotations (Shoemake)
            double u1 = rng.NextDouble(), u2 = rng.NextDouble() * 2.0 * Math.PI, u3 = rng.NextDouble() * 2.0 * Math.PI;
            double a = Math.Sqrt(1.0 - u1), b = Math.Sqrt(u1);
            return new Quaternion((float)(a * Math.Sin(u2)), (float)(a * Math.Cos(u2)), (float)(b * Math.Sin(u3)), (float)(b * Math.Cos(u3)));
        }

        /// <summary>
        /// One rock: a holder (the mission's own stretch, a few percent more, and the placement the caller sets) with the chosen model inside it, turned, mirrored, sized and tinted from
        /// <paramref name="variation"/>, a seed that has nothing to do with the layout's random stream.
        /// </summary>
        public static GameObject Make(string name, float radius, Vector3 stretch, int variation, Transform parent)
        {
            Load();
            var rng = new System.Random(variation * 7919 + 101);
            var model = Pick(radius, rng);
            var holder = new GameObject(name);
            if (parent != null) holder.transform.SetParent(parent, false);
            holder.transform.localScale = Vector3.Scale(stretch, new Vector3(Lerp(rng, 0.9f, 1.12f), Lerp(rng, 0.9f, 1.12f), Lerp(rng, 0.9f, 1.12f)));
            var inst = UnityEngine.Object.Instantiate(model.Prefab, holder.transform);
            inst.name = model.Id;
            float s = radius * model.Fit / model.Bound * Lerp(rng, 0.94f, 1.08f);
            inst.transform.localRotation = RandomRotation(rng);
            inst.transform.localScale = new Vector3(rng.NextDouble() < 0.5 ? -s : s, s, s);
            int tintIndex = rng.Next(Tints.Count);
            var tint = Tints[tintIndex];
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = tint;
            if (Monolith.Core.CommandLine.Has("-mnLogScenery"))
                Monolith.Core.MnLog.Info($"COMET {name} model {model.Id} radius {radius:0.#} scale {s:0.####} mirror {(inst.transform.localScale.x < 0f ? 1 : 0)} tint {tintIndex} stretch {holder.transform.localScale.x:0.##} {holder.transform.localScale.y:0.##} {holder.transform.localScale.z:0.##}");
            return holder;
        }
    }
}
