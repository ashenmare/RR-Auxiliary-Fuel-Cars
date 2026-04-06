using System.IO;
using UnityEngine;

namespace ExtraTenders;

/// <summary>
/// Writes the mod's car definitions to the game's external AssetPacks folder
/// so the native PrefabStore picks them up on load — no Railloader required.
///
/// Cars defined here reuse vanilla model identifiers (no new asset bundle).
///
/// Steam aux tenders use CarArchetype.Tender so vanilla MU walk treats
/// them as transparent, exactly like primary tenders.
/// </summary>
internal static class CarDefinitions
{
    private const string PackId = "extra-tenders";

    internal static void Install()
    {
        string packDir = Path.Combine(Application.persistentDataPath, "AssetPacks", PackId);
        Directory.CreateDirectory(packDir);

        File.WriteAllText(Path.Combine(packDir, "Catalog.json"),     BuildCatalog());
        File.WriteAllText(Path.Combine(packDir, "Definitions.json"), BuildDefinitions());

        Main.Mod.Logger.Log($"Car definitions written to {packDir}");
    }

    // -----------------------------------------------------------------------
    // Catalog — no asset bundle; we reuse models from vanilla packs
    // -----------------------------------------------------------------------

    private static string BuildCatalog() => """
        {
          "identifier": "extra-tenders",
          "name": "Extra Tenders",
          "shared": false,
          "assets": {}
        }
        """;

    // -----------------------------------------------------------------------
    // Calculations (using game's own load densities)
    //   Coal:  53 lb/ft³  →  1 US gal = 0.13368 ft³  →  1 gal coal = 7.085 lb
    //          10,000 gal coal  = 70,850 lb
    //   Water: 62.4 lb/ft³ → 8.34 lb/gal
    //          30,000 lb water  =  3,597 gal
    //
    // Balanced    (P-43 unchanged):       30,000 lb coal  / 10,000 gal water
    // Coal Heavy  (water space → coal):  60,000 lb coal  / no water slot
    // Water Heavy (coal space → water):   no coal slot    / 13,597 gal water
    // -----------------------------------------------------------------------

