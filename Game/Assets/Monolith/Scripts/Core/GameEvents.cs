using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monolith.Core
{
    /// <summary>One thing that happened, for whoever is listening (mission, HUD, audio, juice).</summary>
    public readonly struct GameEvent
    {
        /// <summary>What happened, snake_case: "boot_complete", "ship_destroyed", "radio_line"...</summary>
        public readonly string Id;
        /// <summary>Who or what it concerns by name (a callsign, a subsystem), or null.</summary>
        public readonly string Subject;
        /// <summary>Optional number (damage, a hull fraction, a count).</summary>
        public readonly float Value;
        /// <summary>The ship it concerns, or <see cref="GameWorld.NoShip"/>.</summary>
        public readonly int Entity;
        /// <summary>The simulation tick it was raised on.</summary>
        public readonly int Tick;

        public GameEvent(string id, string subject, float value, int entity, int tick)
        {
            Id = id; Subject = subject; Value = value; Entity = entity; Tick = tick;
        }

        public override string ToString() =>
            $"{Id}" + (Subject != null ? $"({Subject})" : "") + (Value != 0f ? $"={Value.ToString("0.###", CultureInfo.InvariantCulture)}" : "")
            + (Entity != GameWorld.NoShip ? $" ship#{Entity}" : "") + $"@{Tick}";
    }

    /// <summary>Ids that more than one system needs to agree on. Add yours here with a note of who raises it.</summary>
    public static class EventIds
    {
        /// <summary>Game/GameRoot, once everything is built and the sim is running.</summary>
        public const string BootComplete = "boot_complete";
    }

    /// <summary>
    /// A small synchronous event bus so systems can announce things without referencing each other. Raise from inside
    /// <see cref="ISimSystem.Step"/> (keeps runs repeatable); handlers run immediately, in subscription order, and one
    /// that throws is logged without stopping the rest.
    /// <code>
    ///   var sub = GameEvents.Subscribe("ship_destroyed", e => Debug.Log(e));
    ///   GameEvents.Raise("ship_destroyed", "Alpha 2", entity: 7);
    ///   sub.Dispose();
    /// </code>
    /// </summary>
    public static class GameEvents
    {
        const int RecentMax = 64;

        static readonly Dictionary<string, Action<GameEvent>[]> ById = new Dictionary<string, Action<GameEvent>[]>();
        static Action<GameEvent>[] _all = Array.Empty<Action<GameEvent>>();
        static readonly List<GameEvent> _recent = new List<GameEvent>();

        /// <summary>The last few events, newest last (debugging, late listeners).</summary>
        public static IReadOnlyList<GameEvent> Recent => _recent;

        public static void Raise(string id, string subject = null, float value = 0f, int entity = GameWorld.NoShip)
        {
            if (string.IsNullOrEmpty(id)) return;
            var world = GameWorld.Current;
            var e = new GameEvent(id, subject, value, entity, world != null ? world.Tick : 0);
            _recent.Add(e);
            if (_recent.Count > RecentMax) _recent.RemoveAt(0);
            if (ById.TryGetValue(id, out var handlers)) Invoke(handlers, e);
            Invoke(_all, e);
        }

        /// <summary>Listen for one event id. Dispose the result to stop listening.</summary>
        public static IDisposable Subscribe(string id, Action<GameEvent> handler)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("event id is required", nameof(id));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ById.TryGetValue(id, out var current);
            ById[id] = Append(current, handler);
            return new Subscription(() =>
            {
                if (!ById.TryGetValue(id, out var cur)) return;
                var next = Remove(cur, handler);
                if (next.Length == 0) ById.Remove(id); else ById[id] = next;
            });
        }

        /// <summary>Listen for every event (loggers, the juice sandbox).</summary>
        public static IDisposable SubscribeAll(Action<GameEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _all = Append(_all, handler);
            return new Subscription(() => _all = Remove(_all, handler));
        }

        /// <summary>Drop every listener and the history (a new game session).</summary>
        public static void Reset()
        {
            ById.Clear();
            _all = Array.Empty<Action<GameEvent>>();
            _recent.Clear();
        }

        static void Invoke(Action<GameEvent>[] handlers, GameEvent e)
        {
            // Arrays are replaced, never edited, so (un)subscribing from inside a handler is safe.
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](e); }
                catch (Exception ex) { MnLog.Error($"handler for '{e.Id}' threw: {ex}"); }
            }
        }

        static Action<GameEvent>[] Append(Action<GameEvent>[] src, Action<GameEvent> h)
        {
            int n = src?.Length ?? 0;
            var next = new Action<GameEvent>[n + 1];
            if (n > 0) Array.Copy(src, next, n);
            next[n] = h;
            return next;
        }

        static Action<GameEvent>[] Remove(Action<GameEvent>[] src, Action<GameEvent> h)
        {
            int i = Array.IndexOf(src, h);
            if (i < 0) return src;
            var next = new Action<GameEvent>[src.Length - 1];
            Array.Copy(src, 0, next, 0, i);
            Array.Copy(src, i + 1, next, i, src.Length - i - 1);
            return next;
        }

        sealed class Subscription : IDisposable
        {
            Action _dispose;
            public Subscription(Action dispose) { _dispose = dispose; }
            public void Dispose() { var d = _dispose; _dispose = null; d?.Invoke(); }
        }
    }
}
