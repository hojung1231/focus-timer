using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FocusTimer
{
    /// <summary>사용자 설정 (%APPDATA%\FocusTimer\settings.txt)</summary>
    static class Settings
    {
        static readonly string FilePath = Path.Combine(Storage.Dir, "settings.txt");

        public static readonly int[] TransparencyChoices = { 0, 10, 20, 30, 40, 50, 60, 70 };
        public static readonly int[] MaxItemsChoices = { 1, 2, 3, 4, 5 };

        /// <summary>투명도 % (0 = 불투명). 기본 30%</summary>
        public static int Transparency { get; private set; } = 30;

        /// <summary>자동 감지에서 보여줄 작업 수 (많이 한 순). 기본 3개</summary>
        public static int MaxItems { get; private set; } = 3;

        /// <summary>마우스를 올리면 잠깐 선명하게</summary>
        public static bool HoverOpaque { get; private set; } = true;

        /// <summary>창 크기 배율 (1.0 = 기본). 글자·박스가 함께 커지고 작아짐</summary>
        public static float Scale { get; private set; } = 1.0f;
        public const float MinScale = 0.6f, MaxScale = 3.0f;
        public static readonly int[] ScaleChoices = { 75, 100, 125, 150, 200, 300 };

        public static event Action Changed;

        public static double Opacity => 1.0 - Transparency / 100.0;

        static Settings() => Load();

        public static void SetTransparency(int v) { Transparency = Math.Max(0, Math.Min(80, v)); SaveAndNotify(); }
        public static void SetMaxItems(int v) { MaxItems = Math.Max(1, Math.Min(5, v)); SaveAndNotify(); }
        public static void SetHoverOpaque(bool v) { HoverOpaque = v; SaveAndNotify(); }

        /// <summary>save=false: 모서리를 끄는 동안 (손을 뗄 때 Save 호출)</summary>
        public static void SetScale(float v, bool save = true)
        {
            Scale = Math.Max(MinScale, Math.Min(MaxScale, v));
            if (save) Save();
            Changed?.Invoke();
        }

        static void SaveAndNotify()
        {
            Save();
            Changed?.Invoke();
        }

        static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                if (d.TryGetValue("transparency", out var t) && int.TryParse(t, out int tv))
                    Transparency = Math.Max(0, Math.Min(80, tv));
                if (d.TryGetValue("max_items", out var m) && int.TryParse(m, out int mv))
                    MaxItems = Math.Max(1, Math.Min(5, mv));
                if (d.TryGetValue("hover_opaque", out var h) && bool.TryParse(h, out bool hv))
                    HoverOpaque = hv;
                if (d.TryGetValue("scale", out var sc) &&
                    float.TryParse(sc, NumberStyles.Float, CultureInfo.InvariantCulture, out float sv))
                    Scale = Math.Max(MinScale, Math.Min(MaxScale, sv));
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Storage.Dir);
                var sb = new StringBuilder();
                sb.Append("transparency=").Append(Transparency.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("max_items=").Append(MaxItems.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("hover_opaque=").Append(HoverOpaque ? "true" : "false").Append("\r\n");
                sb.Append("scale=").Append(Scale.ToString("0.00", CultureInfo.InvariantCulture)).Append("\r\n");
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }
}
