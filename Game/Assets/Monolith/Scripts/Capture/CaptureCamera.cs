using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Capture
{
    /// <summary>
    /// Pins a camera to the pose a shot asks for. The pose is re-applied right before the camera renders, so gameplay
    /// camera scripts cannot move the critic's camera. Dispose to stop pinning and put the camera back.
    /// </summary>
    public sealed class CaptureCamera : IDisposable
    {
        readonly Camera _cam;
        readonly Pose _restorePose;
        readonly float _restoreFov;
        Pose _pose;
        float _fov;
        bool _active = true;
        readonly bool _follow;

        public CaptureCamera(Camera cam, ShotBase shot, Pose basePose, float baseFov)
        {
            _cam = cam;
            _restorePose = new Pose(cam.transform.position, cam.transform.rotation);
            _restoreFov = cam.fieldOfView;

            var p = basePose.position;
            var r = basePose.rotation;
            if (shot.pos != null) p = V3(shot.pos);
            if (shot.lookAt != null) r = Quaternion.LookRotation(V3(shot.lookAt) - p, Vector3.up);
            else if (shot.rot != null) r = Quaternion.Euler(V3(shot.rot));
            _pose = new Pose(p, r);
            _fov = shot.fov ?? baseFov;
            _follow = shot.follow == true;

            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Apply();
        }

        public string Description => _follow ? $"{_cam.name} following the game (fov {_fov:0.#})" :
            $"{_cam.name} pos {_pose.position.x:0.###},{_pose.position.y:0.###},{_pose.position.z:0.###} " +
            $"euler {_pose.rotation.eulerAngles.x:0.#},{_pose.rotation.eulerAngles.y:0.#},{_pose.rotation.eulerAngles.z:0.#} fov {_fov:0.#}";

        public void Apply()
        {
            if (!_active || _cam == null) return;
            if (!_follow) _cam.transform.SetPositionAndRotation(_pose.position, _pose.rotation);
            _cam.fieldOfView = _fov;
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c == _cam) Apply();
        }

        public void Dispose()
        {
            if (!_active) return;
            _active = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (_cam != null)
            {
                if (!_follow) _cam.transform.SetPositionAndRotation(_restorePose.position, _restorePose.rotation);
                _cam.fieldOfView = _restoreFov;
            }
        }

        static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);
    }
}
