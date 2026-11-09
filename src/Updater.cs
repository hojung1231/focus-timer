using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FocusTimer
{
    /// <summary>
    /// GitHub 저장소의 download/version.txt 로 새 버전을 확인하고 exe를 교체.
    /// version.txt 형식: 1줄 버전(2.1.0), 2줄 exe의 SHA-256, 3줄부터 변경 내용
    /// </summary>
    static class Updater
    {
        const string DefaultBase = "https://raw.githubusercontent.com/hojung1231/focus-timer/main/download/";
        public const string PageUrl = "https://github.com/hojung1231/focus-timer";

        // GitHub API: 캐시가 거의 없어 배포 직후에도 바로 새 버전이 보임 (raw 주소는 최대 5분 캐시)
        const string ApiBase = "https://api.github.com/repos/hojung1231/focus-timer/contents/download/";

        /// <summary>새 버전 정보를 받아 올 주소들 (앞에서부터 시도)</summary>
        static string[] Sources
        {
            get
            {
                var test = Environment.GetEnvironmentVariable("FOCUSTIMER_UPDATE_BASE");
                return test != null ? new[] { test } : new[] { ApiBase, DefaultBase };
            }
        }

        static Uri UrlFor(string source, string file) =>
            source == ApiBase
                ? new Uri(source + file + "?ref=main")
                : new Uri(source + file + "?t=" + DateTime.UtcNow.Ticks);

        public static readonly Version Current = Norm(Assembly.GetExecutingAssembly().GetName().Version);
        public static Version Latest { get; private set; }
        public static string Notes { get; private set; } = "";
        static string latestSha;

        public static bool Available => Latest != null && Latest > Current;
        public static event Action Changed;

        static Timer timer;
        static bool busy;

        public static string Text(Version v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";

        static Version Norm(Version v) =>
            new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

        static WebClient NewClient()
        {
            var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "FocusTimer-Updater";
            wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github.raw"; // API에서 파일 내용 그대로
            wc.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            return wc;
        }

        /// <summary>UI 스레드에서 호출: 바로 한 번 + 3시간마다 확인</summary>
        public static void Start()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            _ = CheckAsync();
            timer = new Timer { Interval = 3 * 60 * 60 * 1000 };
            timer.Tick += (o, e) => { _ = CheckAsync(); };
            timer.Start();
        }

        public static async Task<bool> CheckAsync()
        {
            if (busy) return false;
            busy = true;
            try
            {
                string txt = null;
                Exception last = null;
                foreach (var src in Sources)
                {
                    try
                    {
                        using (var wc = NewClient())
                            txt = await wc.DownloadStringTaskAsync(UrlFor(src, "version.txt"));
                        if (txt != null && txt.Trim().Length > 0) break;
                    }
                    catch (Exception ex) { last = ex; txt = null; }
                }
                if (txt == null) throw last ?? new WebException("no source");
                var lines = txt.Replace("\r", "").Split('\n');
                Latest = Norm(Version.Parse(lines[0].Trim()));
                latestSha = lines.Length > 1 ? lines[1].Trim().ToLowerInvariant() : "";
                Notes = string.Join("\n", lines.Skip(2).Where(l => l.Trim().Length > 0));
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
            finally { busy = false; }
        }

        /// <summary>새 exe를 받아 검증한 뒤 현재 exe와 교체. 성공하면 true (재시작 필요)</summary>
        public static async Task Download(Action<int> progress)
        {
            string exe = Assembly.GetEntryAssembly().Location;
            string newPath = exe + ".new";
            string oldPath = exe + ".old";

            try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
            Exception last = null;
            bool got = false;
            foreach (var src in Sources)
            {
                try
                {
                    using (var wc = NewClient())
                    {
                        wc.DownloadProgressChanged += (o, e) => progress?.Invoke(e.ProgressPercentage);
                        await wc.DownloadFileTaskAsync(UrlFor(src, "FocusTimer.exe"), newPath);
                    }
                    if (string.IsNullOrEmpty(latestSha) || Sha256(newPath) == latestSha) { got = true; break; }
                    last = new InvalidDataException("받은 파일이 올바르지 않아요 (검증 실패). 잠시 후 다시 시도해 주세요.");
                }
                catch (Exception ex) { last = ex; }
            }
            if (!got)
            {
                try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
                throw last ?? new WebException("download failed");
            }


            // 실행 중인 exe는 덮어쓸 수 없지만 이름은 바꿀 수 있음 → 바꿔치기
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exe, oldPath);
            try { File.Move(newPath, exe); }
            catch { File.Move(oldPath, exe); throw; }
        }

        static string Sha256(string path)
        {
            using (var f = File.OpenRead(path))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>앱이 완전히 닫힌 뒤 새 exe 실행</summary>
        public static void Launch(string args)
        {
            string exe = Assembly.GetEntryAssembly().Location;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                Process.Start(exe, args);
            else
                Process.Start("mono", "\"" + exe + "\" " + args); // 테스트 환경용
        }

        /// <summary>업데이트 후 남은 예전 exe 정리</summary>
        public static void CleanupOld()
        {
            var t = new Timer { Interval = 3000 };
            t.Tick += (o, e) =>
            {
                t.Stop();
                t.Dispose();
                try
                {
                    string old = Assembly.GetEntryAssembly().Location + ".old";
                    if (File.Exists(old)) File.Delete(old);
                }
                catch { }
            };
            t.Start();
        }

        public static void OpenPage()
        {
            try { Process.Start(PageUrl); } catch { }
        }
    }
}
