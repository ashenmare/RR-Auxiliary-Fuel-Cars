# Aux Fuel Cars and Remote MU

**Nexus Mods** https://www.nexusmods.com/railroader/mods/475

This is the source of Aux Fuel Cars and Remote MU 2.3.0. The ready-made zip is on the Nexus page.

More range for your engines: cars that carry extra fuel and water and keep the engine topped up. Plus remote MU:
a helper engine anywhere in the train follows the lead.

## The cars
Built on the game's own cars (same models, nothing new to download). Buy them in the Equipment window.
  Auxiliary Water Car   (AXW)  the 10,000 gal single dome tank car, carrying water          $1,350
  Auxiliary Diesel Car  (AXD)  the USRA 8,000 gal tank car, carrying diesel fuel           $960
  Extra Tender          (AXT)  the P-43 tender: 30,000 lb coal and 9,000 gal water         $5,000
The Extra Tender is priced by the game's own tender rule (what it charges for tender coal and water); the water
and diesel cars cost what the game's own tank cars do. They arrive empty: fill them like any tender. Paint them in the car's Customize window: the water and diesel
cars take the game's tank car schemes (Tank Car Black, Tank Car Silver), the Extra Tender the steam standard.
- Water car: under a water tower or column, with the middle of the car (the dome) under the spout.
- Diesel car: at a diesel stand, middle of the car at the stand, fully stopped (the stand is strict about it).
- Extra Tender: exactly like a P-43 tender (water hatch at the back, coal bunker at the front).
The diesel car can also buy its diesel at an interchange and deliver it to a diesel stand (see DIESEL BY WAYBILL).

DIESEL BY WAYBILL (new in 2.0.6)
The Auxiliary Diesel Car takes waybills like the game's own tank cars: its Operations tab has Automatic Waybills.
- Buy diesel at an interchange: set When Empty to the interchange's fuel dealer ("Diesel Fuel - Sylva Interchange to
  Carolina Fuel Co.", or Andrews, McClain, East Whittier) and leave the empty car on the interchange track. At the
  next interchange service it goes off the railroad and comes back full about a day later, the same car. Price as
  for any tank car: $0.27 a gallon of empty space plus $30 handling ($2,190 for an empty car), to Fuel. A part-full
  car pays only for what it takes.
- At service the dealer takes every car waybilled to it that is stopped anywhere on the interchange tracks, as it
  does any tank car: an aux diesel car in a train standing there is taken out of the train (the train is split, and
  a Remote MU helper behind it loses its lead and idles). Copy to Coupled and Cycle Waybill now include the diesel
  car, so check its waybill before you stop a train on the interchange. A car that is already full but still
  waybilled to the dealer goes too: no charge and no message, back full about a day later.
- Unload at a diesel stand: set When Loaded to an engine service's stand (Alarka, Andrews, Bryson, Dillsboro, East
  Whittier Diesel Stand) and spot the car anywhere on the stand's track: it unloads into the stand's storage (up to
  16,000 gal), and the stand pumps that into your diesels. A full car takes about 6 game hours; a stand that is
  nearly full takes only part, and the rest waits until your engines use some. No charge and no payment, as with a
  tank car.
- With both set, its waybill flips between the two by itself; you (or the Auto Engineer) still haul it. Feed still applies: a full car with Feed on tops up the
  diesel hauling it, so untick Feed to deliver every gallon.
