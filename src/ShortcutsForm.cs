using System;
using System.Drawing;
using System.Windows.Forms;

namespace StayFocus
{
    /// <summary>A text box that shows the key combination pressed while it has focus.</summary>
    class HotkeyBox : TextBox
    {
        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            BackColor = SystemColors.Window;
            TextAlign = HorizontalAlignment.Center;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys modifiers = keyData & Keys.Modifiers;

            // Leave plain Tab / Enter / Esc to the dialog.
            if (modifiers == Keys.None && (key == Keys.Tab || key == Keys.Enter || key == Keys.Escape))
                return base.ProcessCmdKey(ref msg, keyData);

            bool modifierOnly = key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu
                || key == Keys.LWin || key == Keys.RWin;
            Hotkey parsed;
            string combo = Hotkey.Format(modifiers, key);
            if (!modifierOnly && Hotkey.TryParse(combo, out parsed)) Text = combo;
            return true;
        }
    }

    class ShortcutsForm : Form
    {
        readonly HotkeyBox[] boxes;
        readonly string[] defaults;
        readonly Func<string[], string> tryApply;

        /// <param name="tryApply">Applies the shortcuts (same order as labels) and returns an error message, or null on success.</param>
        public ShortcutsForm(string[] labels, string[] current, string[] defaults, Func<string[], string> tryApply)
        {
            this.defaults = defaults;
            this.tryApply = tryApply;
            boxes = new HotkeyBox[labels.Length];

            SuspendLayout();
            Text = "StayFocus Notes - Shortcuts";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true; // notes are always-on-top, the dialog must not open behind them

            int y = 18;
            for (int i = 0; i < labels.Length; i++)
            {
                Controls.Add(MakeLabel(labels[i], 16, y + 4, SystemColors.ControlText));
                boxes[i] = new HotkeyBox();
                boxes[i].SetBounds(170, y, 194, 24);
                boxes[i].Text = current[i];
                Controls.Add(boxes[i]);
                y += 36;
            }

            Controls.Add(MakeLabel("Click a box, then press the keys (two of Ctrl / Alt / Shift + a key).", 16, y + 2, SystemColors.GrayText));
            y += 38;

            Button reset = MakeButton("Defaults", 16, y);
            reset.Click += delegate
            {
                for (int i = 0; i < boxes.Length; i++) boxes[i].Text = this.defaults[i];
            };
            Button save = MakeButton("Save", 204, y);
            save.Click += OnSave;
            Button cancel = MakeButton("Cancel", 288, y);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = save;
            CancelButton = cancel;

            ClientSize = new Size(380, y + 44);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Ui.StyleDialog(this);
            ResumeLayout(false);
            PerformLayout();
        }

        Label MakeLabel(string text, int x, int y, Color color)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.ForeColor = color;
            label.Location = new Point(x, y);
            return label;
        }

        Button MakeButton(string text, int x, int y)
        {
            Button button = new Button();
            button.Text = text;
            button.SetBounds(x, y, 76, 28);
            Controls.Add(button);
            return button;
        }

        void OnSave(object sender, EventArgs e)
        {
            string[] values = new string[boxes.Length];
            string error = null;
            for (int i = 0; i < boxes.Length; i++)
            {
                values[i] = boxes[i].Text;
                for (int j = 0; j < i; j++)
                    if (string.Equals(values[i], values[j], StringComparison.OrdinalIgnoreCase))
                        error = "Each shortcut must be different.";
            }
            if (error == null) error = tryApply(values);

            if (error == null)
            {
                DialogResult = DialogResult.OK;
                return;
            }
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
