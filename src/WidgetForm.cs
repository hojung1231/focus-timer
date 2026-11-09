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
        readonly Label minBtn, closeBtn, gearBtn;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Timer hoverTimer;
        readonly List<Label> leftItems = new List<Label>();
        int lastWidth, lastContentH;
        static bool updateAnnounced;

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
            BtnW = (int)(16 * S);

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

            // ⚙ 옵션 버튼 (배너 우클릭과 같은 메뉴)
            gearBtn = MakeBannerButton("⚙", Theme.BannerHover, true);
            gearBtn.Font = new Font("Segoe UI Symbol", 7f);
            gearBtn.Click += (o, e) => menu.Show(gearBtn, new Point(0, BannerH));
            gearBtn.MouseLeave += (o, e) => PaintGear();
            Banner.Controls.Add(gearBtn);
            Tip.SetToolTip(gearBtn, "옵션 · 업데이트");

            menu.Opening += (o, e) => BuildMenu();
            Banner.ContextMenuStrip = menu;

            Updater.Changed += RefreshUpdateButton;
            Settings.Changed += ApplyOpacity;

            // 마우스를 올리면 선명하게
            hoverTimer = new Timer { Interval = 150 };
            hoverTimer.Tick += (o, e) => ApplyOpacity();
            hoverTimer.Start();
        }

        // ── 옵션 메뉴 ─────────────────────────────────────
        void BuildMenu()
        {
            menu.Items.Clear();

            var trans = new ToolStripMenuItem($"투명도 ({Settings.Transparency}%)");
            foreach (int v in Settings.TransparencyChoices)
            {
                int val = v;
                trans.DropDownItems.Add(new ToolStripMenuItem(v == 0 ? "0% (불투명)" : v + "%", null,
                    (o, e) => Settings.SetTransparency(val)) { Checked = Settings.Transparency == v });
            }
            menu.Items.Add(trans);

            var count = new ToolStripMenuItem($"표시할 작업 수 ({Settings.MaxItems}개)");
            foreach (int v in Settings.MaxItemsChoices)
            {
                int val = v;
                count.DropDownItems.Add(new ToolStripMenuItem(v + "개", null,
                    (o, e) => Settings.SetMaxItems(val)) { Checked = Settings.MaxItems == v });
            }
            count.ToolTipText = "자동 감지에서 많이 한 순서대로 몇 개까지 보여줄지";
            menu.Items.Add(count);

            menu.Items.Add(new ToolStripMenuItem("마우스를 올리면 선명하게", null,
                (o, e) => Settings.SetHoverOpaque(!Settings.HoverOpaque)) { Checked = Settings.HoverOpaque });

            menu.Items.Add(new ToolStripSeparator());
            if (Updater.Available)
            {
                var up = new ToolStripMenuItem($"⬆ 새 버전 v{Updater.Text(Updater.Latest)}로 업데이트", null,
                    (o, e) => UpdateRequested?.Invoke());
                up.Font = new Font(up.Font, FontStyle.Bold);
                up.ForeColor = Theme.UpdateBg;
                menu.Items.Insert(0, up);
                menu.Items.Insert(1, new ToolStripSeparator());
            }
            else
            {
                menu.Items.Add("업데이트 확인", null, async (o, e) =>
                {
                    bool ok = await Updater.CheckAsync();
                    if (!ok) Toast("업데이트 정보를 가져오지 못했어요");
                    else if (!Updater.Available) Toast($"최신 버전이에요 (v{Updater.Text(Updater.Current)})");
                });
            }
            menu.Items.Add("다운로드 페이지 열기", null, (o, e) => Updater.OpenPage());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem($"버전 v{Updater.Text(Updater.Current)}") { Enabled = false });
        }

        void ApplyOpacity()
        {
            if (IsDisposed) return;
            double target = Settings.Opacity;
            if (Settings.HoverOpaque && (Bounds.Contains(Cursor.Position) || menu.Visible))
                target = 1.0;
            if (Math.Abs(Opacity - target) > 0.001) Opacity = target;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshUpdateButton();
            ApplyOpacity();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Updater.Changed -= RefreshUpdateButton;
                Settings.Changed -= ApplyOpacity;
                hoverTimer.Stop();
                hoverTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>잠깐 보였다 사라지는 안내 문구</summary>
        public void Toast(string text)
        {
            if (IsDisposed) return;
            Tip.Show(text, this, Pad, BannerH + Pad, 2500);
        }

        /// <summary>업데이트 받는 중 진행률 안내 (null이면 끝)</summary>
        public void SetUpdateProgress(string text)
        {
            if (text != null) Toast("업데이트 받는 중… " + text);
            else Tip.Hide(this);
        }

        /// <summary>새 버전이 있으면 ⚙ 버튼을 파랗게 + 한 번 안내</summary>
        void RefreshUpdateButton()
        {
            if (IsDisposed) return;
            PaintGear();
            Tip.SetToolTip(gearBtn, Updater.Available
                ? $"새 버전 v{Updater.Text(Updater.Latest)}이 있어요 — 눌러서 업데이트"
                : "옵션 · 업데이트");
            if (Updater.Available && !updateAnnounced && Visible)
            {
                updateAnnounced = true;
                Toast("새 버전이 있어요 · ⚙ 눌러서 업데이트");
            }
        }

        void PaintGear()
        {
            gearBtn.BackColor = Updater.Available ? Theme.UpdateBg : Theme.BannerBg;
            gearBtn.ForeColor = Updater.Available ? Color.White : Theme.BannerFg;
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

            int rightLimit = width - 3 * BtnW;
            int x = 0;
            foreach (var l in leftItems)
            {
                int w = Math.Min(l.Width, Math.Max(0, rightLimit - x));
                l.Bounds = new Rectangle(x, 0, w, BannerH);
                x += w;
            }
            closeBtn.Bounds = new Rectangle(width - BtnW, 0, BtnW, BannerH);
            minBtn.Bounds = new Rectangle(width - 2 * BtnW, 0, BtnW, BannerH);
            gearBtn.Bounds = new Rectangle(width - 3 * BtnW, 0, BtnW, BannerH);
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
