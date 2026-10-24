using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Mission
{
    /// <summary>
    /// What the game does for a mission (the Game assembly implements it with the flight model, the combat rig, the AI and the visuals; the
    /// mission system references Core only). Every call is made on the sim tick.
    /// </summary>
    public interface IMissionHost
    {
        /// <summary>The difficulty level (0 Very easy .. 4 Insane), its name, and a level's index by name.</summary>
        int Level { get; }
        string LevelName { get; }
        int LevelOf(string name);
        /// <summary>The AI flies the player's ship (soaks, captures, the auto-play check).</summary>
        bool PlayerIsAi { get; }

        /// <summary>Bring a ship into the world now: flight, combat, its look, its pilot. Returns its ship id.</summary>
        int Spawn(ShipSpawn spawn);

        /// <summary>
        /// An AI order: <paramref name="current"/> is what the ship does now (left alone if it is already doing exactly that), <paramref name="fallback"/>
        /// what it goes back to when an order is done. Either may be null. Targets are ship ids; a part is an index or -1. Returns true when
        /// anything changed.
        /// </summary>
        bool SetOrders(int ship, ShipOrder current, ShipOrder fallback);

        /// <summary>A waypoints order has flown its last leg.</summary>
        bool OrderFinished(int ship);

        /// <summary>Stop the ship's AI and fly it out: towards <paramref name="to"/> (or on along its heading), afterburner lit.</summary>
        void FlyOut(int ship, Vector3? to);

        /// <summary>Take a ship out of the world without an explosion (it jumped out or docked).</summary>
        void Remove(int ship);

        /// <summary>FS2's beam-free / beam-lock for a capital ship's heavy beams.</summary>
        void SetBeams(int ship, bool free);

        /// <summary>FS2's beam-fire: the capital ship's beams (and its helm) prefer <paramref name="target"/> while it is alive and in reach
        /// (<see cref="GameWorld.NoShip"/>: back to the nearest enemy capital).</summary>
        void SetBeamTarget(int ship, int target);

        /// <summary>A capital ship's or station's gun and flak turrets hold their fire (false) or fire at will (true).</summary>
        void SetTurrets(int ship, bool fire);

        /// <summary>Draw the mission's scenery (rocks and ice; nothing collides with it). Called when a run starts; a host draws it once however
        /// many times the mission is flown again, and a headless host not at all.</summary>
        void Scenery(MissionFile.SceneryDef scenery);

        /// <summary>Destroy one part (the cabin's knock-out), and give it back (a hand repair).</summary>
        void DestroyPart(int ship, int partIndex);
        void RestorePart(int ship, int partIndex);
    }

    /// <summary>Everything the host needs to bring one ship in.</summary>
    public sealed class ShipSpawn
    {
        public string Name, Class, Ai, Role;
        public int Team;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>Starting speed as a share of the class's top speed, and the throttle (0..1).</summary>
        public float Speed, Throttle;
        public bool IsPlayer, Wingman, Invulnerable;
        public bool? SelfHeal;
        public float HullScale = 1f;
        /// <summary>Hull at arrival, 0..1 (a damaged freighter).</summary>
        public float Hull01 = 1f;
        public Dictionary<string, int> Ammo;
        public MissionFile.CapitalDef Capital;
        public MissionFile.LookDef Look;
    }

    /// <summary>An AI order with names resolved to ship ids.</summary>
    public sealed class ShipOrder
    {
        public OrderKind Kind;
        public int Target = GameWorld.NoShip;
        public int Part = -1;
        public List<Vector3> Path;
        public float Speed = -1f;
        public string Then = "stop";
        public override string ToString() => $"{Kind} #{Target}" + (Part >= 0 ? $" part {Part}" : "");
    }

    /// <summary>
    /// The cabin as the mission sees it (design section 6, "What the mission system needs for it"). The cabin is not built yet: the runtime
    /// uses <see cref="CabinStub"/> until the cabin system implements this.
    /// </summary>
    public interface ICabin
    {
        bool Seated { get; }
        bool Powered { get; }
        /// <summary>Has a scan from <paramref name="ship"/> (or any, <see cref="GameWorld.NoShip"/>) detected the player?</summary>
        bool DetectedBy(int ship);
        /// <summary>Does the ship's part (or any part, null) need a hand repair?</summary>
        bool NeedsRepair(int ship, string part);
        void StartScan(int fromShip, float seconds, float range, float now);
        void CallOut(string prompt);
        void KnockOut(int ship, string part, float now);
        void SetLights(string state);
        void Step(MissionRuntime rt);
        string Lights { get; }
    }

    /// <summary>
    /// The stand-in cabin until the real one exists: the pilot is always seated and the ship is powered unless a test says otherwise. A scan
    /// sweep detects the player if the player's ship is within its range while powered at any moment of the sweep (design: detection depends on
    /// the power state; the mission decides what it means). A knocked-out part stays destroyed until repaired; with <see cref="AutoRepairSeconds"/>
    /// set (headless runs with the AI flying), it is put back that long after, as if the pilot had got up and fixed it.
    /// </summary>
    public sealed class CabinStub : ICabin
    {
        sealed class Sweep { public int From; public float Ends, Range; public bool Hit; }
        sealed class Knock { public int Ship; public string Part; public float At; public bool Repaired; }

        readonly List<Sweep> _sweeps = new List<Sweep>();
        readonly List<Knock> _knocks = new List<Knock>();
        readonly HashSet<int> _detectedBy = new HashSet<int>();

        public bool Seated { get; set; } = true;
        public bool Powered { get; set; } = true;
        public string Lights { get; private set; } = "normal";
        public string LastPrompt { get; private set; }
        public float AutoRepairSeconds = -1f;
        /// <summary>A player who obeys every "go dark": the power is off from the moment a sweep starts until the last one ends (auto-play's -mnGoDark).</summary>
        public bool GoesDark;

        public bool DetectedBy(int ship) => ship == GameWorld.NoShip ? _detectedBy.Count > 0 : _detectedBy.Contains(ship);

        public bool NeedsRepair(int ship, string part)
        {
            foreach (var k in _knocks) if (!k.Repaired && k.Ship == ship && (part == null || k.Part == part)) return true;
            return false;
        }

        public void StartScan(int fromShip, float seconds, float range, float now)
        {
            _sweeps.Add(new Sweep { From = fromShip, Ends = now + seconds, Range = range });
            if (GoesDark) Powered = false;
        }
        public void CallOut(string prompt) => LastPrompt = prompt;
        public void KnockOut(int ship, string part, float now) => _knocks.Add(new Knock { Ship = ship, Part = part, At = now });
        public void SetLights(string state) => Lights = state;

        /// <summary>Put a knocked-out part back (what the cabin's hand repair will do).</summary>
        public void Repair(MissionRuntime rt, int ship, string part)
        {
            foreach (var k in _knocks)
            {
                if (k.Repaired || k.Ship != ship || (part != null && k.Part != part)) continue;
                k.Repaired = true;
                var c = rt.World.Combat.Get(ship);
                var p = c?.FindPart(k.Part);
                if (p != null) rt.Host.RestorePart(ship, p.Index);
                rt.Note("cabin", $"hand repair of {k.Part} done (stub)");
            }
        }

        public void Step(MissionRuntime rt)
        {
            var player = rt.World.Ships.Get(rt.World.LocalPlayerShip);
            for (int i = _sweeps.Count - 1; i >= 0; i--)
            {
                var s = _sweeps[i];
                var from = rt.World.Ships.Get(s.From);
                if (from != null && player != null && Powered && Vector3.Distance(from.Position, player.Position) < s.Range) s.Hit = true;
                if (rt.Now < s.Ends) continue;
                if (s.Hit && _detectedBy.Add(s.From)) rt.Note("cabin", $"scan from {from?.Callsign ?? "#" + s.From} detected the player (stub: powered)");
                else if (!s.Hit) rt.Note("cabin", "scan swept past without a contact (stub)");
                _sweeps.RemoveAt(i);
                if (GoesDark && _sweeps.Count == 0) { Powered = true; rt.Note("cabin", "power back on after the sweep (stub)"); }
            }
            if (AutoRepairSeconds >= 0f)
                foreach (var k in _knocks)
                    if (!k.Repaired && rt.Now >= k.At + AutoRepairSeconds) Repair(rt, k.Ship, k.Part);
        }
    }
}
