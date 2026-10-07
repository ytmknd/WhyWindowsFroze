using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static WhyWindowsFroze.Loc;

namespace WhyWindowsFroze
{
    /// <summary>原因カテゴリ（表示名がそのままキーになるので、言語を決めた後に初期化される static readonly にしている）</summary>
    internal static class Cat
    {
        public static readonly string Gpu = L("GPU / 画面表示", "GPU / Display");
        public static readonly string Storage = L("ストレージ", "Storage");
        public static readonly string Memory = L("メモリ / リソース枯渇", "Memory / Resource exhaustion");
        public static readonly string Hardware = L("ハードウェア / 温度", "Hardware / Thermal");
        public static readonly string Power = L("電源 / スリープ", "Power / Sleep");
        public static readonly string App = L("アプリケーション", "Application");
        public static readonly string Shell = L("シェル / ログオン", "Shell / Logon");
        public static readonly string Auth = L("認証 (Entra ID / WAM)", "Authentication (Entra ID / WAM)");
        public static readonly string Network = L("ネットワーク", "Network");
        public static readonly string Security = L("セキュリティ製品", "Security software");
        public static readonly string Update = L("更新 / インストール", "Updates / Installs");
        public static readonly string Driver = L("ドライバー / OS", "Driver / OS");

        public static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            [Gpu] = L("GPU ドライバーを PC メーカー版または最新版に更新する。ドッキングステーション/複数モニター/外部ディスプレイの有無で切り分ける。アプリ側（Office、ブラウザー等）のハードウェアアクセラレーションを無効にして変化を見る。",
                      "Update the GPU driver (OEM or latest). Isolate by docking station / multiple monitors / external displays. Try disabling hardware acceleration in the apps (Office, browsers, etc.)."),
            [Storage] = L("SSD/HDD のファームウェアとストレージドライバー (Intel RST、NVMe 等) を更新し、メーカーの診断ツールで健康状態を確認する。空き容量を確保する。USB ストレージの抜き差しも疑う。",
                          "Update SSD/HDD firmware and storage drivers (Intel RST, NVMe, etc.) and check drive health with the vendor's diagnostic tool. Free up disk space. Also suspect USB storage being plugged/unplugged."),
            [Memory] = L("コミット枯渇やプール枯渇の時に大量のメモリを使っていたプロセス/ドライバーを特定する（2004 のメッセージ、タスクマネージャー、poolmon）。ページファイルを自動管理に戻す。物理メモリが少ない場合は増設を検討。",
                         "Identify the process/driver consuming memory at the time of commit or pool exhaustion (event 2004 message, Task Manager, poolmon). Set the page file back to system-managed. Consider adding RAM if it is small."),
            [Hardware] = L("メーカーのハードウェア診断とメモリ診断を実行し、BIOS/ファームウェアを更新する。冷却（ファン、ほこり、設置場所）と電源（AC アダプター、バッテリー）を確認する。",
                           "Run the vendor's hardware diagnostics and a memory test, and update BIOS/firmware. Check cooling (fans, dust, placement) and power (AC adapter, battery)."),
            [Power] = L("スリープ/モダンスタンバイ/復帰に関わるドライバー（チップセット、GPU、ネットワーク、USB、Bluetooth）と BIOS を更新する。切り分けとしてスリープや高速スタートアップを無効にする。",
                        "Update drivers involved in sleep / Modern Standby / resume (chipset, GPU, network, USB, Bluetooth) and the BIOS. To isolate, disable sleep and Fast Startup."),
            [App] = L("該当アプリを最新化/修復し、アドインや拡張機能を無効にして切り分ける。Office の場合はアドイン、ドキュメントキャッシュ、サインイン ID、ハードウェアアクセラレーションを確認する。",
                      "Update/repair the application and isolate by disabling add-ins and extensions. For Office, check add-ins, the document cache, signed-in identities and hardware acceleration."),
            [Shell] = L("Explorer のシェル拡張（右クリックメニュー等）、切断されたネットワークドライブ、スタートアップアプリ、ログオンスクリプト/GPO、プロファイルの破損を確認する。クリーンブートで切り分ける。",
                        "Check Explorer shell extensions (context menus, etc.), disconnected network drives, startup apps, logon scripts/GPOs and profile corruption. Isolate with a clean boot."),
            [Auth] = L("dsregcmd /status で PRT の状態を確認する。資格情報マネージャーと AAD BrokerPlugin のキャッシュのリセット、アカウントのサインインし直しを試す。",
                       "Check the PRT state with dsregcmd /status. Try resetting Credential Manager and the AAD BrokerPlugin cache, and signing in to the account again."),
            [Network] = L("切断されたネットワークドライブやファイルサーバーへの接続待ちで、Explorer や保存ダイアログが固まることがある。NIC/無線 LAN ドライバーを更新し、アダプターの省電力設定を無効にして切り分ける。",
                          "Explorer and save dialogs can hang while waiting for disconnected network drives or file servers. Update NIC/Wi-Fi drivers and disable the adapter's power saving to isolate."),
            [Security] = L("ウイルス対策/EDR/DLP 製品やファイルシステムフィルターが I/O を遅らせていないか確認する。ウイルス対策の二重稼働を解消し、ベンダーに既知の問題を問い合わせ、一時的な除外設定で切り分ける。",
                           "Check whether antivirus/EDR/DLP products or file system filters are delaying I/O. Avoid running two real-time antivirus products, ask the vendor about known issues, and isolate with temporary exclusions."),
            [Update] = L("直前に入った Windows Update、ドライバー、ソフトウェアを確認し、ロールバックやアンインストールで切り分ける。再起動待ちの更新があれば再起動を完了させる。",
                         "Review Windows Updates, drivers and software installed just before, and isolate by rolling back or uninstalling. Complete any pending restart."),
            [Driver] = L("BugCheck コードとダンプ (WinDbg の !analyze -v) から原因ドライバーを特定する。サービスの異常終了や起動失敗のイベントを確認する。",
                         "Identify the faulting driver from the bugcheck code and dump (WinDbg !analyze -v). Review service crash and start failure events."),
        };
    }

    /// <summary>再起動せずに回復した停止の起点として、そのイベントが示すもの</summary>
    internal enum FreezeLevel
    {
        None,
        /// <summary>アプリ単体の応答停止や I/O の遅延。PC 全体が止まったとは限らない</summary>
        Partial,
        /// <summary>画面表示やシステム全体の停止を示す（TDR、DWM、ライブカーネルイベント、メモリ枯渇）</summary>
        System,
    }

    /// <summary>フリーズ調査で注目すべき既知イベントの定義</summary>
    internal sealed class Signature
    {
        public string Log;          // null = 任意のログ
        public string[] Providers;  // null = 任意のプロバイダー
        public int[] Ids;           // null = 任意の ID
        public int MaxLevel = 99;   // 例: 2 ならエラー以上のみ
        public Regex MessageRegex;  // メッセージ条件
        public int Severity;        // 3=高 2=中 1=低 0=目印
        public string Category;     // 原因カテゴリ（null = 判定に使わない）
        public FreezeLevel Freeze;  // それ自体が停止を示す → 再起動なしで回復した停止の検出に使う
        public string Title;
        public string Hint;

        public bool IsMatch(EvtItem e)
        {
            if (Log != null && !string.Equals(Log, e.Log, StringComparison.OrdinalIgnoreCase)) return false;
            if (Providers != null && !Providers.Any(p => string.Equals(p, e.Provider, StringComparison.OrdinalIgnoreCase))) return false;
            if (Ids != null && Array.IndexOf(Ids, e.Id) < 0) return false;
            if (e.Level != 0 && e.Level > MaxLevel) return false;
            if (MessageRegex != null && !MessageRegex.IsMatch(e.Message ?? "")) return false;
            return true;
        }
    }

    internal static class Signatures
    {
        const RegexOptions RO = RegexOptions.IgnoreCase | RegexOptions.Compiled;

        public static readonly Regex OfficeRx = new Regex(
            @"WINWORD|EXCEL\.EXE|POWERPNT|OUTLOOK\.EXE|ONENOTE|MSACCESS|FileCoAuth|OneDrive\.exe|Microsoft\.AAD\.BrokerPlugin|OfficeClickToRun|msoia|ms-word:|ms-excel:|MSOSYNC|AppVLP|ms-teams|Teams\.exe", RO);

        static readonly Regex ShellRx = new Regex(@"explorer\.exe|ShellExperienceHost|StartMenuExperienceHost|SearchHost|SearchApp|TextInputHost|ctfmon|sihost|LogonUI|ShellHost", RO);
        static readonly Regex DwmRx = new Regex(@"dwm\.exe", RO);
        static readonly Regex CriticalRx = new Regex(@"lsass\.exe|csrss\.exe|wininit\.exe|winlogon\.exe|smss\.exe|services\.exe|svchost\.exe", RO);
        // LiveKernelEvent の P1 がライブダンプのコード（メッセージは OS の言語だが "P1:" の表記は共通）
        static readonly Regex GpuLiveKernelRx = new Regex(@"LiveKernelEvent.*?P1:\s*(141|117|193|1b0)\b", RO | RegexOptions.Singleline);
        static readonly Regex PowerLiveKernelRx = new Regex(@"LiveKernelEvent.*?P1:\s*(1a8|15f|15c|14f|9f)\b", RO | RegexOptions.Singleline);
        static readonly Regex Win32kLiveKernelRx = new Regex(@"LiveKernelEvent.*?P1:\s*(1a1|1a2)\b", RO | RegexOptions.Singleline);

        static readonly string[] StorageControllers =
        {
            "storahci", "stornvme", "iaStorA", "iaStorAC", "iaStorAVC", "iaStorVD", "RSTPCIESTOR", "nvme", "amd_sata", "amdsata", "LSI_SAS",
            "storvsc", "secnvme", "UASPStor", "USBSTOR", "Microsoft-Windows-StorPort", "amdxata", "iaVROC",
        };
        static readonly string[] GpuDrivers = { "nvlddmkm", "amdkmdag", "amdkmdap", "amdwddmg", "igfx", "igfxn", "igfxnd", "igfxCUIService2.0.0.0", "BasicDisplay" };
        static readonly string[] NetDrivers =
        {
            "Netwtw04", "Netwtw06", "Netwtw08", "Netwtw10", "Netwtw12", "Netwtw14", "Netwtw16", "e1dexpress", "e1iexpress", "e2fexpress", "e1rexpress",
            "rt640x64", "rtux64w10", "Rtlwlane", "rtwlane", "RtlWlanu", "athw10x", "Qcamain10x64", "mrvlpcie8897", "b57nd60a", "L1C",
        };

        static int[] I(params int[] ids) => ids;
        static string[] P(params string[] p) => p;

        static readonly string LiveKernelHint = L("C:\\Windows\\LiveKernelReports のダンプを確認してください。", "Check the dumps in C:\\Windows\\LiveKernelReports.");

        public static readonly List<Signature> All = new List<Signature>
        {
            // ======== 電源断 / BugCheck ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Kernel-Power"), Ids = I(41), Severity = 3,
                Title = L("予期しない電源断/再起動 (Kernel-Power 41)", "Unexpected power loss / restart (Kernel-Power 41)") },
            new Signature { Log = "System", Providers = P("EventLog"), Ids = I(6008), Severity = 2,
                Title = L("前回のシャットダウンは予期しないものでした", "The previous shutdown was unexpected") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-WER-SystemErrorReporting", "BugCheck"), Ids = I(1001), Severity = 3, Category = Cat.Driver,
                Title = L("BugCheck（ブルースクリーン）", "Bugcheck (blue screen)"),
                Hint = L("MEMORY.DMP / Minidump を WinDbg で解析してください。", "Analyze MEMORY.DMP / minidumps with WinDbg.") },

            // ======== GPU / 画面 ========
            new Signature { Log = "System", Providers = P("Display"), Ids = I(4101), Severity = 3, Category = Cat.Gpu, Freeze = FreezeLevel.System,
                Title = L("ディスプレイドライバーの応答停止と回復 (TDR)", "Display driver stopped responding and recovered (TDR)") },
            new Signature { Log = "System", Providers = GpuDrivers, MaxLevel = 2, Severity = 2, Category = Cat.Gpu,
                Title = L("GPU ドライバーのエラー", "GPU driver error") },
            new Signature { Log = "Application", Providers = P("Desktop Window Manager"), Ids = I(9009, 9020), Severity = 3, Category = Cat.Gpu, Freeze = FreezeLevel.System,
                Title = L("デスクトップ ウィンドウ マネージャー (DWM) の異常終了", "Desktop Window Manager (DWM) exited unexpectedly") },
            new Signature { Log = "Microsoft-Windows-DxgKrnl-Admin", MaxLevel = 3, Severity = 2, Category = Cat.Gpu,
                Title = L("グラフィックス カーネル (DxgKrnl) の警告/エラー", "Graphics kernel (DxgKrnl) warning/error") },

            // ======== ハードウェア ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-WHEA-Logger"), Ids = I(17, 19, 47), Severity = 2, Category = Cat.Hardware,
                Title = L("訂正済みハードウェアエラー (WHEA)", "Corrected hardware error (WHEA)"),
                Hint = L("多発する場合は PCIe 機器/メモリ/CPU の故障や BIOS の問題を疑います。", "If frequent, suspect a failing PCIe device / memory / CPU or a BIOS issue.") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-WHEA-Logger"), Severity = 3, Category = Cat.Hardware,
                Title = L("ハードウェアエラー (WHEA)", "Hardware error (WHEA)") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-MemoryDiagnostics-Results"), Ids = I(1102), Severity = 3, Category = Cat.Hardware,
                Title = L("Windows メモリ診断でエラーを検出", "Windows Memory Diagnostic detected errors") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-MemoryDiagnostics-Results"), Ids = I(1101), Severity = 0,
                Title = L("Windows メモリ診断: エラーなし", "Windows Memory Diagnostic: no errors") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Kernel-Processor-Power"), Ids = I(37), Severity = 1, Category = Cat.Hardware,
                Title = L("ファームウェアによる CPU 速度制限（温度/電源）", "CPU speed limited by firmware (thermal/power)") },
            new Signature { Log = "Microsoft-Windows-Kernel-Power/Thermal-Operational", MaxLevel = 3, Severity = 2, Category = Cat.Hardware,
                Title = L("温度による制限/警告", "Thermal throttling / warning") },

            // ======== ストレージ ========
            new Signature { Log = "System", Providers = P("disk"), Ids = I(153, 129), Severity = 3, Category = Cat.Storage, Freeze = FreezeLevel.Partial,
                Title = L("ディスク I/O の再試行/リセット", "Disk I/O retried / reset") },
            new Signature { Log = "System", Providers = P("disk"), Ids = I(7, 11, 51, 52, 154, 157), Severity = 3, Category = Cat.Storage,
                Title = L("ディスクのエラー/故障予測/切断", "Disk error / predicted failure / surprise removal") },
            new Signature { Log = "System", Providers = StorageControllers, Ids = I(9, 11, 129), Severity = 3, Category = Cat.Storage, Freeze = FreezeLevel.Partial,
                Title = L("ストレージ コントローラーのリセット/タイムアウト", "Storage controller reset / timeout") },
            new Signature { Log = "System", Providers = P("Ntfs", "Microsoft-Windows-Ntfs"), Ids = I(50, 55, 98, 137, 140), MaxLevel = 3, Severity = 2, Category = Cat.Storage,
                Title = L("ファイルシステムの異常 (NTFS)", "File system problem (NTFS)") },
            new Signature { Log = "System", Providers = P("Application Popup"), Ids = I(26), MessageRegex = new Regex("書き込み遅延|Delayed Write", RO), Severity = 3, Category = Cat.Storage, Freeze = FreezeLevel.Partial,
                Title = L("書き込み遅延エラー", "Delayed write failed") },
            new Signature { Log = "System", Providers = P("srv", "Srv"), Ids = I(2013), Severity = 2, Category = Cat.Storage,
                Title = L("ディスクの空き容量不足", "Low disk space") },
            new Signature { Log = "Microsoft-Windows-Storage-Storport/Operational", MaxLevel = 2, Severity = 2, Category = Cat.Storage,
                Title = L("Storport のエラー", "Storport error") },
            new Signature { Log = "Application", Providers = P("Wininit", "Chkdsk"), Ids = I(1001, 26212, 26214), Severity = 1, Category = Cat.Storage,
                Title = L("チェックディスク (chkdsk) の実行結果", "Check Disk (chkdsk) result") },

            // ======== メモリ / リソース ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Resource-Exhaustion-Detector"), Ids = I(2004), Severity = 3, Category = Cat.Memory, Freeze = FreezeLevel.System,
                Title = L("仮想メモリ不足 (コミット枯渇)", "Low virtual memory (commit exhaustion)"),
                Hint = L("メッセージに大量消費プロセスが書かれています。", "The message lists the processes consuming the most memory.") },
            new Signature { Log = "System", Providers = P("srv", "Srv"), Ids = I(2019, 2020), Severity = 3, Category = Cat.Memory, Freeze = FreezeLevel.System,
                Title = L("カーネルプールの枯渇（ドライバーのメモリリークの疑い）", "Kernel pool exhausted (suspected driver memory leak)") },
            new Signature { Log = "Microsoft-Windows-Resource-Exhaustion-Detector/Operational", Severity = 2, Category = Cat.Memory,
                Title = L("リソース枯渇の検出", "Resource exhaustion detected") },
            new Signature { Log = "Application", Providers = P("Microsoft-Windows-User Profile Service"), Ids = I(1530), Severity = 1, Category = Cat.Memory,
                Title = L("レジストリハンドルのリーク（プロファイル）", "Registry handle leak (user profile)") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Tcpip", "Tcpip"), Ids = I(4227, 4231, 4266), Severity = 2, Category = Cat.Network,
                Title = L("TCP ポートの枯渇", "TCP port exhaustion") },

            // ======== 電源 / スリープ ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Kernel-Power"), Ids = I(42, 506, 507, 107, 105, 566), Severity = 0, Category = Cat.Power,
                Title = L("電源状態の遷移（スリープ/スタンバイ/AC-DC）", "Power state transition (sleep / standby / AC-DC)") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Power-Troubleshooter"), Ids = I(1), Severity = 0, Category = Cat.Power,
                Title = L("スリープから復帰", "Resumed from sleep") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Kernel-Power"), Ids = I(137, 187), Severity = 1, Category = Cat.Power,
                Title = L("電源管理の警告", "Power management warning") },

            // ======== シェル / ログオン ========
            new Signature { Log = "Application", Providers = P("Microsoft-Windows-Winlogon"), Ids = I(6005, 6006), Severity = 2, Category = Cat.Shell, Freeze = FreezeLevel.Partial,
                Title = L("Winlogon 通知の処理が長時間かかった（ログオン/ロック/ログオフの停止）", "Winlogon notification took too long (logon / lock / logoff hang)") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Winlogon"), Ids = I(4005), Severity = 3, Category = Cat.Shell,
                Title = L("Winlogon の異常終了", "Winlogon terminated unexpectedly") },
            new Signature { Log = "Application", Providers = P("Microsoft-Windows-User Profile Service"), Ids = I(1500, 1511, 1515, 1521, 1542), Severity = 2, Category = Cat.Shell,
                Title = L("ユーザープロファイルの読み込み問題", "User profile load problem") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Wininit"), Ids = I(1015), Severity = 3, Category = Cat.Driver,
                Title = L("重要なシステムプロセスの終了", "Critical system process terminated") },

            // ======== アプリケーションの応答停止 / クラッシュ ========
            new Signature { Log = "Application", Providers = P("Application Hang"), Ids = I(1002), MessageRegex = DwmRx, Severity = 3, Category = Cat.Gpu, Freeze = FreezeLevel.System,
                Title = L("DWM（画面描画）の応答停止", "DWM (desktop composition) hang") },
            new Signature { Log = "Application", Providers = P("Application Hang"), Ids = I(1002), MessageRegex = ShellRx, Severity = 3, Category = Cat.Shell, Freeze = FreezeLevel.Partial,
                Title = L("シェル (Explorer/スタート/検索/IME) の応答停止", "Shell (Explorer / Start / Search / IME) hang") },
            new Signature { Log = "Application", Providers = P("Application Hang"), Ids = I(1002), MessageRegex = OfficeRx, Severity = 3, Category = Cat.App, Freeze = FreezeLevel.Partial,
                Title = L("Office/Teams 関連アプリの応答停止", "Office / Teams application hang") },
            new Signature { Log = "Application", Providers = P("Application Hang"), Ids = I(1002), Severity = 2, Category = Cat.App, Freeze = FreezeLevel.Partial,
                Title = L("アプリの応答停止", "Application hang") },
            new Signature { Log = "Application", Providers = P("Application Error"), Ids = I(1000), MessageRegex = DwmRx, Severity = 3, Category = Cat.Gpu,
                Title = L("DWM（画面描画）のクラッシュ", "DWM (desktop composition) crash") },
            new Signature { Log = "Application", Providers = P("Application Error"), Ids = I(1000), MessageRegex = ShellRx, Severity = 2, Category = Cat.Shell,
                Title = L("シェル (Explorer/スタート/検索/IME) のクラッシュ", "Shell (Explorer / Start / Search / IME) crash") },
            new Signature { Log = "Application", Providers = P("Application Error"), Ids = I(1000), MessageRegex = CriticalRx, Severity = 2, Category = Cat.Driver,
                Title = L("システムプロセスのクラッシュ", "System process crash") },
            new Signature { Log = "Application", Providers = P("Application Error"), Ids = I(1000), MessageRegex = OfficeRx, Severity = 2, Category = Cat.App,
                Title = L("Office/Teams 関連アプリのクラッシュ", "Office / Teams application crash") },
            new Signature { Log = "Application", Providers = P("Application Error"), Ids = I(1000), Severity = 1, Category = Cat.App,
                Title = L("アプリのクラッシュ", "Application crash") },
            new Signature { Log = "Application", Providers = P("Windows Error Reporting"), Ids = I(1001), MessageRegex = GpuLiveKernelRx, Severity = 3, Category = Cat.Gpu, Freeze = FreezeLevel.System,
                Title = L("ライブカーネルイベント（GPU のハング検出）", "Live kernel event (GPU hang detected)"), Hint = LiveKernelHint },
            new Signature { Log = "Application", Providers = P("Windows Error Reporting"), Ids = I(1001), MessageRegex = PowerLiveKernelRx, Severity = 3, Category = Cat.Power, Freeze = FreezeLevel.System,
                Title = L("ライブカーネルイベント（スタンバイ/復帰の遅延）", "Live kernel event (standby / resume timeout)"),
                Hint = L("モダンスタンバイからの復帰で画面が戻らない/固まる症状です。C:\\Windows\\LiveKernelReports のダンプを確認してください。",
                         "The screen does not come back or hangs when resuming from Modern Standby. Check the dumps in C:\\Windows\\LiveKernelReports.") },
            new Signature { Log = "Application", Providers = P("Windows Error Reporting"), Ids = I(1001), MessageRegex = Win32kLiveKernelRx, Severity = 3, Category = Cat.Shell, Freeze = FreezeLevel.System,
                Title = L("ライブカーネルイベント（画面/入力処理のハング検出）", "Live kernel event (desktop / input processing hang)"), Hint = LiveKernelHint },
            new Signature { Log = "Application", Providers = P("Windows Error Reporting"), Ids = I(1001), MessageRegex = new Regex("LiveKernelEvent", RO), Severity = 3, Category = Cat.Driver, Freeze = FreezeLevel.System,
                Title = L("ライブカーネルイベント（ドライバーのハング検出）", "Live kernel event (driver hang detected)"), Hint = LiveKernelHint },
            new Signature { Log = "Application", Providers = P("Windows Error Reporting"), Ids = I(1001), MessageRegex = OfficeRx, Severity = 2, Category = Cat.App,
                Title = L("Office/Teams の WER レポート", "Office / Teams WER report") },
            new Signature { Log = "Application", Providers = P(".NET Runtime"), Ids = I(1026), Severity = 1, Category = Cat.App,
                Title = L(".NET アプリの未処理例外", "Unhandled exception in a .NET application") },
            new Signature { Log = "OAlerts", Ids = I(300), Severity = 1, Category = Cat.App,
                Title = L("Office の警告ダイアログ表示", "Office alert dialog shown"),
                Hint = L("ダイアログが裏に隠れると、固まったように見えることがあります。", "A dialog hidden behind other windows can look like a hang.") },

            // ======== サービス / ドライバー ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-DistributedCOM"), Ids = I(10010), MessageRegex = new Regex("Office|Word|Excel|AAD|Broker|OneDrive", RO), Severity = 2, Category = Cat.App,
                Title = L("Office 関連 DCOM サーバーの起動タイムアウト", "Office-related DCOM server did not register in time") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-DistributedCOM"), Ids = I(10010), Severity = 1, Category = Cat.Driver,
                Title = L("DCOM サーバーの起動タイムアウト", "DCOM server did not register in time") },
            new Signature { Log = "System", Providers = P("Service Control Manager"), Ids = I(7031, 7034, 7023, 7024), Severity = 2, Category = Cat.Driver,
                Title = L("サービスの異常終了", "Service terminated unexpectedly") },
            new Signature { Log = "System", Providers = P("Service Control Manager"), Ids = I(7009, 7011, 7022), Severity = 2, Category = Cat.Driver,
                Title = L("サービスの応答タイムアウト", "Service response timeout") },
            new Signature { Log = "System", Providers = P("Service Control Manager"), Ids = I(7000, 7026), Severity = 1, Category = Cat.Driver,
                Title = L("サービス/ドライバーの起動失敗", "Service / driver failed to start") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Kernel-PnP"), Ids = I(219), Severity = 1, Category = Cat.Driver,
                Title = L("デバイスドライバーの読み込み失敗", "Device driver failed to load") },
            new Signature { Log = "System", Providers = P("volmgr"), Ids = I(45, 46, 49, 161), Severity = 1,
                Title = L("クラッシュダンプを作成できない構成", "Crash dump cannot be created"),
                Hint = L("ページファイルとダンプの設定を確認してください。", "Check the page file and dump settings.") },

            // ======== ネットワーク ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-NDIS"), Ids = I(10317, 10400, 10401), Severity = 2, Category = Cat.Network,
                Title = L("ネットワークアダプターのリセット/応答なし", "Network adapter reset / not responding") },
            new Signature { Log = "System", Providers = NetDrivers, MaxLevel = 2, Severity = 1, Category = Cat.Network,
                Title = L("ネットワークドライバーのエラー", "Network driver error") },
            new Signature { Log = "Microsoft-Windows-SMBClient/Connectivity", MaxLevel = 3, Severity = 2, Category = Cat.Network,
                Title = L("ファイルサーバー (SMB) との接続の問題", "File server (SMB) connectivity problem") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-SMBClient", "Mup"), MaxLevel = 2, Severity = 1, Category = Cat.Network,
                Title = L("ファイル共有 (SMB) のエラー", "File sharing (SMB) error") },

            // ======== 認証 ========
            new Signature { Log = "Microsoft-Windows-AAD/Operational", MaxLevel = 2, Severity = 2, Category = Cat.Auth,
                Title = L("Entra ID / WAM 認証エラー", "Entra ID / WAM authentication error") },

            // ======== セキュリティ製品 ========
            // コード整合性のブロックはブラウザー等で日常的に出るため重要度は低め
            new Signature { Log = "Microsoft-Windows-CodeIntegrity/Operational", Ids = I(3033, 3034, 3063, 3077), Severity = 1, Category = Cat.Security,
                Title = L("コード整合性によるモジュール読み込みブロック", "Module load blocked by Code Integrity") },
            new Signature { Log = "Microsoft-Windows-Windows Defender/Operational", Ids = I(1116, 1117, 1118, 1119), Severity = 2, Category = Cat.Security,
                Title = L("Defender のマルウェア検出/対処", "Defender malware detection / action") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-FilterManager"), Ids = I(3, 6), Severity = 2, Category = Cat.Security,
                Title = L("ファイルシステムフィルターの読み込み/アタッチ失敗", "File system filter failed to load / attach") },

            // ======== 更新 / インストール ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-WindowsUpdateClient"), Ids = I(20), Severity = 1, Category = Cat.Update,
                Title = L("Windows Update のインストール失敗", "Windows Update installation failure") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-WindowsUpdateClient"), Ids = I(19), Severity = 1, Category = Cat.Update,
                Title = L("Windows Update のインストール", "Windows Update installed") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-UserPnp"), Ids = I(20001, 20003), Severity = 1, Category = Cat.Update,
                Title = L("デバイスドライバーのインストール/更新", "Device driver installed / updated") },
            new Signature { Log = "System", Providers = P("Service Control Manager"), Ids = I(7045), Severity = 1, Category = Cat.Update,
                Title = L("新しいサービス/ドライバーの登録", "New service / driver installed") },
            new Signature { Log = "Application", Providers = P("MsiInstaller"), Ids = I(11707, 11724, 1033, 1034), Severity = 1, Category = Cat.Update,
                Title = L("ソフトウェアのインストール/削除", "Software installed / removed") },

            // ======== 目印 ========
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Kernel-General"), Ids = I(12), Severity = 0, Title = L("OS 起動", "OS started") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Kernel-General"), Ids = I(13), Severity = 0, Title = L("OS シャットダウン", "OS shut down") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Winlogon"), Ids = I(7001), Severity = 0, Title = L("ユーザー ログオン", "User logon") },
            new Signature { Log = "System", Providers = P("Microsoft-Windows-Winlogon"), Ids = I(7002), Severity = 0, Title = L("ユーザー ログオフ", "User logoff") },
            new Signature { Log = "Security", Ids = I(4688), MessageRegex = OfficeRx, Severity = 1, Category = Cat.App,
                Title = L("Office 関連プロセスの起動", "Office-related process started") },
            new Signature { Log = "Security", Ids = I(4688), Severity = 0,
                Title = L("プロセスの起動", "Process started") },
        };

        public static Signature Match(EvtItem e) => All.FirstOrDefault(s => s.IsMatch(e));

        /// <summary>期間全体を走査する対象（重要度 1 以上の System / Application シグネチャ）の EventID</summary>
        public static int[] GlobalIds(string log) =>
            All.Where(s => s.Severity >= 1 && s.Log == log && s.Ids != null).SelectMany(s => s.Ids).Distinct().OrderBy(i => i).ToArray();

        /// <summary>ID を限定しない（プロバイダー単位の）シグネチャ</summary>
        public static string[] GlobalProviders(string log) =>
            All.Where(s => s.Severity >= 1 && s.Log == log && s.Ids == null && s.Providers != null).SelectMany(s => s.Providers).Distinct().ToArray();

        // ---- BugCheck コード ----
        static readonly Dictionary<long, (string Name, string Cat, string Desc)> BugChecks = new Dictionary<long, (string, string, string)>
        {
            [0x01] = ("APC_INDEX_MISMATCH", Cat.Driver, L("ドライバーの不整合", "Driver inconsistency")),
            [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", Cat.Driver, L("ドライバーの不正なメモリアクセス", "Invalid memory access by a driver")),
            [0x19] = ("BAD_POOL_HEADER", Cat.Driver, L("カーネルプールの破損（ドライバー）", "Kernel pool corruption (driver)")),
            [0x1A] = ("MEMORY_MANAGEMENT", Cat.Hardware, L("メモリ管理の異常（メモリ故障やドライバー）", "Memory management error (faulty RAM or driver)")),
            [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", Cat.Driver, L("カーネルモードの未処理例外", "Unhandled kernel-mode exception")),
            [0x24] = ("NTFS_FILE_SYSTEM", Cat.Storage, L("NTFS の異常（ディスク/ファイルシステム破損）", "NTFS error (disk / file system corruption)")),
            [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", Cat.Driver, L("システムサービス中の例外", "Exception during a system service")),
            [0x4E] = ("PFN_LIST_CORRUPT", Cat.Hardware, L("メモリ管理情報の破損（メモリ故障やドライバー）", "Memory manager data corrupted (faulty RAM or driver)")),
            [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", Cat.Driver, L("無効なメモリ参照（ドライバー/メモリ）", "Invalid memory reference (driver / RAM)")),
            [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", Cat.Storage, L("ページファイルからの読み込み失敗（ストレージ）", "Failed to read from the page file (storage)")),
            [0x7B] = ("INACCESSIBLE_BOOT_DEVICE", Cat.Storage, L("起動ディスクにアクセスできない", "Boot device inaccessible")),
            [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", Cat.Driver, L("システムスレッドの未処理例外", "Unhandled exception in a system thread")),
            [0x7F] = ("UNEXPECTED_KERNEL_MODE_TRAP", Cat.Hardware, L("予期しないカーネルトラップ（ハードウェア/ドライバー）", "Unexpected kernel trap (hardware / driver)")),
            [0x8E] = ("KERNEL_MODE_EXCEPTION_NOT_HANDLED", Cat.Driver, L("カーネルモードの未処理例外", "Unhandled kernel-mode exception")),
            [0x9C] = ("MACHINE_CHECK_EXCEPTION", Cat.Hardware, L("CPU のマシンチェック例外", "CPU machine check exception")),
            [0x9F] = ("DRIVER_POWER_STATE_FAILURE", Cat.Power, L("スリープ/復帰時にドライバーが電源要求に応答しない", "A driver did not respond to a power request during sleep/resume")),
            [0xA0] = ("INTERNAL_POWER_ERROR", Cat.Power, L("電源管理の内部エラー", "Internal power management error")),
            [0xBE] = ("ATTEMPTED_WRITE_TO_READONLY_MEMORY", Cat.Driver, L("読み取り専用メモリへの書き込み（ドライバー）", "Write to read-only memory (driver)")),
            [0xC2] = ("BAD_POOL_CALLER", Cat.Driver, L("不正なプール操作（ドライバー）", "Invalid pool operation (driver)")),
            [0xC4] = ("DRIVER_VERIFIER_DETECTED_VIOLATION", Cat.Driver, L("ドライバーの検証ツールが違反を検出", "Driver Verifier detected a violation")),
            [0xC5] = ("DRIVER_CORRUPTED_EXPOOL", Cat.Driver, L("ドライバーによるプール破損", "Pool corrupted by a driver")),
            [0xCA] = ("PNP_DETECTED_FATAL_ERROR", Cat.Driver, L("プラグアンドプレイの致命的エラー", "Fatal Plug and Play error")),
            [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", Cat.Driver, L("ドライバーの不正なメモリアクセス", "Invalid memory access by a driver")),
            [0xE2] = ("MANUALLY_INITIATED_CRASH", Cat.Driver, L("Ctrl+ScrollLock による手動クラッシュ（フリーズ中のダンプ）", "Manual crash via Ctrl+ScrollLock (dump taken during a hang)")),
            [0xEA] = ("THREAD_STUCK_IN_DEVICE_DRIVER", Cat.Gpu, L("ドライバー（主に GPU）が無限ループ", "A driver (usually GPU) is stuck in a loop")),
            [0xED] = ("UNMOUNTABLE_BOOT_VOLUME", Cat.Storage, L("起動ボリュームをマウントできない", "Boot volume cannot be mounted")),
            [0xEF] = ("CRITICAL_PROCESS_DIED", Cat.Driver, L("重要なシステムプロセスの終了", "A critical system process died")),
            [0xF4] = ("CRITICAL_OBJECT_TERMINATION", Cat.Storage, L("重要プロセスの終了（ストレージ I/O 失敗が多い）", "Critical process terminated (often storage I/O failure)")),
            [0xF7] = ("DRIVER_OVERRAN_STACK_BUFFER", Cat.Driver, L("ドライバーのバッファーオーバーラン", "Driver stack buffer overrun")),
            [0xFC] = ("ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", Cat.Driver, L("実行不可メモリの実行（ドライバー）", "Execution of non-executable memory (driver)")),
            [0x101] = ("CLOCK_WATCHDOG_TIMEOUT", Cat.Hardware, L("CPU コアが応答しない", "A CPU core is not responding")),
            [0x109] = ("CRITICAL_STRUCTURE_CORRUPTION", Cat.Driver, L("カーネル構造体の破損", "Kernel structure corruption")),
            [0x10E] = ("VIDEO_MEMORY_MANAGEMENT_INTERNAL", Cat.Gpu, L("GPU メモリ管理の異常", "GPU memory management error")),
            [0x113] = ("VIDEO_DXGKRNL_FATAL_ERROR", Cat.Gpu, L("グラフィックスカーネルの致命的エラー", "Fatal graphics kernel error")),
            [0x116] = ("VIDEO_TDR_FAILURE", Cat.Gpu, L("GPU ドライバーのリセットに失敗", "GPU driver reset failed")),
            [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", Cat.Gpu, L("GPU の応答タイムアウト", "GPU response timeout")),
            [0x119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", Cat.Gpu, L("GPU スケジューラーの異常", "GPU scheduler error")),
            [0x124] = ("WHEA_UNCORRECTABLE_ERROR", Cat.Hardware, L("訂正不能なハードウェアエラー", "Uncorrectable hardware error")),
            [0x12B] = ("FAULTY_HARDWARE_CORRUPTED_PAGE", Cat.Hardware, L("メモリの故障", "Faulty memory")),
            [0x133] = ("DPC_WATCHDOG_VIOLATION", Cat.Driver, L("ドライバーが長時間 CPU を占有（ストレージ/GPU/ネットワークのドライバーに多い）", "A driver held the CPU too long (often storage / GPU / network drivers)")),
            [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", Cat.Driver, L("カーネルデータの破損検出", "Kernel data corruption detected")),
            [0x13A] = ("KERNEL_MODE_HEAP_CORRUPTION", Cat.Driver, L("カーネルヒープの破損", "Kernel heap corruption")),
            [0x14F] = ("PDC_WATCHDOG_TIMEOUT", Cat.Power, L("モダンスタンバイ遷移のタイムアウト", "Modern Standby transition timeout")),
            [0x154] = ("UNEXPECTED_STORE_EXCEPTION", Cat.Storage, L("ストアの例外（ストレージ）", "Store exception (storage)")),
            [0x15F] = ("CONNECTED_STANDBY_WATCHDOG_TIMEOUT_LIVEDUMP", Cat.Power, L("モダンスタンバイのタイムアウト", "Modern Standby timeout")),
            [0x1A8] = ("CONNECTED_STANDBY_RESUME_WATCHDOG", Cat.Power, L("モダンスタンバイ復帰のタイムアウト", "Modern Standby resume timeout")),
            [0x1C8] = ("MANUALLY_INITIATED_POWER_BUTTON_HOLD", Cat.Driver, L("電源ボタン長押しで取得したダンプ（フリーズ中の状態）", "Dump taken by holding the power button (state during the hang)")),
        };

        public static (string Name, string Cat, string Desc) BugCheck(long code)
        {
            code &= ~0x10000000L; // 0x1000007E → 0x7E など
            return BugChecks.TryGetValue(code, out var v) ? v : (L("不明なコード", "Unknown code"), Cat.Driver, L("Microsoft のバグチェックコード一覧を参照してください", "See Microsoft's bug check code reference"));
        }
    }
}
