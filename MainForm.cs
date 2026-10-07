using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static WhyWindowsFroze.Loc;

namespace WhyWindowsFroze
{
    internal sealed class MainForm : Form
    {
        readonly bool _autoRun;
        readonly float _scale;
        AnalysisResult _res;
        CancellationTokenSource _cts;

        // 上部
        NumericUpDown _nudDays, _nudWindow;
        TextBox _txtOut;
        CheckBox _chkEvtx, _chkZip, _chkRecovered, _chkAt;
        DateTimePicker _dtAt;
        Button _btnRun, _btnCancel, _btnReport, _btnFolder;

        // 中央
        ListView _lvIncidents;
        TabControl _tabs;
        TabPage _tabCause, _tabTimeline, _tabLog;
        Label _lblIncident;
        ListView _lvCauses, _lvFindings, _lvTimeline, _lvGlobal, _lvPrefetch, _lvDumps, _lvUsers, _lvSys, _lvHealth, _lvCoverage;
        TextBox _txtCauseDetail, _txtTimelineDetail, _txtGlobalDetail, _txtRecs, _txtLog, _txtFilter, _txtCoverage;
        CheckBox _chkInfo;

        // 下部
        ToolStripStatusLabel _lblStatus;
        ToolStripProgressBar _progress;

        public MainForm(bool autoRun, int? days = null, int? window = null, DateTime? at = null)
        {
            _autoRun = autoRun;
            using (var g = CreateGraphics()) _scale = g.DpiX / 96f;

            SuspendLayout();
            Text = L($"WhyWindowsFroze {Program.Version} - Windows フリーズ原因診断（{Environment.MachineName}）", $"WhyWindowsFroze {Program.Version} - Windows freeze diagnostics ({Environment.MachineName})");
            Font = new Font(L("Yu Gothic UI", "Segoe UI"), 9f);
            Size = new Size(S(1320), S(860));
            MinimumSize = new Size(S(900), S(600));
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BuildMain();
            BuildTop();
            BuildStatus();
            if (days.HasValue) _nudDays.Value = Math.Max(_nudDays.Minimum, Math.Min(_nudDays.Maximum, days.Value));
            if (window.HasValue) _nudWindow.Value = Math.Max(_nudWindow.Minimum, Math.Min(_nudWindow.Maximum, window.Value));
            if (at.HasValue) { _chkAt.Checked = true; _dtAt.Value = at.Value; }
            ResumeLayout(true);
        }

        int S(int px) => (int)(px * _scale);

        // ---------------- 画面構成 ----------------

        void BuildTop()
        {
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(S(6), S(6), S(6), S(2)),
            };
            Control Lbl(string t) => new Label { Text = t, AutoSize = true, Margin = new Padding(S(6), S(7), S(2), 0) };

