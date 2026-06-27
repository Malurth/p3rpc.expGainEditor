using p3rpc.expGainEditor.Configuration;
using p3rpc.expGainEditor.Template;
using Reloaded.Mod.Interfaces;
using System.Drawing;
using UnrealEssentials.Interfaces;

namespace p3rpc.expGainEditor;

/// <summary>
/// Reads the configured EXP multipliers and byte-patches bundled (zen-format) copies of three game
/// assets, then feeds them to Unreal Essentials so the game loads our values. No game hooks.
///
/// Delivery is identical to the difficulty editor: bundle vanilla zen assets, patch a copy at known
/// byte offsets, write to Generated/, and AddFromFolder. The difference is that enemy EXP lives in a
/// large variable-length DataAsset, so the per-enemy EXP offsets (and each enemy's group) are
/// precomputed offline into <see cref="BakeData"/> rather than hand-listed.
/// </summary>
public class Mod : ModBase
{
    private readonly IModLoader _modLoader;
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private Config _configuration;

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _configuration = context.Configuration;

        var generated = Bake();
        if (generated != null)
            Register(generated);
    }

    // ---- enemy tiers ----
    // Order MUST match the group ints baked into BakeData. Tiers are classified OFFLINE (see the gen
    // scripts / BakeData header) from each enemy's `flags` bitfield + EXP-for-level reward:
    //   Normal  = flags 0                         (~1x EXP)
    //   Strong  = flags bit 15 (0x8000)           (~4x, tanky glowing field shadows)
    //   Rare    = flags bit 9  (0x200)            (~15x, low-HP gold fleeing shadows)
    //   Miniboss= flags bit 8  (0x100), low reward(~2x, tanky guardians: gatekeepers/Monad)
    //   Boss    = flags bit 8  (0x100), high reward(~30x, story/endgame/superbosses)
    //   Reaper  = the lone roaming superboss (race 14, 175820 EXP) carved out of Boss for its own knob
    public enum EnemyGroup { Normal, Strong, Rare, Miniboss, Boss, Reaper }

    private double GroupMultiplier(EnemyGroup g) => _configuration.GlobalEnemyExp * (g switch
    {
        EnemyGroup.Normal => _configuration.NormalShadowExp,
        EnemyGroup.Strong => _configuration.StrongShadowExp,
        EnemyGroup.Rare => _configuration.RareShadowExp,
        EnemyGroup.Miniboss => _configuration.MinibossExp,
        EnemyGroup.Boss => _configuration.BossExp,
        EnemyGroup.Reaper => _configuration.ReaperExp,
        _ => 1.0,
    });

    private const string AssetRoot = @"P3R\Content";

    /// <summary>Patches all bundled assets into Generated/. Returns that folder, or null on failure/no-op.</summary>
    private string? Bake()
    {
        try
        {
            var modDir = _modLoader.GetDirectoryForModId(_modConfig.ModId);

            if (!BakeData.Ready)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] Offset tables not generated yet - mod is a no-op. (scaffold)", Color.Yellow);
                return null;
            }

            int total = 0;
            total += PatchEnemyExp(modDir);
            total += PatchLevelGapCurve(modDir);
            total += PatchShuffleWand(modDir);

            _logger.WriteLine($"[{_modConfig.ModId}] Baked EXP edits ({total} values changed from vanilla).", Color.LightGreen);
            return Path.Combine(modDir, "Generated");
        }
        catch (Exception e)
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Bake failed: {e}", Color.Red);
            return null;
        }
    }

    /// <summary>DatEnemyDataAsset: scale each enemy's exp (UInt32) by its group multiplier.</summary>
    private int PatchEnemyExp(string modDir)
    {
        int changed = 0;
        foreach (var (relPath, enemies) in BakeData.EnemyAssets)
        {
            var bytes = LoadTemplate(modDir, relPath, out var outPath);
            if (bytes == null) continue;

            // Validate the bundled template matches the offsets we precomputed before touching it.
            foreach (var e in enemies)
            {
                if (e.Offset + 4 > bytes.Length || BitConverter.ToUInt32(bytes, e.Offset) != e.VanillaExp)
                {
                    _logger.WriteLine($"[{_modConfig.ModId}] {relPath}: validation failed at offset {e.Offset} (game data version differs?); skipping this asset.", Color.Red);
                    bytes = null;
                    break;
                }
            }
            if (bytes == null) continue;

            foreach (var e in enemies)
            {
                double mult = GroupMultiplier((EnemyGroup)e.Group);
                uint v = (uint)Math.Max(0, Math.Round(e.VanillaExp * mult));
                if (v != e.VanillaExp) changed++;
                BitConverter.GetBytes(v).CopyTo(bytes, e.Offset);
            }
            File.WriteAllBytes(outPath, bytes);
            if (_configuration.LogToConsole)
                _logger.WriteLine($"[{_modConfig.ModId}]   {relPath}: {enemies.Length} enemies", Color.Gray);
        }
        return changed;
    }

    /// <summary>DT_BtlCalcLevelExpRatio: lerp each Ratio float toward 1.0 by the scaling strength.</summary>
    private int PatchLevelGapCurve(string modDir)
    {
        double s = _configuration.LevelGapScalingStrength;
        int changed = 0;
        foreach (var (relPath, cells) in BakeData.LevelCurveAssets)
        {
            var bytes = LoadTemplate(modDir, relPath, out var outPath);
            if (bytes == null || !ValidateFloats(bytes, relPath, cells)) continue;
            foreach (var c in cells)
            {
                float v = (float)Math.Max(0.0, 1.0 + (c.Vanilla - 1.0) * s);  // clamp: strength>1 can't go negative
                if (Math.Abs(v - c.Vanilla) > 1e-6) changed++;
                BitConverter.GetBytes(v).CopyTo(bytes, c.Offset);
            }
            File.WriteAllBytes(outPath, bytes);
        }
        return changed;
    }

    /// <summary>DatShuffleWandArcanaDataAsset: scale each Wand coefficient float by the multiplier.</summary>
    private int PatchShuffleWand(string modDir)
    {
        double m = _configuration.ShuffleWandExp;
        int changed = 0;
        foreach (var (relPath, cells) in BakeData.ShuffleAssets)
        {
            var bytes = LoadTemplate(modDir, relPath, out var outPath);
            if (bytes == null || !ValidateFloats(bytes, relPath, cells)) continue;
            foreach (var c in cells)
            {
                float v = (float)Math.Max(0.0, c.Vanilla * m);
                if (Math.Abs(v - c.Vanilla) > 1e-6) changed++;
                BitConverter.GetBytes(v).CopyTo(bytes, c.Offset);
            }
            File.WriteAllBytes(outPath, bytes);
        }
        return changed;
    }

    /// <summary>Verify a bundled float table still matches the precomputed vanilla values before patching.</summary>
    private bool ValidateFloats(byte[] bytes, string relPath, BakeData.FloatCell[] cells)
    {
        foreach (var c in cells)
            if (c.Offset + 4 > bytes.Length || Math.Abs(BitConverter.ToSingle(bytes, c.Offset) - c.Vanilla) > 1e-4)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] {relPath}: validation failed at offset {c.Offset} (game data version differs?); skipping this asset.", Color.Red);
                return false;
            }
        return true;
    }

    /// <summary>Reads a bundled template into memory and resolves its Generated/ output path.</summary>
    private byte[]? LoadTemplate(string modDir, string relPath, out string outPath)
    {
        var src = Path.Combine(modDir, "Assets", relPath);
        outPath = Path.Combine(modDir, "Generated", AssetRoot, relPath);
        if (!File.Exists(src))
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Missing bundled asset: {relPath}", Color.Red);
            return null;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        return File.ReadAllBytes(src);
    }

    private void Register(string generatedFolder)
    {
        var controller = _modLoader.GetController<IUnrealEssentials>();
        if (controller == null || !controller.TryGetTarget(out var ue))
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Could not get Unreal Essentials controller; is it enabled?", Color.Red);
            return;
        }
        ue.AddFromFolder(generatedFolder);
        _logger.WriteLine($"[{_modConfig.ModId}] Registered patched EXP tables with Unreal Essentials.", Color.LightGreen);
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _configuration = configuration;
        Bake();
        _logger.WriteLine($"[{_modConfig.ModId}] Config updated - restart the game for changes to take effect.", Color.Yellow);
    }

    public Mod() { _modLoader = null!; _logger = null!; _modConfig = null!; _configuration = null!; }
}
