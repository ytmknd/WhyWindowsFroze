using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WhyWindowsFroze
{
    /// <summary>
    /// メッセージの国際化。Windows の表示言語が日本語なら日本語、それ以外は英語。
    /// 文字列は呼び出し側に L("日本語", "English") の形でまとめて書く。
    /// </summary>
    internal static class Loc
    {
        /// <summary>起動引数 /lang:ja|en で上書きできる（他のクラスより先に設定すること）</summary>
        public static bool Ja = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

        public static string L(string ja, string en) => Ja ? ja : en;
    }

    internal static class Util
    {
        public static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        public static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        public static string T(DateTime? t) => t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "-";

        public static string TMs(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        /// <summary>イベントログ XPath 用の UTC 時刻表記</summary>
        public static string U(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        public static string Dur(TimeSpan ts)
        {
            string sign = ts < TimeSpan.Zero ? "-" : "";
            ts = ts.Duration();
            if (ts.TotalDays >= 1) return sign + Loc.L($"{(int)ts.TotalDays}日{ts.Hours}時間", $"{(int)ts.TotalDays}d {ts.Hours}h");
            if (ts.TotalHours >= 1) return sign + Loc.L($"{(int)ts.TotalHours}時間{ts.Minutes}分", $"{(int)ts.TotalHours}h {ts.Minutes}m");
            if (ts.TotalMinutes >= 1) return sign + Loc.L($"{(int)ts.TotalMinutes}分{ts.Seconds}秒", $"{(int)ts.TotalMinutes}m {ts.Seconds}s");
            return sign + Loc.L($"{ts.TotalSeconds:0}秒", $"{ts.TotalSeconds:0}s");
        }

        /// <summary>"2026-10-07T00:00:00.500000000Z" 形式（小数部 9 桁を含む）をローカル時刻に変換</summary>
        public static DateTime? ParseUtcIso(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = Regex.Match(s.Trim(), @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d+))?Z?$");
            if (!m.Success) return null;
            var dt = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            if (m.Groups[2].Success)
                dt = dt.AddTicks(long.Parse((m.Groups[2].Value + "0000000").Substring(0, 7), CultureInfo.InvariantCulture));
            if (dt.Year < 2000) return null;
            return dt.ToLocalTime();
        }

        public static (int Code, string Output) Run(string exe, string args, int timeoutMs = 120000)
        {
            try
            {
                var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = oem,
                    StandardErrorEncoding = oem,
                };
                using (var p = Process.Start(psi))
                {
                    var so = p.StandardOutput.ReadToEndAsync();
                    var se = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return (-1, Loc.L("タイムアウト", "Timed out"));
                    }
                    return (p.ExitCode, (so.Result + se.Result).TrimEnd());
                }
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        public static string Sys32(string exe) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);

        public static RegistryKey HKLM => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        public static RegistryKey HKU => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        public static RegistryKey HKCR => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);

        public static object Reg(RegistryKey root, string path, string name)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                    return k?.GetValue(name);
            }
            catch { return null; }
        }

        public static string RegS(RegistryKey root, string path, string name)
        {
            var v = Reg(root, path, name);
            if (v == null) return null;
            if (v is string[] arr) return string.Join("; ", arr);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static string SidToName(string sid)
        {
            try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
            catch { return sid; }
        }

        public static string Trunc(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max) + "…";

        public static string OneLine(string s) => Regex.Replace(s ?? "", @"\s*[\r\n]+\s*", " / ");

        public static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
