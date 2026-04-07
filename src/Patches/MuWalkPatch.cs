using HarmonyLib;
using Model;
using Model.Definition;
using Model.Physics;

namespace ExtraTenders.Patches;

/// <summary>
/// Vanilla MU control propagation walks the coupled consist in
/// FindSourceLocomotive and skips cars with CarArchetype.Tender —
/// that's why vanilla steam tenders are "transparent" to MU.
///
/// Any other non-locomotive car breaks the walk: the method returns null
/// and the trailing loco loses MU.
///
/// This patch replaces FindSourceLocomotive with a copy that also skips
/// our aux cars (DT diesel tank, AUXB steam tender) so the vanilla MU
/// behaviour is preserved for DPC-free players.
/// </summary>
[HarmonyPatch(typeof(BaseLocomotive), "FindSourceLocomotive")]
internal static class MuWalkPatch
{
    // Returning false tells Harmony to skip the original method.
    static bool Prefix(
        BaseLocomotive __instance,
        Car.LogicalEnd searchDirection,
        ref BaseLocomotive? __result)
    {
        bool stop = false;
        int? idx = __instance.set.IndexOfCar(__instance);
        if (!idx.HasValue)
        {
            // Let the original run so it throws with its own message.
            return true;
        }

        int carIndex = idx.Value;

        // The walk starts from the opposite end to the search direction,
        // exactly as the original does.
        var fromEnd = (searchDirection == Car.LogicalEnd.A)
            ? Car.LogicalEnd.B
            : Car.LogicalEnd.A;

        Car car;
        while (!stop &&
               (car = __instance.set.NextCarConnected(
                    ref carIndex,
                    fromEnd,
                    IntegrationSet.EnumerationCondition.AirAndCoupled,
                    out stop)) != null)
        {
            if (car == __instance)                    continue; // skip self
            if (car.Archetype == CarArchetype.Tender) continue; // vanilla tender skip
            if (IsOurAuxCar(car))                     continue; // our aux cars skip

            // Anything else that is not a locomotive stops the walk.
            if (!car.IsLocomotive || car is not BaseLocomotive sourceLoco)
            {
                __result = null;
                return false;
            }

            // Found a coupled, non-cut-out locomotive — this is the MU source.
            if (!sourceLoco.locomotiveControl.air.IsCutOut)
            {
                __result = sourceLoco;
                return false;
            }
        }

        __result = null;
        return false;
    }

    // Our aux cars: DT (diesel fuel tender) and AUXT (steam aux tender).
    // Both use Tank archetype so they are spawnable, but must not break the MU walk.
    private static bool IsOurAuxCar(Car car) =>
        car.CarType == "DT" || car.CarType == "AUXT";
}
