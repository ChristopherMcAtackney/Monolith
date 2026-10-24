using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.Flight;
using Monolith.ShipArt;
using Monolith.Sky;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// A look-development scene for a real ship model: a camera, the Sky system exactly as the game uses it (Sun, fill, exposure, camera look),
    /// a long-range shadow volume, the model, and optionally a placeholder fighter beside it for scale and some damage applied through the
    /// same <see cref="IShipDamage"/> interface the combat system will use. A capture plan asks for it with <c>"scene": "corvette"</c>;
    /// <c>"sceneOptions"</c> is a <see cref="ShowcaseOptions"/>. The scene never touches the Sun, exposure or the look.
    /// </summary>
    public static class ShipShowcase
    {
        public const string SceneName = "corvette";

        public sealed class ShowcaseOptions
        {
            /// <summary>Ship model id (Resources/ShipArt/&lt;id&gt;).</summary>
            public string ship = "earth_corvette";
            /// <summary>The ship's orientation as Unity Euler degrees. The Sun is fixed in the sky, so these decide where the light falls on the hull
            /// (tools/ship/make_ship_plan.py solves them for a wanted Sun direction in the ship's own frame).</summary>
            public float yaw = 0f, pitch = 0f, roll = 0f;
            public float[] pos = { 0f, 0f, 0f };
            /// <summary>0 uses the shared render-quality profile's (6 km).</summary>
            public float shadowDistance = 0f;
            /// <summary>-1 lets the LOD group decide; 0..n forces that level (to inspect the lower ones).</summary>
            public int forceLod = -1;
            public Fighter fighter;
            public Damage damage;
            /// <summary>More ship models in the picture (for scale shots): model id, world position and Euler rotation.</summary>
            public List<Extra> ships;
            /// <summary>Look-development: names of materials to leave out of the picture (e.g. ["MN_EarthWear", "MN_EarthPaint"] shows the bare plating), to tell which layer makes a mark.</summary>
            public List<string> hideMaterials;
            /// <summary>Any <see cref="SkyOptions"/> fields to override by name, for look development (e.g. {"AmbientNits": 2000}); the defaults are the game's.</summary>
            public JObject sky;


            public sealed class Extra { public string ship; public float[] pos, rot; }
            public sealed class Fighter { public float[] pos, rot; public string cls = "vanguard_fighter"; }
            public sealed class HullHit { public float[] pos, normal; public float radius = 3f, severity = 0.6f; }
            public sealed class Damage
            {
                /// <summary>Subsystems to destroy by name (SUB_flak_P02) or kind (the first intact one of that kind).</summary>
                public List<string> destroy = new List<string>();
                public Dictionary<string, float> health = new Dictionary<string, float>();
                /// <summary>Seconds into the debris' flight when the picture is taken.</summary>
                public float debrisAge = 1.2f;
                public float hull = 0f;
                public List<HullHit> hits = new List<HullHit>();
            }
        }

        public static bool TryBuild(GameWorld world, Transform parent, string scene, string optionsJson)
        {
            if (!string.Equals(scene, SceneName, StringComparison.OrdinalIgnoreCase)) return false;
            var opts = new ShowcaseOptions();
            if (!string.IsNullOrEmpty(optionsJson)) JsonConvert.PopulateObject(optionsJson, opts, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });

            var root = new GameObject("ShipShowcase").transform;
            root.SetParent(parent, false);
            var cam = MakeCamera(root);
            world.MainCamera = cam;
            var skyOptions = new SkyOptions();
            if (opts.sky != null) JsonConvert.PopulateObject(opts.sky.ToString(), skyOptions);
            if (opts.shadowDistance > 0f) skyOptions.ShadowDistance = opts.shadowDistance;
            var rig = SkyRig.Enable(cam, root, skyOptions);          // the Sky system: Sun, fill, exposure and the camera look (its defaults unless the plan overrides some)

            var shipGo = ShipArtCatalog.Instantiate(opts.ship, root);
            shipGo.transform.SetPositionAndRotation(new Vector3(opts.pos[0], opts.pos[1], opts.pos[2]), Quaternion.Euler(opts.pitch, opts.yaw, opts.roll));
            var sunInShip = Quaternion.Inverse(shipGo.transform.rotation) * rig.SunDirection;
            MnLog.Info($"SHOWCASE sun in the ship's frame (x starboard, y up, z bow): {sunInShip.x:0.000}, {sunInShip.y:0.000}, {sunInShip.z:0.000}");
            if (opts.forceLod >= 0) shipGo.GetComponent<LODGroup>().ForceLOD(opts.forceLod);
            if (opts.ships != null)
                foreach (var e in opts.ships)
                {
                    var go = ShipArtCatalog.Instantiate(e.ship, root);
                    go.transform.SetPositionAndRotation(V3(e.pos), Quaternion.Euler(V3(e.rot)));
                    if (opts.forceLod >= 0) go.GetComponent<LODGroup>().ForceLOD(opts.forceLod);
                }
            if (opts.hideMaterials != null && opts.hideMaterials.Count > 0)
                foreach (var r in shipGo.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] != null && opts.hideMaterials.Contains(mats[i].name)) mats[i] = null;
                    r.sharedMaterials = mats;
                }

            if (opts.damage != null)
            {
                var dmg = shipGo.GetComponent<ShipDamage>();
                foreach (var h in opts.damage.health) dmg.SetSubsystemHealth(h.Key, h.Value);
                foreach (var d in opts.damage.destroy) dmg.DestroySubsystem(d, opts.damage.debrisAge);
                if (opts.damage.hull > 0f) dmg.SetHullDamage(opts.damage.hull);
                foreach (var h in opts.damage.hits) dmg.AddHullHit(V3(h.pos), V3(h.normal), h.radius, h.severity);
                MnLog.Info($"SHOWCASE damage: {opts.damage.destroy.Count} destroyed, hull {opts.damage.hull}, {dmg.DecalCount} decals");
            }

            if (opts.fighter != null)
            {
                var cls = ShipCatalog.Load(opts.fighter.cls);
                var mats = PlaceholderShips.MakeMaterials();
                var f = PlaceholderShips.Fighter("Fighter", cls.Size, mats, root);
                f.transform.SetPositionAndRotation(V3(opts.fighter.pos), Quaternion.Euler(V3(opts.fighter.rot)));
            }
            var manifest = ShipArtCatalog.Manifest(opts.ship);
            MnLog.Info($"SHOWCASE {opts.ship}: {manifest.Length} m, hull triangles {string.Join("/", manifest.LodTriangles)}, {manifest.Subsystems.Count} subsystems");
            return true;
        }

        static Vector3 V3(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static Camera MakeCamera(Transform root)
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 0f, -900f);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100000f;
            cam.allowHDR = true;
            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            hd.stopNaNs = true;
            hd.dithering = true;
            return cam;
        }

    }
}
