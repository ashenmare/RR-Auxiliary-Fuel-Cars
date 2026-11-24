using System;
using System.Collections;
using System.Collections.Generic;
using Game.State;
using Model;
using Model.Ops;
using UnityEngine;

namespace AuxFuelCars
{
    // 2.3.0, takers: cars that aren't engines but need fuel or water (OpenTakers, the "takers" list of another
    // pack's aux-fuel-cars.json). They have no once-a-second update of their own (only BaseLocomotive runs
    // PeriodicUpdate), so one tick does it for them: TakerTick, a host-only coroutine on a one-second wait as the
    // engines' is (BaseLocomotive's one-second wait), made at world load only when a taker was accepted.
    // The rule is the engines' own, TopUp.Run with the taker as the holder: both of its ends are walked through coupled
    // joints, the aux cars it passes over are the ones with a slot for a load the taker takes (and, for another pack's,
    // that give it), the order is farthest first, each load moves at the tower rate times Faster Fueling's multiple,
    // and a car's Feed box off is never drawn from.
    // The Lash-up feed setting: a taker counts as a diesel unit. On: a taker's walk passes over directly
    // coupled takers and diesel units as a diesel's does (heater - heater - aux fills both; heater - diesel - aux fills
    // the heater through the diesel), and a diesel's walk passes over takers (diesel - heater - aux feeds the diesel;
    // TopUp.PassDiesel). A steam engine's walk has no lash-up and still ends at a taker. Off: as 2.3.0 was, a taker's
    // walk passes over aux cars only, and an engine's walk ends at a taker.
    // The tick is a poll on purpose: the game has no event for a car being added or removed, so it looks at
    // the world's car list once a second, indexes it (no enumerator, no allocation), and costs nothing at all unless a
    // pack declared a taker (no driver is made then).
    internal static class TakerTopUp
    {
        private static readonly Car.LogicalEnd[] Both = { Car.LogicalEnd.A, Car.LogicalEnd.B };
        // A taker takes at most the loads that have a rate (water, coal, diesel-fuel: three), see OpenTakers.Accept.
        private static readonly float[] Before = new float[3];

        // One second of every taker in cars. Host only (the loads are written on the host and replicated, as the game's burn is).
        internal static void Tick(IReadOnlyCollection<Car> cars, float dt)
        {
            if (cars == null || OpenTakers.Count == 0 || !StateManager.IsHost) return;
            bool lashUp = HostSetting.LashUp;
            if (cars is List<Car> list)
            {
                for (int i = 0; i < list.Count; i++) One(list[i], dt, lashUp);
            }
            else
            {
                foreach (Car c in cars) One(c, dt, lashUp);   // not the game's own list type: an enumerator a second, nothing else
            }
        }

        private static void One(Car car, float dt, bool lashUp)
        {
            try
            {
                if (car == null || car.set == null || car.IsInBardo) return;
                if (!OpenTakers.TryGet(car.DefinitionInfo?.Identifier, out OpenTakers.Entry e) || car.id == null) return;
                string[] loads = e.Loads;
                if (loads == null || loads.Length > Before.Length) return;
                FeedLog.TakerRan(car.id);
                // Reading a load builds its key (the game's GetLoadInfo allocates), so a taker with no aux car beside it, the
                // usual case for one riding in a train, is not read at all: TopUp.Run would find nothing to walk.
                if (!AuxAhead(car, loads, lashUp)) return;
                for (int k = 0; k < loads.Length; k++) Before[k] = Qty(car, loads[k]);
                FeedLog.BeginTaker(car.id);
                try { TopUp.Run(car, car, null, Both, loads, dt, lashUp); }
                finally { FeedLog.EndTaker(); }
                // What it drew this second: the car's own quantity before and after. Nothing else writes it between the two
                // reads (one synchronous call on the main thread), and TopUp.Run only ever raises a holder's load.
                for (int k = 0; k < loads.Length; k++)
                {
                    float got = Qty(car, loads[k]) - Before[k];
                    if (got >= Logic.MinMove) FeedLog.TakerDrew(car.id, loads[k], got);
                }
            }
            catch (Exception x)
            {
                Util.WarnOnceWhole("taker", "taker top-up failed (logged once; it keeps trying): ", x);
            }
        }

        // An aux car on either side, directly coupled or (Lash-up feed on) past takers and diesel units: what TopUp.Run's walk takes.
        private static bool AuxAhead(Car car, string[] loads, bool lashUp)
        {
            for (int n = 0; n < Both.Length; n++)
                if (TopUp.AuxAhead(car, car.CoupledTo(Both[n]), loads, lashUp)) return true;
            return false;
        }

        // The quantity in the car's slot for a load, read as TopUp.Move reads the holder's (the slot's, whatever it is labeled); 0 for none.
        private static float Qty(Car car, string loadId)
        {
            var slots = car.Definition.LoadSlots;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].RequiredLoadIdentifier == loadId)
                    return car.GetLoadInfo(i)?.Quantity ?? 0f;
            return 0f;
        }
    }

    // The tick's driver: one GameObject with TakerTick on it, from world load to world unload.
    internal static class TakerDriver
    {
        private static GameObject _host;

        // At world load, after OpenCarsLoader.Load: made only when a taker was accepted. Replaces any earlier one.
        internal static void Start()
        {
            Stop();
            if (OpenTakers.Count == 0) return;
            _host = new GameObject("AuxFuelCars.Takers");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<TakerTick>();
        }

        internal static void Stop()
        {
            GameObject host = _host;
            _host = null;
            if (host != null) UnityEngine.Object.Destroy(host);
        }

        internal static bool Running => _host != null;
    }

    internal sealed class TakerTick : MonoBehaviour
    {
        private void Start() => StartCoroutine(Loop());

        private IEnumerator Loop()
        {
            var wait = new WaitForSeconds(1f);   // scaled time, as the engines' own: it stops with the game
            while (true)
            {
                yield return wait;
                try { TakerTopUp.Tick(TrainController.Shared?.Cars, 1f); }
                catch (Exception x) { Util.WarnOnceWhole("taker-tick", "taker top-up tick failed (logged once; it keeps trying): ", x); }
            }
        }
    }
}
