using System;
using HarmonyLib;
using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// "Auto" mode PER SAVE: while <see cref="autoEnabled"/> is set, continuously forces every free-colonist pair's compatibility to
    /// <see cref="autoTarget"/> (absolute).
    /// <para>
    /// The global mod settings (<see cref="GlobalAdjusterSettings"/>) only hold the DEFAULT for new games. A save that has no stored values
    /// yet (new game, or a save made before this add-on stored anything) copies the current global values exactly once; after that the
    /// save owns its values and changing the global default does not touch it.
    /// </para>
    /// This is deliberately NOT a GameComponent: a save that names a GameComponent class of a removed mod logs red errors on every load.
    /// The values live in their own node (<c>pcgaAuto</c>) inside the save's game node, written by a postfix on the game's own expose
    /// method. Without this add-on the node is simply ignored, so removing the mod mid-save is silent.
    /// </summary>
    public class AutoAdjustState
    {
        // ~250 ticks = 4 s at normal speed: imperceptible delay, negligible CPU.
        private const int ThrottleTicks = 250;
        private const string NodeName = "pcgaAuto";

        private static AutoAdjustState instance;

        /// <summary>This save: Auto mode on/off.</summary>
        public bool autoEnabled;

        /// <summary>This save: absolute value Auto mode forces every pair to.</summary>
        public float autoTarget;

        /// <summary>This save: skip pairs that are lovers, spouses, fiances or family.</summary>
        public bool exemptLoversAndFamily;

        /// <summary>True once the values above were taken from the global defaults (or loaded from the save).</summary>
        private bool initialized;

        /// <summary>The state of the running game, or null (main menu).</summary>
        public static AutoAdjustState Active
        {
            get { return Current.Game != null ? instance : null; }
        }

        private void Expose()
        {
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref autoEnabled, "autoEnabled", false);
            Scribe_Values.Look(ref autoTarget, "autoTarget", 0f);
            Scribe_Values.Look(ref exemptLoversAndFamily, "exemptLoversAndFamily", false);
        }

        /// <summary>Takes the global defaults once, when this save has no values of its own yet.</summary>
        public void FinalizeInit()
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

        // ---- hooks (applied by GlobalAdjusterStartup) ----------------------------------------------------------------

        /// <summary>Prefix on Game.InitNewGame: a new game starts with a fresh, unseeded state.</summary>
        internal static void NewGamePrefix()
        {
            instance = new AutoAdjustState();
        }

        /// <summary>Postfix on Game.FinalizeInit (new game and load): seed from the global defaults if the save had no values.</summary>
        internal static void FinalizeInitPostfix()
        {
            try
            {
                if (instance == null) instance = new AutoAdjustState();
                instance.FinalizeInit();
            }
            catch (Exception e) { Log.Warning("[CompatibilityGlobalAdjuster] Could not set up Auto mode for this game: " + e.Message); }
        }

        /// <summary>Postfix on Game.ExposeSmallComponents: runs when saving and when loading; reads or writes our node.</summary>
        internal static void ExposePostfix()
        {
            try
            {
                if (Scribe.mode == LoadSaveMode.LoadingVars) instance = new AutoAdjustState();
                AutoAdjustState s = instance;
                if (s == null) return;
                if (Scribe.mode != LoadSaveMode.Saving && Scribe.mode != LoadSaveMode.LoadingVars) return;
                if (!Scribe.EnterNode(NodeName)) return;   // loading a save without our node: defaults, seeded later
                try { s.Expose(); }
                finally { Scribe.ExitNode(); }
            }
            catch (Exception e) { Log.Warning("[CompatibilityGlobalAdjuster] Could not read or write the Auto mode values of this save: " + e.Message); }
        }

        /// <summary>Postfix on TickManager.DoSingleTick.</summary>
        internal static void TickPostfix()
        {
            AutoAdjustState s = instance;
            if (s == null || !s.autoEnabled || Current.ProgramState != ProgramState.Playing || Current.Game == null) return;
            if (Find.TickManager.TicksGame % ThrottleTicks != 0) return;
            try
            {
                if (!BaseModAccess.Available) return;   // without the base mod there is no store to write to
                GlobalAdjustment.Apply(AdjustMode.Absolute, s.autoTarget, BaseModAccess.SliderMin, BaseModAccess.SliderMax, showMessages: false);
            }
            catch (Exception e) { Log.ErrorOnce("[CompatibilityGlobalAdjuster] Auto mode failed: " + e, 1948273); }
        }

        /// <summary>Applies the four hooks. Each one separately, so a changed vanilla method disables only its part (and says so).</summary>
        internal static void Install(Harmony harmony)
        {
            Hook(harmony, typeof(Game), "InitNewGame", prefix: nameof(NewGamePrefix));
            Hook(harmony, typeof(Game), "FinalizeInit", postfix: nameof(FinalizeInitPostfix));
            Hook(harmony, typeof(Game), "ExposeSmallComponents", postfix: nameof(ExposePostfix));
            Hook(harmony, typeof(TickManager), "DoSingleTick", postfix: nameof(TickPostfix));
        }

        private static void Hook(Harmony harmony, Type type, string method, string prefix = null, string postfix = null)
        {
            try
            {
                var original = AccessTools.Method(type, method);
                if (original == null)
                {
                    Log.Warning("[CompatibilityGlobalAdjuster] " + type.Name + "." + method + " not found: Auto mode is limited in this game version.");
                    return;
                }
                harmony.Patch(original,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(AutoAdjustState), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(AutoAdjustState), postfix));
            }
            catch (Exception e) { Log.Warning("[CompatibilityGlobalAdjuster] Could not patch " + type.Name + "." + method + ": " + e.Message); }
        }
    }
}
