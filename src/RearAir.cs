using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Physics;
using Model.Physics.Air;

namespace AuxFuelCars
{
    // Helper engines work the air. A Remote MU helper stays cut out (the MU throttle copy and the
    // air deferral both need it cut out, LocomotiveAirSystem._ShouldDeferToLocomotiveAir), and its brake cylinder keeps
    // following the lead's control line as the game already does. What changes: its brake pipe feed and vent valves
    // are run the way the lead's own brake valve runs its, toward the lead's pipe target, so the rear charges the
    // pipe and applies and releases from its end too.
    //
    // How the game does it (0.6.2, SetAirGraph.cs): every physics tick, UpdateTopology walks the train's cars in index
    // order, sets each car's two end connections (:313-347: element i's B end reaches element i+1's brake pipe when the
    // hoses are on, valve = the smaller of the two angle cocks, 0 under 0.01; an end with no hose vents to air through
    // its own cock), and for each engine runs UpdateLocomotiveValves: UpdateCachedShouldDeferToLocomotiveAir (which asks
    // FindMuSourceLocomotive, so RemoteMuPatch answers here), UpdateControlValues (the lap ratchet), then
    //   cut in:  SetLocomotiveValves(target = Min(LapTrainBrakePressure, brakeFeedValvePressure), control line), and
    //            CarInfo.brakeLineTarget = target (so ClampLocomotivePressures holds that end of the pipe at it);
    //   cut out: pipe feed (+2) and vent (+3) closed, brakeLineTarget -1, the cylinder filled or vented toward the
    //            deferral source's control line (CarInfo.brakeCylinderTarget).
    // Then the air job runs, then ClampLocomotivePressures.
    //
    // The hook: a postfix on UpdateTopology, after the whole loop. A helper indexed before its lead would otherwise
    // read the lead's lap from the tick before (the lead's UpdateControlValues hasn't run yet); after the loop every
    // lead's lap is this tick's, so the helper and the lead regulate to the same target on every tick, and a lead that
    // reduces, dumps or goes to emergency is followed in the same tick. For each helper that passes Logic.WorksAir:
    // the game's own SetLocomotiveValves with the lead's target for the pipe and, for the cylinder, the very target
    // the game set this tick (CarInfo.brakeCylinderTarget, so its cylinder valves come out exactly as the game left
    // them), and brakeLineTarget = the lead's target so the game's own clamp treats that end as a cut-in engine's.
    //
    // Rules:
    // - A bottled-up air line: the helper works only on a whole brake pipe,
    //   read from this tick's connections (the postfix runs after UpdateTopology set them): every joint between it and
    //   the lead open, and no opening to air anywhere on the pipe they share (an open angle cock at an end, or a hose
    //   off with its cock open), so a cut held behind closed cocks stays held and an open cock dumps as in the game.
    //   The walk follows the game's own representation: a valve is stored 0 or at least 0.01.
    // - S3: while the Auto Engineer drives the lead, the helper doesn't work the air (the AE plans braking without it).
    //   2.0.10: it does, with the AE's copy wired (below); the stand-aside stays only for a helper short of air.
    // - S2: fail closed. Nothing is kept between ticks because the game closes a cut-out engine's pipe valves and sets
    //   its target to -1 on every tick before this runs. On the next step of a train whose
    //   helper it worked (2.0.10: per graph, below), the postfix checks that this still holds for every cut-out engine,
    //   before any early return; if a game update changes it, the hook closes them itself, stands down and warns once
    //   (once per world). Before each write it also checks the two
    //   pipe connections are laid out as in 0.6.2 (AirGraphConfigurator.ConfigureLocomotive: +2 main reservoir to pipe,
    //   +3 pipe to air). There is no graph at mod load, so this is checked on the live graph, at every write.
    //
    // The Auto Engineer plans brakes on a copy of this graph (SetAirGraph.CloneSnapshot) and drives only its own
    // engine's pipe valves in that copy (AirGraphPredictionJob, RolloutSegmentJob); every other valve keeps its copied
    // state. Its jobs are Burst code and can't be patched, so the copy itself carries the helper (a
    // look-ahead tweak): RearAirSnapshotPatch closes every cut-out engine's
    // pipe feed and vent in the copy, as the game has them, and for each helper that works the air it turns the
    // helper's vent connection (+3, pipe to air, unused while cut out) into a pipe from the helper's brake pipe to the
    // lead's, open, at its own rate (2). The AE's lead then charges and vents the rear through it, as the helper does
    // live (tools/airsim ae-predicts: the game's own rollout job on the copy against the live train). Every AE reader
    // copies whole connections (RolloutSegmentSimulator.CopyGraph, BrakePressureSimulator.SetGraphState), so the wire
    // reaches all of them.
    //
    // The copy is wired, and the helper works at all under the AE, only while its
    // main reservoir held the lead's feed valve + 10. That line has no hysteresis: under the AE the live helper switched
    // off and on as its reservoir crossed it, inside the AE's look-ahead, while the copy stays as it was cloned, so
    // the AE could expect more braking than the train got (measured up to 0.28 mean brake fraction). Now
    // (Logic.FeedLatch, Logic.WireCopy): under the AE the helper always vents; it feeds only while its latch is
    // on (on at the feed valve + 10, off under the feed valve); and the copy is wired whenever the helper feeds now or
    // its compressor can bring it to the on-line within the AE's look-ahead, so in the cases tools/airsim runs (the s1-*
    // checks) a remaining miss leaves the train more braking than the copy expects: proven for the rollout (0.02 s, 8 s,
    // which picks apply and release) and for the brake predictor's model at 0.02 s, not at the 0.1 s step the game runs
    // the predictor at, where the game's own lead-only prediction already misses
    // by more than the margin. The main reservoirs are joined in
    // the copy (below). Only an in-game test can show the AE drives well.
    // The latch is per helper car, kept here (RearAirPatch._feeding), reset at every world load.
    //
    // The setting has three choices (Logic.HelperAir).
    // Always is all of the above. By hand only is 2.0.9's air rule on this machinery: Works stands the helper aside
    // whenever the AE drives its lead (the orders read every tick, as above), so under the AE the hook writes nothing,
    // the game's cut-out valves stand, and the copy patch only closes valves the game already closed (the AE plans on
    // the game's own copy). The stand-down, the let-go and the latch are unchanged; the latch is updated only on the
    // steps a helper works. The AE taking over or letting go mid-application hands over on the next air step: the game
    // closes the helper's valves on every step (A3) and the hook either writes them again (by hand) or not (AE).
    //
    // A stand-down lets go. The hook lists the graphs it wrote on their last air step; the next step's
    // postfix checks the game closed them (S2) and drops them. A stand-down closes every listed graph's cut-out engines
    // once and empties the list, so nothing of the mod's stays open and the hook writes nothing more until the next
    // world load.
    //
    // Runs on every computer, from replicated inputs (MU, cut out, the lead's brake handle, angle cocks, the lead's
    // Auto Engineer orders), as the deferral does; the host's air still overwrites a client's about once a second
    // (TrainController.SendAirIfNeeded).
    internal static class RearAir
    {
        // The lead's pipe target this tick, and the helper's valves set by the game's own brake valve logic. Returns the
        // target, for the game's clamp. Called by the postfix and by tools/airsim (the harness on the game's own code).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float Apply(ref AirGraph graph, int reservoirStart, int connectionStart, LocomotiveAirSystem lead, float cylinderTarget)
        {
            float target = Logic.PipeTarget(lead.LapTrainBrakePressure, lead.brakeFeedValvePressure);
            AirGraphConfigurator.SetLocomotiveValves(ref graph, reservoirStart, connectionStart, target, cylinderTarget);
            return target;
        }
    }