- Only the diesel car: the water car and the Extra Tender take no waybills (no interchange sells water).
- No industry ever orders an aux diesel car as a revenue car; it only goes where you send it. The game now counts it
  as a freight car (the placer lists it under Freight, the Company window's load inventory counts its diesel), and so
  do other mods that look at freight cars: one that waits for every freight car in a train to be loaded or emptied
  waits on the diesel car too. The four dealers' interchange tables now read "TM, AXD", and the per-car price there
  averages the diesel car in (display only; the charge is always per gallon).

## What it does
Couple an aux car to an engine and it keeps that engine full: the game burns from the tender (or the tank, or the
diesel's own tank) as usual, and every second the aux cars put back what was burned. The game's own gauges stay
the truth: the tender reads full until the aux cars run dry.
- Steam with a tender: aux cars behind the tender.    loco - tender - aux - aux
- Tank engine: aux cars at its rear (bunker end).     tank engine - aux
- Diesel: aux cars at either end.                     aux - diesel - aux - aux
- Two engines back to back share an aux car between them:   loco - tender - aux - tender - loco
- Chains: as many as you like ("if you are crazy enough to need that much"). The farthest car is emptied first,
  so the empty one is always at the end of the chain, ready to drop.
- Only coupled joints count. A car that is just touching does nothing.
- The chain is the aux cars in a row. Anything else ends it: a boxcar, a caboose, another engine (except diesel
  units in a diesel's lash-up, below).
- Diesel lash-ups (new in 2.1.0, the Lash-up feed setting, on by default): diesel units coupled together pass the
  feed along, so every unit is topped up: loco - loco - aux tops up both locos, and in loco - aux - loco - aux the
  first loco reaches past the second. Only diesel units pass it: a boxcar, caboose, coach or steam engine between
  them still ends the chain. From 2.3.0 a diesel's chain also steps over an aux car that gives it nothing, such as a
  water car: loco - water car - diesel car tops up the loco. Lash-up feed off: only the unit coupled to an aux car is
  topped up, as before 2.1.0, and a car that gives it nothing ends the chain.
- Other packs' cars can be aux cars too (see OTHER PACKS' CARS), and cars that need fuel or water without being engines can take from them (see CARS THAT TAKE).
- Steam takes coal and water (an oil-fired steam engine, one whose tender holds diesel and no coal, takes diesel and water), diesels take diesel. A diesel car in a steam chain ends it, and the other way round.
- The Extra Tender is an extra: an engine built for a tender still needs its own tender, with the aux cars behind it.

## The feed checkbox
Each aux car has a Feed box in its Operations tab (car inspector), like the MoW work car's Enabled box. On (the
default): the car feeds. Off: it is never drawn from, so a car you are moving stays full, or a car parked as a
water tower isn't drained by every engine that couples to it by accident. An unticked car still passes the feed
along from cars farther down the chain, and towers and fuel stands still fill it. In multiplayer the box needs the
same crew access as any car control.

OTHER PACKS' CARS (new in 2.1.0)
Another mod can make its own cars work as aux cars. It puts a file named aux-fuel-cars.json at the top of its own
mod folder (beside its Info.json):
  { "auxFuelCars": 1, "cars": [ { "id": "battlestar/gtel-c", "loads": ["rr/water", "rr/diesel-fuel"] } ] }
- "id": the car's definition identifier (as in that mod's own definitions). Matched by id only, never by car type.
- "loads" (optional): the loads it gives. Left out, it gives every load it has a slot for, as this mod's own cars
  do. A name without a slash means the game's own load: "water" is rr/water, "diesel-fuel" rr/diesel-fuel, "coal"
  rr/coal. A name with a slash is used as written.
- "auxFuelCars": the file's format, 1. A file with a higher number is for a newer version of this mod: it is skipped.
Those cars then work exactly like this mod's: the chain, farthest car first, the Feed checkbox, the remote water
tower, lash-ups. A car with water and diesel feeds both a steam engine's tender and a diesel; a car limited to
water ends a diesel's chain when Lash-up feed is off, as this mod's water car does (with it on, the chain steps over it). The files are read when a save loads, only from mods
that are turned on; nothing in them is run. A file or a car that can't be used (not valid JSON, no id, a car that
isn't defined, an engine, a load it has no slot for) gets one line in Player.log, starting
"aux-fuel-cars.json in <mod>:", and is skipped; the rest still load. A car declared by two mods is taken from the
first one loaded. Declare only cars meant to be drawn from: a declared car that is also some engine's own tender
gives to any other engine coupled to it. Other mods can read the declared cars in the data feed (openCars).

CARS THAT TAKE (new in 2.3.0)
A car that is not an engine but needs fuel or water, such as a heater car, can be declared by its own mod as a taker, in
the same aux-fuel-cars.json, in a "takers" list:
  { "auxFuelCars": 1, "cars": [], "takers": [ { "id": "somemod/heater-car", "loads": ["diesel-fuel", "water"], "capacity": "slot" } ] }
