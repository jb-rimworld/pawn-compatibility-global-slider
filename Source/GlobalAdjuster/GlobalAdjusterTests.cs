// In-game suites for Pawn Compatibility Editor - Global Slider. Compiled ONLY into test builds (tools/jbmod passes -p:JBTests=true,
// which defines JB_TESTS and references JB Framework's test harness). The release build has none of this and never references the framework.
//
// This mod does not use JB Framework. The harness finds the suites through the mod's content pack (JBTests.props). Test builds also
// read three environment variables so one test build can play several scenarios (the game process inherits them from the jbmod call):
//   PCGA_TEST_GLOBALS=on:1.5:exempt   the global defaults ("new games") are set to this right after all mods are loaded
//   PCGA_TEST_SAVEVAL=on:3.0:noexempt phase 1 of pcga.saveload puts these values into the SAVE's component before saving
//   PCGA_TEST_ADDITION=1              after a load, check the old-save migration (a save without our component takes the global values once)
//
//   JBFramework/tools/jbmod PawnCompatibilityGlobalAdjuster test pcga.auto,pcga.window jomar.pawncompatibilityeditor
//   PCGA_TEST_GLOBALS=on:1.5:exempt JBFramework/tools/jbmod PawnCompatibilityGlobalAdjuster _two pcga_sl pcga.saveload jomar.pawncompatibilityeditor -- jomar.pawncompatibilityeditor
#if JB_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using JBFramework;
using JBFramework.Testing;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnCompatibilityGlobalAdjuster.Tests
{
    /// <summary>Test-only: sets the global defaults from PCGA_TEST_GLOBALS once startup has finished (mods and defs loaded).</summary>
    [StaticConstructorOnStartup]
    static class TestGlobals
    {
        static TestGlobals()
        {
            string g = Environment.GetEnvironmentVariable("PCGA_TEST_GLOBALS");
            var s = GlobalAdjusterMod.Settings;
            if (string.IsNullOrEmpty(g) || s == null) return;
            T.ParseValues(g, out bool on, out float target, out bool exempt);
            s.autoEnabled = on; s.autoTarget = target; s.exemptLoversAndFamily = exempt;
            Log.Message("[PCGA-TEST] global defaults set from PCGA_TEST_GLOBALS: " + g);
        }
    }

    /// <summary>Test-only: old-save migration check (jbmod addition: a vanilla save, made without this mod, loaded with it).</summary>
    public sealed class TestAdditionCheck : GameComponent
    {
        bool additionDone;

        public TestAdditionCheck(Game game) { }

        public override void GameComponentTick()
        {
            if (additionDone || Environment.GetEnvironmentVariable("PCGA_TEST_ADDITION") != "1") return;
            additionDone = true;
            var comp = AutoAdjustState.Active; var g = GlobalAdjusterMod.Settings;
            string wantStr = Environment.GetEnvironmentVariable("PCGA_TEST_GLOBALS") ?? "off:0:noexempt";
            T.ParseValues(wantStr, out bool on, out float target, out bool exempt);
            var errs = new List<string>();
            if (comp == null) errs.Add("the old save got no component");
            else
            {
                if (comp.autoEnabled != on || !T.Near(comp.autoTarget, target) || comp.exemptLoversAndFamily != exempt)
                    errs.Add($"seeded {comp.autoEnabled}/{comp.autoTarget}/{comp.exemptLoversAndFamily}, global was {wantStr}");
                // the global default changes later: the save must keep its own values (run the init again, as a reload would)
                g.autoEnabled = !on; g.autoTarget = target + 1f; g.exemptLoversAndFamily = !exempt;
                comp.FinalizeInit();
                if (comp.autoEnabled != on || !T.Near(comp.autoTarget, target) || comp.exemptLoversAndFamily != exempt)
                    errs.Add("a later change of the global default reached the save");
            }
            if (errs.Count == 0) Log.Message("[PCGA-ADDITION] PASS: the old save took the global values once (" + wantStr + ") and kept them");
            else Log.Error("[PCGA-TEST] addition check failed: " + string.Join("; ", errs));
        }
    }

    static class T
    {
        public static bool Near(float a, float b, float tol = 0.011f) => Math.Abs(a - b) <= tol;

        public static void ParseValues(string s, out bool on, out float target, out bool exempt)
        {
            var p = s.Split(':');
            on = p[0] == "on";
            target = p.Length > 1 ? float.Parse(p[1], CultureInfo.InvariantCulture) : 0f;
            exempt = p.Length > 2 && p[2] == "exempt";
        }

        public static GlobalAdjusterMod ModHandle => LoadedModManager.ModHandles.OfType<GlobalAdjusterMod>().FirstOrDefault();

        public static Type DialogType => HarmonyLib.AccessTools.TypeByName("CompatibilityEditor.Dialog_CompatibilityEditor");

        public static float Compat(Pawn a, Pawn b) => a.relations.CompatibilityWith(b);

        public static void ClearStore()
        {
            object store = BaseModAccess.StoreInstance;
            store?.GetType().GetMethod("ClearAll")?.Invoke(store, null);
        }

        public static string ShotDir
        {
            get { string v = Environment.GetEnvironmentVariable("JB_UI_SHOTS"); return string.IsNullOrEmpty(v) || v == "0" ? null : v; }
        }

        public static void Shot(string name)
        {
            string dir = ShotDir;
            if (dir == null) return;
            try { Directory.CreateDirectory(dir); ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png")); }
            catch (Exception e) { Log.Warning("[JBTEST] screenshot failed: " + e.Message); }
        }

        public static IEnumerable<int> Frames(int frames)
        {
            int until = Time.frameCount + frames, guard = 0;
            while (Time.frameCount < until && guard++ < 3000) yield return 1;
        }

        public static W NoPause<W>(W w) where W : Window { w.forcePause = false; w.absorbInputAroundWindow = false; w.closeOnClickedOutside = false; return w; }

        public static bool German => LanguageDatabase.activeLanguage != null && LanguageDatabase.activeLanguage.folderName.StartsWith("German", StringComparison.Ordinal);

        /// <summary>Removes every direct relation among the given pawns, so a test starts from strangers.</summary>
        public static void MakeStrangers(List<Pawn> ps)
        {
            foreach (var a in ps)
                foreach (var b in ps)
                    if (a != b)
                        foreach (var r in a.relations.DirectRelations.Where(x => x.otherPawn == b).ToList())
                            a.relations.RemoveDirectRelation(r);
        }

        public static Pawn NewColonist(Map map)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(TestCells.Anchor(map), map, 8), map);
            return p;
        }

        public static void Dispose(Pawn p)
        {
            if (p == null || p.Destroyed) return;
            try { if (p.Spawned) p.DeSpawn(); Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard); }
            catch (Exception e) { Log.Warning("[JBTEST] could not dispose a test pawn: " + e.Message); }
        }

        /// <summary>Compatibility of every pair among the pawns, "name-name = value".</summary>
        public static List<string> Pairs(List<Pawn> ps)
        {
            var l = new List<string>();
            for (int i = 0; i < ps.Count; i++)
                for (int j = i + 1; j < ps.Count; j++) l.Add(ps[i].LabelShort + "-" + ps[j].LabelShort + "=" + Compat(ps[i], ps[j]).ToString("0.00", CultureInfo.InvariantCulture));
            return l;
        }
    }

    /// <summary>pcga.base: the base mod is there and the add-on found its store, or says so (startup message instead of an error).</summary>
    sealed class BaseSuite : KitTestSuite
    {
        public override string Name => "pcga.base";
        public override int MaxTicks => 200;

        public override IEnumerable<int> Run(TestContext t)
        {
            bool basePresent = ModLister.GetActiveModWithIdentifier("jomar.pawncompatibilityeditor", true) != null;
            t.Note("base mod (jomar.pawncompatibilityeditor) active: " + basePresent);
            t.Check(BaseModAccess.Available == basePresent, "BaseModAccess.Available == base mod active (" + BaseModAccess.Available + ")");
            t.Check((BaseModAccess.StoreInstance != null) == basePresent, "the base mod's store instance exists exactly when the base mod is active");
            if (basePresent)
            {
                t.Check(T.Near(BaseModAccess.SliderMin, -5f) && T.Near(BaseModAccess.SliderMax, 5f), "slider limits read from the base mod (" + BaseModAccess.SliderMin + " .. " + BaseModAccess.SliderMax + ")");
                var dialogType = T.DialogType;
                t.Check(dialogType != null, "Dialog_CompatibilityEditor type found");
            }
            else
            {
                t.Check(GlobalAdjustment.Apply(AdjustMode.Absolute, 1f, -5f, 5f) == 0, "Apply without the base mod does nothing (and does not throw)");
                t.Check(T.DialogType == null, "no dialog type without the base mod");
                t.Check(T.ModHandle != null, "the add-on's own settings entry still exists");
            }
            yield break;
        }
    }

    /// <summary>pcga.auto: per-save Auto mode, exemption of lovers and family, manual Apply, recruits, clamping.</summary>
    sealed class AutoSuite : KitTestSuite
    {
        public override string Name => "pcga.auto";
        public override int MaxTicks => 6000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var g = GlobalAdjusterMod.Settings;
            t.Check(BaseModAccess.Available && BaseModAccess.StoreInstance != null, "base mod and its store are there");
            var comp = AutoAdjustState.Active;
            t.Check(comp != null, "the save has the add-on's state");
            if (comp == null || !BaseModAccess.Available) yield break;
            float min = BaseModAccess.SliderMin, max = BaseModAccess.SliderMax;
            bool g0 = g.autoEnabled; float gt0 = g.autoTarget; bool ge0 = g.exemptLoversAndFamily;
            var cols = GlobalAdjustment.GetColonists();
            t.Check(cols.Count >= 3, "at least 3 free colonists (" + cols.Count + ")");
            if (cols.Count < 3) yield break;
            cols = cols.Take(3).ToList();
            var A = cols[0]; var B = cols[1]; var C = cols[2];
            Pawn recruit = null, parent = null, parent2 = null;
            var savedRel = new List<(Pawn a, Pawn b, PawnRelationDef d)>();
            foreach (var a in cols) foreach (var r in a.relations.DirectRelations) savedRel.Add((a, r.otherPawn, r.def));
            try
            {
                // random start colonists may be related to each other through parents the game generated: cut every direct relation, so the
                // pairs are real strangers (implied relations such as siblings only exist through direct ones)
                foreach (var a in cols) foreach (var r in a.relations.DirectRelations.ToList()) a.relations.RemoveDirectRelation(r);
                T.ClearStore();
                comp.autoEnabled = false; comp.exemptLoversAndFamily = false; comp.autoTarget = 0f;
                var before = T.Pairs(cols);
                yield return 600;
                t.Check(before.SequenceEqual(T.Pairs(cols)), "Auto off: 600 ticks pass and no pair is touched");

                // --- Auto on, absolute target ---
                comp.autoTarget = 2f; comp.autoEnabled = true;
                yield return 300;
                t.Check(T.Near(T.Compat(A, B), 2f) && T.Near(T.Compat(A, C), 2f) && T.Near(T.Compat(B, C), 2f), "Auto on, target 2.00: every pair reaches 2.00 within 300 ticks (" + string.Join(", ", T.Pairs(cols)) + ")");
                comp.autoTarget = 99f;
                yield return 300;
                t.Check(T.Near(T.Compat(A, B), max), "a target beyond the slider limit is clamped to " + max);
                comp.autoTarget = -99f;
                yield return 300;
                t.Check(T.Near(T.Compat(A, B), min), "a target below the slider limit is clamped to " + min);

                // --- exempt lovers and family ---
                comp.autoEnabled = false;
                A.relations.AddDirectRelation(PawnRelationDefOf.Lover, B);
                // siblings are an implied relation: they exist through a shared parent, not as a stored sibling link
                parent = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, fixedGender: Gender.Female));
                parent2 = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, fixedGender: Gender.Male));
                foreach (var kid in new[] { A, C })   // a random start colonist may already have parents of its own: siblings need exactly the same ones
                    foreach (var r in kid.relations.DirectRelations.Where(x => x.def == PawnRelationDefOf.Parent).ToList()) kid.relations.RemoveDirectRelation(r);
                foreach (var pp in new[] { parent, parent2 }) { Find.WorldPawns.PassToWorld(pp, RimWorld.Planet.PawnDiscardDecideMode.KeepForever); A.relations.AddDirectRelation(PawnRelationDefOf.Parent, pp); C.relations.AddDirectRelation(PawnRelationDefOf.Parent, pp); }
                t.Check(A.GetRelations(C).Contains(PawnRelationDefOf.Sibling) && !A.relations.DirectRelations.Any(r => r.otherPawn == C), "A and C are siblings through a shared parent (implied, not a direct relation)");
                T.ClearStore();
                float loverBefore = T.Compat(A, B), sibBefore = T.Compat(A, C), strangerBefore = T.Compat(B, C);
                t.Check(GlobalAdjustment.IsLoverOrFamily(A, B) && GlobalAdjustment.IsLoverOrFamily(A, C) && !GlobalAdjustment.IsLoverOrFamily(B, C), "relation helper: lovers and siblings yes, strangers no");
                comp.exemptLoversAndFamily = true; comp.autoTarget = 3f; comp.autoEnabled = true;
                yield return 300;
                t.Check(T.Near(T.Compat(B, C), 3f), "exempt on, Auto 3.0: the strangers pair reaches 3.00");
                t.Check(T.Near(T.Compat(A, B), loverBefore), "exempt on: the lovers keep their value (" + loverBefore.ToString("0.00") + ")");
                t.Check(T.Near(T.Compat(A, C), sibBefore), "exempt on: the siblings keep their value (" + sibBefore.ToString("0.00") + ")");
                comp.exemptLoversAndFamily = false;
                yield return 300;
                t.Check(T.Near(T.Compat(A, B), 3f) && T.Near(T.Compat(A, C), 3f), "exempt off: lovers and siblings follow the target too");

                // --- a recruit joins while Auto is on ---
                recruit = T.NewColonist(map);
                t.Check(GlobalAdjustment.GetColonists().Contains(recruit), "the recruit counts as a free colonist");
                yield return 300;
                t.Check(T.Near(T.Compat(recruit, A), 3f) && T.Near(T.Compat(recruit, B), 3f) && T.Near(T.Compat(recruit, C), 3f), "Auto catches the new colonist within 300 ticks (3.00 with all three)");

                // --- manual Apply (Auto off), relative and absolute, exempt on ---
                comp.autoEnabled = false; T.ClearStore();
                T.MakeStrangers(cols); A.relations.AddDirectRelation(PawnRelationDefOf.Lover, B);
                float lov = T.Compat(A, B), str = T.Compat(B, C);
                comp.exemptLoversAndFamily = true;
                int n = GlobalAdjustment.Apply(AdjustMode.Relative, 1f, min, max, showMessages: false);
                t.Check(T.Near(T.Compat(A, B), lov), "manual relative Apply with exempt on: lovers untouched");
                t.Check(T.Near(T.Compat(B, C), Mathf.Clamp(str + 1f, min, max)), "manual relative Apply: a stranger pair moved by +1 (" + str.ToString("0.00") + " -> " + T.Compat(B, C).ToString("0.00") + "), " + n + " pairs changed");
                comp.exemptLoversAndFamily = false;
                GlobalAdjustment.Apply(AdjustMode.Absolute, 0f, min, max, showMessages: false);
                t.Check(T.Near(T.Compat(A, B), 0f) && T.Near(T.Compat(B, C), 0f), "manual absolute Apply 0.00, exempt off: every pair at exactly 0.00");
                t.Check(GlobalAdjustment.Apply(AdjustMode.Absolute, 0f, min, max, showMessages: false) == 0, "applying the same value again changes nothing (cheap)");

                // --- the global defaults never touch the running save ---
                g.autoEnabled = true; g.autoTarget = -4f; g.exemptLoversAndFamily = true;
                yield return 300;
                t.Check(!comp.autoEnabled && T.Near(T.Compat(B, C), 0f), "changing the global default while the save has Auto off changes nothing in the save");
            }
            finally
            {
                comp.autoEnabled = false; comp.autoTarget = 0f; comp.exemptLoversAndFamily = false;
                g.autoEnabled = g0; g.autoTarget = gt0; g.exemptLoversAndFamily = ge0;
                T.ClearStore();
                T.Dispose(recruit);
                foreach (var pp in new[] { parent, parent2 })
                    if (pp != null) { foreach (var a in cols) foreach (var r in a.relations.DirectRelations.Where(x => x.otherPawn == pp).ToList()) a.relations.RemoveDirectRelation(r); Find.WorldPawns.RemovePawn(pp); Find.WorldPawns.PassToWorld(pp, RimWorld.Planet.PawnDiscardDecideMode.Discard); }
                T.MakeStrangers(cols);
                foreach (var (a, b, d) in savedRel) if (!a.relations.DirectRelationExists(d, b)) a.relations.AddDirectRelation(d, b);
            }
        }
    }

    /// <summary>pcga.window: the add-on's settings window and the strip in the base mod's dialog, with and without Auto, drawn without errors (screenshots with JB_UI_SHOTS).</summary>
    sealed class WindowSuite : KitTestSuite
    {
        public override string Name => "pcga.window";
        public override int MaxTicks => 4000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var mod = T.ModHandle; var comp = AutoAdjustState.Active;
            t.Check(mod != null && comp != null, "settings entry and component exist");
            if (mod == null || comp == null) yield break;
            string lang = T.German ? "de" : "en";
            bool a0 = comp.autoEnabled; float t0 = comp.autoTarget; bool e0 = comp.exemptLoversAndFamily;
            Window settings = null, editor = null;
            try
            {
                comp.autoEnabled = false; comp.autoTarget = 1.5f;
                settings = T.NoPause(new Dialog_ModSettings(mod));
                Find.WindowStack.Add(settings);
                foreach (var y in T.Frames(4)) yield return y;
                t.Check(settings.IsOpen, "the add-on's settings window opened and drew");
                T.Shot("pcga_settings_" + lang + "_ingame");
                foreach (var y in T.Frames(3)) yield return y;   // the screenshot is taken at the end of the frame: keep the window open until then
                var sz = settings.InitialSize;
                t.Note("settings window size " + sz.x + "x" + sz.y);
                settings.Close();
                foreach (var y in T.Frames(3)) yield return y;

                var dialogType = T.DialogType;
                if (dialogType == null) { t.Note("no base mod: no dialog strip to look at"); yield break; }
                editor = T.NoPause((Window)Activator.CreateInstance(dialogType, true));
                Find.WindowStack.Add(editor);
                foreach (var y in T.Frames(4)) yield return y;
                t.Check(editor.IsOpen, "the base mod's dialog with the slider strip opened and drew");
                T.Shot("pcga_dialog_" + lang + "_auto_off");
                foreach (var y in T.Frames(3)) yield return y;
                comp.autoEnabled = true; comp.autoTarget = 2.5f;
                foreach (var y in T.Frames(3)) yield return y;
                T.Shot("pcga_dialog_" + lang + "_auto_on");
                foreach (var y in T.Frames(3)) yield return y;
                t.Check(editor.IsOpen, "still open with the Auto notice drawn");
                editor.Close();
                foreach (var y in T.Frames(2)) yield return y;
            }
            finally
            {
                comp.autoEnabled = a0; comp.autoTarget = t0; comp.exemptLoversAndFamily = e0;
                if (settings != null && settings.IsOpen) settings.Close();
                if (editor != null && editor.IsOpen) editor.Close();
            }
        }

    }

    /// <summary>pcga.saveload (two phases, jbmod _two): the save's own values survive a reload and are not re-seeded from the global defaults.</summary>
    sealed class SaveLoadSuite : KitTestSuite
    {
        public override string Name => "pcga.saveload";
        public override int MaxTicks => 9000;
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;

        static string SaveName => "pcga_" + (Environment.GetEnvironmentVariable("PCGA_TEST_SAVEVAL") ?? "x").Replace(':', '_').Replace('.', 'p').Replace('-', 'm');
        static string Side => Path.Combine(GenFilePaths.SavedGamesFolderPath, SaveName + ".values.txt");

        public override IEnumerable<int> Run(TestContext t)
        {
            var comp = AutoAdjustState.Active; var g = GlobalAdjusterMod.Settings;
            t.Check(comp != null, "component exists");
            if (comp == null) yield break;
            bool loaded = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JB_TEST_LOAD"));
            string gs = Environment.GetEnvironmentVariable("PCGA_TEST_GLOBALS");
            if (!loaded)
            {
                // phase 1: a NEW game must have taken the global default
                if (gs != null)
                {
                    T.ParseValues(gs, out bool gon, out float gt, out bool gex);
                    t.Check(comp.autoEnabled == gon && T.Near(comp.autoTarget, gt) && comp.exemptLoversAndFamily == gex, $"new game took the global default ({gs}): {comp.autoEnabled}/{comp.autoTarget}/{comp.exemptLoversAndFamily}");
                }
                else t.Check(!comp.autoEnabled && comp.autoTarget == 0f && !comp.exemptLoversAndFamily, "new game with untouched defaults: Auto off, target 0, exempt off");
                // the save gets its own values, then the global default moves away from them
                string sv = Environment.GetEnvironmentVariable("PCGA_TEST_SAVEVAL") ?? "on:3.0:noexempt";
                T.ParseValues(sv, out bool on, out float target, out bool exempt);
                comp.autoEnabled = on; comp.autoTarget = target; comp.exemptLoversAndFamily = exempt;
                g.autoEnabled = !on; g.autoTarget = target + 1f; g.exemptLoversAndFamily = !exempt;
                var cols = GlobalAdjustment.GetColonists();
                yield return 400;
                var pairs = T.Pairs(cols);
                if (on) t.Check(cols.Count >= 2 && pairs.All(p => T.Near(float.Parse(p.Split('=')[1], CultureInfo.InvariantCulture), Mathf.Clamp(target, BaseModAccess.SliderMin, BaseModAccess.SliderMax))), "Auto drove the pairs to the save's target: " + string.Join(", ", pairs));
                File.WriteAllLines(Side, new[] { sv });
                var runners = Current.Game.components.Where(c => c.GetType().Assembly == typeof(KitTestSuite).Assembly).ToList();
                foreach (var r in runners) Current.Game.components.Remove(r);   // the harness must not be in the save (removal loads without it)
                try { GameDataSaveLoader.SaveGame(SaveName); }
                finally { foreach (var r in runners) if (!Current.Game.components.Contains(r)) Current.Game.components.Add(r); }
                string path = GenFilePaths.FilePathForSavedGame(SaveName);
                t.Check(File.Exists(path), "saved " + SaveName);
                string text = File.ReadAllText(path);
                t.Check(text.Contains("<pcgaAuto>") && !text.Contains("AutoAdjustState"), "the save holds the add-on's own node (pcgaAuto), no GameComponent class");
                t.Check(!text.Contains("JBFramework.Testing") && !text.Contains("GlobalAdjusterTestTenant"), "no test code in the save");
                yield break;
            }
            // phase 2: the load. The globals are set from PCGA_TEST_GLOBALS again at startup (different from the save's values)
            string saved = File.ReadAllLines(Side)[0];
            T.ParseValues(saved, out bool wOn, out float wT, out bool wEx);
            t.Check(comp.autoEnabled == wOn && T.Near(comp.autoTarget, wT) && comp.exemptLoversAndFamily == wEx, $"after the load the save has its own values ({saved}): {comp.autoEnabled}/{comp.autoTarget}/{comp.exemptLoversAndFamily}");
            if (gs != null)
            {
                T.ParseValues(gs, out bool gon, out float gt, out bool gex);
                t.Check(g.autoEnabled == gon && T.Near(g.autoTarget, gt), $"the global defaults differ from the save's and were not copied over it (global {g.autoEnabled}/{g.autoTarget})");
                t.Check(gon != wOn || !T.Near(gt, wT) || gex != wEx, "the scenario really has global defaults different from the save");
            }
            if (wOn)
            {
                // Auto resumes by itself after the load: knock one pair away and see it come back
                var cols = GlobalAdjustment.GetColonists();
                t.Check(cols.Count >= 2, "colonists after the load: " + cols.Count);
                if (cols.Count >= 2)
                {
                    float want = Mathf.Clamp(wT, BaseModAccess.SliderMin, BaseModAccess.SliderMax);
                    if (!wEx || !GlobalAdjustment.IsLoverOrFamily(cols[0], cols[1]))
                    {
                        yield return 300;
                        t.Check(T.Near(T.Compat(cols[0], cols[1]), want), "Auto kept working after the load (" + T.Compat(cols[0], cols[1]).ToString("0.00") + " = " + want + ")");
                    }
                }
            }
            else
            {
                var cols = GlobalAdjustment.GetColonists(); var before = T.Pairs(cols);
                yield return 600;
                t.Check(before.SequenceEqual(T.Pairs(cols)), "Auto off in this save: nothing moves although the global default says on");
            }
        }
    }
}
#endif
