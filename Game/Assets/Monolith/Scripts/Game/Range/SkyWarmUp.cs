using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Looks round the sky with a very wide view in the scene's first four frames (while it is still loading) so the sky's Milky Way
    /// has been drawn from every side before the player sees anything. Found by measurement: the first time the galactic band comes into view
    /// the sky costs a one-off stall of about half a second (some 30 MB of managed allocation and four collections in one frame), which
    /// shows up as a freeze the first time you turn towards it. The cause is in the sky system; this only hides it. The camera's own view
    /// code puts the pose back every frame, so nothing is left changed.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class SkyWarmUp : MonoBehaviour
    {
        public const int Frames = 4;
        /// <summary>How many warm-ups are still looking round (the loading screen waits for none).</summary>
        public static int Pending { get; private set; }
        Camera _camera;
        int _frame;
        float _fov;

        public static void Attach(Camera camera, Transform parent)
        {
            var go = new GameObject("SkyWarmUp");
            go.transform.SetParent(parent, false);
            var w = go.AddComponent<SkyWarmUp>();
            w._camera = camera; w._fov = camera.fieldOfView;
            Pending++;
        }

        void OnDestroy() { Pending = Mathf.Max(0, Pending - 1); }

        void LateUpdate()
        {
            if (_camera == null) { Destroy(this); return; }
            if (_frame >= Frames) { _camera.fieldOfView = _fov; Destroy(this); return; }
            // A few very wide looks that between them cover the whole sky (the view's own code puts the pose back every frame).
            float[] yaw = { 0f, 120f, 240f, 60f }, pitch = { 20f, -20f, 20f, -50f };
            _camera.fieldOfView = 140f;
            _camera.transform.rotation = Quaternion.Euler(pitch[_frame], yaw[_frame], 0f);
            _frame++;
        }
    }
}
