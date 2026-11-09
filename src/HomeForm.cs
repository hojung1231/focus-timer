using System;
using System.Drawing;
using System.Windows.Forms;

namespace FocusTimer
{
    /// <summary>홈 화면: 버전 선택</summary>
    class HomeForm : WidgetForm
    {
        public override int ModeId => 0;

        public event Action<int> ModeChosen;

        public HomeForm() : base(showHome: false)
        {
            AddBannerItem(" 업무 집중 타이머", (int)(80 * S), null, false);

            var c1 = new OptionCard(S, 1, "직접 기록", "업무를 입력하고\n직접 시작 · 정지");
            var c2 = new OptionCard(S, 2, "자동 감지", "하는 작업을 알아서 기록\n딴짓하면 버튼 하나로 복귀");
            Tip.SetToolTip(c1, "업무를 입력하고 직접 시작 · 정지");
            Tip.SetToolTip(c2, "하는 작업을 알아서 기록, 딴짓하면 버튼 하나로 복귀");
            c1.Click += (o, e) => BeginInvoke((Action)(() => ModeChosen?.Invoke(1)));
            c2.Click += (o, e) => BeginInvoke((Action)(() => ModeChosen?.Invoke(2)));

            int w = Pad + 2 * (ColW + Pad);
            c1.Bounds = new Rectangle(Pad, BannerH + Pad, ColW, BoxH);
            c2.Bounds = new Rectangle(Pad * 2 + ColW, BannerH + Pad, ColW, BoxH);
            Controls.Add(c1);
            Controls.Add(c2);
            SetContentSize(w, Pad + BoxH + Pad);
        }
    }

    class OptionCard : Control
    {
        readonly float s;
        readonly int number;
        readonly string title, desc;
        readonly Font numFont, titleFont, descFont;
        bool hover;

        public OptionCard(float scale, int number, string title, string desc)
        {
            s = scale;
            this.number = number;
            this.title = title;
            this.desc = desc;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FormBg;
            Cursor = Cursors.Hand;
            numFont = new Font("Consolas", 6f, FontStyle.Bold);
            titleFont = new Font("Malgun Gothic", 8.5f, FontStyle.Bold);
            descFont = new Font("Malgun Gothic", 8f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color bg = hover ? (number == 1 ? Theme.PausedHover : Color.FromArgb(36, 160, 74))
                             : (number == 1 ? Theme.PausedBg : Theme.RunningBg);
            Theme.FillRounded(g, ClientRectangle, (int)(4 * s), bg);

            int ip = (int)(5 * s);
            var r = Rectangle.Inflate(ClientRectangle, -ip, -ip);
            TextRenderer.DrawText(g, "VER " + number, numFont, r, Color.FromArgb(200, 255, 255, 255),
                TextFormatFlags.Left | TextFormatFlags.Top);

            var titleRect = new Rectangle(r.X, r.Y + (int)(10 * s), r.Width, r.Bottom - r.Y - (int)(10 * s));
            TextRenderer.DrawText(g, title, titleFont, titleRect, Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { numFont.Dispose(); titleFont.Dispose(); descFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
