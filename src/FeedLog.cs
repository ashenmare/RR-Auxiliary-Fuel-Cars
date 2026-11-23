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
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Rec> Aux = new Dictionary<string, Rec>();

        internal static void Clear() { lock (Gate) Aux.Clear(); }

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
                List<KeyValuePair<string, long>> seen = Of(auxId).Seen;
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
            lock (Gate) Of(auxId).Gave = now;
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
