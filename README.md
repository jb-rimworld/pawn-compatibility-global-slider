# Pawn Compatibility Editor – Global Slider

Companion mod for **Pawn Compatibility Editor** by Jomar
(`jomar.pawncompatibilityeditor`). It adds a **global slider** on top of the
compatibility editor dialog (the one you open from the bottom button bar).

## What it does

Instead of editing one pair at a time, this add-on lets you adjust the
compatibility of **every pair of free colonists in your current faction** at
once. It works in two modes and adds a continuous **Auto** mode.

### Modes

- **Relative** – adds the chosen amount to each pair's *current* compatibility
  (positive improves, negative worsens). Can be applied repeatedly to stack.
- **Absolute** – sets every pair's compatibility to *exactly* the chosen value,
  ignoring its current value.

Pick the mode in **Options → Mod Settings → Compatibility Global Adjuster**, or
use the strip on top of the editor dialog. Both surfaces honour the same mode.
All values are clamped to the min/max you configured in the base mod's settings
and rounded to 0.01.

### Auto mode (continuous, absolute)

Enable **Auto mode** in the settings and pick a target. A lightweight
GameComponent then re-asserts that target for every colonist pair roughly every
4 seconds, so **new colonists** (recruits, births, freed prisoners) are snapped
to the target automatically. While Auto is on:

- the in-dialog Apply button is replaced with an "Auto active" notice (manual
  edits would just be re-asserted on the next pass),
- a dirty-check skips pairs that already match the target, so it's cheap.

### Where the data lives

Adjustments are written into the **same override store** the base mod uses, so:

- they stack cleanly with per-pair edits you make in the dialog,
- they are saved with your game,
- the base mod's per-pair **Reset** button still restores the original
  game-generated value (the true "vanilla" value is preserved).

> Absolute-mode note: the base mod deletes a pair's override when it equals the
> pair's vanilla value. So if you set everyone to a value that happens to equal
> a pair's natural compatibility, that pair simply has no override entry — which
> is correct. Verify by reading the in-game compatibility, not the raw store.

Mode, the one-shot value, the Auto toggle and the Auto target are stored as
**global mod settings** (per user, like the base mod's slider min/max), not
per-save.

## How it works (for future-you)

- `BaseModAccess.cs` – reflection bridge into the base mod's `internal`
  `CompatibilityOverrideStore` and its public settings (resolved by name once,
  cached).
- `GlobalAdjustment.cs` – defines `enum AdjustMode { Relative, Absolute }` and
  iterates every free-colonist pair, applying either `clamp(current + value)`
  (Relative) or `clamp(value)` (Absolute), with a per-pair dirty-check.
- `GlobalAdjusterSettings.cs` – `ModSettings` holding `mode`, `adjustValue`,
  `autoEnabled`, `autoTarget` (Scribe_Values).
- `GlobalAdjusterMod.cs` – the `Verse.Mod` subclass (calls
  `GetSettings<GlobalAdjusterSettings>()` so the mod appears under Mod Settings)
  that renders the settings page, plus the separate
  `[StaticConstructorOnStartup] GlobalAdjusterStartup` that wires up the manual
  Harmony patches (the dialog type is internal, so it's patched by name).
- `AutoAdjustGameComponent.cs` – `GameComponent` (auto-instantiated, no Def)
  whose throttled `GameComponentTick` forces every pair to the Auto target.
- `Patch_DialogGlobalSlider.cs` – Harmony prefix on `Dialog_CompatibilityEditor.
  DoWindowContents` draws the mode-aware global strip and shifts the original
  content down; postfix on `InitialSize` grows the window to fit.

## Building

Source is in `Source/GlobalAdjuster/`. Output goes to `1.6/Assemblies/`.

```sh
export PATH="$HOME/.dotnet:$PATH"
cd "Source/GlobalAdjuster"
dotnet build -c Release
```

Reference paths in the `.csproj` point at the RimWorld managed assemblies and
the Harmony workshop DLL via `$(HOME)`; adjust if your install moves.

Requires: Harmony, Pawn Compatibility Editor. Load this mod **after** both.
