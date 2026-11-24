using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AuxFuelCars
{
    // 2.0.8, the feed: what the host's top-up did, kept as it happens (TopUp.Run, once a second per engine, on the host
    // only), so the feed reports what the mod really did instead of walking the trains again. Per aux car: the engines
    // whose top-up walked through it and when, and when it last gave. 2.1.0: any number of engines (a lash-up of three
    // diesel units shares one aux car; 2.0.8 to 2.0.10 kept two), each dropped once it is no longer current.
    // BCL only. Cleared at every world load and unload.
    internal static class FeedLog
    {
        // A record older than this is no longer current: the top-up runs once a second, so this is two missed seconds
        // and a half (a paused game, a car uncoupled, the engine gone).
        internal const double FreshSeconds = 2.5;

        // A guard on one car's list, not a design limit (the walk stops at Logic.MaxChain cars).
        private const int MaxEngines = 16;

        private sealed class Rec
        {
            internal readonly List<KeyValuePair<string, long>> Seen = new List<KeyValuePair<string, long>>();
            internal long Gave;
            // 2.3.0: the same for the takers (non-engine cars) its walk reached, kept apart so "feeds" and "feeding" stay the engines'.
            internal readonly List<KeyValuePair<string, long>> SeenTakers = new List<KeyValuePair<string, long>>();
            internal long GaveTaker;
        }

        // 2.3.0: what one taker has drawn: per load id, the total since this world loaded, and when it last drew.
        private sealed class TakerRec
        {
            internal readonly Dictionary<string, float> Drew = new Dictionary<string, float>(StringComparer.Ordinal);
            internal long Last;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Rec> Aux = new Dictionary<string, Rec>();

        private static readonly Dictionary<string, TakerRec> Takers = new Dictionary<string, TakerRec>(StringComparer.Ordinal);
        // The taker whose top-up is running (TakerTopUp sets it around TopUp.Run, on the main thread); null for an engine's.
        private static string _taker;

        // 2.3.0: what each engine has drawn into a tank kept on its keys (OpenEngineTanks), by engine car id.
        private static readonly Dictionary<string, TakerRec> Tanks = new Dictionary<string, TakerRec>(StringComparer.Ordinal);

        internal static void Clear() { lock (Gate) { Aux.Clear(); Takers.Clear(); Tanks.Clear(); _taker = null; } }

        // This engine has a tank on its keys (known to the feed from here on).
        internal static void TankRan(string engineId)
        {
            if (engineId == null) return;
            lock (Gate) if (!Tanks.ContainsKey(engineId)) Tanks[engineId] = new TakerRec();
        }

        // True when TankRan or TankDrew has seen this engine (a lookup, no allocation).
        internal static bool TankKnown(string engineId)
        {
            if (engineId == null) return false;
            lock (Gate) return Tanks.ContainsKey(engineId);
        }

        internal static void TankDrew(string engineId, string loadId, float qty)
        {
            if (engineId == null || loadId == null || !(qty > 0f)) return;
            long now = Now;
            lock (Gate)
            {
                if (!Tanks.TryGetValue(engineId, out TakerRec r)) Tanks[engineId] = r = new TakerRec();
                r.Drew.TryGetValue(loadId, out float have);
                r.Drew[loadId] = have + qty;
                r.Last = now;
            }
        }

        // The totals drawn into the engine's tank by load id (sorted) and whether it drew within the window. false: no tank seen on it.
        internal static bool TankDrawn(string engineId, List<KeyValuePair<string, float>> into, out bool drawing)
        {
            drawing = false;
            if (engineId == null) return false;
            long now = Now;
            lock (Gate)
            {
                if (!Tanks.TryGetValue(engineId, out TakerRec r)) return false;
                foreach (KeyValuePair<string, float> kv in r.Drew) into.Add(kv);
                drawing = Fresh(r.Last, now);
            }
            into.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return true;
        }

        // 2.3.0: TopUp.Run records into the taker lists, not the engines', between these two.
        internal static void BeginTaker(string takerId) { _taker = takerId; }
        internal static void EndTaker() { _taker = null; }

        // This taker's top-up ran (it is known to the feed from here on).
        internal static void TakerRan(string takerId)
        {
            if (takerId == null) return;
            lock (Gate) if (!Takers.ContainsKey(takerId)) Takers[takerId] = new TakerRec();
        }

        // The taker drew qty of loadId at this top-up.
        internal static void TakerDrew(string takerId, string loadId, float qty)
        {
            if (takerId == null || loadId == null || !(qty > 0f)) return;
            long now = Now;
            lock (Gate)
            {
                if (!Takers.TryGetValue(takerId, out TakerRec r)) Takers[takerId] = r = new TakerRec();
                r.Drew.TryGetValue(loadId, out float have);
                r.Drew[loadId] = have + qty;
                r.Last = now;
            }
        }

        internal static bool IsTaker(string id)
        {
            if (id == null) return false;
            lock (Gate) return Takers.ContainsKey(id);
        }

        // The totals drawn so far by load id (sorted by id) and whether it drew within the window. false: not a taker seen.
        internal static bool TakerDrawn(string takerId, List<KeyValuePair<string, float>> into, out bool drawing)
        {
            drawing = false;
            if (takerId == null) return false;
            long now = Now;
            lock (Gate)
            {
                if (!Takers.TryGetValue(takerId, out TakerRec r)) return false;
                foreach (KeyValuePair<string, float> kv in r.Drew) into.Add(kv);
                drawing = Fresh(r.Last, now);
            }
            into.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return true;
        }

        private static long Now => Stopwatch.GetTimestamp();
        private static bool Fresh(long stamp, long now) => stamp != 0 && (now - stamp) <= (long)(FreshSeconds * Stopwatch.Frequency);

        private static Rec Of(string auxId)
        {
            if (!Aux.TryGetValue(auxId, out Rec r)) Aux[auxId] = r = new Rec();
            return r;
        }

        // The top-up of locoId walked through this aux car.
        internal static void Chain(string auxId, string locoId)
        {
            if (auxId == null || locoId == null) return;
            long now = Now;
            lock (Gate)
            {
                List<KeyValuePair<string, long>> seen = _taker != null ? Of(auxId).SeenTakers : Of(auxId).Seen;
                seen.RemoveAll(p => p.Key == locoId || !Fresh(p.Value, now));
                if (seen.Count >= MaxEngines) seen.RemoveAt(0);
                seen.Add(new KeyValuePair<string, long>(locoId, now));
            }
        }

        // This aux car gave something at this top-up.
        internal static void Gave(string auxId)
        {
            if (auxId == null) return;
            long now = Now;
            lock (Gate)
            {
                if (_taker != null) Of(auxId).GaveTaker = now; else Of(auxId).Gave = now;
            }
        }

        // 2.3.0: the takers it feeds now (current records only, in id order) and whether it gave to one within the window.
        internal static List<object> FeedsTakers(string auxId, out bool feeding)
        {
            var ids = new List<string>();
            feeding = false;
            if (auxId != null)
            {
                long now = Now;
                lock (Gate)
                {
                    if (Aux.TryGetValue(auxId, out Rec r))
                    {
                        foreach (KeyValuePair<string, long> p in r.SeenTakers)
                            if (Fresh(p.Value, now) && !ids.Contains(p.Key)) ids.Add(p.Key);
                        feeding = Fresh(r.GaveTaker, now);
                    }
                }
            }
            ids.Sort(string.CompareOrdinal);
            return new List<object>(ids);
        }

        // The engines it feeds now (current records only, in id order) and whether it gave within the window.
        internal static List<object> Feeds(string auxId, out bool feeding)
        {
            var ids = new List<string>();
            feeding = false;
            if (auxId != null)
            {
                long now = Now;
                lock (Gate)
                {
                    if (Aux.TryGetValue(auxId, out Rec r))
                    {
                        foreach (KeyValuePair<string, long> p in r.Seen)
                            if (Fresh(p.Value, now) && !ids.Contains(p.Key)) ids.Add(p.Key);
                        feeding = Fresh(r.Gave, now);
                    }
                }
            }
            ids.Sort(string.CompareOrdinal);
            return new List<object>(ids);
        }
    }
}
