using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WhyWindowsFroze
{
    /// <summary>
    /// C:\Windows\Prefetch\*.pf から直近 8 回の実行時刻を取り出す。
    /// フリーズの直前にどのアプリが起動したかを確認できる。
    /// </summary>
    internal static class Prefetch
    {
        /// <summary>頻繁に起動する Windows 内部プロセス（フリーズ直前の起動として表示しても意味が薄い）</summary>
        public static readonly HashSet<string> Noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SVCHOST.EXE", "DLLHOST.EXE", "CONHOST.EXE", "RUNTIMEBROKER.EXE", "BACKGROUNDTASKHOST.EXE", "TASKHOSTW.EXE",
            "SEARCHPROTOCOLHOST.EXE", "SEARCHFILTERHOST.EXE", "SEARCHINDEXER.EXE", "WMIPRVSE.EXE", "AUDIODG.EXE", "SPPSVC.EXE",
            "COMPATTELRUNNER.EXE", "MPCMDRUN.EXE", "MSMPENG.EXE", "SMARTSCREEN.EXE", "SIHCLIENT.EXE", "USOCLIENT.EXE",
            "MOUSOCOREWORKER.EXE", "TIWORKER.EXE", "TRUSTEDINSTALLER.EXE", "DEVICECENSUS.EXE", "WERMGR.EXE", "WERFAULT.EXE",
            "SPPEXTCOMOBJ.EXE", "SGRMBROKER.EXE", "MPDEFENDERCOREService.EXE", "NISSRV.EXE", "MICROSOFTEDGEUPDATE.EXE",
            "GOOGLEUPDATE.EXE", "UPDATER.EXE", "SECURITYHEALTHHOST.EXE", "SECURITYHEALTHSERVICE.EXE", "CTFMON.EXE",
            "SETTINGSYNCHOST.EXE", "USERINIT.EXE", "CONSENT.EXE", "DEVICEENROLLER.EXE", "OMADMCLIENT.EXE", "TASKMGR.EXE",
            "GPUPDATE.EXE", "WSQMCONS.EXE", "DISMHOST.EXE", "WUAUCLT.EXE", "SIHOST.EXE", "FONTDRVHOST.EXE",
        };

        public sealed class Entry
        {
            public string Exe;
            public string File;
            public List<DateTime> RunTimes = new List<DateTime>();
            public string Error;
        }

        public static List<Entry> LoadAll()
        {
            var list = new List<Entry>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            string[] files;
            try { files = Directory.GetFiles(dir, "*.pf"); }
            catch { return list; }

            foreach (var f in files)
            {
                string name = Path.GetFileName(f);
                int dash = name.LastIndexOf('-');
                if (dash <= 0) continue;
                string exe = name.Substring(0, dash).ToUpperInvariant();
                var e = new Entry { Exe = exe, File = name };
                try { e.RunTimes = Parse(File.ReadAllBytes(f)); }
                catch (Exception ex) { e.Error = ex.Message; }
                list.Add(e);
            }
            return list;
        }

        static List<DateTime> Parse(byte[] raw)
        {
            byte[] data = raw;
            // Windows 10 以降は "MAM\x04" ヘッダー + Xpress Huffman 圧縮
            if (raw.Length > 8 && raw[0] == 'M' && raw[1] == 'A' && raw[2] == 'M')
            {
                int size = BitConverter.ToInt32(raw, 4);
                var comp = new byte[raw.Length - 8];
                Buffer.BlockCopy(raw, 8, comp, 0, comp.Length);
                uint st = Native.RtlGetCompressionWorkSpaceSize(Native.COMPRESSION_FORMAT_XPRESS_HUFF, out uint ws, out _);
                if (st != 0) throw new InvalidDataException($"RtlGetCompressionWorkSpaceSize 0x{st:X}");
                data = new byte[size];
                st = Native.RtlDecompressBufferEx(Native.COMPRESSION_FORMAT_XPRESS_HUFF, data, size, comp, comp.Length, out _, new byte[ws]);
                if (st != 0) throw new InvalidDataException($"RtlDecompressBufferEx 0x{st:X}");
            }
            if (data.Length < 0x100 || data[4] != 'S' || data[5] != 'C' || data[6] != 'C' || data[7] != 'A')
                throw new InvalidDataException(Loc.L("SCCA シグネチャがありません", "SCCA signature not found"));

            int version = BitConverter.ToInt32(data, 0);
            int offset, count;
            switch (version)
            {
                case 17: offset = 0x78; count = 1; break;
                case 23: offset = 0x80; count = 1; break;
                case 26:
                case 30:
                case 31: offset = 0x80; count = 8; break;
                default: throw new InvalidDataException(Loc.L("未対応の Prefetch バージョン ", "Unsupported Prefetch version ") + version);
            }

            var list = new List<DateTime>();
            for (int i = 0; i < count; i++)
            {
                long ft = BitConverter.ToInt64(data, offset + i * 8);
                if (ft <= 0) continue;
                try
                {
                    var t = DateTime.FromFileTimeUtc(ft);
                    if (t.Year >= 2000 && t <= DateTime.UtcNow.AddDays(1)) list.Add(t.ToLocalTime());
                }
                catch { }
            }
            return list.OrderByDescending(t => t).ToList();
        }
    }
}
