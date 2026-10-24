namespace Monolith.Core
{
    /// <summary>
    /// A flag between the audio system and the placeholders it replaces. The audio system sets <see cref="Owned"/> when it plays the lock tone and
    /// the missile warning itself; the HUD's stand-in tones (CombatAudio) then stay silent. Nothing else lives here.
    /// </summary>
    public static class AudioLink
    {
        public static bool Owned;
    }
}
