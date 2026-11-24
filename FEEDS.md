# Aux Fuel Cars and Remote MU feed (AuxFuelCars.Api), Aux Fuel Cars and Remote MU 2.3.0

Aux Fuel Cars and Remote MU publishes a small read-only feed: `AuxFuelCars.Api` in `AuxFuelCars.dll`. From 2.0.9 it is also listed with Mod Feeds (the shared `ModFeeds.cs`, Revision 3) as `aux-fuel-cars`, with a description of every top-level key, so a mod that carries `ModFeeds.cs` finds it with `ModFeeds.List()` and reads it with `ModFeeds.Read("aux-fuel-cars")`. A mod without it can read it by reflection, with no reference and no load order, to show the aux cars (what they hold, which engine they feed) and the Remote MU helpers (which lead each follows, and whether it works the air). This file gives the ashenmare feed convention first, then this feed's schema.

## Ashenmare feed convention, v1

A feed is a `public static class` named `Api` in the mod's own namespace with these three members:

| Member | Meaning |
| --- | --- |
| `public const int Version` | The feed's schema version. Within a version, fields are only ever added. Renaming, retyping or dropping one raises the version, so a consumer checks it and reads only the versions it knows. |
| `public const string Feed` | The feed's stable id, in kebab case. It never changes. |
| `public static object Read()` | The data. `"api"` (int, equal to `Version`) and `"feed"` (string, equal to `Feed`) are always at the top level. |

To find feeds generically, scan the loaded assemblies (`AppDomain.CurrentDomain.GetAssemblies()`) for public static classes named `Api` with a literal `int Version`, a literal `string Feed` and a public static parameterless `Read()` returning `object`. Read the constants with `FieldInfo.GetRawConstantValue()`, and bind `Read` once with `Delegate.CreateDelegate(typeof(Func<object>), method)`.

The rules:
- **Plain data only.** `Read()` returns BCL types only (`Dictionary<string, object>`, `List<object>`, `string`, `int`, `float`, `bool`, `null`), never one of the mod's own types, so any JSON serializer can write it as is. Floats are always finite (or `null`).
- **camelCase keys.**
- **Main thread.** Call feeds on Unity's main thread (from a `MonoBehaviour`, or a game HTTP handler that runs there).
- **Never throws.** On a failure it returns `{ "api": <Version>, "feed": "<Feed>", "error": "<what went wrong>" }` and logs once in the mod's own log. Check for `"error"` first.
- **Read only.** Reading a feed changes nothing in the game.
- **Cheap to poll.** A feed works only when it's called (no timer, no Update loop, no Harmony patch of its own) and is light enough to read every couple of seconds.
- **Don't change the answers.** Copy what you want to keep or edit.

## The members of Aux Fuel Cars and Remote MU

```csharp
namespace AuxFuelCars
{
    public static class Api
    {
        public const int Version = 1;
        public const string Feed = "aux-fuel-cars";
        public static object Read();
    }
}
```

