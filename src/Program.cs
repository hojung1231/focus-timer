using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FocusTimer
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool updated = args.Contains("--updated");
            int mode = 0;
            var m = args.FirstOrDefault(a => a.StartsWith("--mode="));
            if (m != null) int.TryParse(m.Substring(7), out mode);

            string restartArgs = null;
            using (var mutex = new Mutex(false, "FocusTimer_SingleInstance_v1"))
            {
                bool owned;
                // 업데이트 직후에는 예전 프로그램이 완전히 닫힐 때까지 잠깐 기다림
                try { owned = mutex.WaitOne(updated ? 15000 : 0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) return; // 이미 실행 중이면 하나만

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var app = new AppController(mode, updated);
                    Application.Run(app);
                    restartArgs = app.RestartArgs;
                }
                finally { mutex.ReleaseMutex(); }
            }

            if (restartArgs != null) Updater.Launch(restartArgs);
        }
    }

    /// <summary>홈 화면 ↔ 버전 1/2 화면 전환, 업데이트 실행</summary>
    class AppController : ApplicationContext
    {
        WidgetForm current;
        public string RestartArgs { get; private set; }

        public AppController(int mode, bool updated)
        {
            if (mode == 1) Switch(new ManualForm());
            else if (mode == 2) Switch(new AutoForm());
            else ShowHome();

            if (updated)
            {
                Updater.CleanupOld();
                var f = current;
                f.Shown += (o, e) => f.Toast($"v{Updater.Text(Updater.Current)}(으)로 업데이트했어요");
            }
            Updater.Start();
        }

        void ShowHome()
        {
            var home = new HomeForm();
            home.ModeChosen += mode =>
            {
                if (mode == 1) Switch(new ManualForm());
                else Switch(new AutoForm());
            };
            Switch(home);
        }

        void Switch(WidgetForm next)
        {
            var old = current;
            current = next;
            next.HomeRequested += ShowHome;
            next.UpdateRequested += () => RunUpdate(next);
            next.FormClosed += (o, e) => { if (current == next) ExitThread(); };

            if (old != null) next.PlaceLike(old);
            else next.CenterTop();
            next.Show();
            old?.Close();
        }

        async void RunUpdate(WidgetForm f)
        {
            string msg = $"새 버전이 있어요.\n\n현재 v{Updater.Text(Updater.Current)} → 새 버전 v{Updater.Text(Updater.Latest)}";
            if (Updater.Notes.Length > 0) msg += "\n\n" + Updater.Notes;
            msg += "\n\n지금 업데이트하고 다시 시작할까요?";
            if (f.ModeId == 2) msg += "\n(자동 감지 화면의 이번 기록은 초기화됩니다)";
            if (MessageBox.Show(f, msg, "업데이트", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            f.SetUpdateProgress("받는 중…");
            try
            {
                await Updater.Download(p => f.SetUpdateProgress($"{p}%"));
            }
            catch (Exception ex)
            {
                f.SetUpdateProgress(null);
                string why = ex is UnauthorizedAccessException || ex is IOException && !(ex is InvalidDataException)
                    ? "이 폴더에서는 파일을 바꿀 수 없어요.\n프로그램을 바탕화면이나 내 문서 폴더로 옮겨서 실행해 주세요."
                    : ex is InvalidDataException ? ex.Message
                    : "새 버전을 받지 못했어요. 인터넷 연결을 확인해 주세요.";
                if (MessageBox.Show(f, why + "\n\n다운로드 페이지를 열까요?", "업데이트 실패",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Updater.OpenPage();
                return;
            }

            // 기록 저장(창 닫기) → 완전히 종료된 뒤 새 버전 실행
            RestartArgs = $"--updated --mode={f.ModeId}";
            current.Close();
        }
    }
}
