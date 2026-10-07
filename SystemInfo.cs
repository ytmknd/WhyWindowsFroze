using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static WhyWindowsFroze.Loc;

namespace WhyWindowsFroze
{
    internal sealed class InfoSection
    {
        public string Title;
        public List<(string Key, string Value)> Rows = new List<(string, string)>();
        public string Pre; // 整形済みテキスト（コマンド出力など）

        public InfoSection(string title) { Title = title; }
        public void Add(string k, object v) => Rows.Add((k, v == null ? "-" : Convert.ToString(v, CultureInfo.InvariantCulture)));
    }

    internal sealed class DumpFile
    {
        public string Path;
        public DateTime Time;
        public long Size;
        public string Kind;
    }

    internal static class SystemInfo
    {
        const string NtCv = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        const string CrashControl = @"SYSTEM\CurrentControlSet\Control\CrashControl";

        static List<ManagementObject> Wmi(string query, string scope = @"root\cimv2")
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, query))
                    return s.Get().Cast<ManagementObject>().ToList();
            }
            catch { return new List<ManagementObject>(); }
        }

        static string WmiDate(object v)
        {
            try { return v == null ? "-" : ManagementDateTimeConverter.ToDateTime(v.ToString()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
            catch { return v?.ToString(); }
        }

        public static List<InfoSection> Collect()
        {
            var list = new List<InfoSection>();
            var hklm = Util.HKLM;

            var os = new InfoSection(L("OS / ハードウェア", "OS / Hardware"));
            os.Add(L("コンピューター名", "Computer name"), Environment.MachineName);
            os.Add("OS", $"{Util.RegS(hklm, NtCv, "ProductName")} {Util.RegS(hklm, NtCv, "DisplayVersion")} (Build {Util.RegS(hklm, NtCv, "CurrentBuildNumber")}.{Util.RegS(hklm, NtCv, "UBR")})");
            foreach (var cs in Wmi("SELECT Manufacturer,Model,TotalPhysicalMemory,AutomaticManagedPagefile FROM Win32_ComputerSystem"))
            {
                os.Add(L("機種", "Model"), $"{cs["Manufacturer"]} {cs["Model"]}");
                os.Add(L("物理メモリ", "Physical memory"), $"{Convert.ToUInt64(cs["TotalPhysicalMemory"]) / 1048576} MB");
                os.Add(L("ページファイル自動管理", "Page file system-managed"), cs["AutomaticManagedPagefile"]);
            }
            foreach (var b in Wmi("SELECT SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"))
                os.Add("BIOS", $"{b["SMBIOSBIOSVersion"]} ({WmiDate(b["ReleaseDate"])})");
            foreach (var c in Wmi("SELECT Name FROM Win32_Processor"))
                os.Add("CPU", c["Name"]?.ToString().Trim());
            foreach (var o in Wmi("SELECT LastBootUpTime,InstallDate FROM Win32_OperatingSystem"))
            {
                os.Add(L("最終起動", "Last boot"), WmiDate2(o["LastBootUpTime"]));
                os.Add(L("OS インストール日", "OS install date"), WmiDate(o["InstallDate"]));
            }
            foreach (var pf in Wmi("SELECT Name,AllocatedBaseSize,CurrentUsage,PeakUsage FROM Win32_PageFileUsage"))
                os.Add(L("ページファイル", "Page file"), L($"{pf["Name"]} 割当 {pf["AllocatedBaseSize"]} MB / 使用 {pf["CurrentUsage"]} MB / ピーク {pf["PeakUsage"]} MB", $"{pf["Name"]} allocated {pf["AllocatedBaseSize"]} MB / used {pf["CurrentUsage"]} MB / peak {pf["PeakUsage"]} MB"));
            try
            {
                var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                os.Add(L("システムドライブ空き", "System drive free"), $"{c.AvailableFreeSpace / 1073741824.0:0.0} GB / {c.TotalSize / 1073741824.0:0.0} GB");
            }
            catch { }
            os.Add(L("高速スタートアップ (HiberbootEnabled)", "Fast Startup (HiberbootEnabled)"), Util.RegS(hklm, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled"));
            os.Add("Prefetch (EnablePrefetcher)", Util.RegS(hklm, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher"));
            list.Add(os);

            var gpu = new InfoSection(L("GPU / ディスプレイ", "GPU / Display"));
            foreach (var v in Wmi("SELECT Name,DriverVersion,DriverDate,CurrentHorizontalResolution,CurrentVerticalResolution,PNPDeviceID FROM Win32_VideoController"))
                gpu.Add(v["Name"]?.ToString(), L($"ドライバー {v["DriverVersion"]} ({WmiDate(v["DriverDate"])})  解像度 {v["CurrentHorizontalResolution"]}x{v["CurrentVerticalResolution"]}", $"Driver {v["DriverVersion"]} ({WmiDate(v["DriverDate"])})  Resolution {v["CurrentHorizontalResolution"]}x{v["CurrentVerticalResolution"]}"));
            gpu.Add(L("接続モニター数", "Connected monitors"), Wmi("SELECT InstanceName FROM WmiMonitorID", @"root\wmi").Count);
            gpu.Add("TdrDelay", Util.RegS(hklm, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay") ?? L("(既定 2 秒)", "(default 2 s)"));
            gpu.Add("HwSchMode (HAGS)", Util.RegS(hklm, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") ?? L("(既定)", "(default)"));
            list.Add(gpu);

            var disk = new InfoSection(L("ストレージ", "Storage"));
            foreach (var d in Wmi("SELECT FriendlyName,MediaType,BusType,HealthStatus,FirmwareVersion,Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                string media = MediaType(d["MediaType"]);
                disk.Add(d["FriendlyName"]?.ToString(), $"{media} / Bus={BusType(d["BusType"])} / {L("状態", "Health")}={Health(d["HealthStatus"])} / FW={d["FirmwareVersion"]} / {Convert.ToUInt64(d["Size"] ?? 0UL) / 1000000000UL} GB");
            }
            list.Add(disk);

            var dump = new InfoSection(L("クラッシュダンプ設定", "Crash dump settings"));
            var cde = Util.Reg(hklm, CrashControl, "CrashDumpEnabled");
            dump.Add("CrashDumpEnabled", $"{cde} ({DumpKind(cde)})");
            dump.Add(L("FilterPages (アクティブメモリダンプ)", "FilterPages (active memory dump)"), Util.RegS(hklm, CrashControl, "FilterPages"));
            dump.Add("DumpFile", Util.RegS(hklm, CrashControl, "DumpFile"));
            dump.Add("AutoReboot", Util.RegS(hklm, CrashControl, "AutoReboot"));
            dump.Add("AlwaysKeepMemoryDump", Util.RegS(hklm, CrashControl, "AlwaysKeepMemoryDump"));
            dump.Add("NMICrashDump", Util.RegS(hklm, CrashControl, "NMICrashDump"));
            foreach (var svc in new[] { "kbdhid", "i8042prt" })
                dump.Add($"CrashOnCtrlScroll ({svc})", Util.RegS(hklm, $@"SYSTEM\CurrentControlSet\Services\{svc}\Parameters", "CrashOnCtrlScroll"));
            list.Add(dump);

            var off = new InfoSection(L("Office / ブラウザー", "Office / Browsers"));
            const string c2r = @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration";
            off.Add(L("Office バージョン", "Office version"), Util.RegS(hklm, c2r, "VersionToReport"));
            off.Add(L("Office 製品", "Office products"), Util.RegS(hklm, c2r, "ProductReleaseIds"));
            off.Add(L("Office プラットフォーム", "Office platform"), Util.RegS(hklm, c2r, "Platform"));
            off.Add(L("更新チャネル", "Update channel"), Util.RegS(hklm, c2r, "UpdateChannel") ?? Util.RegS(hklm, c2r, "CDNBaseUrl"));
            off.Add(L("Office 共有ライセンス", "Office shared computer licensing"), Util.RegS(hklm, c2r, "SharedComputerLicensing"));
            off.Add("Edge", Util.RegS(hklm, @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}", "pv"));
            off.Add("WebView2 Runtime", Util.RegS(hklm, @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", "pv"));
            off.Add("Chrome", Util.RegS(hklm, @"SOFTWARE\WOW6432Node\Google\Update\Clients\{8A69D345-D564-463c-AFF1-A69D9E530F96}", "pv"));
            foreach (var proto in new[] { "ms-word", "ms-excel" })
                off.Add($"{proto}: " + L("ハンドラー", "handler"), Util.RegS(Util.HKCR, $@"{proto}\shell\open\command", ""));
            foreach (var p in new[] { @"SOFTWARE\Microsoft\Office", @"SOFTWARE\WOW6432Node\Microsoft\Office" })
                foreach (var app in new[] { "Word", "Excel" })
                    foreach (var a in AddIns(hklm, $@"{p}\{app}\Addins"))
                        off.Add(L($"アドイン (全ユーザー/{app})", $"Add-in (all users/{app})"), a);
            list.Add(off);

            var sec = new InfoSection(L("セキュリティ製品 / フィルタードライバー", "Security software / Filter drivers"));
            foreach (var av in Wmi("SELECT displayName,productState FROM AntiVirusProduct", @"root\SecurityCenter2"))
                sec.Add(L("ウイルス対策", "Antivirus"), $"{av["displayName"]} (state=0x{Convert.ToUInt32(av["productState"]):X})");
            var flt = Util.Run(Util.Sys32("fltMC.exe"), "filters");
            sec.Pre = "> fltmc filters\r\n" + flt.Output;
            list.Add(sec);

            var pw = new InfoSection(L("電源", "Power"));
            pw.Pre = "> powercfg /a\r\n" + Util.Run(Util.Sys32("powercfg.exe"), "/a").Output;
            list.Add(pw);

            return list;
        }

        static string WmiDate2(object v)
        {
            try { return ManagementDateTimeConverter.ToDateTime(v.ToString()).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
            catch { return v?.ToString(); }
        }

        public static string DumpKind(object v)
        {
            switch (v is int i ? i : -1)
            {
                case 0: return L("なし", "None");
                case 1: return L("完全メモリダンプ", "Complete memory dump");
                case 2: return L("カーネルメモリダンプ", "Kernel memory dump");
                case 3: return L("最小メモリダンプ", "Small memory dump");
                case 7: return L("自動メモリダンプ", "Automatic memory dump");
                default: return L("不明", "Unknown");
            }
        }

        static string MediaType(object v)
        {
            switch (Convert.ToInt32(v ?? 0)) { case 3: return "HDD"; case 4: return "SSD"; case 5: return "SCM"; default: return L("不明", "Unknown"); }
        }

        static string BusType(object v)
        {
            switch (Convert.ToInt32(v ?? 0)) { case 7: return "USB"; case 11: return "SATA"; case 17: return "NVMe"; case 8: return "RAID"; case 10: return "SAS"; default: return Convert.ToString(v); }
        }

        static string Health(object v)
        {
            switch (Convert.ToInt32(v ?? -1)) { case 0: return L("正常", "Healthy"); case 1: return L("警告", "Warning"); case 2: return L("異常", "Unhealthy"); default: return L("不明", "Unknown"); }
        }

        static IEnumerable<string> AddIns(RegistryKey root, string path)
        {
            RegistryKey k = null;
            try { k = root.OpenSubKey(path); } catch { }
            if (k == null) yield break;
            using (k)
            {
                foreach (var n in k.GetSubKeyNames())
                {
                    string fn = Util.RegS(k, n, "FriendlyName") ?? n;
                    string lb = Util.RegS(k, n, "LoadBehavior");
                    yield return $"{fn} [{n}] LoadBehavior={lb}";
                }
            }
        }

        static readonly Regex UserSid = new Regex(@"^S-1-(5-21|12-1)-[\d-]+$", RegexOptions.Compiled);

        /// <summary>ユーザー依存の要因を探すため、プロファイルごとの Office 関連設定を集める</summary>
        public static List<InfoSection> CollectUsers()
        {
            var list = new List<InfoSection>();
            var hku = Util.HKU;
            var loaded = new HashSet<string>(hku.GetSubKeyNames().Where(s => UserSid.IsMatch(s)), StringComparer.OrdinalIgnoreCase);

            string[] profiles;
            using (var pl = Util.HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                profiles = pl?.GetSubKeyNames().Where(s => UserSid.IsMatch(s)).ToArray() ?? new string[0];

            foreach (var sid in profiles.Union(loaded))
            {
                var s = new InfoSection(L("ユーザー: ", "User: ") + Util.SidToName(sid));
                s.Add("SID", sid);
                string profile = Util.RegS(Util.HKLM, $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}", "ProfileImagePath");
                s.Add(L("プロファイル", "Profile"), profile);
                if (profile != null)
                {
                    var cache = Path.Combine(profile, @"AppData\Local\Microsoft\Office\16.0\OfficeFileCache");
                    s.Add(L("Office ドキュメントキャッシュ", "Office document cache"), DirSize(cache));
                    try { s.Add(L("プロファイル最終更新", "Profile last modified"), Directory.GetLastWriteTime(Path.Combine(profile, "NTUSER.DAT")).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)); } catch { }
                }

                if (!loaded.Contains(sid))
                {
                    s.Add(L("レジストリ", "Registry"), L("未ロード（ログオンしていないため設定は取得できません。対象ユーザーのログオン中に実行してください）", "Not loaded (the user is not logged on, so settings are unavailable; run while the user is logged on)"));
                    list.Add(s);
                    continue;
                }

                string u = sid + @"\";
                s.Add(L("既定のブラウザー (https)", "Default browser (https)"), Util.RegS(hku, u + @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId"));
                s.Add(L("Office ハードウェアアクセラレーション無効", "Office DisableHardwareAcceleration"), Util.RegS(hku, u + @"Software\Microsoft\Office\16.0\Common\Graphics", "DisableHardwareAcceleration") ?? L("(未設定)", "(not set)"));
                s.Add(L("同 (ポリシー)", "  (policy)"), Util.RegS(hku, u + @"Software\Policies\Microsoft\Office\16.0\Common\Graphics", "DisableHardwareAcceleration") ?? L("(未設定)", "(not set)"));
                s.Add(L("表示スケール LogPixels", "Display scale LogPixels"), Util.RegS(hku, u + @"Control Panel\Desktop", "LogPixels") ?? L("(既定 96)", "(default 96)"));
                s.Add(L("OneDrive バージョン", "OneDrive version"), Util.RegS(hku, u + @"Software\Microsoft\OneDrive", "Version"));

                using (var acc = hku.OpenSubKey(u + @"Software\Microsoft\OneDrive\Accounts"))
                    if (acc != null)
                        foreach (var n in acc.GetSubKeyNames().Where(n => n.StartsWith("Business", StringComparison.OrdinalIgnoreCase)))
                            s.Add($"OneDrive {n}", $"{Util.RegS(acc, n, "UserEmail")}  ({Util.RegS(acc, n, "UserFolder")})");

                using (var ids = hku.OpenSubKey(u + @"Software\Microsoft\Office\16.0\Common\Identity\Identities"))
                    if (ids != null)
                    {
                        s.Add(L("Office サインイン ID 数", "Office signed-in identities"), ids.SubKeyCount);
                        foreach (var n in ids.GetSubKeyNames())
                            s.Add("Office ID", $"{Util.RegS(ids, n, "EmailAddress")} ({Util.RegS(ids, n, "ProviderId")})");
                    }

                foreach (var app in new[] { "Word", "Excel" })
                {
                    foreach (var a in AddIns(hku, u + $@"Software\Microsoft\Office\{app}\Addins"))
                        s.Add(L($"アドイン ({app})", $"Add-in ({app})"), a);
                    using (var r = hku.OpenSubKey(u + $@"Software\Microsoft\Office\16.0\{app}\Resiliency\DisabledItems"))
                        if (r != null && r.ValueCount > 0) s.Add(L($"無効化された項目 ({app})", $"Disabled items ({app})"), r.ValueCount.ToString());
                    using (var r = hku.OpenSubKey(u + $@"Software\Microsoft\Office\16.0\{app}\Resiliency\CrashingAddinList"))
                        if (r != null && r.ValueCount > 0) s.Add(L($"クラッシュしたアドイン ({app})", $"Crashing add-ins ({app})"), string.Join(", ", r.GetValueNames()));
                }
                list.Add(s);
            }
            return list;
        }

        static string DirSize(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return L("(なし)", "(none)");
                long size = 0; int n = 0;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    size += f.Length; n++;
                    if (n > 200000) break;
                }
                return $"{size / 1048576.0:0.0} MB / {n} " + L("ファイル", "files");
            }
            catch (Exception ex) { return L("取得失敗: ", "Failed: ") + ex.Message; }
        }

        public static List<DumpFile> CollectDumps()
        {
            var list = new List<DumpFile>();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            void AddFiles(string dir, string pattern, string kind, SearchOption opt)
            {
                try
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (var f in new DirectoryInfo(dir).EnumerateFiles(pattern, opt))
                        list.Add(new DumpFile { Path = f.FullName, Time = f.LastWriteTime, Size = f.Length, Kind = kind });
                }
                catch { }
            }
            AddFiles(win, "MEMORY.DMP", L("メモリダンプ", "Memory dump"), SearchOption.TopDirectoryOnly);
            AddFiles(Path.Combine(win, "Minidump"), "*.dmp", L("ミニダンプ", "Minidump"), SearchOption.TopDirectoryOnly);
            AddFiles(Path.Combine(win, "LiveKernelReports"), "*.dmp", L("ライブカーネルダンプ", "Live kernel dump"), SearchOption.AllDirectories);
            return list.OrderByDescending(d => d.Time).ToList();
        }

        /// <summary>WER レポートフォルダー（システム全体＋各ユーザー）</summary>
        public static List<(string Dir, DateTime Time)> CollectWer()
        {
            var roots = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER\ReportQueue"),
            };
            try
            {
                using (var pl = Util.HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                    foreach (var sid in pl.GetSubKeyNames().Where(s => UserSid.IsMatch(s)))
                    {
                        var p = Util.RegS(pl, sid, "ProfileImagePath");
                        if (p == null) continue;
                        roots.Add(Path.Combine(p, @"AppData\Local\Microsoft\Windows\WER\ReportArchive"));
                        roots.Add(Path.Combine(p, @"AppData\Local\Microsoft\Windows\WER\ReportQueue"));
                    }
            }
            catch { }

            var rx = new Regex(@"Kernel|LiveKernel|BlueScreen|WINWORD|EXCEL|POWERPNT|explorer|dwm|msedge|FileCoAuth|OneDrive|AppHang", RegexOptions.IgnoreCase);
            var list = new List<(string, DateTime)>();
            foreach (var r in roots)
            {
                try
                {
                    if (!Directory.Exists(r)) continue;
                    foreach (var d in Directory.GetDirectories(r))
                        if (rx.IsMatch(Path.GetFileName(d)))
                            list.Add((d, Directory.GetLastWriteTime(d)));
                }
                catch { }
            }
            return list.OrderByDescending(x => x.Item2).ToList();
        }
    }
}
