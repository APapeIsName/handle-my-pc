using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayTimer
{
    enum UsageKind { Free = 'f', Quest = 'q', Idle = 'i' }

    // 같은 앱을 같은 방식으로 쭉 쓴 구간. 시각은 그 (논리적) 하루가 시작한 때로부터의 초.
    class UsageSegment
    {
        public int Start, End;
        public UsageKind Kind;
        public string App = "";
        public string Quest = "";

        public int Seconds { get { return Math.Max(0, End - Start); } }
    }

    class UsageDone
    {
        public int At;
        public string Title = "";
    }

    // 하루치 사용 기록. %LOCALAPPDATA%\PlayTimer\history\yyyy-MM-dd.txt
    class DayLog
    {
        public string Day = "";
        public readonly List<UsageSegment> Segments = new List<UsageSegment>();
        public readonly List<UsageDone> Done = new List<UsageDone>();
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>();

        // 이 간격(초)보다 짧게 끊긴 같은 앱은 한 구간으로 이어 붙인다.
        const int JoinGap = 5;

        public void Record(int offset, int seconds, UsageKind kind, string app, string quest)
        {
            if (seconds <= 0) return;
            app = app ?? "";
            quest = quest ?? "";
            var last = Segments.Count > 0 ? Segments[Segments.Count - 1] : null;
            if (last != null && last.Kind == kind && last.App == app && last.Quest == quest && offset - last.End <= JoinGap && offset >= last.End)
            {
                last.End = offset;
                return;
            }
            int start = Math.Max(last != null ? last.End : 0, offset - seconds);
            Segments.Add(new UsageSegment { Start = start, End = offset, Kind = kind, App = app, Quest = quest });
        }

        // at 이후 기록을 잘라 낸다(입력이 없던 시간을 뒤늦게 "자리 비움"으로 바꿀 때).
        public void TrimAfter(int at)
        {
            while (Segments.Count > 0)
            {
                var last = Segments[Segments.Count - 1];
                if (last.Start >= at && last.Kind != UsageKind.Idle) { Segments.RemoveAt(Segments.Count - 1); continue; }
                if (last.End > at && last.Kind != UsageKind.Idle) last.End = at;
                break;
            }
        }

        public string NameOf(string app)
        {
            string n;
            return Names.TryGetValue(app, out n) && n.Length > 0 ? n : app;
        }

        public int Total(UsageKind kind)
        {
            int s = 0;
            foreach (var seg in Segments) if (seg.Kind == kind) s += seg.Seconds;
            return s;
        }

        public int ActiveSeconds { get { return Total(UsageKind.Free) + Total(UsageKind.Quest); } }

        // 앱별 (놀이, 퀘스트) 사용 초. 자리 비움은 뺀다.
        public List<KeyValuePair<string, int[]>> ByApp()
        {
            var map = new Dictionary<string, int[]>();
            foreach (var seg in Segments)
            {
                if (seg.Kind == UsageKind.Idle) continue;
                int[] v;
                if (!map.TryGetValue(seg.App, out v)) map[seg.App] = v = new int[2];
                v[seg.Kind == UsageKind.Quest ? 1 : 0] += seg.Seconds;
            }
            var list = new List<KeyValuePair<string, int[]>>(map);
            list.Sort((a, b) => (b.Value[0] + b.Value[1]).CompareTo(a.Value[0] + a.Value[1]));
            return list;
        }

        // 컴퓨터를 실제로 쓴 구간(자리 비움 제외). gap 초 이상 끊기면 다른 구간으로 본다.
        public List<UsageSegment> Sessions(int gap)
        {
            var result = new List<UsageSegment>();
            UsageSegment cur = null;
            foreach (var seg in Segments)
            {
                if (seg.Kind == UsageKind.Idle) continue;
                if (cur != null && seg.Start - cur.End <= gap) { cur.End = Math.Max(cur.End, seg.End); continue; }
                cur = new UsageSegment { Start = seg.Start, End = seg.End };
                result.Add(cur);
            }
            return result;
        }

        // 구간 안에서 가장 오래 쓴 앱들
        public List<string> TopAppsIn(int start, int end, int count)
        {
            var map = new Dictionary<string, int>();
            foreach (var seg in Segments)
            {
                if (seg.Kind == UsageKind.Idle || seg.End <= start || seg.Start >= end) continue;
                int s = Math.Min(seg.End, end) - Math.Max(seg.Start, start);
                int v;
                map.TryGetValue(seg.App, out v);
                map[seg.App] = v + s;
            }
            var list = new List<KeyValuePair<string, int>>(map);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var names = new List<string>();
            for (int i = 0; i < list.Count && i < count; i++) names.Add(NameOf(list[i].Key));
            return names;
        }

        public static DayLog Load(string path, string day)
        {
            var log = new DayLog { Day = day };
            if (!File.Exists(path)) return log;
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split('\t');
                try
                {
                    if (p[0] == "name" && p.Length >= 3) log.Names[p[1]] = p[2];
                    else if (p[0] == "seg" && p.Length >= 5)
                        log.Segments.Add(new UsageSegment
                        {
                            Start = int.Parse(p[1], CultureInfo.InvariantCulture),
                            End = int.Parse(p[2], CultureInfo.InvariantCulture),
                            Kind = (UsageKind)p[3][0],
                            App = p[4],
                            Quest = p.Length > 5 ? p[5] : ""
                        });
                    else if (p[0] == "done" && p.Length >= 3)
                        log.Done.Add(new UsageDone { At = int.Parse(p[1], CultureInfo.InvariantCulture), Title = p[2] });
                }
                catch { }
            }
            return log;
        }

        static string Clean(string s)
        {
            return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.AppendLine("#PlayTimer usage v1");
            foreach (var kv in Names) sb.AppendLine("name\t" + Clean(kv.Key) + "\t" + Clean(kv.Value));
            foreach (var s in Segments)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "seg\t{0}\t{1}\t{2}\t{3}\t{4}", s.Start, s.End, (char)s.Kind, Clean(s.App), Clean(s.Quest)));
            foreach (var d in Done)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "done\t{0}\t{1}", d.At, Clean(d.Title)));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }

    // 날짜별 기록 파일을 다룬다.
    class UsageHistory
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        readonly string dir;
        readonly int resetHour;
        DayLog today;
        static readonly Dictionary<string, string> nameCache = new Dictionary<string, string>();

        public UsageHistory(string dir, int resetHour, int keepDays)
        {
            this.dir = dir;
            this.resetHour = resetHour;
            Cleanup(keepDays);
        }

        public string PathFor(string day) { return Path.Combine(dir, day + ".txt"); }

        public DateTime DayStart(string day)
        {
            return DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddHours(resetHour);
        }

        public DayLog Today(string day)
        {
            if (today == null || today.Day != day)
            {
                if (today != null) Save();
                today = DayLog.Load(PathFor(day), day);
            }
            return today;
        }

        public DayLog Load(string day)
        {
            if (today != null && today.Day == day) return today;
            return DayLog.Load(PathFor(day), day);
        }

        public bool Exists(string day)
        {
            return (today != null && today.Day == day) || File.Exists(PathFor(day));
        }

        public void Save()
        {
            if (today == null) return;
            try { today.Save(PathFor(today.Day)); } catch { }
        }

        public void Record(string day, DateTime nowLocal, double seconds, UsageKind kind, string app, string quest, double idleSince)
        {
            var log = Today(day);
            int offset = (int)(nowLocal - DayStart(day)).TotalSeconds;
            if (offset < 0 || offset > 86400 + 3600) return;
            if (!string.IsNullOrEmpty(app) && !log.Names.ContainsKey(app)) log.Names[app] = DisplayName(app);
            var last = log.Segments.Count > 0 ? log.Segments[log.Segments.Count - 1] : null;
            if (kind == UsageKind.Idle && idleSince > 0 && (last == null || last.Kind != UsageKind.Idle))
            {
                // 입력이 끊긴 순간부터를 자리 비움으로 고쳐 쓴다.
                int from = Math.Max(0, offset - (int)idleSince);
                log.TrimAfter(from);
                seconds = offset - Math.Max(from, log.Segments.Count > 0 ? log.Segments[log.Segments.Count - 1].End : 0);
            }
            log.Record(offset, (int)Math.Round(seconds), kind, app, quest);
        }

        public void AddDone(string day, DateTime nowLocal, string title)
        {
            var log = Today(day);
            log.Done.Add(new UsageDone { At = (int)(nowLocal - DayStart(day)).TotalSeconds, Title = title });
        }

        public void RemoveDone(string day, string title)
        {
            var log = Today(day);
            int i = log.Done.FindLastIndex(d => d.Title == title);
            if (i >= 0) log.Done.RemoveAt(i);
        }

        void Cleanup(int keepDays)
        {
            if (keepDays <= 0) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                var cutoff = DateTime.Now.AddDays(-keepDays).ToString("yyyy-MM-dd");
                foreach (var f in Directory.GetFiles(dir, "????-??-??.txt"))
                    if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(f), cutoff) < 0) File.Delete(f);
            }
            catch { }
        }

        // 마지막 키보드·마우스 입력 뒤로 지난 초
        public static double IdleSeconds()
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref info)) return 0;
                return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
            }
            catch { return 0; }
        }

        // "chrome" → "Google Chrome" (실행 파일의 설명). 알 수 없으면 프로세스 이름 그대로.
        public static string DisplayName(string app)
        {
            string name;
            if (nameCache.TryGetValue(app, out name)) return name;
            name = app;
            if (app == "playtimer") name = "PlayTimer";
            else if (app == "explorer") name = "Windows 탐색기·바탕 화면";
            else
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(app))
                    {
                        try
                        {
                            var desc = p.MainModule.FileVersionInfo.FileDescription;
                            if (!string.IsNullOrEmpty(desc) && desc.Trim().Length > 0) { name = desc.Trim(); break; }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }
            nameCache[app] = name;
            return name;
        }
    }
}
