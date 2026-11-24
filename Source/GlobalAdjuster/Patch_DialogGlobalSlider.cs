using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// Injects a global compatibility slider on top of Jomar's
    /// <c>Dialog_CompatibilityEditor</c>, and grows the window to fit it.
    /// Wired up via manual Harmony patches (the dialog type is internal).
    /// </summary>
    internal static class Patch_DialogGlobalSlider
    {
        // Extra height added to the window and reserved at the top of its content.
        internal const float StripHeight = 118f;
        private const float Gap = 8f;
        private const float Pad = 8f;

        private sealed class State
        {
            public float value;
            public string buffer = "0.00";
        }

        // One slider state per open dialog instance; auto-collected when the window dies.
        private static readonly ConditionalWeakTable<object, State> states =
            new ConditionalWeakTable<object, State>();

        /// <summary>Postfix on get_InitialSize: make the window taller.</summary>
        internal static void InitialSizePostfix(ref Vector2 __result)
        {
            __result.y += StripHeight;
        }

        /// <summary>
        /// Prefix on DoWindowContents: draw our strip in the reserved top band,
        /// then hand the original a rect shifted down by StripHeight so its
        /// existing layout renders unchanged below us.
        /// </summary>
        internal static void DoWindowContentsPrefix(Window __instance, ref Rect __0)
        {
            // Positional argument (__0): the base mod renamed the parameter (rect -> inRect) in its 1.7 update.
            ref Rect rect = ref __0;
            State state = states.GetOrCreateValue(__instance);

            float min = BaseModAccess.SliderMin;
            float max = BaseModAccess.SliderMax;
            state.value = Mathf.Clamp(state.value, min, max);

            Rect strip = new Rect(rect.x, rect.y, rect.width, StripHeight - Gap);
            DrawGlobalSection(strip, state, min, max);

            // Reserve the strip; the original draws in the remaining area.
            rect.y += StripHeight;
            rect.height -= StripHeight;
        }

        private static void DrawGlobalSection(Rect box, State state, float min, float max)
        {
            Widgets.DrawMenuSection(box);
            Rect inner = box.ContractedBy(Pad);

            // Settings may be null if the Mod ctor hasn't run (shouldn't happen at
            // runtime, but be defensive) — default to Relative.
            GlobalAdjusterSettings settings = GlobalAdjusterMod.Settings;
            AdjustMode mode = settings != null ? settings.mode : AdjustMode.Relative;
            bool autoOn = settings != null && settings.autoEnabled;

            // Title
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Rect titleRect = new Rect(inner.x, inner.y, inner.width, Text.LineHeight);
            Widgets.Label(titleRect, "CompatibilityGlobal.Title".Translate());

            // When Auto is active, the slider/apply are meaningless (auto re-asserts
            // every pass). Replace them with a read-only notice.
            if (autoOn)
            {
                Rect noticeRect = new Rect(inner.x, titleRect.yMax + 6f, inner.width,
                    inner.height - (titleRect.yMax - inner.y) - 6f);
                GUI.color = new Color(0.9f, 0.85f, 0.6f);
                Widgets.Label(noticeRect,
                    "CompatibilityGlobal.AutoActiveNotice".Translate(settings.autoTarget.ToString("F2")));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            // Slider + numeric field row
            float textBoxWidth = 60f;
            float rowY = titleRect.yMax + 4f;
            float rowH = 24f;
            Rect sliderRect = new Rect(inner.x, rowY, inner.width - textBoxWidth - 8f, rowH);
            Rect textRect = new Rect(sliderRect.xMax + 8f, rowY, textBoxWidth, rowH);

            float newVal = Widgets.HorizontalSlider(sliderRect, state.value, min, max, middleAlignment: true);
            if (!Mathf.Approximately(newVal, state.value))
            {
                state.value = Mathf.Round(newVal * 100f) / 100f;
                state.buffer = state.value.ToString("F2");
            }
            TooltipHandler.TipRegion(sliderRect, mode == AdjustMode.Absolute
                ? "CompatibilityGlobal.SliderTipAbsolute".Translate()
                : "CompatibilityGlobal.SliderTip".Translate());

            float prevText = state.value;
            Widgets.TextFieldNumeric(textRect, ref state.value, ref state.buffer, min, max);
            if (!Mathf.Approximately(state.value, prevText))
                state.value = Mathf.Round(state.value * 100f) / 100f;

            // Apply button
            float btnY = rowY + rowH + 6f;
            float btnH = 30f;
            float btnW = Mathf.Min(260f, inner.width);
            Rect btnRect = new Rect(inner.x, btnY, btnW, btnH);

            int colonistCount = GlobalAdjustment.GetColonists().Count;
            int pairCount = GlobalAdjustment.PairCount(colonistCount);
            // In Absolute mode 0 is a legitimate target, so don't gate on value != 0.
            bool valueMeaningful = mode == AdjustMode.Absolute || !Mathf.Approximately(state.value, 0f);
            bool canApply = pairCount > 0 && valueMeaningful && BaseModAccess.StoreInstance != null;

            string applyLabel = mode == AdjustMode.Absolute
                ? "CompatibilityGlobal.ApplyAbsolute".Translate()
                : "CompatibilityGlobal.Apply".Translate();

            Color prevColor = GUI.color;
            if (!canApply) GUI.color = Color.gray;
            bool clicked = Widgets.ButtonText(btnRect, applyLabel);
            GUI.color = prevColor;
            TooltipHandler.TipRegion(btnRect, mode == AdjustMode.Absolute
                ? "CompatibilityGlobal.ApplyTipAbsolute".Translate()
                : "CompatibilityGlobal.ApplyTip".Translate());

            if (clicked && canApply)
            {
                float value = state.value;
                AdjustMode appliedMode = mode;
                string valStr = appliedMode == AdjustMode.Absolute ? value.ToString("F2") : SignedString(value);
                string confirm = appliedMode == AdjustMode.Absolute
                    ? "CompatibilityGlobal.ConfirmAbsolute".Translate(pairCount, valStr)
                    : "CompatibilityGlobal.Confirm".Translate(pairCount, valStr);
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirm, () =>
                {
                    int changed = GlobalAdjustment.Apply(appliedMode, value, min, max);
                    string done = appliedMode == AdjustMode.Absolute
                        ? "CompatibilityGlobal.DoneAbsolute".Translate(changed, valStr)
                        : "CompatibilityGlobal.Done".Translate(changed, valStr);
                    Messages.Message(done, MessageTypeDefOf.TaskCompletion, false);
                    // In Relative mode reset to 0 so repeated applies don't surprise;
                    // in Absolute mode keep the value (it's the target, not a delta).
                    if (appliedMode == AdjustMode.Relative)
                    {
                        state.value = 0f;
                        state.buffer = "0.00";
                    }
                }));
            }

            // Reset GUI state for the original draw.
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static string SignedString(float v)
        {
            return (v >= 0f ? "+" : "") + v.ToString("F2");
        }
    }
}
