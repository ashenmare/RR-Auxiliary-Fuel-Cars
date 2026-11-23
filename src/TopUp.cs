using System;
using System.Collections.Generic;
using System.Reflection;
using Game.State;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using Model.Ops;

namespace AuxFuelCars
{
    // The top-up, host only, once a second per locomotive (BaseLocomotive.PeriodicUpdateBody runs PeriodicUpdate(1f)
    // on every machine; only the host writes loads, as the game's own burn does). It runs after the game has burned
    // this second's fuel and water, so the game stays the only consumer and its gauges stay the truth: the aux cars
    // put back what was burned, from the farthest car first.
    //
    // The holder is the car the game itself burns from: SteamLocomotive.FuelCar() (the tender, or the engine itself
    // for a tank engine), or the diesel itself. The walk starts at the holder and goes only through coupled joints
    // (Car.CoupledTo), never through a car that is merely touching:
    //   tender:      from the tender's end that isn't the engine (loco - tender - aux - aux ...)
    //   tank engine: from the engine's rear (its physical R end, where a tender would ride)
    //   diesel:      from both ends (aux - loco - aux, loco - aux - aux)
    // It passes over the mod's own cars that carry a load the holder takes (Feed on or off) and stops at anything
    // else, so loco - tender - aux - tender - loco shares the aux car between both engines.
    // 2.1.0: other packs' declared cars (OpenCars, aux-fuel-cars.json) are aux cars too, for the loads they declare.
    // And the lash-up feed (the Lash-up feed setting, on by default): a diesel's walk also passes over
    // directly coupled diesel units, so diesel - diesel - aux fills both units; any other car still ends it.
    //
    // No allocation on the game's Boehm collector: this runs once a second for every engine on the host, so with
    // no aux car beside the engine it allocates nothing. The loads and end arrays are made once by the postfixes and filled
    // in place; the delegates below are made once; and a side is walked only when the car coupled to the holder on it is
    // itself an aux car (or, under Lash-up, a diesel unit to pass over), because Logic.Walk takes nothing otherwise. All of
    // it is the game's main thread (PeriodicUpdate), one engine at a time, so the shared scratch is never in use twice.
    internal static class TopUp
    {
        // The loads of the Run in progress (the caller's array, read by IsAuxNow).
        private static string[] _loads;
        private static readonly Func<Car, bool> IsAuxNow = c => IsAux(c, _loads);
        private static readonly Func<Car, bool> PassDiesel = c => c is DieselLocomotive;
        private static readonly Func<Car, Car, Car> NextCar = Next;

        internal static void Run(Car loco, Car holder, Car notThisWay, Car.LogicalEnd[] ends, string[] loads, float dt, bool lashUp = false)
        {
            if (holder == null || holder.set == null || holder.IsInBardo) return;
            _loads = loads;
            Func<Car, bool> passOver = lashUp ? PassDiesel : null;
            List<Car> sideA = null, sideB = null;
            foreach (Car.LogicalEnd end in ends)
            {
                Car first = holder.CoupledTo(end);
                if (first == null || ReferenceEquals(first, notThisWay)) continue;
                // Logic.Walk takes the first car only if it is an aux car or a car to pass over; otherwise its answer is
                // an empty list, so don't build it (the list and set it allocates).
                if (!IsAux(first, loads) && !(passOver != null && passOver(first))) continue;
                List<Car> side = Logic.Walk<Car>(holder, first, IsAuxNow, NextCar, passOver);
                if (end == Car.LogicalEnd.A) sideA = side; else sideB = side;
            }
            if ((sideA == null || sideA.Count == 0) && (sideB == null || sideB.Count == 0)) return;
            List<Car> sources = Logic.AuxOnly(Logic.FarthestFirst(sideA, sideB), IsAuxNow);
            if (sources.Count == 0) return;
            // 2.0.8, the feed: which engine each aux car in the chain serves (FeedLog, read only by Api.Read).
            string locoId = loco?.id;
            foreach (Car c in sources) FeedLog.Chain(c.id, locoId);
            foreach (string load in loads) Move(holder, sources, load, dt);
        }

        // The car coupled at the other end of car from prev (null when that end is uncoupled).
        private static Car Next(Car car, Car prev)
        {
            Car a = car.CoupledTo(Car.LogicalEnd.A);
            Car b = car.CoupledTo(Car.LogicalEnd.B);
            if (ReferenceEquals(a, prev)) return b;
            if (ReferenceEquals(b, prev)) return a;
            return null;
        }

        // One of the mod's own cars, or (2.1.0) a car another pack declared, with a slot for a load the holder takes
        // that it gives (OpenCars.Gives: a declared car's own list of loads, when it has one).
        private static bool IsAux(Car car, string[] loads)
        {
            string id = car?.DefinitionInfo?.Identifier;
            if (id == null) return false;
            foreach (string load in loads)
                if (OpenCars.Gives(id, load) && SlotFor(car, load) >= 0) return true;
            return false;
        }

