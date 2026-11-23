using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AuxFuelCars
{
    // 2.1.2: the "Helper engines work the air" setting in settings.json (BCL and Newtonsoft only: tools/gamecheck runs
    // it with the game's own Newtonsoft, and Main.LoadSettings may name it).
    //
    // Keys, the first one present deciding:
    //   helperAir  (2.1.2, the dropdown; the key in any case, 2.2.0) "Off", "By hand only" or "Always"; trimmed, any case, as the section's dropdown
    //              reads a choice (SettingsSection ValueIn). Any other value, null too, is the default for this setting
    //              alone, as the dropdown shows it, and doesn't fall through to the older key (so the mod runs what the
    //              dropdown shows; tools/gamecheck checks it with the section's own ValueIn).
    //   rearAir    (2.0.8 to 2.1.1, a toggle) true = By hand only (2.1.2's choice for it, kept),
    //              false = Off. Read only when helperAir isn't in the file; read as the toggle was (the key in any case; a
    //              bool, a number or a text Newtonsoft converts); anything else is the default.
    //
    // Migration: a readable file with no helperAir and a readable rearAir gets "helperAir" added at load, before the
    // settings section reads the file, so the dropdown shows what the mod runs (with false it would show the default,
    // Always, while the mod ran Off, and the first Save would quietly turn helpers back on). Written for true too,
    // so a later change of the default can't change what an upgraded player chose. The key is inserted as text right
    // after the root brace (Industry Car Order 1.3.0's way), so the rest of the file stays as written; rearAir is kept,
    // unread from then on.
    internal static class HelperAirSetting
    {
        internal const string Key = "helperAir";
        internal const string OldKey = "rearAir";

        // The dropdown's texts in its order; index = (int)HelperAir.
        internal static readonly string[] Options = { "Off", "By hand only", "Always" };

        // Always by default. It is the value of a missing or unreadable helperAir
        // with no rearAir to migrate; a key already there keeps its value, and the migration writes its own.
        internal const HelperAir Default = HelperAir.Always;

        internal static string Name(HelperAir a) => Options[(int)a];

        // Where the value came from: the new key, the older toggle, or neither (the default).
        internal enum Source { Default, Choice, OldToggle }

        internal static HelperAir Read(JObject o, out Source from)
        {
            from = Source.Default;
            if (o == null) return Default;
            // 2.2.0: the key in any case, as the settings section (Revision 10) and JsonConvert find it.
            if (o.TryGetValue(Key, StringComparison.OrdinalIgnoreCase, out JToken t))
            {
                from = Source.Choice;
                if (t != null && t.Type == JTokenType.String)
                {
                    string s = ((string)t).Trim();
                    for (int i = 0; i < Options.Length; i++)
                        if (string.Equals(Options[i], s, StringComparison.OrdinalIgnoreCase)) return (HelperAir)i;
                }
                return Default;
            }
            // As the toggle's own binding read it (2.0.8 to 2.1.1: Newtonsoft's field binding ignores
            // a key's case, and converts 0.0 and 1.0 to a bool), so "RearAir": false and "rearAir": 0.0 stay Off.
            if (o.TryGetValue(OldKey, StringComparison.OrdinalIgnoreCase, out JToken old) && old.Type != JTokenType.Null
                && (old.Type == JTokenType.Boolean || old.Type == JTokenType.Integer || old.Type == JTokenType.Float || old.Type == JTokenType.String))
            {
                try
                {
                    bool on = old.ToObject<bool>();
                    from = Source.OldToggle;
                    return on ? HelperAir.ByHand : HelperAir.Off;
                }
                catch (Exception) { }
            }
            return Default;
        }

        // The file's text with "helperAir": "<name>" right after the root object's opening brace, so comments and the
        // layout stay as written. Null unless the result parses to the same object plus that one key (a key already
        // there, or a file it can't place the key in, is left alone).
        internal static string Insert(string text, HelperAir a)
        {
            if (text == null) return null;
            int brace = RootBrace(text);
            if (brace < 0) return null;
            string name = Name(a);
            string made = text.Substring(0, brace + 1) + " \"" + Key + "\": \"" + name + "\"," + text.Substring(brace + 1);
            try
            {
                JObject was = JObject.Parse(text), now = JObject.Parse(made);
                if (was == null || now == null || was.ContainsKey(Key) || now[Key]?.Type != JTokenType.String || (string)now[Key] != name) return null;
                now.Remove(Key);
                return JToken.DeepEquals(was, now) ? made : null;
            }
            catch (Exception) { return null; }
        }

        // The same edit on the file's bytes, so the rest of the file stays as it was. Only a file
        // that is UTF-8 (with or without a byte order mark) is edited, written back with or without its mark as it was.
        // Anything else (UTF-16, or bytes that aren't valid UTF-8, such as a Latin-1 comment) is left alone: null.
        internal static byte[] InsertBytes(byte[] raw, HelperAir a)
        {
            if (raw == null) return null;
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            int skip = bom ? 3 : 0;
            string text;
            try { text = new UTF8Encoding(false, true).GetString(raw, skip, raw.Length - skip); }
            catch (ArgumentException) { return null; }
            string made = Insert(text, a);
            if (made == null) return null;
            byte[] body = new UTF8Encoding(false).GetBytes(made);
            if (!bom) return body;
            var all = new byte[3 + body.Length];
            all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
            Buffer.BlockCopy(body, 0, all, 3, body.Length);
            return all;
        }

        // The index of the root object's '{': leading white space, a byte order mark and // and /* */ comments are
        // skipped; anything else is -1.
        private static int RootBrace(string text)
        {
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '/' && i + 1 < n && text[i + 1] == '/') { while (i < n && text[i] != '\n') i++; }
                else if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) return -1;
                    i = end + 2;
                }
                else if (c == '{') return i;
                else if (char.IsWhiteSpace(c) || c == '﻿') i++;
                else return -1;
            }
            return -1;
        }
    }
}
