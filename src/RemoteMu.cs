using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.Physics;
using Game.Messages;

namespace AuxFuelCars
{
    // Remote MU: a postfix on BaseLocomotive.FindMuSourceLocomotive (internal, 0.6.2). Every MU consumer asks this one
    // method which engine an MU-enabled engine follows: the host's throttle and reverser copy
    // (BaseLocomotive.PeriodicUpdateForMu), each machine's air deferral (LocomotiveAirSystem._ShouldDeferToLocomotiveAir)
    // and the Auto Engineer's count of MU'd engines (AutoEngineer.CachedMuConnectedLocomotives).
    //
    // Vanilla walks the coupled, air-connected cars from the engine toward its F end, then its R end; it skips itself
    // and tenders, takes the first engine whose air is cut in, and gives up at the first car that is neither. When it
    // gives up both ways (null), this walks the same cars the same way with one change: a car that is neither engine
    // nor tender is passed over. Whenever vanilla finds a source, the answer is left alone, so every consist vanilla
    // handles behaves exactly as before. The walk stays bounded by coupled, hose-connected joints, as vanilla's is
    // (anglecocks are not checked, as vanilla doesn't).
    //
    // Runs on every machine: on clients it keeps the air deferral the same as the host's.
    [HarmonyPatch]
    internal static class RemoteMuPatch
    {
        private static readonly string MuKey = PropertyChange.KeyForControl(PropertyChange.Control.Mu);

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(BaseLocomotive), "FindMuSourceLocomotive", Type.EmptyTypes);

        private static bool Prepare(MethodBase original)
        {
            if (original != null) return true;
            MethodInfo m = AccessTools.Method(typeof(BaseLocomotive), "FindMuSourceLocomotive", Type.EmptyTypes);
            if (m == null || m.ReturnType != typeof(BaseLocomotive))
            {
                Util.Warn("BaseLocomotive.FindMuSourceLocomotive() isn't there as in 0.6.2; remote MU stays off");
                return false;
            }
            return true;
        }

        // A helper that was following through the remote walk and loses it (a hose or coupler broken
        // between, cars set out, the lead's air cut out, Remote MU saved Off) would keep its last throttle and
        // reverser: vanilla only copies them while a source exists and never resets them (BaseLocomotive.
        // PeriodicUpdateForMu), and with its air cut out and no deferral its brake cylinder vents. So remember which
        // engines the remote walk answered for; when one of them, still MU'd with its air cut out, gets no source, mark
        // it lost, and the host's once-a-second locomotive postfix (TopUp.cs) puts its throttle to idle once
        // (ReleaseIfLost). A player who cuts its air in or unticks MU has taken it back: it's forgotten, not touched.
        // Ids, not objects, under a lock: the AE's caches may ask from elsewhere. Cleared at world unload.
        // 2.0.8: each helper's lead (its car id) and which way the walk found it, for helper engines working the air
        // (RearAir.cs asks IsRemote every physics tick) and for the feed (Links). A struct value: no allocation per tick.
        // An entry for a helper that was deleted or sold while linked stays until the world unloads, but
        // nothing walks the train's cars for it any more: the air hook goes by FollowSerial, which moves only when the
        // walk answers for a live helper, and the feed lists only cars that still exist.
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Link> Remote = new Dictionary<string, Link>();
        private static readonly HashSet<string> Lost = new HashSet<string>();
        private static volatile int _followSerial;

        internal struct Link
        {
            internal string LeadId;
            internal bool LeadTowardF;   // the lead was found walking from the helper's F end (else its R end)
        }

        internal static void Clear() { lock (Gate) { Remote.Clear(); Lost.Clear(); } }

        private static void Forget(string id) { if (id == null) return; lock (Gate) { Remote.Remove(id); Lost.Remove(id); } }
        private static void Followed(string id, string leadId, bool towardF)
        {
            if (id == null) return;
            lock (Gate) { Remote[id] = new Link { LeadId = leadId, LeadTowardF = towardF }; Lost.Remove(id); _followSerial++; }
        }
        private static void LoseIfRemote(string id) { if (id == null) return; lock (Gate) { if (Remote.Remove(id)) Lost.Add(id); } }

        // Moves every time the remote walk answers for a helper. The game asks for every helper of a train inside that
        // train's UpdateTopology, before RearAirPatch's postfix, so a postfix that sees it unchanged since the last one
        // knows its train has no remote helper (other askers only make it move more often, never less).
        internal static int FollowSerial => _followSerial;

        // This helper is following leadId through the remote walk now (its last MU question was answered by it, with
        // that lead).
        internal static bool IsRemote(string id, string leadId)
        {
            if (id == null || leadId == null) return false;
            lock (Gate) return Remote.TryGetValue(id, out Link link) && link.LeadId == leadId;
        }

