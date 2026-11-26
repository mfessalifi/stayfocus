using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>A dialog with a hint, a list and a row of buttons. Used for the trash and for the backups.</summary>
    class ListDialog : Form
    {
        readonly ListBox list = new ListBox();
        readonly List<Button> needSelection = new List<Button>();
        int nextButtonX = Ui.S(16);
        bool placeholder; // the list only shows a "nothing here" line

        /// <summary>Index of the chosen line, or -1.</summary>
        public int SelectedIndex
        {
            get { return placeholder ? -1 : list.SelectedIndex; }
        }

        public ListDialog(string title, string hint)
        {
            AutoScaleMode = AutoScaleMode.None; // sizes below are scaled by hand with Ui.S
            Text = title;
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true; // notes are always-on-top, the dialog must not open behind them
            ClientSize = new Size(Ui.S(540), Ui.S(334));

            Label hintLabel = new Label();
            hintLabel.AutoSize = false;
            hintLabel.Text = hint;
            hintLabel.Tag = "faint";
            hintLabel.ForeColor = SystemColors.GrayText;
            hintLabel.SetBounds(Ui.S(16), Ui.S(12), Ui.S(508), Ui.S(34));
            Controls.Add(hintLabel);

            list.IntegralHeight = false;
            list.HorizontalScrollbar = true;
            list.SetBounds(Ui.S(16), Ui.S(50), Ui.S(508), Ui.S(230));
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.Add(list);

            Button close = new Button();
            close.Text = "Close";
            close.SetBounds(Ui.S(448), Ui.S(292), Ui.S(76), Ui.S(28));
            close.DialogResult = DialogResult.Cancel;
            Controls.Add(close);
            CancelButton = close;
            Ui.StyleDialog(this);
        }

        /// <summary>Adds a button to the bottom row, left to right.</summary>
        /// <param name="needsSelection">True if the button only makes sense with a line selected.</param>
        public void AddButton(string text, bool needsSelection, EventHandler onClick)
        {
            Button button = new Button();
            button.Text = text;
            int width = Math.Max(Ui.S(76), TextRenderer.MeasureText(text, Font).Width + Ui.S(24));
            button.SetBounds(nextButtonX, Ui.S(292), width, Ui.S(28));
            nextButtonX += width + Ui.S(8);
            button.Click += onClick;
            Controls.Add(button);
            if (needsSelection) needSelection.Add(button);
            Ui.StyleDialog(this);
            UpdateButtons();
        }

        /// <summary>Replaces the lines, keeping the selection near where it was.</summary>
        public void SetItems(IList<string> lines, string emptyText)
        {
            int keep = list.SelectedIndex;
            placeholder = lines.Count == 0;
            list.BeginUpdate();
            list.Items.Clear();
            if (placeholder) list.Items.Add(emptyText);
            else foreach (string line in lines) list.Items.Add(line);
            list.EndUpdate();
            list.Enabled = !placeholder;
            if (!placeholder) list.SelectedIndex = Math.Max(0, Math.Min(keep, lines.Count - 1));
            UpdateButtons();
        }

        void UpdateButtons()
        {
            foreach (Button button in needSelection) button.Enabled = SelectedIndex >= 0;
        }
    }
}
