using HarmonyLib;
using Model;

namespace ExtraTenders.Patches;

/// <summary>
/// Seeds fuel into freshly placed aux cars so they spawn with fuel
/// without needing a locomotive coupled first.
///
/// InitializeIfNew is a no-op when load slots already have data
/// (save load, or already coupled to a loco), so this is safe to call
/// unconditionally on Start.
/// </summary>
[HarmonyPatch(typeof(Car), "Start")]
internal static class CarSpawnPatch
{
    static void Postfix(Car __instance)
    {
        if (__instance.CarType == "DT" || __instance.CarType == "AUXT")
        {
            FuelTransfer.InitializeIfNew(__instance);
        }
    }
}