        // The feed: a copy of every remote link, helper id to its link.
        internal static List<KeyValuePair<string, Link>> Links()
        {
            lock (Gate) return new List<KeyValuePair<string, Link>>(Remote);
        }

        // Host only, from the steam and diesel PeriodicUpdate postfixes (once a second per engine).
        internal static void ReleaseIfLost(BaseLocomotive loco)
        {
            string id = loco?.id;
            if (id == null) return;
            lock (Gate) { if (!Lost.Remove(id)) return; }
            // Only an engine that is still a helper (MU on, air cut out); one the player has taken
            // back since the loss was marked is left alone.
            if (!loco.KeyValueObject[MuKey].BoolValue) return;
            var air = loco.locomotiveControl != null ? loco.locomotiveControl.air : null;
            if (air == null || !air.IsCutOut) return;
            // The same call vanilla's MU copy uses to set it (Car.SendPropertyChange, public).
            loco.SendPropertyChange(PropertyChange.Control.Throttle, 0f);
            if (DebugLog.On) Util.Detail("mu-lost-lead", "remote MU: " + loco.id + " lost its lead, throttle to idle (its air is cut out, so it has no brakes of its own)");
        }

        private static void Postfix(BaseLocomotive __instance, ref BaseLocomotive __result)
        {
            try
            {
                BaseLocomotive self = __instance;
                string id = self?.id;
                if (!ReferenceEquals(__result, null)) { Forget(id); return; }   // vanilla found a source: vanilla's rules
                if (self == null) return;
                // Only for a helper: its MU box on and its air cut out. Every vanilla consumer already asks only for an
                // MU'd engine (BaseLocomotive.PeriodicUpdateForMu, LocomotiveAirSystem._ShouldDeferToLocomotiveAir,
                // AutoEngineer's MU count); this also keeps a lone lead or a hand-run engine from walking the train.
                // MU is read from the car's own key, as BaseLocomotive.IsMuEnabled (internal) reads it.
                if (!self.KeyValueObject[MuKey].BoolValue) { Forget(id); return; }
                var air = self.locomotiveControl != null ? self.locomotiveControl.air : null;
                if (air == null || !air.IsCutOut) { Forget(id); return; }
                if (!HostSetting.RemoteMu) { LoseIfRemote(id); return; }   // switched off: a remote helper is let go
                BaseLocomotive f = Walk(self, Car.End.F);                     // F side, then R, as vanilla orders them
                __result = f ?? Walk(self, Car.End.R);
                if (!ReferenceEquals(__result, null)) Followed(id, __result.id, !ReferenceEquals(f, null)); else LoseIfRemote(id);
            }
            catch (Exception e)
            {
                __result = null;
                Util.WarnOnceWhole("mu", "remote MU walk failed (logged once; vanilla's answer stands): ", e);
            }
        }

        // The remote rule (Logic.RemoteSource, the one the probe fuzzes against vanilla's): along the coupled,
        // air-connected cars, the first engine whose air is cut in; itself, tenders, cut-out engines and every other car
        // are passed over. One loop, no lists or delegates, since the air system asks every physics tick
        // (TrainController.FixedUpdate -> IntegrationSet.UpdateAir -> SetAirGraph.UpdateLocomotiveValves ->
        // UpdateCachedShouldDeferToLocomotiveAir -> FindMuSourceLocomotive).
        private static BaseLocomotive Walk(BaseLocomotive self, Car.End toward)
        {
            IntegrationSet set = self.set;
            if (set == null) return null;
            int? index = set.IndexOfCar(self);
            if (!index.HasValue) return null;
            Car.LogicalEnd search = self.EndToLogical(toward);
            Car.LogicalEnd fromEnd = search == Car.LogicalEnd.A ? Car.LogicalEnd.B : Car.LogicalEnd.A;
            int i = index.Value;
            bool stop = false;
            Car car;
            for (int n = 0; n < 2000 && !stop && (car = set.NextCarConnected(ref i, fromEnd, IntegrationSet.EnumerationCondition.AirAndCoupled, out stop)) != null; n++)
                if (Classify(self, car) == Logic.MuCar.CutInLoco) return car as BaseLocomotive;
            return null;
        }

        private static Logic.MuCar Classify(BaseLocomotive self, Car car)
        {
            if (ReferenceEquals(car, self)) return Logic.MuCar.Self;
            if (car.CarType.IsTender) return Logic.MuCar.Tender;
            if (car.IsLocomotive && car is BaseLocomotive loco)
            {
                // Vanilla reads locomotiveControl.air.IsCutOut; an engine without one can't be a source here.
                var air = loco.locomotiveControl != null ? loco.locomotiveControl.air : null;
                if (air == null) return Logic.MuCar.CutOutLoco;
                return air.IsCutOut ? Logic.MuCar.CutOutLoco : Logic.MuCar.CutInLoco;
            }
            return Logic.MuCar.Other;
        }
    }
}
