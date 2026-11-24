using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// How a global adjustment combines with each pair's current compatibility.
    /// <list type="bullet">
    /// <item><c>Relative</c> – shift every pair by the chosen value (current + value).</item>
    /// <item><c>Absolute</c> – set every pair to exactly the chosen value.</item>
    /// </list>
    /// </summary>
    public enum AdjustMode
    {
        Relative = 0,
        Absolute = 1
    }

    /// <summary>
    /// Applies a global compatibility change to every pair of the player's current
    /// free colonists, writing the result into the base mod's override store.
    /// Supports both a relative shift and an absolute set (see <see cref="AdjustMode"/>).
    /// </summary>
    internal static class GlobalAdjustment
    {
        /// <summary>Free colonists on the current map (matches what the base editor lists).</summary>
        internal static List<Pawn> GetColonists()
        {
            if (Find.CurrentMap == null)
                return new List<Pawn>();
            return new List<Pawn>(Find.CurrentMap.mapPawns.FreeColonists);
        }

        /// <summary>
        /// True when b is a's lover, spouse, fiance or blood relative. Uses the game's own relation lookup, which also returns the IMPLIED
        /// relations (siblings, cousins, grandparents ...): those are never stored in DirectRelations, only parents, children and partners are.
        /// </summary>
        internal static bool IsLoverOrFamily(Pawn a, Pawn b)
        {
            if (a == null || b == null || a.relations == null) return false;
            foreach (PawnRelationDef d in a.GetRelations(b))
            {
                if (d == PawnRelationDefOf.Lover || d == PawnRelationDefOf.Spouse || d == PawnRelationDefOf.Fiance || d.familyByBloodRelation)
                    return true;
            }
            return false;
        }

        internal static int PairCount(int colonistCount)
        {
            if (colonistCount < 2) return 0;
            return colonistCount * (colonistCount - 1) / 2;
        }

        /// <summary>
        /// Apply a global compatibility change to every free-colonist pair.
        /// <para>
        /// <c>Relative</c>: each pair becomes <c>clamp(current + value)</c> (stacks with
        /// prior edits). <c>Absolute</c>: each pair becomes exactly <c>clamp(value)</c>,
        /// ignoring its current value.
        /// </para>
        /// All results are rounded to 2 decimals (0.01 step) and clamped to [min, max].
        /// Returns the number of pairs that actually changed.
        /// </summary>
        /// <param name="showMessages">
        /// When false (used by the throttled auto pass) the no-store / not-enough
        /// notifications are suppressed so they don't spam the message log.
        /// </param>
        internal static int Apply(AdjustMode mode, float value, float min, float max, bool showMessages = true)
        {
            object store = BaseModAccess.StoreInstance;
            if (store == null)
            {
                if (showMessages)
                    Messages.Message("CompatibilityGlobal.NoStore".Translate(), MessageTypeDefOf.RejectInput, false);
                return 0;
            }

            List<Pawn> colonists = GetColonists();
            if (colonists.Count < 2)
            {
                if (showMessages)
                    Messages.Message("CompatibilityGlobal.NotEnough".Translate(), MessageTypeDefOf.RejectInput, false);
                return 0;
            }

            bool exempt = AutoAdjustState.Active?.exemptLoversAndFamily ?? false;

            int changed = 0;
            for (int i = 0; i < colonists.Count; i++)
            {
                for (int j = i + 1; j < colonists.Count; j++)
                {
                    Pawn a = colonists[i];
                    Pawn b = colonists[j];
                    if (a == null || b == null || a.relations == null) continue;
                    if (exempt && IsLoverOrFamily(a, b)) continue;

                    // CompatibilityWith already returns the active override (base mod
                    // postfixes it), so this is the effective current value.
                    float current = a.relations.CompatibilityWith(b);

                    // Preserve the true game-generated value for the per-pair Reset button.
                    float vanilla;
                    if (!BaseModAccess.TryGetVanilla(store, a.thingIDNumber, b.thingIDNumber, out vanilla))
                        vanilla = current; // no override yet -> current is the vanilla value

                    float target = mode == AdjustMode.Absolute ? value : current + value;
                    float newVal = Mathf.Clamp(target, min, max);
                    newVal = Mathf.Round(newVal * 100f) / 100f;

                    // Dirty-check: skip the (reflection) write when the effective value
                    // is already what we want. NOTE: the base mod's SetOverride deletes
                    // the entry when newVal == vanilla, so a pair whose vanilla already
                    // equals the absolute target ends up with no override (correct).
                    if (Mathf.Approximately(newVal, current))
                        continue;

                    changed++;
                    BaseModAccess.SetOverride(store, a.thingIDNumber, b.thingIDNumber, newVal, vanilla);
                }
            }

            return changed;
        }
    }
}
