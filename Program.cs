using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FocusTimer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var mutex = new Mutex(true, "FocusTimer_SingleInstance_v1", out bool created))
            {
                if (!created) return; // 이미 실행 중이면 하나만
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    static class Theme
    {
        public static readonly Color FormBg = Color.FromArgb(32, 33, 36);
        public static readonly Color Border = Color.FromArgb(95, 99, 104);
        public static readonly Color BannerBg = Color.FromArgb(45, 46, 48);
        public static readonly Color BannerHover = Color.FromArgb(70, 72, 76);
        public static readonly Color CloseHover = Color.FromArgb(217, 48, 37);
        public static readonly Color BannerFg = Color.FromArgb(232, 234, 237);

        public static readonly Color PausedBg = Color.FromArgb(60, 64, 67);
        public static readonly Color PausedFg = Color.FromArgb(232, 234, 237);
        public static readonly Color RunningBg = Color.FromArgb(30, 142, 62);
        public static readonly Color RunningFg = Color.White;
        public static readonly Color EditBg = Color.White;
        public static readonly Color EditBorder = Color.FromArgb(138, 180, 248);

        public static readonly Color TimerBg = Color.FromArgb(23, 24, 26);
        public static readonly Color TimerPaused = Color.FromArgb(154, 160, 166);
        public static readonly Color TimerRunning = Color.FromArgb(129, 201, 149);
        public static readonly Color Hint = Color.FromArgb(128, 134, 139);
    }

    /// <summary>업무 하나: 위쪽 2x1인치 업무 박스 + 아래쪽 시간 측정 박스</summary>
    class TaskBox : Control
    {
        public string TaskText = "";
        public bool HasStarted;   // Enter로 한 번이라도 시작했는지
        public bool Running;
        public bool Editing;

        long accTicks;
        readonly Stopwatch sw = new Stopwatch();
        readonly TextBox editor;
        readonly float s;
        readonly int ip;
        readonly Font textFont, timerFont, hintFont;

        public Rectangle BoxRect, TimerRect;

        public event Action<TaskBox> RunStarted;
        public event Action<TaskBox> DeleteRequested;
        public event Action Changed;

        const TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
            TextFormatFlags.TextBoxControl;

        public TaskBox(float scale, int w, int boxH, int gap, int timerH)
        {
            s = scale;
            ip = (int)(8 * s);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FormBg;
            Size = new Size(w, boxH + gap + timerH);
            BoxRect = new Rectangle(0, 0, w, boxH);
            TimerRect = new Rectangle(0, boxH + gap, w, timerH);

            textFont = new Font("Malgun Gothic", 11f, FontStyle.Bold);
            timerFont = new Font("Consolas", 12f, FontStyle.Bold);
            hintFont = new Font("Malgun Gothic", 8.5f);

            editor = new TextBox
            {
                Multiline = true,
                WordWrap = true,
                AcceptsReturn = false,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.None,
                TextAlign = HorizontalAlignment.Center,
                Font = textFont,
                BackColor = Theme.EditBg,
                ForeColor = Color.Black,
                Bounds = new Rectangle(ip, ip + (int)(4 * s), w - 2 * ip, boxH - 2 * ip - (int)(4 * s)),
            };
            editor.KeyDown += Editor_KeyDown;
            editor.Leave += (o, e) =>
            {
                // 이미 시작한 업무를 수정하다가 다른 곳을 누르면 수정 내용 반영
                if (Editing && HasStarted) BeginInvoke((Action)(() => Commit()));
            };
            Controls.Add(editor);

            var menu = new ContextMenuStrip();
            menu.Items.Add("텍스트 수정", null, (o, e) => BeginEdit());
            menu.Items.Add("시간 초기화", null, (o, e) => ResetTime());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("업무 삭제", null, (o, e) => DeleteRequested?.Invoke(this));
            ContextMenuStrip = menu;

            ShowEditor(false);
        }

        public TimeSpan Elapsed => new TimeSpan(accTicks + (Running ? sw.Elapsed.Ticks : 0));

        public void LoadState(string text, long ticks)
        {
            TaskText = text;
            accTicks = ticks;
            HasStarted = true;
            Running = false;
            Editing = false;
            editor.Visible = false;
            Invalidate();
        }

        // ── 편집 ──────────────────────────────────────────
        void ShowEditor(bool focus)
        {
            Editing = true;
            editor.Text = TaskText;
            editor.Visible = true;
            if (focus)
            {
                editor.Focus();
                editor.SelectionStart = editor.TextLength;
            }
            Invalidate();
        }

        public void BeginEdit() => ShowEditor(true);

        void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Commit();
            }
            else if (e.KeyCode == Keys.Escape && HasStarted)
            {
                e.SuppressKeyPress = true;
                editor.Text = TaskText; // 수정 취소
                Commit();
            }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;
                editor.SelectAll();
            }
        }

        void Commit()
        {
            if (!Editing) return;
            string text = editor.Text.Replace("\r", " ").Replace("\n", " ").Trim();

            if (!HasStarted)
            {
                if (text.Length == 0) return;      // 빈칸이면 대기
                TaskText = text;
                HasStarted = true;
                CloseEditor();
                Resume();                           // 처음 Enter → 시간 측정 시작
            }
            else
            {
                if (text.Length > 0) TaskText = text;
                CloseEditor();                      // 수정만, 시간 상태는 그대로
                Changed?.Invoke();
            }
        }

        void CloseEditor()
        {
            Editing = false;
            editor.Visible = false;
            var f = FindForm();
            if (f != null) f.ActiveControl = null;
            Invalidate();
        }

        // ── 시간 ──────────────────────────────────────────
        public void Toggle()
        {
            if (Running) Pause(); else Resume();
        }

        public void Resume()
        {
            if (Running) return;
            Running = true;
            sw.Restart();
            RunStarted?.Invoke(this);
            Changed?.Invoke();
            Invalidate();
        }

        public void Pause()
        {
            if (!Running) return;
            accTicks += sw.Elapsed.Ticks;
            sw.Reset();
            Running = false;
            Changed?.Invoke();
            Invalidate();
        }

        public void ResetTime()
        {
            accTicks = 0;
            if (Running) sw.Restart();
            Changed?.Invoke();
            Invalidate();
        }

        public void ClearAll()
        {
            Pause();
            accTicks = 0;
            TaskText = "";
            HasStarted = false;
            ShowEditor(true);
            Changed?.Invoke();
        }

        public void InvalidateTimer() => Invalidate(TimerRect);

        // ── 마우스 ─────────────────────────────────────────
        Rectangle TextHitRect()
        {
            if (string.IsNullOrEmpty(TaskText)) return Rectangle.Empty;
            var inner = Rectangle.Inflate(BoxRect, -ip, -ip);
            var sz = TextRenderer.MeasureText(TaskText, textFont, inner.Size, TextFlags);
            int w = Math.Min(sz.Width, inner.Width);
            int h = Math.Min(sz.Height, inner.Height);
            var r = new Rectangle(inner.X + (inner.Width - w) / 2, inner.Y + (inner.Height - h) / 2, w, h);
            return Rectangle.Inflate(r, (int)(3 * s), (int)(2 * s));
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || Editing || !BoxRect.Contains(e.Location)) return;

            if (!HasStarted) { BeginEdit(); return; }

            if (TextHitRect().Contains(e.Location))
                BeginEdit();          // 글자 부분 클릭 → 텍스트 수정
            else
            {
                var f = FindForm();   // 다른 박스에서 수정 중이던 내용 확정
                if (f != null) f.ActiveControl = null;
                Toggle();             // 나머지 부분 클릭 → 시작/정지
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor c = Cursors.Default;
            if (!Editing && BoxRect.Contains(e.Location))
                c = HasStarted && !TextHitRect().Contains(e.Location) ? Cursors.Hand : Cursors.IBeam;
            if (Cursor != c) Cursor = c;
        }

        // ── 그리기 ─────────────────────────────────────────
        static GraphicsPath Rounded(Rectangle r, int rad)
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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int rad = (int)(6 * s);

            // 업무 박스
            var box = new Rectangle(BoxRect.X, BoxRect.Y, BoxRect.Width - 1, BoxRect.Height - 1);
            Color bg = Editing ? Theme.EditBg : Running ? Theme.RunningBg : Theme.PausedBg;
            Color fg = Running ? Theme.RunningFg : Theme.PausedFg;
            using (var path = Rounded(box, rad))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (Editing)
                    using (var pen = new Pen(Theme.EditBorder, Math.Max(2f, 2 * s))) g.DrawPath(pen, path);
            }
            if (!Editing)
                TextRenderer.DrawText(g, TaskText, textFont, Rectangle.Inflate(BoxRect, -ip, -ip), fg, TextFlags);

            // 시간 측정 박스
            var tr = new Rectangle(TimerRect.X, TimerRect.Y, TimerRect.Width - 1, TimerRect.Height - 1);
            using (var path = Rounded(tr, rad))
            using (var b = new SolidBrush(Theme.TimerBg))
                g.FillPath(b, path);

            var cflags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                         TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (!HasStarted)
            {
                TextRenderer.DrawText(g, "업무 입력 후 Enter", hintFont, TimerRect, Theme.Hint, cflags);
            }
            else
            {
                var t = Elapsed;
                string str = (Running ? "● " : "‖ ") +
                             $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
                TextRenderer.DrawText(g, str, timerFont, TimerRect,
                    Running ? Theme.TimerRunning : Theme.TimerPaused, cflags);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                textFont.Dispose();
                timerFont.Dispose();
                hintFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    class MainForm : Form
    {
        readonly float s;
        readonly int colW, boxH, timerH, bannerH, pad, gap, btnW, addW;
        readonly Panel banner;
        readonly Label addBtn, minBtn, closeBtn;
        readonly List<TaskBox> tasks = new List<TaskBox>();
        readonly System.Windows.Forms.Timer tick;
        int saveCounter;

        static readonly string SaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusTimer");
        static readonly string SavePath = Path.Combine(SaveDir, "tasks.txt");

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;                // 항상 위
            ShowInTaskbar = true;
            Text = "업무 집중 타이머";
            BackColor = Theme.FormBg;
            DoubleBuffered = true;
            Icon = MakeIcon();

            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            colW = (int)Math.Round(2 * 96 * s);   // 가로 2인치
            boxH = (int)Math.Round(1 * 96 * s);   // 세로 1인치
            timerH = (int)(30 * s);
            bannerH = (int)(24 * s);
            pad = (int)(5 * s);
            gap = (int)(4 * s);
            btnW = (int)(30 * s);
            addW = (int)(78 * s);

            banner = new Panel { BackColor = Theme.BannerBg };
            banner.MouseDown += DragWindow;
            banner.DoubleClick += (o, e) => CenterTop(); // 배너 더블클릭 → 화면 상단 중앙으로
            Controls.Add(banner);

            addBtn = MakeBannerButton("+ 새 업무", Theme.BannerHover);
            addBtn.Click += (o, e) => AddTask(focus: true);
            minBtn = MakeBannerButton("—", Theme.BannerHover);
            minBtn.Click += (o, e) => WindowState = FormWindowState.Minimized;
            closeBtn = MakeBannerButton("✕", Theme.CloseHover);
            closeBtn.Click += (o, e) => Close();
            banner.Controls.AddRange(new Control[] { addBtn, minBtn, closeBtn });

            var tip = new ToolTip();
            tip.SetToolTip(addBtn, "오른쪽에 새 업무 추가");
            tip.SetToolTip(minBtn, "최소화");
            tip.SetToolTip(closeBtn, "닫기 (기록은 저장됨)");

            LoadTasks();
            if (tasks.Count == 0) AddTask(focus: false, keepCenter: false);
            LayoutAll();
            CenterTop();

            tick = new System.Windows.Forms.Timer { Interval = 250 };
            tick.Tick += (o, e) =>
            {
                foreach (var t in tasks) if (t.Running) t.InvalidateTimer();
                if (++saveCounter >= 240) { saveCounter = 0; SaveTasks(); } // 1분마다 자동 저장
            };
            tick.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000; // WS_MINIMIZEBOX: 작업표시줄 클릭으로 최소화/복원
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var first = tasks[0];
            if (!first.HasStarted) first.BeginEdit();
        }

        Label MakeBannerButton(string text, Color hover)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.BannerFg,
                BackColor = Theme.BannerBg,
                Font = new Font("Malgun Gothic", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            l.MouseEnter += (o, e) => l.BackColor = hover;
            l.MouseLeave += (o, e) => l.BackColor = Theme.BannerBg;
            return l;
        }

        void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
        }

        void CenterTop()
        {
            var wa = Screen.FromControl(this).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top);
        }

        TaskBox AddTask(bool focus, bool keepCenter = true)
        {
            int cx = Left + Width / 2;
            var t = new TaskBox(s, colW, boxH, gap, timerH);
            t.RunStarted += OnRunStarted;
            t.DeleteRequested += OnDeleteRequested;
            t.Changed += SaveTasks;
            tasks.Add(t);
            Controls.Add(t);
            LayoutAll();
            if (keepCenter) KeepCenter(cx);
            if (focus) t.BeginEdit();
            return t;
        }

        void KeepCenter(int cx)
        {
            var wa = Screen.FromControl(this).WorkingArea;
            int x = cx - Width / 2;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            Location = new Point(x, Top);
        }

        void OnRunStarted(TaskBox started)
        {
            // 순 업무시간: 한 번에 하나의 업무만 측정
            foreach (var t in tasks)
                if (t != started && t.Running) t.Pause();
        }

        void OnDeleteRequested(TaskBox t)
        {
            string name = string.IsNullOrEmpty(t.TaskText) ? "이 업무" : $"'{t.TaskText}'";
            if (t.HasStarted &&
                MessageBox.Show(this, $"{name}를 삭제할까요?\n측정 시간: {Fmt(t.Elapsed)}", "업무 삭제",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            if (tasks.Count == 1) { t.ClearAll(); return; }

            int cx = Left + Width / 2;
            tasks.Remove(t);
            Controls.Remove(t);
            t.Dispose();
            LayoutAll();
            KeepCenter(cx);
            SaveTasks();
        }

        static string Fmt(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

        void LayoutAll()
        {
            int n = Math.Max(1, tasks.Count);
            int w = pad + n * (colW + pad);
            int h = bannerH + pad + boxH + gap + timerH + pad;
            ClientSize = new Size(w, h);

            banner.Bounds = new Rectangle(0, 0, w, bannerH);
            addBtn.Bounds = new Rectangle(0, 0, addW, bannerH);
            closeBtn.Bounds = new Rectangle(w - btnW, 0, btnW, bannerH);
            minBtn.Bounds = new Rectangle(w - 2 * btnW, 0, btnW, bannerH);

            for (int i = 0; i < tasks.Count; i++)
                tasks[i].Location = new Point(pad + i * (colW + pad), bannerH + pad);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveTasks();
            base.OnFormClosing(e);
        }

        // ── 저장/불러오기 (%APPDATA%\FocusTimer\tasks.txt) ─────────
        void SaveTasks()
        {
            try
            {
                Directory.CreateDirectory(SaveDir);
                var sb = new StringBuilder();
                foreach (var t in tasks)
                {
                    if (!t.HasStarted) continue;
                    sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(t.TaskText)))
                      .Append('\t').Append(t.Elapsed.Ticks).Append("\r\n");
                }
                File.WriteAllText(SavePath, sb.ToString());
            }
            catch { /* 저장 실패는 무시 */ }
        }

        void LoadTasks()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                foreach (var line in File.ReadAllLines(SavePath))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 2 || !long.TryParse(parts[1], out long ticks)) continue;
                    string text = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                    var t = AddTask(focus: false, keepCenter: false);
                    t.LoadState(text, ticks);
                }
            }
            catch { /* 손상된 파일이면 새로 시작 */ }
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
