// Aux Fuel Cars and Remote MU 2.3.0: the three cars, cloned from the game's own definitions with modifyAs, and (modifyMap, at
// the end) diesel by waybill for the Auxiliary Diesel Car at the interchanges and the diesel stands. No bundles and no
// model edits: a clone is filed under the pack that owns its base, so "./tankcar01", "./tankcar02" and
// "./lt-462-p43a" resolve to the stock models (AssetPackManager.LoadDefinitions, 0.6.2). Info.json requires the three
// base packs, because Data.rl only sees definitions from packs ordered before it.
//
// Car types: a fresh "AX" prefix. Not "LT" (a tender: drawbar at its front, never sold, grabbed by any tender engine
// as its tender) and not a type any industry's car type filter matches: an order picks its cars by the ordering
// component's filter (PrefabStoreExtensions.Random), so a "TM" aux car would turn up as someone's revenue car. AX is
// none of those: sold at its own price, placeable, never ordered. From 2.0.5 the DLL registers "AXD" (only) as a
// freight car type so the diesel car can be waybilled; being freight is not what gets a car ordered.
//
// The DLL finds these cars by their identifiers (src/Ids.cs), never by car type. Slot order is frozen from 2.0.0:
// saved loads are keyed by slot index, so reordering slots later would move fuel between slots in old saves.
//
// The Extra Tender's price uses the game's own tender price rule (EquipmentPricing.Capacity: $200 x (thousand
// gallons of water)^1.3, plus $100 per ton of coal), so its range costs what a tender's does. The water and diesel
// cars keep their base tank cars' own prices ($1,350 and $960 list: the same price as the game's own tank cars).
//   Extra Tender  30,000 lb coal + 9,000 gal water   -> 4,980 -> $5,000
//   Water car: not set, so it keeps its base tank car's own price ($1,350 list, the same as the game's tank cars)
//   Diesel car: not set, so it keeps its base tank car's own price ($960 list; same rule)

import log

// Must match src/Ids.cs (tools/probe checks it).
let waterId = "ashenmare/aux-water-car"
let dieselId = "ashenmare/aux-diesel-car"
let tenderId = "ashenmare/aux-extra-tender"
// The game's schemes for the base cars (pack rr/car-styles, 0.6.3), cloned for ours: [base, new id, our cars].
let styleClones = [
    ["rr/style-tank-black", "ashenmare/style-aux-tank-black", [waterId, dieselId]],
    ["rr/style-tank-silver", "ashenmare/style-aux-tank-silver", [waterId, dieselId]],
    ["rr/style-alw-steam", "ashenmare/style-aux-tender-alw", [tenderId]]
]

// A LoadTarget mid-car on top of the tank, where the dome is. The towers and the fuel stand fill a car whose target
// is within (target radius + 0.2 m) of their spout, measured flat (CarLoadTargetLoader.LoadSlotFromCar); the stock
// tank cars have no target at all, so without this they could not be filled. 1.0 m is the GP9's own radius.
func tankTarget() {
    return {
        "kind": "LoadTarget",
        "radius": 1.0,
        "slotIndex": 0,
        "name": "Aux Fuel LoadTarget",
        "transform": { "position": [0.0, 2.9, 0.0], "rotation": [0.0, 0.0, 0.0, 1.0], "scale": [1.0, 1.0, 1.0] },
        "enabled": true
    }
}

