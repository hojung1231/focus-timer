using System.Drawing;
using System.Drawing.Drawing2D;

namespace FocusTimer
{
    static class Theme
    {
        public static readonly Color FormBg = Color.FromArgb(32, 33, 36);
        public static readonly Color Border = Color.FromArgb(95, 99, 104);
        public static readonly Color BannerBg = Color.FromArgb(45, 46, 48);
        public static readonly Color BannerHover = Color.FromArgb(70, 72, 76);
        public static readonly Color CloseHover = Color.FromArgb(217, 48, 37);
        public static readonly Color BannerFg = Color.FromArgb(232, 234, 237);

        public static readonly Color PausedBg = Color.FromArgb(60, 64, 67);
        public static readonly Color PausedHover = Color.FromArgb(76, 80, 84);
        public static readonly Color PausedFg = Color.FromArgb(232, 234, 237);
        public static readonly Color SubFg = Color.FromArgb(189, 193, 198);
        public static readonly Color RunningBg = Color.FromArgb(30, 142, 62);
        public static readonly Color RunningFg = Color.White;
        public static readonly Color EditBg = Color.White;
        public static readonly Color EditBorder = Color.FromArgb(138, 180, 248);

        public static readonly Color ReturnBg = Color.FromArgb(227, 116, 0);
        public static readonly Color ReturnHover = Color.FromArgb(242, 135, 20);
        public static readonly Color DistractBg = Color.FromArgb(197, 34, 31);
        public static readonly Color DistractHover = Color.FromArgb(217, 48, 37);

        public static readonly Color UpdateBg = Color.FromArgb(26, 115, 232);
        public static readonly Color TimerBg = Color.FromArgb(23, 24, 26);
        public static readonly Color TimerPaused = Color.FromArgb(154, 160, 166);
        public static readonly Color TimerRunning = Color.FromArgb(129, 201, 149);
        public static readonly Color TimerAway = Color.FromArgb(253, 214, 99);
        public static readonly Color Hint = Color.FromArgb(128, 134, 139);

        /// <summary>창 크기 배율이 적용된 글꼴 (pt는 기본 크기 기준)</summary>
        public static System.Drawing.Font Font(string family, float pt, FontStyle style = FontStyle.Regular) =>
            new System.Drawing.Font(family, System.Math.Max(4f, pt * Settings.Scale), style);

        public static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, Rectangle r, int rad, Color c)
        {
            var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var path = Rounded(rr, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        public static string Fmt(System.TimeSpan t) =>
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

        public static string FmtShort(System.TimeSpan t) =>
            t.TotalHours >= 1 ? Fmt(t) : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