    private static string BuildDefinitions() => """
        {
          "objects": [

            {
              "identifier": "et-dt-8k",
              "metadata": {
                "name": "Diesel Fuel Tender",
                "description": "8,000 gal auxiliary diesel fuel car",
                "tags": ["freight", "colorable"],
                "credits": "Extra Tenders mod — reuses USRA tank car model by Elijah Gooden"
              },
              "definition": {
                "kind": "Car",
                "modelIdentifier": "tm-tankcar02",
                "carType": "DT",
                "archetype": "Tank",
                "visibleInPlacer": true,
                "basePrice": 1200,
                "baseRoadNumber": "8000",
                "weightEmpty": 38500,
                "truckIdentifier": "truck.andrews.type-a",
                "loadSlots": [
                  {
                    "maximumCapacity": 8000.0,
                    "loadUnits": "Gallons",
                    "requiredLoadIdentifier": "diesel-fuel"
                  }
                ],
                "truckSeparation": 7.5477,
                "length": 11.0,
                "couplerHeight": 0.835,
                "airHosePosition": [-0.398, 0.8810002, 0.05],
                "brakeAnimations": [{ "clipName": "brake.rig" }],
                "minimumCurveRadius": "ExtraSmall",
                "components": [
                  { "kind": "Ladder", "height": 1.2, "name": "Ladder 1",
                    "transform": { "position": [1.46062326, 0.8826294, -4.99146652],
                      "rotation": [0.0, 0.7071068, 0.0, 0.7071068], "scale": [1.0,1.0,1.0] } },
                  { "kind": "Ladder", "height": 1.2, "name": "Ladder 2",
                    "transform": { "position": [-1.46063471, 0.878479, -4.98637867],
                      "rotation": [0.0, -0.7071068, 0.0, 0.7071068], "scale": [1.0,1.0,1.0] } },
                  { "kind": "Ladder", "height": 1.2, "name": "Ladder 3",
                    "transform": { "position": [-1.46071875, 0.8272705, 5.012111],
                      "rotation": [0.0, -0.7071068, 0.0, 0.7071068], "scale": [1.0,1.0,1.0] } },
                  { "kind": "Ladder", "height": 1.2, "name": "Ladder 4",
                    "transform": { "position": [1.460721, 0.8272705, 4.994409],
                      "rotation": [0.0, 0.7071068, 0.0, 0.7071068], "scale": [1.0,1.0,1.0] } },
                  { "kind": "Ladder", "height": 2.8, "name": "Ladder 5",
                    "transform": { "position": [-1.54826164, 1.67034912, -4.70841042E-05],
                      "rotation": [0.0, 0.7071068, 0.0, -0.7071068], "scale": [1.0,1.0,1.0] } },
                  { "kind": "Ladder", "height": 2.8, "name": "Ladder 6",
                    "transform": { "position": [1.548262, 1.670349, -4.70841E-05],
                      "rotation": [0.0, 0.707106769, 0.0, 0.707106769], "scale": [1.0,1.0,1.0] } },
                  { "kind": "ToggleAnimation",
                    "animation": { "clipName": "hatch.top" }, "speed": 2.0, "key": "hatch",
                    "targetColliderObject": { "path": ["tankcar02","Top Hatch","Bone","Hatch2_LOD0"] },
                    "title": "Hatch", "messageTrue": "Click to Close", "messageFalse": "Click to Open",
                    "name": "Top Hatch",
                    "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "ToggleAnimation",
                    "animation": { "clipName": "brake.whl" }, "speed": 0.3, "key": "handbrake",
                    "targetColliderObject": { "path": ["tankcar02","Brake Wheel","Tankcar02_11_LOD0"] },
                    "title": "Handbrake", "messageTrue": "Click to Release", "messageFalse": "Click to Apply",
                    "name": "Handbrake",
                    "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "LoadTarget", "radius": 1.5, "slotIndex": 0, "name": "LoadTarget Diesel",
                    "transform": { "position": [0.0, 2.85, 0.0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.7,0.25,0.2], "content": "Lettering", "name": "Decal 1",
                    "transform": { "position": [1.02982056, 2.12359619, -4.46212244],
                      "rotation": [-3.10876653E-19, 0.7071068, -2.28820465E-19, -0.7071068], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.7,0.23,0.4], "content": "RoadNumber", "name": "Decal 2",
                    "transform": { "position": [1.02987111, 2.12359619, -3.80669045],
                      "rotation": [7.958637E-18, 0.7071068, 5.58014925E-18, -0.7071068], "scale": [1,1,1] } }
                ]
              }
            },

            {
              "identifier": "et-lt-balanced",
              "metadata": {
                "name": "Aux Tender – Balanced",
                "description": "30,000 lb coal  ·  10,000 gal water — K5 Pacific capacity",
                "tags": ["tender"],
                "credits": "Extra Tenders mod — reuses K5 Pacific tender model"
              },
              "definition": {
                "kind": "Car",
                "modelIdentifier": "lt-462-p43",
                "carType": "AUXB",
                "archetype": "Tender",
                "visibleInPlacer": true,
                "basePrice": 4000,
                "baseRoadNumber": "5000",
                "weightEmpty": 78200,
                "truckIdentifier": "truck.usra.2axle",
                "loadSlots": [
                  { "maximumCapacity": 30000.0, "loadUnits": "Pounds",  "requiredLoadIdentifier": "coal"  },
                  { "maximumCapacity": 10000.0, "loadUnits": "Gallons", "requiredLoadIdentifier": "water" }
                ],
                "truckSeparation": 5.49276,
                "length": 9.488968,
                "couplerHeight": 0.82,
                "airHosePosition": [-0.51, 0.941, 0.097],
                "brakeAnimations": [{ "clipName": "Brake Rig" }],
                "minimumCurveRadius": "ExtraSmall",
                "components": [
                  { "kind": "Ladder", "height": 1.5, "name": "Ladder 1",
                    "transform": { "position": [1.55737758,1.27807617,4.81346273], "rotation": [0,0.7071068,0,0.7071069], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.5, "name": "Ladder 2",
                    "transform": { "position": [-1.56009126,1.10125732,4.79524755], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.0, "name": "Ladder 3",
                    "transform": { "position": [-1.54359829,0.7304077,-4.52823257], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.0, "name": "Ladder 4",
                    "transform": { "position": [1.54423046,0.635009766,-4.460742], "rotation": [0,0.7071068,0,0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 2.0, "name": "Ladder 5",
                    "transform": { "position": [-0.8645691,2.024292,-4.53153038], "rotation": [0,1.0,0,-8.301258e-08], "scale": [1,1,1] } },
                  { "kind": "LoadAnimation", "animation": { "clipName": "Coal" },
                    "slotIndex": 0, "loadIdentifier": "coal", "name": "Coal Animation",
                    "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "LoadTarget", "radius": 2.0, "slotIndex": 0, "name": "LoadTarget Coal",
                    "transform": { "position": [-3.278673e-05,3.800171,1.7842958], "rotation": [0,0,0,1.00000012], "scale": [1,1,1] } },
                  { "kind": "LoadAnimation", "animation": { "clipName": "Water" },
                    "slotIndex": 1, "loadIdentifier": "water", "name": "Water Animation",
                    "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "LoadTarget", "radius": 0.3, "slotIndex": 1, "name": "LoadTarget Water",
                    "transform": { "position": [5.27178127e-05,3.27868652,-3.224721], "rotation": [0,0,0,1.00000012], "scale": [1,1,1] } },
                  { "kind": "ToggleAnimation", "animation": { "clipName": "Hatch" }, "speed": 0.8,
                    "key": "water.hatch", "targetColliderObject": { "path": ["tender","Hatch"] },
                    "title": "Water Hatch", "messageTrue": "Click to Close", "messageFalse": "Click to Open",
                    "name": "Water Hatch", "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.8,1.0,0.05], "content": "RoadNumber", "name": "Decal Rear",
                    "transform": { "position": [-0.000192140753,2.211731,-4.3829875], "rotation": [0,0,0,1.00000012], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [5.0,0.8,0.05], "content": "Lettering", "name": "Decal Right",
                    "transform": { "position": [1.50808656,2.1895752,-0.101290241], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [5.0,0.8,0.05], "content": "Lettering", "name": "Decal Left",
                    "transform": { "position": [-1.50807,2.189575,-0.1012902], "rotation": [0,0.707106769,0,0.707106769], "scale": [1,1,1] } },
                  { "kind": "MarkerLight", "radius": 1.25, "end": 1, "name": "MarkerLight",
                    "transform": { "position": [3.59040132e-06,2.77160645,-4.482633], "rotation": [-9.26095135e-17,0,4.35059e-17,1.00000012], "scale": [1,1,1] } },
                  { "kind": "PrefabControl", "prefab": "HandbrakeWheel", "name": "Handbrake Tender",
                    "transform": { "position": [-1.17106259,2.4954834,4.59386444], "rotation": [0,-0.7071068,0,0.7071068], "scale": [0.8,0.8,0.8] } },
                  { "kind": "Headlight", "forward": false, "lightEnabled": true, "name": "Tender Light",
                    "transform": { "position": [0,3.44466,-4.31624], "rotation": [0,1.0,0,-4.371139e-08], "scale": [0.995,0.995,0.995] } },
                  { "kind": "Decal", "size": [0.25,0.14,0.05], "content": "RoadNumber", "forceColor": "ffff", "name": "Tender Rear Decal Left",
                    "transform": { "position": [0.223451,3.44466,-4.05694], "rotation": [0,-0.707106769,0,0.707106769], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.25,0.14,0.05], "content": "RoadNumber", "forceColor": "ffff", "name": "Tender Rear Decal Right",
                    "transform": { "position": [-0.223451,3.44466,-4.05694], "rotation": [0,0.707106769,0,0.707106769], "scale": [1,1,1] } }
                ]
              }
            },

            {
              "identifier": "et-lt-coal",
              "metadata": {
                "name": "Aux Tender – Coal Heavy",
                "description": "60,000 lb coal (30 tons)  ·  no water",
                "tags": ["tender"],
                "credits": "Extra Tenders mod — reuses K5 Pacific tender model"
              },
              "definition": {
                "kind": "Car",
                "modelIdentifier": "lt-462-p43",
                "carType": "AUXC",
                "archetype": "Tender",
                "visibleInPlacer": true,
                "basePrice": 4000,
                "baseRoadNumber": "5100",
                "weightEmpty": 78200,
                "truckIdentifier": "truck.usra.2axle",
                "loadSlots": [
                  { "maximumCapacity": 60000.0, "loadUnits": "Pounds", "requiredLoadIdentifier": "coal" }
                ],
                "truckSeparation": 5.49276,
                "length": 9.488968,
                "couplerHeight": 0.82,
                "airHosePosition": [-0.51, 0.941, 0.097],
                "brakeAnimations": [{ "clipName": "Brake Rig" }],
                "minimumCurveRadius": "ExtraSmall",
                "components": [
                  { "kind": "Ladder", "height": 1.5, "name": "Ladder 1",
                    "transform": { "position": [1.55737758,1.27807617,4.81346273], "rotation": [0,0.7071068,0,0.7071069], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.5, "name": "Ladder 2",
                    "transform": { "position": [-1.56009126,1.10125732,4.79524755], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.0, "name": "Ladder 3",
                    "transform": { "position": [-1.54359829,0.7304077,-4.52823257], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.0, "name": "Ladder 4",
                    "transform": { "position": [1.54423046,0.635009766,-4.460742], "rotation": [0,0.7071068,0,0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 2.0, "name": "Ladder 5",
                    "transform": { "position": [-0.8645691,2.024292,-4.53153038], "rotation": [0,1.0,0,-8.301258e-08], "scale": [1,1,1] } },
                  { "kind": "LoadAnimation", "animation": { "clipName": "Coal" },
                    "slotIndex": 0, "loadIdentifier": "coal", "name": "Coal Animation",
                    "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "LoadTarget", "radius": 2.0, "slotIndex": 0, "name": "LoadTarget Coal",
                    "transform": { "position": [-3.278673e-05,3.800171,1.7842958], "rotation": [0,0,0,1.00000012], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.8,1.0,0.05], "content": "RoadNumber", "name": "Decal Rear",
                    "transform": { "position": [-0.000192140753,2.211731,-4.3829875], "rotation": [0,0,0,1.00000012], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [5.0,0.8,0.05], "content": "Lettering", "name": "Decal Right",
                    "transform": { "position": [1.50808656,2.1895752,-0.101290241], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [5.0,0.8,0.05], "content": "Lettering", "name": "Decal Left",
                    "transform": { "position": [-1.50807,2.189575,-0.1012902], "rotation": [0,0.707106769,0,0.707106769], "scale": [1,1,1] } },
                  { "kind": "MarkerLight", "radius": 1.25, "end": 1, "name": "MarkerLight",
                    "transform": { "position": [3.59040132e-06,2.77160645,-4.482633], "rotation": [-9.26095135e-17,0,4.35059e-17,1.00000012], "scale": [1,1,1] } },
                  { "kind": "PrefabControl", "prefab": "HandbrakeWheel", "name": "Handbrake Tender",
                    "transform": { "position": [-1.17106259,2.4954834,4.59386444], "rotation": [0,-0.7071068,0,0.7071068], "scale": [0.8,0.8,0.8] } },
                  { "kind": "Headlight", "forward": false, "lightEnabled": true, "name": "Tender Light",
                    "transform": { "position": [0,3.44466,-4.31624], "rotation": [0,1.0,0,-4.371139e-08], "scale": [0.995,0.995,0.995] } },
                  { "kind": "Decal", "size": [0.25,0.14,0.05], "content": "RoadNumber", "forceColor": "ffff", "name": "Tender Rear Decal Left",
                    "transform": { "position": [0.223451,3.44466,-4.05694], "rotation": [0,-0.707106769,0,0.707106769], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.25,0.14,0.05], "content": "RoadNumber", "forceColor": "ffff", "name": "Tender Rear Decal Right",
                    "transform": { "position": [-0.223451,3.44466,-4.05694], "rotation": [0,0.707106769,0,0.707106769], "scale": [1,1,1] } }
                ]
              }
            },

            {
              "identifier": "et-lt-water",
              "metadata": {
                "name": "Aux Tender – Water Heavy",
                "description": "no coal  ·  13,597 gal water — 30,000 lb coal space converted to water (62.4 lb/ft³)",
                "tags": ["tender"],
                "credits": "Extra Tenders mod — reuses K5 Pacific tender model"
              },
              "definition": {
                "kind": "Car",
                "modelIdentifier": "lt-462-p43",
                "carType": "AUXW",
                "archetype": "Tender",
                "visibleInPlacer": true,
                "basePrice": 4000,
                "baseRoadNumber": "5200",
                "weightEmpty": 78200,
                "truckIdentifier": "truck.usra.2axle",
                "loadSlots": [
                  { "maximumCapacity": 13597.0, "loadUnits": "Gallons", "requiredLoadIdentifier": "water" }
                ],
                "truckSeparation": 5.49276,
                "length": 9.488968,
                "couplerHeight": 0.82,
                "airHosePosition": [-0.51, 0.941, 0.097],
                "brakeAnimations": [{ "clipName": "Brake Rig" }],
                "minimumCurveRadius": "ExtraSmall",
                "components": [
                  { "kind": "Ladder", "height": 1.5, "name": "Ladder 1",
                    "transform": { "position": [1.55737758,1.27807617,4.81346273], "rotation": [0,0.7071068,0,0.7071069], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.5, "name": "Ladder 2",
                    "transform": { "position": [-1.56009126,1.10125732,4.79524755], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.0, "name": "Ladder 3",
                    "transform": { "position": [-1.54359829,0.7304077,-4.52823257], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 1.0, "name": "Ladder 4",
                    "transform": { "position": [1.54423046,0.635009766,-4.460742], "rotation": [0,0.7071068,0,0.7071068], "scale": [1,1,1] } },
                  { "kind": "Ladder", "height": 2.0, "name": "Ladder 5",
                    "transform": { "position": [-0.8645691,2.024292,-4.53153038], "rotation": [0,1.0,0,-8.301258e-08], "scale": [1,1,1] } },
                  { "kind": "LoadAnimation", "animation": { "clipName": "Water" },
                    "slotIndex": 0, "loadIdentifier": "water", "name": "Water Animation",
                    "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "LoadTarget", "radius": 0.3, "slotIndex": 0, "name": "LoadTarget Water",
                    "transform": { "position": [5.27178127e-05,3.27868652,-3.224721], "rotation": [0,0,0,1.00000012], "scale": [1,1,1] } },
                  { "kind": "ToggleAnimation", "animation": { "clipName": "Hatch" }, "speed": 0.8,
                    "key": "water.hatch", "targetColliderObject": { "path": ["tender","Hatch"] },
                    "title": "Water Hatch", "messageTrue": "Click to Close", "messageFalse": "Click to Open",
                    "name": "Water Hatch", "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.8,1.0,0.05], "content": "RoadNumber", "name": "Decal Rear",
                    "transform": { "position": [-0.000192140753,2.211731,-4.3829875], "rotation": [0,0,0,1.00000012], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [5.0,0.8,0.05], "content": "Lettering", "name": "Decal Right",
                    "transform": { "position": [1.50808656,2.1895752,-0.101290241], "rotation": [0,0.7071068,0,-0.7071068], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [5.0,0.8,0.05], "content": "Lettering", "name": "Decal Left",
                    "transform": { "position": [-1.50807,2.189575,-0.1012902], "rotation": [0,0.707106769,0,0.707106769], "scale": [1,1,1] } },
                  { "kind": "MarkerLight", "radius": 1.25, "end": 1, "name": "MarkerLight",
                    "transform": { "position": [3.59040132e-06,2.77160645,-4.482633], "rotation": [-9.26095135e-17,0,4.35059e-17,1.00000012], "scale": [1,1,1] } },
                  { "kind": "PrefabControl", "prefab": "HandbrakeWheel", "name": "Handbrake Tender",
                    "transform": { "position": [-1.17106259,2.4954834,4.59386444], "rotation": [0,-0.7071068,0,0.7071068], "scale": [0.8,0.8,0.8] } },
                  { "kind": "Headlight", "forward": false, "lightEnabled": true, "name": "Tender Light",
                    "transform": { "position": [0,3.44466,-4.31624], "rotation": [0,1.0,0,-4.371139e-08], "scale": [0.995,0.995,0.995] } },
                  { "kind": "Decal", "size": [0.25,0.14,0.05], "content": "RoadNumber", "forceColor": "ffff", "name": "Tender Rear Decal Left",
                    "transform": { "position": [0.223451,3.44466,-4.05694], "rotation": [0,-0.707106769,0,0.707106769], "scale": [1,1,1] } },
                  { "kind": "Decal", "size": [0.25,0.14,0.05], "content": "RoadNumber", "forceColor": "ffff", "name": "Tender Rear Decal Right",
                    "transform": { "position": [-0.223451,3.44466,-4.05694], "rotation": [0,0.707106769,0,0.707106769], "scale": [1,1,1] } }
                ]
              }
            }

          ]
        }
        """;
}
