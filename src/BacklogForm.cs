using System;
using System.Drawing;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>One note shown as a card on the backlog board.</summary>
    class NoteCard : Control
    {
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font TitleDoneFont = new Font("Segoe UI Semibold", 9.75f, FontStyle.Strikeout);
        public static readonly Font SnippetFont = new Font("Segoe UI", 9f);

        public readonly NoteForm Note;

        bool hover;
        Point pressedAt;

        public NoteCard(NoteForm note)
        {
            Note = note;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(note.Data.Tags.Count > 0 ? 96 : 76); // one extra line when there are tags
            Margin = new Padding(Ui.S(6), Ui.S(6), Ui.S(6), 0);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int color = Ui.ClampColor(Note.Data.Color);
            Color accent = Ui.HeaderColors[color];
            Graphics g = e.Graphics;
            g.Clear(Ui.BodyColors[color]);
            using (SolidBrush stripe = new SolidBrush(accent)) g.FillRectangle(stripe, 0, 0, Ui.S(6), Height);
            using (Pen edge = new Pen(Ui.Darken(accent, hover ? 0.45f : 0.15f))) g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);

            // First line is the title, the rest is a two-line preview.
            string text = (Note.Data.Text ?? "").Trim();
            int lineEnd = text.IndexOfAny(new char[] { '\r', '\n' });
            string title = lineEnd < 0 ? text : text.Substring(0, lineEnd);
            string rest = lineEnd < 0 ? "" : text.Substring(lineEnd).Trim().Replace("\r\n", " ").Replace('\n', ' ');

            bool done = NoteStatus.Clamp(Note.Data.Status) == NoteStatus.Done;
            bool empty = title.Length == 0;
            Color gray = Color.FromArgb(0x70, 0x70, 0x70);
            int left = Ui.S(16), width = Width - Ui.S(24);

            TextRenderer.DrawText(g, empty ? "(empty note)" : title, done ? TitleDoneFont : TitleFont,
                new Rectangle(left, Ui.S(8), width, Ui.S(20)), empty || done ? gray : Ui.TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, rest, SnippetFont,
                new Rectangle(left, Ui.S(31), width, Ui.S(36)), gray,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis
                | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            if (Note.Data.Tags.Count > 0)
                TextRenderer.DrawText(g, Tags.Display(Note.Data.Tags), SnippetFont,
                    new Rectangle(left, Ui.S(70), width, Ui.S(18)), Ui.Darken(accent, 0.55f),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) pressedAt = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.Left) return;
            Size slack = SystemInformation.DragSize;
            if (Math.Abs(e.X - pressedAt.X) > slack.Width || Math.Abs(e.Y - pressedAt.Y) > slack.Height)
                DoDragDrop(this, DragDropEffects.Move);
        }
    }

    /// <summary>The backlog: every note, grouped in columns by status.</summary>
    class BacklogForm : Form
    {
        readonly NotesApp app;
        readonly TextBox search = new TextBox();
        readonly ComboBox tagFilter = new ComboBox();
        readonly ToolStripMenuItem tagsItem = new ToolStripMenuItem("Tags");
        System.Collections.Generic.List<string> filterTags = new System.Collections.Generic.List<string>();
        string selectedTag; // null = all tags
        bool rebuilding;
        readonly Label[] headers = new Label[NoteStatus.Names.Length];
        readonly FlowLayoutPanel[] columns = new FlowLayoutPanel[NoteStatus.Names.Length];
        readonly ContextMenuStrip cardMenu = new ContextMenuStrip();
        readonly Font headerFont = new Font("Segoe UI Semibold", 9.5f);
        NoteForm menuTarget;
        bool closingForReal;

        public BacklogForm(NotesApp app, Rectangle savedBounds)
        {
            this.app = app;

            AutoScaleMode = AutoScaleMode.None;
            Text = "StayFocus Notes - Backlog";
            Icon = Ui.LoadIcon(new Size(32, 32));
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            MinimumSize = new Size(Ui.S(680), Ui.S(360));

            bool savedIsUsable = false;
            if (savedBounds.Width > 0 && savedBounds.Height > 0)
                foreach (Screen screen in Screen.AllScreens)
                    savedIsUsable |= screen.WorkingArea.IntersectsWith(savedBounds);
            if (savedIsUsable)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = savedBounds;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(Ui.S(1020), Ui.S(600));
            }

            // Board: one header + one scrolling column per status.
            TableLayoutPanel board = new TableLayoutPanel();
            board.Dock = DockStyle.Fill;
            board.Padding = new Padding(Ui.S(8), 0, Ui.S(8), Ui.S(8));
            board.ColumnCount = columns.Length;
            board.RowCount = 2;
            board.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(34)));
            board.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            for (int i = 0; i < columns.Length; i++)
            {
                board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns.Length));

                Label header = new Label();
                header.Dock = DockStyle.Fill;
                header.Font = headerFont;
                header.ForeColor = Color.FromArgb(0x44, 0x44, 0x44);
                header.TextAlign = ContentAlignment.MiddleLeft;
                header.Margin = new Padding(Ui.S(6), 0, Ui.S(4), 0);
                headers[i] = header;
                board.Controls.Add(header, i, 0);

                FlowLayoutPanel column = new FlowLayoutPanel();
                column.Dock = DockStyle.Fill;
                column.FlowDirection = FlowDirection.TopDown;
                column.WrapContents = false;
                column.AutoScroll = true;
                column.BackColor = Color.FromArgb(0xF1, 0xF2, 0xF4);
                column.Margin = new Padding(Ui.S(4));
                column.ClientSizeChanged += delegate { FitCards(column); };
                columns[i] = column;
                board.Controls.Add(column, i, 1);

                AcceptDrops(header, i);
                AcceptDrops(column, i);
            }
            Controls.Add(board);

            // Top bar. Added after the board so it docks above it.
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = Ui.S(50);

            Button add = new Button();
            add.Text = "+ New note";
            add.FlatStyle = FlatStyle.System;
            add.SetBounds(Ui.S(16), Ui.S(11), Ui.S(104), Ui.S(28));
            add.Click += delegate { this.app.NewNote(null); };
            bar.Controls.Add(add);

            Label searchLabel = new Label();
            searchLabel.AutoSize = true;
            searchLabel.Text = "Search";
            searchLabel.Location = new Point(Ui.S(140), Ui.S(17));
            bar.Controls.Add(searchLabel);

            search.SetBounds(Ui.S(190), Ui.S(14), Ui.S(180), Ui.S(24));
            search.TextChanged += delegate { Rebuild(); };
            bar.Controls.Add(search);

            Label tagLabel = new Label();
            tagLabel.AutoSize = true;
            tagLabel.Text = "Tag";
            tagLabel.Location = new Point(Ui.S(388), Ui.S(17));
            bar.Controls.Add(tagLabel);

            tagFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            tagFilter.SetBounds(Ui.S(420), Ui.S(13), Ui.S(160), Ui.S(24));
            tagFilter.SelectedIndexChanged += delegate
            {
                if (rebuilding) return; // Rebuild() itself refills the list
                int index = tagFilter.SelectedIndex;
                selectedTag = index <= 0 ? null : filterTags[index - 1];
                Rebuild();
            };
            bar.Controls.Add(tagFilter);

            Label hint = new Label();
            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Text = "Drag cards between columns. Double-click to open. Right-click for more.";
            hint.Location = new Point(Ui.S(598), Ui.S(17));
            bar.Controls.Add(hint);
            Controls.Add(bar);

            BuildCardMenu();
        }

        /// <summary>Redraws every column from the current notes, keeping each column's scroll position.</summary>
        public void Rebuild()
        {
            string filter = search.Text.Trim();
            RefillTagFilter();
            SuspendLayout();
            for (int status = 0; status < columns.Length; status++)
            {
                FlowLayoutPanel column = columns[status];
                int scrollY = -column.AutoScrollPosition.Y;
                column.SuspendLayout();
                while (column.Controls.Count > 0)
                {
                    Control old = column.Controls[0];
                    column.Controls.RemoveAt(0);
                    old.Dispose();
                }

                int count = 0;
                foreach (NoteForm note in app.Notes)
                {
                    if (NoteStatus.Clamp(note.Data.Status) != status) continue;
                    if (!Matches(note, filter)) continue;
                    column.Controls.Add(MakeCard(note, status));
                    count++;
                }
                if (count == 0)
                {
                    Label none = new Label();
                    none.AutoSize = true;
                    none.ForeColor = SystemColors.GrayText;
                    none.Text = filter.Length > 0 || selectedTag != null ? "No match" : "Nothing here";
                    none.Margin = new Padding(Ui.S(10), Ui.S(10), 0, 0);
                    AcceptDrops(none, status);
                    column.Controls.Add(none);
                }

                column.ResumeLayout();
                FitCards(column);
                column.AutoScrollPosition = new Point(0, scrollY);
                headers[status].Text = NoteStatus.Names[status].ToUpper() + "   " + count;
            }
            ResumeLayout();
        }

        /// <summary>True if the note passes the tag filter and the search box (which looks in text and tag names).</summary>
        bool Matches(NoteForm note, string filter)
        {
            if (selectedTag != null && Tags.IndexOf(note.Data.Tags, selectedTag) < 0) return false;
            if (filter.Length == 0) return true;
            if ((note.Data.Text ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            string tagPart = filter.TrimStart('#');
            if (tagPart.Length == 0) return false;
            foreach (string tag in note.Data.Tags)
                if (tag.IndexOf(tagPart, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Lists the tags currently in use (with how many notes carry each), keeping the selection.</summary>
        void RefillTagFilter()
        {
            filterTags = app.AllTags();
            if (selectedTag != null && Tags.IndexOf(filterTags, selectedTag) < 0) selectedTag = null; // tag no longer used

            rebuilding = true;
            tagFilter.BeginUpdate();
            tagFilter.Items.Clear();
            tagFilter.Items.Add("All tags");
            foreach (string tag in filterTags)
            {
                int count = 0;
                foreach (NoteForm note in app.Notes)
                    if (Tags.IndexOf(note.Data.Tags, tag) >= 0) count++;
                tagFilter.Items.Add("#" + tag + "  (" + count + ")");
            }
            tagFilter.SelectedIndex = selectedTag == null ? 0 : Tags.IndexOf(filterTags, selectedTag) + 1;
            tagFilter.EndUpdate();
            rebuilding = false;
        }

        public void CloseForReal()
        {
            closingForReal = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // The X button only hides the board; the app keeps running in the tray.
            if (!closingForReal && !app.Exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ApplyCapturePrivacy(this, app.HideFromCapture);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (WindowState == FormWindowState.Normal) app.BoardMoved(Bounds);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cardMenu.Dispose();
                headerFont.Dispose();
            }
            base.Dispose(disposing);
        }

        NoteCard MakeCard(NoteForm note, int status)
        {
            NoteCard card = new NoteCard(note);
            card.ContextMenuStrip = cardMenu;
            card.DoubleClick += delegate { app.OpenNote(note); };
            AcceptDrops(card, status);
            return card;
        }

        static void FitCards(FlowLayoutPanel column)
        {
            int width = column.ClientSize.Width - Ui.S(12);
            foreach (Control child in column.Controls)
                if (child is NoteCard) child.Width = width;
        }

        /// <summary>Dropping a card on this control moves its note to the given status.</summary>
        void AcceptDrops(Control target, int status)
        {
            target.AllowDrop = true;
            DragEventHandler allow = delegate(object sender, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(typeof(NoteCard)) ? DragDropEffects.Move : DragDropEffects.None;
            };
            target.DragEnter += allow;
            target.DragOver += allow;
            target.DragDrop += delegate(object sender, DragEventArgs e)
            {
                NoteCard card = e.Data.GetData(typeof(NoteCard)) as NoteCard;
                if (card == null) return;
                NoteForm note = card.Note;
                // Wait until the drag has fully ended before the board (and the dragged card) is rebuilt.
                BeginInvoke((MethodInvoker)delegate { app.SetStatus(note, status); });
            };
        }

        void BuildCardMenu()
        {
            cardMenu.Items.Add(new ToolStripMenuItem("Open", null, delegate { if (menuTarget != null) app.OpenNote(menuTarget); }));
            cardMenu.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < NoteStatus.Names.Length; i++)
            {
                int status = i;
                ToolStripMenuItem move = new ToolStripMenuItem("Move to " + NoteStatus.Names[i], null,
                    delegate { if (menuTarget != null) app.SetStatus(menuTarget, status); });
                move.Tag = status;
                cardMenu.Items.Add(move);
            }
            cardMenu.Items.Add(new ToolStripSeparator());
            cardMenu.Items.Add(tagsItem);
            cardMenu.Items.Add(new ToolStripSeparator());
            cardMenu.Items.Add(new ToolStripMenuItem("Delete", null,
                delegate { if (menuTarget != null) app.DeleteNoteWithConfirm(menuTarget, this); }));

            cardMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                NoteCard card = cardMenu.SourceControl as NoteCard;
                if (card == null)
                {
                    e.Cancel = true;
                    return;
                }
                menuTarget = card.Note;
                app.FillTagMenu(tagsItem.DropDownItems, menuTarget, this);
                foreach (ToolStripItem item in cardMenu.Items)
                    if (item.Tag is int) item.Enabled = (int)item.Tag != NoteStatus.Clamp(menuTarget.Data.Status);
            };
        }
    }
}
