using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using UI;
using UI.Builder;
using UI.Extensions;

namespace AuxFuelCars
{
    // settings.json beside the dll, also edited in game (Preferences > Mods, SettingsSection.cs held by SettingsUi).
    internal sealed class Settings
    {
        // On: an MU'd helper anywhere in the train follows the lead (RemoteMu.cs). Off: vanilla MU only. The host's
        // value rules every machine in its world (HostSetting.cs); a save in Preferences takes effect at once, and a helper
        // that was following remotely drops to idle (RemoteMuPatch.ReleaseIfLost).
        public bool remoteMu = true;

        // 2.0.8 to 2.1.1: rearAir, on/off. 2.1.2: "Helper engines work the air", three choices (Logic.HelperAir): Off,
        // By hand only, Always (the default since 2.1.3); saved as the dropdown's text under helperAir and
        // read by HelperAirSetting.Read (which migrates rearAir), never by Newtonsoft's field binding (internal), so a
        // value it can't read costs only this setting, as the dropdown shows it. The host's value rules, as for remoteMu.
        internal HelperAir helperAir = HelperAirSetting.Default;

        // On: a diesel's top-up passes over directly coupled diesel units, so every unit of a lash-up
        // draws from the aux cars coupled to it (TopUp.cs). Off: only the unit touching an aux car draws, as before.
        // The host's value rules (only the host tops up).
        public bool lashUp = true;
    }

    internal static class Util
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        internal static void ResetWarnings() => Warned.Clear();

        // A per-world warning said again in the next world (RearAirPatch.Reset).
        internal static void Unwarn(string key)
        {
            try { Warned.Remove(key); } catch { }
        }

        // The mod logger hands its text to Serilog as a message template; keep braces out of it.
        internal static string Plain(string s) => (s ?? "").Replace('{', '(').Replace('}', ')');

        internal static void Info(string text)
        {
            try { Main.Log?.Info(Plain(Main.LogTag + ": " + text)); } catch { }
        }

        internal static void Warn(string text)
        {
            try { Main.Log?.Warning(Plain(Main.LogTag + ": " + text)); } catch { }
        }

        // Even the logger must not throw out of a postfix on the game's once-a-second update.
        internal static void WarnOnce(string key, string text)
        {
            try { if (Warned.Add(key)) Warn(text); } catch { }
        }

        // 2.0.6 (privacy): every exception text this mod logs goes through PathClean (SettingsSection.cs), so a folder
        // path keeps only its part from the game's Railroader folder on, or just the file name, and nothing before it
        // (no home folder, no account name). The cleaning runs inside the logger's own try.
        // A failure's message, cleaned.
        internal static void Warn(string text, Exception e)
        {
            try { Warn(text + PathClean.CleanText(e?.Message)); } catch { }
        }

        internal static void WarnOnce(string key, string text, Exception e)
        {
            try { if (Warned.Add(key)) Warn(text + PathClean.CleanText(e?.Message)); } catch { }
        }

