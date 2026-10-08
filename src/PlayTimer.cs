// PlayTimer - PC방처럼 하루 사용 시간을 재고, 시간이 끝나면 끄라고 계속 조르는 트레이 앱.
// 윈도우에 기본 포함된 .NET Framework 4.x 컴파일러(csc.exe)로 빌드할 수 있도록 C# 5 문법만 사용한다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PlayTimer
{
    static class Program
    {
        public const string ShowEventName = "PlayTimer_ShowSettings";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "PlayTimer_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // 이미 실행 중이면 실행 중인 쪽에 "설정 창 열어"라고 알리고 끝낸다.
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return;
                }
                try { SetProcessDPIAware(); } catch { }
                Ui.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    class Config
    {
        public int DailyLimitMinutes = 180;
        public int ResetHour = 4;
        public int[] WarnAtMinutes = new int[] { 10, 5, 1 };
        public int Stage2AfterMinutes = 10;
        public int Stage3AfterMinutes = 20;
        public int Stage1NagSeconds = 120;
        public int Stage2NagSeconds = 60;
        public int Stage3NagSeconds = 60;
        public int Stage2CloseDelaySeconds = 10;
        public int Stage3CloseDelaySeconds = 20;
        public int ExtensionMinutes = 30;
        public int MaxExtensionsPerDay = 1;
        public bool ShowQuestsOnStart = true;
        public int FocusBlockDelaySeconds = 3;
        public string[] AlwaysAllowedApps = new string[0];
        public int QuestXp = 10;
        public int IdleMinutes = 5;
        public int HistoryDays = 90;
        public int BedtimeWarnMinutes = 30;
        public int BedtimeStartStage = 2;

        public static Config Load(string path)
        {
            var c = new Config();
            if (!File.Exists(path)) return c;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                try
                {
                    switch (key)
                    {
                        case "DailyLimitMinutes": c.DailyLimitMinutes = int.Parse(val); break;
                        case "ResetHour": c.ResetHour = int.Parse(val); break;
                        case "WarnAtMinutes": c.WarnAtMinutes = ParseList(val); break;
                        case "Stage2AfterMinutes": c.Stage2AfterMinutes = int.Parse(val); break;
                        case "Stage3AfterMinutes": c.Stage3AfterMinutes = int.Parse(val); break;
                        case "Stage1NagSeconds": c.Stage1NagSeconds = int.Parse(val); break;
                        case "Stage2NagSeconds": c.Stage2NagSeconds = int.Parse(val); break;
                        case "Stage3NagSeconds": c.Stage3NagSeconds = int.Parse(val); break;
                        case "Stage2CloseDelaySeconds": c.Stage2CloseDelaySeconds = int.Parse(val); break;
                        case "Stage3CloseDelaySeconds": c.Stage3CloseDelaySeconds = int.Parse(val); break;
                        case "ExtensionMinutes": c.ExtensionMinutes = int.Parse(val); break;
                        case "MaxExtensionsPerDay": c.MaxExtensionsPerDay = int.Parse(val); break;
                        case "ShowQuestsOnStart": c.ShowQuestsOnStart = val == "1" || val.ToLowerInvariant() == "true"; break;
                        case "FocusBlockDelaySeconds": c.FocusBlockDelaySeconds = int.Parse(val); break;
                        case "AlwaysAllowedApps": c.AlwaysAllowedApps = val.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries); break;
                        case "QuestXp": c.QuestXp = int.Parse(val); break;
                        case "IdleMinutes": c.IdleMinutes = int.Parse(val); break;
                        case "HistoryDays": c.HistoryDays = int.Parse(val); break;
                        case "BedtimeWarnMinutes": c.BedtimeWarnMinutes = int.Parse(val); break;
                        case "BedtimeStartStage": c.BedtimeStartStage = Math.Max(1, Math.Min(3, int.Parse(val))); break;
                    }
                }
                catch (FormatException) { }
            }
            return c;
        }

        static int[] ParseList(string val)
        {
            var list = new List<int>();
            foreach (var part in val.Split(','))
            {
                int n;
                if (int.TryParse(part.Trim(), out n) && n > 0) list.Add(n);
            }
            return list.ToArray();
        }
    }

    // 하루 단위 사용 기록. %LOCALAPPDATA%\PlayTimer\state.txt 에 저장되어 재부팅해도 유지된다.
    class State
    {
        public string Day = "";
        public double UsedSeconds;
        public int ExtensionsUsed;
        // 시간대 밖이어도 연장으로 허용된 시각(UTC ticks)
        public long GraceUntilUtcTicks;

        public static State Load(string path)
        {
            var s = new State();
            if (!File.Exists(path)) return s;
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq);
                string val = line.Substring(eq + 1);
                if (key == "day") s.Day = val;
                else if (key == "used") double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.UsedSeconds);
                else if (key == "ext") int.TryParse(val, out s.ExtensionsUsed);
                else if (key == "grace") long.TryParse(val, out s.GraceUntilUtcTicks);
            }
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllLines(tmp, new string[] {
                "day=" + Day,
                "used=" + UsedSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "ext=" + ExtensionsUsed,
                "grace=" + GraceUntilUtcTicks
            });
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }


    class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        readonly Config config;
        readonly string statePath;
        readonly string schedulePath;
        readonly string questsPath;
        State state;
        Schedule schedule;
        QuestStore quests;
        FocusSession focus;
        FocusHud hud;
        QuestBoard board;
        readonly UsageHistory history;
        HistoryForm historyForm;
        readonly System.Windows.Forms.Timer guard;
        IntPtr badWindow = IntPtr.Zero;
        DateTime badSince;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer tick;
        readonly ToolStripLabel menuHeader, menuSub;
        readonly ToolStripMenuItem extendItem, stopFocusItem;
        readonly EventWaitHandle showEvent;
        readonly SynchronizationContext ui;

        DateTime lastTick = DateTime.UtcNow;
        DateTime lastSave = DateTime.UtcNow;
        DateTime lastNagClosed = DateTime.MinValue;
        DateTime flyoutClosedAt = DateTime.MinValue;
        double outsideSeconds;
        double prevRemaining = double.NaN;
        double prevBedtime = double.NaN;
        bool locked;
        bool shuttingDown;
        Form activeNag;
        int activeStage;
        StatusFlyout flyout;
        ScheduleForm scheduleForm;
        ShutdownNotice shutdownNotice;
        string lastIconKey;
        IntPtr lastIconHandle = IntPtr.Zero;

        public event EventHandler QuestsChanged;

        // 코드에서 조르기 창을 닫을 때는 "아직 못 닫음" 잠금을 무시한다.
        public bool ClosingByApp { get; private set; }

        public TrayApp()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            config = Config.Load(Path.Combine(exeDir, "config.ini"));
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayTimer");
            statePath = Path.Combine(dataDir, "state.txt");
            schedulePath = Path.Combine(dataDir, "schedule.txt");
            questsPath = Path.Combine(dataDir, "quests.txt");
            bool firstRun = !File.Exists(schedulePath);
            state = State.Load(statePath);
            schedule = Schedule.Load(schedulePath, config.DailyLimitMinutes);
            quests = QuestStore.Load(questsPath);
            history = new UsageHistory(Path.Combine(dataDir, "history"), config.ResetHour, config.HistoryDays);
            RollDayIfNeeded();

            menuHeader = new ToolStripLabel { Font = Ui.Font(11f, FontStyle.Bold), Margin = Ui.Pad(4, 6, 4, 0) };
            menuSub = new ToolStripLabel { ForeColor = Ui.SubText, Margin = Ui.Pad(4, 0, 4, 6) };
            extendItem = new ToolStripMenuItem("", null, delegate { RequestExtend(null); });
            stopFocusItem = new ToolStripMenuItem("집중 그만하기", null, delegate { StopFocus(true); });
            var menu = new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
            menu.Items.Add(menuHeader);
            menu.Items.Add(menuSub);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("오늘의 퀘스트...", null, delegate { OpenQuests(false); }) { Font = Ui.Font(9.5f, FontStyle.Bold) });
            menu.Items.Add(stopFocusItem);
            menu.Items.Add(new ToolStripMenuItem("사용 기록...", null, delegate { OpenHistory(); }));
            menu.Items.Add(new ToolStripMenuItem("시간 설정...", null, delegate { OpenSchedule(); }));
            menu.Items.Add(extendItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("PC 끄기", null, delegate { RequestShutdown(null); }));
            menu.Items.Add(new ToolStripMenuItem("타이머 종료", null, delegate { ConfirmExit(); }));
            menu.Opening += delegate { UpdateMenu(); };

            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
            tray.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenQuests(false); };

            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { ui.Post(delegate { OpenQuests(false); }, null); }, null, -1, false);

            SystemEvents.SessionSwitch += OnSessionSwitch;

            tick = new System.Windows.Forms.Timer { Interval = 1000 };
            tick.Tick += delegate { OnTick(); };
            tick.Start();

            guard = new System.Windows.Forms.Timer { Interval = 300 };
            guard.Tick += delegate { GuardApps(); };
            guard.Start();

            UpdateTray();
            // 켜자마자 조르지 않도록, 첫 알림까지 여유를 둔다.
            lastNagClosed = DateTime.UtcNow;
            if (firstRun)
            {
                Toast.Show("PlayTimer를 시작했어요", "요일별 사용 시간과 시간대를 정해 주세요. 트레이 아이콘을 더블클릭하면 오늘의 퀘스트를 적을 수 있어요.", Ui.Blue);
                OpenSchedule();
            }
            else if (config.ShowQuestsOnStart && quests.Todo(Today, TodayDow).Count > 0)
            {
                OpenQuests(true);
            }
            else if (RemainingSeconds() > 0)
            {
                Toast.Show("남은 시간 " + Ui.Clock(RemainingSeconds()), WindowLineShort(), Ui.Blue);
            }
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect)
                locked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
                locked = false;
        }

        // ── 시간 계산 ─────────────────────────────────────────────

        DateTime Logical()
        {
            return DateTime.Now.AddHours(-config.ResetHour);
        }

        bool RollDayIfNeeded()
        {
            string today = Logical().ToString("yyyy-MM-dd");
            if (state.Day == today) return false;
            state = new State { Day = today, GraceUntilUtcTicks = state.GraceUntilUtcTicks };
            SaveState();
            return true;
        }

        // ── 퀘스트용 날짜 ────────────────────────────────────────

        public string Today { get { return Logical().ToString("yyyy-MM-dd"); } }
        public string Yesterday { get { return Logical().AddDays(-1).ToString("yyyy-MM-dd"); } }
        public DayOfWeek TodayDow { get { return Logical().DayOfWeek; } }
        public string DayLabel
        {
            get
            {
                var l = Logical();
                return string.Format("{0}월 {1}일 ({2})", l.Month, l.Day, ScheduleForm.DayNames[(int)l.DayOfWeek]);
            }
        }
        public QuestStore Quests { get { return quests; } }
        public FocusSession Focus { get { return focus; } }

        // 일일 퀘스트를 끝내야 자유 시간이 열리는 설정이고, 아직 남았는가
        bool Gated()
        {
            // 퀘스트를 하고 있는 동안은 "퀘스트 먼저" 잠금을 풀어 둔다.
            if (focus != null) return false;
            return quests.LockFreeTime && quests.HasUnfinishedDaily(Today, TodayDow);
        }

        double LimitSeconds()
        {
            int today = (int)Logical().DayOfWeek;
            return (schedule.LimitMinutes[today] + state.ExtensionsUsed * config.ExtensionMinutes) * 60.0;
        }

        double QuotaRemaining()
        {
            return LimitSeconds() - state.UsedSeconds;
        }

        double GraceRemaining()
        {
            return (new DateTime(state.GraceUntilUtcTicks, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
        }

        // 허용 시간대 기준 남은 시간. 시간대 밖이면 밖에서 쓴 시간만큼 음수.
        double WindowRemaining()
        {
            double g = GraceRemaining();
            if (Gated()) return g > 0 ? g : -outsideSeconds;
            double w = schedule.SecondsUntilWindowEnd(Logical());
            if (w > 0 || g > 0) return Math.Max(w, g);
            return -outsideSeconds;
        }

        // 취침 시각까지 남은 초(지났으면 음수). 취침 시각이 없으면 double.MaxValue.
        double BedtimeRemaining()
        {
            return schedule.SecondsUntilBedtime(Logical());
        }

        // 취침 시각이 지났는가. 지나면 남은 시간·연장·집중과 상관없이 종료를 알린다.
        bool PastBedtime()
        {
            return BedtimeRemaining() <= 0;
        }

        // 총량, 시간대, 취침 중 먼저 끝나는 쪽
        double RemainingSeconds()
        {
            return Math.Min(Math.Min(QuotaRemaining(), WindowRemaining()), BedtimeRemaining());
        }

        // 지금 남은 시간을 정하는 게 취침 시각인가
        bool BedtimeBinding()
        {
            double b = BedtimeRemaining();
            return b != double.MaxValue && b <= Math.Min(QuotaRemaining(), WindowRemaining());
        }

        string BedtimeText()
        {
            int bed = schedule.BedtimeMinutes[(int)Logical().DayOfWeek];
            return bed > 0 ? DayHeader.BedClock(config.ResetHour, bed) : null;
        }

        // 논리적 시각을 사람이 읽는 시각으로: "22:00", "내일 18:00", "토요일 10:00"
        string When(DateTime logical)
        {
            DateTime real = logical.AddHours(config.ResetHour);
            DateTime today = DateTime.Now.Date;
            string hm = real.ToString("HH:mm");
            if (real.Date == today) return hm;
            if (real.Date == today.AddDays(1)) return "내일 " + hm;
            return ScheduleForm.DayNames[(int)real.DayOfWeek] + "요일 " + hm;
        }

        string WindowLineShort()
        {
            if (PastBedtime()) return "잘 시간이 지났어요 (" + BedtimeText() + ").";
            if (focus != null && !focus.Quest.CountsAsPlay) return "집중 중: " + focus.Quest.Title;
            var logical = Logical();
            if (Gated() && GraceRemaining() <= 0) return "일일 퀘스트를 끝내면 자유 시간이 열려요.";
            if (schedule.AlwaysAllowed()) return "시간대 제한 없이 총량만 적용돼요.";
            DateTime start, end;
            if (GraceRemaining() > 0 && !schedule.IsAllowed(logical))
                return "연장 중이에요. " + When(logical.AddSeconds(GraceRemaining())) + "까지";
            if (schedule.CurrentWindow(logical, out start, out end))
                return (end - logical).TotalDays >= 6 ? "지금 사용 가능해요." : "지금 사용 가능 · " + When(end) + "까지";
            DateTime? next = schedule.NextWindowStart(logical);
            return next.HasValue ? "지금은 쉬는 시간 · 다음 " + When(next.Value) : "이번 주에는 사용 가능한 시간대가 없어요.";
        }

        static string Seconds(int s)
        {
            return s % 60 == 0 ? (s / 60) + "분" : s + "초";
        }

        int StageFor(double overSeconds)
        {
            double overMin = overSeconds / 60.0;
            int stage = overMin >= config.Stage3AfterMinutes ? 3 : overMin >= config.Stage2AfterMinutes ? 2 : 1;
            // 잘 시간은 처음부터 강하게(기본: 화면 가운데 창부터) 알린다.
            if (PastBedtime()) stage = Math.Max(stage, config.BedtimeStartStage);
            return stage;
        }

        public Status GetStatus()
        {
            var st = new Status();
            var logical = Logical();
            st.Remaining = RemainingSeconds();
            st.UsedSeconds = state.UsedSeconds;
            st.LimitSeconds = LimitSeconds();
            st.QuotaBinding = QuotaRemaining() <= WindowRemaining();
            st.InWindow = (focus != null && !focus.Quest.CountsAsPlay) || (schedule.IsAllowed(logical) && !Gated()) || GraceRemaining() > 0;
            st.WindowLine = "●  " + WindowLineShort();
            st.DayLabel = string.Format("{0}월 {1}일 ({2})", logical.Month, logical.Day, ScheduleForm.DayNames[(int)logical.DayOfWeek]);
            st.ExtensionsLeft = Math.Max(0, config.MaxExtensionsPerDay - state.ExtensionsUsed);
            st.ExtensionMinutes = config.ExtensionMinutes;
            st.Bedtime = BedtimeText();

            var todo = quests.Todo(Today, TodayDow);
            st.HasTodo = todo.Count > 0;
            bool bedNow = PastBedtime();
            if (PastBedtime())
            {
                // 잘 시간에는 연장도, 퀘스트로 넘어가는 것도 없다.
                st.ExtensionsLeft = 0;
                st.HasTodo = false;
            }
            int total = quests.DailyFor(TodayDow).Count;
            var questParts = new List<string>();
            if (total > 0) questParts.Add(string.Format("일일 퀘스트 {0}/{1}", quests.DailyDone(Today, TodayDow), total));
            int streak = quests.CurrentStreak(Today, Yesterday);
            if (streak > 0) questParts.Add("연속 " + streak + "일");
            questParts.Add("Lv " + quests.Level);
            st.QuestLine = string.Join(" · ", questParts.ToArray());
            st.InFocus = focus != null;

            if (bedNow)
            {
                st.Tag = "잘 시간";
                st.Reason = "잘 시간이에요";
                st.ReasonDetail = "취침 시간 " + BedtimeText() + "이 지났어요. 남은 시간과 상관없이 오늘은 여기까지 하고 쉬어요.";
            }
            else if (QuotaRemaining() <= 0)
            {
                st.Reason = "오늘 사용 시간이 끝났어요";
                st.ReasonDetail = "오늘 총량 " + Ui.Duration((int)(LimitSeconds() / 60)) + "을 모두 썼어요.";
            }
            else if (Gated() && GraceRemaining() <= 0)
            {
                var names = quests.DailyFor(TodayDow).FindAll(q => !q.IsDone(Today)).ConvertAll(q => q.Title);
                st.Tag = "퀘스트 먼저";
                st.Reason = "오늘 퀘스트를 먼저 해요";
                st.ReasonDetail = "남은 일일 퀘스트: " + string.Join(", ", names.ToArray()) + ". 끝내고 체크하면 자유 시간이 열려요.";
            }
            else
            {
                st.Tag = "쉬는 시간";
                st.Reason = "지금은 쉬는 시간이에요";
                DateTime? next = schedule.NextWindowStart(logical);
                st.ReasonDetail = next.HasValue ? "다음 사용 가능 시간은 " + When(next.Value) + "이에요." : "이번 주에는 남은 시간대가 없어요.";
            }

            double overMin = st.OverSeconds / 60.0;
            switch (StageFor(st.OverSeconds))
            {
                case 1:
                    st.EscalationHint = string.Format("닫으면 {0} 뒤에 다시 알려요. {1}분 뒤부터는 더 강하게 알려요.",
                        Seconds(config.Stage1NagSeconds), (int)Math.Ceiling(config.Stage2AfterMinutes - overMin));
                    break;
                case 2:
                    st.EscalationHint = string.Format("닫아도 {0} 뒤에 다시 떠요. {1}분 뒤부터는 화면 전체를 덮어요.",
                        Seconds(config.Stage2NagSeconds), (int)Math.Ceiling(config.Stage3AfterMinutes - overMin));
                    break;
                default:
                    st.EscalationHint = string.Format("닫아도 {0} 뒤에 다시 덮어요.", Seconds(config.Stage3NagSeconds));
                    break;
            }
            return st;
        }

        // ── 매초 ─────────────────────────────────────────────────

        void OnTick()
        {
            DateTime now = DateTime.UtcNow;
            double delta = (now - lastTick).TotalSeconds;
            lastTick = now;
            // 절전/최대 절전 동안의 공백은 사용 시간으로 치지 않는다.
            if (delta < 0 || delta > 5) delta = 0;

            if (RollDayIfNeeded())
            {
                NotifyQuestsChanged();
                if (config.ShowQuestsOnStart && quests.Todo(Today, TodayDow).Count > 0) OpenQuests(true);
            }
            if (locked) return;

            RecordUsage(delta);
            if ((now - lastSave).TotalSeconds >= 30) history.Save();
            if (historyForm != null && (int)now.TimeOfDay.TotalSeconds % 60 == 0) historyForm.RefreshIfToday();

            // 취침: 미리 한 번 알리고, 시각이 되면 집중 중이어도 끝낸다.
            double bedLeft = BedtimeRemaining();
            if (bedLeft != double.MaxValue && !double.IsNaN(prevBedtime) && config.BedtimeWarnMinutes > 0)
            {
                double t = config.BedtimeWarnMinutes * 60.0;
                if (prevBedtime > t && bedLeft <= t && bedLeft > 0)
                    Toast.Show(config.BedtimeWarnMinutes + "분 뒤에 잘 시간이에요", "취침 시간 " + BedtimeText() + ". 하던 걸 슬슬 마무리하세요.", Ui.Amber);
            }
            prevBedtime = bedLeft;
            if (focus != null && bedLeft <= 0)
            {
                StopFocus(false);
                Toast.Show("잘 시간이라 집중을 마쳤어요", "진행한 시간은 저장했어요. 내일 이어서 해요.", Ui.Blue);
            }

            if (focus != null)
            {
                bool wasReached = focus.Quest.TargetReached(Today);
                focus.Elapsed += delta;
                focus.Quest.AddProgress(Today, delta);
                if (!wasReached && focus.Quest.TargetReached(Today))
                    Toast.Show("목표 시간을 채웠어요", focus.Quest.Title + " · 위쪽 막대에서 완료를 눌러 마무리하세요.", Ui.Green);
            }
            if (focus != null && !focus.Quest.CountsAsPlay)
            {
                // 놀이 시간에 넣지 않는 퀘스트: 총량을 쓰지 않고, 시간대·퀘스트 알림도 멈춘다.
                outsideSeconds = 0;
                prevRemaining = double.NaN;
                if ((now - lastSave).TotalSeconds >= 30)
                {
                    SaveState();
                    SaveQuests();
                    lastSave = now;
                }
                UpdateTray();
                return;
            }

            state.UsedSeconds += delta;
            if ((schedule.IsAllowed(Logical()) && !Gated()) || GraceRemaining() > 0) outsideSeconds = 0;
            else outsideSeconds += delta;

            double before = double.IsNaN(prevRemaining) ? double.MaxValue : prevRemaining;
            double after = RemainingSeconds();
            prevRemaining = after;

            // 여러 경고 지점을 한꺼번에 지나면 가장 급한 것 하나만 보여 준다.
            int crossed = int.MaxValue;
            foreach (int w in config.WarnAtMinutes)
            {
                double t = w * 60.0;
                if (before > t && after <= t && after > 0) crossed = Math.Min(crossed, w);
            }
            if (crossed != int.MaxValue)
            {
                string body = BedtimeBinding()
                    ? "곧 잘 시간이에요(" + BedtimeText() + "). 슬슬 정리하세요."
                    : QuotaRemaining() <= WindowRemaining()
                    ? "오늘 사용 시간이 곧 끝나요. 슬슬 정리하세요."
                    : "곧 쉬는 시간이에요(" + When(Logical().AddSeconds(after)) + "부터). 슬슬 정리하세요.";
                Toast.Show(crossed + "분 남았어요", body, crossed <= 1 ? Ui.Red : Ui.Amber);
            }
            if (before > 0 && after <= 0)
                lastNagClosed = DateTime.MinValue;

            if (after <= 0) MaybeNag(-after);

            if ((now - lastSave).TotalSeconds >= 30)
            {
                SaveState();
                lastSave = now;
            }
            UpdateTray();
        }

        void MaybeNag(double overSeconds)
        {
            if (shuttingDown) return;
            int stage = StageFor(overSeconds);
            if (activeNag != null)
            {
                // 무시하고 있으면 열린 창을 다음 단계 창으로 바꾼다.
                if (stage <= activeStage) return;
                CloseNag();
                lastNagClosed = DateTime.MinValue;
            }
            int interval = stage == 3 ? config.Stage3NagSeconds : stage == 2 ? config.Stage2NagSeconds : config.Stage1NagSeconds;
            if ((DateTime.UtcNow - lastNagClosed).TotalSeconds < interval) return;

            if (flyout != null) flyout.Close();
            if (stage == 3) activeNag = new OverlayNag(this, config.Stage3CloseDelaySeconds);
            else if (stage == 2) activeNag = new NagDialog(this, config.Stage2CloseDelaySeconds);
            else activeNag = new NagCard(this);
            activeStage = stage;
            var shown = activeNag;
            shown.FormClosed += delegate
            {
                if (activeNag == shown) activeNag = null;
                lastNagClosed = DateTime.UtcNow;
            };
            shown.Show();
        }

        void CloseNag()
        {
            if (activeNag == null) return;
            ClosingByApp = true;
            try { activeNag.Close(); }
            finally { ClosingByApp = false; }
            activeNag = null;
        }

        // ── 사용자 동작 ──────────────────────────────────────────

        public void RequestExtend(IWin32Window owner)
        {
            if (PastBedtime())
            {
                ConfirmDialog.Info(owner, "잘 시간에는 연장할 수 없어요", "취침 시간 " + BedtimeText() + "이 지났어요. 내일 다시 만나요.");
                return;
            }
            int left = Math.Max(0, config.MaxExtensionsPerDay - state.ExtensionsUsed);
            if (left <= 0 || config.ExtensionMinutes <= 0)
            {
                ConfirmDialog.Info(owner, "더 연장할 수 없어요", "오늘 연장 횟수를 모두 썼어요. 내일 다시 쓸 수 있어요.");
                return;
            }
            int choice = ConfirmDialog.Show(owner,
                config.ExtensionMinutes + "분 연장할까요?",
                string.Format("오늘 남은 연장 횟수가 {0}회에서 {1}회로 줄어요.", left, left - 1),
                new[] { "연장하기", "취소" }, new[] { ButtonKind.Primary, ButtonKind.Ghost }, 1);
            if (choice != 0) return;

            // 이미 초과한 시간은 연장분에서 깎이지 않도록, 초과분을 털어 낸다.
            double quotaOver = -QuotaRemaining();
            if (quotaOver > 0) state.UsedSeconds -= quotaOver;
            state.ExtensionsUsed++;
            double window = WindowRemaining();
            if (window <= QuotaRemaining())
            {
                // 시간대가 먼저 끝나는(또는 이미 밖인) 상황이면 시간대도 연장한다.
                state.GraceUntilUtcTicks = DateTime.UtcNow.AddSeconds(Math.Max(0, window) + config.ExtensionMinutes * 60.0).Ticks;
                outsideSeconds = 0;
            }
            prevRemaining = RemainingSeconds();
            SaveState();
            CloseNag();
            UpdateTray();
            Toast.Show(config.ExtensionMinutes + "분 연장했어요", "남은 시간 " + Ui.Clock(RemainingSeconds()), Ui.Blue);
        }

        public void RequestShutdown(IWin32Window owner)
        {
            int choice = ConfirmDialog.Show(owner, "PC를 끌까요?",
                "1분 뒤에 꺼져요. 그 사이에 저장하거나 취소할 수 있어요.",
                new[] { "끄기", "취소" }, new[] { ButtonKind.Danger, ButtonKind.Ghost }, 0);
            if (choice != 0) return;
            SaveState();
            if (!RunShutdown("/s /t 60")) return;

            shuttingDown = true;
            CloseNag();
            if (shutdownNotice != null) shutdownNotice.Close();
            shutdownNotice = new ShutdownNotice(60);
            shutdownNotice.Cancelled += delegate
            {
                RunShutdown("/a");
                shuttingDown = false;
                lastNagClosed = DateTime.UtcNow;
            };
            shutdownNotice.FormClosed += delegate { shutdownNotice = null; };
            shutdownNotice.Show();
        }

        static bool RunShutdown(string args)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo("shutdown", args) { CreateNoWindow = true, UseShellExecute = false })) { }
                return true;
            }
            catch (Exception ex)
            {
                ConfirmDialog.Info(null, "종료 명령을 실행하지 못했어요", ex.Message);
                return false;
            }
        }

        void ConfirmExit()
        {
            int choice = ConfirmDialog.Show(null, "타이머를 끌까요?",
                "끄면 오늘은 알림이 뜨지 않아요. 다음에 로그인하면 다시 켜져요.",
                new[] { "계속 켜두기", "끄기" }, new[] { ButtonKind.Primary, ButtonKind.Ghost }, 0);
            if (choice == 1) ExitThread();
        }

        // ── 사용 기록 ────────────────────────────────────────────

        // 맨 앞 창의 앱을 기록한다. 입력이 IdleMinutes 넘게 없으면 자리 비움으로 친다.
        void RecordUsage(double delta)
        {
            if (delta <= 0) return;
            string app;
            AppGuard.Foreground(out app);
            double idle = UsageHistory.IdleSeconds();
            bool isIdle = config.IdleMinutes > 0 && idle >= config.IdleMinutes * 60.0;
            var kind = isIdle ? UsageKind.Idle : focus != null ? UsageKind.Quest : UsageKind.Free;
            try
            {
                history.Record(Today, DateTime.Now, delta, kind, isIdle ? "" : app ?? "", focus != null ? focus.Quest.Title : "", isIdle ? idle : 0);
            }
            catch { }
        }

        public void OpenHistory()
        {
            if (flyout != null) flyout.Close();
            if (historyForm != null)
            {
                if (historyForm.WindowState == FormWindowState.Minimized) historyForm.WindowState = FormWindowState.Normal;
                historyForm.Activate();
                return;
            }
            historyForm = new HistoryForm(this, history);
            historyForm.FormClosed += delegate { historyForm = null; };
            historyForm.Show();
            historyForm.Activate();
        }

        // ── 퀘스트와 집중 ────────────────────────────────────────

        public void NotifyQuestsChanged()
        {
            if (QuestsChanged != null) QuestsChanged(this, EventArgs.Empty);
            UpdateTray();
        }

        public void SaveQuests()
        {
            try { quests.Save(questsPath); } catch { }
        }

        public void OpenQuests(bool atStart)
        {
            if (flyout != null) flyout.Close();
            if (board != null)
            {
                if (board.WindowState == FormWindowState.Minimized) board.WindowState = FormWindowState.Normal;
                board.Activate();
                return;
            }
            board = new QuestBoard(this, atStart);
            board.FormClosed += delegate { board = null; };
            board.Show();
            board.Activate();
        }

        // 알림 창의 "퀘스트 하기": 알림을 닫고 퀘스트 창을 연다.
        public void OpenQuestsFromNag()
        {
            CloseNag();
            OpenQuests(false);
        }

        public void EditQuest(IWin32Window owner, Quest quest)
        {
            bool isNew = quest == null;
            var source = quest ?? new Quest();
            using (var editor = new QuestEditor(source, isNew))
            {
                if (owner != null) editor.ShowDialog(owner); else editor.ShowDialog();
                if (editor.DialogResult != DialogResult.OK) return;
                if (editor.Deleted)
                {
                    if (focus != null && focus.Quest == quest) StopFocus(false);
                    quests.Quests.Remove(quest);
                }
                else if (isNew)
                {
                    quests.Quests.Add(editor.Result);
                }
                else
                {
                    var r = editor.Result;
                    quest.Title = r.Title;
                    quest.Daily = r.Daily;
                    quest.Days = r.Days;
                    quest.TargetMinutes = r.TargetMinutes;
                    quest.Apps = r.Apps;
                    quest.CountsAsPlay = r.CountsAsPlay;
                }
            }
            SaveQuests();
            NotifyQuestsChanged();
        }

        public void StartFocus(Quest quest)
        {
            if (PastBedtime())
            {
                ConfirmDialog.Info(board, "잘 시간이 지났어요", "취침 시간 " + BedtimeText() + " 이후에는 집중을 시작할 수 없어요. 내일 이어서 해요.");
                return;
            }
            if (focus != null) StopFocus(false);
            focus = new FocusSession { Quest = quest };
            CloseNag();
            if (flyout != null) flyout.Close();
            hud = new FocusHud(this);
            hud.Show();
            badWindow = IntPtr.Zero;
            if (board != null) board.WindowState = FormWindowState.Minimized;
            Toast.Show("집중 시작: " + quest.Title,
                (quest.Apps.Count > 0 ? "지금부터 " + string.Join(", ", quest.Apps.ToArray()) + "만 쓸 수 있어요." : "앱 제한 없이 시간을 재요.")
                + (quest.CountsAsPlay ? " 이 시간은 놀이 시간에 들어가요." : " 이 시간은 놀이 시간에서 빠져요."),
                Ui.Green);
            NotifyQuestsChanged();
        }

        public void StopFocus(bool announce)
        {
            if (focus == null) return;
            var f = focus;
            focus = null;
            if (hud != null) { hud.Close(); hud = null; }
            SaveQuests();
            // 집중을 마치자마자 조르지 않도록 한 번 쉬고 시작한다.
            lastNagClosed = DateTime.UtcNow;
            if (announce)
                Toast.Show("집중을 마쳤어요", string.Format("{0} · {1}{2}", f.Quest.Title, Ui.Duration((int)(f.Elapsed / 60)),
                    f.Blocked > 0 ? " · 딴짓 " + f.Blocked + "번 막음" : ""), Ui.Blue);
            NotifyQuestsChanged();
        }

        public void CompleteFocus()
        {
            if (focus == null) return;
            var q = focus.Quest;
            StopFocus(false);
            CompleteQuest(q);
        }

        public void CompleteQuest(Quest quest)
        {
            int levelBefore = quests.Level;
            bool allDaily = quests.Complete(quest, Today, Yesterday, TodayDow, config.QuestXp);
            history.AddDone(Today, DateTime.Now, quest.Title);
            if (focus != null && focus.Quest == quest) StopFocus(false);
            SaveQuests();
            if (allDaily)
                Toast.Show("오늘 일일 퀘스트를 모두 끝냈어요!",
                    string.Format("연속 {0}일 · +{1} XP{2}", quests.CurrentStreak(Today, Yesterday), config.QuestXp,
                        quests.LockFreeTime ? " · 자유 시간이 열렸어요" : ""), Ui.Green);
            else
                Toast.Show("퀘스트 완료: " + quest.Title, "+" + config.QuestXp + " XP", Ui.Green);
            if (quests.Level > levelBefore)
                Toast.Show("레벨 업! Lv " + quests.Level, "꾸준함이 쌓이고 있어요.", Ui.Blue);
            NotifyQuestsChanged();
        }

        public void UncompleteQuest(Quest quest)
        {
            quests.Uncomplete(quest, Today, Yesterday, config.QuestXp);
            history.RemoveDone(Today, quest.Title);
            SaveQuests();
            NotifyQuestsChanged();
        }

        // 집중 중에 허용되지 않은 앱이 맨 앞에 오면, 잠깐 경고한 뒤 최소화한다.
        void GuardApps()
        {
            if (focus == null || focus.Quest.Apps.Count == 0 || locked || hud == null) { badWindow = IntPtr.Zero; return; }
            string name;
            IntPtr h = AppGuard.Foreground(out name);
            if (h == IntPtr.Zero || AppGuard.IsAllowed(name, focus.Quest.Apps, config.AlwaysAllowedApps))
            {
                badWindow = IntPtr.Zero;
                return;
            }
            if (h != badWindow)
            {
                badWindow = h;
                badSince = DateTime.UtcNow;
            }
            double waited = (DateTime.UtcNow - badSince).TotalSeconds;
            if (waited >= config.FocusBlockDelaySeconds)
            {
                AppGuard.Minimize(h);
                focus.Blocked++;
                hud.Warn(name + " 창을 내렸어요. 지금은 " + focus.Quest.Title + " 시간이에요");
                badWindow = IntPtr.Zero;
            }
            else
            {
                hud.Warn(string.Format("{0}은(는) 지금 쓸 수 없어요 · {1}초 뒤 내려가요", name, (int)Math.Ceiling(config.FocusBlockDelaySeconds - waited)));
            }
        }

        public void OpenSchedule()
        {
            if (flyout != null) flyout.Close();
            if (scheduleForm != null)
            {
                if (scheduleForm.WindowState == FormWindowState.Minimized) scheduleForm.WindowState = FormWindowState.Normal;
                scheduleForm.Activate();
                return;
            }
            scheduleForm = new ScheduleForm(schedule, config.ResetHour);
            scheduleForm.Saved += delegate
            {
                schedule = scheduleForm.Result;
                try { schedule.Save(schedulePath); }
                catch (Exception ex) { ConfirmDialog.Info(null, "시간표를 저장하지 못했어요", ex.Message); }
                lastIconKey = null;
                UpdateTray();
                Toast.Show("시간표를 저장했어요", WindowLineShort(), Ui.Blue);
            };
            scheduleForm.FormClosed += delegate { scheduleForm = null; };
            scheduleForm.Show();
            scheduleForm.Activate();
        }

        void ToggleFlyout()
        {
            if (flyout != null) { flyout.Close(); return; }
            // 아이콘을 누르는 순간 카드가 포커스를 잃어 닫혔다면 다시 열지 않는다(토글).
            if ((DateTime.UtcNow - flyoutClosedAt).TotalMilliseconds < 300) return;
            flyout = new StatusFlyout(this);
            flyout.FormClosed += delegate { flyout = null; flyoutClosedAt = DateTime.UtcNow; };
            flyout.Show();
            // 트레이에서 띄운 창은 포커스를 못 받는 경우가 있어, 그러면 바깥을 눌러도 닫히지 않는다.
            try { SetForegroundWindow(flyout.Handle); } catch { }
            flyout.Activate();
        }

        protected override void ExitThreadCore()
        {
            tick.Stop();
            guard.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SaveState();
            SaveQuests();
            history.Save();
            if (historyForm != null) historyForm.Close();
            if (hud != null) hud.Close();
            if (board != null) board.Close();
            CloseNag();
            foreach (var f in new Form[] { flyout, scheduleForm, shutdownNotice })
                if (f != null) f.Close();
            tray.Visible = false;
            tray.Dispose();
            showEvent.Close();
            FreeIcon(lastIconHandle);
            base.ExitThreadCore();
        }

        static void FreeIcon(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try { DestroyIcon(handle); } catch { }
        }

        void SaveState()
        {
            try { state.Save(statePath); } catch { }
        }

        // ── 트레이 ───────────────────────────────────────────────

        void UpdateMenu()
        {
            double rem = RemainingSeconds();
            menuHeader.Text = rem > 0 ? Ui.Clock(rem) + " 남음" : Ui.Clock(-rem) + " 초과";
            menuHeader.ForeColor = rem <= 0 ? Ui.Red : rem <= 600 ? Ui.Amber : Ui.Text;
            menuSub.Text = WindowLineShort();
            int left = Math.Max(0, config.MaxExtensionsPerDay - state.ExtensionsUsed);
            extendItem.Text = string.Format("{0}분 연장 ({1}회 남음)", config.ExtensionMinutes, left);
            extendItem.Enabled = left > 0 && config.ExtensionMinutes > 0;
            stopFocusItem.Visible = focus != null;
            if (focus != null)
            {
                menuHeader.Text = "집중 중 " + Ui.Clock(focus.Elapsed);
                menuHeader.ForeColor = Ui.Green;
            }
        }

        void UpdateTray()
        {
            double rem = RemainingSeconds();
            bool over = rem <= 0;
            int minutes = (int)Math.Ceiling(Math.Abs(rem) / 60.0);
            string text = minutes.ToString();
            if (!over && minutes >= 100) text = (minutes / 60) + "h";
            if (over) text = minutes >= 100 ? "!" : "+" + minutes;

            string tip = over ? "PlayTimer · " + Ui.Clock(-rem) + " 초과" : "PlayTimer · " + Ui.Clock(rem) + " 남음";
            Color bg = over ? Ui.Red : rem <= 600 ? Ui.Amber : Ui.Blue;
            if (focus != null && (!focus.Quest.CountsAsPlay || !over))
            {
                // 집중 중에는 초록 아이콘에 집중한 분(목표가 있으면 남은 분)을 표시한다.
                double shown = focus.Quest.TargetMinutes > 0
                    ? Math.Max(0, focus.Quest.TargetMinutes * 60.0 - focus.Quest.Progress(Today))
                    : focus.Elapsed;
                int m = (int)Math.Ceiling(shown / 60.0);
                text = m >= 100 ? (m / 60) + "h" : m.ToString();
                bg = Ui.Green;
                tip = "PlayTimer · 집중: " + focus.Quest.Title;
            }
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            string key = text + bg.ToArgb();
            if (key == lastIconKey) return;
            lastIconKey = key;

            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(bg))
                using (var font = new Font("Segoe UI", text.Length >= 3 ? 13f : 18f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var path = Ui.Round(new Rectangle(0, 0, 31, 31), 8))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.FillPath(brush, path);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, font, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
                }
                IntPtr h = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(h);
                FreeIcon(lastIconHandle);
                lastIconHandle = h;
            }
        }
    }
}
