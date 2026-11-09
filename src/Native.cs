using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FocusTimer
{
    /// <summary>마지막 키보드·마우스 입력 후 지난 시간</summary>
    static class InputIdle
    {
        public const double Limit = 30;   // 30초 동안 움직임이 없으면 타이머 정지

        public static double Seconds()
        {
            try
            {
                var lii = new Native.LASTINPUTINFO { cbSize = 8 };
                if (!Native.GetLastInputInfo(ref lii)) return 0;
                return unchecked((uint)Environment.TickCount - lii.dwTime) / 1000.0;
            }
            catch
            {
                // Windows가 아닌 테스트 환경: 파일에 적힌 숫자를 입력 없는 시간으로 사용
                try
                {
                    var f = Environment.GetEnvironmentVariable("FOCUSTIMER_DEMO_IDLE_FILE");
                    if (f != null && System.IO.File.Exists(f))
                        return double.Parse(System.IO.File.ReadAllText(f).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { }
                return 0;
            }
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        public const int SW_RESTORE = 9;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    }
}
