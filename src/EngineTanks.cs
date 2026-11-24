using System;
using System.Collections.Generic;
using System.IO;
using KeyValue.Runtime;
using Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuxFuelCars
{
    // 2.3.0, engine tanks: a tank another pack keeps on a locomotive's own KeyValue object (for example a diesel's steam
    // generator water), declared in the same aux-fuel-cars.json under "engineTanks":
    //   "engineTanks": [ { "load": "water", "levelKey": "_pack.gen-gal", "capacityKey": "_pack.gen-cap" } ]
    // load: the load it takes (a name without a slash means "rr/<name>"); it must have a refill rate (water, coal or diesel-fuel).
    // capacityKey, levelKey: keys on the engine's KeyValueObject, both plain numbers in the load's units (gallons for water).
    //   capacity 0 or absent: the engine has no tank, so it is skipped. Level absent: the tank is full, so nothing is drawn and
    //   nothing is written. The host raises the level, never above the capacity; clients read the host's value.
    // The format number stays 1, and a 2.2.x or 2.3.0 reader ignores the key. Data only: a bad file or entry costs one Player.log line and is skipped.
    internal static class OpenEngineTanks
    {
        internal sealed class Entry
        {
            internal string Pack;
            internal string Load;
            internal string LevelKey;
            internal string CapacityKey;
            // { Load }, made once: TopUp.Sources reads it without changing it.
            internal string[] Loads;
        }

        private static readonly Entry[] None = new Entry[0];
        private static volatile Entry[] _all = None;

        // The declared tanks, in the order the packs loaded (an array, so the engines' once-a-second pass indexes it).
        internal static Entry[] All => _all;

        internal static int Count => _all.Length;

        // Puts the tanks in force and the engines' hook with them (the hook is nothing while none is declared).
        internal static void Set(List<Entry> tanks)
        {
            _all = tanks == null || tanks.Count == 0 ? None : tanks.ToArray();
            TopUp.EngineTankHook = _all.Length == 0 ? null : EngineTankTopUp.Hook;
        }

        internal static void Clear() => Set(null);

        private static string Where(string pack) => OpenCars.FileName + " in " + (string.IsNullOrEmpty(pack) ? "a pack" : pack) + ": ";

        // One file's tanks, checked as far as the text goes; lines for what is wrong with an entry. Nothing, and no line, for a file
        // OpenCars.Parse already reported or that has no "engineTanks".
        internal static List<Entry> Parse(string text, string pack, List<string> log)
        {
            var entries = new List<Entry>();
            JObject root;
            try { root = Load(text ?? ""); }
            catch (JsonException) { return entries; }
            JToken format = root["auxFuelCars"];
            if (format == null || format.Type != JTokenType.Integer || !(((JValue)format).Value is long v) || v < 1 || v > OpenCars.Format) return entries;
            JToken list = root["engineTanks"];
            if (list == null || list.Type == JTokenType.Null) return entries;
            if (!(list is JArray tanks)) { log.Add(Where(pack) + "\"engineTanks\" isn't a list; skipped"); return entries; }
            for (int k = 0; k < tanks.Count; k++)
            {
                string at = "engine tank " + (k + 1);
                if (!(tanks[k] is JObject t)) { log.Add(Where(pack) + at + " isn't an object; skipped"); continue; }
                string load = t["load"] != null && t["load"].Type == JTokenType.String ? OpenCars.LoadId((string)t["load"]) : null;
                if (load == null) { log.Add(Where(pack) + at + " has no \"load\" name; skipped"); continue; }
                if (!(Logic.RatePerSecond(load) > 0f)) { log.Add(Where(pack) + at + ": " + load + " has no refill rate; skipped"); continue; }
                string level = Key(t["levelKey"]), cap = Key(t["capacityKey"]);
                if (level == null || cap == null) { log.Add(Where(pack) + at + " needs a \"levelKey\" and a \"capacityKey\"; skipped"); continue; }
                if (!SaneKey(level) || !SaneKey(cap)) { log.Add(Where(pack) + at + ": a key may only hold letters, digits, . _ - and be at most " + MaxKey + " long; skipped"); continue; }
                if (level == cap) { log.Add(Where(pack) + at + ": the level and the capacity are the same key; skipped"); continue; }
                entries.Add(new Entry { Pack = pack, Load = load, LevelKey = level, CapacityKey = cap, Loads = new[] { load } });
            }
            return entries;
        }

        internal const int MaxKey = 64;

        // A key is written to the engine's own data by the host once a second, so it is a plain name: letters, digits, dot, underscore, hyphen.
        internal static bool SaneKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKey) return false;
            foreach (char c in key)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-')) return false;
            return true;
        }

        private static string Key(JToken t)
        {
            string s = t != null && t.Type == JTokenType.String ? ((string)t).Trim() : null;
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static JObject Load(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            {
                JObject root = JObject.Load(reader);
                while (reader.Read()) { }
                return root;
            }
        }

        // One pack's file into into: an entry whose level key another entry already uses is skipped (two tanks can't share a level).
        internal static void ReadPack(string text, string pack, List<Entry> into, List<string> log)
        {
            var lines = new List<string>();
            List<Entry> parsed;
            try { parsed = Parse(text, pack, lines); }
            catch (Exception x)
            {
                log.Add(Where(pack) + "engine tanks couldn't be read (" + x.GetType().Name + "); skipped");
                return;
            }
            foreach (Entry tk in parsed)
            {
                bool dup = false;
                foreach (Entry have in into) if (have.LevelKey == tk.LevelKey || have.CapacityKey == tk.LevelKey || have.LevelKey == tk.CapacityKey) dup = true;
                if (dup) { lines.Add(Where(pack) + "the key " + tk.LevelKey + " is already used by another engine tank; skipped"); continue; }
                into.Add(tk);
            }
            log.AddRange(lines);
        }
    }

    // The host's once-a-second pass over one diesel (called from DieselTopUpPatch's postfix, host only): for each declared tank the
    // engine has (a capacity above 0 and a level key present), draw the load from the aux cars in the engine's chain, the walk of its
    // own fuel (both ends, farthest first, the Lash-up feed's passing over, Feed boxes, Faster Fueling's multiple), and raise the level key
    // by what was drawn, capped at the capacity. Written only as a real raise.
    internal static class EngineTankTopUp
    {
        internal static readonly Action<Model.DieselLocomotive, float, bool> Hook = Run;
        private static readonly Car.LogicalEnd[] Both = { Car.LogicalEnd.A, Car.LogicalEnd.B };

        private static bool Number(Value v) => v.Type == KeyValue.Runtime.ValueType.Float || v.Type == KeyValue.Runtime.ValueType.Int;

        internal static void Run(Model.DieselLocomotive loco, float dt, bool lashUp)
        {
            OpenEngineTanks.Entry[] tanks = OpenEngineTanks.All;
            if (tanks.Length == 0 || loco == null || loco.set == null || loco.IsInBardo || loco.id == null) return;
            KeyValueObject kv = loco.KeyValueObject;
            if (kv == null) return;
            for (int i = 0; i < tanks.Length; i++)
            {
                OpenEngineTanks.Entry e = tanks[i];
                Value capV = kv[e.CapacityKey];
                if (!Number(capV)) continue;
                float cap = capV.FloatValue;
                if (!(cap > 0f)) continue;
                FeedLog.TankRan(loco.id);
                Value levelV = kv[e.LevelKey];
                if (!Number(levelV)) continue;   // absent: the tank is full
                float level = levelV.FloatValue;
                float deficit = cap - level;
                if (!(deficit >= Logic.MinMove)) continue;
                // The same rule as the engine's own top-up, without its list: no aux car in reach, no walk.
                if (!AuxAhead(loco, e.Loads, lashUp)) continue;
                List<Car> sources = TopUp.Sources(loco, null, Both, e.Loads, lashUp);
                if (sources == null || sources.Count == 0) continue;
                foreach (Car c in sources) FeedLog.Chain(c.id, loco.id);
                float got = TopUp.DrawFrom(sources, e.Load, deficit, dt);
                if (!(got > 0f)) continue;
                float now = Math.Min(cap, level + got);
                if (!(now > level)) continue;
                kv[e.LevelKey] = Value.Float(now);
                FeedLog.TankDrew(loco.id, e.Load, now - level);
            }
        }

        private static bool AuxAhead(Car car, string[] loads, bool lashUp)
        {
            for (int n = 0; n < Both.Length; n++)
                if (TopUp.AuxAhead(car, car.CoupledTo(Both[n]), loads, lashUp)) return true;
            return false;
        }
    }
}