        // A car's slot for a load, from that car's own definition (never the locomotive's per-load slot cache, which
        // would hand a tender's index to an aux car with a different layout).
        private static int SlotFor(Car car, string loadId)
        {
            var slots = car.Definition.LoadSlots;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].RequiredLoadIdentifier == loadId) return i;
            return -1;
        }

        private static void Move(Car holder, List<Car> sources, string load, float dt)
        {
            int hs = SlotFor(holder, load);
            if (hs < 0) return;
            float cap = holder.Definition.LoadSlots[hs].CalculateMaximumCapacity(load, holder);
            CarLoadInfo? held = holder.GetLoadInfo(hs);
            // As the game's own burn reads it: the slot's quantity, whatever it's labeled.
            float have = held?.Quantity ?? 0f;
            float deficit = cap - have;
            if (!(deficit >= Logic.MinMove)) return;

            var available = new float[sources.Count];
            var slots = new int[sources.Count];
            var infos = new CarLoadInfo?[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                slots[i] = SlotFor(sources[i], load);
                if (slots[i] < 0 || !OpenCars.Gives(sources[i].DefinitionInfo.Identifier, load) || !Feed.IsOn(sources[i])) continue;
                infos[i] = sources[i].GetLoadInfo(slots[i]);
                if (infos[i].HasValue && infos[i].Value.LoadId == load) available[i] = infos[i].Value.Quantity;
            }
            var taken = new float[sources.Count];
            float total = Logic.Draw(deficit, Logic.RatePerSecond(load) * FasterFueling.Multiplier(load) * dt, available, taken);
            if (total <= 0f) return;
            for (int i = 0; i < sources.Count; i++)
            {
                if (taken[i] <= 0f) continue;
                CarLoadInfo info = infos[i].Value;
                sources[i].SetLoadInfo(slots[i], new CarLoadInfo(load, Math.Max(0f, info.Quantity - taken[i]), info.OriginId));
                FeedLog.Gave(sources[i].id);
            }
            holder.SetLoadInfo(hs, new CarLoadInfo(load, Math.Min(cap, have + total), held?.OriginId));
        }
    }

    // Steam: a postfix on SteamLocomotive.PeriodicUpdate (0.6.2: burns in PeriodicUpdateConsumeFuel, called from it).
    // ArticulatedSteamLocomotive doesn't override it, so this covers articulateds too.
    [HarmonyPatch]
    internal static class SteamTopUpPatch
    {
        private static Func<SteamLocomotive, Car> _fuelCar;

        private static bool Prepare()
        {
            if (_fuelCar != null) return true;
            // internal virtual Car FuelCar(): the car the game burns from; bound as an open delegate so an override
            // (a future engine class) is still the one called.
            MethodInfo m = AccessTools.Method(typeof(SteamLocomotive), "FuelCar", Type.EmptyTypes);
            if (m == null || m.ReturnType != typeof(Car))
            {
                Util.Warn("SteamLocomotive.FuelCar() isn't there as in 0.6.2; steam top-up stays off");
                return false;
            }
            _fuelCar = OpenDelegate.Of<Func<SteamLocomotive, Car>>(m);
            return true;
        }

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(SteamLocomotive), "PeriodicUpdate", new[] { typeof(float) });

        private static readonly Car.LogicalEnd[] Both = { Car.LogicalEnd.A, Car.LogicalEnd.B };
        // Made once, filled in place for each engine (2.2.0; the main thread, one engine at a time).
        private static readonly Car.LogicalEnd[] Rear = new Car.LogicalEnd[1];
        private static readonly string[] Loads = new string[2];

        private static void Postfix(SteamLocomotive __instance, float dt)
        {
            if (!StateManager.IsHost) return;
            try { RemoteMuPatch.ReleaseIfLost(__instance); }
            catch (Exception e) { Util.WarnOnceWhole("mu-release", "couldn't idle a remote MU helper that lost its lead (logged once): ", e); }
            try
            {
                SteamLocomotive loco = __instance;
                if (loco == null || loco.set == null || loco.IsInBardo) return;
                Car holder = _fuelCar(loco);
                if (holder == null) return;
                Loads[0] = loco.LocoDefinition.FuelLoadId;
                Loads[1] = Logic.Water;
                if (ReferenceEquals(holder, loco))
                {
                    Rear[0] = loco.EndToLogical(Car.End.R);
                    TopUp.Run(loco, holder, null, Rear, Loads, dt);   // tank engine: its rear
                }
                else
                    TopUp.Run(loco, holder, loco, Both, Loads, dt);   // tender: away from the engine
            }
            catch (Exception e)
            {
                Util.WarnOnceWhole("steam", "steam top-up failed (logged once; it keeps trying): ", e);
            }
        }
    }

    // Diesel: a postfix on DieselLocomotive.PeriodicUpdate. The diesel holds its own fuel; aux cars at either end.
    [HarmonyPatch]
    internal static class DieselTopUpPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(DieselLocomotive), "PeriodicUpdate", new[] { typeof(float) });

        private static readonly Car.LogicalEnd[] Both = { Car.LogicalEnd.A, Car.LogicalEnd.B };
        private static readonly string[] Loads = new string[1];   // made once, filled in place (2.2.0)

        private static void Postfix(DieselLocomotive __instance, float dt)
        {
            if (!StateManager.IsHost) return;
            try { RemoteMuPatch.ReleaseIfLost(__instance); }
            catch (Exception e) { Util.WarnOnceWhole("mu-release", "couldn't idle a remote MU helper that lost its lead (logged once): ", e); }
            try
            {
                DieselLocomotive loco = __instance;
                if (loco == null || loco.set == null || loco.IsInBardo) return;
                if (!(loco.Definition is DieselLocomotiveDefinition def)) return;
                // 2.1.0: the host's Lash-up feed setting (the top-up runs only on the host).
                Loads[0] = def.FuelLoadId;
                TopUp.Run(loco, loco, null, Both, Loads, dt, HostSetting.LashUp);
            }
            catch (Exception e)
            {
                Util.WarnOnceWhole("diesel", "diesel top-up failed (logged once; it keeps trying): ", e);
            }
        }
    }
}
