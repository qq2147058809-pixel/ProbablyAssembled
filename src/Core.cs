// Copyright © 2026 阿铭。复用须先取得作者书面许可，详见项目根目录 COPYRIGHT.md。
// 使用、修改或改编本项目代码制作 Mod 时，必须在该 Mod 的说明或鸣谢中注明
// 原作者“阿铭”、项目“PC expansion”及 https://github.com/qq2147058809-pixel/ProbablyAssembled 。
// Reuse requires prior written permission and visible mod credits naming 阿铭, PC expansion,
// and the source repository above. See COPYRIGHT.md; attribution alone does not grant permission.

using System;
using HarmonyLib;
using MelonLoader;

[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyCompany("阿铭")]
[assembly: System.Reflection.AssemblyMetadata("Author", "阿铭")]
[assembly: MelonInfo(typeof(PCExpansion.Core), "深空装机 - PC expansion", "0.1.0", "阿铭", "")]
[assembly: MelonGame("Questing Goose Studio", "Probably Stolen")]

namespace PCExpansion;

public sealed class Core : MelonMod
{
    internal static MelonLogger.Instance? Log { get; private set; }
    internal static MelonPreferences_Entry<bool>? DebugLogging { get; private set; }
    private static MelonPreferences_Category? preferenceCategory;
    private static long hoverWarnAt;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        WorkroomTrial.OnSceneChanged();
        WorkroomGameAdapter.ResetPatchStatus();
        var preferences = MelonPreferences.CreateCategory(
            "PCExpansion", LanguageText.Get("preferences.category"));
        preferenceCategory = preferences;
        DebugLogging = preferences.CreateEntry(
            "DebugLogging", false, LanguageText.Get("preferences.debug.name"),
            LanguageText.Get("preferences.debug.description"));
        LanguageText.LanguageChanged += RefreshPreferenceLabels;
        // 不使用一次性 PatchAll：一个过期补丁不应阻止其他独立模块加载。
        HarmonyInstance.UnpatchSelf();
        var patched = 0;
        foreach (var type in typeof(Core).Assembly.GetTypes())
        {
            if (!type.IsClass || type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try
            {
                new PatchClassProcessor(HarmonyInstance, type).Patch();
                WorkroomGameAdapter.PatchInstalled(type, HarmonyInstance.Id);
                patched++;
            }
            catch (Exception ex)
            {
                Log.Error("补丁失败（已隔离）" + type.Name + "：" + ex.Message);
            }
        }

        WorkroomGameAdapter.ReportPatchStatus();
        Log.Msg("深空装机 / PC expansion v0.1.0 已加载，作者：阿铭。");
        Log.Msg("已启用补丁模块：" + patched + " 个。");
    }

    internal static void Debug(string message)
    {
        if (DebugLogging?.Value ?? false) Log?.Msg(message);
    }

    public override void OnUpdate()
    {
        WorkroomCashRepair.RetryRecovery();
        ComputerSupplierNpcs.UpdateStockCleanup();
        NpcStockOffers.Update();
        WorkroomTrial.Update();
        try { AssemblyDebugUi.UpdateHover(); }
        catch (Exception ex)
        {
            if (Environment.TickCount64 < hoverWarnAt) return;
            hoverWarnAt = Environment.TickCount64 + 5000;
            Log?.Warning("工作间按钮悬停更新失败：" + ex);
        }
    }

    public override void OnLateUpdate()
    {
        WorkroomStoreKey.Update();
        try { WorkroomEntryHover.LateUpdate(); }
        catch (Exception ex)
        {
            WorkroomNativeTooltip.Warn(ex);
            WorkroomTrial.OnSceneChanged();
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        WorkroomTrial.OnSceneChanged();
        AssemblyDebugFonts.Reset();
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) => WorkroomTrial.OnSceneChanged();
    public override void OnApplicationQuit() => WorkroomTrial.OnSceneChanged();
    public override void OnDeinitializeMelon() => WorkroomTrial.OnSceneChanged();

    private static void RefreshPreferenceLabels()
    {
        if (preferenceCategory != null) preferenceCategory.DisplayName = LanguageText.Get("preferences.category");
        if (DebugLogging == null) return;
        DebugLogging.DisplayName = LanguageText.Get("preferences.debug.name");
        DebugLogging.Description = LanguageText.Get("preferences.debug.description");
    }

    internal static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\0", string.Empty).Trim();
}
