using System;

namespace StayFocus
{
    /// <summary>
    /// To-do items inside a note. An item is a line that starts with a box character, so a checklist
    /// stays plain text in the notes file: "☐ call the bank" / "☑ call the bank".
    /// </summary>
    static class Checklist
    {
        public const char Open = '☐';    // ☐
        public const char Ticked = '☑';  // ☑
        public const string OpenPrefix = "☐ ";

        public static bool IsBox(char c)
        {
            return c == Open || c == Ticked;
        }

        /// <summary>Index of the first character of the line that contains the given position.</summary>
        public static int LineStart(string text, int index)
        {
            if (index > text.Length) index = text.Length;
            return index > 0 ? text.LastIndexOf('\n', index - 1) + 1 : 0;
        }

        /// <summary>Index just after the last character of the line (its line break, or the end of the text).</summary>
        public static int LineEnd(string text, int index)
        {
            if (index > text.Length) index = text.Length;
            int end = text.IndexOfAny(new char[] { '\r', '\n' }, index);
            return end < 0 ? text.Length : end;
        }

        /// <summary>Position of the box of the line starting at lineStart, or -1 if that line is not an item.</summary>
        public static int BoxIndex(string text, int lineStart)
        {
            int i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++; // indented items count too
            return i < text.Length && IsBox(text[i]) ? i : -1;
        }

        /// <summary>How many items a note has, and how many of them are ticked.</summary>
        public static void Count(string text, out int done, out int total)
        {
            done = 0;
            total = 0;
            if (string.IsNullOrEmpty(text)) return;
            int start = 0;
            while (true)
            {
                int box = BoxIndex(text, start);
                if (box >= 0)
                {
                    total++;
                    if (text[box] == Ticked) done++;
                }
                int lineBreak = text.IndexOf('\n', start);
                if (lineBreak < 0) return;
                start = lineBreak + 1;
            }
        }

        /// <summary>
        /// Adds a box to every line of the block, or removes the boxes when every line already has one.
        /// Blank lines are left alone unless the block is a single line.
        /// </summary>
        public static string ToggleItems(string block)
        {
            string[] lines = block.Replace("\r\n", "\n").Split('\n');
            bool single = lines.Length == 1;

            bool allItems = true;
            bool anyContent = false;
            foreach (string line in lines)
            {
                if (line.Trim().Length == 0) continue;
                anyContent = true;
                if (BoxIndex(line, 0) < 0) allItems = false;
            }
            bool remove = anyContent && allItems;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int box = BoxIndex(line, 0);
                if (remove)
                {
                    if (box < 0) continue;
                    int after = box + 1;
                    if (after < line.Length && line[after] == ' ') after++;
                    lines[i] = line.Substring(0, box) + line.Substring(after);
                }
                else if (box < 0 && (single || line.Trim().Length > 0))
                {
                    int indent = 0;
                    while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t')) indent++;
                    lines[i] = line.Substring(0, indent) + OpenPrefix + line.Substring(indent);
                }
            }
            return string.Join("\r\n", lines);
        }
    }
}
