using System;
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
                Application.Run(new AppController());
            }
        }
    }

    /// <summary>홈 화면 ↔ 버전 1/2 화면 전환</summary>
    class AppController : ApplicationContext
    {
        WidgetForm current;

        public AppController() => ShowHome();

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
            next.FormClosed += (o, e) => { if (current == next) ExitThread(); };

            if (old != null) next.PlaceLike(old);
            else next.CenterTop();
            next.Show();
            old?.Close();
        }
    }
}
