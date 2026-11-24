using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace PawnCompatibilityGlobalAdjuster
{
    /// <summary>
    /// Reflection bridge into Jomar's "Pawn Compatibility Editor" assembly.
    /// The override store class is <c>internal</c>, so we cannot reference it at
    /// compile time from this separate mod. We resolve everything by name once
    /// and cache the members.
    /// </summary>
    internal static class BaseModAccess
    {
        private static bool resolved;
        private static bool available;

        // CompatibilityOverrideStore (internal GameComponent)
        private static FieldInfo storeInstanceField;
        private static MethodInfo tryGetOverrideMethod;
        private static MethodInfo tryGetVanillaMethod;
        private static MethodInfo setOverrideMethod;

        // CompatibilityEditorMod.Settings (public) -> sliderMin / sliderMax (public)
        private static FieldInfo settingsField;
        private static FieldInfo sliderMinField;
        private static FieldInfo sliderMaxField;

        private static void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;

            try
            {
                Type storeType = AccessTools.TypeByName("CompatibilityEditor.CompatibilityOverrideStore");
                if (storeType == null)
                {
                    Log.Warning("[CompatibilityGlobalAdjuster] CompatibilityOverrideStore type not found. Is Pawn Compatibility Editor active?");
                    return;
                }

                storeInstanceField = AccessTools.Field(storeType, "Instance");
                tryGetOverrideMethod = AccessTools.Method(storeType, "TryGetOverride");
                tryGetVanillaMethod = AccessTools.Method(storeType, "TryGetVanilla");
                setOverrideMethod = AccessTools.Method(storeType, "SetOverride");

                Type modType = AccessTools.TypeByName("CompatibilityEditor.CompatibilityEditorMod");
                if (modType != null)
                {
                    settingsField = AccessTools.Field(modType, "Settings");
                    if (settingsField != null)
                    {
                        Type settingsType = settingsField.FieldType;
                        sliderMinField = AccessTools.Field(settingsType, "sliderMin");
                        sliderMaxField = AccessTools.Field(settingsType, "sliderMax");
                    }
                }

                available = storeInstanceField != null
                            && tryGetOverrideMethod != null
                            && tryGetVanillaMethod != null
                            && setOverrideMethod != null;

                if (!available)
                    Log.Warning("[CompatibilityGlobalAdjuster] Failed to resolve one or more store members via reflection.");
            }
            catch (Exception e)
            {
                Log.Error("[CompatibilityGlobalAdjuster] Error resolving base mod members: " + e);
            }
        }

        /// <summary>Whether the base mod's store API was found.</summary>
        internal static bool Available
        {
            get { EnsureResolved(); return available; }
        }

        /// <summary>Live CompatibilityOverrideStore.Instance, or null if no game / not found.</summary>
        internal static object StoreInstance
        {
            get
            {
                EnsureResolved();
                return storeInstanceField?.GetValue(null);
            }
        }

        internal static bool TryGetOverride(object store, int idA, int idB, out float value)
        {
            object[] args = { idA, idB, 0f };
            bool result = (bool)tryGetOverrideMethod.Invoke(store, args);
            value = (float)args[2];
            return result;
        }

        internal static bool TryGetVanilla(object store, int idA, int idB, out float value)
        {
            object[] args = { idA, idB, 0f };
            bool result = (bool)tryGetVanillaMethod.Invoke(store, args);
            value = (float)args[2];
            return result;
        }

        internal static void SetOverride(object store, int idA, int idB, float overrideVal, float vanillaVal)
        {
            setOverrideMethod.Invoke(store, new object[] { idA, idB, overrideVal, vanillaVal });
        }

        /// <summary>Slider min from the base mod's settings (fallback -5).</summary>
        internal static float SliderMin
        {
            get
            {
                EnsureResolved();
                try
                {
                    object settings = settingsField?.GetValue(null);
                    if (settings != null && sliderMinField != null)
                        return (float)sliderMinField.GetValue(settings);
                }
                catch { }
                return -5f;
            }
        }

        /// <summary>Slider max from the base mod's settings (fallback +5).</summary>
        internal static float SliderMax
        {
            get
            {
                EnsureResolved();
                try
                {
                    object settings = settingsField?.GetValue(null);
                    if (settings != null && sliderMaxField != null)
                        return (float)sliderMaxField.GetValue(settings);
                }
                catch { }
                return 5f;
            }
        }
    }
}
