using HarmonyLib;
using Model.Ops;
using Model.Ops.Definition;

namespace ExtraTenders.Patches;

/// <summary>
/// Makes our aux cars load 10x faster at coal towers, water towers, and
/// fuel stands by multiplying quantityToLoad in OpsCarAdapter.Load when
/// the car is one of ours.
///
/// Patching at the car level (rather than the service point) means:
///   - Vanilla locos and tenders at the same service point are unaffected.
///   - No interaction with other mods that patch IndustryLoader.Service.
/// </summary>
[HarmonyPatch(typeof(OpsCarAdapter), nameof(OpsCarAdapter.Load))]
internal static class LoaderSpeedPatch
{
    // ref __instance is required for Harmony to access struct instance members.
    static void Prefix(ref OpsCarAdapter __instance, Load load, ref float quantityToLoad)
    {
        if (!IsOurCar(__instance)) return;
        if (!IsFuelLoad(load))     return;
        quantityToLoad *= Main.Settings.LoadMultiplier;
    }

    private static bool IsOurCar(OpsCarAdapter car)
    {
        var t = car.CarType;
        return t == "DT" || t.StartsWith("AUX", System.StringComparison.Ordinal);
    }

    private static bool IsFuelLoad(Load load)
    {
        var id = load.id;
        return id == "coal" || id == "water" || id == "diesel-fuel";
    }
}