        // A whole exception (type, message and stack), cleaned, once per key. The key is checked first, so a postfix
        // that keeps failing on the game's update never cleans a text it won't log.
        internal static void WarnOnceWhole(string key, string text, Exception e)
        {
            try { if (Warned.Add(key)) Warn(text + PathClean.CleanText(e?.ToString(), 4000)); } catch { }
        }
    }

    // Railroader 0.6 entry point. The cars themselves come from Scripts/Data.rl (no code). Three patched parts, each
    // on its own so a missing hook stands only that part down: steam top-up, diesel top-up, remote MU. At each world
    // load: the Feed checkbox in the aux cars' Operations tab, and the "AX" car type's display name.
    public sealed class Main : IModEntryPoint, IWorldLifecycle
    {
        internal const string Name = "Aux Fuel Cars and Remote MU";
        // The log prefix stays the old name: README.txt and the gates quote the log lines (2.1.4).
        internal const string LogTag = "Auxiliary Fuel Cars";
        internal const string Version = "2.2.1";
        private const string HarmonyId = "ashenmare.aux-fuel-cars";
        internal static IModLogger Log;
        // The helper air part went in at load (the feed reports it). A bool, so the feed can read it from anywhere.
        internal static volatile bool RearHooked;
        internal static Settings Settings = new Settings();
        private Harmony _harmony;

        // 2.0.6 (the Mono class-load rule): the four lifecycle methods name only the BCL, the loader's and Harmony's
        // types, and this mod's Util and Settings. Every class typed by the game (SettingsUi, WorldUi, FreightType,
        // HostSetting, RemoteMuPatch and the patch classes) is reached only through the NoInlining helpers below, each
        // called inside a try: on Mono a method that names a class whose fields can't load throws at its caller when it
        // is compiled, before its own try runs. 2.0.5 (and 2.0.4 on Nexus) threw out of OnModLoad that way, before its
        // first line (SettingsUi's UIState field), and out of OnWorldUnload (RemoteMuPatch.Clear, outside any try).
        // Railroader.dll being unloadable is covered by the no-game test; the helpers must stay NoInlining.
        public void OnModLoad(IModContext context)
        {
            Api.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;   // the feed answers on this thread
            Log = context.Log;
            Util.ResetWarnings();
            string settingsPath = Path.Combine(context.ModDirectory, "settings.json");
            try { Settings = LoadSettings(settingsPath); }
            catch (Exception e)
            {
                Settings = new Settings();
                Util.Warn("settings couldn't load, using the defaults (remote MU on): ", e);
            }
            try
            {
                _harmony = new Harmony(HarmonyId);
                // This mod's own patches off first. A mod-set change reuses the loaded
                // assembly, and Harmony doesn't dedupe: if an unload's UnpatchAll failed, a second copy of each patch
                // would go on (a second RemoteMuPatch postfix would forget every link the first one made).
                try { _harmony.UnpatchAll(HarmonyId); } catch (Exception) { }
                string steam = Part(SteamPatch, "steam top-up");
                string diesel = Part(DieselPatch, "diesel top-up");
                // The steam and diesel postfixes are what idle a helper that loses its remote lead
                // (RemoteMuPatch.ReleaseIfLost), so Remote MU only goes on when both are on.
                string remote = steam.EndsWith(" on") && diesel.EndsWith(" on")
                    ? Part(RemotePatch, "remote MU")
                    : "remote MU off (it needs the steam and diesel parts, which idle a helper that loses its lead)";
                // 2.0.8: helper engines work the air, only with Remote MU on. The Auto Engineer's copy guard goes in
                // first; the air part goes in only after it, so the Auto Engineer never plans on a helper's open valves.
                string rear = "helper engines' air off";
                if (remote.EndsWith(" on"))
                {
                    string guard = Part(RearSnapshotPatch, "helper engines' air (Auto Engineer copy)");
                    if (guard.EndsWith(" on")) rear = Part(RearPatch, "helper engines' air");
                }
                RearHooked = rear.EndsWith(" on");
                var parts = new List<string> { steam, diesel, remote, rear };
                Util.Info(Version + " loaded: " + string.Join(", ", parts)
                    + (Settings.remoteMu ? " (Remote MU setting: on" : " (Remote MU setting: off")
                    + ", helper engines work the air: " + HelperAirSetting.Name(Settings.helperAir)
                + (Settings.lashUp ? ", lash-up feed: on)" : ", lash-up feed: off)"));
                // 2.0.9: the feed on the shared list (ModFeeds.cs), so any mod can find and read it. ModFeeds
                // and Api name no game type, so this is safe here; Api.Read stays for mods that scan.
                try { if (!PublishFeed()) Util.Warn("another mod already publishes the feed id \"" + Api.Feed + "\", so this one isn't listed"); }
                catch (Exception) { }
            }
            catch (Exception e)
            {
                try { _harmony?.UnpatchAll(HarmonyId); } catch (Exception) { }
                Util.Warn("couldn't start on this game version; the cars stay plain cars and MU stays vanilla: ", e);
                try { StopSettings(); } catch (Exception) { }
                return;
            }
            try { StartSettings(_harmony, settingsPath); }
            catch (Exception e)
            {
                Util.Warn("the settings aren't in Preferences > Mods on this game version (the mod works without them): ", e);
                try { StopSettings(); } catch (Exception) { }
            }
        }

        // The game clears its UI extension, car type and freight prefix registries before each world load, then calls this.
        public void OnWorldLoad()
        {
            Api.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            try { ResetPerWorld(); } catch (Exception) { }
            // 2.1.0: other packs' aux-fuel-cars.json, before the Feed checkbox is registered so their cars get it.
            try { LoadOpenCars(); }
            catch (Exception e)
            {
                try { ClearOpenCars(); } catch (Exception) { }
                Util.Warn("couldn't read other packs' aux-fuel-cars.json (only this mod's own cars feed): ", e);
            }
            try { RegisterWorldUi(); }
            catch (Exception e) { Util.Warn("the Feed checkbox isn't available on this game version (aux cars always feed): ", e); }
            try { RegisterFreight(); }
            catch (Exception e) { Util.Warn("the aux diesel car's waybill panel isn't available on this game version (it still fills at fuel stands; waybills it already has still run, so reload the save to manage them): ", e); }
            try { ShareSetting(); }
            catch (Exception e)
            {
                try { DetachSetting(); } catch (Exception) { }
                Util.Warn("couldn't share the Remote MU setting on this game version; each computer uses its own: ", e);
            }
        }

        public void OnWorldUnload()
        {
            try { UnregisterSetting(); } catch (Exception) { }
            try { ClearRemoteMu(); } catch (Exception) { }
            try { ResetPerWorld(); } catch (Exception) { }
        }

        // 2.0.6 (unload order): the mod lets go of its Harmony instance first (its "off" state), stops each part in its
        // own try, then runs UnpatchAll in its own try, so nothing that throws on the way can leave the patches in.
        // 2.0.9: the feed comes off the shared list first.
        public void OnModUnload()
        {
            try { ModFeeds.Withdraw(Api.Feed); } catch (Exception) { }
            Harmony harmony = _harmony;
            _harmony = null;
            RearHooked = false;
            Api.MainThreadId = -1;
            try { StopSettings(); } catch (Exception) { }
            try { UnregisterFreight(); } catch (Exception) { }
            try { DetachSetting(); } catch (Exception) { }
            try { harmony?.UnpatchAll(HarmonyId); }
            catch (Exception e) { Util.Warn("couldn't remove every patch on unload: ", e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Settings LoadSettings(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(text)) return new Settings();   // as JsonConvert read it (null: the defaults)
                    // JObject.Parse skips comments before the object, as JsonConvert's read did; anything but an object
                    // throws, and the catch below takes the defaults.
                    JObject o = JObject.Parse(text);
                    Settings s = From(o);
                    // 2.1.2: the older toggle into the dropdown's key (HelperAirSetting), once, before the section reads it.
                    HelperAirSetting.Read(o, out HelperAirSetting.Source from);
                    if (from == HelperAirSetting.Source.OldToggle) MigrateHelperAir(path, s.helperAir);
                    return s;
                }
            }
            catch (Exception e)
            {
                Util.Warn("couldn't read settings.json, using the defaults (remote MU on): ", e);
                return new Settings();
            }
            // No file yet: write one with the defaults so players can find it. Never in the zip, so an update can't
            // overwrite someone's own value; an existing file (even an unreadable one) is never touched (but by the
            // one-time 2.1.2 migration above).
            Settings d = new Settings();
            string tmp = path + ".tmp";
            try
            {
                // The toggles as Newtonsoft writes them, the helper air choice as the dropdown saves it, in the
                // dropdown's order (after remoteMu).
                JObject o = JObject.FromObject(d);
                var choice = new JProperty(HelperAirSetting.Key, HelperAirSetting.Name(d.helperAir));
                JProperty first = o.Property(nameof(Settings.remoteMu));
                if (first != null) first.AddAfterSelf(choice); else o.Add(choice);
                File.WriteAllText(tmp, o.ToString(Formatting.Indented));
                File.Move(tmp, path);
                Util.Info("wrote settings.json with the defaults (remoteMu true)");
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                Util.Warn("couldn't write a default settings.json (the defaults apply anyway): ", e);
            }
            return d;
        }

        // The one read of settings.json, at load and after a save in Preferences > Mods: the toggles by Newtonsoft (a value
        // it can't convert throws, and the caller falls back to every default, as the section's message says), the helper
        // air choice by HelperAirSetting.Read (2.1.2).
        internal static Settings From(JObject o)
        {
            Settings s = o.ToObject<Settings>() ?? new Settings();
            s.helperAir = HelperAirSetting.Read(o, out _);
            return s;
        }

        // 2.1.2: add helperAir from the older rearAir (HelperAirSetting). On failure the mod still runs the choice it read
        // this session; only the dropdown can disagree (when that choice isn't its default), and the log says what to do.
        private static void MigrateHelperAir(string path, HelperAir air)
        {
            string tmp = path + ".tmp";
            string name = HelperAirSetting.Name(air);
            string fix = air == HelperAirSetting.Default ? null
                : "helper engines work the air: " + name + " (from rearAir), but Preferences > Mods shows "
                  + HelperAirSetting.Name(HelperAirSetting.Default) + ": pick " + name + " there and save";
            try
            {
                // On the file's bytes, so a byte order mark stays and a file that isn't UTF-8 is left as it is.
                byte[] made = HelperAirSetting.InsertBytes(File.ReadAllBytes(path), air);
                if (made == null)
                {
                    if (fix != null) Util.Warn("couldn't add helperAir to settings.json without changing the rest of it; " + fix);
                    return;
                }
                File.WriteAllBytes(tmp, made);
                File.Replace(tmp, path, null);
                Util.Info("added \"helperAir\": \"" + name + "\" to settings.json, from rearAir (kept, now unread)");
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                if (fix != null) Util.Warn("couldn't add helperAir to settings.json (" + fix + "): ", e);
            }
        }

        // After a save in Preferences > Mods: the same read as at load, from the file just written.
        internal static void Applied(JObject saved)
        {
            Settings = From(saved);
            try { PublishSetting(); }
            catch (Exception e) { Util.Warn("couldn't send the Remote MU setting to the other players: ", e); }
            bool everyone = false;
            try { everyone = HostsWorld(); } catch (Exception) { }
            Util.Info("settings saved, remote MU " + (Settings.remoteMu ? "on" : "off") + ", helper engines work the air: "
                + HelperAirSetting.Name(Settings.helperAir) + ", lash-up feed " + (Settings.lashUp ? "on" : "off") + ", from the next MU check (within a second)"
                + (everyone ? ", for everyone in this world" : ""));
        }

        // One patch class, on its own: a missing hook (or a class that won't load) stands only that part down.
        private string Part(Func<Type> patchClass, string what)
        {
            try
            {
                List<System.Reflection.MethodInfo> done = _harmony.CreateClassProcessor(patchClass()).Patch();
                return what + (done != null && done.Count > 0 ? " on" : " off");
            }
            catch (Exception e)
            {
                Util.Warn(what + " is unavailable on this game version, left as vanilla: ", e);
                return what + " off";
            }
        }

        // The feed's entry on the shared list: Api.Read, with a line per top-level key (release/FEEDS.md has the schema).
        private static bool PublishFeed()
        {
            var fields = new Dictionary<string, string>
            {
                { "modVersion", "the mod's version" },
                { "host", "true on the computer that runs the railroad (single player or the multiplayer host); the top-up runs only there" },
                { "remoteMu", "the Remote MU setting in force in this world (the host's in multiplayer)" },
                { "rearAir", "true unless the Helper engines work the air setting in force in this world is Off (the host's in multiplayer)" },
                { "helperAir", "the Helper engines work the air setting in force in this world (the host's in multiplayer): off, byHand or always" },
                { "lashUp", "the Lash-up feed setting in force in this world (the host's in multiplayer)" },
                { "openCars", "other packs' cars declared as aux cars (aux-fuel-cars.json), by definition id: pack, loads (null: every load it has a slot for)" },
                { "rearAirHooked", "the helper air part went in when the mod loaded" },
                { "auxCars", "each aux car by car id: kind (water, diesel, tender, or declared for another pack's car), feedOn, loads (quantity, capacity, units: gal, lb, or units for any other), feeds (the engines it fed in the last 2.5 s, by car id), feeding" },
                { "helpers", "each Remote MU helper by car id: leadId, position (rear or mid), workingAir (it works the brake pipe now)" },
                { "fuelSpeeds", "Faster Fueling's refuel speeds as followed now: following (its feed says it is active), and the multiple of the game's speed for coal, water and diesel (1 when not followed)" },
            };
            return ModFeeds.Publish(Api.Feed, Api.Read, Name, Name, Version, Api.Version, fields);
        }

        // The helpers: each names one game-typed class of this mod, so that class loads (or fails to) when the helper is
        // compiled, inside its caller's try. Each one must stay NoInlining in the built dll.
        [MethodImpl(MethodImplOptions.NoInlining)] private static Type SteamPatch() => typeof(SteamTopUpPatch);
        [MethodImpl(MethodImplOptions.NoInlining)] private static Type DieselPatch() => typeof(DieselTopUpPatch);
        [MethodImpl(MethodImplOptions.NoInlining)] private static Type RemotePatch() => typeof(RemoteMuPatch);
        [MethodImpl(MethodImplOptions.NoInlining)] private static Type RearPatch() => typeof(RearAirPatch);
        [MethodImpl(MethodImplOptions.NoInlining)] private static Type RearSnapshotPatch() => typeof(RearAirSnapshotPatch);
        [MethodImpl(MethodImplOptions.NoInlining)] private static void StartSettings(Harmony harmony, string path) => SettingsUi.Start(harmony, path);
        [MethodImpl(MethodImplOptions.NoInlining)] private static void StopSettings() => SettingsUi.Stop();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void RegisterWorldUi() => WorldUi.Register();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void RegisterFreight() => FreightType.Register();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void UnregisterFreight() => FreightType.Unregister();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void DetachSetting() => HostSetting.Detach();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void UnregisterSetting() => HostSetting.Unregister();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void ClearRemoteMu() => RemoteMuPatch.Clear();
        // Every per-world latch and record (statics survive a mod-set change): the rear air stand-down, the feed's log.
        [MethodImpl(MethodImplOptions.NoInlining)] private static void ResetPerWorld() { FasterFueling.Reset(); RearAirPatch.Reset(); FeedLog.Clear(); Api.Reset(); OpenCars.Clear(); }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void LoadOpenCars() => OpenCarsLoader.Load();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void ClearOpenCars() => OpenCars.Clear();

        // OnWorldLoad: the world-level Remote MU setting, and which computer's value rules.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ShareSetting()
        {
            HostSetting.Register();
            bool? world = HostSetting.WorldValue;
            Util.Info(HostSetting.IsClient
                ? "Remote MU follows the host: " + (world.HasValue ? (world.Value ? "on" : "off") : "not heard yet, this computer's setting until it is")
                : "Remote MU " + (Settings.remoteMu ? "on" : "off") + " for this world (this computer hosts, so its setting rules)");
        }

        // After a save: the host writes it into the world, and when this computer hosts a world everyone follows it.
        [MethodImpl(MethodImplOptions.NoInlining)] private static void PublishSetting() => HostSetting.Publish();
        [MethodImpl(MethodImplOptions.NoInlining)] private static bool HostsWorld() => HostSetting.InWorld && !HostSetting.IsClient;
    }

    // The world-load registrations: the Feed checkbox and the car type name. Typed by the game's UI, so kept off Main
    // and called from a NoInlining method inside a try (a game update that breaks a type here costs only this).
    internal static class WorldUi
    {
        private const string ExtensionId = "ashenmare.aux-fuel-cars.feed";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            // Only on the three aux cars and (2.1.0) other packs' declared ones: the car inspector passes each car's
            // definition identifier as "identifier".
            var match = Ids.All.Concat(OpenCars.Cars.Keys).Select(id => new Dictionary<string, string> { { "identifier", id } }).ToList();
            UIExtensionRegistry.RegisterExtension<Car>("carInspector.operations", ExtensionId, match, 100, BuildFeed);
            // "AXW - Auxiliary Fuel" in the shop's detail line; the placer lists the three together, before the tenders.
            CarTypeDisplayRegistry.Register(Ids.TypePrefix, Ids.TypeDisplayName, 9990);

            // Proof in Player.log that Data.rl made the cars (its own "[aux-fuel-cars]" lines are earlier, at startup).
            var library = TrainController.Shared?.ContentLibrary;
            if (library == null) return;
            var have = new HashSet<string>(library.AllCarDefinitionInfos.Select(i => i.Identifier));
            var missing = Ids.All.Where(id => !have.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (missing.Count == 0) Util.Info("the three aux cars are defined (water car, diesel car, extra tender)");
            else Util.Warn("not defined in this world: " + string.Join(", ", missing)
                + " (Scripts/Data.rl didn't make them; look for \"aux-fuel-cars/Data.rl\" errors in Player.log)");
        }

        private static void BuildFeed(UIPanelBuilder builder, Car car)
        {
            if (car == null) return;
            // The write applies locally at once, also on a client, and goes to the host. The box redraws when the key
            // changes, from this click or from anyone else's (another player, or the host's answer to a refused write):
            // the panel's own RebuildOnPropertyChange, as the inspector's handbrake and load rows do, instead of the MoW
            // box's immediate Rebuild inside the click.
            // If a builder ever has no panel to observe with, the MoW box's own way.
            bool observed;
            try { builder.RebuildOnPropertyChange(car.KeyValueObject, Ids.KvKey); observed = true; }
            catch (Exception) { observed = false; }
            builder.AddFieldToggle("Feed", () => Feed.IsOn(car), v =>
            {
                Feed.Set(car, v);
                if (!observed) builder.Rebuild();
            }, Feed.CanWrite(car))
                .Tooltip("Feed", "Off: the car is never drawn from.");
        }
    }

    // 2.0.5: the Auxiliary Diesel Car as a freight car type, so it can be waybilled: the car inspector builds its waybill
    // and Automatic Waybills panels only for a freight car type (CarInspector.PopulateOperationsPanel, CarType.IsFreight: a
    // prefix of X, H, T, F or G, 0.6.2). The game's list is public and is one of the mod registries it resets before
    // each world load (GlobalGameManager.ClearModRegistries -> CarType.ResetFreightCarTypePrefixes, also on the
    // console's script reload), beside the car type names, so this runs at every world load on every machine.
    // Exactly "AXD": the match is StartsWith, so "AX" would make the water car and the Extra Tender freight too.
    // Freight is not what makes an industry order a car: orders pick definitions by the ordering component's car type
    // filter only (PrefabStoreExtensions.Random), and Data.rl adds AXD only where nothing orders.
    // Where to: Data.rl's modifyMap puts AXD on the interchange diesel loaders and the diesel stands.
    internal static class FreightType
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            List<string> prefixes = CarType.FreightCarTypePrefixes;
            if (!prefixes.Contains(Ids.DieselCarType)) prefixes.Add(Ids.DieselCarType);
            Util.Info("the aux diesel car (" + Ids.DieselCarType + ") takes waybills: interchange diesel, and diesel stands to unload");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Unregister() => CarType.FreightCarTypePrefixes.Remove(Ids.DieselCarType);
    }

    // Everything typed by SettingsSection.cs (and so by the game's UI) lives here, off Main, in a field typed object, and
    // is only touched from these NoInlining methods, which Main reaches through its own NoInlining helpers inside a try
    // (SettingsSection.cs Revision 7).
    internal static class SettingsUi
    {
        private static object _section;   // a SettingsSection

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Start(Harmony harmony, string settingsPath)
        {
            Stop();
            var fields = new[]
            {
                SettingField.Bool("remoteMu", "Remote MU", true, true,
                    "Off: the game's own MU only."),
                SettingField.Choice(HelperAirSetting.Key, "Helper engines work the air", HelperAirSetting.Options, (int)HelperAirSetting.Default, true,
                    "By hand only: not while the Auto Engineer drives."),
                SettingField.Bool("lashUp", "Lash-up feed", true, true,
                    "Diesel units coupled together all draw from the aux cars."),
            };
            _section = new SettingsSection(Main.Name, settingsPath, fields, Main.Applied,
                m => Util.Warn(m),
                "In multiplayer the host's setting rules.");
            PreferencesModsTab.Add(harmony, Draw, m => Util.Warn(m));
        }

        private static readonly UIState<bool> ClientExpanded = new UIState<bool>(false);

        // On a client in a world: the host's value, read only. Anywhere else (the host, single player, the main menu):
        // the editable section.
        private static void Draw(UIPanelBuilder b)
        {
            bool client;
            try { client = HostSetting.IsClient; } catch (Exception) { client = false; }
            if (!client) { (_section as SettingsSection)?.Build(b, true); return; }
            try { DrawClient(b); }
            catch (Exception e) { Util.WarnOnce("client-settings", "couldn't draw the host's Remote MU setting: ", e); }
        }

        private static void DrawClient(UIPanelBuilder b)
        {
            b.AddExpandable(Main.Name, ClientExpanded, c =>
            {
                bool? host = HostSetting.WorldValue;
                c.AddField("Remote MU", host.HasValue ? (host.Value ? "On (the host's setting)" : "Off (the host's setting)")
                                                      : "The host's setting hasn't arrived yet");
                HelperAir? air = HostSetting.WorldHelperAir;
                c.AddField("Helper engines work the air", air.HasValue ? HelperAirSetting.Name(air.Value) + " (the host's setting)"
                                                                     : "The host's setting hasn't arrived yet");
                bool? lash = HostSetting.WorldLashUp;
                c.AddField("Lash-up feed", lash.HasValue ? (lash.Value ? "On (the host's setting)" : "Off (the host's setting)")
                                                         : "The host's setting hasn't arrived yet");
                c.AddLabel("<style=Footnote>The host's setting rules here.</style>");
            }, ExpandableStyle.SectionHeader);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Stop()
        {
            PreferencesModsTab.Remove();
            _section = null;
        }
    }
}
