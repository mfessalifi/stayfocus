using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StayFocus
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // "StayFocusNotes.exe --quit" asks the running copy to save and exit (used when updating the app).
            if (Array.IndexOf(args, "--quit") >= 0)
            {
                IntPtr target = Native.FindWindow(null, HotkeyWindow.Title);
                if (target != IntPtr.Zero) Native.PostMessage(target, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            bool isFirstInstance;
            using (Mutex mutex = new Mutex(true, "StayFocusNotes.SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    // Already running: ask that copy to show the notes, then leave.
                    IntPtr running = Native.FindWindow(null, HotkeyWindow.Title);
                    if (running != IntPtr.Zero) Native.PostMessage(running, Native.WM_SHOWNOTES, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                NotesApp app;
                try
                {
                    app = new NotesApp();
                }
                catch (NotesFileException ex)
                {
                    MessageBox.Show(ex.Message, "StayFocus Notes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Application.Run(app);
                GC.KeepAlive(mutex);
            }
        }
    }

    /// <summary>Invisible window that receives the global shortcuts from Windows.</summary>
    class HotkeyWindow : NativeWindow
    {
        public const string Title = "StayFocusNotes.MessageWindow";

        public event Action<int> HotkeyPressed;
        public event Action ShowRequested;
        public event Action ExitRequested;

        public HotkeyWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = Title;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                if (HotkeyPressed != null) HotkeyPressed(m.WParam.ToInt32());
                return;
            }
            if (m.Msg == Native.WM_SHOWNOTES)
            {
                if (ShowRequested != null) ShowRequested();
                return;
            }
            if (m.Msg == Native.WM_CLOSE)
            {
                // A polite "please quit" from outside (e.g. taskkill without /f): save and exit cleanly.
                if (ExitRequested != null) ExitRequested();
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>The app itself: tray icon, shortcuts, the notes and the backlog board.</summary>
    class NotesApp : ApplicationContext
    {
        const int ToggleId = 1;
        const int NewNoteId = 2;
        const int BacklogId = 3;
        const string AppName = "StayFocus Notes";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "StayFocusNotes";

        readonly AppData data;
        readonly List<NoteForm> notes = new List<NoteForm>();
        readonly HotkeyWindow window;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer saveTimer;
        ToolStripMenuItem newItem, toggleItem, backlogItem, startupItem, captureItem;
        BacklogForm backlog;
        NoteForm lastActive;
        bool boardDirty;
        bool saveFailed;
        bool shortcutsOpen;

        public bool Exiting { get; private set; }

        public List<NoteForm> Notes
        {
            get { return notes; }
        }

        /// <summary>When on, notes and the backlog do not appear in screenshots, recordings or screen sharing.</summary>
        public bool HideFromCapture
        {
            get { return data.HideFromCapture; }
        }

        void SetHideFromCapture(bool hide)
        {
            data.HideFromCapture = hide;
            foreach (NoteForm note in notes) note.CaptureSettingChanged();
            if (backlog != null) Ui.ApplyCapturePrivacy(backlog, hide);
            SaveNow();
        }

        public NotesApp()
        {
            bool firstRun;
            string warning;
            data = Store.Load(out firstRun, out warning);

            window = new HotkeyWindow();
            window.HotkeyPressed += OnHotkey;
            window.ShowRequested += delegate { ShowNotes(true); };
            window.ExitRequested += ExitApp;

            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 600;
            saveTimer.Tick += delegate
            {
                SaveNow();
                if (boardDirty) RefreshBacklog();
            };

            tray = BuildTray();

            foreach (NoteData note in data.Notes) notes.Add(new NoteForm(this, note));

            if (firstRun)
            {
                NoteData welcome = CreateNoteData(null);
                welcome.Text = "Welcome to StayFocus Notes!\r\n\r\n"
                    + data.ToggleHotkey + "\r\n   show / hide all notes\r\n"
                    + data.NewNoteHotkey + "\r\n   new note\r\n"
                    + data.BacklogHotkey + "\r\n   backlog board\r\n\r\n"
                    + "Drag the top bar to move me. Everything is saved automatically.";
                welcome.H = Ui.S(300);
                data.Notes.Add(welcome);
                notes.Add(new NoteForm(this, welcome));
                data.NotesVisible = true;
            }

            string failedHotkey = RegisterHotkeys();
            if (data.NotesVisible) ShowNotes(false);

            if (warning != null)
                tray.ShowBalloonTip(8000, AppName, warning, ToolTipIcon.Warning);
            else if (failedHotkey != null)
                tray.ShowBalloonTip(8000, AppName, failedHotkey + " is already used by another app. Right-click the tray icon and choose Shortcuts to pick another.", ToolTipIcon.Warning);
            else if (firstRun)
                tray.ShowBalloonTip(5000, AppName, "Running in the tray. Press " + data.ToggleHotkey + " to show or hide your notes.", ToolTipIcon.Info);

            Application.ApplicationExit += delegate { SaveNow(); };
        }

        // ----- notes -----

        public void ToggleNotes()
        {
            bool anyVisible = false;
            foreach (NoteForm note in notes) anyVisible |= note.Visible;
            if (anyVisible) HideNotes();
            else ShowNotes(true);
        }

        /// <summary>Shows every note that is not archived.</summary>
        /// <param name="activate">True when the user asked for the notes, so one of them takes the keyboard.</param>
        public void ShowNotes(bool activate)
        {
            List<NoteForm> onDesktop = notes.FindAll(delegate(NoteForm n) { return n.Data.Status != NoteStatus.Archive; });
            if (onDesktop.Count == 0)
            {
                if (activate) NewNote(null);
                return;
            }

            data.NotesVisible = true;
            foreach (NoteForm note in onDesktop)
            {
                note.EnsureOnScreen();
                if (!note.Visible) note.Show();
            }

            if (activate)
            {
                NoteForm target = lastActive != null && onDesktop.Contains(lastActive) ? lastActive : onDesktop[onDesktop.Count - 1];
                target.Activate();
                foreach (NoteForm note in onDesktop) note.RaiseToTop();
                target.RaiseToTop();
                target.FocusBody();
            }
            ScheduleSave();
        }

        public void HideNotes()
        {
            data.NotesVisible = false;
            foreach (NoteForm note in notes) note.Hide();
            ScheduleSave();
        }

        /// <summary>Shows one note and gives it the keyboard (used by the backlog, works for archived notes too).</summary>
        public void OpenNote(NoteForm note)
        {
            note.EnsureOnScreen();
            if (!note.Visible) note.Show();
            note.Activate();
            note.RaiseToTop();
            note.FocusBody();
        }

        public void NewNote(NoteForm near)
        {
            NoteData noteData = CreateNoteData(near);
            data.Notes.Add(noteData);
            NoteForm note = new NoteForm(this, noteData);
            notes.Add(note);
            lastActive = note;
            ShowNotes(true);
            RefreshBacklog();
        }

        public void SetStatus(NoteForm note, int status)
        {
            if (NoteStatus.Clamp(note.Data.Status) == status) return;
            note.Data.Status = status;
            note.RefreshLook();

            if (status == NoteStatus.Archive) note.Hide();
            else if (data.NotesVisible && !note.Visible)
            {
                note.EnsureOnScreen();
                note.Show();
            }
            RefreshBacklog();
            SaveNow();
        }

        public void DeleteNoteWithConfirm(NoteForm note, IWin32Window owner)
        {
            if ((note.Data.Text ?? "").Trim().Length > 0)
            {
                DialogResult answer = MessageBox.Show(owner, "Delete this note? This cannot be undone.", AppName,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.OK) return;
            }
            notes.Remove(note);
            data.Notes.Remove(note.Data);
            if (lastActive == note) lastActive = null;
            note.CloseForReal();
            RefreshBacklog();
            SaveNow();
        }

        public void NoteActivated(NoteForm note)
        {
            lastActive = note;
        }

        NoteData CreateNoteData(NoteForm near)
        {
            NoteData note = new NoteData();
            note.Id = Guid.NewGuid().ToString("N");
            note.Text = "";
            note.Pinned = true;
            note.Status = NoteStatus.ToDo;
            note.Tags = new List<string>();
            note.W = Ui.S(270);
            note.H = Ui.S(250);

            if (near != null)
            {
                note.Color = near.Data.Color;
                note.X = near.Left + Ui.S(28);
                note.Y = near.Top + Ui.S(28);
            }
            else
            {
                // Top-right of the screen the mouse is on, stepping down so notes do not stack exactly.
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                int step = Ui.S(28) * (notes.Count % 8);
                note.X = area.Right - note.W - Ui.S(48) - step;
                note.Y = area.Top + Ui.S(48) + step;
            }
            return note;
        }

        // ----- tags -----

        /// <summary>Every tag used by any note, sorted, each once.</summary>
        public List<string> AllTags()
        {
            SortedSet<string> all = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NoteForm note in notes)
                foreach (string tag in note.Data.Tags) all.Add(tag);
            return new List<string>(all);
        }

        /// <summary>Fills a menu with one tick-able entry per known tag plus "New tag...", for the given note.</summary>
        public void FillTagMenu(ToolStripItemCollection items, NoteForm note, IWin32Window owner)
        {
            while (items.Count > 0)
            {
                ToolStripItem old = items[0];
                items.RemoveAt(0);
                old.Dispose();
            }
            foreach (string known in AllTags())
            {
                string tag = known;
                ToolStripMenuItem item = new ToolStripMenuItem("#" + tag, null, delegate { ToggleTag(note, tag); });
                item.Checked = Tags.IndexOf(note.Data.Tags, tag) >= 0;
                items.Add(item);
            }
            if (items.Count > 0) items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("New tag...", null, delegate { PromptNewTags(note, owner); }));
        }

        void ToggleTag(NoteForm note, string tag)
        {
            int index = Tags.IndexOf(note.Data.Tags, tag);
            if (index >= 0) note.Data.Tags.RemoveAt(index);
            else if (!Tags.Add(note.Data.Tags, tag)) return;
            TagsChanged(note);
        }

        void PromptNewTags(NoteForm note, IWin32Window owner)
        {
            using (TagPromptForm dialog = new TagPromptForm())
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                List<string> known = AllTags();
                bool changed = false;
                foreach (string tag in Tags.Parse(dialog.Value))
                {
                    // Reuse the spelling of an existing tag so "Work" and "work" stay one tag.
                    int match = Tags.IndexOf(known, tag);
                    changed |= Tags.Add(note.Data.Tags, match >= 0 ? known[match] : tag);
                }
                if (changed) TagsChanged(note);
            }
        }

        void TagsChanged(NoteForm note)
        {
            note.RefreshTags();
            RefreshBacklog();
            SaveNow();
        }

        // ----- backlog board -----

        public void ShowBacklog()
        {
            if (backlog == null)
                backlog = new BacklogForm(this, new Rectangle(data.BoardX, data.BoardY, data.BoardW, data.BoardH));
            backlog.Rebuild();
            boardDirty = false;
            if (!backlog.Visible) backlog.Show();
            if (backlog.WindowState == FormWindowState.Minimized) backlog.WindowState = FormWindowState.Normal;
            backlog.Activate();
        }

        public void ToggleBacklog()
        {
            // Hide only when it is the window in use; otherwise bring it forward.
            if (backlog != null && backlog.Visible && Form.ActiveForm == backlog) backlog.Hide();
            else ShowBacklog();
        }

        public void BoardMoved(Rectangle bounds)
        {
            data.BoardX = bounds.X;
            data.BoardY = bounds.Y;
            data.BoardW = bounds.Width;
            data.BoardH = bounds.Height;
            ScheduleSave();
        }

        void RefreshBacklog()
        {
            boardDirty = false;
            if (backlog != null && backlog.Visible) backlog.Rebuild();
        }

        // ----- saving -----

        /// <summary>A note's text or color changed: save soon and refresh the backlog once typing pauses.</summary>
        public void ContentChanged()
        {
            boardDirty = true;
            ScheduleSave();
        }

        public void ScheduleSave()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        void SaveNow()
        {
            saveTimer.Stop();
            try
            {
                Store.Save(data);
                saveFailed = false;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                if (saveFailed || Exiting) return;
                saveFailed = true; // warn once, not on every keystroke
                tray.ShowBalloonTip(8000, AppName, "Could not save your notes: " + ex.Message, ToolTipIcon.Error);
            }
        }

        // ----- shortcuts -----

        void OnHotkey(int id)
        {
            if (id == ToggleId) ToggleNotes();
            else if (id == NewNoteId) NewNote(null);
            else if (id == BacklogId) ToggleBacklog();
        }

        /// <summary>Returns the first shortcut Windows refused (already taken), or null if all work.</summary>
        string RegisterHotkeys()
        {
            UnregisterHotkeys();
            string failed = null;
            if (!Register(ToggleId, data.ToggleHotkey)) failed = data.ToggleHotkey;
            if (!Register(NewNoteId, data.NewNoteHotkey) && failed == null) failed = data.NewNoteHotkey;
            if (!Register(BacklogId, data.BacklogHotkey) && failed == null) failed = data.BacklogHotkey;
            UpdateMenuShortcuts();
            return failed;
        }

        bool Register(int id, string text)
        {
            Hotkey hotkey;
            if (!Hotkey.TryParse(text, out hotkey)) return false;
            return Native.RegisterHotKey(window.Handle, id, hotkey.Mods | Native.MOD_NOREPEAT, (uint)hotkey.Key);
        }

        void UnregisterHotkeys()
        {
            Native.UnregisterHotKey(window.Handle, ToggleId);
            Native.UnregisterHotKey(window.Handle, NewNoteId);
            Native.UnregisterHotKey(window.Handle, BacklogId);
        }

        void EditShortcuts()
        {
            if (shortcutsOpen) return;
            shortcutsOpen = true;
            // Release the shortcuts while the dialog is open so pressing them is captured, not acted on.
            UnregisterHotkeys();
            try
            {
                string[] labels = { "Show / hide notes", "New note", "Show / hide backlog" };
                string[] current = { data.ToggleHotkey, data.NewNoteHotkey, data.BacklogHotkey };
                string[] defaults = { Store.DefaultToggleHotkey, Store.DefaultNewHotkey, Store.DefaultBacklogHotkey };
                using (ShortcutsForm dialog = new ShortcutsForm(labels, current, defaults, TryApplyHotkeys))
                    dialog.ShowDialog();
            }
            finally
            {
                RegisterHotkeys();
                shortcutsOpen = false;
            }
        }

        /// <param name="values">Toggle, new note, backlog - in that order.</param>
        string TryApplyHotkeys(string[] values)
        {
            Hotkey parsed;
            foreach (string value in values)
                if (!Hotkey.TryParse(value, out parsed))
                    return "Each shortcut needs at least two of Ctrl, Alt, Shift plus one more key.";

            string[] old = { data.ToggleHotkey, data.NewNoteHotkey, data.BacklogHotkey };
            data.ToggleHotkey = values[0];
            data.NewNoteHotkey = values[1];
            data.BacklogHotkey = values[2];
            string failed = RegisterHotkeys();
            UnregisterHotkeys(); // the dialog is still open; EditShortcuts registers again when it closes
            if (failed != null)
            {
                data.ToggleHotkey = old[0];
                data.NewNoteHotkey = old[1];
                data.BacklogHotkey = old[2];
                return failed + " is already used by Windows or another app. Please choose a different one.";
            }
            SaveNow();
            return null;
        }

        // ----- tray -----

        NotifyIcon BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem about = new ToolStripMenuItem(AppName + " " + typeof(Program).Assembly.GetName().Version.ToString(3));
            about.Enabled = false; // just a label telling which version is running
            menu.Items.Add(about);
            menu.Items.Add(new ToolStripSeparator());
            newItem = new ToolStripMenuItem("New note", null, delegate { NewNote(null); });
            toggleItem = new ToolStripMenuItem("Show / hide notes", null, delegate { ToggleNotes(); });
            backlogItem = new ToolStripMenuItem("Backlog", null, delegate { ShowBacklog(); });
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { SetStartWithWindows(!startupItem.Checked); });
            menu.Items.Add(newItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(backlogItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Shortcuts...", null, delegate { EditShortcuts(); }));
            captureItem = new ToolStripMenuItem("Hide from screen capture", null, delegate { SetHideFromCapture(!data.HideFromCapture); });
            menu.Items.Add(startupItem);
            menu.Items.Add(captureItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApp(); }));
            menu.Opening += delegate
            {
                startupItem.Checked = StartsWithWindows();
                captureItem.Checked = data.HideFromCapture;
            };

            NotifyIcon icon = new NotifyIcon();
            icon.Icon = Ui.LoadIcon(SystemInformation.SmallIconSize);
            icon.Text = AppName;
            icon.ContextMenuStrip = menu;
            icon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ToggleNotes();
            };
            icon.Visible = true;
            return icon;
        }

        void UpdateMenuShortcuts()
        {
            if (toggleItem == null) return;
            toggleItem.ShortcutKeyDisplayString = data.ToggleHotkey;
            newItem.ShortcutKeyDisplayString = data.NewNoteHotkey;
            backlogItem.ShortcutKeyDisplayString = data.BacklogHotkey;
        }

        static bool StartsWithWindows()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }

        static void SetStartWithWindows(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(RunValue, false);
            }
        }

        void ExitApp()
        {
            SaveNow();
            Exiting = true;
            UnregisterHotkeys();
            tray.Visible = false;
            foreach (NoteForm note in notes.ToArray()) note.CloseForReal();
            if (backlog != null) backlog.CloseForReal();
            tray.Dispose();
            window.DestroyHandle();
            ExitThread();
        }
    }
}
