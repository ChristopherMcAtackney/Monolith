using System.Collections.Generic;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The retro battle map (design section 5): blips on a radar-style plan, no 3D renderings of ships. The plan table shows it, and the seat's switch to the map draws it over
    /// the view. It is drawn by the HUD kit (<see cref="HudMap"/>) in whichever style the player chose, from the same contacts the radar uses, so the table, the seat and the
    /// HUD agree; this class only gathers them for the cabin's displays.
    /// </summary>
    public static class BlipMap
    {
        static readonly List<Contact> Contacts = new List<Contact>(64);

        /// <summary>The map's range (m): the smallest step that holds every enemy within 8 km and every capital ship in play (with a little hysteresis, so it does not flutter).</summary>
        public static float PickRange(GameWorld w, ShipState me)
        {
            HudContacts.Gather(w, me, Contacts);
            return HudContacts.PickMapRange(Contacts);
        }

        public static string RangeText(float m) => HudContacts.RangeText(m);

        /// <summary>Draw the map into <paramref name="L"/> within a circle of <paramref name="radius"/> pixels at <paramref name="centre"/>.</summary>
        public static void Draw(HudLines L, GameWorld w, Vector2 centre, float radius, float lw, Color ink, float time, float? fixedRange = null)
        {
            var me = w.Ships.Get(w.LocalPlayerShip);
            HudContacts.Gather(w, me, Contacts);
            float range = fixedRange ?? (me != null ? HudContacts.PickMapRange(Contacts) : 3000f);
            HudMap.Draw(L, Contacts, centre, radius, range, time, me != null);
        }
    }
}
