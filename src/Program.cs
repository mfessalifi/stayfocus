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
        const int SearchId = 4;
        const string AppName = "StayFocus Notes";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "StayFocusNotes";
        static readonly string[] ThemeNames = { "Same as Windows", "Light", "Dark" };

        readonly AppData data;
        readonly List<NoteForm> notes = new List<NoteForm>();
        // Reminders already announced in this session: note -> the reminder time that was announced.
        readonly Dictionary<NoteData, string> announced = new Dictionary<NoteData, string>();
        readonly HotkeyWindow window;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer saveTimer;
        readonly System.Windows.Forms.Timer clock;
        readonly ToolStripMenuItem[] themeItems = new ToolStripMenuItem[ThemeNames.Length];
        ToolStripMenuItem newItem, toggleItem, backlogItem, searchItem, trashItem, startupItem, captureItem;
        BacklogForm backlog;
        SearchForm search;
        NoteForm lastActive;
        NoteForm balloonNote; // the note a click on the reminder balloon opens
        DateTime today = DateTime.Today;
        bool boardDirty;
        bool saveFailed;
        bool dialogOpen;
        bool trashHintShown;

        public bool Exiting { get; private set; }

        public List<NoteForm> Notes
        {
            get { return notes; }
        }

        public int TrashCount
        {
            get { return data.Trash.Count; }
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
            Ui.SetDark(WantsDark());

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
                    + data.BacklogHotkey + "\r\n   backlog board\r\n"
                    + data.SearchHotkey + "\r\n   search your notes\r\n\r\n"
                    + "Drag the top bar to move me. Everything is saved automatically.";
                welcome.H = Ui.S(330);
                data.Notes.Add(welcome);
                notes.Add(new NoteForm(this, welcome));
                data.NotesVisible = true;
            }

            if (PurgeTrash()) SaveNow();
            string failedHotkey = RegisterHotkeys();
            if (data.NotesVisible) ShowNotes(false);
            CheckReminders(); // reminders that came due while the app was not running

            // Every few seconds: is a reminder due, has the day changed?
            clock = new System.Windows.Forms.Timer();
            clock.Interval = 5000;
            clock.Tick += delegate { OnClock(); };
            clock.Start();

            // Follow Windows when it switches between light and dark.
            SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;

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

        /// <summary>Shows one note and gives it the keyboard (used by the backlog and the search, works for archived notes too).</summary>
        public void OpenNote(NoteForm note)
        {
            if (!notes.Contains(note)) return; // deleted in the meantime
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

        static string FirstLine(NoteData note, int maxLength)
        {
            string text = (note.Text ?? "").Trim();
            int end = text.IndexOfAny(new char[] { '\r', '\n' });
            if (end >= 0) text = text.Substring(0, end);
            if (text.Length == 0) return "(empty note)";
            return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
        }

        // ----- trash -----

        /// <summary>Moves a note to the trash, where it can be restored for a while. An empty note is simply removed.</summary>
        public void DeleteNote(NoteForm note)
        {
            notes.Remove(note);
            data.Notes.Remove(note.Data);
            announced.Remove(note.Data);
            if (lastActive == note) lastActive = null;
            if (balloonNote == note) balloonNote = null;

            bool worthKeeping = (note.Data.Text ?? "").Trim().Length > 0;
            if (worthKeeping)
            {
                note.Data.DeletedAt = Store.FormatTime(DateTime.Now);
                data.Trash.Insert(0, note.Data);
            }
            note.CloseForReal();
            RefreshBacklog();
            SaveNow();

            if (worthKeeping && !trashHintShown)
            {
                trashHintShown = true; // once per session is enough
                tray.ShowBalloonTip(6000, AppName, "Note moved to the trash. To bring it back within " + Store.TrashDays
                    + " days: right-click the tray icon and choose Trash.", ToolTipIcon.Info);
            }
        }

        void RestoreFromTrash(NoteData note)
        {
            if (!data.Trash.Remove(note)) return;
            note.DeletedAt = null;
            data.Notes.Add(note);
            NoteForm form = new NoteForm(this, note);
            notes.Add(form);
            if (note.Status != NoteStatus.Archive)
            {
                form.EnsureOnScreen();
                form.Show();
                form.RaiseToTop();
            }
            RefreshBacklog();
            SaveNow();
        }

        /// <summary>Removes notes that have been in the trash longer than Store.TrashDays. True if any was removed.</summary>
        bool PurgeTrash()
        {
            DateTime limit = DateTime.Now.AddDays(-Store.TrashDays);
            return data.Trash.RemoveAll(delegate(NoteData note)
            {
                DateTime deleted;
                return Store.TryParseTime(note.DeletedAt, out deleted) && deleted < limit;
            }) > 0;
        }

        public void ShowTrash()
        {
            if (dialogOpen) return;
            dialogOpen = true;
            try
            {
                using (ListDialog dialog = new ListDialog(AppName + " - Trash",
                    "Deleted notes stay here for " + Store.TrashDays + " days, then they are removed for good."))
                {
                    MethodInvoker refill = delegate
                    {
                        List<string> lines = new List<string>();
                        foreach (NoteData note in data.Trash)
                        {
                            DateTime deleted;
                            Store.TryParseTime(note.DeletedAt, out deleted);
                            lines.Add("Deleted " + Reminder.Display(deleted) + "      " + FirstLine(note, 70));
                        }
                        dialog.SetItems(lines, "The trash is empty.");
                        RefreshBacklog();
                    };
                    dialog.AddButton("Restore", true, delegate
                    {
                        int index = dialog.SelectedIndex;
                        if (index < 0 || index >= data.Trash.Count) return;
                        RestoreFromTrash(data.Trash[index]);
                        refill();
                    });
                    dialog.AddButton("Delete forever", true, delegate
                    {
                        int index = dialog.SelectedIndex;
                        if (index < 0 || index >= data.Trash.Count) return;
                        if (!Confirm(dialog, "Delete this note for good? This cannot be undone.")) return;
                        data.Trash.RemoveAt(index);
                        SaveNow();
                        refill();
                    });
                    dialog.AddButton("Empty trash", false, delegate
                    {
                        if (data.Trash.Count == 0) return;
                        if (!Confirm(dialog, "Delete everything in the trash for good (" + data.Trash.Count + " notes)? This cannot be undone.")) return;
                        data.Trash.Clear();
                        SaveNow();
                        refill();
                    });
                    refill();
                    dialog.ShowDialog();
                }
            }
            finally
            {
                dialogOpen = false;
            }
        }

        static bool Confirm(IWin32Window owner, string question)
        {
            return MessageBox.Show(owner, question, AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.OK;
        }

        // ----- backups -----

        public void ShowBackups()
        {
            if (dialogOpen) return;
            dialogOpen = true;
            try
            {
                SaveNow();
                using (ListDialog dialog = new ListDialog(AppName + " - Backups",
                    "A copy of your notes is kept for each of the last " + Store.DailyBackupsKept + " days the app was used. "
                    + "Restoring one replaces the notes you have now."))
                {
                    List<BackupInfo> backups = new List<BackupInfo>();
                    MethodInvoker refill = delegate
                    {
                        backups = Store.ListBackups();
                        List<string> lines = new List<string>();
                        foreach (BackupInfo backup in backups) lines.Add(DescribeBackup(backup));
                        dialog.SetItems(lines, "No backup yet. The first one is made the next day the app starts.");
                    };
                    dialog.AddButton("Restore", true, delegate
                    {
                        int index = dialog.SelectedIndex;
                        if (index < 0 || index >= backups.Count) return;
                        if (!Confirm(dialog, "Replace your current notes with this backup?\n\n"
                            + "Your current notes are saved as a backup first, so you can come back to them.")) return;
                        string error = RestoreBackup(backups[index]);
                        if (error != null) MessageBox.Show(dialog, error, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        refill();
                    });
                    dialog.AddButton("Open folder", false, delegate
                    {
                        try
                        {
                            Directory.CreateDirectory(Store.BackupFolder);
                            System.Diagnostics.Process.Start("explorer.exe", "\"" + Store.BackupFolder + "\"");
                        }
                        catch (Exception ex)
                        {
                            if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)) throw;
                            MessageBox.Show(dialog, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    });
                    refill();
                    dialog.ShowDialog();
                }
            }
            finally
            {
                dialogOpen = false;
            }
        }

        static string DescribeBackup(BackupInfo backup)
        {
            string label = backup.BeforeRestore
                ? "Before a restore, " + backup.When.ToString("ddd d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                : "Start of " + backup.When.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                int count = Store.ReadBackup(backup.Path).Notes.Count;
                return label + "      " + count + (count == 1 ? " note" : " notes");
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.Serialization.SerializationException)) throw;
                return label + "      (cannot be read)";
            }
        }

        /// <summary>Replaces the notes and the trash with those of a backup; settings are kept. Returns an error message, or null.</summary>
        string RestoreBackup(BackupInfo backup)
        {
            AppData old;
            try
            {
                old = Store.ReadBackup(backup.Path);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.Serialization.SerializationException)) throw;
                return "This backup cannot be read, so nothing was changed.\n\n" + ex.Message;
            }

            SaveNow();
            if (saveFailed || !Store.BackupBeforeRestore())
                return "A copy of your current notes could not be saved first, so nothing was changed.";

            foreach (NoteForm note in notes.ToArray()) note.CloseForReal();
            notes.Clear();
            announced.Clear();
            lastActive = null;
            balloonNote = null;

            data.Notes = old.Notes;
            data.Trash = old.Trash;
            PurgeTrash();
            foreach (NoteData note in data.Notes) notes.Add(new NoteForm(this, note));

            ShowNotes(false);
            RefreshBacklog();
            SaveNow();
            CheckReminders();
            return null;
        }

        // ----- reminders -----

        /// <summary>Fills a menu with the choices for a note's reminder.</summary>
        public void FillReminderMenu(ToolStripItemCollection items, NoteForm note, IWin32Window owner)
        {
            ClearMenu(items);
            DateTime now = DateTime.Now;
            DateTime current;
            bool has = Store.TryParseTime(note.Data.RemindAt, out current);
            if (has)
            {
                ToolStripMenuItem label = new ToolStripMenuItem((current <= now ? "Was due: " : "Set for: ") + Reminder.Display(current));
                label.Enabled = false;
                items.Add(label);
                items.Add(new ToolStripSeparator());
            }

            AddReminderChoice(items, note, "In 15 minutes", Reminder.RoundUp(now.AddMinutes(15)));
            AddReminderChoice(items, note, "In 1 hour", Reminder.RoundUp(now.AddHours(1)));
            if (now.Hour < 17) AddReminderChoice(items, note, "Today at 18:00", now.Date.AddHours(18));
            AddReminderChoice(items, note, "Tomorrow at 09:00", now.Date.AddDays(1).AddHours(9));
            AddReminderChoice(items, note, "Next Monday at 09:00", Reminder.NextMonday(now).AddHours(9));
            items.Add(new ToolStripMenuItem("Pick a day and time...", null, delegate { PickReminder(note, owner); }));
            if (has)
            {
                items.Add(new ToolStripSeparator());
                items.Add(new ToolStripMenuItem(current <= now ? "Done - remove the reminder" : "Remove the reminder", null,
                    delegate { SetReminder(note, null); }));
            }
        }

        void AddReminderChoice(ToolStripItemCollection items, NoteForm note, string text, DateTime when)
        {
            items.Add(new ToolStripMenuItem(text, null, delegate { SetReminder(note, when); }));
        }

        void PickReminder(NoteForm note, IWin32Window owner)
        {
            DateTime initial;
            if (!Store.TryParseTime(note.Data.RemindAt, out initial) || initial < DateTime.Now)
                initial = DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1);
            using (ReminderForm dialog = new ReminderForm(initial))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                SetReminder(note, dialog.Value);
            }
        }

        void SetReminder(NoteForm note, DateTime? when)
        {
            if (!notes.Contains(note)) return;
            note.Data.RemindAt = when.HasValue ? Store.FormatTime(when.Value) : null;
            announced.Remove(note.Data);
            note.StopAlert();
            note.RefreshReminder();
            RefreshBacklog();
            SaveNow();
            CheckReminders(); // a time in the past is due right away
        }

        /// <summary>
        /// Announces every reminder that has come due: the note comes forward, its top bar blinks and the tray shows a message.
        /// The reminder then stays on the note, marked as due, until the user removes or moves it.
        /// </summary>
        void CheckReminders()
        {
            DateTime now = DateTime.Now;
            List<NoteForm> due = new List<NoteForm>();
            foreach (NoteForm note in notes)
            {
                DateTime when;
                if (!Store.TryParseTime(note.Data.RemindAt, out when) || when > now) continue;
                string already;
                if (announced.TryGetValue(note.Data, out already) && already == note.Data.RemindAt) continue;
                announced[note.Data] = note.Data.RemindAt;
                due.Add(note);
            }
            if (due.Count == 0) return;

            foreach (NoteForm note in due)
            {
                note.EnsureOnScreen();
                note.RefreshReminder();
                if (!note.Visible) note.Show(); // shown without taking the keyboard
                note.StartAlert();
            }
            RefreshBacklog();

            balloonNote = due[due.Count - 1];
            string message = due.Count == 1 ? FirstLine(balloonNote.Data, 120) : due.Count + " reminders are due.";
            tray.ShowBalloonTip(10000, "Reminder", message, ToolTipIcon.Info);
        }

        void OnClock()
        {
            CheckReminders();
            if (DateTime.Today == today) return;

            // A new day: keep a copy of the notes, clean the trash, and "Tomorrow 09:00" now reads "Today 09:00".
            today = DateTime.Today;
            Store.BackupIfNewDay();
            if (PurgeTrash()) SaveNow();
            foreach (NoteForm note in notes) note.RefreshReminder();
            RefreshBacklog();
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
            ClearMenu(items);
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

        static void ClearMenu(ToolStripItemCollection items)
        {
            while (items.Count > 0)
            {
                ToolStripItem old = items[0];
                items.RemoveAt(0);
                old.Dispose();
            }
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

        // ----- backlog board and search -----

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

        /// <summary>Opens the quick search window; pressing the shortcut again while it is in use closes it.</summary>
        public void ToggleSearch()
        {
            if (search != null && !search.IsDisposed)
            {
                if (Form.ActiveForm == search) search.Close();
                else search.Activate();
                return;
            }
            search = new SearchForm(this);
            search.FormClosed += delegate(object sender, FormClosedEventArgs e)
            {
                if (search == sender) search = null;
            };
            search.Show();
            search.Activate();
        }

        // ----- theme -----

        bool WantsDark()
        {
            return data.Theme == 2 || (data.Theme == 0 && Ui.WindowsPrefersDark());
        }

        void SetTheme(int theme)
        {
            data.Theme = theme;
            ApplyTheme();
            SaveNow();
        }

        void ApplyTheme()
        {
            bool dark = WantsDark();
            if (dark == Ui.Dark) return;
            Ui.SetDark(dark);
            foreach (NoteForm note in notes) note.ApplyTheme();
            if (backlog != null) backlog.ApplyTheme();
            if (search != null && !search.IsDisposed) search.Close(); // reopens with the new colors
        }

        void OnWindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Exiting || data.Theme != 0 || e.Category != UserPreferenceCategory.General) return;
            ApplyTheme();
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
            else if (id == SearchId) ToggleSearch();
        }

        /// <summary>Returns the first shortcut Windows refused (already taken), or null if all work.</summary>
        string RegisterHotkeys()
        {
            UnregisterHotkeys();
            string failed = null;
            if (!Register(ToggleId, data.ToggleHotkey)) failed = data.ToggleHotkey;
            if (!Register(NewNoteId, data.NewNoteHotkey) && failed == null) failed = data.NewNoteHotkey;
            if (!Register(BacklogId, data.BacklogHotkey) && failed == null) failed = data.BacklogHotkey;
            if (!Register(SearchId, data.SearchHotkey) && failed == null) failed = data.SearchHotkey;
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
            Native.UnregisterHotKey(window.Handle, SearchId);
        }

        void EditShortcuts()
        {
            if (dialogOpen) return;
            dialogOpen = true;
            // Release the shortcuts while the dialog is open so pressing them is captured, not acted on.
            UnregisterHotkeys();
            try
            {
                string[] labels = { "Show / hide notes", "New note", "Show / hide backlog", "Search notes" };
                string[] current = { data.ToggleHotkey, data.NewNoteHotkey, data.BacklogHotkey, data.SearchHotkey };
                string[] defaults = { Store.DefaultToggleHotkey, Store.DefaultNewHotkey, Store.DefaultBacklogHotkey, Store.DefaultSearchHotkey };
                using (ShortcutsForm dialog = new ShortcutsForm(labels, current, defaults, TryApplyHotkeys))
                    dialog.ShowDialog();
            }
            finally
            {
                RegisterHotkeys();
                dialogOpen = false;
            }
        }

        /// <param name="values">Toggle, new note, backlog, search - in that order.</param>
        string TryApplyHotkeys(string[] values)
        {
            Hotkey parsed;
            foreach (string value in values)
                if (!Hotkey.TryParse(value, out parsed))
                    return "Each shortcut needs at least two of Ctrl, Alt, Shift plus one more key.";

            string[] old = { data.ToggleHotkey, data.NewNoteHotkey, data.BacklogHotkey, data.SearchHotkey };
            data.ToggleHotkey = values[0];
            data.NewNoteHotkey = values[1];
            data.BacklogHotkey = values[2];
            data.SearchHotkey = values[3];
            string failed = RegisterHotkeys();
            UnregisterHotkeys(); // the dialog is still open; EditShortcuts registers again when it closes
            if (failed != null)
            {
                data.ToggleHotkey = old[0];
                data.NewNoteHotkey = old[1];
                data.BacklogHotkey = old[2];
                data.SearchHotkey = old[3];
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
            searchItem = new ToolStripMenuItem("Search notes", null, delegate { ToggleSearch(); });
            menu.Items.Add(newItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(backlogItem);
            menu.Items.Add(searchItem);
            menu.Items.Add(new ToolStripSeparator());
            trashItem = new ToolStripMenuItem("Trash...", null, delegate { ShowTrash(); });
            menu.Items.Add(trashItem);
            menu.Items.Add(new ToolStripMenuItem("Backups...", null, delegate { ShowBackups(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Shortcuts...", null, delegate { EditShortcuts(); }));
            ToolStripMenuItem theme = new ToolStripMenuItem("Theme");
            for (int i = 0; i < ThemeNames.Length; i++)
            {
                int choice = i;
                themeItems[i] = new ToolStripMenuItem(ThemeNames[i], null, delegate { SetTheme(choice); });
                theme.DropDownItems.Add(themeItems[i]);
            }
            menu.Items.Add(theme);
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { SetStartWithWindows(!startupItem.Checked); });
            captureItem = new ToolStripMenuItem("Hide from screen capture", null, delegate { SetHideFromCapture(!data.HideFromCapture); });
            menu.Items.Add(startupItem);
            menu.Items.Add(captureItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApp(); }));
            menu.Opening += delegate
            {
                startupItem.Checked = StartsWithWindows();
                captureItem.Checked = data.HideFromCapture;
                trashItem.Text = data.Trash.Count == 0 ? "Trash..." : "Trash (" + data.Trash.Count + ")...";
                for (int i = 0; i < themeItems.Length; i++) themeItems[i].Checked = i == data.Theme;
            };

            NotifyIcon icon = new NotifyIcon();
            icon.Icon = Ui.LoadIcon(SystemInformation.SmallIconSize);
            icon.Text = AppName;
            icon.ContextMenuStrip = menu;
            icon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ToggleNotes();
            };
            icon.BalloonTipClicked += delegate
            {
                // Only the reminder message leads somewhere.
                NoteForm target = balloonNote;
                balloonNote = null;
                if (target != null) OpenNote(target);
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
            searchItem.ShortcutKeyDisplayString = data.SearchHotkey;
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
            SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
            clock.Stop();
            UnregisterHotkeys();
            tray.Visible = false;
            if (search != null && !search.IsDisposed) search.Close();
            foreach (NoteForm note in notes.ToArray()) note.CloseForReal();
            if (backlog != null) backlog.CloseForReal();
            tray.Dispose();
            window.DestroyHandle();
            ExitThread();
        }
    }
}