func modifyDefinitions(context) {
    // Auxiliary Water Car: the 10,000 gal single dome tank car (rr/tm-tankcar01), its one slot locked to water.
    context.modifyAs("rr/tm-tankcar01", waterId, (obj) => {
        obj["metadata"]["name"] = "Auxiliary Water Car"
        obj["metadata"]["description"] = "10,000 gal water car. Tops up a coupled tender or tank engine."
        obj["definition"]["carType"] = "AXW"
        obj["definition"]["builderPhoto"] = "rr/tm-tankcar01--bp0"
        obj["definition"]["loadSlots"] = [{ "maximumCapacity": 10000.0, "loadUnits": "Gallons", "requiredLoadIdentifier": "rr/water" }]
        obj["definition"]["components"]["auxfuel-loadtarget"] = tankTarget()
    })

    // Auxiliary Diesel Car: the USRA 8,000 gal tank car (rr/tm-tankcar02), its one slot locked to diesel.
    context.modifyAs("rr/tm-tankcar02", dieselId, (obj) => {
        obj["metadata"]["name"] = "Auxiliary Diesel Car"
        obj["metadata"]["description"] = "8,000 gal diesel fuel car. Tops up a coupled diesel locomotive."
        obj["definition"]["carType"] = "AXD"
        obj["definition"]["builderPhoto"] = "rr/tm-tankcar02--bp0"
        obj["definition"]["loadSlots"] = [{ "maximumCapacity": 8000.0, "loadUnits": "Gallons", "requiredLoadIdentifier": "rr/diesel-fuel" }]
        obj["definition"]["components"]["auxfuel-loadtarget"] = tankTarget()
    })

    // Extra Tender: the P-43 tender (rr/lt-462-p43) as an ordinary car. Its slots (coal 30,000 lb, water 9,000 gal),
    // load targets and coal/water animations are kept as they are. A tender's price is worked out from its capacity
    // and its authored price ignored; off "LT" the authored price is the price, so it is set here.
    context.modifyAs("rr/lt-462-p43", tenderId, (obj) => {
        obj["metadata"]["name"] = "Extra Tender"
        obj["metadata"]["description"] = "P-43 tender body run as an auxiliary coal and water car."
        obj["definition"]["carType"] = "AXT"
        obj["definition"]["basePrice"] = 5000
        obj["definition"]["builderPhoto"] = "rr/ls-462-p43--lt-462-p43"
    })

    // Paint schemes (2.0.4: the aux diesel car wasn't color customizable). The Customize window offers paint
    // and colors only when a CarStyle applies to the car, by carTypeFilter or by naming it in carIdentifiers
    // (CarStyleSelector.OfferedStyles; CarCustomizeWindow.BuildSectionStyle). The game's tank car schemes match "T*"
    // and its tender scheme "LT*"; ours are AXW, AXD and AXT, so none applied. Clone them (pack rr/car-styles, required
    // in Info.json) for our cars by id, each one's styles unchanged.
    // 2.2.1 (0.6.3): no existence guard. Relay's getDefinition became the internal _getDefinition, and modifyAs is the public
    // way in; it throws if a scheme is missing (rr/car-styles is required, and has all three), which the message log shows.
    for sb in styleClones {
        let ids = sb[2]
        context.modifyAs(sb[0], sb[1], (obj) => {
            obj["definition"]["carTypeFilter"] = ""
            obj["definition"]["carIdentifiers"] = ids
        })
    }
}

// 2.0.5: diesel by waybill for the Auxiliary Diesel Car (as the 1.x cars took interchange waybills; and waybilled to
// the diesel stands to unload). Two kinds of map component get the AXD car type added; from 2.1.5 they are found by rule
// on whatever map is loaded (the stock map has nine: four loaders, five stands; a map mod's own interchange or stand
// counts too, as Weed Sprayer finds interchanges):
//   - the interchange diesel loaders (InterchangedIndustryLoader, rr/diesel-fuel): an empty AXD waybilled there goes
//     off the map and comes back full, charged the load's price plus the game's $30 handling, to Fuel;
//   - the engine-service diesel stands' unloaders (IndustryUnloader, rr/diesel-fuel): a full AXD waybilled there
//     unloads into the stand's storage, which the fuel stand then pumps into engines. No charge and no payment.
// A component's car types are its authored "carTypeFilter" if it has one, else the load's (rr/diesel-fuel: "TM")
// (IndustryDataLoader.ResolveCarTypeFilter, 0.6.2), and the destination menu, the interchange fill and the unload all
// test that filter. So an authored "TM,AXD" here adds the aux diesel car and keeps every TM tank car; the load itself
// is left alone, so nothing that orders or accepts diesel elsewhere changes.
// Why no industry starts ordering AXD cars: an order picks its cars by the ordering component's own filter
// (PrefabStoreExtensions.Random), and none of these order. The interchange loader only sends back the cars it
// took (OrderCars: ReturnFromBardo, by car id); a stand's unloader orders only with orderLoads on, and every one here
// is checked to have orderLoads and orderAwayEmpties false (the code's defaults are true, so a missing key is skipped).
// The menu also needs the car to be a freight car type (CarInspector shows the waybill panel only then): the DLL adds
// exactly "AXD" to the game's freight car type prefixes at each world load (src/Main.cs, FreightType).
// map.modify as Weed Sprayer and Flat Loads: has() guards, writes through the full path, no per-change lines (Player.log gets one summary and the warnings, HOUSE 1.9).
let axd = "AXD"
let dieselLoad = "rr/diesel-fuel"
// What an un-authored diesel component inherits (rr/diesel-fuel's own filter), plus the aux diesel car.
let dieselTypes = "TM,AXD"
// 2.0.10: a filter of only spaces counts as none, as the game reads it (ResolveCarTypeFilter: IsNullOrWhiteSpace means
// inherited), so it becomes "TM,AXD" and not "   ,AXD" (which would drop the TM tank cars).
func filterOf(c) {
    if !c.has("carTypeFilter") { return "" }
    var f = c["carTypeFilter"]
    if f == null { return "" }
    if f.trim() == "" { return "" }
    return f
}

