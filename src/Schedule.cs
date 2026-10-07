using System;
using System.IO;
using System.Text;

namespace PlayTimer
{
    // 요일별 하루 총량과 30분 단위 허용 시간대.
    // 하루는 ResetHour 시각에 시작하므로, 슬롯 0은 그 요일의 ResetHour:00 이다.
    // (ResetHour=4 이면 금요일 칸의 마지막 슬롯은 토요일 03:30 ~ 04:00)
    class Schedule
    {
        public const int SlotMinutes = 30;
        public const int SlotsPerDay = 24 * 60 / SlotMinutes;

        static readonly string[] Keys = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        // DayOfWeek 순서(일=0)로 저장한다.
        public readonly int[] LimitMinutes = new int[7];
        public readonly bool[][] Allowed = new bool[7][];

        public Schedule(int defaultLimitMinutes)
        {
            for (int d = 0; d < 7; d++)
            {
                LimitMinutes[d] = defaultLimitMinutes;
                Allowed[d] = new bool[SlotsPerDay];
                for (int s = 0; s < SlotsPerDay; s++) Allowed[d][s] = true;
            }
        }

        public Schedule Clone()
        {
            var c = new Schedule(0);
            for (int d = 0; d < 7; d++)
            {
                c.LimitMinutes[d] = LimitMinutes[d];
                Array.Copy(Allowed[d], c.Allowed[d], SlotsPerDay);
            }
            return c;
        }

        public static Schedule Load(string path, int defaultLimitMinutes)
        {
            var s = new Schedule(defaultLimitMinutes);
            if (!File.Exists(path)) return s;
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                int dot = line.IndexOf('.');
                if (eq < 0 || dot < 0 || dot > eq) continue;
                int d = Array.IndexOf(Keys, line.Substring(0, dot));
                if (d < 0) continue;
                string field = line.Substring(dot + 1, eq - dot - 1);
                string val = line.Substring(eq + 1).Trim();
                if (field == "limit")
                {
                    int n;
                    if (int.TryParse(val, out n) && n >= 0) s.LimitMinutes[d] = Math.Min(n, 24 * 60);
                }
                else if (field == "slots" && val.Length == SlotsPerDay)
                {
                    for (int i = 0; i < SlotsPerDay; i++) s.Allowed[d][i] = val[i] == '1';
                }
            }
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.AppendLine("# PlayTimer 시간표 (트레이 메뉴의 '시간 설정'에서 편집하세요)");
            for (int d = 0; d < 7; d++)
            {
                sb.AppendLine(Keys[d] + ".limit=" + LimitMinutes[d]);
                var slots = new char[SlotsPerDay];
                for (int i = 0; i < SlotsPerDay; i++) slots[i] = Allowed[d][i] ? '1' : '0';
                sb.AppendLine(Keys[d] + ".slots=" + new string(slots));
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public int AllowedMinutes(int day)
        {
            int n = 0;
            foreach (bool b in Allowed[day]) if (b) n++;
            return n * SlotMinutes;
        }

        // logical: 실제 시각에서 ResetHour 만큼 뺀 "논리적 시각".
        public bool IsAllowed(DateTime logical)
        {
            int slot = (int)(logical.TimeOfDay.TotalMinutes / SlotMinutes);
            return Allowed[(int)logical.DayOfWeek][slot];
        }

        // 지금 허용 시간대 안이라면, 그 시간대가 끝날 때까지 남은 초. 밖이면 0.
        public double SecondsUntilWindowEnd(DateTime logical)
        {
            if (!IsAllowed(logical)) return 0;
            DateTime t = logical.Date.AddMinutes(((int)(logical.TimeOfDay.TotalMinutes / SlotMinutes) + 1) * SlotMinutes);
            DateTime cap = logical.AddDays(7);
            while (t < cap && IsAllowed(t)) t = t.AddMinutes(SlotMinutes);
            return (t - logical).TotalSeconds;
        }
    }
}
