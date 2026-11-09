using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FocusTimer
{
    /// <summary>버전 1: 직접 입력하고 직접 시작·정지</summary>
    class ManualForm : WidgetForm
    {
        public override int ModeId => 1;

        readonly List<TaskBox> tasks = new List<TaskBox>();
        readonly Timer tick;
        int saveCounter;

        static readonly string SavePath = Path.Combine(Storage.Dir, "tasks.txt");

        public ManualForm() : base(showHome: true)
        {
            var add = AddBannerItem("+", BtnW, "오른쪽에 새 업무 추가", true);
            add.Click += (o, e) => AddTask(focus: true);

            // ↺ 초기화: 시간만 0으로 / 업무 모두 지우기
            var reset = AddBannerItem("↺", BtnW, "초기화", true);
            reset.Font = new Font("Segoe UI Symbol", 7.5f, FontStyle.Bold);
            var resetMenu = new ContextMenuStrip();
            resetMenu.Items.Add("모든 업무 시간만 0으로", null, (o, e) => ResetAllTimes());
            resetMenu.Items.Add("업무 모두 지우기", null, (o, e) => ClearAllTasks());
            reset.Click += (o, e) => resetMenu.Show(reset, new Point(0, BannerH));

            LoadTasks();
            if (tasks.Count == 0) AddTask(focus: false, keepCenter: false);
            LayoutAll();

            tick = new Timer { Interval = 250 };
            tick.Tick += (o, e) =>
            {
                CheckIdle();
                foreach (var t in tasks) if (t.Running) t.InvalidateTimer();
                if (++saveCounter >= 240) { saveCounter = 0; SaveTasks(); } // 1분마다 자동 저장
            };
            tick.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!tasks[0].HasStarted) tasks[0].BeginEdit();
        }

        /// <summary>30초 동안 움직임이 없으면 자동 정지, 다시 움직이면 자동 재개</summary>
        void CheckIdle()
        {
            double idle = InputIdle.Seconds();
            if (idle >= InputIdle.Limit)
            {
                foreach (var t in tasks) if (t.Running) t.IdlePause(idle);
            }
            else if (idle < 2)
            {
                var t = tasks.FirstOrDefault(x => x.AutoPaused);
                if (t != null) t.Resume();
            }
        }

        void ResetAllTimes()
        {
            if (MessageBox.Show(this, "모든 업무의 측정 시간을 0으로 만들까요?\n(업무 이름은 그대로)", "초기화",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            foreach (var t in tasks) t.ResetTime();
            SaveTasks();
        }

        void ClearAllTasks()
        {
            if (MessageBox.Show(this, "모든 업무와 시간을 지울까요?", "초기화",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int cx = CenterX;
            foreach (var t in tasks.Skip(1).ToList())
            {
                tasks.Remove(t);
                Controls.Remove(t);
                t.Dispose();
            }
            tasks[0].ClearAll();
            LayoutAll();
            KeepCenter(cx);
            SaveTasks();
        }

        TaskBox AddTask(bool focus, bool keepCenter = true)
        {
            int cx = CenterX;
            var t = new TaskBox(S, ColW, BoxH, Gap, TimerH);
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
                MessageBox.Show(this, $"{name}를 삭제할까요?\n측정 시간: {Theme.Fmt(t.Elapsed)}", "업무 삭제",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            if (tasks.Count == 1) { t.ClearAll(); return; }

            int cx = CenterX;
            tasks.Remove(t);
            Controls.Remove(t);
            t.Dispose();
            LayoutAll();
            KeepCenter(cx);
            SaveTasks();
        }

        void LayoutAll()
        {
            int n = Math.Max(1, tasks.Count);
            int w = Pad + n * (ColW + Pad);
            SetContentSize(w, Pad + BoxH + Gap + TimerH + Pad);
            for (int i = 0; i < tasks.Count; i++)
                tasks[i].Location = new Point(Pad + i * (ColW + Pad), BannerH + Pad);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            tick.Stop();
            SaveTasks();
            base.OnFormClosing(e);
        }

        // ── 저장/불러오기 (%APPDATA%\FocusTimer\tasks.txt) ─────────
        void SaveTasks()
        {
            try
            {
                Directory.CreateDirectory(Storage.Dir);
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
    }

    static class Storage
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusTimer");
    }

    /// <summary>업무 하나: 위쪽 2x1인치 업무 박스 + 아래쪽 시간 측정 박스</summary>
    class TaskBox : Control
    {
        public string TaskText = "";
        public bool HasStarted;   // Enter로 한 번이라도 시작했는지
        public bool Running;
        public bool Editing;
        public bool AutoPaused;   // 움직임이 없어서 자동으로 멈춘 상태

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
            ip = (int)(4 * s);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FormBg;
            Size = new Size(w, boxH + gap + timerH);
            BoxRect = new Rectangle(0, 0, w, boxH);
            TimerRect = new Rectangle(0, boxH + gap, w, timerH);

            textFont = new Font("Malgun Gothic", 7.5f, FontStyle.Bold);
            timerFont = new Font("Consolas", 7.5f, FontStyle.Bold);
            hintFont = new Font("Malgun Gothic", 6.5f);

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
                Bounds = new Rectangle(ip, ip + (int)(2 * s), w - 2 * ip, boxH - 2 * ip - (int)(2 * s)),
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
            AutoPaused = false;
            if (Running) return;
            Running = true;
            sw.Restart();
            RunStarted?.Invoke(this);
            Changed?.Invoke();
            Invalidate();
        }

        public void Pause()
        {
            AutoPaused = false;
            if (!Running) return;
            accTicks += sw.Elapsed.Ticks;
            sw.Reset();
            Running = false;
            Changed?.Invoke();
            Invalidate();
        }

        /// <summary>자동 정지: 움직임이 없던 시간은 빼고 멈춤</summary>
        public void IdlePause(double idleSeconds)
        {
            if (!Running) return;
            long run = sw.Elapsed.Ticks;
            long idleTicks = Math.Min(run, TimeSpan.FromSeconds(idleSeconds).Ticks);
            accTicks += run - idleTicks;
            sw.Reset();
            Running = false;
            AutoPaused = true;
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
            return Rectangle.Inflate(r, (int)(2 * s), (int)(1 * s));
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
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int rad = (int)(4 * s);

            // 업무 박스
            var box = new Rectangle(BoxRect.X, BoxRect.Y, BoxRect.Width - 1, BoxRect.Height - 1);
            Color bg = Editing ? Theme.EditBg : Running ? Theme.RunningBg : Theme.PausedBg;
            Color fg = Running ? Theme.RunningFg : Theme.PausedFg;
            using (var path = Theme.Rounded(box, rad))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (Editing)
                    using (var pen = new Pen(Theme.EditBorder, Math.Max(2f, 2 * s))) g.DrawPath(pen, path);
            }
            if (!Editing)
                TextRenderer.DrawText(g, TaskText, textFont, Rectangle.Inflate(BoxRect, -ip, -ip), fg, TextFlags);

            // 시간 측정 박스
            Theme.FillRounded(g, TimerRect, rad, Theme.TimerBg);
            var cflags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                         TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (!HasStarted)
            {
                TextRenderer.DrawText(g, "입력 후 Enter", hintFont, TimerRect, Theme.Hint, cflags);
            }
            else
            {
                string str = (Running ? "● " : AutoPaused ? "자리비움 " : "‖ ") + (AutoPaused ? Theme.FmtShort(Elapsed) : Theme.Fmt(Elapsed));
                Color c = Running ? Theme.TimerRunning : AutoPaused ? Theme.TimerAway : Theme.TimerPaused;
                TextRenderer.DrawText(g, str, timerFont, TimerRect, c, cflags);
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
}
