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
        readonly Label minBtn, closeBtn, updateBtn;
        readonly List<Label> leftItems = new List<Label>();
        int lastWidth, lastContentH;
        string updateProgress;   // 업데이트 받는 중이면 진행률 글자

        public event Action HomeRequested;
        public event Action UpdateRequested;

        /// <summary>0 = 홈, 1 = 직접 기록, 2 = 자동 감지 (업데이트 후 같은 화면으로 재시작)</summary>
        public abstract int ModeId { get; }

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
            ColW = (int)Math.Round(1 * 96 * S);   // 가로 1인치
            BoxH = (int)Math.Round(0.5 * 96 * S); // 세로 0.5인치
            TimerH = (int)(14 * S);
            BannerH = (int)(15 * S);
            Pad = Math.Max(1, (int)(2 * S));
            Gap = Math.Max(1, (int)(1 * S));
            BtnW = (int)(17 * S);

            Banner = new Panel { BackColor = Theme.BannerBg };
            Banner.MouseDown += DragWindow;
            Banner.DoubleClick += (o, e) => CenterTop(); // 배너 더블클릭 → 화면 상단 중앙
            Controls.Add(Banner);

            if (showHome)
            {
                var home = AddBannerItem("⌂", BtnW, "홈 (버전 선택)", true);
                home.Font = new Font("Segoe UI Symbol", 7f, FontStyle.Bold);
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

            // 새 버전이 있을 때만 보이는 업데이트 버튼
            updateBtn = MakeBannerButton("⬆", Color.FromArgb(66, 133, 244), true);
            updateBtn.BackColor = Theme.UpdateBg;
            updateBtn.ForeColor = Color.White;
            updateBtn.MouseLeave += (o, e) => updateBtn.BackColor = Theme.UpdateBg;
            updateBtn.Visible = false;
            updateBtn.Click += (o, e) => { if (updateProgress == null) UpdateRequested?.Invoke(); };
            Banner.Controls.Add(updateBtn);

            // 배너 우클릭: 업데이트 확인 / 버전 정보
            var menu = new ContextMenuStrip();
            menu.Items.Add("업데이트 확인", null, async (o, e) =>
            {
                bool ok = await Updater.CheckAsync();
                if (!ok) Toast("업데이트 정보를 가져오지 못했어요");
                else if (!Updater.Available) Toast($"최신 버전이에요 (v{Updater.Text(Updater.Current)})");
            });
            menu.Items.Add("다운로드 페이지 열기", null, (o, e) => Updater.OpenPage());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem($"버전 v{Updater.Text(Updater.Current)}") { Enabled = false });
            Banner.ContextMenuStrip = menu;

            Updater.Changed += RefreshUpdateButton;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshUpdateButton();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Updater.Changed -= RefreshUpdateButton;
            base.Dispose(disposing);
        }

        /// <summary>잠깐 보였다 사라지는 안내 문구</summary>
        public void Toast(string text)
        {
            if (IsDisposed) return;
            Tip.Show(text, this, Pad, BannerH + Pad, 2500);
        }

        public void SetUpdateProgress(string text)
        {
            updateProgress = text;
            RefreshUpdateButton();
        }

        void RefreshUpdateButton()
        {
            if (IsDisposed) return;
            bool show = Updater.Available || updateProgress != null;
            updateBtn.Visible = show;
            if (show)
                Tip.SetToolTip(updateBtn, $"새 버전 v{Updater.Text(Updater.Latest)} 업데이트 (현재 v{Updater.Text(Updater.Current)})");
            if (lastWidth > 0) SetContentSize(lastWidth, lastContentH);
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
                Font = new Font("Malgun Gothic", 6.5f, FontStyle.Bold),
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
                l.MouseUp += (o, e) =>
                {
                    if (e.Button == MouseButtons.Right && Banner.ContextMenuStrip != null)
                        Banner.ContextMenuStrip.Show(l, e.Location);
                };
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
                l.Font = new Font("Malgun Gothic", 6.5f);
            }
            if (tip != null) Tip.SetToolTip(l, tip);
            leftItems.Add(l);
            Banner.Controls.Add(l);
            return l;
        }

        /// <summary>창 크기 지정 (배너 아래 내용 영역 크기)</summary>
        protected void SetContentSize(int width, int contentHeight)
        {
            lastWidth = width;
            lastContentH = contentHeight;
            ClientSize = new Size(width, BannerH + contentHeight);
            Banner.Bounds = new Rectangle(0, 0, width, BannerH);

            int leftTotal = 0;
            foreach (var l in leftItems) leftTotal += l.Width;
            int rightLimit = width - 2 * BtnW;
            if (updateBtn.Visible)
            {
                // 자리가 있으면 "⬆ 업데이트", 좁으면 "⬆"만
                int wide = (int)(50 * S);
                bool roomy = width - 2 * BtnW - leftTotal >= wide;
                int uw = roomy ? wide : BtnW;
                updateBtn.Text = updateProgress ?? (roomy ? "⬆ 업데이트" : "⬆");
                if (updateProgress != null) uw = Math.Max(uw, (int)(36 * S));
                updateBtn.Bounds = new Rectangle(rightLimit - uw, 0, uw, BannerH);
                rightLimit -= uw;
            }
            int x = 0;
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
