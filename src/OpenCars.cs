using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuxFuelCars
{
    // Open cars: another pack declares its own cars as aux cars with a file named aux-fuel-cars.json
    // in its own folder:
    //   { "auxFuelCars": 1, "cars": [ { "id": "battlestar/gtel-c", "loads": ["water", "diesel-fuel"] } ] }
    // auxFuelCars is the format (1; a higher one is logged and the file skipped). id is a car definition identifier,
    // required: cars are matched by id, never by car type, so a car type that happens to start like this mod's (AX) or
    // not changes nothing. loads is optional: when given, the car gives only those loads; left out, every load it has a
    // slot for, as the mod's own cars do. A load name without a slash means "rr/<name>"; with a slash it is used as is.
    //
    // Data only: the files are read as text at each world load (OpenCarsLoader, from the packs the game loaded); nothing
    // in another pack is run. A bad file, a bad entry or a car that isn't defined costs one Player.log line and is
    // skipped, never more. This class names only the BCL and Newtonsoft (no game type), so tools/probe runs it as it is.
    internal static class OpenCars
    {
        internal const string FileName = "aux-fuel-cars.json";
        internal const int Format = 1;
        // A declaration is a few lines; anything this big is not one.
        internal const long MaxBytes = 256 * 1024;

        internal sealed class Entry
        {
            internal string Id;
            internal string Pack;
            // The load ids it gives; null: every load it has a slot for.
            internal HashSet<string> Loads;
        }

        // The declared cars in force, by definition id. Replaced whole at each world load (a new dictionary, never
        // changed after), so a reader on any thread sees one world's set.
        private static volatile Dictionary<string, Entry> _cars = new Dictionary<string, Entry>(StringComparer.Ordinal);

        internal static IReadOnlyDictionary<string, Entry> Cars => _cars;

        internal static void Set(Dictionary<string, Entry> cars) => _cars = cars ?? new Dictionary<string, Entry>(StringComparer.Ordinal);

        internal static void Clear() => Set(null);

        internal static bool Has(string id) => id != null && _cars.ContainsKey(id);

        // A car of this definition gives this load to an engine: one of the mod's own three (for any load it has a slot
        // for), or a declared car whose loads allow it.
        internal static bool Gives(string defId, string loadId)
        {
            if (defId == null || loadId == null) return false;
            if (Ids.All.Contains(defId)) return true;
            return _cars.TryGetValue(defId, out Entry e) && (e.Loads == null || e.Loads.Contains(loadId));
        }

        // "water" -> "rr/water"; "rr/water" or "somepack/oil" as is; blank -> null.
        internal static string LoadId(string name)
        {
            string n = name?.Trim();
            if (string.IsNullOrEmpty(n)) return null;
            return n.IndexOf('/') >= 0 ? n : "rr/" + n;
        }

        private static string Where(string pack) => FileName + " in " + (string.IsNullOrEmpty(pack) ? "a pack" : pack) + ": ";

        // One file's text, from the pack named pack. Returns its entries, not yet checked against the game's
        // definitions (Accept does that); every problem adds one line to log.
        internal static List<Entry> Parse(string text, string pack, List<string> log)
        {
            var entries = new List<Entry>();
            JObject root;
            try { root = Load(text ?? ""); }
            catch (JsonException) { log.Add(Where(pack) + "isn't a JSON object; skipped"); return entries; }
            JToken format = root["auxFuelCars"];
            if (format == null || format.Type != JTokenType.Integer)
            {
                log.Add(Where(pack) + "has no \"auxFuelCars\" format number; skipped");
                return entries;
            }
            // An integer too big for a long is held as a BigInteger, which Value<long>() can't
            // convert (InvalidCastException); read the stored value's type instead.
            if (!(((JValue)format).Value is long v))
            {
                log.Add(Where(pack) + "has a format number this version of Auxiliary Fuel Cars can't read (it reads format " + Format + "); skipped");
                return entries;
            }
            if (v > Format) { log.Add(Where(pack) + "is format " + v + "; this version of Auxiliary Fuel Cars reads format " + Format + "; skipped"); return entries; }
            if (v < 1) { log.Add(Where(pack) + "has format " + v + ", which isn't one; skipped"); return entries; }
            if (!(root["cars"] is JArray cars))
            {
                log.Add(Where(pack) + "has no \"cars\" list; skipped");
                return entries;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int k = 0; k < cars.Count; k++)
            {
                string at = "car " + (k + 1);
                if (!(cars[k] is JObject car)) { log.Add(Where(pack) + at + " isn't an object; skipped"); continue; }
                JToken idTok = car["id"];
                string id = idTok != null && idTok.Type == JTokenType.String ? ((string)idTok)?.Trim() : null;
                if (string.IsNullOrEmpty(id)) { log.Add(Where(pack) + at + " has no \"id\"; skipped"); continue; }
                if (Ids.All.Contains(id)) { log.Add(Where(pack) + id + " is one of this mod's own cars; skipped"); continue; }
                if (!seen.Add(id)) { log.Add(Where(pack) + id + " is listed twice; the second skipped"); continue; }
                HashSet<string> loads = null;
                JToken lt = car["loads"];
                if (lt != null && lt.Type != JTokenType.Null)
                {
                    bool bad = !(lt is JArray);
                    loads = new HashSet<string>(StringComparer.Ordinal);
                    if (!bad)
                        foreach (JToken l in (JArray)lt)
                        {
                            string lid = l.Type == JTokenType.String ? LoadId((string)l) : null;
                            if (lid == null) { bad = true; break; }
                            loads.Add(lid);
                        }
                    if (bad || loads.Count == 0) { log.Add(Where(pack) + id + ": \"loads\" isn't a list of load names; skipped"); continue; }
                }
                entries.Add(new Entry { Id = id, Pack = pack, Loads = loads });
            }
            return entries;
        }

        // JObject.Parse as the game's Newtonsoft runs it (JObject.Load, then the rest of the text read so trailing content
        // throws), with dates left as text (by default an id such as "2026-11-07T10:00:00" became a Date
        // token and read as no id).
        private static JObject Load(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            {
                JObject root = JObject.Load(reader);
                while (reader.Read()) { }
                return root;
            }
        }

        // The definition lookup for one id: its slots' load ids (null: no such definition) and whether it is an engine.
        internal delegate IList<string> Lookup(string id, out bool isEngine);

        // One pack's file, start to end: parsed, each car checked against the game (lookup) and
        // accepted into into. Anything that throws costs this pack's file one line and leaves into as it was; the other
        // packs still load.
        internal static void ReadPack(string text, string pack, Lookup lookup, Dictionary<string, Entry> into, List<string> log)
        {
            var staged = new Dictionary<string, Entry>(into, StringComparer.Ordinal);
            var lines = new List<string>();
            try
            {
                foreach (Entry d in Parse(text, pack, lines))
                {
                    IList<string> slots = lookup(d.Id, out bool engine);
                    Accept(d, slots, engine, staged, lines);
                }
            }
            catch (Exception x)
            {
                // The exception's type name only: a message can carry a path.
                log.Add(Where(pack) + "couldn't be read (" + x.GetType().Name + "); skipped");
                return;
            }
            log.AddRange(lines);
            into.Clear();
            foreach (KeyValuePair<string, Entry> kv in staged) into[kv.Key] = kv.Value;
        }

        // One parsed entry against the game: slotLoads is the load id of each of its definition's slots (null: no such
        // definition in this world), isEngine whether that definition is a locomotive. Accepted entries go into into,
        // first pack first; every problem adds one line to log.
        internal static void Accept(Entry d, IList<string> slotLoads, bool isEngine, Dictionary<string, Entry> into, List<string> log)
        {
            if (d == null) return;
            if (slotLoads == null) { log.Add(Where(d.Pack) + d.Id + " isn't defined in this world; skipped"); return; }
            if (isEngine) { log.Add(Where(d.Pack) + d.Id + " is an engine, not a car; skipped"); return; }
            if (into.TryGetValue(d.Id, out Entry first)) { log.Add(Where(d.Pack) + d.Id + " is already declared by " + first.Pack + "; skipped"); return; }
            var have = new HashSet<string>(StringComparer.Ordinal);
            foreach (string s in slotLoads) if (!string.IsNullOrEmpty(s)) have.Add(s);
            HashSet<string> loads = null;
            if (d.Loads != null)
            {
                loads = new HashSet<string>(StringComparer.Ordinal);
                var missing = new List<string>();
                foreach (string l in d.Loads) if (have.Contains(l)) loads.Add(l); else missing.Add(l);
                missing.Sort(StringComparer.Ordinal);
                if (missing.Count > 0) log.Add(Where(d.Pack) + d.Id + " has no slot for " + string.Join(", ", missing) + (loads.Count > 0 ? "; it gives the rest" : "; skipped"));
                if (loads.Count == 0) return;
            }
            else if (have.Count == 0) { log.Add(Where(d.Pack) + d.Id + " has no load slots; skipped"); return; }
            into[d.Id] = new Entry { Id = d.Id, Pack = d.Pack, Loads = loads };
        }
    }
}
