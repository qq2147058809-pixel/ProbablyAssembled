// Copyright © 2026 阿铭。复用须先取得作者书面许可，详见项目根目录 COPYRIGHT.md。
// 使用、修改或改编本项目代码制作 Mod 时，必须在该 Mod 的说明或鸣谢中注明
// 原作者“阿铭”、项目“PC expansion”及 https://github.com/qq2147058809-pixel/ProbablyAssembled 。
// Reuse requires prior written permission and visible mod credits naming 阿铭, PC expansion,
// and the source repository above. See COPYRIGHT.md; attribution alone does not grant permission.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MelonLoader;

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyCompany("阿铭")]
[assembly: AssemblyMetadata("Author", "阿铭")]
[assembly: MelonInfo(typeof(PCExpansionEnglishPatch.Patch), "PC expansion English Patch", "0.1.0", "阿铭", "")]
[assembly: MelonGame("Questing Goose Studio", "Probably Stolen")]

namespace PCExpansionEnglishPatch;

public sealed class Patch : MelonMod
{
    private bool finished;
    private bool waitingLogged;
    private int retryFrames;

    public override void OnInitializeMelon() => TryApply();

    public override void OnUpdate()
    {
        if (!finished && ++retryFrames >= 60)
        {
            retryFrames = 0;
            TryApply();
        }
    }

    private void TryApply()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "PCExpansion", StringComparison.Ordinal))
                    continue;

                var languageText = assembly.GetType("PCExpansion.LanguageText", false);
                var install = languageText?.GetMethod("InstallEnglishTranslations", BindingFlags.Public | BindingFlags.Static);
                finished = true;
                if (install == null)
                {
                    LoggerInstance.Warning("This base mod does not support the independent translation catalog. Update both DLLs together; Chinese remains active.");
                    return;
                }

                if (ApplyTo(languageText!)) LoggerInstance.Msg("Independent English translations loaded. No gameplay modules were registered by this patch.");
                else LoggerInstance.Warning("Translation contract rejected. Chinese remains active; use matching DLLs.");
                return;
            }

            if (!waitingLogged)
            {
                LoggerInstance.Warning("Base mod is missing. No translations or gameplay have been enabled. Install PCExpansion.dll alongside this patch.");
                waitingLogged = true;
            }
        }
        catch (Exception ex)
        {
            finished = true;
            LoggerInstance.Error("Could not load translations; Chinese remains active: " + ex.GetBaseException().Message);
        }
    }

    // 只通过反射连接本体；本补丁程序集没有本体/游戏程序集依赖。
    internal static bool ApplyTo(Type language)
    {
        var self = typeof(Patch).Assembly;
        using var catalogStream = self.GetManifestResourceStream("PCExpansionEnglishPatch.Localization.en.json")
            ?? throw new InvalidOperationException("English catalog is missing.");
        using var contractStream = self.GetManifestResourceStream("PCExpansionEnglishPatch.Localization.contract.json")
            ?? throw new InvalidOperationException("Translation contract is missing.");
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(catalogStream)
            ?? throw new InvalidOperationException("English catalog cannot be read.");
        using var contract = JsonDocument.Parse(contractStream);
        var schema = contract.RootElement.GetProperty("ContractVersion").GetString();
        var fingerprint = contract.RootElement.GetProperty("BaseCatalogSha256").GetString();
        var install = language.GetMethod("InstallEnglishTranslations", BindingFlags.Public | BindingFlags.Static);
        if (install == null || install.Invoke(null, new object?[] { schema, fingerprint, catalog }) is not true) return false;
        // 页面只嵌入独立补丁；页面错误不撤销其他物品的有效翻译。
        var pages = language.Assembly.GetType("PCExpansion.ComputerManualPages", false);
        var installPages = pages?.GetMethod("InstallEnglishManualPages", BindingFlags.Public | BindingFlags.Static);
        if (installPages == null)
        {
            MelonLogger.Warning("Computer manual page support is missing. Update both DLLs together.");
            return true;
        }
        try
        {
            using var pageStream = self.GetManifestResourceStream("PCExpansionEnglishPatch.Manual.en.json");
            using var reader = pageStream == null ? null : new StreamReader(pageStream, new UTF8Encoding(false, true));
            var pageJson = reader?.ReadToEnd() ?? string.Empty;
            var pageContract = contract.RootElement.TryGetProperty("ManualContractVersion", out var version)
                ? version.GetString() : string.Empty;
            if (installPages.Invoke(null, new object?[] { pageContract, fingerprint, pageJson }) is not true)
                MelonLogger.Warning("English computer manual pages were rejected; the complete Chinese manual remains available.");
        }
        catch (Exception ex)
        {
            // 包括资源损坏和接口异常；先清除旧页面，保证不会沿用不匹配内容。
            try { installPages.Invoke(null, new object?[] { string.Empty, fingerprint, string.Empty }); } catch { }
            MelonLogger.Warning("English manual could not be loaded; Chinese pages remain active: " + ex.GetBaseException().Message);
        }
        return true;
    }
}
