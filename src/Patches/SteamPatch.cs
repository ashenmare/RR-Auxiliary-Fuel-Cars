using Game.State;
using HarmonyLib;
using Model;
using Model.Definition;


namespace ExtraTenders.Patches;

/// <summary>
/// After each periodic fuel tick, top up the primary tender from an auxiliary
/// tender (if one is coupled behind it).  This makes the aux tender drain first
/// and the primary tender only start emptying once the aux is gone.
///
/// Expected consist: [Steam Loco] — [Primary Tender] — [Aux Tender]
/// </summary>
[HarmonyPatch(typeof(SteamLocomotive), "PeriodicUpdate")]
internal static class SteamLocomotivePatch
{
    // Runs after the game has already consumed coal/water from the primary tender.
    static void Postfix(SteamLocomotive __instance)
    {
        if (!StateManager.IsHost) return;

        // Find the primary tender (Tender archetype adjacent to the locomotive).
        // Note: hasTender is intentionally not checked — it only returns true for
        // the loco's designated rear end, which breaks back-to-back MU consists.
        if (!TryGetPrimaryTender(__instance, out var primary)) return;

        // Find an aux tender coupled to the other end of the primary tender.
        if (!TryGetAuxTender(primary, __instance, out var aux)) return;

        FuelTransfer.InitializeIfNew(aux);
        FuelTransfer.TopUp(aux, primary, "coal");
        FuelTransfer.TopUp(aux, primary, "water");
    }

    // ------------------------------------------------------------------

    private static bool TryGetPrimaryTender(Car loco, out Car tender)
    {
        if (loco.TryGetAdjacentCar(Car.LogicalEnd.A, out tender) && tender.Archetype == CarArchetype.Tender) return true;
        if (loco.TryGetAdjacentCar(Car.LogicalEnd.B, out tender) && tender.Archetype == CarArchetype.Tender) return true;
        tender = null!;
        return false;
    }

    private static bool TryGetAuxTender(Car primary, Car loco, out Car aux)
    {
        // Check both ends of the primary tender; skip the end pointing at the
        // locomotive, and never treat another locomotive as an aux tender.
        // This keeps MU consists ([S1]—[T1]—[S2]—[T2]) safe: the car at the
        // far end of T1 is S2 (a locomotive), which is excluded.
        foreach (var end in new[] { Car.LogicalEnd.A, Car.LogicalEnd.B })
        {
            if (!primary.TryGetAdjacentCar(end, out var candidate)) continue;
            if (candidate == loco) continue;
            if (!IsOurAuxTender(candidate)) continue;

            if (FuelTransfer.HasLoad(candidate, "coal") || FuelTransfer.HasLoad(candidate, "water"))
            {
                aux = candidate;
                return true;
            }
        }

        aux = null!;
        return false;
    }

    // Only drain cars we created — all our aux tender carTypes start with "AUX".
    // Vanilla tenders never use this prefix, so facing tenders in back-to-back
    // MU consists ([L1]—[T1]—[T2]—[L2]) are still safe.
    private static bool IsOurAuxTender(Car car) =>
        car.CarType.StartsWith("AUX", System.StringComparison.Ordinal);

}
