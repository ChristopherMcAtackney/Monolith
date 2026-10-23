using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.View
{
    /// <summary>Resources/View/view.json (format "monolith-view/1"): camera, motion dust and placeholder stars.</summary>
    public sealed class ViewSettings
    {
        public const string Format = "monolith-view/1";
        public const string ResourcePath = "View/view";

        public string format, about;
        public CameraSettings camera = new CameraSettings();
        public DustSettings dust = new DustSettings();
        public StarSettings stars = new StarSettings();

        public sealed class CameraSettings
        {
            public float fovVertical = 55f;
            public float[] eye = { 0f, 0.4f, 1.5f };
            public float near = 0.1f, far = 100000f;
            public string why;
            public Vector3 Eye => eye != null && eye.Length == 3 ? new Vector3(eye[0], eye[1], eye[2]) : Vector3.zero;
        }

        public sealed class DustSettings
        {
            public int count = 700;
            public float radius = 110f, streakSeconds = 0.05f, halfWidthPx = 0.9f, brightness = 0.55f, nearFade = 2f;
            public int seed = 11;
            public string why;
        }

        public sealed class StarSettings
        {
            public int count = 5000, seed = 7;
            public string why;
        }

        public static ViewSettings Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
            ViewSettings s;
            try
            {
                s = JsonConvert.DeserializeObject<ViewSettings>(asset.text,
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            }
            catch (Exception e) { throw new InvalidDataException($"Resources/{ResourcePath}.json: {e.Message}"); }
            if (s == null || s.format != Format) throw new InvalidDataException($"Resources/{ResourcePath}.json: format '{s?.format}', expected '{Format}'");
            return s;
        }
    }
}
