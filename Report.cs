using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static WhyWindowsFroze.Loc;

namespace WhyWindowsFroze
{
    internal sealed class Report
    {
        readonly AnalysisResult _r;

        public Report(AnalysisResult r) { _r = r; }

        public static string ConfidenceExplain => L("確信度は [発生前] の根拠と発生時の状況（BugCheck、復帰/起動/ログオン直後など）だけから判定します。[起点]（症状そのもの）・[発生後]・[現在の状態] はスコアには含みますが、確信度には含みません。",
                                                     "Confidence is based only on [before] evidence and the circumstances (bugcheck, right after resume/boot/logon, etc.). [symptom], [after] and [current state] evidence count toward the score but not the confidence.");

        public static string SevLabel(int s) => s >= 3 ? L("高", "High") : s == 2 ? L("中", "Medium") : s == 1 ? L("低", "Low") : L("参考", "Info");

        string CountText() => L($"{_r.Incidents.Count} 件（{_r.CountText()}）", $"{_r.Incidents.Count} ({_r.CountText()})");

        public static string NoneText => L("期間内に、電源断・ブルースクリーン・画面/システムの一時停止・アプリ応答停止のいずれも検出されませんでした。",
                                            "No power loss, blue screen, display/system hang or app hang was detected in the period.");

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine(L($"===== WhyWindowsFroze 解析結果 ({Environment.MachineName}) =====", $"===== WhyWindowsFroze analysis result ({Environment.MachineName}) ====="));
            sb.AppendLine(L($"期間: {Util.T(_r.From)} ～ {Util.T(_r.To)}", $"Period: {Util.T(_r.From)} - {Util.T(_r.To)}"));
            sb.AppendLine(L("検出した事象: ", "Detected: ") + CountText());
            if (_r.Incidents.Count == 0) sb.AppendLine(NoneText);
            sb.AppendLine();
            sb.AppendLine(L("調査範囲の注意:", "Coverage notes:"));
            foreach (var w in _r.CoverageWarnings()) sb.AppendLine(" ! " + w);
            foreach (var i in _r.Incidents)
            {
                sb.AppendLine();
                sb.AppendLine(L($"#{i.No}  [{i.KindText}]  発生 {Util.T(i.When)}  再起動 {i.RebootText}  BugCheck {i.BugCheckText}  ユーザー {i.UserText}",
                                $"#{i.No}  [{i.KindText}]  occurred {Util.T(i.When)}  restart {i.RebootText}  bugcheck {i.BugCheckText}  user {i.UserText}"));
                foreach (var c in i.Causes.Take(3))
                    sb.AppendLine(L($"   原因候補 {c.Category} (確信度 {c.ConfidenceText}、スコア {c.Score}): {c.EvidenceText}", $"   Cause candidate {c.Category} (confidence {c.ConfidenceText}, score {c.Score}): {c.EvidenceText}"));
                foreach (var f in i.Findings.Where(f => f.Sev >= 1).Take(12))
                    sb.AppendLine($"   [{SevLabel(f.Sev)}] {f.Text}");
            }
            if (_r.Recommendations.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(L("推奨:", "Recommendations:"));
                foreach (var r in _r.Recommendations) sb.AppendLine(" - " + r);
            }
            return sb.ToString();
        }

