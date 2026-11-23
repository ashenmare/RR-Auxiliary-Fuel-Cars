using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AuxFuelCars
{
    // 2.0.8: the house feed (release/FEEDS.md, shipped in the zip). 2.0.9: listed with Mod Feeds too (ModFeeds.cs,
    // Main.PublishFeed), so other mods find it with ModFeeds.List and read it with ModFeeds.Read; a mod without it can
    // still read it by reflection. Neither depends on the other.
    //  - Version (const int): the schema version. Within a version keys are only added; a rename, retype or removal
    //    raises it.
    //  - Feed (const string): the stable feed id, "aux-fuel-cars".
    //  - Read(): the aux cars and the Remote MU helpers in this world, plain BCL data, a new dictionary on every call.
    //    Never throws: on a failure it answers { "api": 1, "feed": "aux-fuel-cars", "error": "..." }. Read only.
    //
    // Read names only BCL types, PathClean (BCL only) and this class. Everything that touches the game is in
    // FeedReader, whose class holds game-typed members; on Mono naming it would load it when Read is compiled, and a
    // failure there would throw at Read's caller, before Read's own try. So Read reaches it only through ReadFeed, a
    // NoInlining method called inside Read's try (the house class-load rule), and logs through LogOnce the same way.
    public static class Api
    {
        public const int Version = 1;
        public const string Feed = "aux-fuel-cars";

        // The thread that loaded the mod (Unity's main thread); -1 when the mod isn't loaded. Main sets it.
        internal static volatile int MainThreadId = -1;

        // One log line per world; reset at every world load (statics survive a mod-set change).
        private static bool _logged;

        // Built at load, so the answer when even a new error dictionary can't be built is still a dictionary.
        private static readonly Dictionary<string, object> Fallback =
            new Dictionary<string, object> { { "api", Version }, { "feed", Feed }, { "error", "the feed failed" } };

        internal static void Reset() => _logged = false;

        public static object Read()
        {
            try
            {
                int main = MainThreadId;
                if (main == -1) return Error(Main.Name + " isn't loaded");
                if (Thread.CurrentThread.ManagedThreadId != main) return Error("call it on the game's main thread");
                return ReadFeed();
            }
            catch (Exception e)
            {
                string why;
                try { why = PathClean.CleanText(e.GetType().Name + ": " + e.Message); }
                catch (Exception) { why = "the feed failed"; }
                if (!_logged)
                {
                    _logged = true;
                    try { LogOnce(why); } catch (Exception) { }
                }
                return Error(why);
            }
        }

        private static object Error(string why)
        {
            try { return new Dictionary<string, object> { { "api", Version }, { "feed", Feed }, { "error", why } }; }
            catch (Exception) { return Fallback; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object ReadFeed() => FeedReader.Read();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LogOnce(string why) => Util.Warn("the feed couldn't answer: " + why);
    }
}
