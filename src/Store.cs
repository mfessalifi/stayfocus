using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace StayFocus
{
    /// <summary>Where a note is in the backlog. Archived notes are kept but not shown on the desktop.</summary>
    static class NoteStatus
    {
        public const int ToDo = 0;
        public const int Doing = 1;
        public const int Done = 2;
        public const int Archive = 3;

        public static readonly string[] Names = { "To do", "Doing", "Done", "Archive" };

        public static int Clamp(int status)
        {
            return status >= ToDo && status <= Archive ? status : ToDo;
        }
    }

    [DataContract]
    public class NoteData
    {
        [DataMember] public string Id;
        [DataMember] public string Text;
        [DataMember] public int X;
        [DataMember] public int Y;
        [DataMember] public int W;
        [DataMember] public int H;
        [DataMember] public int Color;
        [DataMember] public bool Pinned;
        [DataMember] public int Status;
        [DataMember] public List<string> Tags;

        /// <summary>When to remind, local time as "yyyy-MM-dd HH:mm". Absent when the note has no reminder.</summary>
        [DataMember(EmitDefaultValue = false)] public string RemindAt;

        /// <summary>When the note was moved to the trash (same format). Only set for notes in the trash.</summary>
        [DataMember(EmitDefaultValue = false)] public string DeletedAt;
    }

    [DataContract]
    public class AppData
    {
        [DataMember] public string ToggleHotkey;
        [DataMember] public string NewNoteHotkey;
        [DataMember] public string BacklogHotkey;
        [DataMember] public string SearchHotkey;
        [DataMember] public bool NotesVisible;
        [DataMember] public bool HideFromCapture;
        /// <summary>0 = same as Windows, 1 = light, 2 = dark.</summary>
        [DataMember] public int Theme;
        [DataMember] public int BoardX;
        [DataMember] public int BoardY;
        [DataMember] public int BoardW;
        [DataMember] public int BoardH;
        [DataMember] public List<NoteData> Notes;
        /// <summary>Deleted notes, newest first. Each is removed for good after Store.TrashDays days.</summary>
        [DataMember] public List<NoteData> Trash;
    }

    /// <summary>The notes file exists but cannot be used safely; the app should tell the user and not start.</summary>
    class NotesFileException : Exception
    {
        public NotesFileException(string message) : base(message) { }
    }

    /// <summary>One saved copy of the notes file in the backups folder.</summary>
    class BackupInfo
    {
        public string Path;
        public DateTime When;
        /// <summary>True for the copy made just before a restore, false for the daily copies.</summary>
        public bool BeforeRestore;
    }

    /// <summary>Loads and saves everything to one JSON file in the user's AppData folder.</summary>
    static class Store
    {
        public const string DefaultToggleHotkey = "Ctrl+Alt+N";
        public const string DefaultNewHotkey = "Ctrl+Alt+Shift+N";
        public const string DefaultBacklogHotkey = "Ctrl+Alt+B";
        public const string DefaultSearchHotkey = "Ctrl+Alt+F";

        public const int TrashDays = 30;
        public const int DailyBackupsKept = 14;
        const int RestoreCopiesKept = 3;

        const string TimeFormat = "yyyy-MM-dd HH:mm";

        public static string Folder
        {
            get
            {
                // STAYFOCUS_DATA_DIR lets you keep the notes somewhere else (portable use, testing).
                string custom = Environment.GetEnvironmentVariable("STAYFOCUS_DATA_DIR");
                if (!string.IsNullOrEmpty(custom)) return custom;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StayFocusNotes");
            }
        }

        public static string BackupFolder
        {
            get { return Path.Combine(Folder, "backups"); }
        }

        static string FilePath
        {
            get { return Path.Combine(Folder, "notes.json"); }
        }

        // ----- times stored in the file -----

        public static string FormatTime(DateTime when)
        {
            return when.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        public static bool TryParseTime(string stored, out DateTime when)
        {
            return DateTime.TryParseExact(stored ?? "", TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
        }

        // ----- load / save -----

        public static AppData Load(out bool firstRun, out string warning)
        {
            firstRun = false;
            warning = null;
            AppData data = null;

            if (!File.Exists(FilePath))
            {
                firstRun = true;
            }
            else
            {
                byte[] bytes = ReadWithRetry();
                try
                {
                    data = Parse(bytes);
                    // The file is readable: keep today's copy of it before anything changes it.
                    BackupIfNewDay();
                }
                catch (Exception ex)
                {
                    if (!(ex is SerializationException || ex is System.Xml.XmlException)) throw;
                    // The content is damaged. Starting fresh will overwrite it on the next save,
                    // so only continue once a safety copy really exists.
                    string backup = Path.Combine(Folder, "notes.unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                    try
                    {
                        File.WriteAllBytes(backup, bytes);
                    }
                    catch (Exception copyError)
                    {
                        if (!(copyError is IOException || copyError is UnauthorizedAccessException)) throw;
                        throw new NotesFileException("Your notes file is damaged and a safety copy could not be made, so StayFocus Notes did not start.\n\n"
                            + FilePath + "\n\n" + copyError.Message);
                    }
                    warning = "The notes file could not be read. A copy was kept as " + Path.GetFileName(backup)
                        + ". Right-click the tray icon and choose Backups to bring back an earlier day.";
                }
            }

            if (data == null) data = new AppData { NotesVisible = true };
            Normalize(data);
            return data;
        }

        static AppData Parse(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
                return (AppData)new DataContractJsonSerializer(typeof(AppData)).ReadObject(stream);
        }

        /// <summary>Repairs whatever a hand-edited or older file may be missing, so the rest of the app can trust the data.</summary>
        static void Normalize(AppData data)
        {
            data.Notes = CleanNotes(data.Notes);
            data.Trash = CleanNotes(data.Trash);

            DateTime parsed;
            foreach (NoteData note in data.Notes) note.DeletedAt = null;
            foreach (NoteData note in data.Trash)
                if (!TryParseTime(note.DeletedAt, out parsed)) note.DeletedAt = FormatTime(DateTime.Now);

            if (data.Theme < 0 || data.Theme > 2) data.Theme = 0;

            Hotkey hotkey;
            if (!Hotkey.TryParse(data.ToggleHotkey, out hotkey)) data.ToggleHotkey = DefaultToggleHotkey;
            if (!Hotkey.TryParse(data.NewNoteHotkey, out hotkey)) data.NewNoteHotkey = DefaultNewHotkey;
            if (!Hotkey.TryParse(data.BacklogHotkey, out hotkey)) data.BacklogHotkey = DefaultBacklogHotkey;
            if (!Hotkey.TryParse(data.SearchHotkey, out hotkey)) data.SearchHotkey = DefaultSearchHotkey;
        }

        static List<NoteData> CleanNotes(List<NoteData> notes)
        {
            if (notes == null) notes = new List<NoteData>();
            notes.RemoveAll(delegate(NoteData n) { return n == null; });
            DateTime parsed;
            foreach (NoteData note in notes)
            {
                if (string.IsNullOrEmpty(note.Id)) note.Id = Guid.NewGuid().ToString("N");
                note.Status = NoteStatus.Clamp(note.Status);
                note.Tags = Tags.CleanList(note.Tags);
                if (!TryParseTime(note.RemindAt, out parsed)) note.RemindAt = null;
            }
            return notes;
        }

        public static void Save(AppData data)
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            using (FileStream fs = File.Create(temp))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, false, true, "  "))
            {
                new DataContractJsonSerializer(typeof(AppData)).WriteObject(writer, data);
            }
            // Swap the finished file in, so a crash mid-write cannot leave a half-written notes file.
            try
            {
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            finally
            {
                // If the swap failed, do not leave a stray copy of the notes lying around.
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Reads the notes file, waiting a few seconds if another program (antivirus, backup, sync) has it locked.
        /// If it still cannot be read the app must not start: starting empty would overwrite the real notes.
        /// </summary>
        static byte[] ReadWithRetry()
        {
            Exception last = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    return File.ReadAllBytes(FilePath);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                    last = ex;
                }
                System.Threading.Thread.Sleep(300);
            }
            throw new NotesFileException("Your notes file could not be opened, so StayFocus Notes did not start "
                + "(starting without it could overwrite your notes). Close any program using the file and try again.\n\n"
                + FilePath + "\n\n" + last.Message);
        }

        // ----- backups -----

        /// <summary>
        /// Keeps one copy of the notes file per day the app is used: the notes as they were when that day started.
        /// Does nothing if today's copy exists. A backup that cannot be written never stops the app.
        /// </summary>
        public static void BackupIfNewDay()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string target = Path.Combine(BackupFolder, "notes-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
                if (File.Exists(target)) return;
                Directory.CreateDirectory(BackupFolder);
                File.Copy(FilePath, target);
                Prune(false, DailyBackupsKept);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Copies the notes file as it is now, so a restore can itself be undone. False if the copy failed.</summary>
        public static bool BackupBeforeRestore()
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                if (File.Exists(FilePath))
                    File.Copy(FilePath, Path.Combine(BackupFolder, "notes-before-restore-"
                        + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json"), true);
                Prune(true, RestoreCopiesKept);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>The backups on disk, newest first.</summary>
        public static List<BackupInfo> ListBackups()
        {
            List<BackupInfo> list = new List<BackupInfo>();
            try
            {
                if (!Directory.Exists(BackupFolder)) return list;
                foreach (string path in Directory.GetFiles(BackupFolder, "notes-*.json"))
                {
                    string stamp = Path.GetFileNameWithoutExtension(path).Substring("notes-".Length);
                    BackupInfo info = new BackupInfo();
                    info.Path = path;
                    const string restorePrefix = "before-restore-";
                    if (stamp.StartsWith(restorePrefix, StringComparison.Ordinal))
                    {
                        info.BeforeRestore = true;
                        if (!DateTime.TryParseExact(stamp.Substring(restorePrefix.Length), "yyyyMMdd-HHmmss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out info.When)) continue;
                    }
                    else if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out info.When))
                    {
                        continue; // not one of ours
                    }
                    list.Add(info);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            list.Sort(delegate(BackupInfo a, BackupInfo b) { return b.When.CompareTo(a.When); });
            return list;
        }

        /// <summary>Reads a backup. Throws IOException, UnauthorizedAccessException or SerializationException if it cannot be used.</summary>
        public static AppData ReadBackup(string path)
        {
            AppData data;
            try
            {
                data = Parse(File.ReadAllBytes(path));
            }
            catch (System.Xml.XmlException ex)
            {
                throw new SerializationException(ex.Message, ex);
            }
            if (data == null) throw new SerializationException("The file is empty.");
            Normalize(data);
            return data;
        }

        static void Prune(bool beforeRestore, int keep)
        {
            int seen = 0;
            foreach (BackupInfo info in ListBackups())
            {
                if (info.BeforeRestore != beforeRestore) continue;
                if (++seen <= keep) continue;
                try { File.Delete(info.Path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
