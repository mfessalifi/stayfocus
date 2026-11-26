using System;
using System.Drawing;

namespace StayFocus
{
    /// <summary>Shared look: DPI scaling and the note color palette.</summary>
    static class Ui
    {
        public static readonly float Scale = ReadScale();

        public static readonly string[] ColorNames = { "Yellow", "Pink", "Green", "Blue", "Orange", "Purple" };

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

        public static readonly Color TextColor = Color.FromArgb(0x2B, 0x2B, 0x2B);

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
        public static void ApplyCapturePrivacy(System.Windows.Forms.Form form, bool hide)
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
}
