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

        public readonly NoteData Data;

        readonly NotesApp app;
        readonly Panel header;
        readonly TextBox body;
        readonly Label pinButton;
        readonly Label statusButton;
        readonly Label tagStrip;
        readonly Font tagFont = new Font("Segoe UI", 8.25f);
        ContextMenuStrip tagMenu;
        readonly ToolTip tips = new ToolTip();
        readonly Font iconFont = new Font("Segoe MDL2 Assets", 9.5f);
        readonly Font statusFont = new Font("Segoe UI Semibold", 8.25f);
        readonly Font bodyFont = new Font("Segoe UI", 11f);
        readonly Font bodyDoneFont = new Font("Segoe UI", 11f, FontStyle.Strikeout);
        ContextMenuStrip colorMenu;
        ContextMenuStrip statusMenu;
        bool loaded;
        bool closingForReal;

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
            MinimumSize = new Size(Ui.S(230), Ui.S(120));
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

            AddButton(GlyphAdd, iconFont, "New note", 0, bar, AnchorStyles.Left, delegate { this.app.NewNote(this); });
            statusButton = AddButton("", statusFont, "Change status", bar, Ui.S(70), AnchorStyles.Left, null);
            statusButton.Click += delegate { ShowStatusMenu(); };
            AddButton(GlyphDelete, iconFont, "Delete this note", width - bar, bar, AnchorStyles.Right,
                delegate { this.app.DeleteNoteWithConfirm(this, this); });
            pinButton = AddButton(GlyphPin, iconFont, "", width - 2 * bar, bar, AnchorStyles.Right, delegate { TogglePinned(); });
            Label colorButton = AddButton(GlyphColor, iconFont, "Change color", width - 3 * bar, bar, AnchorStyles.Right, null);
            colorButton.Click += delegate { ShowColorMenu(colorButton); };
            Label tagButton = AddButton(GlyphTag, iconFont, "Tags", width - 4 * bar, bar, AnchorStyles.Right, null);
            tagButton.Click += delegate { ShowTagMenu(tagButton); };

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
            Controls.Add(body);

            // One line under the text listing the note's tags; only shown when there are some.
            tagStrip = new Label();
            tagStrip.AutoSize = false;
            tagStrip.AutoEllipsis = true;
            tagStrip.UseMnemonic = false;
            tagStrip.Font = tagFont;
            tagStrip.TextAlign = ContentAlignment.MiddleLeft;
            tagStrip.Cursor = Cursors.Hand;
            tagStrip.SetBounds(pad, ClientSize.Height - Ui.S(28), width - 2 * pad - Ui.S(12), Ui.S(18));
            tagStrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            tagStrip.Click += delegate { ShowTagMenu(tagStrip); };
            tips.SetToolTip(tagStrip, "Tags (click to change)");
            Controls.Add(tagStrip);

            ApplyColor();
            ApplyPinnedLook();
            RefreshLook();
            RefreshTags();
            loaded = true;
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
            using (Pen edge = new Pen(Ui.Darken(Ui.HeaderColors[Ui.ClampColor(Data.Color)], 0.2f)))
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
            body.ForeColor = done ? Color.FromArgb(0x77, 0x77, 0x77) : Ui.TextColor;
        }

        /// <summary>Shows the note's tags under the text, giving that line back to the text when there are none.</summary>
        public void RefreshTags()
        {
            bool any = Data.Tags.Count > 0;
            tagStrip.Text = Tags.Display(Data.Tags);
            tagStrip.Visible = any;
            body.Height = ClientSize.Height - body.Top - Ui.S(any ? 32 : 14);
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
            button.MouseEnter += delegate { button.BackColor = Ui.Darken(header.BackColor, 0.1f); };
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
            BackColor = Ui.BodyColors[index];
            body.BackColor = Ui.BodyColors[index];
            header.BackColor = Ui.HeaderColors[index];
            Color glyphColor = Ui.Darken(Ui.HeaderColors[index], 0.62f);
            foreach (Control button in header.Controls) button.ForeColor = glyphColor;
            tagStrip.ForeColor = Ui.Darken(Ui.HeaderColors[index], 0.55f);
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
