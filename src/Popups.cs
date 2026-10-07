using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PlayTimer
{
    // 팝업들이 화면에 보여 줄 현재 상태 한 장.
    class Status
    {
        public double Remaining;        // 총량·시간대 중 먼저 끝나는 쪽 기준. 음수면 초과
        public double UsedSeconds;
        public double LimitSeconds;
        public bool QuotaBinding;       // 총량이 먼저 끝나는가
        public bool InWindow;
        public string WindowLine;       // "18:00 – 22:00 사용 가능" 등
        public string DayLabel;         // "10월 7일 (수)"
        public string Reason;           // 왜 조르는지 한 줄
        public string ReasonDetail;
        public string EscalationHint;   // 다음에 무슨 일이 일어나는지
        public int ExtensionsLeft;
        public int ExtensionMinutes;

        public bool Over { get { return Remaining <= 0; } }
        public double OverSeconds { get { return Math.Max(0, -Remaining); } }
        public bool CanExtend { get { return ExtensionsLeft > 0 && ExtensionMinutes > 0; } }

        public Color StateColor
        {
            get { return Over ? Ui.Red : Remaining <= 600 ? Ui.Amber : Ui.Blue; }
        }
    }

    // 오른쪽 아래에 잠깐 떴다 사라지는 알림. 여러 개면 위로 쌓인다.
    class Toast : CardForm
    {
        static readonly List<Toast> open = new List<Toast>();
        readonly Timer life = new Timer();
        readonly Color accent;

        public static void Show(string title, string body, Color accent)
        {
            new Toast(title, body, accent).Show();
        }

        Toast(string title, string body, Color accent) : base(Ui.DarkSurface, true, true)
        {
            this.accent = accent;
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Ui.Pad(22, 14, 18, 14),
                BackColor = Color.Transparent
            };
            var t = Ui.Label(title, Ui.Font(11f, FontStyle.Bold), Ui.DarkText);
            t.Margin = Ui.Pad(0, 0, 0, 4);
            var b = Ui.Label(body, Ui.Font(9.5f), Ui.DarkSubText);
            b.MaximumSize = new Size(Ui.S(300), 0);
            stack.Controls.Add(t);
            stack.Controls.Add(b);
            Controls.Add(stack);

            foreach (Control c in new Control[] { this, stack, t, b })
                c.Click += delegate { Close(); };
            Cursor = Cursors.Hand;

            var pref = stack.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Ui.S(340), pref.Height);
            Opacity = 0;

            life.Interval = 30;
            int age = 0;
            life.Tick += delegate
            {
                age += life.Interval;
                if (age < 200) Opacity = age / 200.0;
                else if (age < 8000) Opacity = 1;
                else if (age < 8300) Opacity = 1 - (age - 8000) / 300.0;
                else Close();
            };
            Shown += delegate { life.Start(); };

            open.Add(this);
            Reflow();
            FormClosed += delegate { open.Remove(this); Reflow(); };
        }

        static void Reflow()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            int y = area.Bottom - Ui.S(16);
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var t = open[i];
                y -= t.Height;
                t.Location = new Point(area.Right - t.Width - Ui.S(16), y);
                y -= Ui.S(10);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var b = new SolidBrush(accent))
                e.Graphics.FillRectangle(b, 0, 0, Ui.S(5), Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) life.Dispose();
            base.Dispose(disposing);
        }
    }

    enum NagSize { Small, Medium, Large }

    // 1·2·3단계 조르기 창이 공통으로 쓰는 내용 영역.
    class NagPanel : FlowLayoutPanel
    {
        readonly TrayApp app;
        readonly NagSize size;
        readonly Label header, big, title, detail, hint;
        readonly UiButton shutdownButton, extendButton, closeButton;
        readonly string closeText;
        readonly int closeDelay;
        readonly DateTime shownAt = DateTime.UtcNow;

        public NagPanel(TrayApp app, Form host, NagSize size, int closeDelaySeconds)
        {
            this.app = app;
            this.size = size;
            closeDelay = closeDelaySeconds;

            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.Transparent;

            bool large = size == NagSize.Large;
            int wrap = Ui.S(size == NagSize.Small ? 360 : size == NagSize.Medium ? 460 : 860);

            header = Ui.Label("", Ui.Font(large ? 14f : 10f, FontStyle.Bold), Ui.Red);
            big = Ui.Label("", Ui.Font(large ? 64f : 34f, FontStyle.Bold), Ui.Red);
            title = Ui.Label("", Ui.Font(large ? 26f : size == NagSize.Medium ? 16f : 13f, FontStyle.Bold), Color.White);
            detail = Ui.Label("", Ui.Font(large ? 14f : 10f), Ui.DarkText);
            hint = Ui.Label("", Ui.Font(large ? 12f : 9f), Ui.DarkSubText);
            foreach (var l in new[] { header, big, title, detail, hint })
            {
                l.MaximumSize = new Size(wrap, 0);
                if (large) { l.Anchor = AnchorStyles.None; l.TextAlign = ContentAlignment.MiddleCenter; }
            }
            header.Margin = Ui.Pad(0, 0, 0, size == NagSize.Small ? 6 : 0);
            big.Margin = Ui.Pad(0, 0, 0, large ? 8 : 4);
            title.Margin = Ui.Pad(0, 0, 0, large ? 12 : 6);
            detail.Margin = Ui.Pad(0, 0, 0, 4);
            hint.Margin = Ui.Pad(0, 0, 0, large ? 36 : 20);

            Controls.Add(header);
            if (size != NagSize.Small) Controls.Add(big);
            Controls.Add(title);
            Controls.Add(detail);
            Controls.Add(hint);

            closeText = large ? "닫기" : "조금만 더";
            shutdownButton = new UiButton("지금 PC 끄기", ButtonKind.Danger, true);
            shutdownButton.Click += delegate { app.RequestShutdown(host); };
            extendButton = new UiButton("", ButtonKind.Secondary, true);
            extendButton.Click += delegate { app.RequestExtend(host); };
            closeButton = new UiButton(closeText + " (00)", ButtonKind.Ghost, true);
            closeButton.Click += delegate { host.Close(); };
            if (large)
                foreach (var b in new[] { shutdownButton, extendButton, closeButton })
                {
                    b.Font = Ui.Font(12f, b == shutdownButton ? FontStyle.Bold : FontStyle.Regular);
                    b.FitToText();
                    b.Height = Ui.S(48);
                }

            var row = CardForm.ButtonRow(false);
            row.Controls.Add(shutdownButton);
            row.Controls.Add(extendButton);
            row.Controls.Add(closeButton);
            if (large) row.Anchor = AnchorStyles.None;
            row.Margin = Ui.Pad(-4, 0, 0, 0);
            Controls.Add(row);

            UpdateContent();
        }

        public bool CanClose
        {
            get { return app.ClosingByApp || (DateTime.UtcNow - shownAt).TotalSeconds >= closeDelay; }
        }

        public void UpdateContent()
        {
            var st = app.GetStatus();
            string over = "+" + Ui.Clock(st.OverSeconds);
            switch (size)
            {
                case NagSize.Small:
                    header.Text = "●  시간 초과  " + over;
                    title.Text = st.Reason;
                    break;
                case NagSize.Medium:
                    header.Text = "시간 초과";
                    big.Text = over;
                    title.Text = "진짜로 이제 그만할 시간이에요";
                    break;
                default:
                    header.Text = "시간 초과";
                    big.Text = over;
                    title.Text = "그만! 오늘은 여기까지";
                    break;
            }
            detail.Text = size == NagSize.Small ? st.ReasonDetail : st.Reason + ". " + st.ReasonDetail;
            hint.Text = st.EscalationHint;

            extendButton.Visible = st.CanExtend;
            string ext = string.Format("{0}분 연장 ({1}회 남음)", st.ExtensionMinutes, st.ExtensionsLeft);
            if (extendButton.Text != ext) { extendButton.Text = ext; FitKeepHeight(extendButton); }

            double waited = (DateTime.UtcNow - shownAt).TotalSeconds;
            if (waited < closeDelay)
            {
                closeButton.Enabled = false;
                closeButton.Text = string.Format("{0} ({1})", closeText, (int)Math.Ceiling(closeDelay - waited));
                closeButton.Progress = (float)(waited / closeDelay);
            }
            else if (!closeButton.Enabled || closeButton.Text != closeText)
            {
                closeButton.Enabled = true;
                closeButton.Text = closeText;
                closeButton.Progress = -1;
            }
        }

        static void FitKeepHeight(UiButton b)
        {
            int h = b.Height;
            b.FitToText();
            b.Height = h;
        }
    }

    // 1단계: 오른쪽 아래 카드. 하던 일을 방해하지 않도록 포커스를 뺏지 않는다.
    class NagCard : CardForm
    {
        readonly NagPanel panel;
        readonly Timer timer = new Timer();

        public NagCard(TrayApp app) : base(Ui.DarkSurface, true, true)
        {
            panel = new NagPanel(app, this, NagSize.Small, 0);
            panel.Padding = Ui.Pad(24, 20, 24, 20);
            Controls.Add(panel);
            MakeDraggable(panel);
            ClientSize = panel.GetPreferredSize(Size.Empty);
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - Ui.S(16), area.Bottom - Height - Ui.S(16));

            timer.Interval = 1000;
            timer.Tick += delegate { panel.UpdateContent(); };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var b = new SolidBrush(Ui.Red))
                e.Graphics.FillRectangle(b, 0, 0, Ui.S(5), Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // 2단계: 화면 가운데 창. 몇 초 동안 닫을 수 없다.
    class NagDialog : CardForm
    {
        readonly NagPanel panel;
        readonly Timer timer = new Timer();

        public NagDialog(TrayApp app, int closeDelaySeconds) : base(Ui.DarkSurface, true, false)
        {
            panel = new NagPanel(app, this, NagSize.Medium, closeDelaySeconds);
            panel.Padding = Ui.Pad(36, 30, 36, 30);
            Controls.Add(panel);
            MakeDraggable(panel);
            ClientSize = panel.GetPreferredSize(Size.Empty);
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);

            timer.Interval = 250;
            timer.Tick += delegate { panel.UpdateContent(); };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var b = new SolidBrush(Ui.Red))
                e.Graphics.FillRectangle(b, 0, 0, Width, Ui.S(5));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !panel.CanClose) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // 3단계: 모든 모니터를 덮는 전체화면.
    class OverlayNag : Form
    {
        readonly NagPanel panel;
        readonly List<Form> covers = new List<Form>();
        readonly Timer timer = new Timer();

        public OverlayNag(TrayApp app, int closeDelaySeconds)
        {
            ConfigureCover(this, Screen.PrimaryScreen);
            panel = new NagPanel(app, this, NagSize.Large, closeDelaySeconds);
            Controls.Add(panel);
            Layout += delegate { CenterPanel(); };

            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Primary) continue;
                var cover = new Form();
                ConfigureCover(cover, screen);
                cover.FormClosing += delegate(object s, FormClosingEventArgs e)
                {
                    if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
                };
                covers.Add(cover);
            }
            Shown += delegate { foreach (var c in covers) c.Show(); CenterPanel(); };
            FormClosed += delegate { foreach (var c in covers) { c.Hide(); c.Dispose(); } };

            timer.Interval = 250;
            timer.Tick += delegate { panel.UpdateContent(); CenterPanel(); };
            timer.Start();
        }

        void CenterPanel()
        {
            var pref = panel.GetPreferredSize(Size.Empty);
            panel.Location = new Point((ClientSize.Width - pref.Width) / 2, (ClientSize.Height - pref.Height) / 2);
        }

        static void ConfigureCover(Form f, Screen screen)
        {
            f.FormBorderStyle = FormBorderStyle.None;
            f.ShowInTaskbar = false;
            f.TopMost = true;
            f.StartPosition = FormStartPosition.Manual;
            f.Bounds = screen.Bounds;
            f.BackColor = Color.FromArgb(18, 18, 20);
            f.Opacity = 0.95;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !panel.CanClose) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // 트레이 아이콘을 클릭하면 뜨는 오늘 현황 카드.
    class StatusFlyout : CardForm
    {
        readonly TrayApp app;
        readonly Label day, remaining, remainingSuffix, usage, window, extension;
        readonly Panel bar;
        readonly UiButton extendButton;
        readonly Timer timer = new Timer();
        Status last;

        public StatusFlyout(TrayApp app) : base(Ui.Surface, true, false)
        {
            this.app = app;
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Ui.Pad(22, 18, 22, 18),
                BackColor = Color.Transparent
            };

            day = Ui.Label("", Ui.Font(9.5f), Ui.SubText);
            day.Margin = Ui.Pad(0, 0, 0, 2);

            var remainRow = CardForm.ButtonRow(false);
            remaining = Ui.Label("", Ui.Font(26f, FontStyle.Bold), Ui.Blue);
            remainingSuffix = Ui.Label("", Ui.Font(11f), Ui.SubText);
            remainingSuffix.Margin = Ui.Pad(6, 16, 0, 0);
            remainRow.Controls.Add(remaining);
            remainRow.Controls.Add(remainingSuffix);
            remainRow.Margin = Ui.Pad(0, 0, 0, 8);

            bar = new Panel { Size = Ui.S(300, 8), Margin = Ui.Pad(0, 0, 0, 6), BackColor = Color.Transparent };
            bar.Paint += PaintBar;

            usage = Ui.Label("", Ui.Font(9.5f), Ui.SubText);
            usage.Margin = Ui.Pad(0, 0, 0, 14);
            window = Ui.Label("", Ui.Font(10f), Ui.Text);
            window.Margin = Ui.Pad(0, 0, 0, 4);
            extension = Ui.Label("", Ui.Font(9.5f), Ui.SubText);
            extension.Margin = Ui.Pad(0, 0, 0, 18);

            var row = CardForm.ButtonRow(false);
            var settings = new UiButton("시간 설정", ButtonKind.Primary, false);
            settings.Click += delegate { Close(); app.OpenSchedule(); };
            extendButton = new UiButton("연장", ButtonKind.Secondary, false);
            // 확인 창이 뜨면 이 카드는 포커스를 잃어 닫히므로, 먼저 닫고 묻는다.
            extendButton.Click += delegate { Close(); app.RequestExtend(null); };
            var shutdown = new UiButton("PC 끄기", ButtonKind.Ghost, false);
            shutdown.Click += delegate { Close(); app.RequestShutdown(null); };
            row.Controls.Add(settings);
            row.Controls.Add(extendButton);
            row.Controls.Add(shutdown);
            row.Margin = Ui.Pad(-4, 0, 0, 0);

            foreach (Control c in new Control[] { day, remainRow, bar, usage, window, extension, row })
                stack.Controls.Add(c);
            Controls.Add(stack);

            Refresh2();
            ClientSize = new Size(Math.Max(Ui.S(344), stack.GetPreferredSize(Size.Empty).Width), stack.GetPreferredSize(Size.Empty).Height);
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - Ui.S(12), area.Bottom - Height - Ui.S(12));

            Deactivate += delegate { Close(); };
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            timer.Interval = 1000;
            timer.Tick += delegate { Refresh2(); };
            timer.Start();
        }

        void Refresh2()
        {
            var st = app.GetStatus();
            last = st;
            day.Text = st.DayLabel;
            remaining.Text = Ui.Clock(Math.Abs(st.Remaining));
            remaining.ForeColor = st.StateColor;
            remainingSuffix.Text = st.Over ? "초과" : "남음";
            usage.Text = string.Format("오늘 {0} 사용 · 총량 {1}", Ui.Duration((int)(st.UsedSeconds / 60)), Ui.Duration((int)(st.LimitSeconds / 60)));
            window.Text = st.WindowLine;
            window.ForeColor = st.InWindow ? Ui.Green : Ui.Red;
            extension.Text = st.CanExtend
                ? string.Format("오늘 연장 {0}회 남음 ({1}분씩)", st.ExtensionsLeft, st.ExtensionMinutes)
                : "오늘은 더 연장할 수 없어요";
            extendButton.Enabled = st.CanExtend;
            if (st.CanExtend) extendButton.Text = "+" + st.ExtensionMinutes + "분";
            bar.Invalidate();
        }

        void PaintBar(object sender, PaintEventArgs e)
        {
            if (last == null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, bar.Width - 1, bar.Height - 1);
            using (var path = Ui.Round(rect, bar.Height / 2))
            using (var b = new SolidBrush(Ui.SurfaceAlt))
                g.FillPath(b, path);
            double frac = last.LimitSeconds > 0 ? Math.Min(1, last.UsedSeconds / last.LimitSeconds) : 1;
            int w = (int)(rect.Width * frac);
            if (w > 0)
                using (var path = Ui.Round(new Rectangle(0, 0, Math.Max(w, bar.Height), rect.Height), bar.Height / 2))
                using (var b = new SolidBrush(last.StateColor))
                    g.FillPath(b, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // "곧 꺼집니다" 카운트다운과 취소 버튼.
    class ShutdownNotice : CardForm
    {
        readonly Timer timer = new Timer();
        readonly DateTime at;
        readonly Label countdown;

        public event EventHandler Cancelled;

        public ShutdownNotice(int seconds) : base(Ui.DarkSurface, true, true)
        {
            at = DateTime.UtcNow.AddSeconds(seconds);
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Ui.Pad(24, 18, 24, 18),
                BackColor = Color.Transparent
            };
            var title = Ui.Label("PC가 곧 꺼져요", Ui.Font(12f, FontStyle.Bold), Ui.DarkText);
            countdown = Ui.Label("", Ui.Font(10f), Ui.DarkSubText);
            countdown.Margin = Ui.Pad(0, 4, 0, 14);
            var cancel = new UiButton("종료 취소", ButtonKind.Secondary, true);
            cancel.Margin = Padding.Empty;
            cancel.Click += delegate
            {
                if (Cancelled != null) Cancelled(this, EventArgs.Empty);
                Close();
            };
            stack.Controls.Add(title);
            stack.Controls.Add(countdown);
            stack.Controls.Add(cancel);
            Controls.Add(stack);
            Tick();
            ClientSize = new Size(Ui.S(320), stack.GetPreferredSize(Size.Empty).Height);
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - Ui.S(16), area.Bottom - Height - Ui.S(16));

            timer.Interval = 500;
            timer.Tick += delegate { Tick(); };
            timer.Start();
        }

        void Tick()
        {
            int left = (int)Math.Max(0, Math.Ceiling((at - DateTime.UtcNow).TotalSeconds));
            countdown.Text = left + "초 뒤에 꺼집니다. 저장할 것을 저장하세요.";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
