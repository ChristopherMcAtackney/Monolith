using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monolith.ShipArt
{
    public enum SubsystemState { Intact, Damaged, Destroyed }

    /// <summary>One detachable chunk of a wrecked subsystem: where it sits on the ship, and how it flies off when it lets go.</summary>
    [Serializable]
    public sealed class DebrisPiece
    {
        public Transform Piece;
        /// <summary>Resting place and ejection velocity (m/s) and spin (degrees/s), in the subsystem's local axes.</summary>
        public Vector3 Origin, Velocity, SpinDegPerSec;
    }

    /// <summary>
    /// A named, separately targetable and destroyable part of a ship model: a flak battery, an engine, the bridge, a sensor array.
    /// ShipArtSetup puts it on the <c>SUB_&lt;kind&gt;_&lt;name&gt;</c> object of a ship prefab from the model's manifest. The object's
    /// transform is the pivot: its forward (+z) is where the part points; <see cref="Radius"/> is its size in metres for a target
    /// bracket or damage volume.
    ///
    /// The combat system drives it through <see cref="SetHealth"/> or <see cref="SetState"/>; nothing else needs to know how it looks:
    /// intact parts, an optional half-wrecked "damaged" look and a blackened "destroyed" stump are separate child objects
    /// (the model script builds them), and the wreck throws <see cref="Debris"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipSubsystem : MonoBehaviour
    {
        public const string Engine = "engine", Flak = "flak", AntiFighter = "aaf", Bridge = "bridge", Sensors = "sensors", Comms = "comms", Reactor = "reactor", Launcher = "launcher";

        /// <summary>What kind of part this is (see the constants); the model script chooses it.</summary>
        public string Kind;
        /// <summary>Size of the part in metres (half the extent of its target box).</summary>
        public float Radius = 1f;
        /// <summary>Relative hit points: 1 is a typical part; a bridge or reactor may be more. The ship's damage data scales it.</summary>
        public float Hp = 1f;
        public string[] Tags = new string[0];

        [SerializeField] GameObject[] _intact = new GameObject[0], _damaged = new GameObject[0], _destroyed = new GameObject[0];
        [SerializeField] DebrisPiece[] _debris = new DebrisPiece[0];

        public SubsystemState State { get; private set; } = SubsystemState.Intact;
        /// <summary>1 = untouched, 0 = destroyed.</summary>
        public float Health01 { get; private set; } = 1f;
        public bool Destroyed => State == SubsystemState.Destroyed;
        public IReadOnlyList<DebrisPiece> Debris => _debris;

        /// <summary>Raised after the look changed (old state, new state).</summary>
        public event Action<ShipSubsystem, SubsystemState, SubsystemState> StateChanged;

        /// <summary>Called by ShipArtSetup when it builds the prefab.</summary>
        public void Configure(GameObject[] intact, GameObject[] damaged, GameObject[] destroyed, DebrisPiece[] debris)
        {
            _intact = intact; _damaged = damaged; _destroyed = destroyed; _debris = debris;
            foreach (var d in _debris) if (d.Piece != null) d.Piece.gameObject.SetActive(false);
            foreach (var o in _destroyed) if (o != null) o.SetActive(false);
            foreach (var o in _damaged) if (o != null) o.SetActive(false);
        }

        public bool HasDamagedLook => _damaged.Length > 0;
        public bool HasWreck => _destroyed.Length > 0;

        /// <summary>0 to 1: at 0 the part is destroyed; below <paramref name="damagedBelow"/> it shows its damaged look (if the model has one).</summary>
        public void SetHealth(float health01, float damagedBelow = 0.5f)
        {
            float h = Mathf.Clamp01(health01);
            SetState(h <= 0f ? SubsystemState.Destroyed : h < damagedBelow ? SubsystemState.Damaged : SubsystemState.Intact);
            Health01 = h;
        }

        /// <param name="debrisAge">Seconds since the wreck let go of its debris: a destroyed part shows its chunks that far into their flight (0 = just left the hull).</param>
        public void SetState(SubsystemState state, float debrisAge = 0f)
        {
            var old = State;
            State = state;
            if (state == SubsystemState.Destroyed) Health01 = 0f;
            else if (state == SubsystemState.Intact) Health01 = 1f;
            Show(_intact, state == SubsystemState.Intact || (state == SubsystemState.Damaged && _damaged.Length == 0));
            Show(_damaged, state == SubsystemState.Damaged && _damaged.Length > 0);
            Show(_destroyed, state == SubsystemState.Destroyed);
            foreach (var d in _debris) if (d.Piece != null) d.Piece.gameObject.SetActive(state == SubsystemState.Destroyed);
            if (state == SubsystemState.Destroyed) PoseDebris(debrisAge);
            if (old != state) StateChanged?.Invoke(this, old, state);
        }

        static void Show(GameObject[] objs, bool on)
        {
            foreach (var o in objs) if (o != null && o.activeSelf != on) o.SetActive(on);
        }

        /// <summary>Put the chunks <paramref name="age"/> seconds into a straight-line flight from their resting place (still children of the ship).</summary>
        public void PoseDebris(float age)
        {
            foreach (var d in _debris)
            {
                if (d.Piece == null) continue;
                d.Piece.localPosition = d.Origin + d.Velocity * age;
                d.Piece.localRotation = Quaternion.Euler(d.SpinDegPerSec * age);
            }
        }

        /// <summary>Let the chunks go: they leave the ship's hierarchy and the caller (effects, physics) owns their motion from here. Returns each
        /// piece with its world velocity relative to the ship and its spin.</summary>
        public List<(Transform piece, Vector3 velocityRelativeToShip, Vector3 spinDegPerSec)> DetachDebris()
        {
            var list = new List<(Transform, Vector3, Vector3)>();
            foreach (var d in _debris)
            {
                if (d.Piece == null) continue;
                var v = transform.TransformDirection(d.Velocity);
                d.Piece.SetParent(null, true);
                list.Add((d.Piece, v, d.SpinDegPerSec));
            }
            return list;
        }
    }
}