    // The game members both patches need, resolved once, before either patch goes in (all or nothing).
    internal static class RearAirMembers
    {
        internal delegate bool DeferFn(LocomotiveAirSystem air, out LocomotiveAirSystem source);

        internal static Func<SetAirGraph, int, bool> IsLocomotive;
        internal static Func<SetAirGraph, int, int> ReservoirStart;
        internal static Func<SetAirGraph, int, int> ConnectionStart;
        internal static Func<SetAirGraph, int, float> BrakeCylinderTarget;
        internal static Func<SetAirGraph, int, float> BrakeLineTarget;
        internal static Action<SetAirGraph, int, float> SetBrakeLineTarget;
        internal static DeferFn TryDeferred;
        // The feed only (WorksNow): a train's air graph (IntegrationSet.AirGraph, private) and the graph's own fields.
        // Not part of the all-or-nothing: without them the feed reports workingAir false and the air part still works.
        internal static Func<IntegrationSet, SetAirGraph> AirGraphOf;
        private static FieldInfo _graphF, _carsF, _mapF;
        // Resolved once per process: game types and members don't change between world loads or mod-set changes, so
        // this is not a per-world latch; the per-world stand-down is RearAirPatch._down.
        private static bool _tried, _ok;

        internal static bool Resolve()
        {
            if (_tried) return _ok;
            _tried = true;
            const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type sag = typeof(SetAirGraph);
            FieldInfo info = sag.GetField("_carInfo", I);
            Type elem = info != null && info.FieldType.IsArray ? info.FieldType.GetElementType() : null;
            FieldInfo isLoco = elem?.GetField("isLocomotive", I), rs = elem?.GetField("reservoirStart", I), cs = elem?.GetField("connectionStart", I),
                      cyl = elem?.GetField("brakeCylinderTarget", I), line = elem?.GetField("brakeLineTarget", I);
            MethodInfo defer = typeof(LocomotiveAirSystem).GetMethod("TryGetDeferredLocomotiveAir", I);
            MethodInfo topo = sag.GetMethod("UpdateTopology", I, null, Type.EmptyTypes, null);
            MethodInfo clone = sag.GetMethod("CloneSnapshot", I);
            FieldInfo graph = sag.GetField("_graph", I), cars = sag.GetField("_cars", I), map = sag.GetField("_carIndexMap", I);
            // 2.1.0: internal static float AutoEngineer.TrainBrake.CalculatePropagationDelay(int carCount)
            // (read by BrakePressureSimulator.LookaheadTime).
            MethodInfo delay = typeof(AutoEngineer).GetNestedType("TrainBrake", BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetMethod("CalculatePropagationDelay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            if (delay == null || delay.ReturnType != typeof(float)
                || elem == null || !elem.IsValueType || isLoco?.FieldType != typeof(bool) || rs?.FieldType != typeof(int) || cs?.FieldType != typeof(int)
                || cyl?.FieldType != typeof(float) || line?.FieldType != typeof(float)
                || defer == null || defer.ReturnType != typeof(bool) || defer.GetParameters().Length != 1
                || topo == null || clone == null || clone.ReturnType != typeof(AirGraphSnapshot)
                || graph?.FieldType != typeof(AirGraph) || cars?.FieldType != typeof(IReadOnlyList<Car>) || map?.FieldType != typeof(Dictionary<Car, int>))
            {
                Util.Warn("the air graph isn't as in 0.6.2; helper engines don't work the air (Remote MU still works)");
                return false;
            }
            IsLocomotive = (Func<SetAirGraph, int, bool>)Getter(typeof(Func<SetAirGraph, int, bool>), info, elem, isLoco);
            ReservoirStart = (Func<SetAirGraph, int, int>)Getter(typeof(Func<SetAirGraph, int, int>), info, elem, rs);
            ConnectionStart = (Func<SetAirGraph, int, int>)Getter(typeof(Func<SetAirGraph, int, int>), info, elem, cs);
            BrakeCylinderTarget = (Func<SetAirGraph, int, float>)Getter(typeof(Func<SetAirGraph, int, float>), info, elem, cyl);
            BrakeLineTarget = (Func<SetAirGraph, int, float>)Getter(typeof(Func<SetAirGraph, int, float>), info, elem, line);
            SetBrakeLineTarget = Setter(info, elem, line);
            TryDeferred = OpenDelegate.Of<DeferFn>(defer);
            PropagationDelay = (Func<int, float>)Delegate.CreateDelegate(typeof(Func<int, float>), delay);
            _graphF = graph; _carsF = cars; _mapF = map;
            try
            {
                MethodInfo get = typeof(IntegrationSet).GetProperty("AirGraph", I)?.GetGetMethod(true);
                if (get != null && get.ReturnType == sag) AirGraphOf = (Func<IntegrationSet, SetAirGraph>)Delegate.CreateDelegate(typeof(Func<IntegrationSet, SetAirGraph>), get);
            }
            catch (Exception) { AirGraphOf = null; }
            _ok = true;
            return true;
        }

        // CarInfo is a private struct in a private array: a reflection read would box it on every call (every physics
        // tick), so each field is read or written through a small method emitted once, here, that loads the element by
        // address (ldelema) and the field from it, as compiled C# would. No allocation per call.
        private static Delegate Getter(Type delegateType, FieldInfo array, Type elem, FieldInfo field)
        {
            var dm = new DynamicMethod("AuxFuelCars_get_" + field.Name, field.FieldType, new[] { typeof(SetAirGraph), typeof(int) }, typeof(SetAirGraph), true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, array);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelema, elem);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return dm.CreateDelegate(delegateType);
        }

