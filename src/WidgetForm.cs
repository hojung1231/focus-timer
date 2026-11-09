using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FocusTimer
{
    /// <summary>
    /// 공통 위젯 창: 테두리 없음, 항상 위, 상단 배너(홈/최소화/닫기), 드래그 이동.
    /// </summary>
    abstract class WidgetForm : Form
    {
        protected readonly float S;
        protected readonly int BannerH, Pad, Gap, BtnW, ColW, BoxH, TimerH;
        protected readonly Panel Banner;
        protected readonly ToolTip Tip = new ToolTip();
        readonly Label minBtn, closeBtn;
        readonly List<Label> leftItems = new List<Label>();

        public event Action HomeRequested;

        protected WidgetForm(bool showHome)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;                // 항상 위
            ShowInTaskbar = true;
            Text = "업무 집중 타이머";
            BackColor = Theme.FormBg;
            DoubleBuffered = true;
            Icon = AppIcon;

            using (var g = CreateGraphics()) S = g.DpiX / 96f;
            ColW = (int)Math.Round(2 * 96 * S);   // 가로 2인치
            BoxH = (int)Math.Round(1 * 96 * S);   // 세로 1인치
            TimerH = (int)(30 * S);
            BannerH = (int)(24 * S);
            Pad = (int)(5 * S);
            Gap = (int)(4 * S);
            BtnW = (int)(30 * S);

            Banner = new Panel { BackColor = Theme.BannerBg };
            Banner.MouseDown += DragWindow;
            Banner.DoubleClick += (o, e) => CenterTop(); // 배너 더블클릭 → 화면 상단 중앙
            Controls.Add(Banner);

            if (showHome)
            {
                var home = AddBannerItem("⌂", BtnW, "홈 (버전 선택)", true);
                home.Font = new Font("Segoe UI Symbol", 11f, FontStyle.Bold);
                home.Click += (o, e) => BeginInvoke((Action)(() => HomeRequested?.Invoke())); // 클릭 처리 후 전환
            }

            minBtn = MakeBannerButton("—", Theme.BannerHover, true);
            minBtn.Click += (o, e) => WindowState = FormWindowState.Minimized;
            closeBtn = MakeBannerButton("✕", Theme.CloseHover, true);
            closeBtn.Click += (o, e) => Close();
            Banner.Controls.Add(minBtn);
            Banner.Controls.Add(closeBtn);
            Tip.SetToolTip(minBtn, "최소화");
            Tip.SetToolTip(closeBtn, "닫기");
        }

        static Icon appIcon;
        static Icon AppIcon => appIcon ?? (appIcon = MakeIcon());

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000; // WS_MINIMIZEBOX: 작업표시줄 클릭으로 최소화/복원
                return cp;
            }
        }

        // ── 배너 ──────────────────────────────────────────
        Label MakeBannerButton(string text, Color hover, bool clickable)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.BannerFg,
                BackColor = Theme.BannerBg,
                Font = new Font("Malgun Gothic", 9f, FontStyle.Bold),
            };
            if (clickable)
            {
                l.Cursor = Cursors.Hand;
                l.MouseEnter += (o, e) => l.BackColor = hover;
                l.MouseLeave += (o, e) => l.BackColor = Theme.BannerBg;
            }
            else
            {
                l.MouseDown += DragWindow;
                l.DoubleClick += (o, e) => CenterTop();
            }
            return l;
        }

        /// <summary>배너 왼쪽에 버튼(또는 제목 글자)을 차례로 추가</summary>
        protected Label AddBannerItem(string text, int width, string tip, bool clickable)
        {
            var l = MakeBannerButton(text, Theme.BannerHover, clickable);
            l.Width = width;
            if (!clickable)
            {
                l.TextAlign = ContentAlignment.MiddleLeft;
                l.ForeColor = Theme.SubFg;
                l.Font = new Font("Malgun Gothic", 8.5f);
            }
            if (tip != null) Tip.SetToolTip(l, tip);
            leftItems.Add(l);
            Banner.Controls.Add(l);
            return l;
        }

        /// <summary>창 크기 지정 (배너 아래 내용 영역 크기)</summary>
        protected void SetContentSize(int width, int contentHeight)
        {
            ClientSize = new Size(width, BannerH + contentHeight);
            Banner.Bounds = new Rectangle(0, 0, width, BannerH);
            int x = 0;
            int rightLimit = width - 2 * BtnW;
            foreach (var l in leftItems)
            {
                int w = Math.Min(l.Width, Math.Max(0, rightLimit - x));
                l.Bounds = new Rectangle(x, 0, w, BannerH);
                x += w;
            }
            closeBtn.Bounds = new Rectangle(width - BtnW, 0, BtnW, BannerH);
            minBtn.Bounds = new Rectangle(width - 2 * BtnW, 0, BtnW, BannerH);
            Invalidate();
        }

        void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            try
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
            }
            catch (EntryPointNotFoundException) { } // Windows가 아닌 환경
            catch (DllNotFoundException) { }
        }

        // ── 위치 ──────────────────────────────────────────
        public void CenterTop()
        {
            var wa = Screen.FromControl(this).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top);
        }

        /// <summary>창 폭이 바뀌어도 가로 중심을 유지</summary>
        protected void KeepCenter(int centerX)
        {
            var wa = Screen.FromRectangle(Bounds).WorkingArea;
            int x = centerX - Width / 2;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            Location = new Point(x, Top);
        }

        /// <summary>이전 화면과 같은 자리(가로 중심, 높이)에 표시</summary>
        public void PlaceLike(Form other)
        {
            Location = new Point(other.Left + other.Width / 2 - Width / 2, other.Top);
            KeepCenter(other.Left + other.Width / 2);
        }

        protected int CenterX => Left + Width / 2;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(Theme.RunningBg)) g.FillEllipse(b, 2, 2, 28, 28);
                    using (var p = new Pen(Color.White, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(p, 16, 16, 16, 7);
                        g.DrawLine(p, 16, 16, 23, 19);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
