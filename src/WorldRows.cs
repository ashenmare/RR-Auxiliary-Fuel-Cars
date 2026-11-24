using System.Collections.Generic;

namespace AuxFuelCars
{
    // One diesel component as the world has it at load (no game types, so tools/probe tests the rows offline).
    internal sealed class DieselPart
    {
        internal string Industry, Component;
        internal bool IsLoader;          // an InterchangedIndustryLoader (else an IndustryUnloader)
        internal bool OrdersCars;        // an unloader whose orderLoads or orderAwayEmpties is not false (Data.rl leaves it alone)
        internal string Filter;          // its car types as the game resolved them
    }

    internal sealed class Row
    {
        internal string Name;
        internal object[] Kv;
    }

    // The debug log's rows for the map script's per-component edits (1.9: detail goes to the shared debug log, never Player.log).
    // Data.rl can't reach the debug switch, so the DLL reads back what the world ended up with.
    internal static class WorldRows
    {
        internal static string Kind(DieselPart p) => p.IsLoader ? "loader" : (p.OrdersCars ? "orders" : "stand");

        // Whether the filter matches the aux diesel car type, read as the game reads a car type filter (CarTypeFilter.Matches, 0.6.3):
        // a comma list with no trimming; an entry ending in * is a prefix, any other is an exact match ignoring case.
        internal static bool HasAxd(string filter)
        {
            if (filter == null) return false;
            foreach (string entry in filter.Split(','))
            {
                if (entry.EndsWith("*")) { if (Ids.DieselCarType.StartsWith(entry.Substring(0, entry.Length - 1))) return true; }
                else if (string.Equals(Ids.DieselCarType, entry, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static List<Row> Build(List<DieselPart> parts)
        {
            var rows = new List<Row>();
            int withAxd = 0;
            foreach (DieselPart p in parts)
            {
                bool axd = HasAxd(p.Filter);
                if (axd) withAxd++;
                rows.Add(new Row { Name = "diesel-filter", Kv = new object[] { "industry", p.Industry, "component", p.Component, "kind", Kind(p), "filter", p.Filter ?? "", "axd", axd } });
            }
            rows.Add(new Row { Name = "diesel-filters", Kv = new object[] { "parts", parts.Count, "withAxd", withAxd } });
            return rows;
        }
    }
}
