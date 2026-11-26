using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StayFocus
{
    /// <summary>Shared look: DPI scaling, the note color palette and the light / dark theme.</summary>
    static class Ui
    {
        public static readonly float Scale = ReadScale();

        /// <summary>True while the dark theme is in use. Change it with SetDark.</summary>
        public static bool Dark { get; private set; }

        public static readonly string[] ColorNames = { "Yellow", "Pink", "Green", "Blue", "Orange", "Purple" };

        // Light theme: pastel body, stronger header. The header colors are also "the" color of a note
        // (menu swatches, the stripe on a card), whatever the theme.
        public static readonly Color[] BodyColors =
        {
            Color.FromArgb(0xFF, 0xF4, 0xAB),
            Color.FromArgb(0xFF, 0xDC, 0xE9),
            Color.FromArgb(0xDC, 0xF5, 0xC9),
            Color.FromArgb(0xD6, 0xEC, 0xFF),
            Color.FromArgb(0xFF, 0xE2, 0xBD),
            Color.FromArgb(0xE9, 0xDD, 0xFF),
        };

        public static readonly Color[] HeaderColors =
        {
            Color.FromArgb(0xFF, 0xE4, 0x66),
            Color.FromArgb(0xFF, 0xB8, 0xD2),
            Color.FromArgb(0xB7, 0xE8, 0x9C),
            Color.FromArgb(0xA9, 0xD7, 0xFF),
            Color.FromArgb(0xFF, 0xC7, 0x85),
            Color.FromArgb(0xD0, 0xB8, 0xFF),
        };

        // Dark theme: every note is dark gray with a hint of its color, under a muted colored bar.
        static readonly Color DarkBase = Color.FromArgb(0x26, 0x26, 0x28);

        public static readonly Color Alert = Color.FromArgb(0xE5, 0x48, 0x4D);

        // ----- note colors -----

        public static Color Body(int index)
        {
            index = ClampColor(index);
            return Dark ? Mix(DarkBase, HeaderColors[index], 0.10f) : BodyColors[index];
        }

        public static Color Header(int index)
        {
            index = ClampColor(index);
            return Dark ? Mix(DarkBase, HeaderColors[index], 0.40f) : HeaderColors[index];
        }

        /// <summary>Color of the toolbar icons on a note's header.</summary>
        public static Color Glyph(int index)
        {
            index = ClampColor(index);
            return Dark ? Mix(HeaderColors[index], Color.White, 0.55f) : Darken(HeaderColors[index], 0.62f);
        }

        /// <summary>Colored text on a note body (tags, reminder).</summary>
        public static Color Accent(int index)
        {
            index = ClampColor(index);
            return Dark ? HeaderColors[index] : Darken(HeaderColors[index], 0.55f);
        }

        public static Color Edge(int index)
        {
            index = ClampColor(index);
            return Dark ? Mix(DarkBase, HeaderColors[index], 0.55f) : Darken(HeaderColors[index], 0.2f);
        }

        public static Color CardEdge(int index, bool hover)
        {
            index = ClampColor(index);
            if (Dark) return hover ? HeaderColors[index] : Mix(DarkBase, HeaderColors[index], 0.35f);
            return Darken(HeaderColors[index], hover ? 0.45f : 0.15f);
        }

        /// <summary>A button background under the mouse.</summary>
        public static Color Hover(Color background)
        {
            return Dark ? Mix(background, Color.White, 0.14f) : Darken(background, 0.1f);
        }

        public static Color Text
        {
            get { return Dark ? Color.FromArgb(0xEC, 0xEC, 0xEC) : Color.FromArgb(0x2B, 0x2B, 0x2B); }
        }

        /// <summary>Gray text: done notes, previews, hints.</summary>
        public static Color Faint
        {
            get { return Dark ? Color.FromArgb(0x9C, 0x9C, 0x9C) : Color.FromArgb(0x70, 0x70, 0x70); }
        }

        // ----- window colors (board, search, dialogs) -----

        public static Color Window
        {
            get { return Dark ? Color.FromArgb(0x1E, 0x1E, 0x20) : Color.White; }
        }

        public static Color WindowText
        {
            get { return Dark ? Color.FromArgb(0xE6, 0xE6, 0xE6) : SystemColors.ControlText; }
        }

        public static Color Heading
        {
            get { return Dark ? Color.FromArgb(0xC8, 0xC8, 0xC8) : Color.FromArgb(0x44, 0x44, 0x44); }
        }

        /// <summary>Background of a board column.</summary>
        public static Color Panel
        {
            get { return Dark ? Color.FromArgb(0x2A, 0x2A, 0x2D) : Color.FromArgb(0xF1, 0xF2, 0xF4); }
        }

        /// <summary>Background of text boxes, lists and buttons.</summary>
        public static Color Field
        {
            get { return Dark ? Color.FromArgb(0x33, 0x33, 0x37) : SystemColors.Window; }
        }

        public static Color FieldEdge
        {
            get { return Dark ? Color.FromArgb(0x5A, 0x5A, 0x60) : Color.FromArgb(0xB4, 0xB4, 0xB4); }
        }

        public static Color Selection
        {
            get { return Dark ? Color.FromArgb(0x3A, 0x4B, 0x66) : Color.FromArgb(0xDD, 0xEA, 0xFB); }
        }

        // ----- theme -----

        /// <summary>What Windows is set to: Settings > Personalization > Colors > app mode.</summary>
        public static bool WindowsPrefersDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>Switches the palette and the look of every menu. Windows already open must then be restyled.</summary>
        public static void SetDark(bool dark)
        {
            Dark = dark;
            if (dark) ToolStripManager.Renderer = new DarkMenuRenderer();
            else ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional; // back to the standard look
        }

        /// <summary>
        /// Gives a window and its standard controls the colors of the current theme (title bar included).
        /// A label whose Tag is "faint" gets the gray hint color.
        /// </summary>
        public static void Style(Form form)
        {
            form.BackColor = Window;
            form.ForeColor = WindowText;
            StyleChildren(form);
            form.HandleCreated -= OnStyledHandleCreated; // the title bar needs the real window
            form.HandleCreated += OnStyledHandleCreated;
            ApplyTitleBar(form);
        }

        /// <summary>Dialogs keep the standard Windows look in the light theme and are only restyled in the dark one.</summary>
        public static void StyleDialog(Form form)
        {
            if (Dark) Style(form);
        }

        static void OnStyledHandleCreated(object sender, EventArgs e)
        {
            ApplyTitleBar((Form)sender);
        }

        static void StyleChildren(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                TextBox textBox = child as TextBox;
                ListBox listBox = child as ListBox;
                ComboBox comboBox = child as ComboBox;
                if (button != null)
                {
                    button.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.System;
                    if (Dark)
                    {
                        button.BackColor = Field;
                        button.ForeColor = WindowText;
                        button.FlatAppearance.BorderColor = FieldEdge;
                    }
                    else
                    {
                        button.ForeColor = SystemColors.ControlText;
                        button.UseVisualStyleBackColor = true;
                    }
                }
                else if (textBox != null)
                {
                    textBox.BorderStyle = Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    textBox.BackColor = Field;
                    textBox.ForeColor = WindowText;
                }
                else if (listBox != null)
                {
                    listBox.BorderStyle = Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                    listBox.BackColor = Field;
                    listBox.ForeColor = WindowText;
                    DarkScrollBars(listBox);
                }
                else if (comboBox != null)
                {
                    comboBox.BackColor = Field;
                    comboBox.ForeColor = WindowText;
                    DarkScrollBars(comboBox); // also switches the box itself to the dark Windows look
                }
                else if (child is Label)
                {
                    child.ForeColor = "faint".Equals(child.Tag) ? (Dark ? Faint : SystemColors.GrayText) : WindowText;
                }
                StyleChildren(child);
            }
        }

        /// <summary>Dark or light title bar (Windows 10 2004 and later; older versions ignore it).</summary>
        public static void ApplyTitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;
            int on = Dark ? 1 : 0;
            const int UseImmersiveDarkMode = 20, UseImmersiveDarkModeOld = 19;
            try
            {
                if (Native.DwmSetWindowAttribute(form.Handle, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                    Native.DwmSetWindowAttribute(form.Handle, UseImmersiveDarkModeOld, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { return; }
            catch (EntryPointNotFoundException) { return; }
            // Make Windows redraw the frame now instead of at the next activation.
            Native.SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE
                | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        /// <summary>Scroll bars that match the theme, for a control that scrolls.</summary>
        public static void DarkScrollBars(Control control)
        {
            control.HandleCreated -= OnScrollerHandleCreated;
            control.HandleCreated += OnScrollerHandleCreated;
            if (control.IsHandleCreated) ApplyScrollTheme(control);
        }

        static void OnScrollerHandleCreated(object sender, EventArgs e)
        {
            ApplyScrollTheme((Control)sender);
        }

        static void ApplyScrollTheme(Control control)
        {
            try
            {
                string dark = control is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer";
                Native.SetWindowTheme(control.Handle, Dark ? dark : "Explorer", null);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        // ----- helpers -----

        static float ReadScale()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        public static int S(int value)
        {
            return (int)Math.Round(value * Scale);
        }

        /// <summary>The app icon (embedded in the exe) at the size closest to the one asked for.</summary>
        public static Icon LoadIcon(Size size)
        {
            using (System.IO.Stream stream = typeof(Ui).Assembly.GetManifestResourceStream("app.ico"))
                return new Icon(stream, size);
        }

        /// <summary>Makes a window invisible (or visible again) to screenshots, recordings and screen sharing.</summary>
        public static void ApplyCapturePrivacy(Form form, bool hide)
        {
            if (!form.IsHandleCreated) return;
            if (!hide)
            {
                Native.SetWindowDisplayAffinity(form.Handle, Native.WDA_NONE);
                return;
            }
            // Windows 10 2004 and later leave the window out of the capture; older versions can only black it out.
            if (!Native.SetWindowDisplayAffinity(form.Handle, Native.WDA_EXCLUDEFROMCAPTURE))
                Native.SetWindowDisplayAffinity(form.Handle, Native.WDA_MONITOR);
        }

        public static int ClampColor(int index)
        {
            return index >= 0 && index < BodyColors.Length ? index : 0;
        }

        /// <summary>Mixes a color toward black; amount 0 leaves it unchanged, 1 gives black.</summary>
        public static Color Darken(Color c, float amount)
        {
            float keep = 1f - amount;
            return Color.FromArgb((int)(c.R * keep), (int)(c.G * keep), (int)(c.B * keep));
        }

        /// <summary>Blends two colors; amount 0 gives the first, 1 gives the second.</summary>
        public static Color Mix(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        public static Bitmap Swatch(int index)
        {
            int n = S(16);
            Bitmap bitmap = new Bitmap(n, n);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (SolidBrush fill = new SolidBrush(HeaderColors[index]))
            using (Pen edge = new Pen(Darken(HeaderColors[index], 0.25f)))
            {
                g.FillRectangle(fill, 1, 1, n - 3, n - 3);
                g.DrawRectangle(edge, 1, 1, n - 3, n - 3);
            }
            return bitmap;
        }
    }

    /// <summary>Colors of the menus (tray, note buttons, cards) in the dark theme.</summary>
    class DarkMenuColors : ProfessionalColorTable
    {
        static readonly Color Back = Color.FromArgb(0x2B, 0x2B, 0x2E);
        static readonly Color Line = Color.FromArgb(0x4A, 0x4A, 0x50);
        static readonly Color Lit = Color.FromArgb(0x40, 0x40, 0x46);

        public override Color ToolStripDropDownBackground { get { return Back; } }
        public override Color ImageMarginGradientBegin { get { return Back; } }
        public override Color ImageMarginGradientMiddle { get { return Back; } }
        public override Color ImageMarginGradientEnd { get { return Back; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Line; } }
        public override Color MenuItemSelected { get { return Lit; } }
        public override Color MenuItemSelectedGradientBegin { get { return Lit; } }
        public override Color MenuItemSelectedGradientEnd { get { return Lit; } }
        public override Color MenuItemPressedGradientBegin { get { return Lit; } }
        public override Color MenuItemPressedGradientMiddle { get { return Lit; } }
        public override Color MenuItemPressedGradientEnd { get { return Lit; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Line; } }
        public override Color CheckBackground { get { return Lit; } }
        public override Color CheckSelectedBackground { get { return Lit; } }
        public override Color CheckPressedBackground { get { return Lit; } }
        public override Color ButtonSelectedBorder { get { return Line; } }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        static readonly Color Light = Color.FromArgb(0xEC, 0xEC, 0xEC);
        static readonly Color Disabled = Color.FromArgb(0x8A, 0x8A, 0x8A);

        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Light : Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Light : Disabled;
            base.OnRenderArrow(e);
        }

        // The standard tick is dark on a light square; draw a light one instead.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle box = e.ImageRectangle;
            box.Inflate(2, 2);
            using (SolidBrush fill = new SolidBrush(ColorTable.CheckBackground)) e.Graphics.FillRectangle(fill, box);
            using (Pen edge = new Pen(ColorTable.MenuItemBorder)) e.Graphics.DrawRectangle(edge, box.X, box.Y, box.Width - 1, box.Height - 1);
            if (e.Item.Image != null) return; // the item's own picture is drawn on top of the square

            Rectangle r = e.ImageRectangle;
            SmoothingMode before = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen tick = new Pen(Light, Math.Max(1.6f, r.Width / 9f)))
                e.Graphics.DrawLines(tick, new PointF[]
                {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
                });
            e.Graphics.SmoothingMode = before;
        }
    }
}
