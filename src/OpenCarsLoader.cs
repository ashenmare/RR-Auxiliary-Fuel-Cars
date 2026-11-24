using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;

namespace AuxFuelCars
{
    // 2.1.0, open cars: the game side of OpenCars. At each world load, before the Feed checkbox is registered (so the
    // declared cars get it), every pack the game loaded (AssetPackManager.OrderedPacks: the enabled set, in load order)
    // is asked for aux-fuel-cars.json at its folder's top (AssetPackRuntimeStore.BasePath); each file found is read as
    // text, parsed, and each car checked against this world's car definitions. Logs name the pack by its identifier,
    // never by its folder. Called from Main through a NoInlining helper inside a try.
    internal static class OpenCarsLoader
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Load()
        {
            var into = new Dictionary<string, OpenCars.Entry>(StringComparer.Ordinal);
            var takers = new Dictionary<string, OpenTakers.Entry>(StringComparer.Ordinal);
            var log = new List<string>();
            AssetPackManager apm = AssetPackManager.Shared;
            IReadOnlyList<AssetPackManager.PackEntry> packs = apm?.OrderedPacks;
            OpenCars.Lookup lookup = (string defId, out bool engine) =>
            {
                engine = false;
                if (!apm.TryGetDefinitionInfo(defId, out TypedContainerItem<CarDefinition> info) || info?.Definition == null) return null;
                engine = info.Definition is LocomotiveDefinition;
                return SlotLoads(info.Definition);
            };
            // The files found, in load order: the aux cars of every pack first, then the takers (2.3.0), so a car one pack
            // declares as a taker and another as an aux car is known as an aux car whichever loads first.
            var files = new List<KeyValuePair<string, string>>();
            if (packs != null)
                foreach (AssetPackManager.PackEntry pack in packs)
                {
                    string dir = pack?.Store?.BasePath;
                    if (string.IsNullOrEmpty(dir)) continue;
                    string id = pack.Store.Identifier ?? "a pack";   // the pack's Info.json identifier (AssetPackManager makes each store with it)
                    string text = Read(Path.Combine(dir, OpenCars.FileName), id, log);
                    if (text == null) continue;
                    files.Add(new KeyValuePair<string, string>(id, text));
                    // Each file on its own; one that throws is skipped with one line naming its pack.
                    OpenCars.ReadPack(text, id, lookup, into, log);
                }
            foreach (KeyValuePair<string, string> f in files) OpenTakers.ReadPack(f.Value, f.Key, lookup, into, takers, log);
            OpenTakers.Set(takers);
            var tanks = new List<OpenEngineTanks.Entry>();
            foreach (KeyValuePair<string, string> f in files) OpenEngineTanks.ReadPack(f.Value, f.Key, tanks, log);
            OpenEngineTanks.Set(tanks);
            OpenCars.Set(into);
            foreach (string line in log) Util.Warn(line);
            if (into.Count > 0)
            {
                var names = new List<string>();
                foreach (OpenCars.Entry d in into.Values) names.Add(d.Id + " (" + d.Pack + ")");
                names.Sort(StringComparer.Ordinal);
                if (DebugLog.On) Util.Detail("other-packs-cars", "other packs' aux cars: " + string.Join(", ", names));
            }
            if (tanks.Count > 0)
            {
                var names = new List<string>();
                foreach (OpenEngineTanks.Entry d in tanks) names.Add(d.Load + " (" + d.Pack + ": " + d.LevelKey + " of " + d.CapacityKey + ")");
                if (DebugLog.On) Util.Detail("other-packs-engine-tanks", "other packs' engine tanks: " + string.Join(", ", names));
            }
            if (takers.Count > 0)
            {
                var names = new List<string>();
                foreach (OpenTakers.Entry d in takers.Values) names.Add(d.Id + " (" + d.Pack + ": " + string.Join("+", d.Loads) + ")");
                names.Sort(StringComparer.Ordinal);
                if (DebugLog.On) Util.Detail("other-packs-takers", "other packs' takers: " + string.Join(", ", names));
            }
        }

        // The load id of each slot (null for a slot that takes any load).
        internal static List<string> SlotLoads(CarDefinition def)
        {
            var list = new List<string>();
            if (def?.LoadSlots != null)
                foreach (LoadSlot s in def.LoadSlots) list.Add(s?.RequiredLoadIdentifier);
            return list;
        }

        // The file's text, or null: none there (the usual case), too big, or unreadable (one line, no path).
        private static string Read(string path, string pack, List<string> log)
        {
            try
            {
                var f = new FileInfo(path);
                if (!f.Exists) return null;
                if (f.Length > OpenCars.MaxBytes) { log.Add(OpenCars.FileName + " in " + pack + ": over " + (OpenCars.MaxBytes / 1024) + " KB; skipped"); return null; }
                return File.ReadAllText(path);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is System.Security.SecurityException || x is NotSupportedException || x is ArgumentException)
            {
                // The exception's type name only: its message can carry the path.
                log.Add(OpenCars.FileName + " in " + pack + ": couldn't be read (" + x.GetType().Name + "); skipped");
                return null;
            }
        }
    }
}
