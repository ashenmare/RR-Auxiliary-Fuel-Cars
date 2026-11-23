using System;
using System.Diagnostics;

namespace AuxFuelCars
{
    // When Faster Fueling is installed, the top-up moves fuel at its refuel speeds. Read through Mod
    // Feeds by id ("faster-fueling"), key by key, at any api: the answer's `active` and the three
    // speeds in its `settings`. Never Faster Fueling's own types. No feed, an error answer, `active` false or missing, or
    // a speed that isn't a number above 0: that load runs at the game's own speed, as before this version.
    //
    // The read is kept for CacheSeconds (the top-up asks for every engine every second); the cache and the log latches
    // reset at every world load (Reset). BCL and ModFeeds only, so tools/gamecheck runs it as it is. Main thread.
    internal static class FasterFueling
    {
        internal const string FeedId = "faster-fueling";
        // The api this reader was written for; a key the feed lists as changed above it is skipped (ModFeeds.TryGet).
        internal const int KnownApi = 1;
        internal const double CacheSeconds = 3.0;

        // Where the answer comes from and what time it is; the gate swaps both.
        internal static Func<object> Source = () => ModFeeds.Read(FeedId);
        internal static Func<long> Now = Stopwatch.GetTimestamp;

        private static long _stamp;          // when the cache was filled; 0: never (or reset)
        private static bool _following;
        private static float _coal = 1f, _water = 1f, _diesel = 1f;

        // The loads Faster Fueling has a speed for (the keys of its settings, and the load ids they are for).
        private const string CoalKey = "coalSpeed", WaterKey = "waterSpeed", DieselKey = "dieselSpeed";

        // Every world load: forget the read and let the log lines be said again.
        internal static void Reset()
        {
            _stamp = 0;
            _following = false;
            _coal = _water = _diesel = 1f;
            Util.Unwarn("ff-active");
            Util.Unwarn("ff-settings");
            Util.Unwarn(CoalKey);
            Util.Unwarn(WaterKey);
            Util.Unwarn(DieselKey);
        }

        // The multiple of this mod's per-load rate for this load: Faster Fueling's speed for it while its feed says it is
        // active, else 1. Any load but coal, water and diesel is 1.
        internal static float Multiplier(string loadId)
        {
            Refresh();
            if (!_following) return 1f;
            switch (loadId)
            {
                case Logic.Coal: return _coal;
                case Logic.Water: return _water;
                case Logic.Diesel: return _diesel;
                default: return 1f;
            }
        }

        // What applies now, for the feed: whether Faster Fueling's speeds are followed, and the multiple per load.
        internal static void Current(out bool following, out float coal, out float water, out float diesel)
        {
            Refresh();
            following = _following;
            coal = _following ? _coal : 1f;
            water = _following ? _water : 1f;
            diesel = _following ? _diesel : 1f;
        }

        private static void Refresh()
        {
            long now = Now();
            if (_stamp != 0 && (now - _stamp) >= 0 && (now - _stamp) < (long)(CacheSeconds * Stopwatch.Frequency)) return;
            _stamp = now == 0 ? 1 : now;
            try { Apply(Source()); }
            catch (Exception) { Apply(null); }
        }

        // Fill the cache from one answer (internal for the gate).
        internal static void Apply(object answer)
        {
            _following = false;
            _coal = _water = _diesel = 1f;
            // Not loaded (or not a feed answer at all): the normal case without the mod, nothing to say.
            if (!(answer is System.Collections.Generic.IDictionary<string, object> d) || d.ContainsKey("error")) return;
            if (!ModFeeds.TryGetBool(answer, "active", KnownApi, out bool active))
            {
                Util.WarnOnce("ff-active", "Faster Fueling's feed has no readable \"active\"; refilling at the game's speeds");
                return;
            }
            if (!active) return;
            if (!ModFeeds.TryGet(answer, "settings", KnownApi, out object settings))
            {
                Util.WarnOnce("ff-settings", "Faster Fueling's feed has no readable \"settings\"; refilling at the game's speeds");
                return;
            }
            _following = true;
            _coal = Speed(settings, CoalKey);
            _water = Speed(settings, WaterKey);
            _diesel = Speed(settings, DieselKey);
        }

        private static float Speed(object settings, string key)
        {
            if (ModFeeds.TryGetDouble(settings, key, KnownApi, out double v) && v > 0 && v <= float.MaxValue && (float)v > 0f) return (float)v;
            Util.WarnOnce(key, "Faster Fueling's feed has no usable " + key + "; that load refills at the game's speed");
            return 1f;
        }
    }
}
