using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using static WhyWindowsFroze.Loc;

namespace WhyWindowsFroze
{
    /// <summary>
    /// イベントログ以外から分かる、フリーズの典型的な要因の現在の状態を点検する。
    /// </summary>
    internal static class Health
    {
        static List<ManagementObject> Wmi(string query, string scope = @"root\cimv2")
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, query))
                    return s.Get().Cast<ManagementObject>().ToList();
            }
            catch { return new List<ManagementObject>(); }
        }

        static DateTime? WmiTime(object v)
        {
            try { return v == null ? (DateTime?)null : ManagementDateTimeConverter.ToDateTime(v.ToString()); }
            catch { return null; }
        }

        static double D(object v) => v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);

        // Microsoft 製の標準的なファイルシステムフィルター（これ以外を他社製の可能性ありとして表示）
        static readonly HashSet<string> MsFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bindflt", "ucpd", "wdfilter", "storqosflt", "wcifs", "cldflt", "filecrypt", "luafv", "npsvctrig", "wof", "fileinfo",
            "applockerfltr", "bfs", "prjflt", "mssecflt", "unionfs", "iorate", "winsetupmon", "dfs", "hvsifltr", "wtd", "gameflt",
            "ntfs", "fsdepends", "dax", "csvflt", "dedup", "datascrn", "srmfilter", "svflt", "rsfx", "mpfilter", "appvvfs", "bfsvc",
        };

        public static List<Finding> Check()
        {
            var r = new List<Finding>();
            void Add(int sev, string cat, string text) => r.Add(new Finding(sev, text) { Category = cat });

            // ---- ストレージ ----
            try
            {
                var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                double freeGb = sys.AvailableFreeSpace / 1073741824.0, pct = 100.0 * sys.AvailableFreeSpace / sys.TotalSize;
                if (freeGb < 5 || pct < 5)
                    Add(3, Cat.Storage, L($"システムドライブの空きが {freeGb:0.0} GB（{pct:0}%）しかありません。ページファイルの拡張や更新ができず、動作が極端に遅くなります。",
                                          $"Only {freeGb:0.0} GB ({pct:0}%) free on the system drive. The page file cannot grow and updates cannot install, making the system extremely slow."));
                else if (freeGb < 15 || pct < 10)
                    Add(2, Cat.Storage, L($"システムドライブの空きが少なめです: {freeGb:0.0} GB（{pct:0}%）。", $"System drive is low on space: {freeGb:0.0} GB ({pct:0}%) free."));
                else
                    Add(0, Cat.Storage, L($"システムドライブの空き: {freeGb:0.0} GB（{pct:0}%）", $"System drive free space: {freeGb:0.0} GB ({pct:0}%)"));
            }
            catch { }

            foreach (var d in Wmi("SELECT FriendlyName,HealthStatus,OperationalStatus,MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                int h = Convert.ToInt32(d["HealthStatus"] ?? 0);
                if (h != 0)
                    Add(3, Cat.Storage, L($"ディスク「{d["FriendlyName"]}」の健康状態が{(h == 1 ? "警告" : "異常")}です。バックアップの上、交換を検討してください。",
                                          $"Disk \"{d["FriendlyName"]}\" health is {(h == 1 ? "Warning" : "Unhealthy")}. Back up your data and consider replacing it."));
                else
                    Add(0, Cat.Storage, L($"ディスク「{d["FriendlyName"]}」の健康状態: 正常", $"Disk \"{d["FriendlyName"]}\" health: Healthy"));
            }
            foreach (var c in Wmi("SELECT DeviceId,Temperature,TemperatureMax,Wear,ReadErrorsUncorrected,WriteErrorsUncorrected,PowerOnHours FROM MSFT_StorageReliabilityCounter", @"root\Microsoft\Windows\Storage"))
            {
                double wear = D(c["Wear"]), temp = D(c["Temperature"]), rerr = D(c["ReadErrorsUncorrected"]), werr = D(c["WriteErrorsUncorrected"]);
                string id = L($"ディスク #{c["DeviceId"]}", $"Disk #{c["DeviceId"]}");
                if (rerr > 0 || werr > 0)
                    Add(3, Cat.Storage, L($"{id}: 訂正不能な読み書きエラーが記録されています（読み {rerr} / 書き {werr}）。", $"{id}: uncorrectable read/write errors recorded (read {rerr} / write {werr})."));
                if (wear >= 90) Add(3, Cat.Storage, L($"{id}: SSD の消耗度が {wear}% です。", $"{id}: SSD wear level is {wear}%."));
                else if (wear >= 70) Add(2, Cat.Storage, L($"{id}: SSD の消耗度が {wear}% です。", $"{id}: SSD wear level is {wear}%."));
                if (temp >= 70)
                    Add(2, Cat.Hardware, L($"{id}: 温度が {temp}℃ と高めです（ディスクのサーマルスロットリングで I/O が止まることがあります）。",
                                           $"{id}: temperature is high at {temp}°C (disk thermal throttling can stall I/O)."));
                if (wear > 0 || temp > 0)
                    Add(0, Cat.Storage, L($"{id}: 消耗度 {wear}% / 温度 {temp}℃ / 通電 {c["PowerOnHours"]} 時間", $"{id}: wear {wear}% / temperature {temp}°C / power-on {c["PowerOnHours"]} hours"));
            }

            // ---- メモリ ----
            foreach (var os in Wmi("SELECT TotalVisibleMemorySize,FreePhysicalMemory,TotalVirtualMemorySize,FreeVirtualMemory,LastBootUpTime FROM Win32_OperatingSystem"))
            {
                double totalMb = D(os["TotalVisibleMemorySize"]) / 1024, commitTotal = D(os["TotalVirtualMemorySize"]), commitFree = D(os["FreeVirtualMemory"]);
                double gb = totalMb / 1024;
                if (totalMb > 0 && totalMb < 7000)
                    Add(2, Cat.Memory, L($"物理メモリが {gb:0.0} GB です。現在の Windows とブラウザー/Office/Teams の併用では不足しやすく、ディスクへのページングで固まったように見えることがあります。",
                                         $"Physical memory is {gb:0.0} GB. That is often not enough for current Windows with browsers / Office / Teams; paging to disk can look like a hang."));
                else if (totalMb > 0 && totalMb < 15000)
                    Add(1, Cat.Memory, L($"物理メモリ: {gb:0.0} GB（多くのアプリを同時に使うと不足することがあります）", $"Physical memory: {gb:0.0} GB (may run short with many apps open)"));
                if (commitTotal > 0)
                {
                    double pct = 100 * (1 - commitFree / commitTotal), limGb = commitTotal / 1048576;
                    if (pct >= 85)
                        Add(3, Cat.Memory, L($"現在のコミット使用率が {pct:0}% です（上限 {limGb:0.0} GB）。メモリを大量に使っているプロセスを確認してください。",
                                             $"Current commit charge is {pct:0}% (limit {limGb:0.0} GB). Check which processes are using a lot of memory."));
                    else
                        Add(0, Cat.Memory, L($"現在のコミット使用率: {pct:0}%（上限 {limGb:0.0} GB）", $"Current commit charge: {pct:0}% (limit {limGb:0.0} GB)"));
                }
                var boot = WmiTime(os["LastBootUpTime"]);
                if (boot.HasValue)
                {
                    var up = DateTime.Now - boot.Value;
                    if (up.TotalDays >= 14)
                        Add(1, Cat.Memory, L($"最後の起動から {Util.Dur(up)} 経過しています。長時間の連続稼働はメモリ/ハンドルのリークがたまる原因になります（高速スタートアップ有効時は「シャットダウン」しても再起動になりません）。",
                                             $"{Util.Dur(up)} since the last boot. Long uptime lets memory/handle leaks accumulate (with Fast Startup, \"Shut down\" is not a real restart)."));
                }
            }
            bool autoPf = Wmi("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").Any(c => c["AutomaticManagedPagefile"] is bool b && b);
            if (!autoPf && Wmi("SELECT Name FROM Win32_PageFileUsage").Count == 0)
                Add(2, Cat.Memory, L("ページファイルがありません。メモリ不足でアプリやシステムが停止しやすくなり、ダンプも作成できません。",
                                     "There is no page file. Apps and the system stall more easily under memory pressure, and crash dumps cannot be written."));

            // ---- デバイス / ドライバー ----
            foreach (var p in Wmi("SELECT Name,ConfigManagerErrorCode,PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
            {
                int code = Convert.ToInt32(p["ConfigManagerErrorCode"]);
                if (code == 22 || code == 45) continue; // 無効化 / 未接続
                Add(2, Cat.Driver, L($"問題のあるデバイス: {p["Name"]}（{p["PNPClass"]}、エラーコード {code}）", $"Problem device: {p["Name"]} ({p["PNPClass"]}, error code {code})"));
            }
            foreach (var v in Wmi("SELECT Name,DriverDate,DriverVersion FROM Win32_VideoController"))
            {
                var dt = WmiTime(v["DriverDate"]);
                if (dt.HasValue && (DateTime.Now - dt.Value).TotalDays > 730)
                    Add(1, Cat.Gpu, L($"GPU ドライバーが古いです: {v["Name"]} {v["DriverVersion"]}（{dt:yyyy-MM-dd}）", $"GPU driver is old: {v["Name"]} {v["DriverVersion"]} ({dt:yyyy-MM-dd})"));
                if ((v["Name"]?.ToString() ?? "").IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0)
                    Add(2, Cat.Gpu, L("GPU が「Microsoft Basic Display Adapter」で動作しています（GPU ドライバー未導入/異常）。",
                                      "The GPU is running on \"Microsoft Basic Display Adapter\" (GPU driver missing or failed)."));
            }
            foreach (var b in Wmi("SELECT SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"))
            {
                var dt = WmiTime(b["ReleaseDate"]);
                if (dt.HasValue && (DateTime.Now - dt.Value).TotalDays > 1095)
                    Add(1, Cat.Hardware, L($"BIOS が古いです: {b["SMBIOSBIOSVersion"]}（{dt:yyyy-MM-dd}）。メーカーの更新を確認してください。",
                                           $"BIOS is old: {b["SMBIOSBIOSVersion"]} ({dt:yyyy-MM-dd}). Check for a vendor update."));
            }

            // ---- 電源 ----
            if (Util.RegS(Util.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") == "1")
                Add(0, Cat.Power, L("高速スタートアップが有効です（シャットダウン後もドライバーの状態が持ち越されます。切り分けでは「再起動」を使ってください）。",
                                    "Fast Startup is enabled (driver state carries over across shutdowns; use \"Restart\" when troubleshooting)."));

            // ---- セキュリティ製品 ----
            var avs = Wmi("SELECT displayName,productState FROM AntiVirusProduct", @"root\SecurityCenter2")
                .Select(a => (Name: a["displayName"]?.ToString(), On: ((Convert.ToUInt32(a["productState"] ?? 0u) >> 8) & 0x10) != 0)).ToList();
            var activeAv = avs.Where(a => a.On).Select(a => a.Name).ToList();
            if (activeAv.Count > 1)
                Add(2, Cat.Security, L($"リアルタイム保護が有効なウイルス対策が {activeAv.Count} 個あります: {string.Join(", ", activeAv)}。二重スキャンで I/O が極端に遅くなることがあります。",
                                       $"{activeAv.Count} antivirus products have real-time protection on: {string.Join(", ", activeAv)}. Double scanning can make I/O extremely slow."));
            else if (avs.Count > 0)
                Add(0, Cat.Security, L("ウイルス対策: ", "Antivirus: ") + string.Join(", ", avs.Select(a => a.Name + (a.On ? L("（有効）", " (on)") : L("（無効/パッシブ）", " (off/passive)")))));

            var flt = Util.Run(Util.Sys32("fltMC.exe"), "filters");
            if (flt.Code == 0)
            {
                var third = new List<string>();
                bool body = false;
                foreach (var line in flt.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.TrimStart().StartsWith("---", StringComparison.Ordinal)) { body = true; continue; }
                    if (!body) continue;
                    var name = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (name != null && !MsFilters.Contains(name)) third.Add(name);
                }
                if (third.Count > 0)
                    Add(1, Cat.Security, L($"Microsoft 以外の可能性があるファイルシステムフィルター ({third.Count}): {string.Join(", ", third)}。ファイルを開く/保存する時の停止に関わることがあります。",
                                           $"Possibly non-Microsoft file system filters ({third.Count}): {string.Join(", ", third)}. They can be involved in hangs when opening/saving files."));
            }

            // ---- 更新 ----
            bool reboot = false;
            foreach (var key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending" })
                using (var k = Util.HKLM.OpenSubKey(key)) reboot |= k != null;
            if (reboot)
                Add(2, Cat.Update, L("再起動待ちの更新があります。更新が中途半端な状態だと不安定になることがあるため、再起動を完了させてください。",
                                     "Updates are waiting for a restart. A half-applied update can cause instability; complete the restart."));

            // ---- 診断能力 ----
            var cde = Util.Reg(Util.HKLM, @"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled");
            if (!(cde is int c0) || c0 == 0)
                Add(1, null, L("メモリダンプが無効です。BugCheck が起きても原因ドライバーを特定できません。",
                               "Memory dumps are disabled. The faulting driver cannot be identified when a bugcheck occurs."));

            return r.OrderByDescending(f => f.Sev).ToList();
        }
    }
}
