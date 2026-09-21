using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PoeStashPricer
{
    public class ParsedItem
    {
        public string ItemClass;
        public string Rarity;
        public string Name;
        public string BaseType;
        public int Stack = 1;
        public bool IsStackable;
        public int Level;
        public bool Unidentified;
        /// <summary>Stackable ("Shift click to unstack") but the copied text has no stack size, so the count
        /// has to be read from the number shown on screen (Simulacrum, Shattered Triskelion...).</summary>
        public bool NeedsCount;

        public string DisplayName
        {
            get
            {
                if (BaseType != null && string.Equals(Rarity, "Unique", StringComparison.OrdinalIgnoreCase))
                    return Name + " (" + BaseType + ")";
                return Name;
            }
        }

        /// <summary>Items that can span several cells and should be merged when adjacent cells copy the same text.</summary>
        public bool IsMultiCellCandidate
        {
            get
            {
                if (IsStackable) return false;
                string r = (Rarity ?? "").ToLowerInvariant();
                if (r == "currency" || r == "gem") return false;
                if ((ItemClass ?? "").IndexOf("Gem", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return true;
            }
        }
    }

    /// <summary>Parses the text Path of Exile 2 puts on the clipboard for Ctrl+C over an item.</summary>
    public static class ItemParser
    {
        // Starts with a digit and stops at "/": no two parts of the pattern can match the same spaces, so a long
        // odd line can't make the match slow. Thousands are separated by ".", "," or a (non-breaking) space.
        static readonly Regex StackRx = new Regex(@"^Stack Size:\s*(\d[\d.,   ]*)/", RegexOptions.Compiled);
        static readonly Regex LevelRx = new Regex(@"^Level:\s*(\d+)", RegexOptions.Compiled);

        public static bool LooksLikeItem(string text)
        {
            return text != null && (text.Contains("Rarity:") || text.Contains("Item Class:"));
        }

        public static ParsedItem Parse(string text)
        {
            if (!LooksLikeItem(text)) return null;
            string[] lines = text.Replace("\r", "").Split('\n');
            ParsedItem it = new ParsedItem();

            int i = 0;
            for (; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.StartsWith("--------")) break;
                if (l.Length == 0) continue;
                if (l.StartsWith("Item Class:")) it.ItemClass = l.Substring(11).Trim();
                else if (l.StartsWith("Rarity:")) it.Rarity = l.Substring(7).Trim();
                else if (it.Name == null) it.Name = l;
                else if (it.BaseType == null) it.BaseType = l;
            }
            if (it.Name == null) return null;

            bool levelSeen = false;
            foreach (string raw in lines)
            {
                string l = raw.Trim();
                Match m = StackRx.Match(l);
                if (m.Success)
                {
                    StringBuilder digits = new StringBuilder();
                    foreach (char ch in m.Groups[1].Value) if (char.IsDigit(ch)) digits.Append(ch);
                    int n;
                    if (int.TryParse(digits.ToString(), out n) && n > 0) { it.Stack = n; it.IsStackable = true; }
                    continue;
                }
                if (!levelSeen)
                {
                    m = LevelRx.Match(l);
                    if (m.Success) { it.Level = int.Parse(m.Groups[1].Value); levelSeen = true; continue; }
                }
                if (l == "Unidentified") it.Unidentified = true;
            }
            it.NeedsCount = !it.IsStackable && text.Contains("Shift click to unstack");
            return it;
        }
    }
}
