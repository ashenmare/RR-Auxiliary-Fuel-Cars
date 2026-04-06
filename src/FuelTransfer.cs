using Model.Definition.Data;
using Model.Ops;
using UnityEngine;

namespace ExtraTenders;

/// <summary>
/// Helpers for finding load slots and transferring fuel between cars.
/// </summary>
internal static class FuelTransfer
{
    /// <summary>
    /// Tops up <paramref name="dest"/> from <paramref name="source"/> for the given load identifier.
    /// Transfers only the deficit so the destination stays at capacity.
    /// Returns the amount actually transferred (0 if nothing moved).
    /// </summary>
    internal static float TopUp(Model.Car source, Model.Car dest, string loadId)
    {
        var srcInfo = source.GetLoadInfo(loadId, out int srcSlot);
        if (!srcInfo.HasValue || srcSlot < 0 || srcInfo.Value.Quantity < 0.001f)
            return 0f;

        dest.GetLoadInfo(loadId, out int dstSlot);
        if (dstSlot < 0) return 0f;

        float capacity   = dest.Definition.LoadSlots[dstSlot].MaximumCapacity;
        var   dstInfo    = dest.GetLoadInfo(dstSlot);
        float currentQty = dstInfo.HasValue ? dstInfo.Value.Quantity : 0f;
        float deficit    = capacity - currentQty;
        if (deficit < 0.001f)
            return 0f;

        float transfer = Mathf.Min(deficit, srcInfo.Value.Quantity);

        dest.SetLoadInfo(dstSlot,   new CarLoadInfo(loadId, currentQty             + transfer));
        source.SetLoadInfo(srcSlot, new CarLoadInfo(loadId, srcInfo.Value.Quantity - transfer));

        return transfer;
    }

    /// <summary>
    /// Returns true if <paramref name="car"/> has any load slot whose
    /// RequiredLoadIdentifier matches <paramref name="loadId"/>, or which
    /// currently contains that load.
    /// </summary>
    internal static bool HasLoad(Model.Car car, string loadId)
    {
        car.GetLoadInfo(loadId, out int slot);
        return slot >= 0;
    }

    /// <summary>
    /// If all load slots on <paramref name="car"/> are null (never been
    /// touched — freshly placed, not loaded from save), seeds each slot to
    /// <see cref="Settings.StartingFuelPercent"/> percent of its capacity.
    /// Does nothing if starting fuel is set to 0 or the car already has
    /// load data.
    /// </summary>
    internal static void InitializeIfNew(Model.Car car)
    {
        float pct = Main.Settings.StartingFuelPercent / 100f;
        if (pct < 0.001f) return;

        var slots = car.Definition.LoadSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            // Any non-null slot means this car was already initialized or
            // loaded from a save — leave it untouched.
            if (car.GetLoadInfo(i).HasValue) return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            string? id = slots[i].RequiredLoadIdentifier;
            if (id == null) continue;
            car.SetLoadInfo(i, new CarLoadInfo(id, slots[i].MaximumCapacity * pct));
        }
    }
}
