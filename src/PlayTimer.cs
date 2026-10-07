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

        readonly Config config;
        readonly string statePath;
        readonly string schedulePath;
        State state;
        Schedule schedule;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer tick;
        readonly ToolStripLabel menuHeader, menuSub;
        readonly ToolStripMenuItem extendItem;
        readonly EventWaitHandle showEvent;
        readonly SynchronizationContext ui;

        DateTime lastTick = DateTime.UtcNow;
        DateTime lastSave = DateTime.UtcNow;
        DateTime lastNagClosed = DateTime.MinValue;
        DateTime flyoutClosedAt = DateTime.MinValue;
        double outsideSeconds;
        double prevRemaining = double.NaN;
        bool locked;
        bool shuttingDown;
        Form activeNag;
        int activeStage;
        StatusFlyout flyout;
        ScheduleForm scheduleForm;
        ShutdownNotice shutdownNotice;
        string lastIconKey;
        IntPtr lastIconHandle = IntPtr.Zero;

        // 코드에서 조르기 창을 닫을 때는 "아직 못 닫음" 잠금을 무시한다.
        public bool ClosingByApp { get; private set; }

        public TrayApp()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            config = Config.Load(Path.Combine(exeDir, "config.ini"));
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayTimer");
            statePath = Path.Combine(dataDir, "state.txt");
            schedulePath = Path.Combine(dataDir, "schedule.txt");
            bool firstRun = !File.Exists(schedulePath);
            state = State.Load(statePath);
            schedule = Schedule.Load(schedulePath, config.DailyLimitMinutes);
            RollDayIfNeeded();

            menuHeader = new ToolStripLabel { Font = Ui.Font(11f, FontStyle.Bold), Margin = Ui.Pad(4, 6, 4, 0) };
            menuSub = new ToolStripLabel { ForeColor = Ui.SubText, Margin = Ui.Pad(4, 0, 4, 6) };
            extendItem = new ToolStripMenuItem("", null, delegate { RequestExtend(null); });
            var menu = new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
            menu.Items.Add(menuHeader);
            menu.Items.Add(menuSub);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("시간 설정...", null, delegate { OpenSchedule(); }) { Font = Ui.Font(9.5f, FontStyle.Bold) });
            menu.Items.Add(extendItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("PC 끄기", null, delegate { RequestShutdown(null); }));
            menu.Items.Add(new ToolStripMenuItem("타이머 종료", null, delegate { ConfirmExit(); }));
            menu.Opening += delegate { UpdateMenu(); };

            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
            tray.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenSchedule(); };

            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { ui.Post(delegate { OpenSchedule(); }, null); }, null, -1, false);

            SystemEvents.SessionSwitch += OnSessionSwitch;

            tick = new System.Windows.Forms.Timer { Interval = 1000 };
            tick.Tick += delegate { OnTick(); };
            tick.Start();

            UpdateTray();
            if (firstRun)
            {
                Toast.Show("PlayTimer를 시작했어요", "요일별 사용 시간과 시간대를 정해 주세요. 트레이 아이콘을 클릭하면 언제든 현황을 볼 수 있어요.", Ui.Blue);
                OpenSchedule();
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

        void RollDayIfNeeded()
        {
            string today = Logical().ToString("yyyy-MM-dd");
            if (state.Day != today)
            {
                state = new State { Day = today, GraceUntilUtcTicks = state.GraceUntilUtcTicks };
                SaveState();
            }
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
            double w = schedule.SecondsUntilWindowEnd(Logical());
            double g = GraceRemaining();
            if (w > 0 || g > 0) return Math.Max(w, g);
            return -outsideSeconds;
        }

        // 총량과 시간대 중 먼저 끝나는 쪽
        double RemainingSeconds()
        {
            return Math.Min(QuotaRemaining(), WindowRemaining());
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
            if (schedule.AlwaysAllowed()) return "시간대 제한 없이 총량만 적용돼요.";
            var logical = Logical();
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
            return overMin >= config.Stage3AfterMinutes ? 3 : overMin >= config.Stage2AfterMinutes ? 2 : 1;
        }

        public Status GetStatus()
        {
            var st = new Status();
            var logical = Logical();
            st.Remaining = RemainingSeconds();
            st.UsedSeconds = state.UsedSeconds;
            st.LimitSeconds = LimitSeconds();
            st.QuotaBinding = QuotaRemaining() <= WindowRemaining();
            st.InWindow = schedule.IsAllowed(logical) || GraceRemaining() > 0;
            st.WindowLine = "●  " + WindowLineShort();
            st.DayLabel = string.Format("{0}월 {1}일 ({2})", logical.Month, logical.Day, ScheduleForm.DayNames[(int)logical.DayOfWeek]);
            st.ExtensionsLeft = Math.Max(0, config.MaxExtensionsPerDay - state.ExtensionsUsed);
            st.ExtensionMinutes = config.ExtensionMinutes;

            if (QuotaRemaining() <= 0)
            {
                st.Reason = "오늘 사용 시간이 끝났어요";
                st.ReasonDetail = "오늘 총량 " + Ui.Duration((int)(LimitSeconds() / 60)) + "을 모두 썼어요.";
            }
            else
            {
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

            RollDayIfNeeded();
            if (locked) return;

            state.UsedSeconds += delta;
            if (schedule.IsAllowed(Logical()) || GraceRemaining() > 0) outsideSeconds = 0;
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
                string body = QuotaRemaining() <= WindowRemaining()
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
            scheduleForm.FormClosed += delegate { scheduleForm.Dispose(); scheduleForm = null; };
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
            flyout.Activate();
        }

        protected override void ExitThreadCore()
        {
            tick.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SaveState();
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
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            Color bg = over ? Ui.Red : rem <= 600 ? Ui.Amber : Ui.Blue;
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