// The filter with AXD in it: an un-authored one becomes "TM,AXD" (the inherited TM kept), an authored one gets ",AXD".
func withAxd(f) {
    if f == "" { return dieselTypes }
    return f + "," + axd
}

func hasAxd(f) {
    for t in f.split(",") {
        if t == axd { return true }
    }
    return false
}

func isDieselKind(c, kind) {
    return c.has("kind") && c["kind"] == kind && c.has("loadId") && c["loadId"] == dieselLoad
}

// A stand unloader that never orders: both keys present and false.
func neverOrders(c) {
    if !c.has("orderLoads") || !c.has("orderAwayEmpties") { return false }
    return !c["orderLoads"] && !c["orderAwayEmpties"]
}

// 2.1.5: found by rule, not by name (a map mod can delete, rename or add an interchange: Tetz's Beta Yard recreates the
// Sylva Interchange as Beta's). Any InterchangedIndustryLoader for rr/diesel-fuel is an interchange's diesel loader; any
// IndustryUnloader for rr/diesel-fuel that never orders is an engine service's stand. The stock map has exactly the
// nine this mod always listed (checked against the 0.6.2 Map.json). Every guard is as before.
func modifyMap(map) {
    map.modify((data) => {
        if !data.has("ops") || !data["ops"].has("areas") {
            log.warning("[aux-fuel-cars] this map has no ops areas; the aux diesel car can't be waybilled for diesel here")
            return
        }
        var loaders = 0
        var stands = 0
        var loaderSeen = 0
        var standSeen = 0
        var areas = data["ops"]["areas"]
        for aid in areas {
            if !areas[aid].has("industries") { continue }
            var industries = areas[aid]["industries"]
            for iid in industries {
                if !industries[iid].has("components") { continue }
                var comps = industries[iid]["components"]
                for cid in comps {
                    var c = comps[cid]
                    var pass = -1
                    if isDieselKind(c, "InterchangedIndustryLoader") { pass = 0 }
                    if isDieselKind(c, "IndustryUnloader") { pass = 1 }
                    if pass < 0 { continue }
                    var at = "\(aid).\(iid).\(cid)"
                    if pass == 1 && !neverOrders(c) {
                        log.warning("[aux-fuel-cars] \(at) could order cars (orderLoads or orderAwayEmpties not false); left alone, so no industry orders an AXD")
                        continue
                    }
                    if pass == 0 { loaderSeen = loaderSeen + 1 } else { standSeen = standSeen + 1 }
                    var f = filterOf(c)
                    if hasAxd(f) {
                        if pass == 0 { loaders = loaders + 1 } else { stands = stands + 1 }
                        continue
                    }
                    var wanted = withAxd(f)
                    data["ops"]["areas"][aid]["industries"][iid]["components"][cid]["carTypeFilter"] = wanted
                    if pass == 0 { loaders = loaders + 1 } else { stands = stands + 1 }
                }
            }
        }
        if loaderSeen == 0 {
            log.warning("[aux-fuel-cars] this map has no interchange diesel loader; the aux diesel car can't buy diesel here")
        }
        // The one line this script writes at startup (HOUSE 1.9); warnings above stay.
        log.info("[aux-fuel-cars] map: the aux diesel car (AXD) at \(loaders) of \(loaderSeen) interchange diesel loaders and \(stands) of \(standSeen) diesel stands")
    })
}
