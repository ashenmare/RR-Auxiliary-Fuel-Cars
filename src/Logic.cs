using System;
using System.Collections.Generic;

namespace AuxFuelCars
{
    // "Helper engines work the air" as three choices.
    // Off: never (the game's cut-out engine). By hand only: 2.0.9's rule, the helper works the air while the lead is
    // driven by hand and stands aside (the game's cut-out valves; the Auto Engineer's copy as the game makes it)
    // whenever the Auto Engineer drives the lead. Always: 2.1.1's rule, under the Auto Engineer too. The numbers are
    // the dropdown's order (HelperAirSetting.Options) and the world key's values (HostSetting).
    internal enum HelperAir { Off = 0, ByHand = 1, Always = 2 }

    // The rules, with no game types in them, so tools/probe/logic.cs compiles this file on its own and tests it under
    // mono. The game-side code (TopUp.cs, RemoteMu.cs) gathers cars and quantities, calls these, and writes the result.
    internal static class Logic
    {
        // The game's own refill rates, per second (observed prefab values of the 0.6.2 loaders: water tower and column
        // 40 gal, coaling tower 127 lb, diesel stand 3 gal; each loader tick is 1 s). Every top-up is capped here.
        // No engine burns this fast (README: "Top-up rate"), so while running the cap never binds and the tender
        // stays full; it binds only on a big deficit, such as a low tender coupled to a full aux car standing as a
        // remote water tower, which then fills at the tower's own speed.
        internal const float WaterPerSecond = 40f;
        internal const float CoalPerSecond = 127f;
        internal const float DieselPerSecond = 3f;

        // Below this a move is not written. The game replicates every load write as its own property change.
        internal const float MinMove = 0.01f;

        // A chain longer than this is cut off (a guard, not a design limit: no real train comes close).
        internal const int MaxChain = 64;

        internal const string Water = "rr/water";
        internal const string Coal = "rr/coal";
        internal const string Diesel = "rr/diesel-fuel";

        // Tower rate for a load, or 0 for a load no aux car carries (so nothing moves).
        internal static float RatePerSecond(string loadId)
        {
            switch (loadId)
            {
                case Water: return WaterPerSecond;
                case Coal: return CoalPerSecond;
                case Diesel: return DieselPerSecond;
                default: return 0f;
            }
        }

        // One side's chain, nearest first. first is the car coupled to the start car on this side (null: nothing
        // coupled there). The walk takes a car while isAux says so, then steps to the car coupled at that car's other
        // end (next(car, cameFrom)), and stops at the first car that isn't one, at an uncoupled end, at a car it has
        // already taken (a loop guard) or at MaxChain.
        // PassOver, when given, is a car the walk steps over without ending (a
        // directly coupled diesel unit). Such a car is in the returned walk too, so FarthestFirst orders by true
        // distance; the caller keeps only the aux cars (AuxOnly). Without passOver the walk is as before.
        internal static List<T> Walk<T>(T start, T first, Func<T, bool> isAux, Func<T, T, T> next, Func<T, bool> passOver = null) where T : class
        {
            var chain = new List<T>();
            var seen = new HashSet<T>(ReferenceComparer<T>.Instance) { start };
            T prev = start, car = first;
            while (car != null && chain.Count < MaxChain && !seen.Contains(car) && (isAux(car) || (passOver != null && passOver(car))))
            {
                chain.Add(car);
                seen.Add(car);
                T following = next(car, prev);
                prev = car;
                car = following;
            }
            return chain;
        }

        // Both sides merged farthest first: by distance from the start car, deepest first; at equal depth side A's car
        // before side B's. A car on both lists (only possible in a loop) is taken once, at its first place.
        internal static List<T> FarthestFirst<T>(List<T> sideA, List<T> sideB) where T : class
        {
            var all = new List<T>();
            var seen = new HashSet<T>(ReferenceComparer<T>.Instance);
            int depth = Math.Max(sideA?.Count ?? 0, sideB?.Count ?? 0);
            for (int d = depth - 1; d >= 0; d--)
            {
                if (sideA != null && d < sideA.Count && seen.Add(sideA[d])) all.Add(sideA[d]);
                if (sideB != null && d < sideB.Count && seen.Add(sideB[d])) all.Add(sideB[d]);
            }
            return all;
        }

        // The aux cars of a merged walk, in its order (the passed-over units dropped).
        internal static List<T> AuxOnly<T>(List<T> walk, Func<T, bool> isAux) where T : class
        {
            var aux = new List<T>(walk.Count);
            foreach (T car in walk) if (isAux(car)) aux.Add(car);
            return aux;
        }