- "id": the car's definition identifier. Never an engine (engines are topped up as before) and never a car the same or
  another mod declares as an aux car: a car gives or takes.
- "loads" (optional): the loads it takes. Left out, every load it has a slot for that has a refill speed (water, coal,
  diesel-fuel). Names as for aux cars.
- "capacity" (optional): "slot", the default and the only choice. It takes up to the room in its own slot for that load.
- Keep "cars": [] in a file with no aux cars: older versions of this mod want the list and log one line without it.

A taker is topped up like an engine: aux cars coupled to either end of it fill it, farthest car first, at the same
speeds (water 40 gal, coal 127 lb, diesel 3 gal a second, times Faster Fueling's if installed), only from cars that
give that load and have their Feed box on. Under the Lash-up feed setting (on by default) a taker counts as a diesel
unit: a diesel's chain passes over takers (diesel - heater - aux feeds the diesel too, and diesel - diesel - heater -
heater - aux feeds all four), and a taker's chain passes over other takers and over diesel units, as a diesel's does
(heater - heater - aux fills both heaters; heater - diesel - aux fills the heater through the diesel; a water car
reached this way fills the heater's water). A car that is no aux car still ends the walk. A steam engine's chain has no lash-up, so it still ends at a taker: steam loco - tender - heater - aux does
not feed the engine. With Lash-up feed off: a taker needs an aux car coupled beside it, and an engine's chain ends at a
taker. It runs once a second, only for the
host, and only when a loaded mod declares a taker; with none, this mod does no extra work. Problems in the file get
one Player.log line each ("aux-fuel-cars.json in <mod>:") and that entry is skipped. The data feed lists them
(openTakers, takers, and feedsTakers on each aux car).

ENGINE TANKS (new in 2.3.0)
A mod can also keep a tank on a diesel engine itself (for example the water of a steam generator) and let aux cars fill it. It says so in the same
file, in an "engineTanks" list:
  "engineTanks": [ { "load": "water", "levelKey": "somemod.tank-level", "capacityKey": "somemod.tank-capacity" } ]
- "load": the load the tank takes (water, coal or diesel-fuel; names as above).
- "capacityKey" and "levelKey": keys on the engine's own data, both plain numbers in the load's units (gallons for water and diesel,
  pounds for coal). A key is up to 64 characters of letters, digits, dot, underscore and hyphen; anything else skips the entry with one line.
  A capacity of 0 or none: the engine has no tank. No level: the tank is full, nothing is drawn and nothing is written.
A diesel with room in such a tank draws from the aux cars of its own chain, farthest car first, at the same speeds, only from cars that give that load and
have their Feed box on, under the same Lash-up feed rules as its fuel, and the host raises the level, never above the capacity. Only the host writes.
With none declared this mod does no extra work. A bad entry gets one Player.log line ("aux-fuel-cars.json in <mod>:") and is skipped. The data feed lists them
(openEngineTanks, engineTanks).

## Remote water tower
Park a full aux car on a siding. Any engine whose tender (or tank engine rear, or diesel) couples to it fills from
it at the game's own tower speed: 40 gal of water and 127 lb of coal a second (diesel 3 gal a second, as the fuel
stand). The same speed limit applies while running, but no engine burns anywhere near that fast, so on the road
the tender simply stays full. The speed only shows when a low tender meets a full aux car.
With Faster Fueling installed, its coal, water and diesel speeds multiply these (the host's, in multiplayer).

## Remote mu
The game's MU only works between engines coupled together. With this mod, an MU'd helper also follows the lead
from elsewhere in the train: mid-train, or pushing at the rear, with cars in between.
Set it up the game's usual way:
- The lead engine: as normal (air cut in). Run it by hand or with the Auto Engineer.
- Each helper: MU on, air cut out (the "MU" and "Cut Out" boxes in its car inspector).
- Every joint between them coupled with the air hoses connected (the air line is the "radio").
The helper copies the lead's throttle and the direction (a helper facing backwards reverses correctly), and its
independent brake follows the lead's, as MU already does. The Auto Engineer counts the helper's pull, so expect it
to accelerate harder. Rules from the game's own MU, unchanged: only an engine with its air cut in can lead, so
helpers can't follow each other; two cut-in leads in one train is not supported (the Auto Engineer logs "MU'd but
not cut out"); a closed angle cock is not checked, only the hoses. Wherever the game's own MU already finds a lead, it is
left exactly as it was. With two engines cut in, a helper follows the first one it finds (looking from its front end
first), and works the air with that one.
If a helper loses the lead (a hose or coupler between them broken, cars set out between, the lead's air cut out, or
Remote MU turned off), it drops to idle within a second. Its air is cut out, so it has no brakes of its own: cut its air
back in (or set its hand brake) to stop it. Cutting its air in or unticking MU hands it back to you, and
the mod leaves its throttle alone.
An engine you only haul follows the lead too if its MU box is on and its air cut out. New engines come with MU off;
if you tick it, untick MU on any engine you're just moving in a train.
In multiplayer the host's Remote MU setting rules for everyone (see SETTINGS).

HELPER ENGINES WORK THE AIR (new in 2.0.8)
A remote helper, mid-train or at the rear, also works the brake pipe from its end, as the lead's brake valve does at
the front: it charges the pipe from its own main reservoir and compressor, and applies and releases with the lead's
automatic brake handle. An emergency application dumps the pipe at the helper too.
- The helper stays cut out (MU needs that). Only its brake pipe valves follow the lead; its own brake handle is
  ignored, and its brake cylinder follows the lead's as before.
- Both valves aim at the same pressure on every physics step (the lead's), so they don't fight.
- Only on a whole brake pipe (2.0.9). If an angle cock is closed or a hose is off anywhere between the helper and
  the lead, or an angle cock is open anywhere on the pipe (at either end of the train, or behind a caboose or tender
  coupled beyond the helper), the helper leaves the air alone, as a cut-out engine does in the game. So a cut you hold
  with closed angle cocks stays applied when the lead releases, and an open angle cock dumps the air as it does
  without the mod. Its throttle still follows the lead (Remote MU only needs the hoses on).
- With the Auto Engineer on the lead, it depends on the setting (three choices from 2.1.2):
  - By hand only: while the AE drives the lead, helpers leave the air alone, as a cut-out engine does in the game,
    so the AE plans its braking on the train's air as it is without this mod. Switch the AE on or off, even in the
    middle of an application, and the helpers hand over on the next physics step.
  - Always (the default since 2.1.3): helpers work the air under the AE too. The AE plans its braking on a copy of the train's air; in that
    copy each helper that can charge the pipe is joined to the lead's brake pipe, so the AE expects the rear to apply
    and release from the helper's end, and its main reservoir is joined to the lead's, so the copy has the helper's
    air behind it. The train itself is unchanged: each helper still charges the pipe from its own main reservoir.
    While the AE drives, a helper always applies with the lead (venting the pipe needs no air of its own), and it
    charges the pipe only while its main reservoir has the air for it: from the lead's feed valve plus 10 psi (100
    psi with the usual 90) until it falls under the feed valve (90). A helper out of fuel and low on air still
    applies the rear but leaves the charging to the lead. If the AE overspeeds or hunts with a helper on your train,
    switch to By hand only.
  With the AE off a helper works the air whatever its reservoir holds, with By hand only and Always alike.
- The moment the link breaks (a hose or coupler parted, cars set out between them, MU unticked, the lead's air cut
  out, or either setting off) it is a plain cut-out engine again: it neither feeds nor vents the pipe. Cutting its
  air in makes it an ordinary engine with its own brake valve, as in the game.
  A train that parts dumps its air as it does without the mod.
- Only remote helpers (cars between them and the lead). Engines coupled together in an ordinary MU lash-up are the
  game's own MU and unchanged.
- Runs on every computer from the same inputs; the host's air is the truth, as the game already does it.

## Settings
In game: Preferences > Mods > Aux Fuel Cars and Remote MU (from the main menu or the Esc menu).
- Remote MU, default on. Off: the game's own MU only. Save applies it at once; a helper that was following from
  elsewhere in the train drops to idle.
- Helper engines work the air, default Always (works only with Remote MU on). Off: a helper's brake pipe stays
  cut out, as in the game. By hand only: helpers work the air while you drive the lead, and leave it alone while the
  Auto Engineer drives it. Always: under the Auto Engineer too (see HELPER ENGINES WORK THE AIR). Save applies it
  at once. A choice already saved is kept: 2.1.2 saved By hand only for a fresh install, so pick Always in
  Preferences > Mods to get the new default. Updating from 2.1.1 or earlier: a setting that was on becomes By hand
  only and one that was off stays Off; settings.json gets the new value once and keeps the old one, unread.
- Lash-up feed, default on. Off: in a diesel lash-up only the unit coupled to an aux car is topped up. Only the
  host tops up, so in multiplayer only the host's value acts.
- Debug log (for bug reports), default off: writes the details described under SEEING IT WORK. One switch for every mod of ours that has it, on this computer only.
- In multiplayer the host's settings rule: the host's choices go to every player (and to anyone who joins later),
  so everyone's helpers and brake gauges agree. On a player who joined, the section shows the host's setting and
  can't be changed; their own setting applies when they host or play alone.
- The section is saved in settings.json. If a value in the file can't be read (for example a decimal in a
  whole-number setting, or text in a number), the mod starts on its default settings until the file is fixed or Save
  writes it right. If settings.json can't be read at all, the mod keeps what it started with and the file is left
  alone until it is fixed (Check again) or the game restarts. A change made to the file while the game runs is shown
  in the section, but the mod keeps what it had until you Save there or restart. Numbers can be typed as 1000, 1,000
  or 1 000, and 12.5 or 12,5 where decimals are allowed; a form that reads two ways (1.000, 0,125) is refused, so
  type it as 1 or 1000 instead. Fields marked "after restart" are read once at load.
