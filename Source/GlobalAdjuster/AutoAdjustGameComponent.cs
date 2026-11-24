using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// Drives "Auto" mode PER SAVE: while <see cref="autoEnabled"/> is set, continuously forces every
    /// free-colonist pair's compatibility to <see cref="autoTarget"/> (absolute).
    /// <para>
    /// The global mod settings (<see cref="GlobalAdjusterSettings"/>) only hold the DEFAULT for new games.
    /// A save that has no stored values yet (new game, or a save made before this component stored anything)
    /// copies the current global values exactly once in <see cref="FinalizeInit"/>; after that the save
    /// owns its values and changing the global default does not touch it.
    /// </para>
    /// RimWorld auto-instantiates every concrete <see cref="GameComponent"/> with a <c>(Game)</c> ctor
    /// (and adds a missing one to a loaded save), no Def required.
    /// </summary>
    public class AutoAdjustGameComponent : GameComponent
    {
        // ~250 ticks = 4 s at normal speed: imperceptible delay, negligible CPU.
        private const int ThrottleTicks = 250;

        /// <summary>This save: Auto mode on/off.</summary>
        public bool autoEnabled;

        /// <summary>This save: absolute value Auto mode forces every pair to.</summary>
        public float autoTarget;

        /// <summary>This save: skip pairs that are lovers, spouses, fiances or blood family.</summary>
        public bool exemptLoversAndFamily;

        /// <summary>True once the values above were taken from the global defaults (or loaded from the save).</summary>
        private bool initialized;

        public AutoAdjustGameComponent(Game game)
        {
        }

        /// <summary>The component of the running game, or null (main menu / not yet created).</summary>
        public static AutoAdjustGameComponent Active
        {
            get { return Current.Game?.GetComponent<AutoAdjustGameComponent>(); }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref autoEnabled, "autoEnabled", false);
            Scribe_Values.Look(ref autoTarget, "autoTarget", 0f);
            Scribe_Values.Look(ref exemptLoversAndFamily, "exemptLoversAndFamily", false);
        }

        public override void FinalizeInit()
        {
            if (initialized) return;
            GlobalAdjusterSettings g = GlobalAdjusterMod.Settings;
            if (g != null)
            {
                autoEnabled = g.autoEnabled;
                autoTarget = g.autoTarget;
                exemptLoversAndFamily = g.exemptLoversAndFamily;
            }
            initialized = true;
        }

        public override void GameComponentTick()
        {
            if (!autoEnabled)
                return;

            if (Find.TickManager.TicksGame % ThrottleTicks != 0)
                return;

            float min = BaseModAccess.SliderMin;
            float max = BaseModAccess.SliderMax;

            // Auto is absolute-only. Suppress messages (this fires repeatedly).
            GlobalAdjustment.Apply(AdjustMode.Absolute, autoTarget, min, max, showMessages: false);
        }
    }
}