        // How much to take from each source, in the order given (farthest first). available[i] is what source i can
        // give (0 for a car whose Feed is off or that has none). The total is min(deficit, cap, all available); it is
        // taken from the first sources first, so the farthest car empties first and can be dropped. Returns the total
        // and fills taken (same length as available); a total under MinMove takes nothing.
        internal static float Draw(float deficit, float cap, IList<float> available, float[] taken)
        {
            for (int i = 0; i < taken.Length; i++) taken[i] = 0f;
            float want = Math.Min(Pos(deficit), Pos(cap));
            float sum = 0f;
            for (int i = 0; i < available.Count; i++) sum += Pos(available[i]);
            float total = Math.Min(want, sum);
            if (!(total >= MinMove)) return 0f;
            float left = total;
            for (int i = 0; i < available.Count && left > 0f; i++)
            {
                float t = Math.Min(left, Pos(available[i]));
                taken[i] = t;
                left -= t;
            }
            // Float rounding can leave a sliver; the total reported is what was actually taken.
            float got = 0f;
            for (int i = 0; i < taken.Length; i++) got += taken[i];
            return got;
        }

        private static float Pos(float v) => v > 0f && !float.IsNaN(v) ? (float.IsPositiveInfinity(v) ? float.MaxValue : v) : 0f;

        // Remote MU.
        // What a car is to a locomotive looking for its MU source (BaseLocomotive.FindSourceLocomotive's own tests).
        internal enum MuCar { Self, Tender, CutInLoco, CutOutLoco, Other }

        // Vanilla's rule over one walk (cars in walk order, already bounded by coupled and air-connected joints): skip
        // itself and tenders, return the first loco whose air is cut in, skip cut-out locos, and give up (-1) at the
        // first car that is none of those. Kept here only so the probe can compare the two rules.
        internal static int VanillaSource(IList<MuCar> walk)
        {
            for (int i = 0; i < walk.Count; i++)
            {
                switch (walk[i])
                {
                    case MuCar.CutInLoco: return i;
                    case MuCar.Other: return -1;
                }
            }
            return -1;
        }

        // The remote rule: the same walk, but a car that is neither engine nor tender is passed over instead of ending
        // the search. Only the first cut-in loco qualifies, as in vanilla, and every helper has its air cut out, so
        // helpers can never pick each other.
        internal static int RemoteSource(IList<MuCar> walk)
        {
            for (int i = 0; i < walk.Count; i++)
                if (walk[i] == MuCar.CutInLoco) return i;
            return -1;
        }

        // The postfix's choice: vanilla's answer whenever it has one (so every consist vanilla handles is unchanged,
        // and a directly coupled engine on the R side still beats a remote one on the F side), else the remote walk
        // F side, then R side, as vanilla orders its two walks. Off: vanilla's answer only.
        internal static T Choose<T>(T vanilla, bool remoteOn, Func<T> remoteF, Func<T> remoteR) where T : class
        {
            if (vanilla != null || !remoteOn) return vanilla;
            return remoteF() ?? remoteR();
        }

        // 2.0.8, helper engines work the air (RearAir.cs). A helper's brake pipe valves are run only when all of these
        // hold at once: both settings on (the host's values), the helper MU'd with its air cut out, the game's own air
        // deferral following a lead this physics tick, that lead found by the remote walk (not vanilla's MU) and the
        // one the walk recorded, the lead's air cut in, the lead in the same air graph, and (a lead with the air
        // bottled up must not work) the brake pipe whole: every joint between the helper
        // and the lead open, and no open angle cock or loose hose anywhere on that pipe; and the
        // Auto Engineer not driving the lead (it plans braking without the helper's air). Any one false: the game's
        // own cut-out valves (feed and vent closed), which the game sets again every tick.
        // The last condition was once aeOk (the Auto Engineer not driving the lead, or its copy able to carry the
        // helper). Now a helper always works the air under the AE (it always vents; only its feed waits on
        // FeedLatch below), so the hook passed true. The parameter stays so the probe's table of every combination
        // stands as it was. rearAirOn is AirOn and aeOk is AeOk below (the setting's three choices).
        internal static bool WorksAir(bool remoteMuOn, bool rearAirOn, bool helperMu, bool helperCutOut, bool deferred,
                                      bool remoteLink, bool leadCutIn, bool leadInGraph, bool pipeWhole, bool aeOk)
            => remoteMuOn && rearAirOn && helperMu && helperCutOut && deferred && remoteLink && leadCutIn && leadInGraph
               && pipeWhole && aeOk;

        // 2.1.2: the setting lets helpers work the air at all (WorksAir's rearAirOn).
        internal static bool AirOn(HelperAir air) => air == HelperAir.ByHand || air == HelperAir.Always;

