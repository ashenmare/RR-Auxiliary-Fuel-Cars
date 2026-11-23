using System.Collections.Generic;

namespace AuxFuelCars
{
    // The three cars' definition identifiers, as Scripts/Data.rl creates them (tools/probe checks the two agree).
    // The mod finds its cars by these, never by car type, so another mod's car that happens to share a type string
    // is never drawn from.
    internal static class Ids
    {
        internal const string WaterCar = "ashenmare/aux-water-car";
        internal const string DieselCar = "ashenmare/aux-diesel-car";
        internal const string ExtraTender = "ashenmare/aux-extra-tender";

        internal static readonly HashSet<string> All = new HashSet<string> { WaterCar, DieselCar, ExtraTender };

        // The car type prefix all three share (AXW, AXD, AXT), named in the shop's detail line and grouped in the placer.
        internal const string TypePrefix = "AX";
        internal const string TypeDisplayName = "Auxiliary Fuel";

        // The diesel car's own car type (Data.rl sets it; tools/probe checks the two agree). 2.0.5 registers exactly
        // this as a freight car type prefix (FreightType), so the inspector offers the diesel car waybills; never "AX",
        // which would make the water car and the Extra Tender freight too.
        internal const string DieselCarType = "AXD";

        // The car's synced KeyValue key: a dictionary, so later per-car options can sit beside "feed".
        internal const string KvKey = "ashenmare.auxfuel";
        internal const string FeedKey = "feed";
    }
}