        private static Action<SetAirGraph, int, float> Setter(FieldInfo array, Type elem, FieldInfo field)
        {
            var dm = new DynamicMethod("AuxFuelCars_set_" + field.Name, typeof(void), new[] { typeof(SetAirGraph), typeof(int), typeof(float) }, typeof(SetAirGraph), true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, array);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelema, elem);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            return (Action<SetAirGraph, int, float>)dm.CreateDelegate(typeof(Action<SetAirGraph, int, float>));
        }

        // Logic.WorksAir for car i of this graph; lead is the engine the game's deferral follows this tick, li its index
        // in this graph (-1 when it isn't in it).
        internal static bool Works(SetAirGraph g, ref AirGraph graph, IReadOnlyList<Car> cars, Dictionary<Car, int> map, int i, bool remoteOn, HelperAir air, out LocomotiveAirSystem lead, out int li)
        {
            lead = null;
            li = -1;
            if (!IsLocomotive(g, i) || !(cars[i].air is LocomotiveAirSystem helper)) return false;   // as the game's own flag
            if (!helper.IsCutOut || !helper.IsMuEnabled) return false;
            bool deferred = TryDeferred(helper, out lead) && lead != null;
            Car leadCar = deferred ? lead.car : null;
            bool known = leadCar != null;
            // The lead the remote walk recorded is the one the deferral follows.
            bool remote = known && RemoteMuPatch.IsRemote(cars[i].id, leadCar.id);
            bool leadIn = known && map.TryGetValue(leadCar, out li);
            bool leadCutIn = deferred && !lead.IsCutOut;
            bool rearOn = Logic.AirOn(air);
            // The cheap conditions first; the lead's orders and the pipe walk only when every other one holds.
            if (!Logic.WorksAir(remoteOn, rearOn, helper.IsMuEnabled, helper.IsCutOut, deferred, remote, leadCutIn, leadIn, true, true)) return false;
            // Always: the Auto Engineer doesn't stand a helper aside; under it only the feed
            // waits on the latch (RearAirPatch.Postfix). By hand only (2.0.9's rule): while the AE drives the lead the
            // helper stands aside, so the live train and the AE's copy are the game's own; its orders are read only then.
            bool aeOk = Logic.AeOk(air, air == HelperAir.ByHand && AeDrives(leadCar));
            bool whole = aeOk && PipeWhole(g, ref graph, cars.Count, i, li);
            return Logic.WorksAir(remoteOn, rearOn, helper.IsMuEnabled, helper.IsCutOut, deferred, remote, leadCutIn, leadIn, whole, aeOk);
        }

