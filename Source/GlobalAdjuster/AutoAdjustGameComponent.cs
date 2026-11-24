using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// Drives "Auto" mode: while <see cref="GlobalAdjusterSettings.autoEnabled"/> is set,
    /// continuously forces every free-colonist pair's compatibility to
    /// <see cref="GlobalAdjusterSettings.autoTarget"/> (absolute).
    /// <para>
    /// RimWorld auto-instantiates every concrete <see cref="GameComponent"/> with a
    /// <c>(Game)</c> ctor found in loaded assemblies — no Def required (the base mod's
    /// own override store proves this mechanism).
    /// </para>
    /// The tick handler is throttled (~every <see cref="ThrottleTicks"/> ticks) and the
    /// underlying <see cref="GlobalAdjustment.Apply"/> dirty-checks each pair, so newly
    /// arrived colonists (recruits, births, freed prisoners) are snapped to the target on
    /// the next pass without thrashing the override dictionary every tick.
    /// </summary>
    public class AutoAdjustGameComponent : GameComponent
    {
        // ~250 ticks ≈ 4s at normal speed: imperceptible delay, negligible CPU.
        private const int ThrottleTicks = 250;

        public AutoAdjustGameComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            GlobalAdjusterSettings settings = GlobalAdjusterMod.Settings;
            if (settings == null || !settings.autoEnabled)
                return;

            if (Find.TickManager.TicksGame % ThrottleTicks != 0)
                return;

            float min = BaseModAccess.SliderMin;
            float max = BaseModAccess.SliderMax;

            // Auto is absolute-only. Suppress messages (this fires repeatedly).
            GlobalAdjustment.Apply(AdjustMode.Absolute, settings.autoTarget, min, max, showMessages: false);
        }
    }
}
