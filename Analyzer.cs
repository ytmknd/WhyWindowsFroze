using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using static WhyWindowsFroze.Loc;

namespace WhyWindowsFroze
{
    internal sealed class AnalyzeOptions
    {
        public int Days = 30;
        public int WindowMinutes = 30;
        public string OutBase;
        public bool SaveEvtx = true;
        public bool Zip = true;
        public bool DetectRecovered = true;
        public int MaxRecovered = 30;
        /// <summary>利用者が申告したフリーズの発生時刻（ログに残らなかったフリーズも、この時刻を基準に解析する）</summary>
        public DateTime? ReportedTime;
        public int MaxGlobalPerQuery = 10000;
        public int MaxWindowPerLog = 5000;
    }

    internal sealed class Finding
    {
        public int Sev;      // 3=高 2=中 1=低 0=参考
        public string Text;
        public string Category;
        public Finding(int sev, string text) { Sev = sev; Text = text; }
    }

    /// <summary>根拠が発生時刻に対してどこにあるか</summary>
    internal enum Phase
    {
        Before,      // 発生前のイベント
        Context,     // 発生時の状況（BugCheck、スリープ遷移中、復帰/起動/ログオン直後、直前の更新や起動アプリ）
        Trigger,     // 起点のイベント（症状そのもの）
        After,       // 発生後のイベント（結果として起きた可能性がある）
        Background,  // 現在の健全性（フリーズ当時の状態とは限らない）
    }

    /// <summary>原因候補（カテゴリごとの根拠とスコア）</summary>
    internal sealed class Cause
    {
        public string Category;
        public int Score;
        public Dictionary<Phase, int> PhaseScore = new Dictionary<Phase, int>();
        public Dictionary<string, int> Evidence = new Dictionary<string, int>();
        public string Hint => Cat.Hints.TryGetValue(Category, out var h) ? h : "";
        public string EvidenceText => string.Join(" / ", Evidence.Select(kv => kv.Value > 1 ? $"{kv.Key} ×{kv.Value}" : kv.Key));

        int P(Phase p) => PhaseScore.TryGetValue(p, out int v) ? v : 0;

        /// <summary>発生前と発生時の状況だけから得た点数（症状・発生後・現在の状態を除いた、独立した根拠）</summary>
        public int DirectScore => P(Phase.Before) + P(Phase.Context);

        /// <summary>3=高 2=中 1=低</summary>
        public int Confidence => DirectScore >= 8 ? 3 : DirectScore >= 3 ? 2 : 1;

        public string ConfidenceText => Confidence == 3 ? L("高", "High") : Confidence == 2 ? L("中", "Medium") : L("低", "Low");

        public string ConfidenceNote => Confidence > 1 ? "" :
            L("発生前の根拠が乏しく、症状・発生後のイベント・現在の状態が中心です。", "Little evidence from before the event; based mainly on the symptom, later events or the current state.");
    }

    internal enum IncidentKind
    {
        PowerLoss,   // Kernel-Power 41（BugCheck、強制電源断、電源喪失/リセット）
        Recovered,   // 再起動なしで回復した、画面/システム全体の停止を示すイベント（TDR、DWM、ライブカーネルイベント、メモリ枯渇）
        Partial,     // アプリの応答停止や I/O 遅延だけ（PC 全体が止まったとは限らない）
        Reported,    // 利用者が指定した時刻
    }

    internal sealed class Incident
    {
        public int No;
        public IncidentKind Kind;
        public DateTime Kp41Time;
        public Dictionary<string, string> Kp41 = new Dictionary<string, string>();
        /// <summary>PowerLoss: 次の OS 起動時刻 / Recovered: 解析する時間窓の終わり</summary>
        public DateTime BootStart;
        public bool BootEstimated;
        public DateTime? LastEvent;
        public string LastEventDesc;
        public EvtItem LastEventItem;
        public DateTime? PowerButton;
        public DateTime? SessionStart;   // 停止したセッションの OS 起動時刻
        public DateTime? FirstTrigger;   // Recovered: 最初のフリーズ兆候
        public List<EvtItem> Triggers = new List<EvtItem>();
        public string User;
        public DateTime WindowFrom;
        public List<EvtItem> Events = new List<EvtItem>();
        public List<(DateTime Time, string Exe)> PrefetchRuns = new List<(DateTime, string)>();
        public List<Finding> Findings = new List<Finding>();
        public List<Cause> Causes = new List<Cause>();
        public List<QueryStat> Stats = new List<QueryStat>();
        public bool AfterResume, AfterBoot, AfterLogon;

        public DateTime LastAlive => LastEvent ?? BootStart;
        /// <summary>基準時刻。電源断は最後の記録、回復型は最初の起点イベント、指定時刻はその時刻</summary>
        public DateTime When => Kind == IncidentKind.PowerLoss ? LastAlive : (FirstTrigger ?? LastAlive);

        public bool IsPowerLoss => Kind == IncidentKind.PowerLoss;