        // 2.1.0: the game's propagation delay for a train of this many cars (AutoEngineer.TrainBrake, internal, bound at
        // load), the brake predictor's look-ahead.
        internal static Func<int, float> PropagationDelay;

        // The compressor that fills this main reservoir in this graph (ConfigureLocomotive: targetReservoir = rs + 3),
        // as the next air step will run it (AirSimKernel.UpdateCompressor: it runs while running and under its cut-out,
        // or once at or under its cut-in, adding fillRate psi per second; SyncToGraph sets fillRate to 0 without fuel).
        // 0 when it won't run.
        internal static float CompressorRate(ref AirGraph graph, int mainReservoir)
        {
            float p = graph.GetReservoirPressure(mainReservoir);
            for (int k = 0; k < graph.CompressorCount; k++)
            {
                CompressorData c = graph.compressors[k];
                if (c.targetReservoir != mainReservoir) continue;
                bool runs = c.isRunning ? p < c.targetPressure : p <= c.cutInPressure;
                return runs && c.fillRate > 0f ? c.fillRate : 0f;
            }
            return 0f;
        }

        // A valve is stored as 0 or at least 0.01 (anything under 0.01 is written 0), so
        // any threshold in (0, 0.01] reads it exactly as the game does.
        private const float Open = 0.001f;

        // The joint between elements j and j+1 is open: element j's B end connection (+1) reaches element j+1's brake
        // pipe (element j's logical B end faces element j+1, IntegrationSet.GetAirConnection)
        // with its valve open. Both cocks closed, either one closed, or the hoses off: not joined.
        private static bool Joined(SetAirGraph g, ref AirGraph graph, int j)
        {
            Connection c = graph.connections[ConnectionStart(g, j) + 1];
            return c.reservoirB == ReservoirStart(g, j + 1) && c.valve >= Open;
        }

