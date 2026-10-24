using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.View
{
    /// <summary>
    /// The player's first-person view, and the only camera there is: it sits at the local player's ship (interpolated
    /// between sim ticks with <see cref="GameWorld.Alpha"/>, so motion is smooth at any frame rate while the sim stays at
    /// 60 Hz), and draws the motion dust (and placeholder stars, for scenes without the Sky system). There is no external
    /// camera of any kind.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class CockpitView : MonoBehaviour
    {
        public const string ShaderPath = "Shaders/MN_Lines";

        GameWorld _world;
        ViewSettings _settings;
        Camera _cam;
        SpaceDust _dust;
        Vector3 _velocity;
        bool _kicked;
        bool _wasDeath;

        public Camera Camera => _cam;
        public ViewSettings Settings => _settings;

        /// <summary>Build the camera (HDRP, black space, no sky drawn), its line pass, and make it GameWorld.MainCamera.</summary>
        public static CockpitView Create(GameWorld world, Transform parent, ViewSettings settings = null)
        {
            settings ??= ViewSettings.Load();
            var go = new GameObject("CockpitCamera") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = settings.camera.fovVertical;
            cam.nearClipPlane = settings.camera.near;
            cam.farClipPlane = settings.camera.far;
            cam.allowHDR = true;
            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            hd.stopNaNs = true;
            hd.dithering = true;

            var view = go.AddComponent<CockpitView>();
            view._world = world;
            view._settings = settings;
            view._cam = cam;
            view._dust = new SpaceDust(settings.dust, go.transform.position);
            world.MainCamera = cam;

            var shader = Resources.Load<Shader>(ShaderPath) ?? throw new FileNotFoundException($"Resources/{ShaderPath}.shader is missing");
            var volume = go.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            volume.priority = 10f;     // before the HUD's pass
            volume.customPasses.Add(new ViewLinesPass
            {
                name = "MN stars and dust",
                Material = new Material(shader) { name = "MN_ViewLines" },
                Stars = settings.stars.count > 0 ? Starfield.Build(settings.stars) : null,
                Dust = view._dust,
                Camera = cam,
            });
            view.Follow();
            return view;
        }

        void LateUpdate() => Follow();

        void Follow()
        {
            var death = _world.Death;
            if (death.CameraActive)
            {
                // The death sequence has the eye (first person throughout): its pose, its field of view, its speed for the dust.
                transform.SetPositionAndRotation(death.Position, death.Rotation);
                _cam.fieldOfView = death.Fov;
                _wasDeath = true; _kicked = false;
                _velocity = death.Velocity;
                _dust.Update(transform.position, transform.forward, _velocity, _cam.nearClipPlane, 1f, 1f - death.Dark);
                return;
            }
            if (_wasDeath) { _wasDeath = false; _cam.fieldOfView = _settings.camera.fovVertical; }
            var ship = _world.Ships.Get(_world.LocalPlayerShip);
            if (ship != null)
            {
                float a = _world.Alpha;
                var rot = ship.RenderRotation(a);
                var cab = _world.Cabin;
                if (cab != null && cab.OwnsCamera)
                {
                    // The pilot is up (Monolith.Cabin): the eye is where the pilot's is in the cabin, and the view is wider the more they stand.
                    transform.SetPositionAndRotation(ship.RenderPosition(a) + rot * cab.Eye(a), rot * cab.Rot(a));
                    _cam.fieldOfView = Mathf.Lerp(_settings.camera.fovVertical, cab.Fov > 0f ? cab.Fov : _settings.camera.fovVertical, cab.Stand01);
                }
                else
                {
                    transform.SetPositionAndRotation(ship.RenderPosition(a) + rot * _settings.camera.Eye, rot);
                    if (cab != null) _cam.fieldOfView = _settings.camera.fovVertical;
                    // The boost widens the view (Juice/BoostFx writes the kick). Only touched while there is a kick, and once more to put it back.
                    if (_world.FovKick != 0f || _kicked) { _cam.fieldOfView = _settings.camera.fovVertical + _world.FovKick; _kicked = _world.FovKick != 0f; }
                }
                _velocity = ship.Velocity;
            }
            _dust.Update(transform.position, transform.forward, _velocity, _cam.nearClipPlane, _world.DustStretch, _world.DustBright);
        }
    }

    /// <summary>Draws stars on the far plane and dust streaks depth-tested against the scene, after post-processing.</summary>
    sealed class ViewLinesPass : CustomPass
    {
        public Material Material;
        public Mesh Stars;
        public SpaceDust Dust;
        public Camera Camera;
        readonly MaterialPropertyBlock _starProps = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock _dustProps = new MaterialPropertyBlock();
        static readonly int MatrixId = Shader.PropertyToID("_MN_Matrix");
        static readonly int ScreenId = Shader.PropertyToID("_MN_Screen");
        static readonly int HalfWidthId = Shader.PropertyToID("_MN_HalfWidth");

        protected override bool executeInSceneView => false;

        protected override void Execute(CustomPassContext ctx)
        {
            var cam = ctx.hdCamera.camera;
            if (cam != Camera || Material == null) return;
            float w = ctx.hdCamera.actualWidth, h = ctx.hdCamera.actualHeight;
            float scale = h / 1080f;
            var screen = new Vector4(w, h, 1f / w, 1f / h);
            // Camera-relative view: the dust mesh is built relative to the eye, and stars only need the rotation.
            var view = cam.worldToCameraMatrix;
            view.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            var m = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * view;

            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
            _starProps.SetMatrix(MatrixId, m);
            _starProps.SetVector(ScreenId, screen);
            if (Stars != null) ctx.cmd.DrawMesh(Stars, Matrix4x4.identity, Material, 0, 0, _starProps);
            _dustProps.SetMatrix(MatrixId, m);
            _dustProps.SetVector(ScreenId, screen);
            _dustProps.SetFloat(HalfWidthId, Dust.HalfWidthPx * Mathf.Max(1f, scale));
            ctx.cmd.DrawMesh(Dust.Mesh, Matrix4x4.identity, Material, 0, 1, _dustProps);
        }
    }

    /// <summary>Places each ship's visual at its interpolated sim pose every frame. The local player's ship has none.</summary>
    public sealed class ShipViews : MonoBehaviour
    {
        GameWorld _world;
        readonly Dictionary<int, Transform> _views = new Dictionary<int, Transform>();
        readonly List<int> _gone = new List<int>();

        public static ShipViews Create(GameWorld world, Transform parent)
        {
            var go = new GameObject("ShipViews");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<ShipViews>();
            v._world = world;
            return v;
        }

        public void Register(int shipId, Transform visual)
        {
            if (visual != null) { var h = visual.GetComponent<ShipHandle>() ?? visual.gameObject.AddComponent<ShipHandle>(); h.Id = shipId; }
            _views[shipId] = visual;
            Place(shipId, visual);
        }

        void LateUpdate()
        {
            _gone.Clear();
            foreach (var kv in _views)
                if (!Place(kv.Key, kv.Value)) _gone.Add(kv.Key);
            foreach (var id in _gone)
            {
                if (_views[id] != null) Destroy(_views[id].gameObject);
                _views.Remove(id);
            }
        }

        bool Place(int id, Transform t)
        {
            var s = _world.Ships.Get(id);
            if (s == null || t == null) return false;
            float a = _world.Alpha;
            t.SetPositionAndRotation(s.RenderPosition(a), s.RenderRotation(a));
            return true;
        }
    }
}
