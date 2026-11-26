using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>Reminder times: how they are shown, and the dialog to pick one.</summary>
    static class Reminder
    {
        public const string Clock = "⏰"; // alarm clock character, shown before a reminder time

        /// <summary>"Today 14:30", "Tomorrow 09:00", "Fri 27 Nov 09:00".</summary>
        public static string Display(DateTime when)
        {
            DateTime today = DateTime.Today;
            string time = when.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (when.Date == today) return "Today " + time;
            if (when.Date == today.AddDays(1)) return "Tomorrow " + time;
            if (when.Date == today.AddDays(-1)) return "Yesterday " + time;
            string day = when.ToString(when.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", CultureInfo.InvariantCulture);
            return day + " " + time;
        }

        /// <summary>The same moment without seconds, moved up to the next full minute.</summary>
        public static DateTime RoundUp(DateTime when)
        {
            DateTime minute = new DateTime(when.Year, when.Month, when.Day, when.Hour, when.Minute, 0);
            return minute < when ? minute.AddMinutes(1) : minute;
        }

        public static DateTime NextMonday(DateTime from)
        {
            int days = ((int)DayOfWeek.Monday - (int)from.DayOfWeek + 7) % 7;
            return from.Date.AddDays(days == 0 ? 7 : days);
        }
    }

    /// <summary>Small dialog asking for the date and time of a reminder.</summary>
    class ReminderForm : Form
    {
        readonly DateTimePicker date = new DateTimePicker();
        readonly DateTimePicker time = new DateTimePicker();

        public DateTime Value
        {
            get
            {
                DateTime t = time.Value;
                return date.Value.Date.AddHours(t.Hour).AddMinutes(t.Minute);
            }
        }

        public ReminderForm(DateTime initial)
        {
            SuspendLayout();
            Text = "Remind me";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true; // notes are always-on-top, the dialog must not open behind them

            Label dateLabel = new Label();
            dateLabel.AutoSize = true;
            dateLabel.Text = "Day";
            dateLabel.Location = new Point(14, 18);
            Controls.Add(dateLabel);

            date.Format = DateTimePickerFormat.Custom;
            date.CustomFormat = "dddd d MMMM yyyy";
            date.MinDate = DateTime.Today;
            date.Value = initial < DateTime.Today ? DateTime.Today : initial.Date;
            date.SetBounds(60, 14, 266, 24);
            Controls.Add(date);

            Label timeLabel = new Label();
            timeLabel.AutoSize = true;
            timeLabel.Text = "Time";
            timeLabel.Location = new Point(14, 52);
            Controls.Add(timeLabel);

            time.Format = DateTimePickerFormat.Custom;
            time.CustomFormat = "HH:mm";
            time.ShowUpDown = true;
            time.Value = DateTime.Today.AddHours(initial.Hour).AddMinutes(initial.Minute);
            time.SetBounds(60, 48, 80, 24);
            Controls.Add(time);

            Button ok = new Button();
            ok.Text = "Set";
            ok.SetBounds(166, 88, 76, 28);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.SetBounds(250, 88, 76, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(340, 130);
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
