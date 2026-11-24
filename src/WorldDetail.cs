using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Model.Ops;

namespace AuxFuelCars
{
    // The game-side half of the world rows (WorldRows.cs): every component that carries rr/diesel-fuel, read from the loaded world.
    // CHECK: OpsController.Shared.AllIndustryComponents (IndustryComponent[]), IndustryComponent.Industry / subIdentifier / carTypeFilter
    // (CarTypeFilter.ToString() is its query string), InterchangedIndustryLoader.loadId, IndustryUnloader.loadId / orderLoads /
    // orderAwayEmpties, Industry.identifier (0.6.3 decompile, Model.Ops).
    internal static class WorldDetail
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void EmitRows(string reason)
        {
            if (!DebugLog.On) return;
            OpsController ops = OpsController.Shared;
            IndustryComponent[] all = ops == null ? null : ops.AllIndustryComponents;
            if (all == null) { if (DebugLog.On) DebugLog.Event(Main.Id, "diesel-filters", "reason", reason, "ready", false); return; }
            var parts = new List<DieselPart>();
            foreach (IndustryComponent c in all)
            {
                var l = c as InterchangedIndustryLoader;
                var u = c as IndustryUnloader;
                if (l != null ? l.loadId != Logic.Diesel : (u == null || u.loadId != Logic.Diesel)) continue;
                parts.Add(new DieselPart
                {
                    Industry = c.Industry == null ? "" : c.Industry.identifier,
                    Component = c.subIdentifier,
                    IsLoader = l != null,
                    OrdersCars = u != null && (u.orderLoads || u.orderAwayEmpties),
                    Filter = c.carTypeFilter == null ? "" : c.carTypeFilter.ToString(),
                });
            }
            if (DebugLog.On) DebugLog.Event(Main.Id, "diesel-world", "reason", reason, "ready", true);
            foreach (Row r in WorldRows.Build(parts))
                if (DebugLog.On) DebugLog.Event(Main.Id, r.Name, r.Kv);
        }
    }
}
