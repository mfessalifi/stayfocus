using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>
    /// Quick search: a small window opened by a shortcut from anywhere. Type a few letters,
    /// pick a note with the arrow keys, Enter opens it. It closes as soon as it is left.
    /// </summary>
    class SearchForm : Form
    {
        const int VisibleRows = 7;

        readonly NotesApp app;
        readonly TextBox input = new TextBox();
        readonly ListBox results = new ListBox();
        readonly Label hint = new Label();
        readonly List<NoteForm> matches = new List<NoteForm>();
        readonly Font inputFont = new Font("Segoe UI", 13f);
        readonly Font titleFont = new Font("Segoe UI Semibold", 9.75f);
        readonly Font lineFont = new Font("Segoe UI", 9f);
        string[] terms = new string[0];
        bool leaving;

        public SearchForm(NotesApp app)
        {
            this.app = app;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true; // above the always-on-top notes
            StartPosition = FormStartPosition.Manual;
            Text = "StayFocus Notes - Search";
            BackColor = Ui.Window;

            int width = Ui.S(560), pad = Ui.S(14);

            input.BorderStyle = BorderStyle.None;
            input.Font = inputFont;
            input.BackColor = Ui.Window;
            input.ForeColor = Ui.WindowText;
            input.MaxLength = 200;
            input.SetBounds(pad, pad, width - 2 * pad, inputFont.Height);
            input.TextChanged += delegate { Refill(); };
            Controls.Add(input);

            results.BorderStyle = BorderStyle.None;
            results.DrawMode = DrawMode.OwnerDrawFixed;
            results.ItemHeight = Ui.S(46);
            results.IntegralHeight = false;
            results.BackColor = Ui.Window;
            results.SetBounds(1, input.Bottom + pad, width - 2, results.ItemHeight * VisibleRows);
            results.DrawItem += DrawResult;
            results.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                int index = results.IndexFromPoint(e.Location);
                if (index >= 0) Open(index);
            };
            Ui.DarkScrollBars(results);
            Controls.Add(results);

            hint.AutoSize = false;
            hint.Font = lineFont;
            hint.ForeColor = Ui.Faint;
            hint.SetBounds(pad, results.Bottom + Ui.S(7), width - 2 * pad, Ui.S(18));
            Controls.Add(hint);

            ClientSize = new Size(width, hint.Bottom + Ui.S(8));
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Math.Max(0, (area.Height - Height) / 4));

            Refill();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW; // not in Alt+Tab
                if (app == null || !app.HideFromCapture) cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ApplyCapturePrivacy(this, app.HideFromCapture);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (leaving) return;
            leaving = true;
            // Closing from inside the deactivation itself confuses Windows; do it right after.
            BeginInvoke((MethodInvoker)Close);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen edge = new Pen(Ui.FieldEdge))
            {
                e.Graphics.DrawRectangle(edge, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                e.Graphics.DrawLine(edge, 1, results.Top - 1, ClientSize.Width - 2, results.Top - 1);
                e.Graphics.DrawLine(edge, 1, results.Bottom, ClientSize.Width - 2, results.Bottom);
            }
        }

        // The text box keeps the keyboard; these keys act on the list or the window instead.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    leaving = true;
                    Close();
                    return true;
                case Keys.Enter:
                    Open(results.SelectedIndex);
                    return true;
                case Keys.Down: Step(1); return true;
                case Keys.Up: Step(-1); return true;
                case Keys.PageDown: Step(VisibleRows - 1); return true;
                case Keys.PageUp: Step(1 - VisibleRows); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inputFont.Dispose();
                titleFont.Dispose();
                lineFont.Dispose();
            }
            base.Dispose(disposing);
        }

        void Step(int by)
        {
            if (matches.Count == 0) return;
            results.SelectedIndex = Math.Max(0, Math.Min(matches.Count - 1, results.SelectedIndex + by));
        }

        void Open(int index)
        {
            if (index < 0 || index >= matches.Count) return;
            NoteForm note = matches[index];
            leaving = true;
            Close();
            app.OpenNote(note);
        }

        // ----- matching -----

        void Refill()
        {
            terms = input.Text.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            matches.Clear();
            List<NoteForm> others = new List<NoteForm>();
            for (int i = app.Notes.Count - 1; i >= 0; i--) // newest first
            {
                NoteForm note = app.Notes[i];
                int rank = Rank(note.Data, terms);
                if (rank == 2) matches.Add(note);
                else if (rank == 1) others.Add(note);
            }
            matches.AddRange(others);

            results.BeginUpdate();
            results.Items.Clear();
            for (int i = 0; i < matches.Count; i++) results.Items.Add(i);
            results.EndUpdate();
            if (matches.Count > 0) results.SelectedIndex = 0;

            if (app.Notes.Count == 0) hint.Text = "You have no notes yet.";
            else if (matches.Count == 0) hint.Text = "No note matches.";
            else hint.Text = matches.Count + (matches.Count == 1 ? " note" : " notes")
                + (terms.Length == 0 ? "   -   type to search text and #tags" : "")
                + "   -   Enter opens, Up / Down choose, Esc closes";
        }

        /// <summary>0 = not a match, 1 = every word is somewhere in the note or its tags, 2 = every word is in the first line.</summary>
        public static int Rank(NoteData data, string[] terms)
        {
            string text = data.Text ?? "";
            string title = FirstLine(text);
            bool allInTitle = true;
            foreach (string term in terms)
            {
                bool inTitle = title.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                bool found = inTitle || text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!found)
                {
                    string tagPart = term.TrimStart('#');
                    if (tagPart.Length > 0)
                        foreach (string tag in data.Tags)
                            found |= tag.IndexOf(tagPart, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (!found) return 0;
                allInTitle &= inTitle;
            }
            return allInTitle ? 2 : 1;
        }

        static string FirstLine(string text)
        {
            text = text.TrimStart();
            int end = text.IndexOfAny(new char[] { '\r', '\n' });
            return end < 0 ? text : text.Substring(0, end);
        }

        /// <summary>The part of the note to show under its first line: around the first word found there, else what follows the title.</summary>
        string Snippet(string text)
        {
            text = text.TrimStart();
            string title = FirstLine(text);
            string rest = text.Substring(title.Length);
            int at = -1;
            foreach (string term in terms)
            {
                if (title.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                at = rest.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) break;
            }
            if (at > 24)
            {
                rest = "..." + rest.Substring(at - 20);
            }
            return rest.Trim().Replace("\r\n", "  ").Replace('\n', ' ').Replace('\r', ' ');
        }

        // ----- drawing -----

        void DrawResult(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= matches.Count) return;
            NoteData data = matches[e.Index].Data;
            Rectangle r = e.Bounds;
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;

            using (SolidBrush back = new SolidBrush(selected ? Ui.Selection : Ui.Window)) g.FillRectangle(back, r);
            using (SolidBrush stripe = new SolidBrush(Ui.HeaderColors[Ui.ClampColor(data.Color)]))
                g.FillRectangle(stripe, r.Left + Ui.S(13), r.Top + Ui.S(8), Ui.S(5), r.Height - Ui.S(16));

            string text = data.Text ?? "";
            string title = FirstLine(text);
            string second = Snippet(text);
            if (data.Tags.Count > 0) second = Tags.Display(data.Tags) + (second.Length > 0 ? "     " + second : "");

            string status = NoteStatus.Names[NoteStatus.Clamp(data.Status)];
            DateTime remindAt;
            if (Store.TryParseTime(data.RemindAt, out remindAt)) status = Reminder.Clock + " " + Reminder.Display(remindAt) + "     " + status;
            int statusWidth = TextRenderer.MeasureText(g, status, lineFont, Size.Empty, TextFormatFlags.NoPrefix).Width;

            int left = r.Left + Ui.S(28), width = r.Width - Ui.S(28) - Ui.S(14);
            const TextFormatFlags oneLine = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, status, lineFont, new Rectangle(left + width - statusWidth, r.Top + Ui.S(5), statusWidth, Ui.S(20)),
                Ui.Faint, oneLine);
            TextRenderer.DrawText(g, title.Length == 0 ? "(empty note)" : title, titleFont,
                new Rectangle(left, r.Top + Ui.S(5), Math.Max(0, width - statusWidth - Ui.S(10)), Ui.S(20)),
                title.Length == 0 ? Ui.Faint : Ui.WindowText, oneLine);
            TextRenderer.DrawText(g, second, lineFont, new Rectangle(left, r.Top + Ui.S(24), width, Ui.S(18)), Ui.Faint, oneLine);
        }
    }
}
