using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>A key combination written like "Ctrl+Alt+N".</summary>
    struct Hotkey
    {
        public uint Mods;
        public Keys Key;

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = new Hotkey();
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                bool last = i == parts.Length - 1;
                if (!last)
                {
                    if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) hotkey.Mods |= Native.MOD_CONTROL;
                    else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) hotkey.Mods |= Native.MOD_ALT;
                    else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) hotkey.Mods |= Native.MOD_SHIFT;
                    else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) hotkey.Mods |= Native.MOD_WIN;
                    else return false;
                }
                else
                {
                    Keys key;
                    if (part.Length == 0 || char.IsDigit(part[0])) return false; // reject raw numbers
                    if (!Enum.TryParse(part, true, out key)) return false;
                    if ((key & Keys.Modifiers) != 0 || key == Keys.None) return false;
                    hotkey.Key = key;
                }
            }
            // Global shortcuts take the keys away from every other program, so insist on at least two
            // modifiers: a mistake or a tampered settings file must not be able to claim Ctrl+C, Ctrl+V, Alt+F4...
            int modifierCount = 0;
            foreach (uint mod in new uint[] { Native.MOD_CONTROL, Native.MOD_ALT, Native.MOD_SHIFT, Native.MOD_WIN })
                if ((hotkey.Mods & mod) != 0) modifierCount++;
            return modifierCount >= 2;
        }

        public static string Format(Keys modifiers, Keys key)
        {
            List<string> parts = new List<string>();
            if ((modifiers & Keys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & Keys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & Keys.Shift) != 0) parts.Add("Shift");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }
    }
}