- The file is written with the default the first time the mod loads, and updates never overwrite it (it isn't in the zip).
The cars themselves have no settings.

## Install
1. Unzip so you have an AuxFuelCars folder in  Documents\Railroader\Mods\
   (Linux/Proton: the same folder inside the game's Proton prefix).
   Inside should be Info.json, AuxFuelCars.dll, 0Harmony.dll, Scripts\Data.rl and the text files.
2. Preferences > Features > Experimental > Allow DLL Mods, then restart the game.
3. Main menu > Mods > Manage Mods > tick Aux Fuel Cars and Remote MU > Apply.
The first time you load a save made before you installed it, the game asks "This Save Uses Different Mods":
choose "Keep My Current Mods" so Aux Fuel Cars and Remote MU stays on for that load.

## Multiplayer
Everyone needs it, at the same version. The cars are new car definitions, and each computer builds its own from its
own mods, so the game treats this mod as required: a player without it, or with a different version, gets "This
server requires a mod you don't have" and can't join until they install the host's version. The top-up runs on the
host only and the game's own sync sends the fuel levels to everyone. Remote MU runs on every computer and follows
the host's setting.

WHY "REQUIRED"
The game has three mod kinds: required (Info.json scope "unscoped"), optional and client. This one adds cars that
every player has to see, so it is required. What that means:
- Joining: every player must have it installed, at the host's exact version (see MULTIPLAYER).
- Saves remember it. A save loaded without it, or with a different version installed, asks first ("Mod Not
  Available"): after an update choose the "Use" button with the version you now have and the save carries on with it.
  "Load Without It" drops the aux cars (see below).

## Removing it safely
1. Bring any remote MU helper to a stand at idle first (the same before saving a world you'll load with Remote MU
   off): a saved helper keeps its throttle, and without the mod nothing idles it.
   Drain or sell your aux cars (set them for sale in the Equipment tab of the car inspector, or just accept losing
   them), then save.
2. Untick it in Manage Mods (or delete its folder).
3. Load the save. Because the save used the mod, the game asks: "This Save Uses Different Mods" (choose "Keep My
   Current Mods") or, if you deleted the folder, "Mod Not Available" (choose "Load Without It"). Choosing the
   save's mods instead turns it back on for that save.
What happens to cars still in the save: when it loads without the mod, the game drops each aux car with its own
message ("could not be loaded ... The car will be missing") and loads everything else; trains are split around
where they were. The cars, and the fuel and water in them, are gone for good at the next save. Nothing else is
lost: tenders and engines keep what they hold, and the Feed setting lived only on the aux cars. Remote helpers
stop following the lead when there are cars between them.

FROM 1.0.x (BEFORE 0.6)
1.0.x was a Unity Mod Manager mod for the pre-0.6 game. Its car packs (the extra-tenders-dt and extra-tenders-lt
folders it wrote to the game's AssetPacks folder in AppData\LocalLow) are not read by 0.6 at all; you can delete
them by hand. This version makes its cars from the game's own definitions instead.

## Seeing it work
Player.log, at startup, has one line from the mod's data script (the map line with the interchanges and diesel stands, and a
warning if a map has none) and one line from the program (the log lines still start with the old
name, Auxiliary Fuel Cars):
  Auxiliary Fuel Cars: 2.3.0 loaded: steam top-up on, diesel top-up on, remote MU on, helper engines' air on (Remote
  MU setting: on, helper engines work the air: on, lash-up feed: on)
Saving the settings adds one "settings saved" line. Nothing else from the mod while you play: warnings and errors only.
The details (the world's Remote MU setting at each load, the aux cars defined, other packs' cars, an engine that lost its
lead and went to idle, and the car types each interchange diesel loader and diesel stand ended up with) are in the debug log: Preferences > Mods > Aux Fuel Cars and Remote MU > "Debug log (for bug
reports)" writes Documents\Railroader\Logs\ashenmare-debug-<date>-<time>.log, one file shared by every mod of ours that
has the switch. Send that file with a bug report.

## For other mods
A read-only data feed, "aux-fuel-cars", listed with Mod Feeds: each aux car (what it holds, which engine it feeds) and
each remote helper (its lead, rear or mid, whether it works the air now), other packs' declared cars, and the cars that take from aux cars (what each has drawn). FEEDS.md in
this folder has the keys.

## If the game changes
If an update renames what a part hooks, or moves or removes a type the mod uses, that part logs one warning and
stays off, and the rest keep working. If an update changes how the game runs a cut-out engine's brake valves, helper
engines stop working the air until the next load, with one line in the log. The cars stay ordinary cars you can
still fill and move; MU stays the game's own. A map with no interchange diesel loader logs a warning, and the diesel car simply
has fewer places to go.

## Changes
2.3.0: Added engine tanks: another mod can declare a tank kept on a diesel's own data (a steam generator's water) for aux cars to fill (ENGINE TANKS).
  Added takers: another mod's cars that are not engines but need fuel or water (a steam heater car) take it from coupled aux cars (CARS THAT TAKE).
  Changed Lash-up feed (on by default): a diesel's chain now steps over an aux car that gives it nothing, such as a water car, so a loco followed
  by a water car and a diesel car fills the loco. This changes feeding for everyone with this mod alone. With the setting off the chain ends at
  such a car as before.
  2.2.2 and 2.2.3 below were not posted on their own and ship in 2.3.0.
2.2.3: Fixed: an oil-fired steam engine (Oil Conversion) now takes diesel from an Auxiliary Diesel Car behind its tender.
2.2.2: Fixed extra lines in Player.log. Nothing during play and one data script line at startup; the debug log has the details.
2.2.1: For Railroader 0.6.3: the paint schemes for the three cars work again.
2.2.0: Follows Faster Fueling's refuel speeds when it is installed. Updated settings menu (searchable).
2.1.5: Finds the interchange diesel loaders and the diesel stands by rule instead of by name, so an interchange
  that a map mod adds, renames or recreates (Tetz's Beta Yard) gets the diesel car too. The stock map is unchanged: the
  same four loaders and five stands. The mod now loads after other mods (load priority 100, and after Tetz's Beta Main
  and Yard and Kater's Sylva Interchange when they are installed), so their changes to the map are in place first.
  A map mod with a higher load priority than 100 still loads after this one and would not be seen.
2.1.4: The in-game name is Aux Fuel Cars and Remote MU, as on Nexus. Nothing else changed.
2.1.3: Helper engines work the air now defaults to Always: the Auto Engineer plans its braking with the helpers' air
  (tested in game: helpers charge the pipe with the lead under the AE; Off and By hand only also tested). A choice
  already saved, and one carried over from an older setting, is kept; only a missing one is Always.
2.1.2: Helper engines work the air has three choices: Off, By hand only and Always. By hand only:
  helpers work the air while you drive the lead and leave it alone while the Auto Engineer drives (as in 2.0.9);
  Always is 2.1.1's behavior. A setting that was on becomes By hand only; one that was off stays Off. The data feed
  adds helperAir.
2.1.1: The Auto Engineer's copy of the train's air now has the helpers' main reservoirs in it. A malformed
  aux-fuel-cars.json in one mod no longer drops every mod's declared cars, and a car id shaped like a date is read as
  an id. (2.1.0 was built but never posted; its changes ship here.)
2.1.0: Lash-up feed: diesel units coupled together all draw from the aux cars (a setting, on by default). Other mods
  can declare their own cars as aux cars (see OTHER PACKS' CARS). Under the Auto Engineer a helper now always applies
  with the lead and charges the pipe only while it has the air; this replaces 2.0.10's rule, which could switch a
  helper off and on inside the AE's look-ahead. The data feed lists the declared cars and the Lash-up feed setting.
  (2.0.7 to 2.0.10 were built but never posted; their changes ship here.)
2.0.10: helper engines work the air under the Auto Engineer too, and the AE plans its braking with them (a helper
  without enough air of its own still leaves the air alone while the AE drives; see HELPER ENGINES WORK THE AIR).
  A diesel loader or stand whose car type filter is blank (spaces only) on a modded map keeps its tank cars when the
  diesel car is added. If the air part stands down after a game update, it lets go of every train at once, and the
  log says so again in the next world.
2.0.9: helper engines work the air only on a whole brake pipe (no closed angle cock or loose hose between the helper
  and the lead, no open angle cock on the pipe), and not while the Auto Engineer drives the lead (see HELPER ENGINES
  WORK THE AIR). The setting is now called Helper engines work the air. The data feed is listed with Mod Feeds.
2.0.8: Remote MU helpers work the brake pipe like the lead, with a setting to turn it off. A data feed for other
  mods (FEEDS.md).
2.0.7: Settings menu updated (labels wrap, shorter texts). American English. Shorter in-game texts and Nexus
  descriptions; the detail is in this file. No change to how the cars or remote MU work.
2.0.6: diesel by waybill: the Auxiliary Diesel Car buys diesel at an interchange's fuel dealer and delivers it to
  an engine service's diesel stand (see DIESEL BY WAYBILL). The game now counts it as a freight car.
  Privacy: cleaned the mod's files and error lines.
  If a later game version moves or removes a type the mod uses, that part now says so in the log and stays off,
  instead of the mod failing at load. The top-up and remote MU work as in 2.0.4.
  (2.0.5 was built but never posted; its diesel by waybill ships here.)
2.0.4: first release for Railroader 0.6.

## Building

Needs the .NET SDK (8 or newer) and a Railroader install for the game's dlls. Point the build at the game's
Managed folder (`Railroader_Data/Managed`) with a property or an environment variable.

```
dotnet build AuxFuelCars.csproj -c Release -p:ManagedPath="<game folder>/Railroader_Data/Managed"
```

or set `RAILROADER_MANAGED` to that folder once and run `dotnet build AuxFuelCars.csproj -c Release`. The build stops with a
message if the folder is missing. NuGet brings in Harmony and the .NET Framework reference assemblies.

The output is `bin/Release/AuxFuelCars.dll` and `bin/Release/0Harmony.dll`. To try it, put both and the `Scripts` folder beside `Info.json`
in a folder named `AuxFuelCars` under `Documents/Railroader/Mods`, and turn on Allow DLL Mods in Preferences.

## License

MIT, see LICENSE.
The release zip includes Harmony (MIT), see THIRD-PARTY-NOTICES.txt.

Making a Railroader mod? Use our settings menu in yours: https://github.com/ashenmare/railroader-mod-settings
Made with AI assistance (Claude), every release reviewed and audited in game by a human.