        // Element k's brake pipe is open to air at an end: an end connection (+0 for A, +1 for B) with no car on the
        // other side and its own cock open.
        private static bool OpenToAir(SetAirGraph g, ref AirGraph graph, int k)
        {
            int cs = ConnectionStart(g, k);
            Connection a = graph.connections[cs], b = graph.connections[cs + 1];
            return (a.reservoirB < 0 && a.valve >= Open) || (b.reservoirB < 0 && b.valve >= Open);
        }

        // The brake pipe the helper would charge is whole: the stretch of cars joined to the helper by open joints holds
        // the lead (no closed cock or loose hose between them), and nothing on that stretch is open to air
        // (an open angle cock at an end, before the lead, beyond the helper, behind a caboose or a tender).
        internal static bool PipeWhole(SetAirGraph g, ref AirGraph graph, int count, int helper, int lead)
        {
            int lo = helper, hi = helper;
            while (lo > 0 && Joined(g, ref graph, lo - 1)) lo--;
            while (hi < count - 1 && Joined(g, ref graph, hi)) hi++;
            if (lead < lo || lead > hi) return false;
            for (int k = lo; k <= hi; k++) if (OpenToAir(g, ref graph, k)) return false;
            return true;
        }

        // The Auto Engineer drives this engine: its orders as the game reads them (Logic.AeDriving). Read every tick from
        // the engine's own KeyValue object, which the game replicates, so every computer agrees. No object: it can't be
        // told, so the helper stays the game's cut-out engine.
        internal static bool AeDrives(Car lead)
        {
            KeyValueObject kvo = lead != null ? lead.KeyValueObject : null;
            if (kvo == null) return true;
            Value orders = kvo[AutoEngineerPersistence.AutoEngineerOrdersKey];
            if (orders.Type != KeyValue.Runtime.ValueType.Dictionary) return Logic.AeDriving(false, false, 0, false);
            Value mode = orders["mode"];
            return Logic.AeDriving(true, !mode.IsNull, mode.IsNull ? 0 : mode.IntValue, orders["enabled"].BoolValue);
        }

        // The helper's two pipe connections as the game lays them out (AirGraphConfigurator.ConfigureLocomotive): +2 from
        // the main reservoir (+3) to the pipe, +3 from the pipe to air.
        internal static bool LayoutOk(ref AirGraph graph, int rs, int cs)
        {
            Connection feed = graph.connections[cs + 2], vent = graph.connections[cs + 3];
            return feed.reservoirA == rs + 3 && feed.reservoirB == rs && vent.reservoirA == rs && vent.reservoirB == -1;
        }

        internal static bool CutOutEngine(SetAirGraph g, IReadOnlyList<Car> cars, int i)
            => IsLocomotive(g, i) && cars[i].air is LocomotiveAirSystem air && air.IsCutOut;

        // As the game leaves every cut-out engine on every tick.
        internal static bool GameClosed(SetAirGraph g, ref AirGraph graph, int i)
        {
            int cs = ConnectionStart(g, i);
            return graph.GetConnectionValve(cs + 2) == 0f && graph.GetConnectionValve(cs + 3) == 0f && BrakeLineTarget(g, i) == -1f;
        }

        // Every cut-out engine of this graph to the game's cut-out pipe valves.
        internal static void CloseCutOut(SetAirGraph g, ref AirGraph graph, IReadOnlyList<Car> cars)
        {
            for (int i = 0; i < cars.Count; i++)
            {
                if (!CutOutEngine(g, cars, i)) continue;
                int cs = ConnectionStart(g, i);
                graph.SetConnectionValve(cs + 2, 0f);
                graph.SetConnectionValve(cs + 3, 0f);
                SetBrakeLineTarget(g, i, -1f);
            }
        }

        // The same for a graph the hook isn't handed right now (another train it wrote), read fresh from the
        // object: a disposed graph has no cars and no native arrays (SetAirGraph.Dispose), and is skipped.
        internal static void CloseListed(SetAirGraph g)
        {
            if (g == null || !_ok || !(_carsF.GetValue(g) is IReadOnlyList<Car> cars)) return;
            AirGraph graph = (AirGraph)_graphF.GetValue(g);   // a struct over the graph's own native arrays: writes land there
            if (graph.IsCreated) CloseCutOut(g, ref graph, cars);
        }

        internal static bool Disposed(SetAirGraph g) => !(_carsF.GetValue(g) is IReadOnlyList<Car>);

