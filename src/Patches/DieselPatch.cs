using Game.State;
using HarmonyLib;
using Model;
using Model.Definition;

namespace ExtraTenders.Patches;

/// <summary>
/// After each periodic fuel tick, top up the diesel locomotive's tank from an
/// external fuel car (if one is coupled at either end).  This makes the
/// external car drain first; the locomotive's own tank only starts dropping
/// once the external car is empty.
///
/// Expected consist: [Aux Tank Car] — [Diesel Loco]  (either end)
///
/// Only cars with carType "DT" are eligible as aux sources,
/// preventing accidental drain of third-party cars or MU-coupled locos.
/// </summary>
[HarmonyPatch(typeof(DieselLocomotive), "PeriodicUpdate")]
internal static class DieselLocomotivePatch
{
    private const string DieselFuelId = "diesel-fuel";

    static void Postfix(DieselLocomotive __instance)
    {
        if (!StateManager.IsHost) return;

        // Find a non-locomotive coupled car that carries diesel fuel.
        if (!TryGetAuxFuelCar(__instance, out var aux)) return;

        FuelTransfer.InitializeIfNew(aux);
        FuelTransfer.TopUp(aux, __instance, DieselFuelId);
    }

    // ------------------------------------------------------------------

    private static bool TryGetAuxFuelCar(Car loco, out Car aux)
    {
        if (loco.TryGetAdjacentCar(Car.LogicalEnd.A, out aux) && IsOurAuxFuelCar(aux)) return true;
        if (loco.TryGetAdjacentCar(Car.LogicalEnd.B, out aux) && IsOurAuxFuelCar(aux)) return true;
        aux = null!;
        return false;
    }

    // Only drain cars we created — DT is our custom carType for the diesel
    // fuel tender; third-party tank cars and locos are never matched.
    private static bool IsOurAuxFuelCar(Car car) =>
        car.CarType == "DT" && FuelTransfer.HasLoad(car, DieselFuelId);

}
