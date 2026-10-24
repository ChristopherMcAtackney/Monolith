using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monolith.ShipArt
{
    /// <summary>
    /// Loads ship models by id from Resources/ShipArt: <c>&lt;id&gt;.prefab</c> (made by ShipArtSetup from the Blender FBX, with its
    /// materials, LOD group, collision proxy and <see cref="ShipSubsystem"/> markers) and <c>&lt;id&gt;.ship.json</c>. Materials are
    /// assets referenced by the prefab, so nothing is looked up by shader name at run time and a player build keeps their shader variants.
    /// </summary>
    public static class ShipArtCatalog
    {
        public const string Folder = "ShipArt";
        static readonly Dictionary<string, ShipManifest> Manifests = new Dictionary<string, ShipManifest>();

        public static bool Exists(string id) => Resources.Load<GameObject>($"{Folder}/{id}") != null;

        public static ShipManifest Manifest(string id)
        {
            if (Manifests.TryGetValue(id, out var m)) return m;
            var asset = Resources.Load<TextAsset>($"{Folder}/{id}.ship") ?? throw new System.IO.FileNotFoundException($"Resources/{Folder}/{id}.ship.json is missing: run tools/blender.ps1 on the model script, then tools/unity.ps1 Monolith.EditorTools.Setup.Project");
            return Manifests[id] = ShipManifest.Parse(asset.text);
        }

        /// <summary>A new instance of the ship's model under <paramref name="parent"/> (identity transform: +z is the bow).</summary>
        public static GameObject Instantiate(string id, Transform parent)
        {
            var prefab = Resources.Load<GameObject>($"{Folder}/{id}") ?? throw new System.IO.FileNotFoundException($"Resources/{Folder}/{id}.prefab is missing: run tools/unity.ps1 Monolith.EditorTools.Setup.Project");
            var go = UnityEngine.Object.Instantiate(prefab, parent);
            go.name = id;
            return go;
        }

        /// <summary>Every subsystem on a ship instance (targeting uses this to cycle parts).</summary>
        public static ShipSubsystem[] Subsystems(GameObject ship) => ship.GetComponentsInChildren<ShipSubsystem>(true);
    }
}
