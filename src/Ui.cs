using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlayTimer
{
    // 색, 글꼴, DPI 배율 등 화면 공통 요소.
    static class Ui
    {
        public static float Scale = 1f;

        public static readonly Color Blue = Color.FromArgb(26, 115, 232);
        public static readonly Color BlueLight = Color.FromArgb(66, 133, 244);
        public static readonly Color Red = Color.FromArgb(234, 67, 53);
        public static readonly Color Amber = Color.FromArgb(242, 153, 0);
        public static readonly Color Green = Color.FromArgb(30, 142, 62);

        public static readonly Color Text = Color.FromArgb(32, 33, 36);
        public static readonly Color SubText = Color.FromArgb(95, 99, 104);
        public static readonly Color Line = Color.FromArgb(218, 220, 224);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(241, 243, 244);

        public static readonly Color DarkSurface = Color.FromArgb(32, 33, 36);
        public static readonly Color DarkSurfaceAlt = Color.FromArgb(48, 49, 52);
        public static readonly Color DarkText = Color.FromArgb(232, 234, 237);
        public static readonly Color DarkSubText = Color.FromArgb(154, 160, 166);

        const string FontName = "Malgun Gothic";

        public static void Init()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            }
            catch { Scale = 1f; }
        }

        public static int S(int px) { return (int)Math.Round(px * Scale); }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public static Point P(int x, int y) { return new Point(S(x), S(y)); }
        public static Padding Pad(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        public static Font Font(float pt) { return new Font(FontName, pt); }
        public static Font Font(float pt, FontStyle style) { return new Font(FontName, pt, style); }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 2) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // 윈도우 11이면 창 모서리를 둥글게. 그 외에는 조용히 무시.
        public static void RoundCorners(Form f)
        {
            try
            {
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(f.Handle, 33, ref pref, sizeof(int));
            }
            catch { }
        }

        // 투명 배경이면 부모를 따라 올라가 실제로 보이는 배경색을 찾는다.
        public static Color EffectiveBack(Control c)
        {
            while (c != null)
            {
                if (c.BackColor.A == 255) return c.BackColor;
                c = c.Parent;
            }
            return Surface;
        }

        public static Label Label(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = true,
                Margin = Padding.Empty
            };
        }

        // "2시간 30분", "45분"
        public static string Duration(int minutes)
        {
            if (minutes <= 0) return "0분";
            int h = minutes / 60, m = minutes % 60;
            if (h == 0) return m + "분";
            if (m == 0) return h + "시간";
            return h + "시간 " + m + "분";
        }

        // 1:05:09 또는 5:09
        public static string Clock(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
            if (ts.TotalHours >= 1)
                return string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            return string.Format("{0}:{1:00}", ts.Minutes, ts.Seconds);
        }
    }

    enum ButtonKind { Primary, Danger, Secondary, Ghost }

    // 둥근 모서리, 호버/눌림/비활성 상태, 잠금 진행률 표시를 지원하는 버튼.
    class UiButton : Control
    {
        readonly ButtonKind kind;
        readonly bool dark;
        bool hover, pressed;
        float progress = -1f;

        public UiButton(string text, ButtonKind kind, bool dark)
        {
            this.kind = kind;
            this.dark = dark;
            Text = text;
            Font = Ui.Font(10f, kind == ButtonKind.Primary || kind == ButtonKind.Danger ? FontStyle.Bold : FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Ui.Pad(4, 0, 4, 0);
            FitToText();
        }

        public void FitToText()
        {
            var size = TextRenderer.MeasureText(Text, Font);
            Size = new Size(Math.Max(Ui.S(84), size.Width + Ui.S(32)), Ui.S(38));
        }

        // 0~1이면 버튼을 왼쪽부터 채우며 "잠시 기다려야 함"을 보여 준다. 음수면 표시 안 함.
        public float Progress
        {
            get { return progress; }
            set { if (Math.Abs(progress - value) > 0.001f) { progress = value; Invalidate(); } }
        }

        Color BaseBack()
        {
            switch (kind)
            {
                case ButtonKind.Primary: return Ui.Blue;
                case ButtonKind.Danger: return Ui.Red;
                case ButtonKind.Secondary: return dark ? Ui.DarkSurfaceAlt : Ui.SurfaceAlt;
                default: return dark ? Ui.DarkSurface : Ui.Surface;
            }
        }

        Color BaseFore()
        {
            if (kind == ButtonKind.Primary || kind == ButtonKind.Danger) return Color.White;
            if (kind == ButtonKind.Ghost) return dark ? Ui.DarkSubText : Ui.SubText;
            return dark ? Ui.DarkText : Ui.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color parentBack = Ui.EffectiveBack(Parent);
            g.Clear(parentBack);

            Color back = BaseBack();
            Color fore = BaseFore();
            Color hoverTarget = dark ? Color.White : Color.Black;
            if (Enabled && pressed) back = Ui.Mix(back, hoverTarget, 0.16f);
            else if (Enabled && hover) back = Ui.Mix(back, hoverTarget, 0.08f);
            if (!Enabled)
            {
                back = Ui.Mix(back, parentBack, 0.55f);
                fore = Ui.Mix(fore, parentBack, 0.45f);
            }

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Ui.Round(rect, Ui.S(8)))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                if (kind == ButtonKind.Ghost)
                    using (var pen = new Pen(dark ? Ui.DarkSurfaceAlt : Ui.Line)) g.DrawPath(pen, path);
                if (progress >= 0 && progress < 1)
                {
                    var clip = g.Clip;
                    g.SetClip(new Rectangle(0, 0, (int)(Width * progress), Height));
                    using (var b = new SolidBrush(Color.FromArgb(50, dark ? Color.White : Color.Black))) g.FillPath(b, path);
                    g.Clip = clip;
                }
                if (Focused && ShowFocusCues)
                    using (var pen = new Pen(Ui.BlueLight, 2)) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    // 테두리 없는 카드형 창. 그림자, 둥근 모서리(윈11), 드래그 이동을 지원한다.
    class CardForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        readonly bool noActivate;
        readonly bool topMost;

        public CardForm(Color back, bool topMost, bool noActivate)
        {
            this.noActivate = noActivate;
            this.topMost = topMost;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = back;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            // 포커스를 뺏지 않는 창은 TopMost 속성 대신 WS_EX_TOPMOST 로 띄운다(속성을 쓰면 활성화될 수 있음).
            if (topMost && !noActivate) TopMost = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                if (noActivate) cp.ExStyle |= 0x08000000 | 0x80; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                if (noActivate && topMost) cp.ExStyle |= 0x8; // WS_EX_TOPMOST
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return noActivate; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.RoundCorners(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 윈도우 10처럼 모서리가 각진 환경에서도 경계가 보이도록 얇은 테두리.
            using (var pen = new Pen(Ui.Mix(BackColor, BackColor.GetBrightness() > 0.5f ? Color.Black : Color.White, 0.15f)))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        // 배경을 잡고 끌면 창이 움직인다.
        public void MakeDraggable(Control c)
        {
            c.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
            };
        }

        public static FlowLayoutPanel ButtonRow(bool rightAlign)
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = rightAlign ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
        }
    }

    // MessageBox 대신 쓰는 확인 창. 누른 버튼의 번호를 돌려준다(닫으면 -1).
    class ConfirmDialog : CardForm
    {
        public int Choice = -1;

        ConfirmDialog(string title, string body, string[] buttons, ButtonKind[] kinds, int defaultIndex)
            : base(Ui.Surface, true, false)
        {
            KeyPreview = true;
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Ui.Pad(24, 22, 24, 18),
                BackColor = Color.Transparent
            };
            var titleLabel = Ui.Label(title, Ui.Font(13f, FontStyle.Bold), Ui.Text);
            titleLabel.Margin = Ui.Pad(0, 0, 0, 8);
            var bodyLabel = Ui.Label(body, Ui.Font(10f), Ui.SubText);
            bodyLabel.MaximumSize = new Size(Ui.S(380), 0);
            bodyLabel.Margin = Ui.Pad(0, 0, 0, 20);
            stack.Controls.Add(titleLabel);
            stack.Controls.Add(bodyLabel);

            var row = ButtonRow(true);
            UiButton focus = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                var b = new UiButton(buttons[i], kinds[i], false);
                b.Click += delegate { Choice = index; Close(); };
                row.Controls.Add(b);
                if (i == defaultIndex) focus = b;
            }
            stack.Controls.Add(row);
            Controls.Add(stack);

            MakeDraggable(stack);
            MakeDraggable(titleLabel);
            Load += delegate
            {
                var pref = stack.GetPreferredSize(Size.Empty);
                int rowWidth = row.GetPreferredSize(Size.Empty).Width;
                int width = Math.Max(Ui.S(400), Math.Max(pref.Width, rowWidth + stack.Padding.Horizontal));
                row.Margin = new Padding(Math.Max(0, width - stack.Padding.Horizontal - rowWidth), 0, 0, 0);
                ClientSize = new Size(width, stack.GetPreferredSize(Size.Empty).Height);
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            };
            Shown += delegate { if (focus != null) focus.Focus(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        public static int Show(IWin32Window owner, string title, string body, string[] buttons, ButtonKind[] kinds, int defaultIndex)
        {
            using (var d = new ConfirmDialog(title, body, buttons, kinds, defaultIndex))
            {
                if (owner != null) d.ShowDialog(owner); else d.ShowDialog();
                return d.Choice;
            }
        }

        public static void Info(IWin32Window owner, string title, string body)
        {
            Show(owner, title, body, new[] { "확인" }, new[] { ButtonKind.Primary }, 0);
        }
    }
}
