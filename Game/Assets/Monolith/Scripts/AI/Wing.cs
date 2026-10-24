using System.Collections.Generic;
using Monolith.Core;

namespace Monolith.AI
{
    public enum WingOrder { None = 0, Attack = 1, Disable = 2, Cover = 3, Form = 4 }

    /// <summary>
    /// The player's wingmen and the four orders from the quick menu (design section 5): attack my target, disable my target, cover me, form up.
    /// The orders arrive as the player's <see cref="ControlInput"/> buttons (<see cref="ControlButtons.OrderAttack"/>...), whoever presses them
    /// (the pad, a script, later a remote player). Each order is acknowledged on the radio by one wingman, in turn.
    /// </summary>
    public sealed class Wing
    {
        readonly AiDirector _ai;
        readonly List<AiPilot> _members = new List<AiPilot>();
        int _speaker;

        public IReadOnlyList<AiPilot> Members => _members;
        public WingOrder Current { get; private set; }
        /// <summary>The ship that gives the orders (the local player's, unless set).</summary>
        public int Leader = GameWorld.NoShip;

        public Wing(AiDirector ai) { _ai = ai; }

        int LeaderId => Leader != GameWorld.NoShip ? Leader : _ai.World.LocalPlayerShip;

        public void Add(AiPilot p)
        {
            if (_members.Contains(p)) return;
            p.WingSlot = _members.Count;
            _members.Add(p);
            ApplyLevel(p);
        }

        /// <summary>The level's mercy on the player's wingmen (ours: FS2 spares only the player; the wing it commands is spared alike).</summary>
        public void ApplyLevel(AiPilot p)
        {
            var c = _ai.World.Combat.Get(p.Ship);
            if (c != null) c.DamageTakenScale = _ai.Data.Difficulty.WingDamageScale[_ai.Level];
        }

        public void Remove(int ship) => _members.RemoveAll(m => m.Ship == ship);
        public void Clear() { _members.Clear(); Current = WingOrder.None; }

        public void Step(in SimFrame frame)
        {
            int leader = LeaderId;
            if (leader == GameWorld.NoShip || _members.Count == 0) return;
            // The director runs before this tick's inputs are sampled: compare the last two (an order lands one tick later, unnoticed).
            var now = _ai.World.Pilots.Current(leader);
            var before = _ai.World.Pilots.Previous(leader);
            if (now.Pressed(before, ControlButtons.OrderAttack)) Give(WingOrder.Attack);
            else if (now.Pressed(before, ControlButtons.OrderDisable)) Give(WingOrder.Disable);
            else if (now.Pressed(before, ControlButtons.OrderCover)) Give(WingOrder.Cover);
            else if (now.Pressed(before, ControlButtons.OrderForm)) Give(WingOrder.Form);
        }

        AiPilot NextSpeaker()
        {
            for (int k = 0; k < _members.Count; k++)
            {
                var m = _members[(_speaker + k) % _members.Count];
                var c = _ai.World.Combat.Get(m.Ship);
                if (c != null && c.Alive) { _speaker = (_speaker + k + 1) % _members.Count; return m; }
            }
            return null;
        }

        /// <summary>Give the wing an order (the quick menu does this through the buttons). Returns false when it cannot be carried out.</summary>
        public bool Give(WingOrder order)
        {
            int leader = LeaderId;
            var ls = _ai.World.Ships.Get(leader);
            if (ls == null) return false;
            int target = ls.Target;
            var tc = _ai.World.Combat.Get(target);
            bool hostileTarget = tc != null && tc.Alive && Teams.Hostile(ls.Team, tc.Ship.Team);
            var speaker = NextSpeaker();
            if (speaker == null) return false;
            if ((order == WingOrder.Attack || order == WingOrder.Disable) && !hostileTarget)
            {
                _ai.Radio.Say("no_target", speaker.Ship, force: true);
                return false;
            }
            Current = order;
            foreach (var m in _members)
            {
                var c = _ai.World.Combat.Get(m.Ship);
                if (c == null || !c.Alive) continue;
                switch (order)
                {
                    case WingOrder.Attack: m.SetGoal(new AiGoal { Kind = GoalKind.Attack, Target = target, Part = ls.TargetPart }); break;
                    case WingOrder.Disable: m.SetGoal(new AiGoal { Kind = GoalKind.Disable, Target = target, Part = -1 }); break;
                    case WingOrder.Cover: m.SetGoal(new AiGoal { Kind = GoalKind.Guard, Target = leader, Part = -1 }); break;
                    case WingOrder.Form: m.SetGoal(new AiGoal { Kind = GoalKind.Form, Target = leader, Part = -1 }); break;
                }
            }
            GameEvents.RaiseAt(CombatEvents.WingOrder, ls.Position, ls.Forward, order.ToString().ToLowerInvariant(), (int)order, leader,
                order == WingOrder.Attack || order == WingOrder.Disable ? target : GameWorld.NoShip);
            string situation = order == WingOrder.Attack ? "ack_attack" : order == WingOrder.Disable ? "ack_disable" : order == WingOrder.Cover ? "ack_cover" : "ack_form";
            _ai.Radio.Say(situation, speaker.Ship, force: true);
            MnLog.Info($"WING order {order} target #{target} from #{leader}");
            return true;
        }
    }
}
