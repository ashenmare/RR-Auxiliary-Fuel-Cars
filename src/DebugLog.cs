// DebugLog.cs (Revision 2)
// One debug log for every ashenmare mod: a switch, one file per game session, one line per decision. For a bug report: turn
// it on, reproduce, send the file. Every mod that carries this file shares one switch and one file.
//
// USE
//   1. Copy this file into your mod. Change only the namespace line. It needs PathClean in the same namespace (SettingsSection.cs
//      or PathClean.cs, which every mod carries) and no game type.
//   2. OnModLoad:  DebugLog.Init("my-mod-id", "1.2.0");  then DebugLog.OnChanged("my-mod-id", on => { ...write your own
//      settings.json copy of the switch... });  OnModUnload: DebugLog.Forget("my-mod-id").
//   3. A decision:  if (DebugLog.On) DebugLog.Event("my-mod-id", "blast", "quarry", id, "lb", 120.5f);
//      Always behind the On test: the arguments are built only when it is true, so a mod with the log off allocates nothing.
//   4. The settings row: SettingField.Bool("debugLog", "Debug log (for bug reports)", false, true, help). When it is saved,
//      call DebugLog.Set(value) if value != DebugLog.On. OnChanged keeps every mod's own copy of the row in step.
//
// WHAT IT WRITES. <Documents>/Railroader/Logs/ashenmare-debug-<yyyyMMdd>-<HHmmss>.log (the folder the game uses, or the
// -homedir one). Each line: HH:MM:SS.mmm [mod-id] event key=value key=value. Numbers are written in the invariant culture
// (round-trip, so a small number keeps its digits), a value with a space or a quote is quoted, every string value goes through PathClean (paths, the
// user name), then the machine's name becomes <host>, the user's name <user>, a Steam id (17 digits from 7656119) <steamid> and an IPv4 address <ip>.
// A value is cut at 200 characters, a line at 1000. A file holds 5 MB; the next part is a new file whose first line is the
// header block: the header line (game, Harmony, OS family, the mods so far) and one line "[debug-log] mod id=<mod> version=<v>" for
// every mod that has registered. A mod that registers later, while the log is on, appends its own mod line (never twice in one file).
// Old files go by age and size, not by count: a file untouched for 7 days goes, then the oldest go until all of them are under
// 100 MB together; every part of the current session, and the open file, always stay. (Revision 1 kept the last 5 files, and four
// short restarts lost the session that mattered.) Lines are queued (at most 20000, then counted as dropped) and written by one
// background thread that starts on the first ON and ends when the switch is off. Whether the switch is on is kept as a
// file, ashenmare-debug.on, in the same folder, read once when the first copy loads (so it survives a restart).
//
// RULES
//   - Every copy stays identical except the namespace. If you change the file, bump Revision and keep THE SHARED CONTRACT.
//   - Never log: chat, a name the player typed (unless the event is about it), a path built from the home folder (PathClean
//     handles one that slips in), an account or machine name. A key is a short word, a value a number, a flag or an id.
//   - Nothing here names a game type; it can't be the reason a mod fails to load.
//
// THE SHARED CONTRACT (frozen: every copy of this file, of any revision, keeps it exactly)
//   - AppDomain.CurrentDomain.GetData("ashenmare.debuglog.v1") is a Dictionary<string, object> with ordinal keys. The first
//     copy that needs it creates it, holding lock (AppDomain.CurrentDomain), and fills every key below before it stores it.
//   - "cell" bool[1]: the switch. "queue" System.Collections.Concurrent.ConcurrentQueue<string>: finished lines. "cnt" int[2]:
//     lines queued, lines dropped (both through Interlocked). "cfg" long[3]: bytes per file, files kept, queue limit.
//     "lock" object: guards "mods", "listeners", "run" and "env". "mods" List<KeyValuePair<string, string>>: mod id and
//     version. "listeners" Dictionary<string, Action<bool>>: by mod id, called when the switch changes. "run" bool[1]: a
//     writer thread is running. "file" string[1] and "fstat" long[4]: the open file's path, its bytes, the part number,
//     when to retry a failed open (UTC ticks) and the stamp of the newest file name (ticks), kept for the next writer run. "env" Dictionary<string, string>: game, harmony, os. "signal" AutoResetEvent: wakes the
//     writer. "busy" int[1]: 1 while the writer holds lines it hasn't flushed. "dir" string (optional): the folder to use
//     instead of the game's, set by a test. "session" string: yyyyMMdd-HHmmss of the first copy's load.
//   - Revision 2 adds nothing to the slot and keeps the key and every shape. Revision 1 and 2 copies may share one slot (a mod
//     rolled out later than another): Revision 2 raises "cfg"[1] (files kept) to 100000 so a Revision 1 writer stops deleting by count.
//   - Readers ignore keys they don't know and treat a missing or mistyped key as absent, so a later revision can add keys.
//   - A line is "HH:MM:SS.mmm [mod] event key=value ..." and the folder, file names and the .on file are as above.
//   - Nothing else crosses between mods.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AuxFuelCars
{
    internal static class DebugLog
    {
        internal const int Revision = 2;
        private const string SlotKey = "ashenmare.debuglog.v1", Prefix = "ashenmare-debug-", Suffix = ".log", FlagName = "ashenmare-debug.on";
        private const long DefaultCap = 5L * 1024 * 1024;
        private const int DefaultKeep = 5, RaiseKeep = 100000, DefaultMaxQueue = 20000, MaxLine = 1000, MaxValue = 200, FlushMs = 250;

        // Retention (Revision 2): files untouched this long go; then the oldest go until the total is under the size. Settable by tests.
        internal static long KeepDays = 7, KeepBytes = 100L * 1024 * 1024;

        private static bool[] _cell = new bool[1];   // this copy's view of the shared switch (false until Init)
        private static ConcurrentQueue<string> _queue;
        private static int[] _cnt;
        private static long[] _cfg;
        private static Dictionary<string, object> _slot;

        // The switch. One array read; nothing is allocated.
        internal static bool On => _cell[0];

        // ---- connect ----

        internal static void Init(string modId, string version)
        {
            try
            {
                Dictionary<string, object> slot = Slot(out bool created);
                _slot = slot; _cell = (bool[])slot["cell"]; _queue = (ConcurrentQueue<string>)slot["queue"];
                _cnt = (int[])slot["cnt"]; _cfg = (long[])slot["cfg"];
                if (_cfg.Length > 1 && _cfg[1] < RaiseKeep) _cfg[1] = RaiseKeep;   // a Revision 1 writer must not delete by count
                string dir = DirOf(slot);
                if (created && dir != null && File.Exists(Path.Combine(dir, FlagName))) Switch(slot, true);
                object gate = slot["lock"];
                var mods = (List<KeyValuePair<string, string>>)slot["mods"];
                lock (gate)
                {
                    for (int i = mods.Count - 1; i >= 0; i--) if (mods[i].Key == modId) mods.RemoveAt(i);
                    mods.Add(new KeyValuePair<string, string>(modId, version ?? ""));
                }
                if (_cell[0])
                {
                    Enqueue("debug-log", "mod", false, new object[] { "id", modId, "version", version });
                    string d2 = DirOf(slot);
                    if (d2 != null) Prune(d2, ((string[])slot["file"])[0], slot["session"] as string ?? "");
                }
            }
            catch (Exception) { }
        }

        // A mod leaving: its listener goes (the assembly stays loaded, so the mod's own entry in "mods" stays too).
        internal static void Forget(string modId)
        {
            try
            {
                if (_slot == null) return;
                lock (_slot["lock"]) ((Dictionary<string, Action<bool>>)_slot["listeners"]).Remove(modId);
            }
            catch (Exception) { }
        }

        internal static void OnChanged(string modId, Action<bool> listener)
        {
            try
            {
                if (_slot == null) return;
                lock (_slot["lock"]) ((Dictionary<string, Action<bool>>)_slot["listeners"])[modId] = listener;
            }
            catch (Exception) { }
        }

        // Turn the switch on or off for every mod, now. A fresh start with it on writes the .on file; off removes it.
        internal static void Set(bool on)
        {
            try { if (_slot != null) Switch(_slot, on); } catch (Exception) { }
        }

        private static Dictionary<string, object> Slot(out bool created)
        {
            AppDomain d = AppDomain.CurrentDomain;
            created = false;
            if (d.GetData(SlotKey) is Dictionary<string, object> held) return held;
            lock (d)
            {
                held = d.GetData(SlotKey) as Dictionary<string, object>;
                if (held != null) return held;
                DateTime now = DateTime.Now;
                held = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "cell", new bool[1] }, { "queue", new ConcurrentQueue<string>() }, { "cnt", new int[2] },
                    { "cfg", new long[] { DefaultCap, DefaultKeep, DefaultMaxQueue } }, { "lock", new object() },
                    { "mods", new List<KeyValuePair<string, string>>() }, { "listeners", new Dictionary<string, Action<bool>>(StringComparer.Ordinal) },
                    { "run", new bool[1] }, { "env", Environment_() }, { "signal", new AutoResetEvent(false) }, { "busy", new int[1] },
                    { "file", new string[1] }, { "fstat", new long[4] },
                    { "session", now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) },
                };
                d.SetData(SlotKey, held);
                created = true;
                return held;
            }
        }

        private static void Switch(Dictionary<string, object> slot, bool on)
        {
            var cell = (bool[])slot["cell"];
            object gate = slot["lock"];
            Action<bool>[] call;
            lock (gate)
            {
                if (cell[0] == on) return;
                cell[0] = on;
                if (on && !((bool[])slot["run"])[0]) { ((bool[])slot["run"])[0] = true; StartWriter(slot); }
                var ls = (Dictionary<string, Action<bool>>)slot["listeners"];
                call = new Action<bool>[ls.Count]; ls.Values.CopyTo(call, 0);
            }
            string dir = DirOf(slot);
            if (dir != null)
            {
                try
                {
                    string flag = Path.Combine(dir, FlagName);
                    if (on) { Directory.CreateDirectory(dir); if (!File.Exists(flag)) File.WriteAllText(flag, "on\n"); }
                    else if (File.Exists(flag)) File.Delete(flag);
                }
                catch (Exception) { }
            }
            Enqueue("debug-log", on ? "on" : "off", true, null);
            ((AutoResetEvent)slot["signal"]).Set();
            for (int i = 0; i < call.Length; i++) { try { call[i](on); } catch (Exception) { } }
        }

        // ---- the folder ----

        // Mirrors Game.UserPaths.RailroaderDirectory (UserPaths.cs): -homedir <absolute path> else Documents/Railroader; then Logs.
        private static string DirOf(Dictionary<string, object> slot)
        {
            if (slot.TryGetValue("dir", out object o) && o is string s && s.Length > 0) return s;
            return LogsFolder();
        }

        internal static string LogsFolder()
        {
            string root = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], "-homedir", StringComparison.OrdinalIgnoreCase)) { root = i + 1 < args.Length && Path.IsPathRooted(args[i + 1]) ? args[i + 1] : null; break; }
            if (root == null)
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) return null;
                root = Path.Combine(docs, "Railroader");
            }
            return Path.Combine(root, "Logs");
        }

        // ---- an event ----

        internal static void Event(string mod, string name, params object[] kv) { if (_cell[0]) Enqueue(mod, name, false, kv); }

        private static void Enqueue(string mod, string name, bool forced, object[] kv)
        {
            ConcurrentQueue<string> q = _queue; int[] cnt = _cnt;
            if (q == null || (!_cell[0] && !forced)) return;
            string line;
            try { line = Format(DateTime.Now, mod, name, kv); } catch (Exception) { return; }
            if (Interlocked.Increment(ref cnt[0]) > _cfg[2]) { Interlocked.Decrement(ref cnt[0]); Interlocked.Increment(ref cnt[1]); return; }
            q.Enqueue(line);
            if (cnt[0] >= 256) ((AutoResetEvent)_slot["signal"]).Set();
        }

        // The line, without the newline.
        internal static string Format(DateTime at, string mod, string name, object[] kv)
        {
            var sb = new StringBuilder(96);
            sb.Append(at.Hour.ToString("00", CultureInfo.InvariantCulture)).Append(':').Append(at.Minute.ToString("00", CultureInfo.InvariantCulture)).Append(':')
              .Append(at.Second.ToString("00", CultureInfo.InvariantCulture)).Append('.').Append(at.Millisecond.ToString("000", CultureInfo.InvariantCulture));
            sb.Append(" [").Append(Word(mod)).Append("] ").Append(Word(name));
            if (kv != null)
                for (int i = 0; i + 1 < kv.Length; i += 2)
                    sb.Append(' ').Append(Word(kv[i] as string)).Append('=').Append(Value(kv[i + 1]));
            if (sb.Length > MaxLine) { sb.Length = MaxLine - 3; sb.Append("..."); }
            return sb.ToString();
        }

        private static string Word(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            StringBuilder b = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
                if (!ok) { if (b == null) b = new StringBuilder(s); b[i] = '_'; }
            }
            return b == null ? s : b.ToString();
        }

        private static string Value(object v)
        {
            string s;
            if (v == null) return "-";
            if (v is string str) s = Scrub(str);
            else if (v is bool bo) return bo ? "true" : "false";
            else if (v is float f) { if (float.IsNaN(f)) return "nan"; if (float.IsInfinity(f)) return f > 0 ? "inf" : "-inf"; s = f.ToString("R", CultureInfo.InvariantCulture); }
            else if (v is double dd) { if (double.IsNaN(dd)) return "nan"; if (double.IsInfinity(dd)) return dd > 0 ? "inf" : "-inf"; s = dd.ToString("R", CultureInfo.InvariantCulture); }
            else if (v is int || v is long || v is short || v is byte || v is uint || v is ulong) s = Ids(Convert.ToString(v, CultureInfo.InvariantCulture));
            else s = Scrub(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");
            if (s.Length > MaxValue) s = s.Substring(0, MaxValue) + "...";
            return Quote(s);
        }

        private static string Quote(string s)
        {
            bool plain = s.Length > 0;
            for (int i = 0; plain && i < s.Length; i++)
            {
                char c = s[i];
                plain = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == ':' || c == '/' || c == '+' || c == '-' || c == '<' || c == '>' || c == '@' || c == ',' || c == '~' || c == '%';
            }
            if (plain) return s;
            var b = new StringBuilder(s.Length + 2).Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < ' ' || c == (char)0x7f || c == (char)0x2028 || c == (char)0x2029) b.Append(' ');
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        // PathClean (paths and the user name), then the machine's name, a Steam id and an IPv4 address.
        internal static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = PathClean.CleanText(s, MaxValue + 40);
            string host = null, user = null;
            try { host = Environment.MachineName; user = Environment.UserName; } catch (Exception) { }
            if (host != null && host.Length >= 3) s = ReplaceWord(s, host, "<host>");
            if (user != null && user.Length >= 3) s = ReplaceWord(s, user, "<user>");
            return Ids(s);
        }

        private static string ReplaceWord(string s, string word, string with)
        {
            int at = 0; StringBuilder b = null;
            while (true)
            {
                int i = s.IndexOf(word, at, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                bool left = i == 0 || !char.IsLetterOrDigit(s[i - 1]), right = i + word.Length >= s.Length || !char.IsLetterOrDigit(s[i + word.Length]);
                if (b == null) b = new StringBuilder();
                b.Append(s, at, i - at);
                b.Append(left && right ? with : s.Substring(i, word.Length));
                at = i + word.Length;
            }
            if (b == null) return s;
            return b.Append(s, at, s.Length - at).ToString();
        }

        private static bool IsDigit(string s, int i) => i >= 0 && i < s.Length && s[i] >= '0' && s[i] <= '9';

        // A Steam id: 17 digits from 7656119 (not part of a longer run). An IPv4 address: four runs of 1 to 3 digits, each at
        // most 255, joined by dots, with no digit or dot-digit around it.
        private static string Ids(string s)
        {
            StringBuilder b = null;
            int at = 0, i = 0;
            while (i < s.Length)
            {
                if (!IsDigit(s, i)) { i++; continue; }
                int start = i;
                while (IsDigit(s, i)) i++;
                int len = i - start;
                bool isId = len == 17 && string.CompareOrdinal(s, start, "7656119", 0, 7) == 0;
                int end = i; bool isIp = false;
                if (!isId && len <= 3 && !(start > 0 && (s[start - 1] == '.' && IsDigit(s, start - 2))))
                {
                    int p = start, parts = 0; bool ok = true;
                    while (parts < 4)
                    {
                        int q = p; while (IsDigit(s, q)) q++;
                        int n = q - p;
                        if (n < 1 || n > 3 || int.Parse(s.Substring(p, n), CultureInfo.InvariantCulture) > 255) { ok = false; break; }
                        parts++; p = q;
                        if (parts < 4) { if (p < s.Length && s[p] == '.') p++; else { ok = false; break; } }
                    }
                    if (ok && !IsDigit(s, p) && !(p < s.Length && s[p] == '.' && IsDigit(s, p + 1))) { isIp = true; end = p; }
                }
                if (isId || isIp)
                {
                    if (b == null) b = new StringBuilder();
                    b.Append(s, at, start - at).Append(isId ? "<steamid>" : "<ip>");
                    at = end; i = end;
                }
            }
            if (b == null) return s;
            return b.Append(s, at, s.Length - at).ToString();
        }

        // ---- environment (read on the main thread, once) ----

        private static Dictionary<string, string> Environment_()
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal) { { "game", "unknown" }, { "harmony", "unknown" }, { "os", "unknown" } };
            try
            {
                // Application.version, by name, so this file names no game type. Read on the thread that loads the mod.
                Type app = Type.GetType("UnityEngine.Application, UnityEngine.CoreModule");
                object v = app?.GetProperty("version", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null, null);
                if (v is string gv && gv.Length > 0) env["game"] = gv;
            }
            catch (Exception) { }
            try
            {
                var seen = new List<string>();
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Reflection.AssemblyName n = a.GetName();
                    if (n.Name == "0Harmony" && n.Version != null) { string t = n.Version.Major + "." + n.Version.Minor + "." + n.Version.Build; if (!seen.Contains(t)) seen.Add(t); }
                }
                if (seen.Count > 0) env["harmony"] = string.Join("+", seen.ToArray());
            }
            catch (Exception) { }
            try
            {
                PlatformID p = Environment.OSVersion.Platform;
                string os = p == PlatformID.Win32NT ? "windows" : p == PlatformID.MacOSX ? "mac" : "linux";
                // Proton and Wine report Windows; their launcher leaves these names in the environment.
                if (os == "windows" && (Environment.GetEnvironmentVariable("WINEPREFIX") != null || Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH") != null
                                        || Environment.GetEnvironmentVariable("PROTON_VERSION") != null)) os = "windows-wine";
                env["os"] = os;
            }
            catch (Exception) { }
            return env;
        }

        // ---- the writer ----

        private static void StartWriter(Dictionary<string, object> slot)
        {
            var t = new Thread(() => WriteLoop(slot)) { IsBackground = true, Name = "ashenmare-debug-log" };
            t.Start();
        }

        // Test hook: wait until every queued line is on disk.
        internal static bool Flush(int timeoutMs)
        {
            Dictionary<string, object> slot = _slot; if (slot == null) return true;
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end)
            {
                ((AutoResetEvent)slot["signal"]).Set();
                bool idle = ((ConcurrentQueue<string>)slot["queue"]).IsEmpty && ((int[])slot["busy"])[0] == 0;
                if (idle) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        // The open file of one writer run. What outlives a run (the file's path, its size, the part number, when to retry) is kept
        // in the slot's "file" and "fstat" (string[1], long[4]) so a copy from another mod can carry on after an off and on.
        private sealed class Sink
        {
            internal StreamWriter W; internal string Path; internal long Bytes, Part; internal HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        }

        private static void WriteLoop(Dictionary<string, object> slot)
        {
            var queue = (ConcurrentQueue<string>)slot["queue"];
            var cnt = (int[])slot["cnt"]; var cfg = (long[])slot["cfg"]; var busy = (int[])slot["busy"];
            var cell = (bool[])slot["cell"]; var run = (bool[])slot["run"]; var signal = (AutoResetEvent)slot["signal"];
            var file = (string[])slot["file"]; var fstat = (long[])slot["fstat"];
            object gate = slot["lock"];
            var sink = new Sink { Path = file[0], Bytes = fstat[0], Part = fstat[1] };
            while (true)
            {
                signal.WaitOne(FlushMs);
                busy[0] = 1;
                try
                {
                    int dropped = Interlocked.Exchange(ref cnt[1], 0);
                    while (queue.TryDequeue(out string line))
                    {
                        Interlocked.Decrement(ref cnt[0]);
                        Put(slot, sink, line, cfg);
                    }
                    if (dropped > 0) Put(slot, sink, Format(DateTime.Now, "debug-log", "dropped", new object[] { "lines", dropped }), cfg);
                    if (sink.W != null) sink.W.Flush();
                }
                catch (Exception) { CloseSink(sink, false); }
                busy[0] = 0;
                lock (gate)
                {
                    if (!cell[0] && queue.IsEmpty)
                    {
                        CloseSink(sink, true);
                        file[0] = sink.Path; fstat[0] = sink.Bytes; fstat[1] = sink.Part;
                        run[0] = false; return;
                    }
                }
            }
        }

        // keepName: the file stays the one the next run carries on with (an off); else the next line opens a new one.
        private static void CloseSink(Sink s, bool keepName)
        {
            try { s.W?.Dispose(); } catch (Exception) { }
            s.W = null;
            if (!keepName) s.Path = null;
        }

        private static void Put(Dictionary<string, object> slot, Sink s, string line, long[] cfg)
        {
            long need = Encoding.UTF8.GetByteCount(line) + 1;
            if (s.W != null && s.Bytes + need > cfg[0]) CloseSink(s, false);
            if (s.W == null)
            {
                var fstat = (long[])slot["fstat"];
                if (DateTime.UtcNow.Ticks < fstat[2]) return;
                string dir = DirOf(slot);
                if (dir == null) { fstat[2] = DateTime.UtcNow.AddSeconds(5).Ticks; return; }
                try { Open(slot, s, dir, cfg, need); }
                catch (Exception) { CloseSink(s, false); fstat[2] = DateTime.UtcNow.AddSeconds(5).Ticks; return; }
            }
            string mk = ModKey(line);
            if (mk != null && !s.Seen.Add(mk)) return;   // this mod already has its line in this file
            s.W.Write(line); s.W.Write('\n'); s.Bytes += need;
        }

        // "[debug-log] mod id=<mod> version=<v>" (what follows the 12-character time and its space), or null.
        private const string ModHead = "[debug-log] mod id=";
        private static string ModKey(string line) => line.Length > 13 + ModHead.Length && string.CompareOrdinal(line, 13, ModHead, 0, ModHead.Length) == 0 ? line.Substring(13) : null;

        private static void WriteModLines(Sink s, List<KeyValuePair<string, string>> mods)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                string l = Format(DateTime.Now, "debug-log", "mod", new object[] { "id", mods[i].Key, "version", mods[i].Value });
                string k = ModKey(l);
                if (k == null || !s.Seen.Add(k)) continue;
                s.W.Write(l); s.W.Write('\n'); s.Bytes += Encoding.UTF8.GetByteCount(l) + 1;
            }
        }

        // The file of the last run when it is still there with room; else a new part: named for its start time (a second
        // later when that name is taken, so names sort in order), the header first, then the oldest files over the limit go.
        private static void Open(Dictionary<string, object> slot, Sink s, string dir, long[] cfg, long need)
        {
            Directory.CreateDirectory(dir);
            if (s.Path != null && File.Exists(s.Path) && s.Bytes + need <= cfg[0])
            {
                // The file carries on: the mod lines it already has are read back, then a mod that registered while the log was off is added.
                s.Seen = new HashSet<string>(StringComparer.Ordinal);
                try { foreach (string l in File.ReadLines(s.Path)) { string k = ModKey(l); if (k != null) s.Seen.Add(k); } } catch (Exception) { }
                s.W = new StreamWriter(new FileStream(s.Path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { NewLine = "\n" };
                List<KeyValuePair<string, string>> have;
                lock (slot["lock"]) have = new List<KeyValuePair<string, string>>((List<KeyValuePair<string, string>>)slot["mods"]);
                WriteModLines(s, have);
                return;
            }
            // Names only ever go forward: after a second's worth of parts the clock is behind the names, and a name freed by a
            // delete must not be taken again (it would sort before the newer parts and be the next one deleted).
            var fstat = (long[])slot["fstat"];
            DateTime t = DateTime.Now; string path;
            if (t.Ticks <= fstat[3]) t = new DateTime(fstat[3]).AddSeconds(1);
            while (File.Exists(path = Path.Combine(dir, Prefix + t.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Suffix))) t = t.AddSeconds(1);
            fstat[3] = t.Ticks;
            var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            s.W = new StreamWriter(fs, new UTF8Encoding(false)) { NewLine = "\n" };
            s.Path = path; s.Bytes = 0; s.Part++; s.Seen = new HashSet<string>(StringComparer.Ordinal);
            var env = (Dictionary<string, string>)slot["env"];
            var mods = (List<KeyValuePair<string, string>>)slot["mods"];
            var list = new StringBuilder();
            List<KeyValuePair<string, string>> snap;
            lock (slot["lock"]) snap = new List<KeyValuePair<string, string>>(mods);
            for (int i = 0; i < snap.Count; i++) { if (i > 0) list.Append(','); list.Append(Word(snap[i].Key)).Append('@').Append(Word(snap[i].Value)); }
            string head = Format(DateTime.Now, "debug-log", "header", new object[] { "rev", Revision, "session", (string)slot["session"], "part", s.Part,
                "game", env["game"], "harmony", env["harmony"], "os", env["os"], "mods", list.ToString() });
            s.W.Write(head); s.W.Write('\n'); s.Bytes += Encoding.UTF8.GetByteCount(head) + 1;
            WriteModLines(s, snap);
            Prune(dir, path, slot["session"] as string ?? "");
        }

        // Exactly ashenmare-debug-<8 digits>-<6 digits>.log, and never the open one.
        private static bool Ours(string name)
        {
            if (name.Length != Prefix.Length + 15 + Suffix.Length || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Suffix, StringComparison.Ordinal)) return false;
            for (int i = 0; i < 15; i++)
            {
                char c = name[Prefix.Length + i];
                if (i == 8 ? c != '-' : !(c >= '0' && c <= '9')) return false;
            }
            return true;
        }

        // Retention. Never touched: a name that is not exactly ours, the open file, and every part of this session (a name from the
        // session's start on). Of the rest, a file not written for KeepDays goes; then the oldest go until what is left of all
        // our files is at most KeepBytes.
        internal static void Prune(string dir, string keepPath, string session)
        {
            try
            {
                string first = Prefix + session + Suffix;
                var all = new List<string>();
                foreach (string f in Directory.GetFiles(dir)) { string n = Path.GetFileName(f); if (Ours(n)) all.Add(n); }
                all.Sort(StringComparer.Ordinal);
                DateTime cutoff = DateTime.UtcNow.AddDays(-(double)KeepDays);
                var left = new List<string>(); long total = 0;
                foreach (string n in all)
                {
                    string p = Path.Combine(dir, n);
                    bool mine = string.CompareOrdinal(n, first) >= 0 || p == keepPath;
                    FileInfo fi = new FileInfo(p);
                    if (!mine && fi.LastWriteTimeUtc < cutoff) { try { File.Delete(p); continue; } catch (Exception) { } }
                    left.Add(n); total += fi.Exists ? fi.Length : 0;
                }
                for (int i = 0; i < left.Count && total > KeepBytes; i++)
                {
                    string n = left[i], p = Path.Combine(dir, n);
                    if (string.CompareOrdinal(n, first) >= 0 || p == keepPath) continue;
                    long len = 0; try { len = new FileInfo(p).Length; File.Delete(p); total -= len; } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
    }
}