`Read()` builds a new dictionary on every call: one pass over the world's cars for the three aux car types (and, from 2.1.0, other packs' declared cars), and one short walk along the coupled cars for each Remote MU helper. In the main menu (no railroad loaded) `auxCars` and `helpers` are empty. 2.1.0 added the keys `lashUp` and `openCars` and the `kind` `"declared"`; 2.1.2 added `helperAir`; 2.2.0 added `fuelSpeeds`; 2.3.0 added `openTakers`, `takers`, and `feedsTakers` and `feedingTakers` on each aux car (cars that take fuel and water without being engines), and `openEngineTanks` and `engineTanks` (tanks other packs keep on an engine's keys); the schema version stays 1 (keys only added).

## Schema, version 1

Top level:

| Key | Type | Meaning |
| --- | --- | --- |
| `api` | int | `1` |
| `feed` | string | `"aux-fuel-cars"` |
| `modVersion` | string | The mod's version, for example `"2.2.1"`. |
| `host` | bool | This computer runs the railroad (single player, or the multiplayer host). The top-up runs only there, so on a client `feeds` is empty and `feeding` is `false` for every car. |
| `remoteMu` | bool | The Remote MU setting in force in this world (in multiplayer, the host's). |
| `rearAir` | bool | `true` unless the "Helper engines work the air" setting in force in this world is Off (in multiplayer, the host's). Before 2.1.2 the setting was on or off; `helperAir` has the choice. |
| `helperAir` | string | The "Helper engines work the air" setting in force in this world (in multiplayer, the host's): `"off"`, `"byHand"` (helpers work the air while the lead is driven by hand, and leave it alone while the Auto Engineer drives it) or `"always"` (under the Auto Engineer too; the default since 2.1.3). From 2.1.2. |
| `lashUp` | bool | The "Lash-up feed" setting in force in this world (in multiplayer, the host's; only the host tops up). On: a diesel's top-up passes over directly coupled diesel units. From 2.1.0. |
| `fuelSpeeds` | object | Faster Fueling's refuel speeds as the top-up follows them now (below). From 2.2.0. |
| `openCars` | object | Other packs' cars declared as aux cars with an `aux-fuel-cars.json` file, keyed by car definition id (empty when none). From 2.1.0. |
| `openTakers` | object | Other packs' cars declared as takers (cars that are not engines but take fuel or water from coupled aux cars), keyed by car definition id (empty when none). From 2.3.0. |
| `takers` | object | One entry per taker car in the world, keyed by its car id. From 2.3.0. |
| `openEngineTanks` | list | Other packs' engine tanks (a tank kept on a diesel's own keys that aux cars fill), in load order (empty when none). Each: `pack` (the declaring mod's identifier), `load` (for example `"rr/water"`), `levelKey`, `capacityKey`. From 2.3.0. |
| `engineTanks` | object | One entry per engine seen with a tank this session, keyed by its car id: `drew` (what it has drawn into the tank since this world loaded, keyed `"water"`, `"coal"` or `"diesel"`, in the load's units (gallons for water and diesel, pounds for coal); not saved) and `drawing` (it drew in the last 2.5 seconds; `false` on a client). From 2.3.0. |
| `rearAirHooked` | bool | The air part went in when the mod loaded. `false` on a game version where its hooks aren't found, or with Remote MU unavailable; helpers then never work the air. |
| `auxCars` | object | One entry per aux car in the world, keyed by its car id (the game's own `Car.id`, not the name shown on the car). |
| `helpers` | object | One entry per Remote MU helper, keyed by the helper's car id: an engine with MU on and its air cut out that follows a lead found past other cars (helpers the game's own MU already handles are not listed). |

Each `auxCars` entry:

| Key | Type | Meaning |
| --- | --- | --- |
| `kind` | string | `"water"` (Auxiliary Water Car), `"diesel"` (Auxiliary Diesel Car), `"tender"` (Extra Tender), or (2.1.0) `"declared"`: another pack's car declared in its `aux-fuel-cars.json` (see `openCars`). |
| `feedOn` | bool | The car's Feed box. `false`: it is never drawn from. |
| `loads` | object | What it carries, keyed `"water"`, `"coal"` or `"diesel"` (the water car: water; the diesel car: diesel; the Extra Tender: coal and water; a declared car: each of its slots for these three loads, whatever its declaration allows it to give). |
| `feeds` | list of string | The car ids of the engines whose top-up reached this car in the last 2.5 seconds (the top-up runs once a second): usually one; two when it sits between two engines; with the Lash-up feed, every unit of a diesel lash-up it reaches. Empty when it serves none, when the game is paused, and on a client. |
| `feeding` | bool | It gave fuel or water to an engine in the last 2.5 seconds. `false` while the engine is full. |
| `feedsTakers` | list of string | From 2.3.0. The car ids of the taker cars (see `takers`) whose top-up reached this car in the last 2.5 seconds. `feeds` and `feeding` stay the engines' own. |
| `feedingTakers` | bool | From 2.3.0. It gave fuel or water to a taker car in the last 2.5 seconds. |

Each `loads` entry:

| Key | Type | Meaning |
| --- | --- | --- |
| `quantity` | float | How much it holds now. |
| `capacity` | float | How much it can hold (the game's own capacity for that load in that car). |
| `units` | string | `"gal"` (water, diesel) or `"lb"` (coal), as the game stores the load; `"units"` if the game ever stores one of these loads in another unit. |

Each `helpers` entry:

| Key | Type | Meaning |
| --- | --- | --- |
| `leadId` | string | The car id of the engine it follows. |
| `position` | string | `"rear"`: nothing but engines and tenders is coupled beyond the helper on the side away from its lead (it pushes at the end). `"mid"`: other cars are coupled beyond it. |
| `workingAir` | bool | Its brake pipe valves work like the lead's now: Remote MU on and Helper engines work the air not Off, the air part in and not stood down, the helper MU'd and cut out, its brake cylinder following this lead, the lead's air cut in, both in one train, the brake pipe whole (no angle cock closed or hose off between them, no angle cock open anywhere on that pipe). While the Auto Engineer drives the lead: `false` with By hand only (2.1.2; the helper leaves the air alone then); with Always it stays `true` whatever the helper's main reservoir holds, as from 2.1.0: the helper then always vents with the lead and charges the pipe only while it has the air (2.0.10 reported `false` there under 100 psi). The air hook's own rule, asked when the feed is read, on this computer's air. The air itself only moves while the train's air is active. |

Each `takers` entry (2.3.0):

| Key | Type | Meaning |
| --- | --- | --- |
| `loads` | object | What it carries, the same shape as an aux car's `loads`: each of its slots for water, coal or diesel, with `quantity`, `capacity` and `units`. |
| `drew` | object | What it has drawn from aux cars since this world loaded, keyed `"water"`, `"coal"` or `"diesel"`, in the units of its `loads` (only the loads it has drawn). Not saved: it starts again at 0 at each world load. |
| `drawing` | bool | It drew something in the last 2.5 seconds. `false` while it is full, has no aux car coupled, and on a client. |

Each `openEngineTanks` entry (2.3.0):

| Key | Type | Meaning |
| --- | --- | --- |
| `pack` | string | The identifier of the mod whose `aux-fuel-cars.json` declared it. |
| `load` | string | The load id the tank takes (for example `"rr/water"`). |
| `levelKey` | string | The key on the engine's own data that holds the tank's level, in the load's units (gallons for water and diesel, pounds for coal). The host raises it. |
| `capacityKey` | string | The key on the engine's own data that holds the tank's capacity; 0 or absent means the engine has no tank. |

Each `openTakers` entry (2.3.0):

| Key | Type | Meaning |
| --- | --- | --- |
| `pack` | string | The identifier of the mod whose `aux-fuel-cars.json` declared it. |
| `loads` | list of string | The load ids it takes (for example `"rr/diesel-fuel"`, `"rr/water"`), in the order the file gave them. |

Each `openCars` entry (2.1.0):

| Key | Type | Meaning |
| --- | --- | --- |
| `pack` | string | The identifier of the mod whose `aux-fuel-cars.json` declared it (the first one loaded, when two declare it). |
| `loads` | list of string or null | The load ids it gives (for example `"rr/water"`), sorted; `null`: every load it has a slot for. Only loads it has a slot for are listed. |

The `fuelSpeeds` entry (2.2.0):

| Key | Type | Meaning |
| --- | --- | --- |
| `following` | bool | Faster Fueling's own feed (`faster-fueling`) says its speeds are active, so the top-up moves fuel at them. `false` when it is not installed, is switched off, or its answer can't be read. |
| `coal` | float | The multiple of the game's coal rate the top-up uses (Faster Fueling's `coalSpeed`); `1` when `following` is `false` or its value can't be read. |
| `water` | float | The same for water (`waterSpeed`). |
| `diesel` | float | The same for diesel (`dieselSpeed`). |

On a failure the answer is instead `{ "api": 1, "feed": "aux-fuel-cars", "error": "..." }`, where `error` is a string; called off the game's main thread, or before the mod has loaded, it answers that way too. The error text never carries a home folder or account name.

Example (a water car feeding its engine, a heater car and an engine's tank, and a pusher at the rear):

```json
{ "api": 1, "feed": "aux-fuel-cars", "modVersion": "2.3.0", "host": true, "remoteMu": true, "rearAir": true, "helperAir": "always", "lashUp": true,
  "fuelSpeeds": { "following": true, "coal": 2, "water": 2, "diesel": 1 },
  "openCars": { "battlestar/gtel-c": { "pack": "<its mod's id>", "loads": [ "rr/diesel-fuel", "rr/water" ] } },
  "openTakers": { "<heater car definition id>": { "pack": "<its mod's id>", "loads": [ "rr/diesel-fuel", "rr/water" ] } },
  "openEngineTanks": [ { "pack": "<its mod's id>", "load": "rr/water", "levelKey": "<level key>", "capacityKey": "<capacity key>" } ],
  "rearAirHooked": true,
  "auxCars": { "<water car id>": { "kind": "water", "feedOn": true,
               "loads": { "water": { "quantity": 9310.5, "capacity": 10000, "units": "gal" } },
               "feeds": [ "<lead id>" ], "feeding": true,
               "feedsTakers": [ "<heater car id>" ], "feedingTakers": true } },
  "takers": { "<heater car id>": { "loads": { "diesel": { "quantity": 240, "capacity": 500, "units": "gal" }, "water": { "quantity": 3100, "capacity": 4000, "units": "gal" } },
              "drew": { "water": 120 }, "drawing": true } },
  "engineTanks": { "<lead id>": { "drew": { "water": 80 }, "drawing": true } },
  "helpers": { "<helper id>": { "leadId": "<lead id>", "position": "rear", "workingAir": true } } }
```
