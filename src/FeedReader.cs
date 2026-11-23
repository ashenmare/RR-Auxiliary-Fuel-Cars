using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.State;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Physics;

namespace AuxFuelCars
{
    // The feed's game side (Api.Read, through its NoInlining ReadFeed). Main thread only (Api checks). Read only: it
    // reads cars, loads and this mod's own records; it writes nothing and sends nothing. Cheap: one pass over the
    // world's cars for the three aux car ids, and one short coupled walk per Remote MU helper.
    internal static class FeedReader
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Read()
        {
            var aux = new Dictionary<string, object>();
            var helpers = new Dictionary<string, object>();
            bool remoteOn = HostSetting.RemoteMu, hooked = Main.RearHooked;
            HelperAir air = HostSetting.HelperAir;
            TrainController tc = TrainController.Shared;
            if (tc != null)
            {
                foreach (Car car in tc.Cars)
                {
                    string def = car?.DefinitionInfo?.Identifier;
                    if (def == null || !(Ids.All.Contains(def) || OpenCars.Has(def)) || car.id == null) continue;
                    aux[car.id] = AuxCar(car, def);
                }
                foreach (KeyValuePair<string, RemoteMuPatch.Link> link in RemoteMuPatch.Links())
                {
                    if (link.Key == null || !tc.TryGetCarForId(link.Key, out Car car) || !(car is BaseLocomotive helper)) continue;
                    helpers[link.Key] = Helper(helper, link.Value, remoteOn, air, hooked);
                }
            }
            return new Dictionary<string, object>
            {
                { "api", Api.Version },
                { "feed", Api.Feed },
                { "modVersion", Main.Version },
                { "host", tc != null && StateManager.IsHost },
                { "remoteMu", remoteOn },
                { "rearAir", Logic.AirOn(air) },
                { "helperAir", FeedName(air) },
                { "rearAirHooked", hooked },
                { "lashUp", HostSetting.LashUp },
                { "openCars", Declared() },
                { "fuelSpeeds", Speeds() },
                { "auxCars", aux },
                { "helpers", helpers },
            };
        }

        // 2.1.2: the helperAir key's text for each choice (FEEDS.md).
        internal static string FeedName(HelperAir air) => air == HelperAir.Off ? "off" : air == HelperAir.Always ? "always" : "byHand";

        // 2.2.0: Faster Fueling's refuel speeds, as the top-up follows them now (FasterFueling; 1 for a load it doesn't speed up).
        private static Dictionary<string, object> Speeds()
        {
            FasterFueling.Current(out bool following, out float coal, out float water, out float diesel);
            return new Dictionary<string, object>
            {
                { "following", following },
                { "coal", Finite(coal) },
                { "water", Finite(water) },
                { "diesel", Finite(diesel) },
            };
        }

        // 2.1.0: other packs' declared cars (OpenCars), by definition id.
        private static Dictionary<string, object> Declared()
        {
            var all = new Dictionary<string, object>();
            foreach (KeyValuePair<string, OpenCars.Entry> kv in OpenCars.Cars)
            {
                List<object> loads = null;
                if (kv.Value.Loads != null)
                {
                    var ids = new List<string>(kv.Value.Loads);
                    ids.Sort(StringComparer.Ordinal);
                    loads = new List<object>(ids);
                }
                all[kv.Key] = new Dictionary<string, object> { { "pack", kv.Value.Pack }, { "loads", loads } };
            }
            return all;
        }

        private static Dictionary<string, object> AuxCar(Car car, string def)
        {
            var loads = new Dictionary<string, object>();
            var slots = car.Definition?.LoadSlots;
            if (slots != null)
                for (int i = 0; i < slots.Count; i++)
                {
                    LoadSlot slot = slots[i];
                    string name = LoadName(slot?.RequiredLoadIdentifier);
                    if (name == null || loads.ContainsKey(name)) continue;
                    CarLoadInfo? info = car.GetLoadInfo(i);
                    float have = info.HasValue && info.Value.LoadId == slot.RequiredLoadIdentifier ? info.Value.Quantity : 0f;
                    loads[name] = new Dictionary<string, object>
                    {
                        { "quantity", Finite(have) },
                        { "capacity", Finite(slot.CalculateMaximumCapacity(slot.RequiredLoadIdentifier, car)) },
                        { "units", Units(slot.LoadUnits) },
                    };
                }
            List<object> feeds = FeedLog.Feeds(car.id, out bool feeding);
            return new Dictionary<string, object>
            {
                { "kind", def == Ids.WaterCar ? "water" : def == Ids.DieselCar ? "diesel" : def == Ids.ExtraTender ? "tender" : "declared" },
                { "feedOn", Feed.IsOn(car) },
                { "loads", loads },
                { "feeds", feeds },
                { "feeding", feeding },
            };
        }

        private static Dictionary<string, object> Helper(BaseLocomotive helper, RemoteMuPatch.Link link, bool remoteOn, HelperAir air, bool hooked)
        {
            // Rear or mid: anything but an engine or a tender coupled beyond the helper, away from its lead.
            Car prev = helper;
            Car cur = helper.CoupledTo(helper.EndToLogical(link.LeadTowardF ? Car.End.R : Car.End.F));
            bool other = false;
            for (int n = 0; n < 2000 && cur != null; n++)
            {
                if (!(cur.IsLocomotive || cur.CarType.IsTender)) { other = true; break; }
                Car next = Next(cur, prev);
                prev = cur;
                cur = next;
            }
            // Working the air: the air hook's own rule (RearAirMembers.Works), asked now on this
            // computer's air graph for the helper's train: the brake pipe whole, (with By hand only) the Auto
            // Engineer not driving the lead, and false while the air part stands down. A train being rebuilt between two air steps can't be read; it
            // reports false until the next step.
            bool works = false;
            if (hooked)
            {
                try { works = RearAirMembers.WorksNow(helper, remoteOn, air); }
                catch (Exception) { works = false; }
            }
            return new Dictionary<string, object>
            {
                { "leadId", link.LeadId },
                { "position", Logic.Position(other) },
                { "workingAir", works },
            };
        }

        private static Car Next(Car car, Car prev)
        {
            Car a = car.CoupledTo(Car.LogicalEnd.A);
            Car b = car.CoupledTo(Car.LogicalEnd.B);
            if (ReferenceEquals(a, prev)) return b;
            if (ReferenceEquals(b, prev)) return a;
            return null;
        }

        private static string LoadName(string loadId)
        {
            switch (loadId)
            {
                case Logic.Water: return "water";
                case Logic.Coal: return "coal";
                case Logic.Diesel: return "diesel";
                default: return null;
            }
        }

        private static string Units(LoadUnits u) => u == LoadUnits.Pounds ? "lb" : u == LoadUnits.Gallons ? "gal" : "units";

        private static object Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? null : (object)v;
    }
}
