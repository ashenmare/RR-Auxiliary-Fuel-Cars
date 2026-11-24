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
    // 2.3.0: under the same setting a declared taker (a heater car) counts as a diesel
    // unit in that walk, for an engine's chain and for a taker's own, so diesel - heater - aux feeds the diesel and
    // heater - heater - aux fills both heaters. A steam engine's walk has no lash-up, so a taker still ends it.
    // And an aux car that gives none of the walker's loads is stepped over too (diesel - water car - diesel car).
    // Both are the full mod's (HostSetting.FullMod); the embedded build keeps its own, older rule.
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
        // The cars a lash-up walk steps over: diesel units and (2.3.0, full mod only) declared takers, which count as diesel units
        // there, and an aux car that gives none of the walker's loads (a water car in a diesel's walk), which the walk steps over
        // as it does a diesel unit and Logic.AuxOnly then drops as a source. Only ever used under Lash-up (a steam engine's walk
        // has none, so a diesel car still ends a steam chain). The embedded build steps over diesel units alone, as it always has.
        private static readonly string[] AllLoads = { Logic.Diesel, Logic.Water, Logic.Coal };
        private static readonly Func<Car, bool> PassDiesel = c => c is DieselLocomotive || (HostSetting.FullMod && (OpenCars.IsTaker(c.DefinitionInfo?.Identifier) || IsAnyAux(c)));
        private static readonly Func<Car, Car, Car> NextCar = Next;
        // 2.3.0: set by OpenEngineTanks.Set when a pack declared an engine tank, else null (the embedded build never sets it).
#pragma warning disable CS0649   // never assigned in the embedded build, which shares this file and declares no engine tanks
        internal static volatile Action<DieselLocomotive, float, bool> EngineTankHook;
