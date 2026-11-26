using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>Rules for note tags: short single words, compared without regard to case.</summary>
    static class Tags
    {
        public const int MaxLength = 24;
        public const int MaxPerNote = 12;

        /// <summary>Keeps letters, digits and - _ . / + only (so no leading #, no spaces), cut to MaxLength.</summary>
        public static string Clean(string raw)
        {
            StringBuilder cleaned = new StringBuilder();
            if (raw == null) return "";
            foreach (char c in raw)
            {
                if (cleaned.Length == MaxLength) break;
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '/' || c == '+') cleaned.Append(c);
            }
            return cleaned.ToString();
        }

        /// <summary>Splits what the user typed ("work, urgent" or "#work #urgent") into clean tags.</summary>
        public static List<string> Parse(string input)
        {
            List<string> result = new List<string>();
            if (input == null) return result;
            char[] separators = { ',', ';', ' ', '\t', '\r', '\n' };
            foreach (string part in input.Split(separators, StringSplitOptions.RemoveEmptyEntries)) Add(result, Clean(part));
            return result;
        }

        /// <summary>Cleans a list read from the notes file, which may have been edited by hand.</summary>
        public static List<string> CleanList(List<string> raw)
        {
            List<string> result = new List<string>();
            if (raw != null)
                foreach (string tag in raw) Add(result, Clean(tag));
            return result;
        }

        public static int IndexOf(List<string> tags, string tag)
        {
            return tags.FindIndex(delegate(string t) { return string.Equals(t, tag, StringComparison.OrdinalIgnoreCase); });
        }

        /// <summary>Adds the tag unless it is empty, already there, or the note is full. Returns true if added.</summary>
        public static bool Add(List<string> tags, string tag)
        {
            if (tag.Length == 0 || tags.Count >= MaxPerNote || IndexOf(tags, tag) >= 0) return false;
            tags.Add(tag);
            return true;
        }

        public static string Display(List<string> tags)
        {
            return tags.Count == 0 ? "" : "#" + string.Join("  #", tags);
        }
    }

    /// <summary>Small dialog asking for one or more new tag names.</summary>
    class TagPromptForm : Form
    {
        readonly TextBox input = new TextBox();

        public string Value
        {
            get { return input.Text; }
        }

        public TagPromptForm()
        {
            SuspendLayout();
            Text = "New tag";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true; // notes are always-on-top, the dialog must not open behind them

            Label hint = new Label();
            hint.AutoSize = true;
            hint.Text = "Tag name (separate several with commas or spaces)";
            hint.Location = new Point(14, 14);
            Controls.Add(hint);

            input.SetBounds(14, 40, 312, 24);
            input.MaxLength = 200;
            Controls.Add(input);

            Button add = new Button();
            add.Text = "Add";
            add.SetBounds(166, 78, 76, 28);
            add.DialogResult = DialogResult.OK;
            Controls.Add(add);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.SetBounds(250, 78, 76, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = add;
            CancelButton = cancel;
            ClientSize = new Size(340, 120);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Ui.StyleDialog(this);
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Open where the mouse is (next to the note or card that asked), kept fully on screen.
            Point mouse = Cursor.Position;
            Rectangle area = Screen.FromPoint(mouse).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(mouse.X - Width / 2, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(mouse.Y - Height / 2, area.Bottom - Height));
            Location = new Point(x, y);
        }
    }
}
