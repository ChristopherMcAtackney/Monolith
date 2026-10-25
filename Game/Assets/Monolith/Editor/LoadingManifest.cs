using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Writes <c>Resources/Loading/preload.json</c>: the assets the loading screen reads one at a time (asynchronously, with the screen drawing) before the world is built, so the build
    /// finds them in memory and the bar moves with real loads. Four lists, one per step of the bar: <c>ships</c> (the ship art), <c>cabin</c> (the cockpit and cabin model), <c>effects</c>
    /// (the flipbooks and the hull sections), <c>sky</c> (the Milky Way, the star catalogue, the sky and look shaders). Each entry is <c>"Resources path|Type"</c>.
    /// It is rewritten before every player build (so a new ship or effect is in it without anyone remembering), and from the menu Monolith / Loading.
    /// </summary>
    public sealed class LoadingManifest : IPreprocessBuildWithReport
    {
        public const string AssetPath = "Assets/Monolith/Resources/Loading/preload.json";
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report) => Write();

        [MenuItem("Monolith/Loading/Write preload manifest")]
        public static void WriteMenu() => Write();

        public static bool Write()
        {
            var o = new JObject
            {
                ["ships"] = Concat(Find("Assets/Monolith/Resources/ShipArt", "*.prefab", "GameObject"), Find("Assets/Monolith/Resources/CometArt", "*.prefab", "GameObject")),
                ["cabin"] = Find("Assets/Monolith/Resources/Cabin", "*.prefab", "GameObject"),
                ["effects"] = Concat(
                    Find("Assets/Monolith/Resources/Juice/Flip", "*.png", "Texture2D"),
                    Find("Assets/Monolith/Resources/Juice/Sections", "*.asset", "Mesh"),
                    Find("Assets/Monolith/Resources/Juice", "*.shader", "Shader")),
                ["sky"] = Concat(
                    Find("Assets/Monolith/Sky/Resources/MonolithSky", "*.exr", "Texture2D"),
                    Find("Assets/Monolith/Sky/Resources/MonolithSky", "stars.bytes", "TextAsset"),
                    Find("Assets/Monolith/Sky/Resources/MonolithSky", "*.shader", "Shader"),
                    Find("Assets/Monolith/Look/Resources/MonolithLook", "*.shader", "Shader")),
            };
            string text = o.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n") + "\n";      // the same bytes on every machine: it is a committed file
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetPath));
            string old = File.Exists(full) ? File.ReadAllText(full).Replace("\r\n", "\n") : null;
            if (old == text) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[MN] LOADING preload manifest written: " + string.Join(", ", o.Properties().Select(p => p.Name + " " + ((JArray)p.Value).Count)));
            return true;
        }

        static JArray Concat(params JArray[] lists)
        {
            var a = new JArray();
            foreach (var l in lists) foreach (var t in l) a.Add(t);
            return a;
        }

        /// <summary>The files of a folder (not below it) as Resources paths with a type, sorted so the manifest is the same every time.</summary>
        static JArray Find(string folder, string pattern, string type)
        {
            var a = new JArray();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder));
            if (!Directory.Exists(dir)) return a;
            foreach (var f in Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).OrderBy(x => x, System.StringComparer.Ordinal))
            {
                string assetPath = folder + "/" + Path.GetFileName(f);
                int i = assetPath.IndexOf("/Resources/", System.StringComparison.Ordinal);
                if (i < 0) continue;
                string res = assetPath.Substring(i + "/Resources/".Length);
                res = res.Substring(0, res.Length - Path.GetExtension(res).Length);
                a.Add(res + "|" + type);
            }
            return a;
        }
    }
}