#pragma warning restore CS0649

        internal static void Run(Car loco, Car holder, Car notThisWay, Car.LogicalEnd[] ends, string[] loads, float dt, bool lashUp = false)
        {
            List<Car> sources = Sources(holder, notThisWay, ends, loads, lashUp);
            if (sources == null || sources.Count == 0) return;
            // 2.0.8, the feed: which engine each aux car in the chain serves (FeedLog, read only by Api.Read).
            string locoId = loco?.id;
            foreach (Car c in sources) FeedLog.Chain(c.id, locoId);
            foreach (string load in loads) Move(holder, sources, load, dt);
        }

        // The aux cars the holder draws from, farthest first (null when there are none to walk). 2.3.0: split out of Run so an
        // engine's tank on a key (EngineTankTopUp) walks the very same chain.
        internal static List<Car> Sources(Car holder, Car notThisWay, Car.LogicalEnd[] ends, string[] loads, bool lashUp)
        {
            if (holder == null || holder.set == null || holder.IsInBardo) return null;
            _loads = loads;
            Func<Car, bool> passOver = lashUp ? PassDiesel : null;
            List<Car> sideA = null, sideB = null;
            foreach (Car.LogicalEnd end in ends)
            {
                Car first = holder.CoupledTo(end);
                if (first == null || ReferenceEquals(first, notThisWay)) continue;
                // Logic.Walk finds nothing unless an aux car is reached (through the cars to pass over), so don't build its
                // list and set: AuxAhead reads the same rule without allocating.
                if (!AuxAhead(holder, first, loads, lashUp)) continue;
                List<Car> side = Logic.Walk<Car>(holder, first, IsAuxNow, NextCar, passOver);
                if (end == Car.LogicalEnd.A) sideA = side; else sideB = side;
            }
            if ((sideA == null || sideA.Count == 0) && (sideB == null || sideB.Count == 0)) return null;
            return Logic.AuxOnly(Logic.FarthestFirst(sideA, sideB), IsAuxNow);
        }

        // True when Run's walk from first (the car coupled to holder on one side) would take an aux car: the same rule as
        // Logic.Walk (aux cars and, under lashUp, passed-over cars), read without building the list or set, so a caller can
        // skip the walk. The count bound stands in for the loop guard.
        internal static bool AuxAhead(Car holder, Car first, string[] loads, bool lashUp)
        {
            Car prev = holder, car = first;
            for (int n = 0; car != null && n < Logic.MaxChain; n++)
            {
                if (IsAux(car, loads)) return true;
                if (!(lashUp && PassDiesel(car))) return false;
                Car following = Next(car, prev);
                prev = car;
                car = following;
            }
            return false;
        }

        // The car coupled at the other end of car from prev (null when that end is uncoupled).
        internal static Car Next(Car car, Car prev)
        {
            Car a = car.CoupledTo(Car.LogicalEnd.A);
            Car b = car.CoupledTo(Car.LogicalEnd.B);
            if (ReferenceEquals(a, prev)) return b;
            if (ReferenceEquals(b, prev)) return a;
            return null;
        }

        // One of the mod's own cars, or (2.1.0) a car another pack declared, with a slot for a load the holder takes
        // that it gives (OpenCars.Gives: a declared car's own list of loads, when it has one).
        internal static bool IsAux(Car car, string[] loads)
        {
            string id = car?.DefinitionInfo?.Identifier;
            if (id == null) return false;
            foreach (string load in loads)
                if (OpenCars.Gives(id, load) && SlotFor(car, load) >= 0) return true;
            return false;
        }

        // An aux car for some load (any of the three), whether or not the walker takes it: IsAux's own test over every load.
        private static bool IsAnyAux(Car car) => IsAux(car, AllLoads);

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

            float total = DrawFrom(sources, load, deficit, dt);
            if (total <= 0f) return;
            holder.SetLoadInfo(hs, new CarLoadInfo(load, Math.Min(cap, have + total), held?.OriginId));
        }

        // Takes up to deficit of load from the sources, farthest first, at the tower's rate (times Faster Fueling's) for dt, writes the
        // aux cars' loads and the feed's record, and returns what was taken (the caller puts it where it goes).
        internal static float DrawFrom(List<Car> sources, string load, float deficit, float dt)
        {
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
            if (total <= 0f) return 0f;
            for (int i = 0; i < sources.Count; i++)
            {
                if (taken[i] <= 0f) continue;
                CarLoadInfo info = infos[i].Value;
                sources[i].SetLoadInfo(slots[i], new CarLoadInfo(load, Math.Max(0f, info.Quantity - taken[i]), info.OriginId));
                FeedLog.Gave(sources[i].id);
            }
            return total;
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

        // 2.2.3: the game answers rr/coal as every steam engine's FuelLoadId (SteamLocomotiveDefinition.FuelLoadId), but an
        // oil-fired engine (Oil Conversion) burns diesel from its tender's slot. So the fuel is read from the holder's own
        // slots: a diesel slot and no coal slot is oil, anything else is the game's answer. Cached per holder definition
        // (the scan would otherwise run every second for every engine); a lookup allocates nothing.
        private static readonly Dictionary<object, bool> Oil = new Dictionary<object, bool>();

        // At world unload: the cache is keyed by definition objects, which a new world makes anew.
        internal static void ClearOil() => Oil.Clear();

        internal static bool OilFired(Car holder)
        {
            object key = holder.Definition;
            if (key == null) return false;
            if (Oil.TryGetValue(key, out bool oil)) return oil;
            bool diesel = false, coal = false;
            var slots = holder.Definition.LoadSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                string id = slots[i]?.RequiredLoadIdentifier;
                if (id == Logic.Diesel) diesel = true; else if (id == Logic.Coal) coal = true;
            }
            oil = diesel && !coal;
            Oil[key] = oil;
            return oil;
        }

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
                Loads[0] = OilFired(holder) ? Logic.Diesel : loco.LocoDefinition.FuelLoadId;
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
            // 2.3.0: a tank another pack keeps on the engine's keys (OpenEngineTanks); null in the embedded build, and while none is declared.
            Action<DieselLocomotive, float, bool> tanks = TopUp.EngineTankHook;
            if (tanks == null) return;
            try { tanks(__instance, dt, HostSetting.LashUp); }
            catch (Exception e)
            {
                Util.WarnOnceWhole("tank", "engine tank top-up failed (logged once; it keeps trying): ", e);
            }
        }
    }
}
