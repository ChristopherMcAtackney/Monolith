using System;
using System.Collections.Generic;

namespace Monolith.Core
{
    /// <summary>Anything that decides what a ship does: the player's pad, an AI, a scripted test, later a remote player.</summary>
    public interface IPilotSource
    {
        ControlInput Sample(in SimFrame frame);
    }

    /// <summary>
    /// Every ship's <see cref="ControlInput"/>, refreshed once per tick (order <see cref="SimOrder.Pilots"/>). Flight and
    /// weapons read <see cref="Current"/> by ship id and never know who is flying.
    /// </summary>
    public sealed class Pilots : ISimSystem
    {
        sealed class Slot
        {
            public IPilotSource Source;
            public ControlInput Current, Previous;
        }

        readonly Dictionary<int, Slot> _slots = new Dictionary<int, Slot>();

        public int Order => SimOrder.Pilots;
        public int Count => _slots.Count;

        public void Attach(int shipId, IPilotSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _slots[shipId] = new Slot { Source = source };
        }

        public void Detach(int shipId) => _slots.Remove(shipId);

        /// <summary>This tick's input for the ship (neutral when it has no pilot).</summary>
        public ControlInput Current(int shipId) => _slots.TryGetValue(shipId, out var s) ? s.Current : default;

        /// <summary>The previous tick's input, for edge detection with <see cref="ControlInput.Pressed"/>.</summary>
        public ControlInput Previous(int shipId) => _slots.TryGetValue(shipId, out var s) ? s.Previous : default;

        public void Step(in SimFrame frame)
        {
            foreach (var s in _slots.Values)
            {
                s.Previous = s.Current;
                s.Current = s.Source.Sample(frame).Sanitized();
            }
        }
    }

    /// <summary>A pilot that plays back keyed inputs by simulated time (tests, capture plans): each key holds until the next.</summary>
    public sealed class ScriptedPilot : IPilotSource
    {
        public readonly struct Key
        {
            public readonly float Time;
            public readonly ControlInput Input;
            public Key(float time, ControlInput input) { Time = time; Input = input; }
        }

        readonly Key[] _keys;

        public ScriptedPilot(IEnumerable<Key> keys)
        {
            var list = new List<Key>(keys);
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            _keys = list.ToArray();
        }

        public ControlInput Sample(in SimFrame frame)
        {
            var result = default(ControlInput);
            for (int i = 0; i < _keys.Length && _keys[i].Time <= frame.Time + 1e-5f; i++) result = _keys[i].Input;
            return result;
        }
    }
}
