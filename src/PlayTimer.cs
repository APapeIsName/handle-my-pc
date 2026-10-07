// PlayTimer - PC방처럼 하루 사용 시간을 재고, 시간이 끝나면 끄라고 계속 조르는 트레이 앱.
// 윈도우에 기본 포함된 .NET Framework 4.x 컴파일러(csc.exe)로 빌드할 수 있도록 C# 5 문법만 사용한다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PlayTimer
{
    static class Program
    {
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
                    MessageBox.Show("PlayTimer가 이미 실행 중입니다.", "PlayTimer");
                    return;
                }
                try { SetProcessDPIAware(); } catch { }
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
                "ext=" + ExtensionsUsed
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
        State state;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer tick;
        readonly ToolStripMenuItem statusItem;
        readonly ToolStripMenuItem extendItem;

        DateTime lastTick = DateTime.UtcNow;
        DateTime lastSave = DateTime.UtcNow;
        DateTime lastNagClosed = DateTime.MinValue;
        bool locked;
        Form activeNag;
        string lastIconText;
        IntPtr lastIconHandle = IntPtr.Zero;

        public TrayApp()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            config = Config.Load(Path.Combine(exeDir, "config.ini"));
            statePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlayTimer", "state.txt");
            state = State.Load(statePath);
            RollDayIfNeeded();

            statusItem = new ToolStripMenuItem("") { Enabled = false };
            extendItem = new ToolStripMenuItem("", null, delegate { TryExtend(null); });
            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(extendItem);
            menu.Items.Add(new ToolStripMenuItem("PC 끄기", null, delegate { ConfirmShutdown(null); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("타이머 종료", null, delegate { ConfirmExit(); }));
            menu.Opening += delegate { UpdateMenu(); };

            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowStatus(); };

            SystemEvents.SessionSwitch += OnSessionSwitch;

            tick = new System.Windows.Forms.Timer { Interval = 1000 };
            tick.Tick += delegate { OnTick(); };
            tick.Start();

            UpdateTray();
            double remaining = RemainingSeconds();
            if (remaining <= 0)
                lastNagClosed = DateTime.UtcNow.AddSeconds(-config.Stage1NagSeconds + 5);
            else
                Toast.Show(string.Format("오늘 남은 시간: {0}", FormatTime(remaining)), false);
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect)
                locked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
                locked = false;
        }

        string TodayKey()
        {
            return DateTime.Now.AddHours(-config.ResetHour).ToString("yyyy-MM-dd");
        }

        void RollDayIfNeeded()
        {
            string today = TodayKey();
            if (state.Day != today)
            {
                state = new State { Day = today };
                SaveState();
            }
        }

        double LimitSeconds()
        {
            return (config.DailyLimitMinutes + state.ExtensionsUsed * config.ExtensionMinutes) * 60.0;
        }

        double RemainingSeconds()
        {
            return LimitSeconds() - state.UsedSeconds;
        }

        void OnTick()
        {
            DateTime now = DateTime.UtcNow;
            double delta = (now - lastTick).TotalSeconds;
            lastTick = now;
            // 절전/최대 절전 동안의 공백은 사용 시간으로 치지 않는다.
            if (delta < 0 || delta > 5) delta = 0;

            RollDayIfNeeded();
            if (locked) return;

            double before = RemainingSeconds();
            state.UsedSeconds += delta;
            double after = RemainingSeconds();

            foreach (int w in config.WarnAtMinutes)
            {
                double t = w * 60.0;
                if (before > t && after <= t)
                    Toast.Show(string.Format("{0}분 남았습니다. 슬슬 정리하세요.", w), w <= 1);
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
            if (activeNag != null) return;
            double overMin = overSeconds / 60.0;
            int stage = overMin >= config.Stage3AfterMinutes ? 3 : overMin >= config.Stage2AfterMinutes ? 2 : 1;
            int interval = stage == 3 ? config.Stage3NagSeconds : stage == 2 ? config.Stage2NagSeconds : config.Stage1NagSeconds;
            if ((DateTime.UtcNow - lastNagClosed).TotalSeconds < interval) return;

            if (stage == 3)
                activeNag = new OverlayNag(this, config.Stage3CloseDelaySeconds);
            else
                activeNag = new NagForm(this, stage, stage == 2 ? config.Stage2CloseDelaySeconds : 0);
            activeNag.FormClosed += delegate
            {
                activeNag = null;
                lastNagClosed = DateTime.UtcNow;
            };
            activeNag.Show();
        }

        public string OvertimeText()
        {
            return FormatTime(Math.Max(0, -RemainingSeconds()));
        }

        public bool CanExtend()
        {
            return state.ExtensionsUsed < config.MaxExtensionsPerDay && config.ExtensionMinutes > 0;
        }

        public string ExtendLabel()
        {
            return string.Format("+{0}분 연장 (오늘 {1}회 남음)", config.ExtensionMinutes,
                Math.Max(0, config.MaxExtensionsPerDay - state.ExtensionsUsed));
        }

        public bool TryExtend(IWin32Window owner)
        {
            if (!CanExtend())
            {
                MessageBox.Show(owner, "오늘은 더 이상 연장할 수 없습니다.", "PlayTimer");
                return false;
            }
            var r = MessageBox.Show(owner,
                string.Format("정말 {0}분 연장할까요?\n오늘 연장 가능 횟수가 1회 줄어듭니다.", config.ExtensionMinutes),
                "PlayTimer", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return false;

            // 이미 초과한 시간은 연장분에서 깎이지 않도록, 초과분을 사용 시간에서 털어 낸다.
            double over = -RemainingSeconds();
            if (over > 0) state.UsedSeconds -= over;
            state.ExtensionsUsed++;
            SaveState();
            UpdateTray();
            if (activeNag != null) activeNag.Close();
            return true;
        }

        public void ConfirmShutdown(IWin32Window owner)
        {
            var r = MessageBox.Show(owner, "1분 뒤에 PC를 끕니다. 저장할 것을 저장하세요.\n(취소하려면 명령 프롬프트에서 shutdown /a)",
                "PlayTimer", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (r != DialogResult.OK) return;
            SaveState();
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 60") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "종료 명령 실행 실패: " + ex.Message, "PlayTimer");
            }
        }

        void ConfirmExit()
        {
            var r = MessageBox.Show("타이머를 끄면 오늘은 더 이상 알림이 뜨지 않습니다.\n정말 끌까요?",
                "PlayTimer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            tick.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SaveState();
            tray.Visible = false;
            tray.Dispose();
            if (lastIconHandle != IntPtr.Zero) DestroyIcon(lastIconHandle);
            base.ExitThreadCore();
        }

        void SaveState()
        {
            try { state.Save(statePath); } catch { }
        }

        void ShowStatus()
        {
            double rem = RemainingSeconds();
            string msg = rem > 0
                ? string.Format("오늘 남은 시간: {0}\n사용한 시간: {1}", FormatTime(rem), FormatTime(state.UsedSeconds))
                : string.Format("시간 초과: {0}\n사용한 시간: {1}", FormatTime(-rem), FormatTime(state.UsedSeconds));
            MessageBox.Show(msg, "PlayTimer");
        }

        void UpdateMenu()
        {
            double rem = RemainingSeconds();
            statusItem.Text = rem > 0 ? "남은 시간 " + FormatTime(rem) : "시간 초과 " + FormatTime(-rem);
            extendItem.Text = ExtendLabel();
            extendItem.Enabled = CanExtend();
        }

        void UpdateTray()
        {
            double rem = RemainingSeconds();
            bool over = rem <= 0;
            int minutes = (int)Math.Ceiling(Math.Abs(rem) / 60.0);
            string text = over ? "+" + minutes : minutes.ToString();
            if (!over && minutes >= 100) text = (minutes / 60) + "h";
            if (over && minutes >= 100) text = "!!";

            tray.Text = over
                ? "PlayTimer - 시간 초과 " + FormatTime(-rem)
                : "PlayTimer - 남은 시간 " + FormatTime(rem);

            string key = text + (over ? "o" : rem <= 600 ? "w" : "n");
            if (key == lastIconText) return;
            lastIconText = key;

            Color bg = over ? Color.FromArgb(200, 30, 30) : rem <= 600 ? Color.FromArgb(230, 140, 0) : Color.FromArgb(40, 120, 200);
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(bg))
                using (var font = new Font("Segoe UI", text.Length >= 3 ? 11f : 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.FillEllipse(brush, 0, 0, 31, 31);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
                }
                IntPtr h = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(h);
                if (lastIconHandle != IntPtr.Zero) DestroyIcon(lastIconHandle);
                lastIconHandle = h;
            }
        }

        public static string FormatTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }
    }

    // 화면 오른쪽 아래에 잠깐 떴다 사라지는 경고 (포커스를 뺏지 않음).
    class Toast : Form
    {
        readonly System.Windows.Forms.Timer life = new System.Windows.Forms.Timer();

        public static void Show(string message, bool urgent)
        {
            new Toast(message, urgent).Show();
        }

        Toast(string message, bool urgent)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = urgent ? Color.FromArgb(200, 30, 30) : Color.FromArgb(35, 35, 40);
            Size = new Size(360, 90);
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);

            var label = new Label
            {
                Text = message,
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 12f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            label.Click += delegate { Close(); };
            Controls.Add(label);

            life.Interval = 8000;
            life.Tick += delegate { life.Stop(); Close(); };
            life.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) life.Dispose();
            base.Dispose(disposing);
        }
    }

    // 1~2단계: 항상 위에 뜨는 팝업. 2단계는 몇 초 동안 닫을 수 없다.
    class NagForm : Form
    {
        readonly TrayApp app;
        readonly Label overLabel;
        readonly Button closeButton;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        int closeDelay;

        public NagForm(TrayApp app, int stage, int closeDelaySeconds)
        {
            this.app = app;
            closeDelay = closeDelaySeconds;

            Text = "PlayTimer";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = stage == 2 ? Color.FromArgb(120, 20, 20) : Color.FromArgb(35, 35, 40);
            ClientSize = stage == 2 ? new Size(560, 300) : new Size(440, 220);

            var title = new Label
            {
                Text = stage == 2 ? "진짜로 이제 그만할 시간입니다" : "오늘 사용 시간이 끝났습니다",
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", stage == 2 ? 20f : 15f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = stage == 2 ? 90 : 70
            };
            overLabel = new Label
            {
                ForeColor = Color.FromArgb(255, 200, 200),
                Font = new Font("맑은 고딕", stage == 2 ? 14f : 11f),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };
            closeButton = MakeButton("조금만 더");
            closeButton.Click += delegate { Close(); };
            var shutdownButton = MakeButton("PC 끄기");
            shutdownButton.Click += delegate { app.ConfirmShutdown(this); };
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(shutdownButton);
            if (app.CanExtend())
            {
                var extendButton = MakeButton(app.ExtendLabel());
                extendButton.Width = 220;
                extendButton.Click += delegate { app.TryExtend(this); };
                buttons.Controls.Add(extendButton);
            }

            Controls.Add(buttons);
            Controls.Add(overLabel);
            Controls.Add(title);

            timer.Interval = 1000;
            timer.Tick += delegate { Refresh2(); };
            timer.Start();
            Refresh2();
        }

        static Button MakeButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 110,
                Height = 36,
                BackColor = Color.WhiteSmoke,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 9.5f)
            };
        }

        void Refresh2()
        {
            overLabel.Text = "초과 시간: " + app.OvertimeText();
            if (closeDelay > 0)
            {
                closeButton.Enabled = false;
                closeButton.Text = string.Format("조금만 더 ({0})", closeDelay);
                closeDelay--;
            }
            else
            {
                closeButton.Enabled = true;
                closeButton.Text = "조금만 더";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !closeButton.Enabled) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // 3단계: 모든 모니터를 덮는 반투명 전체화면. 일정 시간 뒤에야 닫을 수 있다.
    class OverlayNag : Form
    {
        readonly TrayApp app;
        readonly Label overLabel;
        readonly Button closeButton;
        readonly List<Form> covers = new List<Form>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        int closeDelay;

        public OverlayNag(TrayApp app, int closeDelaySeconds)
        {
            this.app = app;
            closeDelay = closeDelaySeconds;

            ConfigureCover(this, Screen.PrimaryScreen);
            Opacity = 0.92;

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var title = new Label
            {
                Text = "그만!\n오늘 사용 시간이 한참 지났습니다.",
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 36f, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter
            };
            overLabel = new Label
            {
                ForeColor = Color.FromArgb(255, 120, 120),
                Font = new Font("맑은 고딕", 24f),
                AutoSize = true,
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 20, 0, 30)
            };

            var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Top, BackColor = Color.Transparent };
            var shutdownButton = MakeButton("PC 끄기");
            shutdownButton.Click += delegate { app.ConfirmShutdown(this); };
            closeButton = MakeButton("");
            closeButton.Click += delegate { Close(); };
            buttons.Controls.Add(shutdownButton);
            if (app.CanExtend())
            {
                var extendButton = MakeButton(app.ExtendLabel());
                extendButton.Width = 300;
                extendButton.Click += delegate { app.TryExtend(this); };
                buttons.Controls.Add(extendButton);
            }
            buttons.Controls.Add(closeButton);

            panel.Controls.Add(new Label { AutoSize = false }, 0, 0);
            panel.Controls.Add(title, 0, 1);
            panel.Controls.Add(overLabel, 0, 2);
            panel.Controls.Add(buttons, 0, 3);
            Controls.Add(panel);

            // 보조 모니터는 검은 화면으로만 덮는다.
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Primary) continue;
                var cover = new Form();
                ConfigureCover(cover, screen);
                cover.Opacity = 0.92;
                cover.FormClosing += delegate(object s, FormClosingEventArgs e)
                {
                    if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
                };
                covers.Add(cover);
            }
            Shown += delegate { foreach (var c in covers) c.Show(); };
            FormClosed += delegate
            {
                foreach (var c in covers) { c.Hide(); c.Dispose(); }
            };

            timer.Interval = 1000;
            timer.Tick += delegate { Refresh2(); };
            timer.Start();
            Refresh2();
        }

        static void ConfigureCover(Form f, Screen screen)
        {
            f.FormBorderStyle = FormBorderStyle.None;
            f.ShowInTaskbar = false;
            f.TopMost = true;
            f.StartPosition = FormStartPosition.Manual;
            f.Bounds = screen.Bounds;
            f.BackColor = Color.Black;
        }

        static Button MakeButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 180,
                Height = 50,
                BackColor = Color.WhiteSmoke,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 12f),
                Margin = new Padding(10)
            };
        }

        void Refresh2()
        {
            overLabel.Text = "초과 시간 " + app.OvertimeText();
            if (closeDelay > 0)
            {
                closeButton.Enabled = false;
                closeButton.Text = string.Format("닫기 ({0})", closeDelay);
                closeDelay--;
            }
            else
            {
                closeButton.Enabled = true;
                closeButton.Text = "닫기";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !closeButton.Enabled) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
