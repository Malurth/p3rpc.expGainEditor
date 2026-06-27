# P3R EXP Gain Editor

A [Reloaded II](https://github.com/Reloaded-Project/Reloaded-II) mod for **Persona 3 Reload** that lets you tune the game's EXP gains in a dedicated editor window — no file editing, no external tools.

Everything defaults to vanilla, so the mod changes nothing until you turn a knob.

## Features

- **A real editor window** (opened from **Configure**) — every knob is a slider paired with a textbox (the textbox takes over-range values past the slider).
- **Global multiplier** — scales the EXP every enemy gives, on top of the per-group knobs.
- **Per enemy-group multipliers**, classified from each enemy's internal class flags:
  - **Normal Shadows** — regular field shadows (baseline, 1× EXP)
  - **Strong Shadows** — tankier glowing field shadows (~4× normal)
  - **Rare Shadows** — gold-bordered fleeing shadows (~15× normal)
  - **Minibosses** — tanky "guardian" encounters, e.g. gatekeepers / Monad (~2× normal)
  - **Bosses** — story / endgame bosses & superbosses (~30× normal)
- **Level-Gap Scaling Strength** — blends the game's "enemy level vs yours" EXP curve toward flat (1 = vanilla, 0 = level ignored, >1 exaggerates), with a **live curve graph** that redraws as you drag the slider.
- **Shuffle Time Wand-card EXP** — scales the EXP granted by Wand minor-arcana cards.
- Everything defaults to vanilla; change only what you want.

## Requirements

- [Reloaded II](https://github.com/Reloaded-Project/Reloaded-II)
- [Unreal Essentials](https://github.com/AnimatedSwine37/UnrealEssentials) — fetched automatically as a dependency

## Installation

1. Set up Reloaded II for Persona 3 Reload (see the [Beginner's Guide](https://gamebanana.com/tuts/17156)).
2. Download this mod (from Releases or GameBanana) and enable it in Reloaded II. Dependencies are pulled in automatically.
3. Launch the game.

## Usage

1. In Reloaded II, select the mod and open **Configure** — this opens the editor window.
2. Drag the sliders (or type exact values), watch the level-gap graph update, and click **Save & Close**.
3. **Relaunch the game** — changes are applied at startup.

## How it works

The mod reads your config and patches copies of three game assets at precomputed byte offsets, then serves them through Unreal Essentials (no game files on disk are modified):

- `DatEnemyDataAsset` — each enemy's EXP, scaled by its group multiplier.
- `DT_BtlCalcLevelExpRatio` — the level-gap EXP curve, lerped toward 1.0 by the scaling strength.
- `DatShuffleWandArcanaDataAsset` — the Shuffle Time Wand-card EXP coefficients.

## Building

Requires the **.NET 9 SDK** and a `RELOADEDIIMODS` environment variable pointing at your Reloaded II mods folder.

- `p3rpc.expGainEditor/BuildLinked.ps1` — builds straight into your mods folder for testing.
- `Publish.ps1` — produces the release packages (GitHub / GameBanana / NuGet).

## Credits

Built on the Reloaded II mod template by Sewer56. Uses [Unreal Essentials](https://github.com/AnimatedSwine37/UnrealEssentials) by AnimatedSwine37 & Rirurin.
