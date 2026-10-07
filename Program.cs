using System;
using System.Linq;
using System.Windows.Forms;

namespace WhyWindowsFroze
{
    internal static class Program
    {
        public const string Version = "1.4.0";

        /// <summary>
        /// 引数（ショートカット配布用、いずれも省略可）:
        ///   /auto       起動直後に解析を開始
        ///   /days:N     解析期間（日）
        ///   /window:N   時間窓（分）
        ///   /at:"yyyy-MM-dd HH:mm"  フリーズの発生時刻（ログに残らなかったフリーズも、この時刻を基準に解析する）
        ///   /lang:ja|en 表示言語（既定は Windows の表示言語）
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // 言語はほかのクラス（カテゴリ名などの静的な文字列）より先に決める
            var lang = args.FirstOrDefault(a => a.StartsWith("/lang:", StringComparison.OrdinalIgnoreCase));
            if (lang != null) Loc.Ja = lang.Substring(6).StartsWith("ja", StringComparison.OrdinalIgnoreCase);

            // .NET では Shift_JIS 等のコードページ（fltmc/powercfg の出力）を使うのに登録が必要
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool auto = args.Any(a => a.Equals("/auto", StringComparison.OrdinalIgnoreCase));
            int? days = IntArg(args, "/days:"), window = IntArg(args, "/window:");
            DateTime? at = null;
            var atArg = args.FirstOrDefault(a => a.StartsWith("/at:", StringComparison.OrdinalIgnoreCase));
            if (atArg != null && DateTime.TryParse(atArg.Substring(4), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var t)) at = t;
            Application.Run(new MainForm(auto, days, window, at));
        }

        static int? IntArg(string[] args, string prefix)
        {
            var a = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return a != null && int.TryParse(a.Substring(prefix.Length), out int v) ? v : (int?)null;
        }
    }
}