        public string Html()
        {
            var h = new StringBuilder();
            h.Append($@"<!doctype html><html lang=""{L("ja", "en")}""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>{L("WhyWindowsFroze レポート", "WhyWindowsFroze report")}</title>");
            h.Append(@"<style>
:root{--bg:#fff;--fg:#1d1d1f;--muted:#6b6b70;--line:#dcdce0;--card:#f6f6f8;--hi:#c62828;--mid:#b26a00;--lo:#1565c0;--ref:#8a8a8f;--hlbg:#fff4e5}
@media (prefers-color-scheme:dark){:root{--bg:#161618;--fg:#e8e8ea;--muted:#9a9aa0;--line:#333338;--card:#202024;--hi:#ff6b6b;--mid:#ffb347;--lo:#6ab0ff;--ref:#8a8a8f;--hlbg:#3a2a12}}
body{background:var(--bg);color:var(--fg);font:14px/1.6 ""Segoe UI"",""Yu Gothic UI"",Meiryo,sans-serif;margin:0;padding:24px;max-width:1400px}
h1{font-size:22px;margin:0 0 4px}h2{font-size:18px;margin:32px 0 8px;border-bottom:2px solid var(--line);padding-bottom:4px}h3{font-size:15px;margin:20px 0 6px}
.muted{color:var(--muted)}.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px 16px;margin:8px 0}
table{border-collapse:collapse;width:100%;font-size:12.5px;margin:6px 0}th,td{border:1px solid var(--line);padding:3px 6px;text-align:left;vertical-align:top}
th{background:var(--card)}td.msg{white-space:pre-wrap;word-break:break-all;max-width:700px}
.s3{color:var(--hi);font-weight:600}.s2{color:var(--mid);font-weight:600}.s1{color:var(--lo)}.s0{color:var(--ref)}
tr.r3 td,tr.r2 td{background:var(--hlbg)}
.badge{display:inline-block;min-width:2.5em;text-align:center;border-radius:4px;padding:0 4px;font-size:12px;border:1px solid currentColor;margin-right:6px}
ul.f{list-style:none;padding:0;margin:0}ul.f li{margin:3px 0}
pre{background:var(--card);border:1px solid var(--line);padding:8px;overflow:auto;font-size:12px;white-space:pre-wrap}
.kv td:first-child{width:260px;color:var(--muted)}details>summary{cursor:pointer;margin:6px 0}
</style></head><body>");
            h.Append(L($"<h1>WhyWindowsFroze Windows フリーズ診断レポート</h1><div class=muted>{Util.H(Environment.MachineName)} ・ 解析期間 {Util.T(_r.From)} ～ {Util.T(_r.To)} ・ 時間窓 最終記録の {_r.WindowMinutes} 分前から ・ v{Program.Version}</div>",
                       $"<h1>WhyWindowsFroze Windows freeze diagnostic report</h1><div class=muted>{Util.H(Environment.MachineName)} · Period {Util.T(_r.From)} - {Util.T(_r.To)} · Window: {_r.WindowMinutes} min before the last record · v{Program.Version}</div>"));

            h.Append($"<h2>{L("概要", "Summary")}</h2>");
            if (_r.Incidents.Count == 0)
                h.Append($"<div class=card>{Util.H(NoneText)}{(_r.Coverage.Any(c => c.Problem) ? L("ただし、読めなかったログや上限で打ち切った検索があるため、検出漏れの可能性があります（「調査範囲」を参照）。", " However, some logs could not be read or searches stopped at a limit, so events may have been missed (see \"Coverage\").") : "")}</div>");
            else
            {
                h.Append($"<div class=muted>{Util.H(CountText())}</div>");
                foreach (var (title, list) in new[]
                {
                    (L("Windows 全体の停止（電源断・ブルースクリーン・画面/システムの一時停止・指定時刻）", "Whole-Windows stops (power loss, blue screen, display/system hang, reported time)"), _r.Incidents.Where(i => i.Kind != IncidentKind.Partial).ToList()),
                    (L("アプリ単体の応答停止・I/O 遅延（PC 全体が止まったとは限らない）", "App-only hangs / I/O delays (the whole PC did not necessarily stop)"), _r.Incidents.Where(i => i.Kind == IncidentKind.Partial).ToList()),
                })
                {
                    if (list.Count == 0) continue;
                    h.Append($"<h3>{Util.H(title)} ({list.Count})</h3>");
                    h.Append(L("<table><tr><th>#</th><th>種類</th><th>発生</th><th>再起動</th><th>空白</th><th>BugCheck</th><th>最有力の原因候補</th><th>確信度</th><th>直前のユーザー</th></tr>",
                               "<table><tr><th>#</th><th>Type</th><th>Occurred</th><th>Restart</th><th>Gap</th><th>Bugcheck</th><th>Most likely cause</th><th>Confidence</th><th>User</th></tr>"));
                    foreach (var i in list)
                        h.Append($"<tr><td><a href='#inc{i.No}'>#{i.No}</a></td><td>{Util.H(i.KindText)}</td><td>{Util.T(i.When)}</td><td>{Util.H(i.RebootText)}</td><td>{i.GapText}</td>" +
                                 $"<td>{Util.H(i.BugCheckText)}</td><td class=s{(i.Causes.Count > 0 ? 3 : 0)}>{Util.H(i.TopCause)}</td><td>{Util.H(i.TopConfidence)}</td><td>{Util.H(i.UserText)}</td></tr>");
                    h.Append("</table>");
                }
            }
            h.Append($"<div class=card><b>{L("調査範囲の注意", "Coverage notes")}</b><ul>");
            foreach (var w in _r.CoverageWarnings()) h.Append($"<li>{Util.H(w)}</li>");
            h.Append($"</ul>{L("詳細は下の「調査範囲」を参照してください。", "See \"Coverage\" below for details.")}</div>");
            if (_r.Recommendations.Count > 0)
            {
                h.Append($"<div class=card><b>{L("推奨事項", "Recommendations")}</b><ul>");
                foreach (var r in _r.Recommendations) h.Append($"<li>{Util.H(r)}</li>");
                h.Append("</ul></div>");
            }

            foreach (var i in _r.Incidents.OrderByDescending(i => i.When)) Incident(h, i);

            h.Append(L("<h2>調査範囲（読んだログ・件数・上限・失敗）</h2>", "<h2>Coverage (logs read, counts, limits, failures)</h2>"));
            h.Append(L("<table><tr><th>目的</th><th>ログ</th><th>状態</th><th>検索回数</th><th>取得件数</th><th>備考</th></tr>",
                       "<table><tr><th>Purpose</th><th>Log</th><th>Status</th><th>Queries</th><th>Events read</th><th>Notes</th></tr>"));
            foreach (var c in _r.CoverageRows())
                h.Append($"<tr class=r{(c.Problem ? 3 : 0)}><td>{Util.H(c.Purpose)}</td><td>{Util.H(c.Log)}</td><td class=s{(c.Problem ? 3 : 0)}>{Util.H(c.Status)}</td><td>{c.Queries}</td><td>{c.Count}</td><td>{Util.H(c.Note)}</td></tr>");
            h.Append("</table>");
            foreach (var kv in _r.Candidates)
                h.Append($"<div class=muted>{Util.H(new Incident { Kind = kv.Key }.KindText)}: {L($"候補 {kv.Value.Total} 件、解析 {kv.Value.Analyzed} 件", $"{kv.Value.Total} candidates, {kv.Value.Analyzed} analyzed")}</div>");

            h.Append(L("<h2>健全性チェック（現在の状態）</h2><table><tr><th>重要度</th><th>カテゴリ</th><th>内容</th></tr>",
                       "<h2>Health check (current state)</h2><table><tr><th>Severity</th><th>Category</th><th>Details</th></tr>"));
            foreach (var f in _r.Health)
                h.Append($"<tr class=r{f.Sev}><td class=s{f.Sev}>{SevLabel(f.Sev)}</td><td>{Util.H(f.Category)}</td><td>{Util.H(f.Text)}</td></tr>");
            h.Append("</table>");

            h.Append(L($"<h2>期間内の既知イベント（{_r.Global.Count} 件）</h2>", $"<h2>Known events in the period ({_r.Global.Count})</h2>"));
            if (_r.Global.Count > 0)
            {
                h.Append(L("<table><tr><th>シグネチャ</th><th>カテゴリ</th><th>件数</th><th>最新</th><th>重要度</th></tr>",
                           "<table><tr><th>Signature</th><th>Category</th><th>Count</th><th>Latest</th><th>Severity</th></tr>"));
                foreach (var g in _r.Global.GroupBy(e => e.Sig.Title).OrderByDescending(g => g.First().Sig.Severity).ThenByDescending(g => g.Count()))
                    h.Append($"<tr><td>{Util.H(g.Key)}</td><td>{Util.H(g.First().Sig.Category)}</td><td>{g.Count()}</td><td>{Util.T(g.Max(e => e.Time))}</td><td class=s{g.First().Sig.Severity}>{SevLabel(g.First().Sig.Severity)}</td></tr>");
                h.Append($"</table><details><summary>{L("一覧を表示（最新 500 件）", "Show list (latest 500)")}</summary>");
                EventTable(h, _r.Global.Take(500), null);
                h.Append("</details>");
            }

            h.Append(L("<h2>アプリの実行履歴 (Prefetch、最近実行されたもの 200 件)</h2>", "<h2>App run history (Prefetch, 200 most recent)</h2>"));
            if (_r.Prefetch.Count == 0)
                h.Append($"<div class=card>{L("取得できませんでした（管理者権限が必要、または Prefetch が無効）。", "Not available (requires administrator rights, or Prefetch is disabled).")}</div>");
            else
            {
                h.Append(L("<table><tr><th>実行ファイル</th><th>直近の実行時刻（最大 8 回）</th></tr>", "<table><tr><th>Executable</th><th>Recent run times (up to 8)</th></tr>"));
                foreach (var p in _r.Prefetch.OrderByDescending(p => p.RunTimes.Count > 0 ? p.RunTimes[0] : DateTime.MinValue).Take(200))
                    h.Append($"<tr><td>{Util.H(p.Exe)}<div class=muted>{Util.H(p.File)}</div></td><td>{(p.Error != null ? Util.H(L("解析失敗: ", "Parse failed: ") + p.Error) : Util.H(string.Join("  ", p.RunTimes.Select(t => Util.T(t)))))}</td></tr>");
                h.Append($"</table><div class=muted>{L("※ Prefetch は起動から約 10 秒後に記録されるため、起動直後にフリーズした場合は残らないことがあります。", "Note: Prefetch is written about 10 seconds after launch, so it may be missing if the freeze happened right after launch.")}</div>");
            }

            h.Append(L("<h2>ダンプファイル / WER レポート</h2>", "<h2>Dump files / WER reports</h2>"));
            if (_r.Dumps.Count == 0) h.Append($"<div class=card>{L("ダンプファイルはありません。", "No dump files.")}</div>");
            else
            {
                h.Append(L("<table><tr><th>種類</th><th>更新日時</th><th>サイズ</th><th>パス</th></tr>", "<table><tr><th>Type</th><th>Modified</th><th>Size</th><th>Path</th></tr>"));
                foreach (var d in _r.Dumps.Take(100))
                    h.Append($"<tr><td>{Util.H(d.Kind)}</td><td>{Util.T(d.Time)}</td><td>{d.Size / 1048576.0:0.0} MB</td><td>{Util.H(d.Path)}</td></tr>");
                h.Append("</table>");
            }
            if (_r.Wer.Count > 0)
            {
                h.Append(L($"<details><summary>WER レポート（カーネル/Office/UI 関連 {_r.Wer.Count} 件）</summary><table><tr><th>日時</th><th>フォルダー</th></tr>",
                           $"<details><summary>WER reports (kernel / Office / UI related: {_r.Wer.Count})</summary><table><tr><th>Date</th><th>Folder</th></tr>"));
                foreach (var w in _r.Wer.Take(300)) h.Append($"<tr><td>{Util.T(w.Time)}</td><td>{Util.H(w.Dir)}</td></tr>");
                h.Append("</table></details>");
            }

            h.Append(L("<h2>ユーザー別の設定（ユーザー依存要因の比較用）</h2>", "<h2>Per-user settings (to compare user-dependent factors)</h2>"));
            foreach (var s in _r.Users) Section(h, s);
            h.Append($"<h2>{L("システム情報", "System information")}</h2>");
            foreach (var s in _r.Sys) Section(h, s);

            h.Append(@"<script>
document.querySelectorAll('input.toggle').forEach(function(cb){cb.addEventListener('change',function(){
  document.querySelectorAll('#'+cb.dataset.t+' tr.r0').forEach(function(tr){tr.style.display=cb.checked?'':'none';});});});
</script></body></html>");
            return h.ToString();
        }

        void Incident(StringBuilder h, Incident i)
        {
            h.Append($"<h2 id='inc{i.No}'>#{i.No}　{Util.H(i.KindText)}　{Util.T(i.When)}</h2>");

            if (i.Causes.Count > 0)
            {
                h.Append(L("<table><tr><th>原因候補</th><th>確信度</th><th>スコア</th><th>根拠</th><th>対処の方向性</th></tr>",
                           "<table><tr><th>Cause candidate</th><th>Confidence</th><th>Score</th><th>Evidence</th><th>What to do</th></tr>"));
                foreach (var c in i.Causes)
                    h.Append($"<tr><td><b>{Util.H(c.Category)}</b></td><td class=s{c.Confidence}>{Util.H(c.ConfidenceText)}</td><td>{c.Score}</td><td>{Util.H(c.EvidenceText)}</td><td>{Util.H(c.Hint)}</td></tr>");
                h.Append($"</table><div class=muted>{Util.H(ConfidenceExplain)}</div>");
            }

            h.Append("<div class=card><ul class=f>");
            foreach (var f in i.Findings)
                h.Append($"<li><span class='badge s{f.Sev}'>{SevLabel(f.Sev)}</span>{Util.H(f.Text)}</li>");
            h.Append("</ul></div>");

            h.Append("<table class=kv>");
            void Row(string k, string v) => h.Append($"<tr><td>{Util.H(k)}</td><td>{Util.H(v)}</td></tr>");
            if (i.Kind == IncidentKind.PowerLoss)
            {
                Row(L("Kernel-Power 41 の記録時刻", "Kernel-Power 41 logged at"), Util.T(i.Kp41Time));
                Row(L("OS 起動時刻", "OS start time") + (i.BootEstimated ? L("（推定）", " (estimated)") : ""), Util.TMs(i.BootStart));
                Row(L("電源断前の最後の記録", "Last record before power loss"), i.LastEvent.HasValue ? $"{Util.TMs(i.LastEvent.Value)}  ({i.LastEventDesc})" : "-");
                Row(L("電源ボタン押下（OS の記録）", "Power button press (recorded by OS)"), i.PowerButton.HasValue ? Util.TMs(i.PowerButton.Value) : "-");
                Row(L("Kernel-Power 41 の詳細", "Kernel-Power 41 details"), string.Join(", ", i.Kp41.Where(kv => kv.Value != "0" && kv.Value != "false").Select(kv => kv.Key + "=" + kv.Value)));
            }
            else if (i.Kind == IncidentKind.Reported)
                Row(L("指定された発生時刻", "Specified time"), Util.TMs(i.When));
            else
            {
                Row(L("停止の兆候", "Hang indications"), $"{Util.TMs(i.FirstTrigger ?? i.LastAlive)} - {Util.TMs(i.LastAlive)}");
                Row(L("起点のイベント", "Symptom events"), string.Join(" / ", i.Triggers.Select(t => $"{Util.T(t.Time)} {t.Sig.Title}")));
            }
            Row(L("このセッションの起動", "This session started"), Util.T(i.SessionStart));
            Row(L("直前にログオンしたユーザー", "Last logged-on user"), i.UserText);
            h.Append("</table>");

            string tid = "tl" + i.No;
            h.Append(L($"<h3>タイムライン（{Util.T(i.WindowFrom)} ～ {Util.T(i.BootStart)}、{i.Events.Count} 件）</h3>", $"<h3>Timeline ({Util.T(i.WindowFrom)} - {Util.T(i.BootStart)}, {i.Events.Count} events)</h3>"));
            foreach (var st in i.Stats.Where(st => st.Problem))
                h.Append($"<div class=s3>{Util.H(st.Log)}: {Util.H(st.StatusText)} {Util.H(st.Truncated ? L($"（上限 {st.Max} 件。{Util.T(st.Oldest)} より前は含まれていません）", $"(limit {st.Max}; events before {Util.T(st.Oldest)} are not included)") : st.Error)}</div>");
            h.Append($"<label><input type=checkbox class=toggle data-t='{tid}'> {L("情報レベル（所見なし）のイベントも表示", "Also show informational events without findings")}</label>");
            EventTable(h, i.Events, tid, i.When, hideInfo: true);
        }

        static void EventTable(StringBuilder h, IEnumerable<EvtItem> events, string id, DateTime? alive = null, bool hideInfo = false)
        {
            h.Append($"<table{(id != null ? $" id='{id}'" : "")}><tr><th>{L("時刻", "Time")}</th>{(alive.HasValue ? $"<th>{L("基準時刻との差", "From reference")}</th>" : "")}" +
                     L("<th>ログ</th><th>ソース</th><th>ID</th><th>レベル</th><th>所見</th><th>メッセージ</th></tr>", "<th>Log</th><th>Source</th><th>ID</th><th>Level</th><th>Finding</th><th>Message</th></tr>"));
            foreach (var e in events)
            {
                int sev = e.Severity;
                string style = hideInfo && sev == 0 ? " style='display:none'" : "";
                h.Append($"<tr class=r{sev}{style}><td>{Util.TMs(e.Time)}</td>");
                if (alive.HasValue) h.Append($"<td>{Util.Dur(e.Time - alive.Value)}</td>");
                h.Append($"<td>{Util.H(ShortLog(e.Log))}</td><td>{Util.H(e.Provider)}</td><td>{e.Id}</td><td>{Util.H(e.LevelName)}</td>" +
                         $"<td class=s{sev}>{Util.H(e.Title)}</td><td class=msg>{Util.H(Util.Trunc(e.Message, 1500))}</td></tr>");
            }
            h.Append("</table>");
        }

        public static string ShortLog(string log) => log?.Replace("Microsoft-Windows-", "");

        static void Section(StringBuilder h, InfoSection s)
        {
            h.Append($"<h3>{Util.H(s.Title)}</h3>");
            if (s.Rows.Count > 0)
            {
                h.Append("<table class=kv>");
                foreach (var (k, v) in s.Rows) h.Append($"<tr><td>{Util.H(k)}</td><td>{Util.H(v)}</td></tr>");
                h.Append("</table>");
            }
            if (!string.IsNullOrEmpty(s.Pre)) h.Append($"<pre>{Util.H(s.Pre)}</pre>");
        }
    }
}
