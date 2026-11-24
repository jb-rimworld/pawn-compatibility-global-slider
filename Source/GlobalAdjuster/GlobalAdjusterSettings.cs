using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// Global (per-user) settings for the add-on, persisted by RimWorld's
    /// <see cref="ModSettings"/> mechanism (matches how the base mod stores its
    /// sliderMin/sliderMax). These are NOT per-save; the actual compatibility
    /// overrides still live in the base mod's per-save store.
    /// </summary>
    public class GlobalAdjusterSettings : ModSettings
    {
        /// <summary>Relative (shift by) vs Absolute (set to) for the one-shot apply.</summary>
        public AdjustMode mode = AdjustMode.Relative;

        /// <summary>The one-shot value used by the "Apply now" / in-dialog button.</summary>
        public float adjustValue = 0f;

        /// <summary>
        /// DEFAULT for new games (and, once, for saves that never had a per-save value): whether Auto mode
        /// starts enabled. The live value lives per save in <see cref="AutoAdjustState"/>.
        /// </summary>
        public bool autoEnabled = false;

        /// <summary>DEFAULT for new games: the absolute value Auto mode forces every pair to.</summary>
        public float autoTarget = 0f;

        /// <summary>DEFAULT for new games: Auto and Apply skip pairs that are lovers, spouses, fiances or blood family.</summary>
        public bool exemptLoversAndFamily = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mode, "mode", AdjustMode.Relative);
            Scribe_Values.Look(ref adjustValue, "adjustValue", 0f);
            Scribe_Values.Look(ref autoEnabled, "autoEnabled", false);
            Scribe_Values.Look(ref autoTarget, "autoTarget", 0f);
            Scribe_Values.Look(ref exemptLoversAndFamily, "exemptLoversAndFamily", false);
        }
    }
}
