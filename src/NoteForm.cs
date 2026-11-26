using System;
using System.Drawing;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>One sticky note: a borderless colored window with a small toolbar and a text area.</summary>
    class NoteForm : Form
    {
        const string GlyphAdd = "";
        const string GlyphColor = "";
        const string GlyphPin = "";
        const string GlyphPinned = "";
        const string GlyphDelete = "";
        const string GlyphTag = "";

        const string GlyphChecklist = "";

        const string GlyphBell = "";

        public readonly NoteData Data;

        readonly NotesApp app;
        readonly Panel header;
        readonly TextBox body;
        readonly Label pinButton;
        readonly Label statusButton;
        readonly Label tagStrip;
        readonly Label remindStrip;
        readonly System.Windows.Forms.Timer flashTimer = new System.Windows.Forms.Timer();
        int flashesLeft; // above 0 while a due reminder is blinking
        ContextMenuStrip reminderMenu;
        readonly Font tagFont = new Font("Segoe UI", 8.25f);
        ContextMenuStrip tagMenu;
        readonly ToolTip tips = new ToolTip();
        readonly Font iconFont = new Font("Segoe MDL2 Assets", 9.5f);
        readonly Font statusFont = new Font("Segoe UI Semibold", 8.25f);
        // Segoe UI Symbol has the same letters as Segoe UI plus the check box characters, so empty and
        // ticked boxes come from one font and are equally wide (Segoe UI borrows them from two fonts).
        readonly Font bodyFont = new Font("Segoe UI Symbol", 11f);
        readonly Font bodyDoneFont = new Font("Segoe UI Symbol", 11f, FontStyle.Strikeout);
        ContextMenuStrip colorMenu;
        ContextMenuStrip statusMenu;
        bool loaded;
        bool closingForReal;
        int pressedBox = -1; // check box under the mouse when the left button went down

        public NoteForm(NotesApp app, NoteData data)
        {
            this.app = app;
            Data = data;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            Text = "StayFocus note";
            MinimumSize = new Size(Ui.S(260), Ui.S(120));
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            // Upper limit too: a damaged or tampered notes file must not produce a note that covers the screen.
            MaximumSize = new Size(Ui.S(1600), Ui.S(1600));
            Bounds = new Rectangle(data.X, data.Y,
                Math.Min(Math.Max(data.W, MinimumSize.Width), MaximumSize.Width),
                Math.Min(Math.Max(data.H, MinimumSize.Height), MaximumSize.Height));

            int bar = Ui.S(30);
            int width = ClientSize.Width;

            header = new Panel();
            header.SetBounds(0, 0, width, bar);
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.MouseDown += OnHeaderMouseDown;
            Controls.Add(header);

            int cell = Ui.S(27); // width of one toolbar button; seven of them plus the status fit the minimum width
            AddButton(GlyphAdd, iconFont, "New note", 0, cell, AnchorStyles.Left, delegate { this.app.NewNote(this); });
            statusButton = AddButton("", statusFont, "Change status", cell, Ui.S(70), AnchorStyles.Left, null);
            statusButton.Click += delegate { ShowStatusMenu(); };
            AddButton(GlyphDelete, iconFont, "Delete (goes to the trash)", width - cell, cell, AnchorStyles.Right,
                delegate { this.app.DeleteNote(this); });
            pinButton = AddButton(GlyphPin, iconFont, "", width - 2 * cell, cell, AnchorStyles.Right, delegate { TogglePinned(); });
            Label colorButton = AddButton(GlyphColor, iconFont, "Change color", width - 3 * cell, cell, AnchorStyles.Right, null);
            colorButton.Click += delegate { ShowColorMenu(colorButton); };
            Label bellButton = AddButton(GlyphBell, iconFont, "Remind me", width - 4 * cell, cell, AnchorStyles.Right, null);
            bellButton.Click += delegate { ShowReminderMenu(bellButton); };
            Label tagButton = AddButton(GlyphTag, iconFont, "Tags", width - 5 * cell, cell, AnchorStyles.Right, null);
            tagButton.Click += delegate { ShowTagMenu(tagButton); };
            AddButton(GlyphChecklist, iconFont, "Checklist: add or remove check boxes (Ctrl+L)", width - 6 * cell, cell,
                AnchorStyles.Right, delegate { ToggleChecklist(); });

            int pad = Ui.S(10);
            int top = bar + Ui.S(8);
            body = new TextBox();
            body.Multiline = true;
            body.BorderStyle = BorderStyle.None;
            body.AcceptsTab = true;
            body.MaxLength = 0;
            body.Text = data.Text ?? "";
            body.SetBounds(pad, top, width - 2 * pad, ClientSize.Height - top - Ui.S(14));
            body.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            body.TextChanged += delegate
            {
                Data.Text = body.Text;
                this.app.ContentChanged();
            };
            body.KeyDown += OnBodyKeyDown;
            body.KeyPress += OnBodyKeyPress;
            body.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                pressedBox = e.Button == MouseButtons.Left ? BoxAt(e.Location) : -1;
            };
            body.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                // A click on a box ticks it; a drag that merely starts there is a text selection.
                int box = pressedBox;
                pressedBox = -1;
                if (e.Button == MouseButtons.Left && box >= 0 && body.SelectionLength == 0 && BoxAt(e.Location) == box)
                    ToggleBox(box);
            };
            body.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                Cursor wanted = e.Button == MouseButtons.None && BoxAt(e.Location) >= 0 ? Cursors.Hand : Cursors.IBeam;
                if (body.Cursor != wanted) body.Cursor = wanted;
            };
            Controls.Add(body);

            // One line under the text: the reminder, then the tags. Only shown when the note has either.
            remindStrip = MakeFooterLabel("Reminder (click to change)");
            remindStrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            remindStrip.Click += delegate { ShowReminderMenu(remindStrip); };
            tagStrip = MakeFooterLabel("Tags (click to change)");
            tagStrip.AutoEllipsis = true;
            tagStrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            tagStrip.Click += delegate { ShowTagMenu(tagStrip); };

            flashTimer.Interval = 500;
            flashTimer.Tick += delegate { OnFlash(); };

            ApplyColor();
            ApplyPinnedLook();
            RefreshLook();
            RefreshFooter();
            loaded = true;
        }

        Label MakeFooterLabel(string tip)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.UseMnemonic = false;
            label.Font = tagFont;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Cursor = Cursors.Hand;
            tips.SetToolTip(label, tip);
            Controls.Add(label);
            return label;
        }

        // ----- window behavior -----

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW; // keep notes out of Alt+Tab
                if (Data != null && Data.Pinned) cp.ExStyle |= Native.WS_EX_TOPMOST;
                // The shadow is drawn by Windows outside our window, so it would still show up in a
                // screen capture as an empty outline. Drop it while notes are hidden from capture.
                if (app == null || !app.HideFromCapture) cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ApplyCapturePrivacy(this, app.HideFromCapture);
        }

        /// <summary>Rebuilds the window so the shadow and the capture setting match the new choice.</summary>
        public void CaptureSettingChanged()
        {
            if (IsHandleCreated) RecreateHandle();
        }

        // Showing notes at startup must not pull focus away from what the user is doing.
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != Native.WM_NCHITTEST || (int)m.Result != Native.HTCLIENT) return;

            // The window has no frame, so report the margins around the text as resize edges.
            int packed = unchecked((int)m.LParam.ToInt64());
            Point p = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
            int grip = Ui.S(8);
            bool left = p.X < grip;
            bool right = p.X >= ClientSize.Width - grip;
            bool bottom = p.Y >= ClientSize.Height - grip;

            if (bottom && right) m.Result = (IntPtr)Native.HTBOTTOMRIGHT;
            else if (bottom && left) m.Result = (IntPtr)Native.HTBOTTOMLEFT;
            else if (bottom) m.Result = (IntPtr)Native.HTBOTTOM;
            else if (right) m.Result = (IntPtr)Native.HTRIGHT;
            else if (left) m.Result = (IntPtr)Native.HTLEFT;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = ClientSize.Width, h = ClientSize.Height;
            using (Pen edge = new Pen(Ui.Edge(Data.Color)))
            {
                e.Graphics.DrawRectangle(edge, 0, 0, w - 1, h - 1);
                int step = Ui.S(4);
                for (int i = 1; i <= 3; i++)
                    e.Graphics.DrawLine(edge, w - 3 - i * step, h - 4, w - 4, h - 3 - i * step);
            }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            RememberBounds();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RememberBounds();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            StopAlert(); // the user has seen it
            app.NoteActivated(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 on a note hides the notes; only the delete button removes one.
            if (!closingForReal && !app.Exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                app.HideNotes();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                iconFont.Dispose();
                statusFont.Dispose();
                bodyFont.Dispose();
                bodyDoneFont.Dispose();
                tagFont.Dispose();
                flashTimer.Dispose();
                if (reminderMenu != null) reminderMenu.Dispose();
                if (tagMenu != null) tagMenu.Dispose();
                if (colorMenu != null) colorMenu.Dispose();
                if (statusMenu != null) statusMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        // ----- used by NotesApp -----

        public void FocusBody()
        {
            body.Focus();
            body.SelectionStart = body.TextLength;
        }

        public void RaiseToTop()
        {
            Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void CloseForReal()
        {
            closingForReal = true;
            Close();
        }

        /// <summary>Brings the note back if its saved position is on a monitor that is no longer there.</summary>
        public void EnsureOnScreen()
        {
            Rectangle titleStrip = new Rectangle(Left, Top, Width, Ui.S(30));
            foreach (Screen screen in Screen.AllScreens)
                if (screen.WorkingArea.IntersectsWith(titleStrip)) return;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - Ui.S(48), area.Top + Ui.S(48));
        }

        /// <summary>Updates the status button and the text style after the status changed.</summary>
        public void RefreshLook()
        {
            int status = NoteStatus.Clamp(Data.Status);
            statusButton.Text = NoteStatus.Names[status] + " ▾";
            bool done = status == NoteStatus.Done;
            body.Font = done ? bodyDoneFont : bodyFont;
            body.ForeColor = done ? Ui.Faint : Ui.Text;
        }

        /// <summary>Call after the note's tags changed.</summary>
        public void RefreshTags()
        {
            RefreshFooter();
        }

        /// <summary>Call after the note's reminder changed, came due, or the day changed.</summary>
        public void RefreshReminder()
        {
            RefreshFooter();
        }

        /// <summary>The colors of the theme changed.</summary>
        public void ApplyTheme()
        {
            ApplyColor();
            RefreshLook();
            RefreshFooter();
        }

        /// <summary>Makes the note impossible to miss for a few seconds: on top of everything, top bar blinking.</summary>
        public void StartAlert()
        {
            flashesLeft = 12;
            header.BackColor = Ui.Alert;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            flashTimer.Start();
        }

        public void StopAlert()
        {
            if (flashesLeft == 0) return;
            flashesLeft = 0;
            flashTimer.Stop();
            header.BackColor = Ui.Header(Data.Color);
            if (!Data.Pinned && IsHandleCreated)
                Native.SetWindowPos(Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void OnFlash()
        {
            if (flashesLeft <= 1)
            {
                StopAlert();
                return;
            }
            flashesLeft--;
            header.BackColor = flashesLeft % 2 == 1 ? Ui.Alert : Ui.Header(Data.Color);
        }

        /// <summary>Lays out the line under the text (reminder, then tags) and gives the text the room that is left.</summary>
        void RefreshFooter()
        {
            DateTime when;
            bool hasReminder = Store.TryParseTime(Data.RemindAt, out when);
            bool due = hasReminder && when <= DateTime.Now;
            bool hasTags = Data.Tags.Count > 0;

            int pad = Ui.S(10);
            int y = ClientSize.Height - Ui.S(28);
            int height = Ui.S(18);
            int available = ClientSize.Width - 2 * pad - Ui.S(12);

            int reminderWidth = 0;
            if (hasReminder)
            {
                remindStrip.Text = Reminder.Clock + " " + (due ? "Due " : "") + Reminder.Display(when);
                remindStrip.ForeColor = due ? Ui.Alert : Ui.Accent(Data.Color);
                reminderWidth = Math.Min(available, TextRenderer.MeasureText(remindStrip.Text, tagFont).Width + Ui.S(2));
                remindStrip.SetBounds(pad, y, reminderWidth, height);
            }
            remindStrip.Visible = hasReminder;

            int tagsLeft = pad + (hasReminder ? reminderWidth + Ui.S(8) : 0);
            tagStrip.Text = Tags.Display(Data.Tags);
            tagStrip.SetBounds(tagsLeft, y, Math.Max(0, pad + available - tagsLeft), height);
            tagStrip.Visible = hasTags;

            body.Height = ClientSize.Height - body.Top - Ui.S(hasReminder || hasTags ? 32 : 14);
        }

        // ----- toolbar -----

        Label AddButton(string text, Font font, string tip, int x, int width, AnchorStyles side, EventHandler onClick)
        {
            Label button = new Label();
            button.AutoSize = false;
            button.UseMnemonic = false;
            button.Text = text;
            button.Font = font;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.BackColor = Color.Transparent;
            button.Cursor = Cursors.Hand;
            button.SetBounds(x, 0, width, header.Height);
            button.Anchor = AnchorStyles.Top | side;
            button.MouseEnter += delegate { button.BackColor = Ui.Hover(header.BackColor); };
            button.MouseLeave += delegate { button.BackColor = Color.Transparent; };
            if (onClick != null) button.Click += onClick;
            if (tip.Length > 0) tips.SetToolTip(button, tip);
            header.Controls.Add(button);
            return button;
        }

        void OnHeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Hand the drag to Windows as if the bar were a normal title bar.
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        void OnBodyKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && !e.Alt && e.KeyCode == Keys.A)
            {
                body.SelectAll();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.L)
            {
                ToggleChecklist();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Enter)
            {
                // Ctrl+Enter ticks or unticks the item the cursor is on.
                string text = body.Text;
                ToggleBox(Checklist.BoxIndex(text, Checklist.LineStart(text, body.SelectionStart)));
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && ContinueChecklist())
            {
                e.SuppressKeyPress = true;
            }
        }

        // ----- checklist -----

        /// <summary>Typing "[ ] " or "[x] " at the start of a line turns it into a check box.</summary>
        void OnBodyKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != ' ' || body.SelectionLength > 0) return;
            string text = body.Text;
            int caret = body.SelectionStart;
            int start = Checklist.LineStart(text, caret);
            while (start < caret && (text[start] == ' ' || text[start] == '\t')) start++;

            string typed = text.Substring(start, caret - start);
            char box;
            if (typed == "[]" || typed == "[ ]") box = Checklist.Open;
            else if (typed == "[x]" || typed == "[X]") box = Checklist.Ticked;
            else return;

            body.Select(start, caret - start);
            body.Paste(box + " "); // Paste(text) keeps Ctrl+Z working, unlike SelectedText
            e.Handled = true;
        }

        /// <summary>Turns the current line (or every selected line) into check box items, or back into plain lines.</summary>
        void ToggleChecklist()
        {
            string text = body.Text;
            int selStart = body.SelectionStart;
            int selEnd = selStart + body.SelectionLength;
            // A selection that stops at the very start of a line does not include that line.
            int last = selEnd > selStart && text[selEnd - 1] == '\n' ? selEnd - 1 : selEnd;

            int from = Checklist.LineStart(text, selStart);
            int to = Checklist.LineEnd(text, last);
            if (to > from && text[to - 1] == '\r') to--;

            string before = text.Substring(from, to - from);
            string after = Checklist.ToggleItems(before);
            if (after != before)
            {
                body.Select(from, to - from);
                body.Paste(after);
                if (selEnd > selStart) body.Select(from, after.Length);
                else body.Select(Math.Min(from + after.Length, Math.Max(from, selStart + after.Length - before.Length)), 0);
            }
            body.Focus();
        }

        /// <summary>Ticks or unticks the box at the given text position, leaving the cursor where it was.</summary>
        void ToggleBox(int boxIndex)
        {
            string text = body.Text;
            if (boxIndex < 0 || boxIndex >= text.Length || !Checklist.IsBox(text[boxIndex])) return;
            int start = body.SelectionStart, length = body.SelectionLength;
            body.Select(boxIndex, 1);
            body.Paste((text[boxIndex] == Checklist.Open ? Checklist.Ticked : Checklist.Open).ToString());
            body.Select(start, length);
        }

        /// <summary>
        /// Enter inside an item starts the next one; Enter on an empty item ends the list.
        /// Returns false when the cursor is not in an item, so Enter behaves as usual.
        /// </summary>
        bool ContinueChecklist()
        {
            if (body.SelectionLength > 0) return false;
            string text = body.Text;
            int caret = body.SelectionStart;
            int start = Checklist.LineStart(text, caret);
            int box = Checklist.BoxIndex(text, start);
            if (box < 0 || caret <= box) return false;

            int end = Checklist.LineEnd(text, caret);
            if (text.Substring(box + 1, end - box - 1).Trim().Length == 0)
            {
                body.Select(start, end - start);
                body.Paste("");
                return true;
            }
            // Cursor between the box and its space: step over the space so the box keeps it.
            if (caret == box + 1 && caret < text.Length && text[caret] == ' ') body.Select(caret + 1, 0);
            body.Paste("\r\n" + text.Substring(start, box - start) + Checklist.OpenPrefix);
            return true;
        }

        /// <summary>Text position of the check box drawn at this point of the text area, or -1.</summary>
        int BoxAt(Point p)
        {
            string text = body.Text;
            if (text.Length == 0) return -1;
            int box = Checklist.BoxIndex(text, Checklist.LineStart(text, body.GetCharIndexFromPosition(p)));
            if (box < 0) return -1;

            Point corner = body.GetPositionFromCharIndex(box);
            int lineHeight = body.Font.Height;
            int width = lineHeight;
            if (box + 1 < text.Length)
            {
                Point next = body.GetPositionFromCharIndex(box + 1);
                if (next.Y == corner.Y && next.X > corner.X) width = next.X - corner.X;
            }
            int slack = Ui.S(3);
            return p.Y >= corner.Y && p.Y < corner.Y + lineHeight && p.X >= corner.X - slack && p.X < corner.X + width + slack
                ? box : -1;
        }

        void ShowColorMenu(Control anchor)
        {
            if (colorMenu == null)
            {
                colorMenu = new ContextMenuStrip();
                for (int i = 0; i < Ui.ColorNames.Length; i++)
                {
                    int index = i;
                    ToolStripMenuItem item = new ToolStripMenuItem(Ui.ColorNames[i], Ui.Swatch(i));
                    item.Click += delegate
                    {
                        Data.Color = index;
                        ApplyColor();
                        app.ContentChanged();
                    };
                    colorMenu.Items.Add(item);
                }
            }
            for (int i = 0; i < colorMenu.Items.Count; i++)
                ((ToolStripMenuItem)colorMenu.Items[i]).Checked = i == Ui.ClampColor(Data.Color);
            colorMenu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowReminderMenu(Control anchor)
        {
            StopAlert();
            if (reminderMenu == null) reminderMenu = new ContextMenuStrip();
            app.FillReminderMenu(reminderMenu.Items, this, this);
            reminderMenu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowTagMenu(Control anchor)
        {
            if (tagMenu == null) tagMenu = new ContextMenuStrip();
            app.FillTagMenu(tagMenu.Items, this, this);
            tagMenu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowStatusMenu()
        {
            if (statusMenu == null)
            {
                statusMenu = new ContextMenuStrip();
                for (int i = 0; i < NoteStatus.Names.Length; i++)
                {
                    int status = i;
                    string label = i == NoteStatus.Archive ? "Archive (hide from desktop)" : NoteStatus.Names[i];
                    statusMenu.Items.Add(new ToolStripMenuItem(label, null, delegate { app.SetStatus(this, status); }));
                }
                statusMenu.Items.Add(new ToolStripSeparator());
                statusMenu.Items.Add(new ToolStripMenuItem("Open backlog", null, delegate { app.ShowBacklog(); }));
            }
            for (int i = 0; i < NoteStatus.Names.Length; i++)
                ((ToolStripMenuItem)statusMenu.Items[i]).Checked = i == NoteStatus.Clamp(Data.Status);
            statusMenu.Show(statusButton, new Point(0, statusButton.Height));
        }

        void TogglePinned()
        {
            Data.Pinned = !Data.Pinned;
            Native.SetWindowPos(Handle, Data.Pinned ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            ApplyPinnedLook();
            app.ScheduleSave();
        }

        // ----- look -----

        void ApplyColor()
        {
            int index = Ui.ClampColor(Data.Color);
            BackColor = Ui.Body(index);
            body.BackColor = Ui.Body(index);
            header.BackColor = Ui.Header(index);
            foreach (Control button in header.Controls) button.ForeColor = Ui.Glyph(index);
            tagStrip.ForeColor = Ui.Accent(index);
            if (loaded) RefreshFooter(); // the reminder line has its own color rule
            Invalidate();
        }

        void ApplyPinnedLook()
        {
            pinButton.Text = Data.Pinned ? GlyphPinned : GlyphPin;
            tips.SetToolTip(pinButton, Data.Pinned ? "Always on top: on" : "Always on top: off");
        }

        void RememberBounds()
        {
            if (!loaded || WindowState != FormWindowState.Normal) return;
            Data.X = Left;
            Data.Y = Top;
            Data.W = Width;
            Data.H = Height;
            app.ScheduleSave();
        }
    }
}