            _nudDays = new NumericUpDown { Minimum = 1, Maximum = 3650, Value = 30, Width = S(60) };
            _nudWindow = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = 30, Width = S(60) };
            _txtOut = new TextBox { Width = S(320), Text = Analyzer.DefaultBase() };
            var browse = new Button { Text = L("参照...", "Browse..."), AutoSize = true };
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { SelectedPath = _txtOut.Text, Description = L("レポートの出力先フォルダー", "Output folder for the report") })
                    if (d.ShowDialog(this) == DialogResult.OK) _txtOut.Text = d.SelectedPath;
            };
            _chkEvtx = new CheckBox { Text = L("生ログ (.evtx) も保存", "Save raw logs (.evtx)"), Checked = true, AutoSize = true, Margin = new Padding(S(10), S(6), 0, 0) };
            _chkZip = new CheckBox { Text = L("ZIP にまとめる", "Create ZIP"), Checked = true, AutoSize = true, Margin = new Padding(S(6), S(6), 0, 0) };
            _chkRecovered = new CheckBox { Text = L("再起動なしの停止・応答停止も検出", "Detect hangs without a restart"), Checked = true, AutoSize = true, Margin = new Padding(S(6), S(6), 0, 0) };
            _chkAt = new CheckBox { Text = L("発生時刻を指定", "Specify time"), AutoSize = true, Margin = new Padding(S(10), S(6), 0, 0) };
            _dtAt = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Width = S(130), Enabled = false, Value = DateTime.Now };
            _chkAt.CheckedChanged += (s, e) => _dtAt.Enabled = _chkAt.Checked;
            _btnRun = new Button { Text = L("解析開始", "Analyze"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(S(14), S(3), S(3), S(3)) };
            _btnRun.Click += async (s, e) => await RunAsync();
            _btnCancel = new Button { Text = L("中止", "Cancel"), AutoSize = true, Enabled = false };
            _btnCancel.Click += (s, e) => _cts?.Cancel();
            _btnReport = new Button { Text = L("HTML レポートを開く", "Open HTML report"), AutoSize = true, Enabled = false, Margin = new Padding(S(14), S(3), S(3), S(3)) };
            _btnReport.Click += (s, e) => { if (_res?.ReportPath != null) Explorer($"\"{_res.ReportPath}\""); };
            _btnFolder = new Button { Text = L("出力フォルダーを開く", "Open output folder"), AutoSize = true, Enabled = false };
            _btnFolder.Click += (s, e) => { if (_res != null) Explorer($"/select,\"{_res.ZipPath ?? _res.OutDir}\""); };

            top.Controls.AddRange(new Control[]
            {
                Lbl(L("期間（日）", "Period (days)")), _nudDays, Lbl(L("時間窓（分）", "Window (min)")), _nudWindow, Lbl(L("出力先", "Output")), _txtOut, browse,
                _chkAt, _dtAt, _chkRecovered, _chkEvtx, _chkZip, _btnRun, _btnCancel, _btnReport, _btnFolder,
            });
            var tip = new ToolTip();
            tip.SetToolTip(_nudDays, L("何日前までのイベントログを調べるか", "How many days of event logs to examine"));
            tip.SetToolTip(_nudWindow, L("基準時刻（電源断前の最後の記録、停止の起点、指定時刻）から、何分前までのイベントを詳しく集めるか", "How many minutes of events before the reference time (last record before power loss, first hang event, or specified time) to collect in detail"));
            tip.SetToolTip(_chkAt, L("ログに残らなかったフリーズも、利用者が覚えている発生時刻を基準に前後のイベントを解析します", "Analyze the events around a time the user remembers, even if the freeze left nothing in the logs"));

            if (!Util.IsAdmin())
            {
                var warn = new Label
                {
                    Text = L("  管理者として実行されていません。Security ログと Prefetch を読めないため、結果が不完全になります。", "  Not running as administrator. The Security log and Prefetch cannot be read, so results will be incomplete."),
                    Dock = DockStyle.Top, AutoSize = false, Height = S(24), TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.FromArgb(255, 228, 228), ForeColor = Color.FromArgb(150, 0, 0),
                };
                Controls.Add(warn);
            }
            Controls.Add(top);
        }

        void BuildStatus()
        {
            var ss = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel(L("［解析開始］を押すと、イベントログから強制電源断・ブルースクリーン・一時フリーズを探し、原因を診断します。", "Click [Analyze] to find forced power-offs, blue screens and temporary freezes in the event logs and diagnose their causes.")) { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _progress = new ToolStripProgressBar { Width = S(160), Style = ProgressBarStyle.Blocks };
            ss.Items.AddRange(new ToolStripItem[] { _lblStatus, _progress });
            Controls.Add(ss);
        }

        void BuildMain()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = S(5) };

            // インシデント一覧
            var grpInc = new GroupBox { Text = L("検出した事象（電源断・ブルースクリーン・画面/システムの一時停止・アプリ応答停止）", "Detected events (power loss / blue screen / display or system hang / app hang)"), Dock = DockStyle.Fill, Padding = new Padding(S(4)) };
            _lvIncidents = NewList(("#", 40), (L("種類", "Type"), 190), (L("発生", "Occurred"), 140), (L("再起動", "Restart"), 140), (L("空白", "Gap"), 80), ("BugCheck", 230), (L("最有力の原因候補", "Most likely cause"), 190), (L("確信度", "Confidence"), 60), (L("直前のユーザー", "User"), 300));
            _lvIncidents.Groups.Add(new ListViewGroup("sys", L("Windows 全体の停止（電源断・ブルースクリーン・画面/システムの一時停止・指定時刻）", "Whole-Windows stops (power loss, blue screen, display/system hang, reported time)")));
            _lvIncidents.Groups.Add(new ListViewGroup("app", L("アプリ単体の応答停止・I/O 遅延（PC 全体が止まったとは限らない）", "App-only hangs / I/O delays (the whole PC did not necessarily stop)")));
            _lvIncidents.SelectedIndexChanged += (s, e) => ShowIncident();
            grpInc.Controls.Add(_lvIncidents);
            split.Panel1.Controls.Add(grpInc);

            _tabs = new TabControl { Dock = DockStyle.Fill };

            // 原因と所見
            _tabCause = new TabPage(L("原因と所見", "Causes & findings"));
            _lblIncident = new Label { Dock = DockStyle.Top, AutoSize = false, Height = S(26), TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(S(4), 0, 0, 0) };
            _lvCauses = NewList((L("原因候補", "Cause candidate"), 190), (L("確信度", "Confidence"), 60), (L("スコア", "Score"), 55), (L("根拠", "Evidence"), 560), (L("対処の方向性", "What to do"), 600));
            _lvFindings = NewList((L("重要度", "Severity"), 65), (L("所見", "Finding"), 1300));
            _txtCauseDetail = DetailBox();
            _lvCauses.SelectedIndexChanged += (s, e) => ShowCauseDetail(_lvCauses);
            _lvFindings.SelectedIndexChanged += (s, e) => ShowCauseDetail(_lvFindings);
            var causeSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel1 };
            var findSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
            causeSplit.Panel1.Controls.Add(_lvCauses);
            findSplit.Panel1.Controls.Add(_lvFindings);
            findSplit.Panel2.Controls.Add(_txtCauseDetail);
            causeSplit.Panel2.Controls.Add(findSplit);
            _tabCause.Controls.Add(causeSplit);
            _tabCause.Controls.Add(_lblIncident);
            Shown += (s, e) => { causeSplit.SplitterDistance = S(100); findSplit.SplitterDistance = Math.Max(S(80), findSplit.Height - S(100)); };

            // タイムライン
            _tabTimeline = new TabPage(L("タイムライン", "Timeline"));
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(S(2)) };
            _chkInfo = new CheckBox { Text = L("所見のない情報イベントも表示", "Also show informational events without findings"), AutoSize = true, Margin = new Padding(S(4), S(5), S(16), 0) };
            _chkInfo.CheckedChanged += (s, e) => FillTimeline();
            _txtFilter = new TextBox { Width = S(260) };
            _txtFilter.TextChanged += (s, e) => FillTimeline();
            bar.Controls.AddRange(new Control[] { _chkInfo, new Label { Text = L("絞り込み", "Filter"), AutoSize = true, Margin = new Padding(0, S(6), S(4), 0) }, _txtFilter });
            _lvTimeline = NewList((L("時刻", "Time"), 150), (L("基準時刻との差", "From reference"), 100), (L("ログ", "Log"), 150), (L("ソース", "Source"), 190), ("ID", 50), (L("レベル", "Level"), 70), (L("所見", "Finding"), 230), (L("メッセージ", "Message"), 900));
            _txtTimelineDetail = DetailBox();
            _lvTimeline.SelectedIndexChanged += (s, e) => _txtTimelineDetail.Text = EventDetail(Sel<EvtItem>(_lvTimeline));
            _tabTimeline.Controls.Add(SplitWithDetail(_lvTimeline, _txtTimelineDetail));
            _tabTimeline.Controls.Add(bar);

            // 期間内シグネチャ
            var tabGlobal = new TabPage(L("期間内の既知イベント", "Known events"));
            _lvGlobal = NewList((L("時刻", "Time"), 150), (L("所見", "Finding"), 260), (L("カテゴリ", "Category"), 160), (L("重要度", "Severity"), 65), (L("ソース", "Source"), 190), ("ID", 50), (L("メッセージ", "Message"), 800));
            _txtGlobalDetail = DetailBox();
            _lvGlobal.SelectedIndexChanged += (s, e) => _txtGlobalDetail.Text = EventDetail(Sel<EvtItem>(_lvGlobal));
            tabGlobal.Controls.Add(SplitWithDetail(_lvGlobal, _txtGlobalDetail));

            // Prefetch
            var tabPf = new TabPage(L("アプリ実行履歴", "App run history"));
            _lvPrefetch = NewList((L("実行ファイル", "Executable"), 260), (L("最終実行", "Last run"), 140), (L("直近の実行時刻（最大 8 回、新しい順）", "Recent run times (up to 8, newest first)"), 1000));

            var tabHealth = new TabPage(L("健全性チェック", "Health check"));
            _lvHealth = NewList((L("重要度", "Severity"), 65), (L("カテゴリ", "Category"), 180), (L("内容", "Details"), 1200));
            tabHealth.Controls.Add(_lvHealth);
            tabPf.Controls.Add(_lvPrefetch);

            // ダンプ / WER
            var tabDump = new TabPage(L("ダンプ / WER", "Dumps / WER"));
            _lvDumps = NewList((L("種類", "Type"), 150), (L("日時", "Date"), 150), (L("サイズ", "Size"), 90), (L("パス", "Path"), 900));
            _lvDumps.DoubleClick += (s, e) => { if (Sel<string>(_lvDumps) is string p) Explorer($"/select,\"{p}\""); };
            tabDump.Controls.Add(_lvDumps);

            // ユーザー / システム
            var tabUsers = new TabPage(L("ユーザー設定", "User settings"));
            _lvUsers = NewList((L("項目", "Item"), 300), (L("値", "Value"), 1000));
            tabUsers.Controls.Add(_lvUsers);
            var tabSys = new TabPage(L("システム情報", "System info"));
            _lvSys = NewList((L("項目", "Item"), 300), (L("値", "Value"), 1000));
            tabSys.Controls.Add(_lvSys);

            // 調査範囲
            var tabCov = new TabPage(L("調査範囲", "Coverage"));
            _lvCoverage = NewList((L("目的", "Purpose"), 200), (L("ログ", "Log"), 300), (L("状態", "Status"), 90), (L("検索回数", "Queries"), 70), (L("取得件数", "Events"), 80), (L("備考", "Notes"), 700));
            _txtCoverage = DetailBox();
            tabCov.Controls.Add(SplitWithDetail(_lvCoverage, _txtCoverage));

            // 推奨事項 / ログ
            var tabRecs = new TabPage(L("推奨事項", "Recommendations"));
            _txtRecs = DetailBox();
            tabRecs.Controls.Add(_txtRecs);
            _tabLog = new TabPage(L("実行ログ", "Log"));
            _txtLog = DetailBox();
            _tabLog.Controls.Add(_txtLog);

            _tabs.TabPages.AddRange(new[] { _tabCause, _tabTimeline, tabHealth, tabGlobal, tabPf, tabDump, tabUsers, tabSys, tabRecs, tabCov, _tabLog });
            split.Panel2.Controls.Add(_tabs);
            Controls.Add(split);
            Shown += (s, e) => split.SplitterDistance = S(170);
        }

        sealed class DbListView : ListView
        {
            public DbListView() { DoubleBuffered = true; }
        }

        ListView NewList(params (string Text, int Width)[] cols)
        {
            var lv = new DbListView
            {
                View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = true, Dock = DockStyle.Fill,
            };
            foreach (var (t, w) in cols) lv.Columns.Add(t, S(w));
            lv.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C && lv.SelectedItems.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (ListViewItem it in lv.SelectedItems)
                        sb.AppendLine(string.Join("\t", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text)));
                    Clipboard.SetText(sb.ToString());
                }
                else if (e.Control && e.KeyCode == Keys.A)
                {
                    lv.BeginUpdate();
                    foreach (ListViewItem it in lv.Items) it.Selected = true;
                    lv.EndUpdate();
                }
            };
            return lv;
        }

        TextBox DetailBox() => new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Dock = DockStyle.Fill,
            BackColor = SystemColors.Window, Font = new Font(L("BIZ UDゴシック", "Consolas"), 9f),
        };

        Control SplitWithDetail(Control list, Control detail)
        {
            var sp = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            sp.Panel1.Controls.Add(list);
            sp.Panel2.Controls.Add(detail);
            Shown += (s, e) => sp.SplitterDistance = Math.Max(S(80), sp.Height - S(130));
            return sp;
        }

        static T Sel<T>(ListView lv) where T : class => lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as T : null;

        static void Colorize(ListViewItem it, int sev)
        {
            switch (sev)
            {
                case 3: it.BackColor = Color.FromArgb(255, 226, 226); it.ForeColor = Color.FromArgb(140, 0, 0); break;
                case 2: it.BackColor = Color.FromArgb(255, 243, 214); break;
                case 1: it.ForeColor = Color.FromArgb(21, 90, 180); break;
                default: it.ForeColor = Color.DimGray; break;
            }
        }

        static ListViewItem Item(object tag, params string[] cols) => new ListViewItem(cols.Select(c => c ?? "").ToArray()) { Tag = tag };

        static void Explorer(string args)
        {
            // 昇格したプロセスから直接開くとブラウザーも管理者で起動するため、explorer 経由でユーザー権限で開く
            try { Process.Start("explorer.exe", args); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "WhyWindowsFroze", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_autoRun) BeginInvoke(new Action(async () => await RunAsync()));
        }

        // ---------------- 解析の実行 ----------------

        async Task RunAsync()
        {
            var opt = new AnalyzeOptions
            {
                Days = (int)_nudDays.Value,
                WindowMinutes = (int)_nudWindow.Value,
                OutBase = _txtOut.Text.Trim(),
                SaveEvtx = _chkEvtx.Checked,
                Zip = _chkZip.Checked,
                DetectRecovered = _chkRecovered.Checked,
                ReportedTime = _chkAt.Checked ? _dtAt.Value : (DateTime?)null,
            };
            if (!string.IsNullOrEmpty(opt.OutBase) && !Directory.Exists(opt.OutBase))
            {
                MessageBox.Show(this, L("出力先フォルダーが存在しません。", "The output folder does not exist."), "WhyWindowsFroze", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetRunning(true);
            _txtLog.Clear();
            _tabs.SelectedTab = _tabLog;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            try
            {
                var res = await Task.Run(() => new Analyzer(opt, Log, ct).Run());
                _res = res;
                ShowResult();
                _lblStatus.Text = L($"完了: {res.CountText()}。", $"Done: {res.CountText()}. ") +
                                  (res.Coverage.Any(c => c.Problem) ? L("読めなかったログ/上限到達あり（調査範囲タブ）。", "Some logs unreadable or limited (see Coverage). ") : "") +
                                  L($"出力先 {res.OutDir}", $"Output: {res.OutDir}");
                _tabs.SelectedTab = _tabCause;
            }
            catch (OperationCanceledException)
            {
                Log(L("中止しました。", "Cancelled."));
                _lblStatus.Text = L("中止しました。", "Cancelled.");
            }
            catch (Exception ex)
            {
                Log(L("エラー: ", "Error: ") + ex);
                _lblStatus.Text = L("エラーが発生しました（実行ログを参照）。", "An error occurred (see the Log tab).");
                MessageBox.Show(this, ex.Message, "WhyWindowsFroze", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetRunning(false);
            }
        }

        void SetRunning(bool running)
        {
            _btnRun.Enabled = !running;
            _btnCancel.Enabled = running;
            _nudDays.Enabled = _nudWindow.Enabled = _txtOut.Enabled = _chkEvtx.Enabled = _chkZip.Enabled = _chkRecovered.Enabled = _chkAt.Enabled = !running;
            _dtAt.Enabled = !running && _chkAt.Checked;
            _btnReport.Enabled = !running && _res?.ReportPath != null;
            _btnFolder.Enabled = !running && _res != null;
            _progress.Style = running ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            _progress.MarqueeAnimationSpeed = running ? 30 : 0;
            UseWaitCursor = running;
            _btnCancel.UseWaitCursor = false;
        }

        void Log(string msg)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), msg); return; }
            _txtLog.AppendText(msg + Environment.NewLine);
            _lblStatus.Text = msg.Trim();
        }

        // ---------------- 結果の表示 ----------------

        void ShowResult()
        {
            var r = _res;

            _lvIncidents.BeginUpdate();
            _lvIncidents.Items.Clear();
            foreach (var i in r.Incidents.OrderByDescending(i => i.When))
            {
                var it = Item(i, "#" + i.No, i.KindText, Util.T(i.When), i.RebootText, i.GapText, i.BugCheckText, i.TopCause, i.TopConfidence, i.UserText);
                it.Group = _lvIncidents.Groups[i.Kind == IncidentKind.Partial ? "app" : "sys"];
                if (i.Causes.Count > 0) it.UseItemStyleForSubItems = false;
                _lvIncidents.Items.Add(it);
                if (i.Causes.Count > 0) { it.SubItems[6].ForeColor = Color.FromArgb(160, 0, 0); it.SubItems[6].Font = new Font(Font, FontStyle.Bold); }
            }
            _lvIncidents.EndUpdate();

            if (r.Incidents.Count == 0)
            {
                _lblIncident.Text = Report.NoneText + (r.Coverage.Any(c => c.Problem)
                    ? L(" ただし読めなかったログ/上限到達があり、検出漏れの可能性があります（調査範囲タブ）。", " However, some logs were unreadable or limited, so events may have been missed (see Coverage).")
                    : L(" 健全性チェックと調査範囲のタブも確認してください。", " Also check the Health check and Coverage tabs."));
                _lvCauses.Items.Clear(); _lvFindings.Items.Clear(); _lvTimeline.Items.Clear();
            }
            else _lvIncidents.Items[0].Selected = true;

            Fill(_lvGlobal, r.Global.Select(e =>
            {
                var it = Item(e, Util.TMs(e.Time), e.Title, e.Sig?.Category, Report.SevLabel(e.Severity), e.Provider, e.Id.ToString(), Util.Trunc(Util.OneLine(e.Message), 400));
                Colorize(it, e.Severity);
                return it;
            }));

            Fill(_lvPrefetch, r.Prefetch.OrderByDescending(p => p.RunTimes.Count > 0 ? p.RunTimes[0] : DateTime.MinValue).Select(p =>
                Item(p, p.Exe, p.RunTimes.Count > 0 ? Util.T(p.RunTimes[0]) : "",
                    p.Error != null ? L("解析失敗: ", "Parse failed: ") + p.Error : string.Join("   ", p.RunTimes.Select(t => Util.T(t))))));

            Fill(_lvHealth, r.Health.Select(f =>
            {
                var it = Item(f, Report.SevLabel(f.Sev), f.Category ?? L("診断", "Diagnostics"), f.Text);
                Colorize(it, f.Sev);
                return it;
            }));
            if (r.Prefetch.Count == 0) _lvPrefetch.Items.Add(Item(null, L("(取得できませんでした)", "(not available)"), "", L("管理者権限が必要です。または Prefetch が無効です。", "Requires administrator rights, or Prefetch is disabled.")));

            Fill(_lvDumps, r.Dumps.Select(d => Item(d.Path, d.Kind, Util.T(d.Time), $"{d.Size / 1048576.0:0.0} MB", d.Path))
                .Concat(r.Wer.Take(300).Select(w => Item(w.Dir, L("WER レポート", "WER report"), Util.T(w.Time), "", w.Dir))));

            FillSections(_lvUsers, r.Users);
            FillSections(_lvSys, r.Sys);

            Fill(_lvCoverage, r.CoverageRows().Select(c =>
            {
                var it = Item(c, c.Purpose, c.Log, c.Status, c.Queries.ToString(), c.Count.ToString(), c.Note);
                Colorize(it, c.Problem ? 3 : 0);
                return it;
            }));
            _txtCoverage.Text = L($"解析期間: {Util.T(r.From)} ～ {Util.T(r.To)}", $"Period: {Util.T(r.From)} - {Util.T(r.To)}") + Environment.NewLine +
                                string.Join(Environment.NewLine, r.Candidates.Select(kv => $"{new Incident { Kind = kv.Key }.KindText}: " +
                                    L($"候補 {kv.Value.Total} 件、解析 {kv.Value.Analyzed} 件", $"{kv.Value.Total} candidates, {kv.Value.Analyzed} analyzed"))) +
                                Environment.NewLine + Environment.NewLine +
                                string.Join(Environment.NewLine, r.CoverageWarnings().Select(w => L("・", "- ") + w));

            _txtRecs.Text = r.Recommendations.Count == 0
                ? L("特にありません。", "None.")
                : string.Join(Environment.NewLine + Environment.NewLine, r.Recommendations.Select(x => L("・", "- ") + x));
        }

        static void Fill(ListView lv, IEnumerable<ListViewItem> items)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Items.AddRange(items.ToArray());
            lv.EndUpdate();
        }

        static void FillSections(ListView lv, List<InfoSection> sections)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Groups.Clear();
            foreach (var s in sections)
            {
                var g = new ListViewGroup(s.Title);
                lv.Groups.Add(g);
                foreach (var (k, v) in s.Rows) lv.Items.Add(new ListViewItem(new[] { k, v }, g));
                if (!string.IsNullOrEmpty(s.Pre))
                    foreach (var line in s.Pre.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                        lv.Items.Add(new ListViewItem(new[] { "", line }, g));
            }
            lv.EndUpdate();
        }

        Incident Current => Sel<Incident>(_lvIncidents);

        void ShowIncident()
        {
            var i = Current;
            if (i == null) return;
            _lblIncident.Text = L($"#{i.No} {i.KindText}　発生 {Util.T(i.When)}　再起動 {i.RebootText}　BugCheck: {i.BugCheckText}　ユーザー: {i.UserText}", $"#{i.No} {i.KindText}    Occurred {Util.T(i.When)}    Restart {i.RebootText}    Bugcheck: {i.BugCheckText}    User: {i.UserText}");

            Fill(_lvCauses, i.Causes.Select((c, n) =>
            {
                var it = Item(c, c.Category, c.ConfidenceText, c.Score.ToString(), c.EvidenceText, c.Hint);
                Colorize(it, n == 0 ? 3 : 2);
                return it;
            }));
            if (i.Causes.Count == 0)
                _lvCauses.Items.Add(Item(null, L("手がかりなし", "No clues"), "", "", L("直前のイベントログに原因カテゴリへ結び付くイベントがありません。所見とタイムラインを確認してください。", "No events before the freeze map to a cause category. Review the findings and the timeline."), ""));

            Fill(_lvFindings, i.Findings.Select(f =>
            {
                var it = Item(f, Report.SevLabel(f.Sev), f.Text);
                Colorize(it, f.Sev);
                return it;
            }));
            _txtCauseDetail.Text = string.Join(Environment.NewLine, i.Findings.Select(f => $"[{Report.SevLabel(f.Sev)}] {f.Text}"));
            _tabTimeline.Text = L("タイムライン", "Timeline") + $" ({i.Events.Count})";
            FillTimeline();
        }

        void ShowCauseDetail(ListView lv)
        {
            switch (Sel<object>(lv))
            {
                case Cause c:
                    _txtCauseDetail.Text = L($"原因候補: {c.Category}（確信度 {c.ConfidenceText}、スコア {c.Score}）", $"Cause candidate: {c.Category} (confidence {c.ConfidenceText}, score {c.Score})") +
                                           $"{Environment.NewLine}{Report.ConfidenceExplain}{(c.ConfidenceNote.Length > 0 ? Environment.NewLine + c.ConfidenceNote : "")}" +
                                           $"{Environment.NewLine}{Environment.NewLine}{L("根拠:", "Evidence:")}{Environment.NewLine}" +
                                           string.Join(Environment.NewLine, c.Evidence.Select(kv => $"  {L("・", "- ")}{kv.Key}{(kv.Value > 1 ? $" ×{kv.Value}" : "")}")) +
                                           $"{Environment.NewLine}{Environment.NewLine}{L("対処の方向性:", "What to do:")}{Environment.NewLine}  {c.Hint}";
                    break;
                case Finding f:
                    _txtCauseDetail.Text = $"[{Report.SevLabel(f.Sev)}] {f.Text}";
                    break;
            }
        }

        void FillTimeline()
        {
            var i = Current;
            if (i == null) return;
            string q = _txtFilter.Text.Trim();
            var items = i.Events
                .Where(e => _chkInfo.Checked || e.Severity > 0 || e.Sig != null)
                .Where(e => q.Length == 0 ||
                            (e.Message ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (e.Provider ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (e.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Id.ToString() == q)
                .Select(e =>
                {
                    var it = Item(e, Util.TMs(e.Time), Util.Dur(e.Time - i.When), Report.ShortLog(e.Log), e.Provider, e.Id.ToString(), e.LevelName, e.Title,
                        Util.Trunc(Util.OneLine(e.Message), 400));
                    Colorize(it, e.Severity);
                    return it;
                }).ToList();
            Fill(_lvTimeline, items);
            if (items.Count > 0) _lvTimeline.EnsureVisible(items.Count - 1); // 停止直前が見えるように末尾へ
            _txtTimelineDetail.Text = L($"{items.Count} / {i.Events.Count} 件を表示。行を選ぶとメッセージ全文が表示されます。", $"Showing {items.Count} of {i.Events.Count} events. Select a row to see the full message.") +
                string.Concat(i.Stats.Where(st => st.Problem).Select(st => Environment.NewLine + L("注意: ", "Warning: ") + st.Log + " " + st.StatusText + " " +
                    (st.Truncated ? L($"（上限 {st.Max} 件。{Util.T(st.Oldest)} より前は含まれていません）", $"(limit {st.Max}; events before {Util.T(st.Oldest)} are not included)") : st.Error)));
        }

        static string EventDetail(EvtItem e)
        {
            if (e == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"{Util.TMs(e.Time)}   {e.Log}   {e.Provider}   ID {e.Id}   {e.LevelName}");
            if (e.Sig != null)
            {
                sb.AppendLine(L("所見: ", "Finding: ") + $"{e.Sig.Title} ({Report.SevLabel(e.Severity)}{(e.Sig.Category != null ? " / " + e.Sig.Category : "")})");
                if (e.Sig.Hint != null) sb.AppendLine(L("ヒント: ", "Hint: ") + e.Sig.Hint);
            }
            sb.AppendLine();
            sb.AppendLine((e.Message ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            return sb.ToString();
        }
    }
}
