using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Xml.Linq;

namespace WhyWindowsFroze
{
    internal sealed class EvtItem
    {
        public DateTime Time;
        public string Log;
        public string Provider;
        public int Id;
        public int Level;
        public string Message;
        public Dictionary<string, string> Data;
        public Signature Sig;

        public int Severity => Sig?.Severity ?? (Level == 1 ? 2 : Level == 2 ? 1 : 0);

        public string Title => Sig?.Title ?? (Level == 1 ? Loc.L("重大", "Critical") : Level == 2 ? Loc.L("エラー", "Error") : "");

        public string LevelName
        {
            get
            {
                switch (Level)
                {
                    case 1: return Loc.L("重大", "Critical");
                    case 2: return Loc.L("エラー", "Error");
                    case 3: return Loc.L("警告", "Warning");
                    case 5: return Loc.L("詳細", "Verbose");
                    default: return Loc.L("情報", "Information");
                }
            }
        }

        public string D(string key) => Data != null && Data.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>1 回のイベントログ検索の結果（レポートの「調査範囲」に載せて、取りこぼしを利用者に知らせる）</summary>
    internal sealed class QueryStat
    {
        public string Purpose;
        public string Log;
        public DateTime From, To;
        public int Max;
        public int Count;
        public bool Truncated;      // 上限に達して打ち切った
        public bool Missing;        // ログがこの PC に存在しない
        public string Error;        // 読み取り失敗の理由
        public DateTime? Oldest, Newest;
        public Incident Incident;   // 時間窓の検索なら対象のインシデント

        public bool Problem => Error != null || Truncated;

        public string StatusText =>
            Error != null ? Loc.L("読み取り失敗", "Read failed") :
            Truncated ? Loc.L("上限到達", "Limit reached") :
            Missing ? Loc.L("ログなし", "Log not present") : "OK";
    }

    internal static class Evt
    {
        public static string TimeRange(DateTime from, DateTime to) =>
            $"TimeCreated[@SystemTime>='{Util.U(from)}' and @SystemTime<'{Util.U(to)}']";

        /// <summary>
        /// イベントを読む。stat を渡すと、取得件数・上限到達・失敗理由を記録する（失敗しても例外は投げない）。
        /// 上限に達したとき残すのは、reverse=true なら新しい側、false なら古い側。
        /// </summary>
        public static IEnumerable<EventRecord> Query(string log, string xpath, bool reverse = false, int max = int.MaxValue, QueryStat stat = null)
        {
            if (stat != null) { stat.Log = log; stat.Max = max; }
            EventLogReader reader;
            try
            {
                reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = reverse });
            }
            catch (Exception ex)
            {
                Fail(stat, ex);
                yield break;
            }
            using (reader)
            {
                int n = 0;
                while (true)
                {
                    EventRecord r;
                    try { r = reader.ReadEvent(); }
                    catch (Exception ex) { Fail(stat, ex); yield break; }
                    if (r == null) yield break;
                    if (n >= max)
                    {
                        r.Dispose();
                        if (stat != null) stat.Truncated = true;
                        yield break;
                    }
                    n++;
                    if (stat != null)
                    {
                        stat.Count = n;
                        if (r.TimeCreated is DateTime t)
                        {
                            if (stat.Oldest == null || t < stat.Oldest) stat.Oldest = t;
                            if (stat.Newest == null || t > stat.Newest) stat.Newest = t;
                        }
                    }
                    yield return r;
                }
            }
        }

        static void Fail(QueryStat stat, Exception ex)
        {
            if (stat == null) return;
            if (ex is EventLogNotFoundException) stat.Missing = true;
            else if (ex is UnauthorizedAccessException) stat.Error = Loc.L("アクセス拒否（管理者権限が必要）", "Access denied (administrator rights required)");
            else stat.Error = ex.GetType().Name + ": " + ex.Message;
        }

        public static bool LogExists(string log)
        {
            try
            {
                using (var c = new EventLogConfiguration(log)) return true;
            }
            catch { return false; }
        }

        public static EvtItem ToItem(EventRecord r)
        {
            var it = new EvtItem
            {
                Time = r.TimeCreated ?? DateTime.MinValue,
                Log = r.LogName,
                Provider = r.ProviderName,
                Id = r.Id,
                Level = r.Level ?? 4,
            };
            it.Data = Data(r);
            if (it.Log == "Security" && it.Id == 4688)
            {
                // 4688 の既定メッセージは冗長なので要点だけにする
                it.Message = $"{it.D("NewProcessName")}  [{it.D("CommandLine")}]  {Loc.L("親", "parent")}={it.D("ParentProcessName")}  {Loc.L("ユーザー", "user")}={it.D("TargetDomainName")}\\{it.D("TargetUserName")}";
            }
            else
            {
                it.Message = Message(r, it.Data);
            }
            it.Sig = Signatures.Match(it);
            return it;
        }

        public static string Message(EventRecord r, Dictionary<string, string> data)
        {
            try
            {
                var m = r.FormatDescription();
                if (!string.IsNullOrWhiteSpace(m)) return m.Trim();
            }
            catch { }
            return string.Join(", ", (data ?? Data(r)).Select(kv => kv.Key + "=" + kv.Value));
        }

        static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

        public static Dictionary<string, string> Data(EventRecord r)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var x = XDocument.Parse(r.ToXml());
                var ed = x.Root.Element(Ns + "EventData");
                if (ed != null)
                {
                    int i = 0;
                    foreach (var e in ed.Elements())
                    {
                        string name = (string)e.Attribute("Name") ?? ("Data" + i);
                        d[name] = e.Value;
                        i++;
                    }
                }
                var ud = x.Root.Element(Ns + "UserData");
                if (ud != null)
                    foreach (var e in ud.Descendants().Where(e => !e.HasElements))
                        d[e.Name.LocalName] = e.Value;
            }
            catch { }
            return d;
        }
    }
}
