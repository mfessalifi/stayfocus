using System;
using System.Collections.Generic;
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
    }

    [DataContract]
    public class AppData
    {
        [DataMember] public string ToggleHotkey;
        [DataMember] public string NewNoteHotkey;
        [DataMember] public string BacklogHotkey;
        [DataMember] public bool NotesVisible;
        [DataMember] public bool HideFromCapture;
        [DataMember] public int BoardX;
        [DataMember] public int BoardY;
        [DataMember] public int BoardW;
        [DataMember] public int BoardH;
        [DataMember] public List<NoteData> Notes;
    }

    /// <summary>The notes file exists but cannot be used safely; the app should tell the user and not start.</summary>
    class NotesFileException : Exception
    {
        public NotesFileException(string message) : base(message) { }
    }

    /// <summary>Loads and saves everything to one JSON file in the user's AppData folder.</summary>
    static class Store
    {
        public const string DefaultToggleHotkey = "Ctrl+Alt+N";
        public const string DefaultNewHotkey = "Ctrl+Alt+Shift+N";
        public const string DefaultBacklogHotkey = "Ctrl+Alt+B";

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

        static string FilePath
        {
            get { return Path.Combine(Folder, "notes.json"); }
        }

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
                    using (MemoryStream stream = new MemoryStream(bytes))
                        data = (AppData)new DataContractJsonSerializer(typeof(AppData)).ReadObject(stream);
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
                    warning = "The notes file could not be read. A copy was kept as " + Path.GetFileName(backup) + ".";
                }
            }

            if (data == null) data = new AppData { NotesVisible = true };
            if (data.Notes == null) data.Notes = new List<NoteData>();
            data.Notes.RemoveAll(delegate(NoteData n) { return n == null; });
            foreach (NoteData note in data.Notes)
            {
                note.Status = NoteStatus.Clamp(note.Status);
                note.Tags = Tags.CleanList(note.Tags);
            }

            Hotkey parsed;
            if (!Hotkey.TryParse(data.ToggleHotkey, out parsed)) data.ToggleHotkey = DefaultToggleHotkey;
            if (!Hotkey.TryParse(data.NewNoteHotkey, out parsed)) data.NewNoteHotkey = DefaultNewHotkey;
            if (!Hotkey.TryParse(data.BacklogHotkey, out parsed)) data.BacklogHotkey = DefaultBacklogHotkey;
            return data;
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
    }
}