        // The feed: the hook's own rule, now, on this computer's air graph for the helper's train, and false
        // while the air part stands down. Only when a feed reader asks.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool WorksNow(Car helper, bool remoteOn, HelperAir air)
        {
            if (helper == null || AirGraphOf == null) return false;
            IntegrationSet set = helper.set;
            return set != null && WorksIn(AirGraphOf(set), helper, remoteOn, air);
        }

        internal static bool WorksIn(SetAirGraph g, Car helper, bool remoteOn, HelperAir air)
        {
            if (g == null || helper == null || !_ok || RearAirPatch.Down) return false;
            var cars = _carsF.GetValue(g) as IReadOnlyList<Car>;
            var map = _mapF.GetValue(g) as Dictionary<Car, int>;
            if (cars == null || map == null || !map.TryGetValue(helper, out int i) || i < 0 || i >= cars.Count || !ReferenceEquals(cars[i], helper)) return false;
            AirGraph graph = (AirGraph)_graphF.GetValue(g);
            return graph.IsCreated && Works(g, ref graph, cars, map, i, remoteOn, air, out _, out _);
        }
    }

    [HarmonyPatch]
    internal static class RearAirPatch
    {
        // Stand-down latch: one failure turns the part off until the next world load (Reset).
        private static bool _down;
        // The graphs whose cut-out engines the hook wrote on their last air step. On the graph's next
        // step the game has closed them again; that step's postfix checks it (S2) and drops the
        // graph unless a helper works again, so the check is one pass over the cars of a train whose helper worked on
        // its last step (a few field reads per engine), and every other train pays one set lookup. A stand-down closes
        // each graph still listed once and empties the list (LetGo). Reference identity: one entry per SetAirGraph.
        // Main thread only, as the game's air step and the Auto Engineer's copy are.
        private static readonly HashSet<SetAirGraph> _written = new HashSet<SetAirGraph>(ByReference<SetAirGraph>.Instance);
        // RemoteMuPatch.FollowSerial at the last postfix: it moves only when the remote walk answered for a helper since,
        // which the game asks for every helper of a train in its own UpdateTopology, before this postfix.
        private static int _seen = -1;
        // The helpers whose feed latch is on (Logic.FeedLatch), by car reference. Updated on every air
        // step a helper works; read by the copy patch. A helper deleted while latched stays listed until the next world
        // load (one reference, never stepped again). Main thread only, as _written.
        private static readonly HashSet<Car> _feeding = new HashSet<Car>(ByReference<Car>.Instance);
        // 2.1.0: the list size at which the next sweep runs, so a sweep doesn't run (and allocate)
        // on every add once more than 16 graphs are listed.
        private static int _sweepAt = 16;
        private static readonly Predicate<SetAirGraph> IsDisposed = RearAirMembers.Disposed;

        // World load: every latch and list, and the rear-air warnings, so a stand-down in this world says so.
        internal static void Reset()
        {
            _down = false; _seen = -1; _written.Clear(); _feeding.Clear(); _sweepAt = 16;
            foreach (string key in WarnKeys) Util.Unwarn(key);
        }

        // The helper's feed latch after its last air step.
        internal static bool Feeding(Car helper) => _feeding.Count != 0 && _feeding.Contains(helper);

        internal static readonly string[] WarnKeys = { "rear-air", "rear-air-drift", "rear-air-layout", "rear-air-copy" };

        // A stand-down from outside a postfix (tools): as LetGo with no graph in hand.
        internal static void StandDown()
        {
            AirGraph none = default(AirGraph);
            LetGo(null, ref none, null);
        }

        internal static bool Down => _down;

        // The hook wrote this graph on its last air step (so the Auto Engineer's copy of it needs putting right).
        internal static bool Wrote(SetAirGraph g) => _written.Count != 0 && _written.Contains(g);

        // Every stand-down (drift, layout, a throw in either patch): the latch, this train's cut-out engines and every
        // listed graph's back to the game's cut-out valves, once, and the list emptied. After this the postfix returns
        // at its first line and the copy patch at its first, so nothing of the mod's writes the air until the next load.
        internal static void LetGo(SetAirGraph g, ref AirGraph graph, IReadOnlyList<Car> cars)
        {
            _down = true;
            try { if (g != null && cars != null && graph.IsCreated) RearAirMembers.CloseCutOut(g, ref graph, cars); }
            catch (Exception) { }
            foreach (SetAirGraph other in _written)
            {
                if (ReferenceEquals(other, g)) continue;
                try { RearAirMembers.CloseListed(other); }
                catch (Exception) { }
            }
            _written.Clear();
        }

        private static bool Prepare(MethodBase original) => RearAirMembers.Resolve();

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(SetAirGraph), "UpdateTopology", Type.EmptyTypes);

        private static void Postfix(SetAirGraph __instance, ref AirGraph ____graph, IReadOnlyList<Car> ____cars, Dictionary<Car, int> ____carIndexMap)
        {
            int serial = RemoteMuPatch.FollowSerial;
            bool followed = serial != _seen;
            _seen = serial;
            bool mine = _written.Count != 0 && _written.Remove(__instance);
            if (!mine && (_down || !followed)) return;   // nothing of the mod's is open here, and nothing to work
            try
            {
                if (____cars == null || ____carIndexMap == null) return;
                int n = ____cars.Count;
                // S2: on a graph the hook wrote last step, before any other return, every cut-out engine is as the game
                // leaves it on every tick.
                if (mine)
                    for (int i = 0; i < n; i++)
                        if (RearAirMembers.CutOutEngine(__instance, ____cars, i) && !RearAirMembers.GameClosed(__instance, ref ____graph, i))
                        {
                            LetGo(__instance, ref ____graph, ____cars);
                            Util.WarnOnce("rear-air-drift", "this game version runs cut-out engines' brake valves differently; helper engines stopped working the air until the next load (logged once)");
                            return;
                        }
                if (_down || !followed) return;
                bool remoteOn = HostSetting.RemoteMu;
                HelperAir air = HostSetting.HelperAir;
                if (!remoteOn || !Logic.AirOn(air)) return;
                for (int i = 0; i < n; i++)
                {
                    if (!RearAirMembers.Works(__instance, ref ____graph, ____cars, ____carIndexMap, i, remoteOn, air, out LocomotiveAirSystem lead, out _)) continue;
                    int rs = RearAirMembers.ReservoirStart(__instance, i);
                    int cs = RearAirMembers.ConnectionStart(__instance, i);
                    if (!RearAirMembers.LayoutOk(ref ____graph, rs, cs))
                    {
                        LetGo(__instance, ref ____graph, ____cars);
                        Util.WarnOnce("rear-air-layout", "this game version lays out an engine's brake valves differently; helper engines stopped working the air until the next load (logged once)");
                        return;
                    }
                    float cylinder = RearAirMembers.BrakeCylinderTarget(__instance, i);
                    if (_written.Add(__instance) && _written.Count > _sweepAt) Sweep();
                    // The feed latch, on every step the helper works, AE or not, so it is right the
                    // moment the AE takes the lead.
                    Car car = ____cars[i];
                    bool was = Feeding(car);
                    bool latched = Logic.FeedLatch(was, ____graph.GetReservoirPressure(rs + 3), lead.brakeFeedValvePressure);
                    if (latched != was) { if (latched) _feeding.Add(car); else _feeding.Remove(car); }
                    float target = RearAir.Apply(ref ____graph, rs, cs, lead, cylinder);
                    // Under the AE and not latched: vent only (the game's clamp then never feeds either: it raises the
                    // pipe to the target only through an open vent, AirGraphConfigurator.ClampReservoir). Reached under
                    // the AE only with Always (2.1.2: By hand only stood the helper aside in Works).
                    if (!latched && RearAirMembers.AeDrives(lead.car)) ____graph.SetConnectionValve(cs + 2, 0f);
                    RearAirMembers.SetBrakeLineTarget(__instance, i, target);
                }
            }
            catch (Exception e)
            {
                LetGo(__instance, ref ____graph, ____cars);
                Util.WarnOnceWhole("rear-air", "helper engines stopped working the air until the next load (logged once): ", e);
            }
        }

        // A graph disposed while listed (its train recoupled into another) is never stepped again; drop such entries now
        // and then so the list stays small. Inside the postfix's try.
        private static void Sweep()
        {
            _written.RemoveWhere(IsDisposed);
            _sweepAt = Math.Max(16, _written.Count * 2);
        }

        // Reference identity (a Unity object's own GetHashCode asks the engine for its instance id).
        private sealed class ByReference<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ByReference<T> Instance = new ByReference<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T o) => RuntimeHelpers.GetHashCode(o);
        }
    }

    // The Auto Engineer's copy of the graph (2.0.10): every cut-out engine's pipe feed and vent closed, as the game itself
    // sets them every tick; then, for each helper that works the air and feeds now or can within the AE's look-ahead
    // (2.1.0, Logic.WireCopy: its latch, its main reservoir and its compressor in the copy, the horizon from the train's
    // car count), its vent connection retargeted to the lead's brake pipe and opened, at its own rate, and (2.1.1) its
    // feed connection retargeted to join its main reservoir to the lead's, opened, at its own rate. Only on a graph the
    // hook wrote last step: any other copy is already the game's own.
    [HarmonyPatch]
    internal static class RearAirSnapshotPatch
    {
        private static bool Prepare(MethodBase original) => RearAirMembers.Resolve();

        private static MethodBase TargetMethod() => typeof(SetAirGraph).GetMethod("CloneSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static void Postfix(SetAirGraph __instance, AirGraphSnapshot __result, ref AirGraph ____graph, IReadOnlyList<Car> ____cars, Dictionary<Car, int> ____carIndexMap)
        {
            if (!RearAirPatch.Wrote(__instance)) return;
            try
            {
                int[] rsAt = __result.ReservoirStart, csAt = __result.ConnectionStart;
                if (!__result.IsCreated || ____cars == null || ____carIndexMap == null || rsAt == null || csAt == null) return;
                AirGraph copy = __result.Graph;   // a struct over the copy's native arrays: writes land in the copy
                bool remoteOn = HostSetting.RemoteMu;
                HelperAir air = HostSetting.HelperAir;
                int n = Math.Min(____cars.Count, Math.Min(csAt.Length, rsAt.Length));
                float horizon = Logic.Horizon(RearAirMembers.PropagationDelay(____cars.Count));
                for (int i = 0; i < n; i++)
                {
                    if (!RearAirMembers.CutOutEngine(__instance, ____cars, i)) continue;
                    int rs = rsAt[i], cs = csAt[i];
                    copy.SetConnectionValve(cs + 2, 0f);
                    copy.SetConnectionValve(cs + 3, 0f);
                    // The hook's own rule, now, on the live graph (a graph rebuilt since its last step has no open joint, so
                    // no helper works on it until the next step). 2.1.2: only with Always. By hand only never wires: the
                    // copy keeps the closed valves only, as the game makes it (2.0.9's copy guard), also on the first clone
                    // after the AE takes over, while the graph is still listed from the last step by hand. (The AE clones
                    // only while it drives, and By hand only
                    // stands helpers aside then.)
                    if (air != HelperAir.Always
                        || !RearAirMembers.Works(__instance, ref ____graph, ____cars, ____carIndexMap, i, remoteOn, air, out LocomotiveAirSystem lead, out int li)
                        || li < 0 || li >= n || !RearAirMembers.LayoutOk(ref copy, rs, cs)) continue;
                    // Wired when it feeds now (the latch as its next step will find it) or its
                    // compressor can bring it to the on-line within the horizon.
                    float mr = copy.GetReservoirPressure(rs + 3), fv = lead.brakeFeedValvePressure;
                    bool latched = Logic.FeedLatch(RearAirPatch.Feeding(____cars[i]), mr, fv);
                    if (!Logic.WireCopy(latched, mr, fv, RearAirMembers.CompressorRate(ref copy, rs + 3), horizon)) continue;
                    // SetConnection writes the whole connection: velocity and flow start at 0 as for a fresh pipe.
                    copy.SetConnection(cs + 3, rs, rsAt[li], copy.connections[cs + 3].pipeRate, 1f);
                    // Tie the main reservoirs: the helper's feed connection (+2, its
                    // main reservoir to its pipe, closed above) joins its main reservoir to the lead's instead, open, at
                    // its own rate. Live, the helper's own feed valve charges the pipe from its main reservoir toward
                    // the lead's target; the AE's jobs regulate only the lead's valves, so in the copy that air has to
                    // reach the pipe through the lead's feed valve. Without the join the copy charged the whole pipe
                    // from the lead's main reservoir alone and released long trains slower than the train does.
                    if (RearAirMembers.IsLocomotive(__instance, li) && RearAirMembers.LayoutOk(ref copy, rsAt[li], csAt[li]))
                        copy.SetConnection(cs + 2, rs + 3, rsAt[li] + 3, copy.connections[cs + 2].pipeRate, 1f);
                }
            }
            catch (Exception e)
            {
                RearAirPatch.LetGo(__instance, ref ____graph, ____cars);
                Util.WarnOnceWhole("rear-air-copy", "helper engines stopped working the air until the next load (the Auto Engineer's copy failed, logged once): ", e);
            }
        }
    }
}
