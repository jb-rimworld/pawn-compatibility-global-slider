using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// Verse.Mod entry point: owns the global <see cref="GlobalAdjusterSettings"/> and
    /// renders the Options &gt; Mod Settings page. RimWorld only lists a mod under
    /// Mod Settings if its assembly contains a <see cref="Mod"/> subclass whose ctor
    /// calls <c>GetSettings&lt;T&gt;()</c>.
    /// <para>
    /// This coexists with <see cref="GlobalAdjusterStartup"/> (the
    /// <see cref="StaticConstructorOnStartup"/> that wires the dialog Harmony patches);
    /// both run at startup and are independent.
    /// </para>
    /// </summary>
    public class GlobalAdjusterMod : Mod
    {
        public static GlobalAdjusterSettings Settings;

        private string adjustBuffer;
        private string autoBuffer;

        public GlobalAdjusterMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<GlobalAdjusterSettings>();
            adjustBuffer = Settings.adjustValue.ToString("F2");
            autoBuffer = Settings.autoTarget.ToString("F2");
        }

        public override string SettingsCategory()
        {
            return "GlobalAdjuster.SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float min = BaseModAccess.SliderMin;
            float max = BaseModAccess.SliderMax;

            Listing_Standard listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(inRect);

            // --- Mode (one-shot apply) ---
            Text.Font = GameFont.Medium;
            listing.Label("CompatibilityGlobal.Mode".Translate());
            Text.Font = GameFont.Small;

            if (listing.RadioButton("CompatibilityGlobal.ModeRelative".Translate(),
                    Settings.mode == AdjustMode.Relative, tabIn: 8f,
                    tooltip: "CompatibilityGlobal.RelativeTip".Translate()))
            {
                Settings.mode = AdjustMode.Relative;
            }
            if (listing.RadioButton("CompatibilityGlobal.ModeAbsolute".Translate(),
                    Settings.mode == AdjustMode.Absolute, tabIn: 8f,
                    tooltip: "CompatibilityGlobal.AbsoluteTip".Translate()))
            {
                Settings.mode = AdjustMode.Absolute;
            }

            listing.Gap(6f);

            // --- One-shot value slider + numeric field ---
            string valueLabel = Settings.mode == AdjustMode.Absolute
                ? "CompatibilityGlobal.SetToLabel".Translate(Settings.adjustValue.ToString("F2"))
                : "CompatibilityGlobal.ShiftByLabel".Translate(SignedString(Settings.adjustValue));
            listing.Label(valueLabel);

            Rect valueRow = listing.GetRect(28f);
            DrawSliderWithField(valueRow, ref Settings.adjustValue, ref adjustBuffer, min, max);

            listing.Gap(10f);

            // --- Apply now button (needs a live map / store) ---
            bool inGame = BaseModAccess.StoreInstance != null && Current.Game != null;
            Rect applyRow = listing.GetRect(34f);
            Rect applyBtn = new Rect(applyRow.x, applyRow.y, Mathf.Min(260f, applyRow.width), applyRow.height);

            Color prevColor = GUI.color;
            if (!inGame) GUI.color = Color.gray;
            bool clickedApply = Widgets.ButtonText(applyBtn, "CompatibilityGlobal.ApplyNow".Translate());
            GUI.color = prevColor;
            TooltipHandler.TipRegion(applyBtn, inGame
                ? "CompatibilityGlobal.ApplyTip".Translate()
                : "CompatibilityGlobal.LoadGameFirst".Translate());

            if (clickedApply && inGame)
                DoApplyNow(min, max);

            listing.Gap(14f);
            listing.GapLine();
            listing.Gap(6f);

            // --- Auto mode (continuous, absolute) ---
            Text.Font = GameFont.Medium;
            listing.Label("CompatibilityGlobal.Auto".Translate());
            Text.Font = GameFont.Small;

            listing.CheckboxLabeled("CompatibilityGlobal.AutoEnable".Translate(),
                ref Settings.autoEnabled, "CompatibilityGlobal.AutoTip".Translate());

            listing.Gap(4f);
            listing.Label("CompatibilityGlobal.AutoTarget".Translate(Settings.autoTarget.ToString("F2")));
            Rect autoRow = listing.GetRect(28f);
            DrawSliderWithField(autoRow, ref Settings.autoTarget, ref autoBuffer, min, max);

            listing.Gap(14f);

            // --- Reset to defaults ---
            Rect resetRow = listing.GetRect(34f);
            Rect resetBtn = new Rect(resetRow.x, resetRow.y, Mathf.Min(260f, resetRow.width), resetRow.height);
            if (Widgets.ButtonText(resetBtn, "CompatibilityGlobal.ResetDefaults".Translate()))
            {
                Settings.mode = AdjustMode.Relative;
                Settings.adjustValue = 0f;
                Settings.autoEnabled = false;
                Settings.autoTarget = 0f;
                adjustBuffer = "0.00";
                autoBuffer = "0.00";
            }

            listing.End();

            // Keep buffers in sync if a slider changed the value above.
            adjustBuffer = Settings.adjustValue.ToString("F2");
            autoBuffer = Settings.autoTarget.ToString("F2");
        }

        private void DrawSliderWithField(Rect row, ref float value, ref string buffer, float min, float max)
        {
            float fieldWidth = 64f;
            Rect sliderRect = new Rect(row.x, row.y, row.width - fieldWidth - 8f, row.height);
            Rect fieldRect = new Rect(sliderRect.xMax + 8f, row.y, fieldWidth, row.height);

            float slid = Widgets.HorizontalSlider(sliderRect, value, min, max, middleAlignment: true,
                roundTo: 0.01f);
            if (!Mathf.Approximately(slid, value))
            {
                value = Mathf.Round(slid * 100f) / 100f;
                buffer = value.ToString("F2");
            }

            float before = value;
            Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, min, max);
            if (!Mathf.Approximately(value, before))
                value = Mathf.Round(value * 100f) / 100f;
        }

        private void DoApplyNow(float min, float max)
        {
            AdjustMode mode = Settings.mode;
            float value = Settings.adjustValue;

            int colonistCount = GlobalAdjustment.GetColonists().Count;
            int pairCount = GlobalAdjustment.PairCount(colonistCount);
            if (pairCount <= 0)
            {
                Messages.Message("CompatibilityGlobal.NotEnough".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            string valStr = mode == AdjustMode.Absolute ? value.ToString("F2") : SignedString(value);
            string confirm = mode == AdjustMode.Absolute
                ? "CompatibilityGlobal.ConfirmAbsolute".Translate(pairCount, valStr)
                : "CompatibilityGlobal.Confirm".Translate(pairCount, valStr);

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirm, () =>
            {
                int changed = GlobalAdjustment.Apply(mode, value, min, max);
                string done = mode == AdjustMode.Absolute
                    ? "CompatibilityGlobal.DoneAbsolute".Translate(changed, valStr)
                    : "CompatibilityGlobal.Done".Translate(changed, valStr);
                Messages.Message(done, MessageTypeDefOf.TaskCompletion, false);
            }));
        }

        internal static string SignedString(float v)
        {
            return (v >= 0f ? "+" : "") + v.ToString("F2");
        }
    }

    [StaticConstructorOnStartup]
    public static class GlobalAdjusterStartup
    {
        static GlobalAdjusterStartup()
        {
            var harmony = new Harmony("juliusberger00.pawncompatibilityglobaladjuster");

            try
            {
                Type dialogType = AccessTools.TypeByName("CompatibilityEditor.Dialog_CompatibilityEditor");
                if (dialogType == null)
                {
                    Log.Error("[CompatibilityGlobalAdjuster] Could not find Dialog_CompatibilityEditor. " +
                              "Is 'Pawn Compatibility Editor' installed and loaded before this mod?");
                    return;
                }

                MethodInfo doWindowContents = AccessTools.Method(dialogType, "DoWindowContents");
                if (doWindowContents != null)
                {
                    harmony.Patch(doWindowContents,
                        prefix: new HarmonyMethod(typeof(Patch_DialogGlobalSlider),
                            nameof(Patch_DialogGlobalSlider.DoWindowContentsPrefix)));
                }
                else
                {
                    Log.Error("[CompatibilityGlobalAdjuster] Could not find Dialog_CompatibilityEditor.DoWindowContents.");
                }

                MethodInfo initialSizeGetter = AccessTools.PropertyGetter(dialogType, "InitialSize");
                if (initialSizeGetter != null)
                {
                    harmony.Patch(initialSizeGetter,
                        postfix: new HarmonyMethod(typeof(Patch_DialogGlobalSlider),
                            nameof(Patch_DialogGlobalSlider.InitialSizePostfix)));
                }
                else
                {
                    Log.Warning("[CompatibilityGlobalAdjuster] Could not find Dialog_CompatibilityEditor.InitialSize; " +
                                "window may be slightly cramped.");
                }

                Log.Message("[CompatibilityGlobalAdjuster] Global compatibility slider patched in.");
            }
            catch (Exception e)
            {
                Log.Error("[CompatibilityGlobalAdjuster] Failed to patch the compatibility editor dialog: " + e);
            }
        }
    }
}
