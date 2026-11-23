using System;
using System.Runtime.CompilerServices;
using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;

namespace AuxFuelCars
{
    // The Remote MU settings as the host decides them (Remote MU, and 2.0.8's rear engines work the air). A world-level KeyValue object of this mod's own, registered with the
    // game's StateManager the way the game registers its own world objects ("tutorial", "timetable"): a
    // KeyValue object and RegisterPropertyObject with HostOnly writes). The game then does the rest: the host's write
    // goes to every connected client and into the snapshot a joining client gets, and the object is saved with the
    // world. Only the host writes it, from its own settings.json (Preferences > Mods), at every world load and every
    // save of the setting; clients only read it. A client's own setting applies only when it hosts or plays alone.
    //
    // The object is its own id, not a key on the game's "_game" object, so nothing of the game's is touched.
    internal static class HostSetting
    {
        internal const string ObjectId = "ashenmare.aux-fuel-cars";
        internal const string RemoteMuKey = "remoteMu";
        // 2.0.8: rear engines work the air. Every computer steps the air for the trains it simulates (the host's air
        // corrects a client's about once a second, TrainController.SendAirIfNeeded / HandleBatchCarAirUpdate), so all of
        // them follow the host's value, as for Remote MU. 2.1.2: three choices (Logic.HelperAir) as an Int under their
        // own key; the 2.0.8 to 2.1.1 bool key is removed from the world at the host's next write (every player runs the
        // same version: the mod is required, README.txt MULTIPLAYER), so a save keeps no dead key.
        internal const string HelperAirKey = "helperAir";
        internal const string OldRearAirKey = "rearAir";
        // 2.1.0: the lash-up feed. Only the host tops up, so only its value acts; it is shared so a client's Preferences
        // can show it.
        internal const string LashUpKey = "lashUp";

        private static KeyValueStorage _world;
        private static IDisposable _observer;
        private static IDisposable _helperObserver;
        private static IDisposable _lashObserver;
        // The world's value, cached by the observer so the MU postfix never touches the KeyValue dictionary (the AE's
        // caches may ask from another thread). -1: none (no world, or the host hasn't written it), 0 off, 1 on.
        private static volatile int _worldRemoteMu = -1;
        private static volatile int _worldHelperAir = -1;   // -1 none, else (int)HelperAir
        private static volatile int _worldLashUp = -1;

        // In a world, as a client: the setting is the host's, and the Preferences section says so.
        internal static bool InWorld => _world != null;

        internal static bool IsClient
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return _world != null && !StateManager.IsHost; }
        }

        // What the MU postfix obeys: the world's value when there is one, else this machine's own setting.
        internal static bool RemoteMu
        {
            get { int w = _worldRemoteMu; return w < 0 ? Main.Settings.remoteMu : w == 1; }
        }

        // The host's value as this machine last heard it; null when none has arrived.
        internal static bool? WorldValue
        {
            get { int w = _worldRemoteMu; return w < 0 ? (bool?)null : w == 1; }
        }

        // What the air hook obeys (RearAir.cs), the same way: the world's value, else this machine's own setting.
        internal static HelperAir HelperAir
        {
            get { int w = _worldHelperAir; return w < 0 ? Main.Settings.helperAir : (HelperAir)w; }
        }

        internal static HelperAir? WorldHelperAir
        {
            get { int w = _worldHelperAir; return w < 0 ? (HelperAir?)null : (HelperAir)w; }
        }

        // The world value as the host wrote it: an Int of the three choices; anything else is none (this machine's own).
        internal static int HelperAirOf(Value v)
            => v.Type == KeyValue.Runtime.ValueType.Int && v.IntValue >= (int)HelperAir.Off && v.IntValue <= (int)HelperAir.Always ? v.IntValue : -1;

        // What the diesel top-up obeys (TopUp.cs, host only), the same way.
        internal static bool LashUp
        {
            get { int w = _worldLashUp; return w < 0 ? Main.Settings.lashUp : w == 1; }
        }

        internal static bool? WorldLashUp
        {
            get { int w = _worldLashUp; return w < 0 ? (bool?)null : w == 1; }
        }

        // OnWorldLoad. Runs after the game restored the save's or the host's snapshot properties: an id restored before
        // it was registered is kept by the game and handed to this object as it registers (PropertyObjectManager).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            Detach();   // only this mod's references: unregistering here would throw away what the game just restored
            var kv = new KeyValueStorage();
            StateManager.Shared.RegisterPropertyObject(ObjectId, kv, AuthorizationRequirement.HostOnly);
            Attach(kv);
            Publish();
        }

        // OnWorldUnload (the game also drops every registration when the map unloads).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Unregister()
        {
            bool had = _world != null;
            Detach();
            if (had) StateManager.Shared?.UnregisterPropertyObject(ObjectId);
        }

        // On the host: write this machine's setting into the world, which the game sends to every client. A no-op when
        // the value is unchanged (KeyValueStorage.Set) and on a client (a client write would be rejected anyway).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Publish()
        {
            KeyValueStorage kv = _world;
            if (kv == null || !StateManager.IsHost) return;
            kv[RemoteMuKey] = Value.Bool(Main.Settings.remoteMu);
            kv[HelperAirKey] = Value.Int((int)Main.Settings.helperAir);
            if (!kv[OldRearAirKey].IsNull) kv[OldRearAirKey] = Value.Null();
            kv[LashUpKey] = Value.Bool(Main.Settings.lashUp);
        }

        // Split from Register so tools/probe can run it on a bare KeyValueStorage, without the game's StateManager.
        internal static void Attach(KeyValueStorage kv)
        {
            _world = kv;
            _observer = kv.Observe(RemoteMuKey, v => _worldRemoteMu = v.IsNull ? -1 : (v.BoolValue ? 1 : 0), true);
            _helperObserver = kv.Observe(HelperAirKey, v => _worldHelperAir = HelperAirOf(v), true);
            _lashObserver = kv.Observe(LashUpKey, v => _worldLashUp = v.IsNull ? -1 : (v.BoolValue ? 1 : 0), true);
        }

        internal static void Detach()
        {
            try { _observer?.Dispose(); } catch (Exception) { }
            try { _helperObserver?.Dispose(); } catch (Exception) { }
            try { _lashObserver?.Dispose(); } catch (Exception) { }
            _observer = null;
            _helperObserver = null;
            _lashObserver = null;
            _world = null;
            _worldRemoteMu = -1;
            _worldHelperAir = -1;
            _worldLashUp = -1;
        }
    }
}
