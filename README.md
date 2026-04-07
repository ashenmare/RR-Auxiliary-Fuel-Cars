# Auxiliary Fuel Cars

A [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) mod for [Railroader](https://store.steampowered.com/app/1784630/Railroader/) that adds auxiliary tender and fuel cars to extend the range of steam and diesel locomotives.

Cars spawn with 20% fuel by default (configurable in the UMM settings panel). Aux cars drain first then the locomotive's primary supply only starts dropping once the aux is empty.

## Cars

### Steam Aux Tender (`AUXT`)

Reuses the K5 Pacific (P-43) tender model. Carries both coal and water, matching vanilla P-43 tender capacity. Couple it behind the primary tender, and coal and water are topped up automatically every fuel tick.

| Name | Coal | Water |
|---|---|---|
| Aux Tender | 30,000 lb | 10,000 gal |

**Standard consist:** `[Steam Loco] — [Primary Tender] — [Aux Tender]`

**Back-to-back MU:** A single shared aux tender between two locos is supported.
`[Loco 1] — [Tender 1] — [Aux Tender] — [Tender 2] — [Loco 2]`

Can be filled at coal loaders and water towers normally.

### Diesel Fuel Tender (`DT`)

Reuses the USRA single-dome tank car (tankcar02) model. Couple it at either end of a diesel locomotive — diesel fuel is topped up automatically each fuel tick. Can be filled at fuel stands and accepts interchange waybills.

| Name | Diesel |
|---|---|
| Diesel Fuel Tender | 8,000 gal |

**Consist:** `[Aux Tank] — [Diesel Loco]` or `[Diesel Loco] — [Aux Tank]`

## Cost
Aux Tender (AUXT): $4,000

Diesel Fuel Tender (DT): $1,200

## Settings

Accessible from the UMM mod menu in-game.

| Setting | Default | Description |
|---|---|---|
| Fuel loading speed multiplier | 10× | Speeds up filling at service points |
| Starting fuel on placement | 20% | How full aux cars spawn |

## MU Safety

- Only cars with carType `DT` or `AUXT` are eligible as aux sources; vanilla rolling stock and third-party cars are never touched.
- A patch to `BaseLocomotive.FindSourceLocomotive` ensures MU control passes through aux cars transparently in a consist.

## Installation

1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) for Railroader if you haven't already.
2. Download the latest release zip and install it via the UMM installer, or extract the `AuxiliaryFuelCars` folder into your `Mods` directory.
3. Launch the game and the new cars appear in the car placer immediately. Vanilla model bundles are copied from your game files on first run.

## Compatibility

- **Railloader**: not required and not used.
- **DPC / DPU mods**: no conflict — fuel transfer is fully independent of MU control.

## License

MIT
