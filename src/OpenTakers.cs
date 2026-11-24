using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuxFuelCars
{
    // 2.3.0, takers: a car that isn't an engine but needs fuel or water, declared by another pack in the same
    // aux-fuel-cars.json as its aux cars (OpenCars), under a "takers" list:
    //   { "auxFuelCars": 1, "cars": [], "takers": [ { "id": "ashenmare/steam-heater-car", "loads": ["diesel-fuel", "water"], "capacity": "slot" } ] }
    // The format number stays 1: a file with "takers" is still format 1, and a 2.2.x reader ignores a key it doesn't
    // know (keep "cars": [] in a file that declares no aux cars, since 2.2.x wants the list).
    // id: a car definition identifier, required, never an engine (engines are topped up as before), never one of this
    //   mod's own cars, never a car the same or another pack declares as an aux car (a car is a giver or a taker).
    // loads: optional; the loads it takes. A name without a slash means "rr/<name>". Each must have a slot on the car and
    //   a refill rate (water, coal or diesel-fuel: Logic.RatePerSecond); left out, every load it has a slot for that has one.
    // capacity: optional, "slot" (the default and the only source): how much it takes is the room in that load's slot, the
    //   game's LoadSlot.CalculateMaximumCapacity, as for an engine's tender. Any other value skips the entry.
    //
    // Data only, like OpenCars: a bad file, a bad entry or a car that isn't defined costs one Player.log line and is skipped.
    // The aux-fuel-cars.json problems OpenCars already reports (not an object, no format number, a format above 1) are not
    // reported again here. This class names only the BCL and Newtonsoft (no game type), so tools/probe could run it as it is.
    internal static class OpenTakers
    {
        internal const string CapacitySlot = "slot";

        internal sealed class Entry
        {
            internal string Id;
            internal string Pack;
            // The load ids it takes, in the order the file gave them; made once, and TopUp.Run reads it without changing it.
            internal string[] Loads;
        }

        // The declared takers in force, by definition id. Replaced whole at each world load (never changed after).
        private static volatile Dictionary<string, Entry> _takers = new Dictionary<string, Entry>(StringComparer.Ordinal);

        internal static IReadOnlyDictionary<string, Entry> Takers => _takers;

        internal static int Count => _takers.Count;

        internal static void Set(Dictionary<string, Entry> takers)
        {
            _takers = takers ?? new Dictionary<string, Entry>(StringComparer.Ordinal);
            OpenCars.SetTakerIds(new HashSet<string>(_takers.Keys, StringComparer.Ordinal));   // the lash-up walk passes over these
        }

        internal static void Clear() => Set(null);

        internal static bool TryGet(string defId, out Entry e)
        {
            e = null;
            return defId != null && _takers.TryGetValue(defId, out e);
        }

        private static string Where(string pack) => OpenCars.FileName + " in " + (string.IsNullOrEmpty(pack) ? "a pack" : pack) + ": ";

        // One file's "takers", not yet checked against the game's definitions (Accept does that). Returns nothing, with no
        // line, for a file OpenCars.Parse has already reported or that has no "takers".
        internal static List<Entry> Parse(string text, string pack, List<string> log)
        {
            var entries = new List<Entry>();
            JObject root;
            try { root = Load(text ?? ""); }
            catch (JsonException) { return entries; }
            JToken format = root["auxFuelCars"];
            if (format == null || format.Type != JTokenType.Integer || !(((JValue)format).Value is long v) || v < 1 || v > OpenCars.Format) return entries;
            JToken list = root["takers"];
            if (list == null || list.Type == JTokenType.Null) return entries;
            if (!(list is JArray takers)) { log.Add(Where(pack) + "\"takers\" isn't a list; skipped"); return entries; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int k = 0; k < takers.Count; k++)
            {
                string at = "taker " + (k + 1);
                if (!(takers[k] is JObject t)) { log.Add(Where(pack) + at + " isn't an object; skipped"); continue; }
                JToken idTok = t["id"];
                string id = idTok != null && idTok.Type == JTokenType.String ? ((string)idTok)?.Trim() : null;
                if (string.IsNullOrEmpty(id)) { log.Add(Where(pack) + at + " has no \"id\"; skipped"); continue; }
                if (Ids.All.Contains(id)) { log.Add(Where(pack) + id + " is one of this mod's own cars; skipped"); continue; }
                if (!seen.Add(id)) { log.Add(Where(pack) + id + " is listed twice as a taker; the second skipped"); continue; }
                JToken cap = t["capacity"];
                if (cap != null && cap.Type != JTokenType.Null && !(cap.Type == JTokenType.String && (string)cap == CapacitySlot))
                {
                    log.Add(Where(pack) + id + ": \"capacity\" must be \"" + CapacitySlot + "\"; skipped");
                    continue;
                }
                string[] loads = null;
                JToken lt = t["loads"];
                if (lt != null && lt.Type != JTokenType.Null)
                {
                    bool bad = !(lt is JArray);
                    var ids = new List<string>();
                    if (!bad)
                        foreach (JToken l in (JArray)lt)
                        {
                            string lid = l.Type == JTokenType.String ? OpenCars.LoadId((string)l) : null;
                            if (lid == null) { bad = true; break; }
                            if (!ids.Contains(lid)) ids.Add(lid);
                        }
                    if (bad || ids.Count == 0) { log.Add(Where(pack) + id + ": \"loads\" isn't a list of load names; skipped"); continue; }
                    loads = ids.ToArray();
                }
                entries.Add(new Entry { Id = id, Pack = pack, Loads = loads });
            }
            return entries;
        }

        // Same reader as OpenCars (dates left as text, trailing content throws).
        private static JObject Load(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            {
                JObject root = JObject.Load(reader);
                while (reader.Read()) { }
                return root;
            }
        }

        // One pack's file: its takers parsed, each checked against the game (lookup, the one OpenCars uses) and accepted
        // into into. Anything that throws costs this file one line and leaves into as it was. aux: the aux cars in force
        // (a car already declared there can't be a taker).
        internal static void ReadPack(string text, string pack, OpenCars.Lookup lookup, IReadOnlyDictionary<string, OpenCars.Entry> aux,
                                      Dictionary<string, Entry> into, List<string> log)
        {
            var staged = new Dictionary<string, Entry>(into, StringComparer.Ordinal);
            var lines = new List<string>();
            try
            {
                foreach (Entry d in Parse(text, pack, lines))
                {
                    IList<string> slots = lookup(d.Id, out bool engine);
                    Accept(d, slots, engine, aux != null && aux.ContainsKey(d.Id), staged, lines);
                }
            }
            catch (Exception x)
            {
                log.Add(Where(pack) + "takers couldn't be read (" + x.GetType().Name + "); skipped");
                return;
            }
            log.AddRange(lines);
            into.Clear();
            foreach (KeyValuePair<string, Entry> kv in staged) into[kv.Key] = kv.Value;
        }

        // One parsed entry against the game: slotLoads is the load id of each slot of its definition (null: not defined in
        // this world), isEngine whether that definition is a locomotive, isAux whether the car is declared as an aux car.
        internal static void Accept(Entry d, IList<string> slotLoads, bool isEngine, bool isAux, Dictionary<string, Entry> into, List<string> log)
        {
            if (d == null) return;
            if (slotLoads == null) { log.Add(Where(d.Pack) + d.Id + " isn't defined in this world; skipped"); return; }
            if (isEngine) { log.Add(Where(d.Pack) + d.Id + " is an engine, not a car; skipped"); return; }
            if (isAux) { log.Add(Where(d.Pack) + d.Id + " is declared as an aux car too; a car gives or takes, skipped as a taker"); return; }
            if (into.TryGetValue(d.Id, out Entry first)) { log.Add(Where(d.Pack) + d.Id + " is already declared as a taker by " + first.Pack + "; skipped"); return; }
            var have = new HashSet<string>(StringComparer.Ordinal);
            foreach (string s in slotLoads) if (!string.IsNullOrEmpty(s)) have.Add(s);
            var keep = new List<string>();
            if (d.Loads != null)
            {
                var missing = new List<string>();
                foreach (string l in d.Loads)
                {
                    if (!have.Contains(l)) missing.Add(l + " (no slot)");
                    else if (!(Logic.RatePerSecond(l) > 0f)) missing.Add(l + " (no refill rate)");
                    else keep.Add(l);
                }
                missing.Sort(StringComparer.Ordinal);
                if (missing.Count > 0) log.Add(Where(d.Pack) + d.Id + " can't take " + string.Join(", ", missing) + (keep.Count > 0 ? "; it takes the rest" : "; skipped"));
            }
            else
            {
                // Left out: every slot's load that has a refill rate, in slot order.
                foreach (string s in slotLoads)
                    if (!string.IsNullOrEmpty(s) && Logic.RatePerSecond(s) > 0f && !keep.Contains(s)) keep.Add(s);
                if (keep.Count == 0) log.Add(Where(d.Pack) + d.Id + " has no slot for water, coal or diesel-fuel; skipped");
            }
            if (keep.Count == 0) return;
            into[d.Id] = new Entry { Id = d.Id, Pack = d.Pack, Loads = keep.ToArray() };
        }
    }
}
