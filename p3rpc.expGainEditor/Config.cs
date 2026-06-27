using p3rpc.expGainEditor.Template.Configuration;
using System.ComponentModel;

namespace p3rpc.expGainEditor.Configuration;

/// <summary>
/// EXP-gain multipliers for Persona 3 Reload. Everything defaults to 1.0 (= vanilla), so the mod
/// changes nothing until you edit it. All knobs are plain multipliers applied at the byte level to
/// the relevant game data (see <c>Mod</c>):
///   - enemy EXP lives in DatEnemyDataAsset (per enemy; grouped here by enemy class),
///   - the level-gap EXP curve in DT_BtlCalcLevelExpRatio,
///   - Shuffle Time Wand-card EXP in DatShuffleWandArcanaDataAsset.
/// </summary>
public class Config : Configurable<Config>
{
    // ---- global ----------------------------------------------------------
    [Category("1. Global")]
    [DisplayName("All Enemy EXP")]
    [Description("Multiplies the EXP every enemy gives. Stacks on top of the per-group multipliers below. 1.0 = vanilla.")]
    [DefaultValue(1.0)]
    public double GlobalEnemyExp { get; set; } = 1.0;

    // ---- per enemy-group (classified from each enemy's `flags` bitfield) --
    [Category("2. Enemy Groups")]
    [DisplayName("Normal Shadows")]
    [Description("EXP multiplier for regular field shadows (plain minimap dot). 1.0 = vanilla.")]
    [DefaultValue(1.0)]
    public double NormalShadowExp { get; set; } = 1.0;

    [Category("2. Enemy Groups")]
    [DisplayName("Strong Shadows")]
    [Description("EXP multiplier for strong shadows (the tankier glowing minimap dots; ~4x normal EXP). 1.0 = vanilla.")]
    [DefaultValue(1.0)]
    public double StrongShadowExp { get; set; } = 1.0;

    [Category("2. Enemy Groups")]
    [DisplayName("Rare Shadows")]
    [Description("EXP multiplier for rare shadows (the gold-bordered fleeing encounters; ~15x normal EXP). 1.0 = vanilla.")]
    [DefaultValue(1.0)]
    public double RareShadowExp { get; set; } = 1.0;

    [Category("2. Enemy Groups")]
    [DisplayName("Minibosses")]
    [Description("EXP multiplier for minibosses - the tanky boss-type 'guardian' encounters (block gatekeepers, Monad); modest reward, ~2x normal EXP. 1.0 = vanilla.")]
    [DefaultValue(1.0)]
    public double MinibossExp { get; set; } = 1.0;

    [Category("2. Enemy Groups")]
    [DisplayName("Bosses")]
    [Description("EXP multiplier for major story/endgame bosses and superbosses (the highest-EXP enemies; ~30x normal EXP, roughly 25-45x). 1.0 = vanilla.")]
    [DefaultValue(1.0)]
    public double BossExp { get; set; } = 1.0;

    // ---- level-gap scaling ----------------------------------------------
    [Category("3. Level Scaling")]
    [DisplayName("Level-Gap Scaling Strength")]
    [Description("How strongly an enemy's level vs yours scales EXP. Vanilla curve (by level gap = enemy level - your level, clamped to +/-10):\n" +
                 "  enemy 10+ below you = 0.32x | 5 below = 0.64x | same..3 above = 1.0x | 5 above = 1.19x | 10+ above = 4.0x.\n" +
                 "This knob blends that curve toward flat: 1.0 = vanilla, 0.0 = flat (level ignored, every kill gives base EXP), " +
                 ">1.0 exaggerates it (e.g. 2.0 roughly doubles the bonus/penalty; clamped so it never drops below 0).")]
    [DefaultValue(1.0)]
    public double LevelGapScalingStrength { get; set; } = 1.0;

    // ---- shuffle time ----------------------------------------------------
    [Category("4. Shuffle Time")]
    [DisplayName("Wand Card EXP")]
    [Description("Multiplies the EXP granted by Wand minor-arcana cards during Shuffle Time (DatShuffleWandArcanaDataAsset). 1.0 = vanilla.")]
    [DefaultValue(1.0)]
    public double ShuffleWandExp { get; set; } = 1.0;

    // ---- debug -----------------------------------------------------------
    [Category("5. Debug")]
    [DisplayName("Log to console")]
    [Description("Write what the mod baked to the Reloaded console (for debugging).")]
    [DefaultValue(false)]
    public bool LogToConsole { get; set; } = false;
}

/// <summary>
/// Overrides the configuration creation process. We bypass Reloaded's default property grid entirely
/// and run our own editor window (sliders + a live level-gap EXP curve graph that redraws as you drag
/// the scaling slider). The [Category]/[DisplayName]/[Description] attributes above are kept anyway so
/// the JSON shape stays stable and the data is self-documenting.
/// </summary>
public class ConfiguratorMixin : ConfiguratorMixinBase
{
    public override bool TryRunCustomConfiguration(Configurator configurator)
    {
        var config = configurator.GetConfiguration<Config>(0);
        ExpConfigWindow.Edit(config);
        return true;
    }
}
