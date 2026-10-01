using System;
using System.Reflection;
using MelonLoader;

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyCompany("阿铭")]
[assembly: AssemblyMetadata("Author", "阿铭")]
[assembly: MelonInfo(typeof(ProbablyAssembledEnglishPatch.Patch), "Probably Assembled English Patch", "0.1.0", "阿铭", "")]
[assembly: MelonGame("Questing Goose Studio", "Probably Stolen")]

namespace ProbablyAssembledEnglishPatch;

public sealed class Patch : MelonMod
{
    private bool applied;
    private bool missingTargetLogged;

    public override void OnInitializeMelon() => TryApply();

    public override void OnUpdate()
    {
        if (!applied) TryApply();
    }

    private void TryApply()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "ProbablyAssembled", StringComparison.Ordinal))
                    continue;

                var languageText = assembly.GetType("ProbablyAssembled.LanguageText", false);
                var enable = languageText?.GetMethod("EnableEnglishPatch", BindingFlags.Public | BindingFlags.Static);
                if (enable == null)
                {
                    if (!missingTargetLogged)
                    {
                        LoggerInstance.Warning("ProbablyAssembled was found, but it does not expose the English patch entry point.");
                        missingTargetLogged = true;
                    }
                    return;
                }

                enable.Invoke(null, null);
                applied = true;
                LoggerInstance.Msg("English text enabled for Probably Assembled.");
                return;
            }

            if (!missingTargetLogged)
            {
                LoggerInstance.Warning("Waiting for ProbablyAssembled. Install the base mod alongside this patch.");
                missingTargetLogged = true;
            }
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("Could not enable English text: " + ex.GetBaseException().Message);
        }
    }
}
