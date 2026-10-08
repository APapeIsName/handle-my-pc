using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PlayTimer
{
    // 지금 맨 앞에 있는 창이 어느 앱인지 알아내고, 허용되지 않은 앱이면 최소화한다.
    static class AppGuard
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        // 윈도우 자체가 쓰는 화면(작업 표시줄, 시작 메뉴, 잠금 화면, 입력기 등)은 항상 허용한다.
        static readonly HashSet<string> SystemApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp", "searchui",
            "lockapp", "logonui", "textinputhost", "ctfmon", "dwm", "taskmgr", "consent", "systemsettings",
            "shellhost", "applicationframehost", "idle"
        };

        public static string Normalize(string name)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (name.EndsWith(".exe")) name = name.Substring(0, name.Length - 4);
            return name;
        }

        static string ProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid)) return Normalize(p.ProcessName);
            }
            catch { return null; }
        }

        // 맨 앞 창과 그 앱 이름. UWP 앱은 껍데기(ApplicationFrameHost) 대신 실제 앱 이름을 찾는다.
        public static IntPtr Foreground(out string name)
        {
            name = null;
            IntPtr hwnd;
            try { hwnd = GetForegroundWindow(); } catch { return IntPtr.Zero; }
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id) { name = "playtimer"; return hwnd; }
            name = ProcessName(pid);
            if (name == "applicationframehost")
            {
                string inner = null;
                EnumChildWindows(hwnd, delegate(IntPtr child, IntPtr l)
                {
                    uint cpid;
                    GetWindowThreadProcessId(child, out cpid);
                    if (cpid != pid) { inner = ProcessName(cpid); return false; }
                    return true;
                }, IntPtr.Zero);
                if (inner != null) name = inner;
            }
            return hwnd;
        }

        public static void Minimize(IntPtr hwnd)
        {
            try { ShowWindow(hwnd, 6); } catch { } // SW_MINIMIZE
        }

        public static bool IsAllowed(string name, IEnumerable<string> questApps, IEnumerable<string> alwaysApps)
        {
            if (string.IsNullOrEmpty(name) || name == "playtimer" || SystemApps.Contains(name)) return true;
            foreach (var a in questApps) if (Normalize(a) == name) return true;
            foreach (var a in alwaysApps) if (Normalize(a) == name) return true;
            return false;
        }

        // 지금 창을 띄우고 있는 앱 목록: (프로세스 이름, 창 제목)
        public static List<KeyValuePair<string, string>> RunningApps()
        {
            var seen = new HashSet<string>();
            var list = new List<KeyValuePair<string, string>>();
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                    string n = Normalize(p.ProcessName);
                    if (SystemApps.Contains(n) || !seen.Add(n)) continue;
                    list.Add(new KeyValuePair<string, string>(n, p.MainWindowTitle));
                }
                catch { }
                finally { p.Dispose(); }
            }
            list.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }

    // 진행 중인 집중(퀘스트) 시간.
    class FocusSession
    {
        public Quest Quest;
        public double Elapsed;       // 이번에 집중한 시간
        public int Blocked;          // 허용되지 않은 앱을 열었다가 최소화된 횟수
    }

    // 집중하는 동안 화면 위쪽에 떠 있는 작은 막대. 포커스를 뺏지 않는다.
    class FocusHud : CardForm
    {
        readonly TrayApp app;
        readonly Label title, time, note;
        readonly UiButton doneButton, stopButton;
        readonly Timer timer = new Timer();
        DateTime warnUntil = DateTime.MinValue;
        string warnText;

        public FocusHud(TrayApp app) : base(Ui.DarkSurface, true, true)
        {
            this.app = app;
            var row = CardForm.ButtonRow(false);
            row.Padding = Ui.Pad(18, 10, 10, 10);

            var dot = Ui.Label("●", Ui.Font(10f), Ui.Green);
            dot.Margin = Ui.Pad(0, 9, 8, 0);
            title = Ui.Label("", Ui.Font(10.5f, FontStyle.Bold), Ui.DarkText);
            title.Margin = Ui.Pad(0, 8, 12, 0);
            title.MaximumSize = new Size(Ui.S(260), Ui.S(24));
            time = Ui.Label("", Ui.Font(10.5f), Ui.DarkText);
            time.Margin = Ui.Pad(0, 8, 12, 0);
            note = Ui.Label("", Ui.Font(9f), Ui.DarkSubText);
            note.Margin = Ui.Pad(0, 10, 12, 0);

            doneButton = new UiButton("완료", ButtonKind.Secondary, true);
            doneButton.Click += delegate { app.CompleteFocus(); };
            stopButton = new UiButton("그만", ButtonKind.Ghost, true);
            stopButton.Click += delegate { app.StopFocus(true); };
            foreach (var b in new[] { doneButton, stopButton })
            {
                b.Height = Ui.S(32);
                b.Width = Ui.S(64);
                b.Margin = Ui.Pad(0, 0, 6, 0);
            }

            foreach (Control c in new Control[] { dot, title, time, note, doneButton, stopButton }) row.Controls.Add(c);
            Controls.Add(row);
            foreach (Control c in new Control[] { row, dot, title, time, note }) MakeDraggable(c);

            UpdateContent();
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Ui.S(10));

            timer.Interval = 500;
            timer.Tick += delegate { UpdateContent(); };
            timer.Start();
        }

        public void Warn(string text)
        {
            warnText = text;
            warnUntil = DateTime.UtcNow.AddSeconds(4);
            UpdateContent();
        }

        void UpdateContent()
        {
            var f = app.Focus;
            if (f == null) return;
            string day = app.Today;
            title.Text = f.Quest.Title;
            double progress = f.Quest.Progress(day);
            bool reached = f.Quest.TargetReached(day);
            time.Text = f.Quest.TargetMinutes > 0
                ? Ui.Clock(progress) + " / " + Ui.Clock(f.Quest.TargetMinutes * 60.0)
                : Ui.Clock(f.Elapsed);
            time.ForeColor = reached ? Ui.Green : Ui.DarkText;

            bool warning = DateTime.UtcNow < warnUntil;
            if (warning) { note.Text = warnText; note.ForeColor = Color.FromArgb(255, 138, 128); }
            else if (reached) { note.Text = "목표 달성! 완료를 눌러 주세요"; note.ForeColor = Ui.Green; }
            else if (f.Quest.Apps.Count > 0) { note.Text = "허용 앱 " + string.Join(", ", f.Quest.Apps.ToArray()); note.ForeColor = Ui.DarkSubText; }
            else { note.Text = "앱 제한 없음"; note.ForeColor = Ui.DarkSubText; }
            note.MaximumSize = new Size(Ui.S(280), Ui.S(20));

            var size = Controls[0].GetPreferredSize(Size.Empty);
            if (ClientSize != size)
            {
                int centerX = Left + Width / 2;
                ClientSize = size;
                Left = centerX - Width / 2;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            bool warning = DateTime.UtcNow < warnUntil;
            using (var b = new SolidBrush(warning ? Ui.Red : Ui.Green))
                e.Graphics.FillRectangle(b, 0, Height - Ui.S(3), Width, Ui.S(3));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
