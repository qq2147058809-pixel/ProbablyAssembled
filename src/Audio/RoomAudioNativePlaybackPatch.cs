using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

// Pausing an AudioSource makes isPlaying false. The cassette coroutine otherwise
// mistakes our region lease for a finished track and immediately selects the next.
[HarmonyPatch(typeof(AudioManager), nameof(AudioManager._WaitForSound_b__10_1))]
internal static class RoomAudioNativePlaybackPatch
{
    [HarmonyPrefix]
    private static bool Prefix(AudioManager __instance, ref bool __result)
    {
        if (!RoomAudio.HoldsNativePlayback(__instance)) return true;
        __result = true;
        return false;
    }
}
