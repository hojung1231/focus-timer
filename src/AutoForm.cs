using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FocusTimer
{
    /// <summary>
    /// 버전 2: 지금 쓰는 창을 자동으로 감지해 작업 이름을 붙이고 시간을 잼.
    /// 딴짓(카카오톡, 유튜브 등) 중이면 버튼 하나로 하던 작업 창으로 복귀.
    /// </summary>
    class AutoForm : WidgetForm
    {
        public override int ModeId => 2;

        const int MaxBoxes = 3;            // 화면에 보여줄 작업 수
        const double MinShowSeconds = 5;   // 잠깐 스친 창은 목록에 안 보이게
        const double IdleLimit = InputIdle.Limit; // 30초 동안 입력이 없으면 자리 비움

        enum Mode { Starting, Work, Distract, Idle }

        readonly IWindowProbe probe;
        readonly DistractionRules rules = new DistractionRules();
        readonly List<WorkItem> items = new List<WorkItem>();
        readonly List<WorkBox> boxes = new List<WorkBox>();
        readonly StateBar bar;
        readonly Timer tick;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly int myPid = Process.GetCurrentProcess().Id;
        readonly int stripH;

        Mode mode = Mode.Starting, modeBeforeIdle = Mode.Starting;
        WorkItem current;                 // 마지막으로 하던 업무 (돌아갈 곳)
        string distractName, distractRule;
        double distractSec, totalWork, lastT;
        int reloadCounter;

        static readonly HashSet<string> NeutralClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "MultitaskingViewFrame",
            "TaskSwitcherWnd", "XamlExplorerHostIslandWindow", "ForegroundStaging", "NotifyIconOverflowWindow",
            "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "#32768",
        };
        static readonly HashSet<string> NeutralProcs = new HashSet<string>
        {
            "lockapp", "searchhost", "searchapp", "searchui", "startmenuexperiencehost",
            "shellexperiencehost", "textinputhost", "shellhost",
        };

        public AutoForm() : base(showHome: true)
        {
            AddBannerItem(" 자동", (int)(26 * S), null, false);
            stripH = (int)(15 * S);

            bool demo = Environment.GetEnvironmentVariable("FOCUSTIMER_DEMO") == "1" &&
                        Environment.OSVersion.Platform != PlatformID.Win32NT;
            probe = demo ? (IWindowProbe)new DemoProbe() : new Win32Probe();

            bar = new StateBar(S);
            bar.Click += (o, e) => { if (mode == Mode.Distract || mode == Mode.Idle) ReturnTo(current); };
            Tip.SetToolTip(bar, "딴짓 중이면 누르면 하던 작업으로 돌아가요 (우클릭: 메뉴)");
            bar.ContextMenuStrip = new ContextMenuStrip();
            bar.ContextMenuStrip.Opening += BarMenuOpening;
            Controls.Add(bar);

            EnsureBoxCount(1);
            UpdateUI();

            tick = new Timer { Interval = 1000 };
            tick.Tick += (o, e) => Tick();
            tick.Start();
        }

        // ── 감지 ──────────────────────────────────────────
        void Tick()
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - lastT;
            lastT = now;
            if (dt < 0 || dt > 5) dt = 0;   // 절전 복귀 등은 계산 안 함

            if (++reloadCounter >= 5) { reloadCounter = 0; rules.ReloadIfChanged(); }

            WindowInfo w = null;
            double idle = 0;
            try { w = probe.Foreground(); idle = probe.IdleSeconds(); } catch { }

            if (idle >= IdleLimit)
            {
                if (mode != Mode.Idle)
                {
                    // 움직임이 없던 시간은 이미 더해졌으므로 다시 뺌
                    if (mode == Mode.Work && current != null)
                    {
                        double d = Math.Min(idle, current.Seconds);
                        current.Seconds -= d;
                        totalWork = Math.Max(0, totalWork - d);
                    }
                    else if (mode == Mode.Distract)
                        distractSec = Math.Max(0, distractSec - idle);
                    modeBeforeIdle = mode;
                    mode = Mode.Idle;
                }
            }
            else
            {
                if (mode == Mode.Idle) mode = modeBeforeIdle;
                Observe(w, dt, now);
            }

            Prune();
            UpdateUI();
        }

        void Observe(WindowInfo w, double dt, double now)
        {
            if (w == null || w.Pid == myPid) return;                 // 이 위젯을 누른 경우 등
            if (NeutralClasses.Contains(w.Class) || NeutralProcs.Contains(w.Proc)) return;
            if (string.IsNullOrWhiteSpace(w.Title)) return;

            string rule = rules.Match(w);
            if (rule != null)
            {
                if (mode != Mode.Distract) distractSec = 0;
                mode = Mode.Distract;
                distractRule = rule;
                distractName = rule.StartsWith("proc:", StringComparison.OrdinalIgnoreCase)
                    ? Naming.AppName(w)
                    : KeywordAsShown(w.Title, rule.Substring(rule.IndexOf(':') + 1).Trim());
                distractSec += dt;
                return;
            }

            // 저장/열기 같은 대화상자는 지금 하던 작업 시간으로 계산
            if (w.Class == "#32770" && current != null && current.Proc == w.Proc)
            {
                current.Seconds += dt;
                current.LastActive = now;
                totalWork += dt;
                mode = Mode.Work;
                return;
            }

            string app = Naming.AppName(w);
            string name = Naming.DocName(w, app);
            string key = w.Proc + "|" + name;
            var item = items.FirstOrDefault(i => i.Key == key);
            if (item == null)
            {
                item = new WorkItem { Key = key, Name = name, App = app, Proc = w.Proc };
                items.Add(item);
            }
            item.Hwnd = w.Hwnd;
            item.Seconds += dt;
            item.LastActive = now;
            totalWork += dt;
            current = item;
            mode = Mode.Work;
            distractSec = 0;
        }

        void Prune()
        {
            items.RemoveAll(i => !probe.IsAlive(i.Hwnd));
            if (items.Count > 40)
            {
                var old = items.OrderBy(i => i.LastActive).Take(items.Count - 40).ToList();
                foreach (var o in old) items.Remove(o);
            }
            if (current != null && !items.Contains(current))
                current = items.OrderByDescending(i => i.LastActive).FirstOrDefault();
        }

        /// <summary>제목 속 키워드를 원래 표기 그대로 (예: "youtube" → "YouTube")</summary>
        static string KeywordAsShown(string title, string keyword)
        {
            int i = (title ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? title.Substring(i, keyword.Length) : keyword;
        }

        // ── 복귀 ──────────────────────────────────────────
        void ReturnTo(WorkItem it)
        {
            if (it == null) return;
            bool ok = false;
            try { ok = probe.Activate(it.Hwnd); } catch { }
            if (!ok)
            {
                items.Remove(it);
                if (current == it) current = null;
                Prune();
                UpdateUI();
                Tip.Show("창이 닫혀서 돌아갈 수 없어요", this, Pad, BannerH, 2000);
            }
        }

        // ── 화면 ──────────────────────────────────────────
        /// <summary>투자 시간이 많은 작업부터 왼쪽에 (지금 하던 작업은 항상 포함)</summary>
        List<WorkItem> VisibleItems()
        {
            var ranked = items
                .Where(i => i == current || i.Seconds >= MinShowSeconds)
                .OrderByDescending(i => i.Seconds)
                .ThenByDescending(i => i.LastActive)
                .ToList();
            var vis = ranked.Take(MaxBoxes).ToList();
            if (current != null && ranked.Contains(current) && !vis.Contains(current))
                vis[vis.Count - 1] = current;   // 돌아갈 작업이 밀려나지 않게
            return vis;
        }

        void EnsureBoxCount(int n)
        {
            if (n == boxes.Count) return;
            int cx = CenterX;
            bool placed = IsHandleCreated && Visible;
            while (boxes.Count < n)
            {
                var b = new WorkBox(S, ColW, BoxH, Gap, TimerH);
                b.Click += (o, e) => { if (b.Item != null) ReturnTo(b.Item); };
                b.ContextMenuStrip = new ContextMenuStrip();
                b.ContextMenuStrip.Opening += (o, e) => BoxMenuOpening(b, e);
                boxes.Add(b);
                Controls.Add(b);
            }
            while (boxes.Count > n)
            {
                var b = boxes[boxes.Count - 1];
                boxes.RemoveAt(boxes.Count - 1);
                Controls.Remove(b);
                b.Dispose();
            }

            int w = Pad + n * (ColW + Pad);
            SetContentSize(w, Pad + BoxH + Gap + TimerH + Pad + stripH + Pad);
            for (int i = 0; i < n; i++)
                boxes[i].Location = new Point(Pad + i * (ColW + Pad), BannerH + Pad);
            bar.Bounds = new Rectangle(Pad, BannerH + Pad + BoxH + Gap + TimerH + Pad, w - 2 * Pad, stripH);
            if (placed) KeepCenter(cx);
        }

        void UpdateUI()
        {
            var vis = VisibleItems();
            EnsureBoxCount(Math.Max(1, vis.Count));
            bool away = mode == Mode.Distract || mode == Mode.Idle;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                b.Item = i < vis.Count ? vis[i] : null;
                b.IsCurrent = b.Item != null && b.Item == current && mode == Mode.Work;
                b.IsReturnTarget = b.Item != null && b.Item == current && away;
                b.Invalidate();
            }

            switch (mode)
            {
                case Mode.Starting:
                    bar.Set("감지 중…", Theme.TimerBg, Theme.Hint, false);
                    break;
                case Mode.Work:
                    bar.Set("● 순업무 " + Theme.Fmt(TimeSpan.FromSeconds(totalWork)), Theme.TimerBg, Theme.TimerRunning, false);
                    break;
                case Mode.Distract:
                    string d = $"{distractName} {Theme.FmtShort(TimeSpan.FromSeconds(distractSec))}";
                    if (current != null) bar.Set("↩ " + d, Theme.DistractBg, Color.White, true);
                    else bar.Set("딴짓 " + d, Theme.DistractBg, Color.White, false);
                    break;
                case Mode.Idle:
                    if (current != null) bar.Set("자리 비움 · ↩", Theme.PausedBg, Theme.PausedFg, true);
                    else bar.Set("자리 비움", Theme.PausedBg, Theme.PausedFg, false);
                    break;
            }
        }

        // ── 우클릭 메뉴 ────────────────────────────────────
        void BoxMenuOpening(WorkBox b, System.ComponentModel.CancelEventArgs e)
        {
            var m = b.ContextMenuStrip;
            m.Items.Clear();
            var it = b.Item;
            if (it != null)
            {
                m.Items.Add($"'{it.App}'을(를) 딴짓으로 분류", null, (o, a) =>
                {
                    rules.AddProc(it.Proc);
                    items.RemoveAll(x => x.Proc == it.Proc);
                    Prune();
                    UpdateUI();
                });
                m.Items.Add("목록에서 지우기", null, (o, a) =>
                {
                    items.Remove(it);
                    Prune();
                    UpdateUI();
                });
                m.Items.Add(new ToolStripSeparator());
            }
            m.Items.Add("딴짓 규칙 편집…", null, (o, a) => OpenRulesFile());
        }

        void BarMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var m = bar.ContextMenuStrip;
            m.Items.Clear();
            if (mode == Mode.Distract && distractRule != null)
            {
                string r = distractRule;
                m.Items.Add($"이건 업무예요 (규칙 '{r}' 해제)", null, (o, a) => rules.Remove(r));
                m.Items.Add(new ToolStripSeparator());
            }
            m.Items.Add("순 업무 시간 초기화", null, (o, a) =>
            {
                totalWork = 0;
                foreach (var i in items) i.Seconds = 0;
                UpdateUI();
            });
            m.Items.Add("딴짓 규칙 편집…", null, (o, a) => OpenRulesFile());
        }

        void OpenRulesFile()
        {
            rules.Save();
            try { Process.Start("notepad.exe", "\"" + DistractionRules.FilePath + "\""); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            tick.Stop();
            base.OnFormClosing(e);
        }
    }

    /// <summary>자동 감지된 작업 박스 (누르면 그 창으로 이동)</summary>
    class WorkBox : Control
    {
        public WorkItem Item;
        public bool IsCurrent, IsReturnTarget;
        bool hover;

        readonly float s;
        readonly int ip;
        readonly Rectangle boxRect, timerRect;
        readonly Font nameFont, appFont, timerFont, hintFont;

        public WorkBox(float scale, int w, int boxH, int gap, int timerH)
        {
            s = scale;
            ip = (int)(4 * s);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FormBg;
            Size = new Size(w, boxH + gap + timerH);
            boxRect = new Rectangle(0, 0, w, boxH);
            timerRect = new Rectangle(0, boxH + gap, w, timerH);
            nameFont = new Font("Malgun Gothic", 7.5f, FontStyle.Bold);
            appFont = new Font("Malgun Gothic", 6f, FontStyle.Bold);
            timerFont = new Font("Consolas", 7.5f, FontStyle.Bold);
            hintFont = new Font("Malgun Gothic", 6.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var c = Item != null && boxRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (Cursor != c) Cursor = c;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int rad = (int)(4 * s);

            Color bg, fg, sub;
            if (Item == null) { bg = Theme.PausedBg; fg = Theme.Hint; sub = Theme.Hint; }
            else if (IsReturnTarget) { bg = hover ? Theme.ReturnHover : Theme.ReturnBg; fg = Color.White; sub = Color.FromArgb(255, 236, 214); }
            else if (IsCurrent) { bg = Theme.RunningBg; fg = Color.White; sub = Color.FromArgb(206, 234, 214); }
            else { bg = hover ? Theme.PausedHover : Theme.PausedBg; fg = Theme.PausedFg; sub = Theme.SubFg; }
            Theme.FillRounded(g, boxRect, rad, bg);

            var inner = Rectangle.Inflate(boxRect, -ip, -(int)(3 * s));
            if (Item == null)
            {
                TextRenderer.DrawText(g, "자동 감지 중…", hintFont, inner, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            else
            {
                int topH = (int)(11 * s);
                var top = new Rectangle(inner.X, inner.Y, inner.Width, topH);
                string topText = IsReturnTarget ? "↩ 돌아가기" : Item.App;
                TextRenderer.DrawText(g, topText, appFont, top, sub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                var nameRect = new Rectangle(inner.X, top.Bottom, inner.Width, inner.Bottom - top.Bottom);
                TextRenderer.DrawText(g, Item.Name, nameFont, nameRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            }

            Theme.FillRounded(g, timerRect, rad, Theme.TimerBg);
            var cflags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (Item == null)
                TextRenderer.DrawText(g, "", hintFont, timerRect, Theme.Hint, cflags);
            else
                TextRenderer.DrawText(g, (IsCurrent ? "● " : "") + Theme.Fmt(TimeSpan.FromSeconds(Item.Seconds)),
                    timerFont, timerRect, IsCurrent ? Theme.TimerRunning : Theme.TimerPaused, cflags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { nameFont.Dispose(); appFont.Dispose(); timerFont.Dispose(); hintFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>아래쪽 상태 줄 (딴짓 중이면 "돌아가기" 버튼이 됨)</summary>
    class StateBar : Control
    {
        readonly float s;
        readonly Font font;
        string text = "";
        Color bg = Theme.TimerBg, fg = Theme.Hint;
        bool clickable, hover;

        public StateBar(float scale)
        {
            s = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FormBg;
            font = new Font("Malgun Gothic", 6.5f, FontStyle.Bold);
        }

        public void Set(string t, Color b, Color f, bool canClick)
        {
            if (t == text && b == bg && f == fg && canClick == clickable) return;
            text = t; bg = b; fg = f; clickable = canClick;
            Cursor = clickable ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color b = bg;
            if (clickable && hover)
                b = bg == Theme.DistractBg ? Theme.DistractHover : Theme.PausedHover;
            Theme.FillRounded(g, ClientRectangle, (int)(4 * s), b);
            TextRenderer.DrawText(g, text, font, Rectangle.Inflate(ClientRectangle, -(int)(3 * s), 0), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) font.Dispose();
            base.Dispose(disposing);
        }
    }
}