        // 2.1.2: WorksAir's aeOk. Always: under the Auto Engineer too (2.1.1). By hand only: only while the AE doesn't
        // drive the lead (2.0.9's stand-aside). Off: never.
        internal static bool AeOk(HelperAir air, bool aeDrivesLead) => air == HelperAir.Always || (air == HelperAir.ByHand && !aeDrivesLead);

        // The helper's feed under the Auto Engineer. The AE plans on a copy where a working helper is a
        // pipe joined to the lead's (RearAirSnapshotPatch), fed from the lead's main reservoir as if the helper's own never
        // ran short. Every miss between that copy and the live train must leave the live train with at least the braking
        // the copy expects. So, while the AE drives the lead:
        //  - the helper always vents toward the lead's target (venting needs no air of its own), so a live application
        //    matches or beats the copy;
        //  - the helper feeds (charges the pipe) only while latched: on once its main reservoir reaches the lead's feed
        //    valve + FeedOnPsi, off only when it falls under the feed valve + FeedOffPsi (from there it can't charge the
        //    pipe to the feed valve). 2.0.10's single line turned the whole helper on and off as the reservoir crossed
        //    it, inside the AE's look-ahead, where the copy can't follow;
        //  - the copy is wired whenever the helper feeds now or can start within the AE's look-ahead (WireCopy). A live
        //    helper that starts feeding later than that, or stops early, then releases the train slower than the copy
        //    expects. Proven only for the rollout (its 0.02 s step, 8 s) and the brake predictor's model run at 0.02 s
        //    (tools/airsim, the s1-* cases): there the train gets at least the braking the copy expects. The game runs
        //    the brake predictor at 0.1 s, where its own lead-only error is larger than the margin both ways; that step
        //    is reported, not proven.
        // Off the AE the helper feeds whenever the game's own valve logic would (no latch), as in 2.0.8 to 2.0.10.
        internal const float FeedOnPsi = 10f;
        internal const float FeedOffPsi = 0f;

        // The latch after this tick; wasOn is its state after the last one.
        internal static bool FeedLatch(bool wasOn, float helperMainReservoir, float leadFeedValve)
        {
            if (helperMainReservoir >= leadFeedValve + FeedOnPsi) return true;
            if (helperMainReservoir < leadFeedValve + FeedOffPsi) return false;
            return wasOn;
        }

        // The rollout search looks 8 s ahead (RolloutController: 2 s steps, depth 4); the AE's brake predictor looks
        // ahead by the game's propagation delay for the train's cars (BrakePressureSimulator.LookaheadTime), 22 s at 82
        // cars. Both read the same copy, so the longer one counts.
        internal const float RolloutSeconds = 8f;

        internal static float Horizon(float propagationDelay) => propagationDelay > RolloutSeconds ? propagationDelay : RolloutSeconds;

        // The copy is wired when the helper feeds after this tick (latched), or its compressor can lift its main
        // reservoir to the on-line within the horizon (compressorRate: psi per second while it runs; 0 when it can't
        // run: no fuel, or stopped above its cut-in).
        internal static bool WireCopy(bool latched, float helperMainReservoir, float leadFeedValve, float compressorRate, float horizonSeconds)
        {
            if (latched) return true;
            float rise = compressorRate > 0f && horizonSeconds > 0f ? compressorRate * horizonSeconds : 0f;
            return helperMainReservoir + rise >= leadFeedValve + FeedOnPsi;
        }

        // 2.0.9: the Auto Engineer drives an engine when its orders (the engine's "aiOrders" key) are on, as the game
        // reads them (Orders.FromPropertyValue, Orders.Enabled): anything but a
        // dictionary is off; a "mode" number other than 0 (Off) is on; with no mode, the older "enabled" flag decides.
        // The hook reads the parts itself because the game's parse also builds the waypoint (an allocation), and this
        // runs every physics tick; tools/airsim checks it against the game's own parse.
        internal static bool AeDriving(bool isDictionary, bool hasMode, int mode, bool enabled)
            => isDictionary && (hasMode ? mode != 0 : enabled);

        // The pipe target the lead's own brake valve regulates to this tick: Mathf.Min(LapTrainBrakePressure,
        // brakeFeedValvePressure) (SetAirGraph.UpdateLocomotiveValves, cut-in branch). Mathf.Min is a < b ? a : b.
        internal static float PipeTarget(float lap, float feedValve) => lap < feedValve ? lap : feedValve;

        // The feed's position of a helper: "rear" when nothing but engines and tenders is coupled beyond it on the side
        // away from its lead (or nothing at all), else "mid".
        internal static string Position(bool otherCarBeyond) => otherCarBeyond ? "mid" : "rear";

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
