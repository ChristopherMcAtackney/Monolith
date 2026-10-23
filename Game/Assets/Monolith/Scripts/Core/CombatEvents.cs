namespace Monolith.Core
{
    /// <summary>
    /// The event ids that combat systems raise and the presentation (juice: effects, shake, rumble, sound) listens for.
    /// Weapons, AI and missions raise them with <see cref="GameEvents.RaiseAt"/> and know nothing about what happens next.
    /// Every event names who it concerns: <c>Entity</c> is the ship it mostly concerns (the shooter, the victim),
    /// <c>Other</c> the second ship (the target of a beam, the attacker of a hit), <c>Position</c> and <c>Direction</c> say where.
    ///
    /// <code>
    ///   id                  Entity        Other        Position / Direction                          Value               Subject
    ///   weapon_fired        shooter       target       muzzle / aim (unit)                           speed m/s (0 = default)  weapon id: laser_*, flak_*, missile_*, torpedo_*
    ///   hit_shield          ship hit      attacker     impact point / incoming travel direction      damage              weapon id
    ///   hit_hull            ship hit      attacker     impact point / surface normal (or travel)     damage              weapon id
    ///   near_miss           shooter       ship passed  closest point to it / travel direction        miss distance m     weapon id
    ///   ship_destroyed      ship          killer       centre / -                                    radius m (0 = ask the registry)   size class: fighter, bomber, corvette, capital (or null)
    ///   subsystem_destroyed ship          killer       the subsystem / outward normal                size m              subsystem name
    ///   explosion           -             -            centre / -                                    radius m            "small", "medium", "large"
    ///   flak_burst          shooter       -            centre / -                                    burst radius m      weapon id
    ///   beam_charge         source ship   target       muzzle / aim                                  charge seconds      beam id
    ///   beam_fire           source ship   target       muzzle / aim                                  duration seconds    beam id
    ///   beam_aim            source ship   target       muzzle / aim, once per tick while firing      -                   beam id   (optional: without it the beam sweeps the target by itself)
    ///   beam_end            source ship   -            -                                             -                   beam id
    /// </code>
    /// A ship that wants to break up on screen registers its visual with the juice director; nothing else about it is needed.
    /// </summary>
    public static class CombatEvents
    {
        public const string WeaponFired = "weapon_fired";
        public const string HitShield = "hit_shield";
        public const string HitHull = "hit_hull";
        public const string NearMiss = "near_miss";
        public const string ShipDestroyed = "ship_destroyed";
        public const string SubsystemDestroyed = "subsystem_destroyed";
        public const string Explosion = "explosion";
        public const string FlakBurst = "flak_burst";
        public const string BeamCharge = "beam_charge";
        public const string BeamFire = "beam_fire";
        public const string BeamAim = "beam_aim";
        public const string BeamEnd = "beam_end";

        public static readonly string[] All =
        {
            WeaponFired, HitShield, HitHull, NearMiss, ShipDestroyed, SubsystemDestroyed, Explosion, FlakBurst,
            BeamCharge, BeamFire, BeamAim, BeamEnd,
        };

        /// <summary>Size classes for <see cref="ShipDestroyed"/>'s subject.</summary>
        public const string SizeFighter = "fighter", SizeBomber = "bomber", SizeCorvette = "corvette", SizeCapital = "capital";
    }
}
