using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FocusTimer
{
    class WindowInfo
    {
        public IntPtr Hwnd;
        public int Pid;
        public string Proc = "";     // 소문자 프로세스 이름 (확장자 없음)
        public string Title = "";
        public string Class = "";
        public string ExePath;
    }

    /// <summary>자동 감지된 작업 하나 (창 하나 = 문서/페이지 하나)</summary>
    class WorkItem
    {
        public string Key, Name, App, Proc;
        public IntPtr Hwnd;
        public double Seconds;
        public double LastActive;
    }

    interface IWindowProbe
    {
        WindowInfo Foreground();
        double IdleSeconds();
        bool IsAlive(IntPtr hwnd);
        bool Activate(IntPtr hwnd);
    }

    /// <summary>Windows에서 현재 맨 앞 창 정보를 읽음</summary>
    class Win32Probe : IWindowProbe
    {
        readonly Dictionary<uint, Tuple<string, string>> procCache = new Dictionary<uint, Tuple<string, string>>();

        public WindowInfo Foreground()
        {
            IntPtr h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            Native.GetWindowThreadProcessId(h, out uint pid);

            int len = Math.Max(0, Native.GetWindowTextLength(h));
            var title = new StringBuilder(len + 2);
            Native.GetWindowText(h, title, title.Capacity);
            var cls = new StringBuilder(256);
            Native.GetClassName(h, cls, cls.Capacity);

            var p = ProcInfo(pid);
            return new WindowInfo
            {
                Hwnd = h,
                Pid = (int)pid,
                Proc = p.Item1,
                ExePath = p.Item2,
                Title = title.ToString(),
                Class = cls.ToString(),
            };
        }

        Tuple<string, string> ProcInfo(uint pid)
        {
            if (procCache.TryGetValue(pid, out var cached)) return cached;
            string name = "", path = null;
            try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName; } catch { }
            IntPtr hp = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (Native.QueryFullProcessImageName(hp, 0, sb, ref size)) path = sb.ToString();
                }
                finally { Native.CloseHandle(hp); }
            }
            if (name.Length == 0 && path != null) name = Path.GetFileNameWithoutExtension(path);
            if (procCache.Count > 300) procCache.Clear();
            var r = Tuple.Create(name.ToLowerInvariant(), path);
            procCache[pid] = r;
            return r;
        }

        public double IdleSeconds()
        {
            var lii = new Native.LASTINPUTINFO { cbSize = 8 };
            if (!Native.GetLastInputInfo(ref lii)) return 0;
            uint idleMs = unchecked((uint)Environment.TickCount - lii.dwTime);
            return idleMs / 1000.0;
        }

        public bool IsAlive(IntPtr hwnd) => Native.IsWindow(hwnd);

        public bool Activate(IntPtr hwnd)
        {
            if (!Native.IsWindow(hwnd)) return false;
            if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
            if (!Native.SetForegroundWindow(hwnd))
            {
                // 포커스 잠금 우회: Alt 키를 한 번 눌렀다 떼고 다시 시도
                Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
                Native.SetForegroundWindow(hwnd);
            }
            return true;
        }
    }

    /// <summary>
    /// 화면 테스트용 가상 창 흐름 (환경변수 FOCUSTIMER_DEMO=1 이고 Windows가 아닐 때만 사용)
    /// </summary>
    class DemoProbe : IWindowProbe
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        WindowInfo forced;
        int forcedStep = -1;

        static readonly (double at, WindowInfo w)[] Script =
        {
            (0,  W(101, "winword",   "2분기 실적 보고서.docx - Word")),
            (6,  W(102, "chrome",    "통계청 KOSIS 국가통계포털 - Chrome")),
            (10, W(103, "kakaotalk", "카카오톡")),
            (16, W(104, "chrome",    "고양이 영상 모음 - YouTube - Chrome")),
            (24, W(103, "kakaotalk", "카카오톡")),
        };

        static WindowInfo W(int h, string proc, string title) =>
            new WindowInfo { Hwnd = (IntPtr)h, Pid = 1000 + h, Proc = proc, Title = title, Class = "Demo" };

        int StepAt(double t)
        {
            int s = 0;
            for (int i = 0; i < Script.Length; i++) if (t >= Script[i].at) s = i;
            return s;
        }

        public WindowInfo Foreground()
        {
            int step = StepAt(clock.Elapsed.TotalSeconds);
            if (forced != null && step == forcedStep) return forced;
            forced = null;
            return Script[step].w;
        }

        public double IdleSeconds() => 0;
        public bool IsAlive(IntPtr hwnd) => true;

        public bool Activate(IntPtr hwnd)
        {
            foreach (var s in Script)
                if (s.w.Hwnd == hwnd)
                {
                    forced = s.w;
                    forcedStep = StepAt(clock.Elapsed.TotalSeconds);
                    return true;
                }
            return false;
        }
    }

    /// <summary>프로그램 이름 / 문서 이름 자동 생성</summary>
    static class Naming
    {
        static readonly Dictionary<string, string> Apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"winword","Word"}, {"excel","Excel"}, {"powerpnt","PowerPoint"}, {"outlook","Outlook"}, {"olk","Outlook"},
            {"onenote","OneNote"}, {"chrome","Chrome"}, {"msedge","Edge"}, {"firefox","Firefox"}, {"whale","Whale"},
            {"opera","Opera"}, {"brave","Brave"}, {"code","VS Code"}, {"devenv","Visual Studio"}, {"notepad","메모장"},
            {"notepad++","Notepad++"}, {"hwp","한글"}, {"hword","한글"}, {"kakaotalk","카카오톡"}, {"explorer","탐색기"},
            {"acrobat","Acrobat"}, {"acrord32","Acrobat Reader"}, {"slack","Slack"}, {"teams","Teams"}, {"ms-teams","Teams"},
            {"zoom","Zoom"}, {"notion","Notion"}, {"figma","Figma"}, {"photoshop","Photoshop"}, {"illustrator","Illustrator"},
            {"windowsterminal","터미널"}, {"cmd","명령 프롬프트"}, {"powershell","PowerShell"}, {"mspaint","그림판"},
            {"applicationframehost","Windows 앱"}, {"discord","Discord"}, {"steam","Steam"},
        };

        static readonly Dictionary<string, string> descCache = new Dictionary<string, string>();

        public static string AppName(WindowInfo w)
        {
            if (Apps.TryGetValue(w.Proc, out var name)) return name;
            if (descCache.TryGetValue(w.Proc, out name)) return name;
            name = w.Proc;
            try
            {
                if (w.ExePath != null)
                {
                    var d = FileVersionInfo.GetVersionInfo(w.ExePath).FileDescription?.Trim();
                    if (!string.IsNullOrEmpty(d) && d.Length <= 30) name = d;
                }
            }
            catch { }
            descCache[w.Proc] = name;
            return name;
        }

        static readonly Regex MorePages = new Regex(
            @"\s*(및 (다른 )?(페이지|탭) ?\d+개( 더)?|and \d+ more (pages?|tabs?))", RegexOptions.IgnoreCase);
        static readonly Regex Separator = new Regex(@"\s+[-–—|]\s+");
        static readonly Regex Profile = new Regex(@"^(개인|personal|work|회사|업무|프로필 ?\d*|profile ?\d*)$", RegexOptions.IgnoreCase);

        /// <summary>창 제목에서 프로그램 이름 꼬리를 떼고 문서/페이지 이름만 남김</summary>
        public static string DocName(WindowInfo w, string app)
        {
            string t = (w.Title ?? "").Replace("​", "").Trim();
            t = t.TrimStart('*', '●', '•', ' ').Trim();
            t = MorePages.Replace(t, "");

            var parts = Separator.Split(t).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (parts.Count > 1)
            {
                parts.RemoveAt(parts.Count - 1);                     // "... - Word", "... - Chrome"
                if (parts.Count > 1 && Profile.IsMatch(parts[parts.Count - 1]))
                    parts.RemoveAt(parts.Count - 1);                 // Edge "... - 개인 - Microsoft Edge"
                t = string.Join(" - ", parts);
            }
            t = t.TrimEnd('*').Trim();
            if (t.Length == 0) return app;
            return t;
        }
    }

    /// <summary>딴짓 규칙 (%APPDATA%\FocusTimer\distractions.txt, 메모장으로 수정 가능)</summary>
    class DistractionRules
    {
        public static readonly string FilePath = Path.Combine(Storage.Dir, "distractions.txt");

        static readonly HashSet<string> Browsers = new HashSet<string>
            { "chrome", "msedge", "firefox", "whale", "opera", "brave", "iexplore", "vivaldi" };

        static readonly string[] Defaults =
        {
            "proc:kakaotalk", "proc:steam",
            "title:youtube", "title:유튜브", "title:netflix", "title:넷플릭스", "title:instagram", "title:인스타그램",
            "title:facebook", "title:페이스북", "title:twitch", "title:치지직", "title:웹툰", "title:webtoon",
            "title:tiktok", "title:틱톡",
        };

        const string Header =
            "# 업무 집중 타이머 - 딴짓으로 분류할 규칙 (한 줄에 하나)\r\n" +
            "# proc:프로그램이름   → 그 프로그램 전체를 딴짓으로 (예: proc:kakaotalk)\r\n" +
            "# title:키워드        → 웹브라우저 창 제목에 키워드가 있으면 딴짓으로 (예: title:youtube)\r\n" +
            "# 저장하면 몇 초 안에 자동 반영됩니다.\r\n";

        List<string> rules = new List<string>();
        DateTime loadedStamp;

        public DistractionRules() => Load();

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { rules = Defaults.ToList(); Save(); return; }
                rules = File.ReadAllLines(FilePath)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .ToList();
                loadedStamp = File.GetLastWriteTimeUtc(FilePath);
            }
            catch { rules = Defaults.ToList(); }
        }

        public void ReloadIfChanged()
        {
            try
            {
                if (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != loadedStamp) Load();
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Storage.Dir);
                File.WriteAllText(FilePath, Header + string.Join("\r\n", rules) + "\r\n", Encoding.UTF8);
                loadedStamp = File.GetLastWriteTimeUtc(FilePath);
            }
            catch { }
        }

        public static bool IsBrowser(string proc) => Browsers.Contains(proc);

        /// <summary>딴짓이면 맞은 규칙을, 아니면 null</summary>
        public string Match(WindowInfo w)
        {
            string title = (w.Title ?? "").ToLowerInvariant();
            foreach (var r in rules)
            {
                int c = r.IndexOf(':');
                if (c < 0) continue;
                string kind = r.Substring(0, c).Trim().ToLowerInvariant();
                string val = r.Substring(c + 1).Trim().ToLowerInvariant();
                if (val.Length == 0) continue;
                if (kind == "proc" && (w.Proc == val || w.Proc == val.Replace(".exe", ""))) return r;
                if (kind == "title" && IsBrowser(w.Proc) && title.Contains(val)) return r;
            }
            return null;
        }

        public void AddProc(string proc)
        {
            string r = "proc:" + proc;
            if (!rules.Contains(r)) { rules.Add(r); Save(); }
        }

        public void Remove(string rule)
        {
            if (rules.Remove(rule)) Save();
        }
    }
}