        /// <summary>電源ボタンが押されたことを OS が把握している</summary>
        public bool PowerButtonUsed => K("LongPowerButtonPressDetected") == "true" || PowerButton.HasValue;

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case IncidentKind.PowerLoss:
                        return BugCheckCode != 0 ? L("ブルースクリーン", "Blue screen")
                             : PowerButtonUsed ? L("強制電源断（電源ボタン）", "Forced power-off (power button)")
                             : L("予期しない電源断/リセット", "Unexpected power loss / reset");
                    case IncidentKind.Recovered: return L("画面/システムの一時停止（回復）", "Display / system hang (recovered)");
                    case IncidentKind.Partial: return L("アプリ応答停止・I/O 遅延", "App hang / I/O delay");
                    default: return L("指定時刻", "Reported time");
                }
            }
        }

        public string K(string key) => Kp41.TryGetValue(key, out var v) ? v : null;

        public long BugCheckCode => long.TryParse(K("BugcheckCode"), out long v) ? v : 0;

        public string BugCheckText
        {
            get
            {
                if (!IsPowerLoss) return "-";
                long c = BugCheckCode;
                return c == 0 ? L("なし", "None") : $"0x{c:X} {Signatures.BugCheck(c).Name}";
            }
        }

        public string RebootText => IsPowerLoss ? Util.T(BootStart) : Kind == IncidentKind.Reported ? "-" : L("なし（回復）", "None (recovered)");
        public string GapText => IsPowerLoss ? Util.Dur(BootStart - LastAlive) : "";
        public string TopConfidence => Causes.Count == 0 ? "" : Causes[0].ConfidenceText;
        public string TopCause => Causes.Count == 0 ? L("手がかりなし", "No clues") : Causes[0].Category;
        public string UserName => User == null ? null : User.Split(new[] { '（', '(' })[0].Trim();
        public string UserText => User ?? L("不明", "Unknown");
    }

    internal sealed class AnalysisResult
    {
        public List<Incident> Incidents;
        public List<EvtItem> Global;
        public List<Finding> Health;
        public List<InfoSection> Sys, Users;
        public List<DumpFile> Dumps;
        public List<(string Dir, DateTime Time)> Wer;
        public List<Prefetch.Entry> Prefetch;
        public List<string> Recommendations;
        public DateTime From, To;
        public int WindowMinutes;
        public string OutDir, ReportPath, ZipPath;
        public bool IsAdmin;
        public List<QueryStat> Coverage = new List<QueryStat>();
        /// <summary>回復型の候補総数と、そのうち詳しく解析した件数</summary>
        public Dictionary<IncidentKind, (int Total, int Analyzed)> Candidates = new Dictionary<IncidentKind, (int, int)>();

        public int Count(IncidentKind k) => Incidents.Count(i => i.Kind == k);

        public string CountText() =>
            L($"電源断/ブルースクリーン {Count(IncidentKind.PowerLoss)} 件、画面/システムの一時停止 {Count(IncidentKind.Recovered)} 件、アプリ応答停止・I/O 遅延 {Count(IncidentKind.Partial)} 件" +
              (Count(IncidentKind.Reported) > 0 ? $"、指定時刻 {Count(IncidentKind.Reported)} 件" : ""),
              $"power loss/blue screen: {Count(IncidentKind.PowerLoss)}, display/system hang: {Count(IncidentKind.Recovered)}, app hang/I/O delay: {Count(IncidentKind.Partial)}" +
              (Count(IncidentKind.Reported) > 0 ? $", reported time: {Count(IncidentKind.Reported)}" : ""));

        /// <summary>調査範囲の行: 目的とログごとにまとめる</summary>
        public List<(string Purpose, string Log, string Status, int Queries, int Count, int Truncated, string Note, bool Problem)> CoverageRows()
        {
            var rows = new List<(string, string, string, int, int, int, string, bool)>();
            foreach (var g in Coverage.GroupBy(s => (s.Purpose, s.Log)))
            {
                var errs = g.Where(s => s.Error != null).Select(s => s.Error).Distinct().ToList();
                int trunc = g.Count(s => s.Truncated), n = g.Count();
                bool missing = g.All(s => s.Missing);
                string status = errs.Count > 0 ? L("読み取り失敗", "Read failed") : trunc > 0 ? L("上限到達", "Limit reached") : missing ? L("ログなし", "Log not present") : "OK";
                var notes = new List<string>(errs);
                if (trunc > 0)
                {
                    var t = g.Where(s => s.Truncated).ToList();
                    notes.Add(n == 1
                        ? L($"上限 {t[0].Max} 件で打ち切り（取得できたのは {Util.T(t[0].Oldest)} ～ {Util.T(t[0].Newest)}）", $"Stopped at the limit of {t[0].Max} (retrieved {Util.T(t[0].Oldest)} - {Util.T(t[0].Newest)})")
                        : L($"{n} 回中 {trunc} 回が上限 {t[0].Max} 件で打ち切り", $"{trunc} of {n} queries stopped at the limit of {t[0].Max}"));
                }
                if (missing) notes.Add(L("この PC にはこのログがありません", "This log does not exist on this PC"));
                rows.Add((g.Key.Purpose, g.Key.Log, status, n, g.Sum(s => s.Count), trunc, string.Join(" / ", notes), errs.Count > 0 || trunc > 0));
            }
            return rows;
        }

        /// <summary>網羅性に関する注意（読めなかったログ、上限による打ち切り、解析しなかった候補、検出できない事象）</summary>
        public List<string> CoverageWarnings()
        {
            var w = new List<string>();
            if (!IsAdmin)
                w.Add(L("管理者として実行されていないため、Security ログと Prefetch を読めず、一部のログも読めない可能性があります。",
                        "Not running as administrator: the Security log and Prefetch cannot be read, and some other logs may be unreadable."));
            foreach (var r in CoverageRows().Where(r => r.Problem))
                w.Add($"{r.Purpose} / {r.Log}: {r.Status}（{r.Note}）");
            foreach (var kv in Candidates.Where(kv => kv.Value.Total > kv.Value.Analyzed))
            {
                var name = new Incident { Kind = kv.Key }.KindText;
                w.Add(L($"「{name}」の候補 {kv.Value.Total} 件のうち、新しい {kv.Value.Analyzed} 件だけを解析しました。",
                        $"Only the newest {kv.Value.Analyzed} of {kv.Value.Total} \"{name}\" candidates were analyzed."));
            }
            w.Add(L("再起動せずに回復し、イベントログに何も残らなかったフリーズは検出できません。発生時刻が分かる場合は［発生時刻を指定］で解析してください。",
                    "Freezes that recovered without a restart and left nothing in the event logs cannot be detected. If you know when it happened, analyze with [Specify time]."));
            return w;
        }
    }

    internal sealed class Analyzer
    {
        readonly AnalyzeOptions _o;
        readonly Action<string> _log;
        readonly CancellationToken _ct;

        public Analyzer(AnalyzeOptions o, Action<string> log, CancellationToken ct)
        {
            _o = o;
            _log = log ?? (_ => { });
            _ct = ct;
        }

        /// <summary>フリーズ直前の詳細を集めるログ（存在しないものは無視）</summary>
        public static readonly string[] WindowLogs =
        {
            "System", "Application", "Security", "OAlerts",
            "Microsoft-Windows-Resource-Exhaustion-Detector/Operational",
            "Microsoft-Windows-Diagnostics-Performance/Operational",
            "Microsoft-Windows-DxgKrnl-Admin", "Microsoft-Windows-DxgKrnl-Operational",
            "Microsoft-Windows-Storage-Storport/Operational",
            "Microsoft-Windows-Ntfs/Operational",
            "Microsoft-Windows-Kernel-PnP/Configuration",
            "Microsoft-Windows-SMBClient/Connectivity",
            "Microsoft-Windows-AAD/Operational",
            "Microsoft-Windows-CodeIntegrity/Operational",
            "Microsoft-Windows-Windows Defender/Operational",
            "Microsoft-Windows-Shell-Core/Operational",
            "Microsoft-Windows-Winlogon/Operational",
            "Microsoft-Windows-User Profile Service/Operational",
            "Microsoft-Windows-Kernel-Power/Thermal-Operational",
        };

        static readonly string[] LastAliveLogs = { "System", "Application", "Security" };

        void Step(string msg) => _log("[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + msg);

        readonly List<QueryStat> _cov = new List<QueryStat>();

        QueryStat Stat(string purpose, DateTime from, DateTime to, Incident inc = null)
        {
            var s = new QueryStat { Purpose = purpose, From = from, To = to, Incident = inc };
            _cov.Add(s);
            inc?.Stats.Add(s);
            return s;
        }

        static string PurposeGlobal => L("期間内の既知イベント", "Known events in the period");
        static string PurposeKp41 => L("電源断の検索 (Kernel-Power 41)", "Power loss search (Kernel-Power 41)");
        static string PurposeWindow => L("各インシデントの時間窓", "Incident time windows");

        public AnalysisResult Run()
        {
            var now = DateTime.Now;
            var res = new AnalysisResult { From = now.AddDays(-_o.Days), To = now, WindowMinutes = _o.WindowMinutes, IsAdmin = Util.IsAdmin(), Coverage = _cov };
            res.OutDir = Path.Combine(string.IsNullOrWhiteSpace(_o.OutBase) ? DefaultBase() : _o.OutBase,
                $"WhyWindowsFroze_{Environment.MachineName}_{now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(res.OutDir);
            Step(L($"解析期間: {Util.T(res.From)} ～ {Util.T(now)}", $"Analysis period: {Util.T(res.From)} - {Util.T(now)}"));

            Step(L("健全性チェック（ディスク、メモリ、デバイス、ドライバー、セキュリティ製品、更新）", "Health check (disks, memory, devices, drivers, security software, updates)"));
            res.Health = Health.Check();

            Step(L("期間内の既知イベントを検索", "Searching for known events in the period"));
            res.Global = ScanSignatures(res.From, now);
            _log(L($"  {res.Global.Count} 件", $"  {res.Global.Count} found"));

            Step(L("電源断 / ブルースクリーン (Kernel-Power 41) を検索", "Searching for power losses / blue screens (Kernel-Power 41)"));
            var incidents = FindPowerLoss(res.From, now);
            _log(L($"  {incidents.Count} 件", $"  {incidents.Count} found"));

            Step(L("Prefetch からアプリの実行履歴を取得", "Reading app run history from Prefetch"));
            res.Prefetch = Prefetch.LoadAll();

            foreach (var inc in incidents)
            {
                _ct.ThrowIfCancellationRequested();
                Step(L($"電源断 ({Util.T(inc.Kp41Time)}) を解析", $"Analyzing power loss ({Util.T(inc.Kp41Time)})"));
                AnalyzePowerLoss(inc);
                AnalyzeCommon(inc, res);
            }

            if (_o.ReportedTime is DateTime at)
            {
                _ct.ThrowIfCancellationRequested();
                Step(L($"指定された時刻 ({Util.T(at)}) を解析", $"Analyzing the specified time ({Util.T(at)})"));
                var inc = new Incident
                {
                    Kind = IncidentKind.Reported,
                    FirstTrigger = at,
                    LastEvent = at,
                    LastEventDesc = L("利用者が指定した時刻", "Time specified by the user"),
                    BootStart = at.AddMinutes(5),
                };
                AnalyzeCommon(inc, res);
                incidents.Add(inc);
            }

            if (_o.DetectRecovered)
            {
                Step(L("再起動せずに回復した停止（画面停止、アプリの応答停止、I/O 遅延など）を検索", "Searching for hangs that recovered without a restart (display, app, I/O delays, etc.)"));
                var rec = FindRecovered(res.Global, incidents, res);
                foreach (var kv in res.Candidates)
                    _log(L($"  {new Incident { Kind = kv.Key }.KindText}: {kv.Value.Total} 件", $"  {new Incident { Kind = kv.Key }.KindText}: {kv.Value.Total} found") +
                         (kv.Value.Total > kv.Value.Analyzed ? L($"（新しい {kv.Value.Analyzed} 件のみ解析）", $" (only the newest {kv.Value.Analyzed} are analyzed)") : ""));
                foreach (var inc in rec)
                {
                    _ct.ThrowIfCancellationRequested();
                    Step(L($"{inc.KindText} ({Util.T(inc.When)}) を解析", $"Analyzing {inc.KindText} ({Util.T(inc.When)})"));
                    AnalyzeCommon(inc, res);
                }
                incidents.AddRange(rec);
            }

            foreach (var r in res.CoverageRows().Where(r => r.Problem))
                _log(L("  注意: ", "  Warning: ") + $"{r.Purpose} / {r.Log}: {r.Status}（{r.Note}）");

            incidents = incidents.OrderBy(i => i.When).ToList();
            for (int i = 0; i < incidents.Count; i++) incidents[i].No = i + 1;
            res.Incidents = incidents;

            _ct.ThrowIfCancellationRequested();
            Step(L("システム情報・ユーザー設定・ダンプを収集", "Collecting system info, user settings and dumps"));
            res.Sys = SystemInfo.Collect();
            res.Users = SystemInfo.CollectUsers();
            res.Dumps = SystemInfo.CollectDumps().Where(d => d.Time >= res.From.AddDays(-30)).ToList();
            res.Wer = SystemInfo.CollectWer().Where(w => w.Time >= res.From).ToList();
            res.Recommendations = Recommendations(res);

            _ct.ThrowIfCancellationRequested();
            Step(L("レポートとアーティファクトを保存", "Saving the report and artifacts"));
            SaveArtifacts(res);
            var report = new Report(res);
            res.ReportPath = Path.Combine(res.OutDir, "report.html");
            File.WriteAllText(res.ReportPath, report.Html(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(res.OutDir, "summary.txt"), report.Summary(), new UTF8Encoding(true));

            if (_o.Zip)
            {
                Step(L("ZIP を作成", "Creating ZIP"));
                var zip = res.OutDir.TrimEnd('\\') + ".zip";
                try
                {
                    if (File.Exists(zip)) File.Delete(zip);
                    ZipFile.CreateFromDirectory(res.OutDir, zip, CompressionLevel.Optimal, true);
                    res.ZipPath = zip;
                }
                catch (Exception ex) { _log(L("  ZIP の作成に失敗: ", "  Failed to create ZIP: ") + ex.Message); }
            }
            Step(L("完了: ", "Done: ") + res.OutDir);
            return res;
        }

        public static string DefaultBase()
        {
            string[] candidates =
            {
                // 単一ファイル exe では BaseDirectory が展開先になり得るため、exe 自身の場所を使う
                Path.GetDirectoryName(Environment.ProcessPath),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.GetTempPath(),
            };
            foreach (var c in candidates)
            {
                try
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    var probe = Path.Combine(c, ".whywindowsfroze_probe");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    return c.TrimEnd('\\');
                }
                catch { }
            }
            return Path.GetTempPath();
        }

        // ---------------- インシデント検出 ----------------

        List<Incident> FindPowerLoss(DateTime from, DateTime to)
        {
            var list = new List<Incident>();
            string xp = $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and {Evt.TimeRange(from, to)}]]";
            foreach (var r in Evt.Query("System", xp, stat: Stat(PurposeKp41, from, to)))
                list.Add(new Incident { Kind = IncidentKind.PowerLoss, Kp41Time = r.TimeCreated ?? DateTime.MinValue, Kp41 = Evt.Data(r) });
            return list.OrderBy(i => i.Kp41Time).ToList();
        }

        /// <summary>
        /// 停止を示すイベントを 5 分以内のまとまりにして、再起動を伴わない停止として扱う。
        /// 画面/システム全体の停止を示すイベントを含むまとまりは Recovered、アプリの応答停止や I/O 遅延だけなら Partial に分ける。
        /// </summary>
        List<Incident> FindRecovered(List<EvtItem> global, List<Incident> powerLoss, AnalysisResult res)
        {
            var triggers = global.Where(e => (e.Sig?.Freeze ?? FreezeLevel.None) != FreezeLevel.None).OrderBy(e => e.Time).ToList();
            var clusters = new List<List<EvtItem>>();
            foreach (var e in triggers)
            {
                if (clusters.Count > 0 && (e.Time - clusters[clusters.Count - 1].Last().Time).TotalMinutes <= 5)
                    clusters[clusters.Count - 1].Add(e);
                else
                    clusters.Add(new List<EvtItem> { e });
            }

            var list = new List<Incident>();
            foreach (var c in clusters)
            {
                DateTime start = c.First().Time, end = c.Last().Time;
                // 電源断や指定時刻の時間窓に入っているものは、そちらの一部として扱う
                if (powerLoss.Any(p => end >= p.WindowFrom && start <= p.BootStart)) continue;
                var last = c.Last();
                list.Add(new Incident
                {
                    Kind = c.Any(e => e.Sig.Freeze == FreezeLevel.System) ? IncidentKind.Recovered : IncidentKind.Partial,
                    FirstTrigger = start,
                    LastEvent = end,
                    LastEventDesc = $"{last.Log} / {last.Provider} / {last.Id}",
                    BootStart = end.AddMinutes(2),
                    Triggers = c,
                });
            }
            var result = new List<Incident>();
            foreach (var g in list.GroupBy(i => i.Kind))
            {
                var take = g.OrderByDescending(i => i.When).Take(_o.MaxRecovered).ToList();
                res.Candidates[g.Key] = (g.Count(), take.Count);
                result.AddRange(take);
            }
            return result;
        }

        void AnalyzePowerLoss(Incident inc)
        {
            // 起動時刻（これより前が電源断前のセッション）
            string kg = $"*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=12 and " +
                        $"TimeCreated[@SystemTime>='{Util.U(inc.Kp41Time.AddMinutes(-30))}' and @SystemTime<='{Util.U(inc.Kp41Time.AddMinutes(1))}']]]";
            var kgr = Evt.Query("System", kg, reverse: true, max: 1).FirstOrDefault();
            if (kgr != null)
                inc.BootStart = Util.ParseUtcIso(Evt.Data(kgr).TryGetValue("StartTime", out var st) ? st : null) ?? kgr.TimeCreated ?? inc.Kp41Time;
            else
            {
                inc.BootStart = inc.Kp41Time.AddMinutes(-2);
                inc.BootEstimated = true;
            }

            // 電源断前に最後に記録されたイベント
            string before = $"*[System[TimeCreated[@SystemTime<'{Util.U(inc.BootStart.AddSeconds(-1))}' and @SystemTime>='{Util.U(inc.BootStart.AddDays(-14))}']]]";
            foreach (var log in LastAliveLogs)
            {
                var r = Evt.Query(log, before, reverse: true, max: 1).FirstOrDefault();
                if (r?.TimeCreated != null && (inc.LastEvent == null || r.TimeCreated > inc.LastEvent))
                {
                    inc.LastEvent = r.TimeCreated;
                    inc.LastEventDesc = $"{log} / {r.ProviderName} / {r.Id}";
                }
            }

            // 電源ボタン押下時刻
            if (ulong.TryParse(inc.K("PowerButtonTimestamp"), out ulong pb) && pb != 0)
            {
                try
                {
                    var t = DateTime.FromFileTimeUtc((long)pb).ToLocalTime();
                    if (t > inc.BootStart.AddDays(-30) && t <= inc.BootStart) inc.PowerButton = t;
                }
                catch { }
            }
            inc.WindowFrom = inc.LastAlive.AddMinutes(-_o.WindowMinutes);
        }

        void AnalyzeCommon(Incident inc, AnalysisResult res)
        {
            if (!inc.IsPowerLoss)
                inc.WindowFrom = inc.When.AddMinutes(-_o.WindowMinutes);

            // 時間窓のイベント。上限に達したときに停止直前の記録が残るよう、新しい順に読む
            string tr = Evt.TimeRange(inc.WindowFrom, inc.BootStart);
            foreach (var log in WindowLogs)
            {
                _ct.ThrowIfCancellationRequested();
                string q = log == "Security" ? $"*[System[EventID=4688 and {tr}]]" : $"*[System[{tr}]]";
                foreach (var r in Evt.Query(log, q, reverse: true, max: _o.MaxWindowPerLog, stat: Stat(PurposeWindow, inc.WindowFrom, inc.BootStart, inc)))
                    inc.Events.Add(Evt.ToItem(r));
            }
            inc.Events = inc.Events.OrderBy(e => e.Time).ToList();
            inc.LastEventItem = inc.Kind == IncidentKind.Recovered || inc.Kind == IncidentKind.Partial
                ? inc.Triggers.LastOrDefault()
                : inc.Events.LastOrDefault(e => e.Log != "Security" && e.Time <= inc.When.AddSeconds(1));

            // 停止したセッションの起動時刻
            var boot = Evt.Query("System",
                $"*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=12 and TimeCreated[@SystemTime<'{Util.U(inc.When)}' and @SystemTime>='{Util.U(inc.When.AddDays(-60))}']]]",
                reverse: true, max: 1).FirstOrDefault();
            if (boot != null)
                inc.SessionStart = Util.ParseUtcIso(Evt.Data(boot).TryGetValue("StartTime", out var bs) ? bs : null) ?? boot.TimeCreated;

            // 直前のログオンユーザー
            string lx = $"*[System[Provider[@Name='Microsoft-Windows-Winlogon'] and EventID=7001 and " +
                        $"TimeCreated[@SystemTime<'{Util.U(inc.When)}' and @SystemTime>='{Util.U(inc.When.AddDays(-60))}']]]";
            var lr = Evt.Query("System", lx, reverse: true, max: 1).FirstOrDefault();
            if (lr != null && Evt.Data(lr).TryGetValue("UserSid", out var sid))
                inc.User = L($"{Util.SidToName(sid)}（{Util.T(lr.TimeCreated)} ログオン）", $"{Util.SidToName(sid)} (logged on {Util.T(lr.TimeCreated)})");

            // 直前に起動したアプリ (Prefetch)
            foreach (var e in res.Prefetch)
            {
                if (Prefetch.Noise.Contains(e.Exe)) continue;
                foreach (var t in e.RunTimes)
                    if (t >= inc.WindowFrom && t < inc.BootStart)
                        inc.PrefetchRuns.Add((t, e.Exe));
            }
            inc.PrefetchRuns = inc.PrefetchRuns.OrderBy(x => x.Time).ToList();

            ScoreCauses(inc, res);
            BuildFindings(inc);
        }

        static bool IsOfficeExe(string exe) =>
            Signatures.OfficeRx.IsMatch(exe) || exe.StartsWith("EXCEL", StringComparison.OrdinalIgnoreCase) ||
            exe.StartsWith("OUTLOOK", StringComparison.OrdinalIgnoreCase) || exe.StartsWith("MS-TEAMS", StringComparison.OrdinalIgnoreCase);

        // ---------------- 原因候補のスコアリング ----------------

        static string PhaseLabel(Phase p)
        {
            switch (p)
            {
                case Phase.Before: return L("[発生前] ", "[before] ");
                case Phase.Trigger: return L("[起点] ", "[symptom] ");
                case Phase.After: return L("[発生後] ", "[after] ");
                case Phase.Background: return L("[現在の状態] ", "[current state] ");
                default: return "";
            }
        }

        static string EventKey(EvtItem e) => $"{e.Time.Ticks}|{e.Log}|{e.Provider}|{e.Id}";

        void ScoreCauses(Incident inc, AnalysisResult res)
        {
            var d = new Dictionary<string, Cause>();
            void Add(string cat, int score, string evidence, Phase phase)
            {
                if (!d.TryGetValue(cat, out var c)) d[cat] = c = new Cause { Category = cat };
                c.Score += score;
                c.PhaseScore[phase] = (c.PhaseScore.TryGetValue(phase, out int ps) ? ps : 0) + score;
                var key = PhaseLabel(phase) + evidence;
                c.Evidence[key] = c.Evidence.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            // 基準時刻: 電源断は最後の記録、回復型は最初の起点イベント、指定時刻はその時刻
            var refT = inc.When;

            // 電源断の状態
            if (inc.IsPowerLoss)
            {
                long bc = inc.BugCheckCode;
                if (bc != 0 && bc != 0xE2 && bc != 0x1C8)
                {
                    var b = Signatures.BugCheck(bc);
                    Add(b.Cat, 12, L($"BugCheck 0x{bc:X} {b.Name}（{b.Desc}）", $"Bugcheck 0x{bc:X} {b.Name} ({b.Desc})"), Phase.Context);
                }
                if (inc.K("SleepInProgress") is string sip && sip != "0" && sip != "false")
                    Add(Cat.Power, 8, L($"スリープ遷移中に停止 (SleepInProgress={sip})", $"Stopped during a sleep transition (SleepInProgress={sip})"), Phase.Context);
                if (inc.K("ConnectedStandbyInProgress") == "true")
                    Add(Cat.Power, 8, L("モダンスタンバイ遷移中に停止", "Stopped during a Modern Standby transition"), Phase.Context);
            }

            // 起点のイベント（症状そのもの）。独立した根拠ではないので、種類ごとに 1 回だけ数える
            var triggerKeys = new HashSet<string>(inc.Triggers.Select(EventKey));
            var seenTrigger = new HashSet<string>();
            foreach (var t in inc.Triggers.Where(t => t.Sig?.Category != null))
                Add(t.Sig.Category, seenTrigger.Add(t.Sig.Title) ? (t.Severity >= 3 ? 3 : 2) : 0, t.Sig.Title, Phase.Trigger);

            // 時間窓の既知イベント。発生前は基準時刻に近いほど重く、発生後は結果として起きた可能性があるので軽く数える。
            // 同じ種類は新しい 3 件まで数え、頻発するノイズに引きずられないようにする
            var perTitle = new Dictionary<string, int>();
            foreach (var e in inc.Events.OrderByDescending(e => e.Time))
            {
                var cat = e.Sig?.Category;
                if (cat == null || e.Severity == 0 || triggerKeys.Contains(EventKey(e))) continue;
                bool before = e.Time <= refT.AddSeconds(1);
                var key = (before ? "B|" : "A|") + e.Sig.Title;
                perTitle[key] = perTitle.TryGetValue(key, out int seen) ? seen + 1 : 1;
                if (perTitle[key] > 3) continue;
                if (before)
                {
                    int w = e.Severity >= 3 ? 4 : e.Severity == 2 ? 2 : 1;
                    if ((refT - e.Time).TotalMinutes <= 5) w *= 2;
                    Add(cat, w, e.Sig.Title, Phase.Before);
                }
                else
                    Add(cat, 1, e.Sig.Title, Phase.After);
            }

            // 発生のパターン
            var resume = inc.Events.LastOrDefault(e => e.Time <= refT &&
                ((e.Provider == "Microsoft-Windows-Power-Troubleshooter" && e.Id == 1) ||
                 (e.Provider == "Microsoft-Windows-Kernel-Power" && (e.Id == 107 || e.Id == 507))));
            if (resume != null && (refT - resume.Time).TotalMinutes <= 10)
            {
                inc.AfterResume = true;
                var dur = Util.Dur(refT - resume.Time);
                Add(Cat.Power, 4, L($"スリープ/スタンバイ復帰の {dur} 後に停止", $"Stopped {dur} after resuming from sleep/standby"), Phase.Context);
            }
            if (inc.SessionStart.HasValue && (refT - inc.SessionStart.Value).TotalMinutes <= 15)
            {
                inc.AfterBoot = true;
                var dur = Util.Dur(refT - inc.SessionStart.Value);
                Add(Cat.Driver, 3, L($"OS 起動の {dur} 後に停止（起動時のドライバー/サービス/スタートアップアプリ）", $"Stopped {dur} after OS start (boot-time drivers / services / startup apps)"), Phase.Context);
            }
            var logon = inc.Events.LastOrDefault(e => e.Time <= refT && e.Provider == "Microsoft-Windows-Winlogon" && e.Id == 7001);
            if (logon != null && (refT - logon.Time).TotalMinutes <= 10)
            {
                inc.AfterLogon = true;
                var dur = Util.Dur(refT - logon.Time);
                Add(Cat.Shell, 2, L($"ログオンの {dur} 後に停止（スタートアップアプリ/ログオンスクリプト/GPO）", $"Stopped {dur} after logon (startup apps / logon scripts / GPO)"), Phase.Context);
            }

            // 直前に起動した Office（基準時刻より前のものだけ）
            foreach (var (t, exe) in inc.PrefetchRuns.Where(p => p.Time <= refT && IsOfficeExe(p.Exe)))
                Add(Cat.App, (refT - t).TotalMinutes <= 5 ? 4 : 2, L($"{exe} の起動 (Prefetch)", $"{exe} started (Prefetch)"), Phase.Context);

            // 直前 3 日以内に実際に入った更新/インストール（失敗は除く。種類ごとに 1 点、合計 3 点まで）
            foreach (var g in res.Global.Where(e => e.Sig?.Category == Cat.Update && e.Sig.Ids?.Contains(20) != true &&
                                                    e.Time < inc.WindowFrom && e.Time >= refT.AddDays(-3))
                                        .GroupBy(e => e.Sig.Title).Take(3))
            {
                var ago = Util.Dur(refT - g.Max(e => e.Time));
                Add(Cat.Update, 1, L($"3 日以内の{g.Key}（{g.Count()} 件、最新は {ago} 前）", $"{g.Key} within 3 days ({g.Count()}, latest {ago} before)"), Phase.Context);
            }

            // 停止前の最後の記録（電源断と指定時刻のみ。回復型では最後の記録は起点イベント自身なので数えない）
            if (inc.Kind != IncidentKind.Recovered && inc.Kind != IncidentKind.Partial &&
                inc.LastEventItem?.Sig?.Category is string lastCat && inc.LastEventItem.Severity >= 1)
                Add(lastCat, 3, L("停止前の最後の記録が該当", "The last record before the stop belongs here"), Phase.Before);

            // 現在の健全性の問題（フリーズ当時の状態とは限らないので背景要因として加点）
            foreach (var h in res.Health.Where(h => h.Sev >= 2 && h.Category != null))
                Add(h.Category, h.Sev, Util.Trunc(h.Text, 40), Phase.Background);

            inc.Causes = d.Values.Where(c => c.Score >= 2)
                .OrderByDescending(c => c.Confidence).ThenByDescending(c => c.Score).ToList();
        }

        // ---------------- 所見 ----------------

        static void BuildFindings(Incident inc)
        {
            var f = inc.Findings;
            var alive = inc.When;

            if (inc.Causes.Count > 0)
            {
                var top = inc.Causes[0];
                f.Add(new Finding(top.Confidence >= 2 ? 3 : 2, L($"最有力の原因候補: {top.Category}（確信度 {top.ConfidenceText}、スコア {top.Score}）根拠: {top.EvidenceText}",
                                                                 $"Most likely cause: {top.Category} (confidence {top.ConfidenceText}, score {top.Score}). Evidence: {top.EvidenceText}") +
                                                               (top.ConfidenceNote.Length > 0 ? " " + top.ConfidenceNote : "")));
                foreach (var c in inc.Causes.Skip(1).Take(3))
                    f.Add(new Finding(2, L($"他の候補: {c.Category}（確信度 {c.ConfidenceText}、スコア {c.Score}）根拠: {c.EvidenceText}",
                                           $"Other candidate: {c.Category} (confidence {c.ConfidenceText}, score {c.Score}). Evidence: {c.EvidenceText}")));
            }
            else
            {
                f.Add(new Finding(2, L("イベントログに直接の手がかりがありません。ログを残せないほどの急な停止（ハードウェア、ファームウェア、電源、カーネルのデッドロック）か、画面と入力だけの停止でエラーを記録しなかった可能性があります。",
                                       "No direct clues in the event logs. Either the system stopped too abruptly to log anything (hardware, firmware, power, kernel deadlock), or only the display/input froze without logging an error.")));
            }

            if (inc.Kind == IncidentKind.Recovered || inc.Kind == IncidentKind.Partial)
            {
                var triggers = string.Join(" / ", inc.Triggers.GroupBy(t => t.Sig.Title).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));
                if (inc.Kind == IncidentKind.Recovered)
                    f.Add(new Finding(2, L($"再起動せずに回復した、画面/システムの停止を示すイベントです（{Util.T(inc.FirstTrigger)} ～ {Util.T(inc.LastEvent)}）。起点: {triggers}",
                                           $"Events indicating a display/system hang that recovered without a restart ({Util.T(inc.FirstTrigger)} - {Util.T(inc.LastEvent)}). Symptom: {triggers}")));
                else
                    f.Add(new Finding(1, L($"アプリの応答停止または I/O の遅延です（{Util.T(inc.FirstTrigger)} ～ {Util.T(inc.LastEvent)}）。PC 全体が止まったとは限りません（応答しないアプリを閉じたときにも記録されます）。起点: {triggers}",
                                           $"An application hang or I/O delay ({Util.T(inc.FirstTrigger)} - {Util.T(inc.LastEvent)}). The whole PC did not necessarily stop (also logged when a non-responding app is closed). Symptom: {triggers}")));
                f.Add(new Finding(0, L("原因候補の [起点] は症状そのもの、[発生後] は結果として起きた可能性があるイベントで、独立した原因の根拠ではありません。確信度は [発生前] と発生時の状況だけから判定しています。",
                                       "[symptom] evidence is the symptom itself and [after] evidence may be a consequence; neither is independent evidence of the cause. Confidence is based only on [before] evidence and the circumstances.")));
                foreach (var t in inc.Triggers.Take(5))
                    f.Add(new Finding(1, $"{Util.T(t.Time)} {t.Sig.Title}: {Util.Trunc(Util.OneLine(t.Message), 200)}"));
            }
            else if (inc.Kind == IncidentKind.Reported)
            {
                f.Add(new Finding(1, L($"利用者が指定した時刻 {Util.T(inc.When)} を基準に、{Util.T(inc.WindowFrom)} ～ {Util.T(inc.BootStart)} のイベントを解析しました。指定時刻より後のイベントは [発生後] として軽く数えています。",
                                       $"Analyzed events from {Util.T(inc.WindowFrom)} to {Util.T(inc.BootStart)} around the user-specified time {Util.T(inc.When)}. Events after that time are counted lightly as [after].")));
            }
            else
            {
                long bc = inc.BugCheckCode;
                if (bc == 0xE2 || bc == 0x1C8)
                    f.Add(new Finding(3, L($"BugCheck 0x{bc:X}: フリーズ中に手動で取得したダンプです。MEMORY.DMP を WinDbg で解析してください（!analyze -v, !locks, !running -ti, !stacks 2）。",
                                           $"Bugcheck 0x{bc:X}: dump taken manually during the hang. Analyze MEMORY.DMP with WinDbg (!analyze -v, !locks, !running -ti, !stacks 2).")));
                else if (bc != 0)
                {
                    var b = Signatures.BugCheck(bc);
                    f.Add(new Finding(3, L($"BugCheck 0x{bc:X} {b.Name}: {b.Desc}。ダンプファイルで原因ドライバーを特定できます。",
                                           $"Bugcheck 0x{bc:X} {b.Name}: {b.Desc}. The dump file can identify the faulting driver.")));
                }
                else if (inc.PowerButtonUsed)
                    f.Add(new Finding(1, L("BugCheck なし: OS が電源ボタンの操作を記録しているため、電源ボタンによる強制電源断と考えられます（ダンプは残っていません）。",
                                           "No bugcheck: the OS recorded a power button action, so this was most likely a forced power-off with the power button (no dump was saved).")));
                else
                    f.Add(new Finding(1, L("BugCheck なし: OS が正常に終了しないまま再起動しました。電源ボタンの長押しのほか、停電・電源ユニット/AC アダプターの問題、バッテリー切れ、ハードウェアのリセット、ダンプを書けないほど急な停止も考えられます（ダンプは残っていません）。",
                                           "No bugcheck: the OS restarted without shutting down cleanly. Besides holding the power button, consider a power outage, PSU/AC adapter problems, a drained battery, a hardware reset, or a stop too abrupt to write a dump (no dump was saved).")));

                if (inc.K("LongPowerButtonPressDetected") == "true")
                    f.Add(new Finding(1, L("電源ボタンの長押しを検出しています。", "A long press of the power button was detected.")));
                if (inc.BootEstimated)
                    f.Add(new Finding(0, L("OS 起動イベント (Kernel-General 12) が見つからないため、起動時刻は推定値です。",
                                           "OS start event (Kernel-General 12) not found; the boot time is estimated.")));

                var gap = Util.Dur(inc.BootStart - alive);
                f.Add(new Finding(1, L($"電源断前の最後の記録: {Util.TMs(alive)}（{inc.LastEventDesc ?? "-"}）。次の起動まで {gap}。",
                                       $"Last record before the power loss: {Util.TMs(alive)} ({inc.LastEventDesc ?? "-"}). {gap} until the next boot.")));
                if ((inc.BootStart - alive).TotalMinutes > 10)
                    f.Add(new Finding(1, L($"解析は最後の記録 ({Util.T(alive)}) を基準にしています。実際に固まったのがこれより後（次の起動までの {gap} の間）なら、時間窓がずれて原因を捉えられていない可能性があります。発生時刻が分かる場合は［発生時刻を指定］で再解析してください。",
                                           $"The analysis is anchored at the last record ({Util.T(alive)}). If the freeze actually happened later (within the {gap} before the next boot), the time window may have missed the cause. If you know when it happened, re-run with [Specify time].")));
                if (inc.PowerButton.HasValue)
                {
                    var after = Util.Dur(inc.PowerButton.Value - alive);
                    f.Add(new Finding(1, L($"OS が電源ボタンの押下を {Util.T(inc.PowerButton)} に記録しています（最後の記録の {after} 後）。この時点でカーネルは入力を受け付けていたため、OS 全体ではなく画面/ユーザー操作系の停止だった可能性があります。",
                                           $"The OS recorded a power button press at {Util.T(inc.PowerButton)} ({after} after the last record). The kernel was still handling input, so this may have been a display/user-interaction freeze rather than a whole-OS hang.")));
                }
            }

            foreach (var st in inc.Stats.Where(st => st.Problem))
                f.Add(new Finding(2, st.Truncated
                    ? L($"時間窓の {st.Log} ログが上限 {st.Max} 件に達しました。新しい側を優先して取得したため、{Util.T(st.Oldest)} より前のイベントは含まれていません。",
                        $"The {st.Log} log hit the limit of {st.Max} events in the time window. Newer events were kept, so events before {Util.T(st.Oldest)} are not included.")
                    : L($"時間窓の {st.Log} ログを読めませんでした: {st.Error}", $"Could not read the {st.Log} log for the time window: {st.Error}")));

            if (inc.SessionStart.HasValue)
            {
                var up = Util.Dur(alive - inc.SessionStart.Value);
                f.Add(new Finding(0, L($"このセッションの起動: {Util.T(inc.SessionStart)}（停止まで {up}）", $"This session started: {Util.T(inc.SessionStart)} ({up} before the stop)")));
            }

            foreach (var (t, exe) in inc.PrefetchRuns.Skip(Math.Max(0, inc.PrefetchRuns.Count - 10)))
            {
                var ago = Util.Dur(alive - t);
                f.Add(new Finding(IsOfficeExe(exe) ? 2 : 1, t <= alive
                    ? L($"{exe} が基準時刻の {ago} 前 ({Util.T(t)}) に起動しています (Prefetch)。", $"{exe} started {ago} before the reference time ({Util.T(t)}) (Prefetch).")
                    : L($"{exe} が基準時刻の {Util.Dur(t - alive)} 後 ({Util.T(t)}) に起動しています (Prefetch)。", $"{exe} started {Util.Dur(t - alive)} after the reference time ({Util.T(t)}) (Prefetch).")));
            }
            foreach (var e in inc.Events.Where(e => e.Log == "Security" && e.Sig != null && e.Sig.Severity >= 1))
                f.Add(new Finding(2, L("監査ログ: ", "Audit log: ") + $"{Util.T(e.Time)} {Util.Trunc(e.Message, 300)}"));

            foreach (var g in inc.Events.Where(e => e.Severity >= 2 && e.Sig != null && e.Sig.Ids?.Contains(41) != true && e.Log != "Security")
                         .GroupBy(e => e.Sig.Title))
            {
                var last = g.Last();
                var ago = Util.Dur((alive - last.Time).Duration());
                bool after = last.Time > alive.AddSeconds(1);
                f.Add(new Finding(last.Sig.Severity, L($"{g.Key}: {g.Count()} 件（最後は基準時刻の {ago} {(after ? "後" : "前")}）", $"{g.Key}: {g.Count()} (latest {ago} {(after ? "after" : "before")} the reference time) ") +
                                                  Util.Trunc(Util.OneLine(last.Message), 200) + (last.Sig.Hint != null ? " → " + last.Sig.Hint : "")));
            }

            int errs = inc.Events.Count(e => e.Sig == null && e.Level <= 2 && e.Level > 0);
            if (errs > 0) f.Add(new Finding(1, L($"その他のエラー/重大イベント: {errs} 件（タイムラインを参照）", $"Other error/critical events: {errs} (see the timeline)")));

            inc.Findings = f.OrderByDescending(x => x.Sev).ToList();
        }

        // ---------------- 期間全体 ----------------

        List<EvtItem> ScanSignatures(DateTime from, DateTime to)
        {
            var list = new List<EvtItem>();
            foreach (var log in new[] { "System", "Application" })
            {
                // XPath の条件数が多すぎるとクエリが失敗するので分割する
                var conds = Signatures.GlobalIds(log).Select(i => "EventID=" + i)
                    .Concat(Signatures.GlobalProviders(log).Select(p => $"Provider[@Name='{p}']")).ToList();
                for (int i = 0; i < conds.Count; i += 15)
                {
                    string xp = $"*[System[({string.Join(" or ", conds.Skip(i).Take(15))}) and {Evt.TimeRange(from, to)}]]";
                    foreach (var r in Evt.Query(log, xp, reverse: true, max: _o.MaxGlobalPerQuery, stat: Stat(PurposeGlobal, from, to)))
                    {
                        _ct.ThrowIfCancellationRequested();
                        var it = Evt.ToItem(r);
                        if (it.Sig != null && it.Sig.Severity >= 1) list.Add(it);
                    }
                }
            }
            // 分割クエリ間の重複を除く
            return list.GroupBy(e => (e.Time, e.Log, e.Provider, e.Id, e.Message)).Select(g => g.First())
                .OrderByDescending(e => e.Time).ToList();
        }

        static List<string> Recommendations(AnalysisResult res)
        {
            var r = new List<string>();
            var incidents = res.Incidents;
            int n = incidents.Count;
            if (n > 0)
                r.Add(L($"期間内に検出: {res.CountText()}。「アプリ応答停止・I/O 遅延」は PC 全体の停止とは限りません。",
                        $"Detected in the period: {res.CountText()}. \"App hang / I/O delay\" does not necessarily mean the whole PC stopped."));

            // 複数のインシデントに共通する原因
            if (n > 0)
            {
                var ranking = incidents.SelectMany(i => i.Causes.Take(2).Select(c => c.Category)).GroupBy(c => c)
                    .Select(g => (Cat: g.Key, N: g.Count())).OrderByDescending(x => x.N).ToList();
                foreach (var (cat, k) in ranking.Take(3))
                    r.Add(L($"「{cat}」が {n} 件中 {k} 件で上位の原因候補です。対処: {Cat.Hints[cat]}",
                            $"\"{cat}\" is a top cause candidate in {k} of {n} incidents. What to do: {Cat.Hints[cat]}"));
            }

            // 発生パターン
            if (n >= 2)
            {
                int resume = incidents.Count(i => i.AfterResume), boot = incidents.Count(i => i.AfterBoot), logon = incidents.Count(i => i.AfterLogon);
                if (resume * 2 >= n) r.Add(L($"{n} 件中 {resume} 件がスリープ/スタンバイ復帰の直後です。スリープ関連のドライバーと BIOS を優先して確認してください。",
                                              $"{resume} of {n} occurred right after resuming from sleep/standby. Check sleep-related drivers and the BIOS first."));
                if (boot * 2 >= n) r.Add(L($"{n} 件中 {boot} 件が OS 起動直後です。スタートアップのアプリ/サービスとドライバーを確認してください。",
                                            $"{boot} of {n} occurred right after OS start. Check startup apps/services and drivers."));
                if (logon * 2 >= n) r.Add(L($"{n} 件中 {logon} 件がログオン直後です。ログオンスクリプト、GPO、スタートアップアプリ、ネットワークドライブを確認してください。",
                                             $"{logon} of {n} occurred right after logon. Check logon scripts, GPOs, startup apps and network drives."));

                var users = incidents.Where(i => i.UserName != null).GroupBy(i => i.UserName).OrderByDescending(g => g.Count()).ToList();
                if (users.Count == 1 && users[0].Count() >= 2)
                    r.Add(L($"すべてのインシデントが {users[0].Key} のログオン中に発生しています。複数のユーザーが使う PC なら、ユーザー固有の設定（アドイン、プロファイル、スタートアップ）を他のユーザーと比べてください。",
                            $"All incidents happened while {users[0].Key} was logged on. If several users share this PC, compare user-specific settings (add-ins, profile, startup) with other users."));

                var hours = incidents.GroupBy(i => i.When.Hour / 2).OrderByDescending(g => g.Count()).First();
                if (n >= 4 && hours.Count() * 2 >= n)
                    r.Add(L($"{n} 件中 {hours.Count()} 件が {hours.Key * 2}時～{hours.Key * 2 + 2}時に集中しています。その時間帯に動くタスク（ウイルススキャン、バックアップ、更新、特定の業務アプリ）を確認してください。",
                            $"{hours.Count()} of {n} are concentrated between {hours.Key * 2}:00 and {hours.Key * 2 + 2}:00. Check tasks running at that time (AV scans, backups, updates, specific business apps)."));
            }
            if (n > 0 && incidents.All(i => i.PrefetchRuns.Any(p => IsOfficeExe(p.Exe))))
                r.Add(L("すべてのインシデントで、直前に Office/Teams が起動しています。発生するユーザーとしないユーザーで「ユーザー設定」タブ（アドイン、サインイン ID、ドキュメントキャッシュ、ハードウェアアクセラレーション）を比べてください。",
                        "Office/Teams started just before every incident. Compare the \"User settings\" tab (add-ins, signed-in identities, document cache, hardware acceleration) between affected and unaffected users."));

            foreach (var h in res.Health.Where(h => h.Sev >= 3))
                r.Add(L("健全性チェック: ", "Health check: ") + h.Text);

            if (res.Dumps.Any())
                r.Add(L("ダンプファイルがあります。WinDbg で開いて !analyze -v を実行すると、原因ドライバーを特定できます。",
                        "Dump files exist. Open them in WinDbg and run !analyze -v to identify the faulting driver."));

            var hklm = Util.HKLM;
            var cde = Util.Reg(hklm, @"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled");
            if (!(cde is int c0) || c0 == 0 || c0 == 3)
                r.Add(L("メモリダンプが無効または最小ダンプのみです。［システムのプロパティ］→［起動と回復］で［自動メモリダンプ］にすると、次回 BugCheck 時の解析精度が上がります。",
                        "Memory dumps are disabled or small-dump only. Set \"Automatic memory dump\" in System Properties > Startup and Recovery for better analysis of the next bugcheck."));
            if (incidents.Any(i => i.Kind == IncidentKind.PowerLoss && i.BugCheckCode == 0) &&
                Util.RegS(hklm, @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters", "CrashOnCtrlScroll") != "1")
                r.Add(L("強制電源断ではダンプが残りません。kbdhid と i8042prt の Parameters に DWORD「CrashOnCtrlScroll=1」を設定して再起動すると、フリーズ中に［右 Ctrl を押したまま ScrollLock を 2 回］でダンプを取れます。",
                        "A forced power-off leaves no dump. Set DWORD \"CrashOnCtrlScroll=1\" under the kbdhid and i8042prt Parameters keys and restart; then you can take a dump during a hang by holding Right Ctrl and pressing Scroll Lock twice."));
            if (Util.IsAdmin() && !Evt.Query("Security", "*[System[EventID=4688]]", max: 1).Any())
                r.Add(L("プロセス作成の監査 (4688) が記録されていません。有効にすると（GPO: 詳細な監査ポリシー → プロセス作成、および「コマンドラインを含める」）、どのアプリ/ファイルを開いた直後に固まったかが分かります。",
                        "Process creation auditing (4688) is not being recorded. Enabling it (GPO: Advanced Audit Policy > Process Creation, plus \"Include command line\") shows which app/file was opened right before a hang."));
            return r;
        }

        // ---------------- 保存 ----------------

        void SaveArtifacts(AnalysisResult res)
        {
            foreach (var inc in res.Incidents)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Time,Log,Provider,EventID,Level,Severity,Category,Signature,Message");
                foreach (var e in inc.Events)
                    sb.AppendLine(string.Join(",", Util.Csv(Util.TMs(e.Time)), Util.Csv(e.Log), Util.Csv(e.Provider),
                        e.Id.ToString(CultureInfo.InvariantCulture), Util.Csv(e.LevelName), e.Severity.ToString(CultureInfo.InvariantCulture),
                        Util.Csv(e.Sig?.Category), Util.Csv(e.Sig?.Title), Util.Csv(e.Message)));
                File.WriteAllText(Path.Combine(res.OutDir, $"incident{inc.No:00}_events.csv"), sb.ToString(), new UTF8Encoding(true));
            }

            // WER レポート (Report.wer のみ。ダンプはサイズが大きいので一覧だけ)
            var wd = Path.Combine(res.OutDir, "wer");
            foreach (var (dir, _) in res.Wer.Take(100))
            {
                var rep = Path.Combine(dir, "Report.wer");
                if (!File.Exists(rep)) continue;
                Directory.CreateDirectory(wd);
                try { File.Copy(rep, Path.Combine(wd, Util.SafeFileName(Path.GetFileName(dir)) + ".wer"), true); } catch { }
            }

            if (!_o.SaveEvtx) return;
            Step(L("生イベントログ (.evtx) をエクスポート", "Exporting raw event logs (.evtx)"));
            var ed = Path.Combine(res.OutDir, "evtx");
            Directory.CreateDirectory(ed);
            string tr = Evt.TimeRange(res.From, res.To);
            foreach (var log in WindowLogs)
            {
                _ct.ThrowIfCancellationRequested();
                if (!Evt.LogExists(log)) continue;
                string q = log == "Security"
                    ? $"*[System[(EventID=4688 or EventID=4624 or EventID=4647 or EventID=4800 or EventID=4801 or EventID=4608 or EventID=1100) and {tr}]]"
                    : $"*[System[{tr}]]";
                string file = Path.Combine(ed, Util.SafeFileName(log.Replace('/', '_')) + ".evtx");
                try
                {
                    EventLogSession.GlobalSession.ExportLogAndMessages(log, PathType.LogName, q, file, false, CultureInfo.CurrentCulture);
                }
                catch (Exception ex)
                {
                    // .evtx 本体は出力済みで、メッセージ情報 (LocaleMetaData) の作成だけ失敗することがある
                    if (File.Exists(file) && new FileInfo(file).Length > 0) continue;
                    try { EventLogSession.GlobalSession.ExportLog(log, PathType.LogName, q, file); }
                    catch { _log($"  {log}: " + L("エクスポート失敗 ", "export failed ") + ex.Message); }
                }
            }
        }
    }
}
