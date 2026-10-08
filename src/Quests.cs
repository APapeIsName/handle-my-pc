using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PlayTimer
{
    // 체크리스트의 한 항목. 매일 반복하는 습관(일일 퀘스트)이거나 한 번만 하는 할 일이다.
    class Quest
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public bool Daily = true;
        public bool[] Days = { true, true, true, true, true, true, true }; // DayOfWeek 순서(일=0)
        public int TargetMinutes;                                         // 0이면 목표 없음
        public List<string> Apps = new List<string>();                    // 집중할 때 허용할 앱(프로세스 이름)
        public string DoneDay = "";                                       // 완료한 (논리적) 날짜
        public string ProgressDay = "";
        public double ProgressSeconds;

        public bool IsDone(string day)
        {
            return Daily ? DoneDay == day : DoneDay != "";
        }

        public bool ScheduledOn(DayOfWeek dow)
        {
            return !Daily || Days[(int)dow];
        }

        // 한 번만 하는 할 일은 끝낸 날까지만 목록에 보인다.
        public bool VisibleOn(string day)
        {
            return Daily || DoneDay == "" || DoneDay == day;
        }

        public double Progress(string day)
        {
            return ProgressDay == day ? ProgressSeconds : 0;
        }

        public void AddProgress(string day, double seconds)
        {
            if (ProgressDay != day) { ProgressDay = day; ProgressSeconds = 0; }
            ProgressSeconds += seconds;
        }

        public bool TargetReached(string day)
        {
            return TargetMinutes > 0 && Progress(day) >= TargetMinutes * 60.0;
        }

        public string DaysText()
        {
            if (!Daily) return "한 번";
            int count = 0;
            foreach (bool b in Days) if (b) count++;
            if (count == 7) return "매일";
            if (count == 5 && !Days[0] && !Days[6]) return "평일";
            if (count == 2 && Days[0] && Days[6]) return "주말";
            var parts = new List<string>();
            foreach (var d in ScheduleForm.Order) if (Days[(int)d]) parts.Add(ScheduleForm.DayNames[(int)d]);
            return parts.Count == 0 ? "요일 없음" : string.Join("·", parts.ToArray());
        }

        public Quest Clone()
        {
            var q = (Quest)MemberwiseClone();
            q.Days = (bool[])Days.Clone();
            q.Apps = new List<string>(Apps);
            return q;
        }
    }

    // 퀘스트 목록과 경험치·연속 달성 기록. %LOCALAPPDATA%\PlayTimer\quests.txt 에 저장한다.
    class QuestStore
    {
        public readonly List<Quest> Quests = new List<Quest>();
        public int Xp;
        public int Streak;
        public string StreakDay = "";
        public bool LockFreeTime = true;  // 일일 퀘스트를 끝내야 자유 시간이 열린다

        public const int XpPerLevel = 100;

        public int Level { get { return 1 + Xp / XpPerLevel; } }

        public List<Quest> DailyFor(DayOfWeek dow)
        {
            return Quests.FindAll(q => q.Daily && q.ScheduledOn(dow));
        }

        public int DailyDone(string day, DayOfWeek dow)
        {
            return DailyFor(dow).FindAll(q => q.IsDone(day)).Count;
        }

        public bool HasUnfinishedDaily(string day, DayOfWeek dow)
        {
            return DailyFor(dow).Exists(q => !q.IsDone(day));
        }

        // 오늘 할 것이 남아 있는가(일일 퀘스트 + 아직 안 끝낸 할 일)
        public List<Quest> Todo(string day, DayOfWeek dow)
        {
            return Quests.FindAll(q => q.ScheduledOn(dow) && q.VisibleOn(day) && !q.IsDone(day));
        }

        public int CurrentStreak(string today, string yesterday)
        {
            return StreakDay == today || StreakDay == yesterday ? Streak : 0;
        }

        // 완료 처리. 오늘 일일 퀘스트를 모두 끝냈으면 true.
        public bool Complete(Quest q, string day, string yesterday, DayOfWeek dow, int xp)
        {
            if (q.IsDone(day)) return false;
            q.DoneDay = day;
            Xp += xp;
            bool allDaily = q.Daily && !HasUnfinishedDaily(day, dow);
            if (allDaily && StreakDay != day)
            {
                Streak = StreakDay == yesterday ? Streak + 1 : 1;
                StreakDay = day;
            }
            return allDaily;
        }

        public void Uncomplete(Quest q, string day, string yesterday, int xp)
        {
            if (!q.IsDone(day)) return;
            q.DoneDay = "";
            Xp = Math.Max(0, Xp - xp);
            if (q.Daily && StreakDay == day)
            {
                Streak = Math.Max(0, Streak - 1);
                StreakDay = Streak > 0 ? yesterday : "";
            }
        }

        public Quest Find(string id)
        {
            return Quests.Find(q => q.Id == id);
        }

        public static QuestStore Load(string path)
        {
            var s = new QuestStore();
            if (!File.Exists(path)) return s;
            Quest cur = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                if (line == "[quest]") { cur = new Quest(); s.Quests.Add(cur); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                if (cur == null)
                {
                    if (key == "xp") int.TryParse(val, out s.Xp);
                    else if (key == "streak") int.TryParse(val, out s.Streak);
                    else if (key == "streakDay") s.StreakDay = val;
                    else if (key == "lockFree") s.LockFreeTime = val == "1";
                    continue;
                }
                switch (key)
                {
                    case "id": cur.Id = val; break;
                    case "title": cur.Title = val; break;
                    case "daily": cur.Daily = val == "1"; break;
                    case "days":
                        if (val.Length == 7) for (int i = 0; i < 7; i++) cur.Days[i] = val[i] == '1';
                        break;
                    case "target": int.TryParse(val, out cur.TargetMinutes); break;
                    case "apps":
                        cur.Apps.Clear();
                        foreach (var a in val.Split('|')) if (a.Trim().Length > 0) cur.Apps.Add(a.Trim());
                        break;
                    case "done": cur.DoneDay = val; break;
                    case "progressDay": cur.ProgressDay = val; break;
                    case "progress":
                        double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cur.ProgressSeconds);
                        break;
                }
            }
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.AppendLine("xp=" + Xp);
            sb.AppendLine("streak=" + Streak);
            sb.AppendLine("streakDay=" + StreakDay);
            sb.AppendLine("lockFree=" + (LockFreeTime ? "1" : "0"));
            foreach (var q in Quests)
            {
                sb.AppendLine("[quest]");
                sb.AppendLine("id=" + q.Id);
                sb.AppendLine("title=" + q.Title.Replace("\r", " ").Replace("\n", " "));
                sb.AppendLine("daily=" + (q.Daily ? "1" : "0"));
                var days = new char[7];
                for (int i = 0; i < 7; i++) days[i] = q.Days[i] ? '1' : '0';
                sb.AppendLine("days=" + new string(days));
                sb.AppendLine("target=" + q.TargetMinutes);
                sb.AppendLine("apps=" + string.Join("|", q.Apps.ToArray()));
                sb.AppendLine("done=" + q.DoneDay);
                sb.AppendLine("progressDay=" + q.ProgressDay);
                sb.AppendLine("progress=" + q.ProgressSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
