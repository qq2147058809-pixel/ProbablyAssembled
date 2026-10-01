using System;
using HarmonyLib;
using MelonLoader;

[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyCompany("阿铭")]
[assembly: System.Reflection.AssemblyMetadata("Author", "阿铭")]
[assembly: MelonInfo(typeof(ProbablyAssembled.Core), "深空装机 - Probably Assembled", "0.1.0", "阿铭", "")]
[assembly: MelonGame("Questing Goose Studio", "Probably Stolen")]

namespace ProbablyAssembled;

public sealed class Core : MelonMod
{
    internal static MelonLogger.Instance? Log { get; private set; }
    internal static MelonPreferences_Entry<bool>? DebugLogging { get; private set; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        var preferences = MelonPreferences.CreateCategory(
            "ProbablyAssembled", "深空装机 / Probably Assembled");
        DebugLogging = preferences.CreateEntry(
            "DebugLogging", false, "输出调试日志 / Debug logging",
            "输出物品注册、双击开箱和补丁命中的详细日志。 / Log item registration, case opening, and patch diagnostics.");
        // 不使用一次性 PatchAll：一个过期补丁不应阻止其他独立模块加载。
        HarmonyInstance.UnpatchSelf();
        var patched = 0;
        foreach (var type in typeof(Core).Assembly.GetTypes())
        {
            if (!type.IsClass || type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try
            {
                new PatchClassProcessor(HarmonyInstance, type).Patch();
                patched++;
            }
            catch (Exception ex)
            {
                Log.Error("补丁失败（已隔离）" + type.Name + "：" + ex.Message);
            }
        }

        Log.Msg("深空装机 / Probably Assembled v0.1.0 已加载，作者：阿铭。");
        Log.Msg("已启用补丁模块：" + patched + " 个。");
    }

    internal static void Debug(string message)
    {
        if (DebugLogging?.Value ?? false) Log?.Msg(message);
    }

    internal static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\0", string.Empty).Trim();
}
