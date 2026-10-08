using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PlayTimer
{
    // "사용 기록" 창: 언제, 어떤 앱을 썼는지 날짜별로 보여 준다.
    class HistoryForm : Form
    {
        public static readonly Color FreeColor = Color.FromArgb(66, 133, 244);
        public static readonly Color QuestColor = Color.FromArgb(30, 142, 62);
        public static readonly Color IdleColor = Color.FromArgb(218, 220, 224);

        readonly TrayApp app;
        readonly UsageHistory history;
        readonly Label dateLabel;
        readonly UiButton nextButton, todayButton;
        readonly Panel body;
        readonly FlowLayoutPanel content;
        readonly WeekChart week;
        readonly int contentWidth;
        string day;

        public HistoryForm(TrayApp app, UsageHistory history)
        {
            this.app = app;
            this.history = history;
            day = app.Today;

            Text = "PlayTimer 사용 기록";
            if (Ui.AppIcon != null) Icon = Ui.AppIcon; else ShowIcon = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(9.5f);
            BackColor = Ui.Surface;
            KeyPreview = true;

            int pad = Ui.S(24);
            int width = Ui.S(720);
            contentWidth = width - pad * 2 - SystemInformation.VerticalScrollBarWidth;

            var title = Ui.Label("사용 기록", Ui.Font(16f, FontStyle.Bold), Ui.Text);
            title.Location = new Point(pad, Ui.S(18));
            var subtitle = Ui.Label("언제, 어떤 앱을 썼는지 보여 줘요. 기록은 이 PC 안에만 저장돼요.", Ui.Font(9.5f), Ui.SubText);
            subtitle.Location = new Point(pad, Ui.S(54));
            Controls.Add(title);
            Controls.Add(subtitle);

            var nav = CardForm.ButtonRow(false);
            nav.Location = new Point(pad - Ui.S(4), Ui.S(86));
            var prev = new UiButton("◀", ButtonKind.Ghost, false) { Size = Ui.S(40, 34) };
            prev.Click += delegate { MoveDay(-1); };
            dateLabel = Ui.Label("", Ui.Font(13f, FontStyle.Bold), Ui.Text);
            dateLabel.AutoSize = false;
            dateLabel.Size = Ui.S(170, 34);
            dateLabel.TextAlign = ContentAlignment.MiddleCenter;
            nextButton = new UiButton("▶", ButtonKind.Ghost, false) { Size = Ui.S(40, 34) };
            nextButton.Click += delegate { MoveDay(1); };
            todayButton = new UiButton("오늘", ButtonKind.Secondary, false) { Size = Ui.S(64, 34) };
            todayButton.Margin = Ui.Pad(12, 0, 0, 0);
            todayButton.Click += delegate { SelectDay(app.Today); };
            foreach (Control c in new Control[] { prev, dateLabel, nextButton, todayButton }) nav.Controls.Add(c);
            Controls.Add(nav);

            week = new WeekChart(history, app.Today) { Location = new Point(pad, Ui.S(132)), Size = new Size(width - pad * 2, Ui.S(132)) };
            week.DaySelected += delegate { SelectDay(week.Selected); };
            Controls.Add(week);

            int top = week.Bottom + Ui.S(8);
            Controls.Add(new Panel { BackColor = Ui.Line, Location = new Point(0, top), Size = new Size(width, 1) });

            int available = Screen.FromPoint(Cursor.Position).WorkingArea.Height - top - Ui.S(70);
            body = new Panel
            {
                Location = new Point(pad, top + 1),
                Size = new Size(width - pad * 2, Math.Max(Ui.S(260), Math.Min(Ui.S(520), available))),
                AutoScroll = true,
                BackColor = Ui.Surface
            };
            content = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Ui.Surface,
                Padding = Ui.Pad(0, 12, 0, 20)
            };
            body.Controls.Add(content);
            Controls.Add(body);

            ClientSize = new Size(width, body.Bottom + Ui.S(4));

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Left) { MoveDay(-1); e.Handled = true; }
                else if (e.KeyCode == Keys.Right) { MoveDay(1); e.Handled = true; }
            };

            SelectDay(day);
        }

        static DateTime Parse(string d) { return DateTime.ParseExact(d, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); }

        void MoveDay(int days)
        {
            string target = Parse(day).AddDays(days).ToString("yyyy-MM-dd");
            if (string.CompareOrdinal(target, app.Today) > 0) return;
            SelectDay(target);
        }

        public void SelectDay(string d)
        {
            day = d;
            var date = Parse(d);
            dateLabel.Text = string.Format("{0}월 {1}일 ({2})", date.Month, date.Day, ScheduleForm.DayNames[(int)date.DayOfWeek]);
            nextButton.Enabled = string.CompareOrdinal(d, app.Today) < 0;
            todayButton.Enabled = d != app.Today;
            week.SelectDay(d);
            Rebuild();
        }

        // 오늘을 보고 있으면 기록이 쌓이는 대로 새로 그린다.
        public void RefreshIfToday()
        {
            if (day == app.Today && !IsDisposed) { week.Invalidate(); Rebuild(); }
        }

        string Clock(int offset)
        {
            return history.DayStart(day).AddSeconds(offset).ToString("HH:mm");
        }

        void Rebuild()
        {
            var log = history.Load(day);
            int scroll = -body.AutoScrollPosition.Y;
            content.SuspendLayout();
            foreach (Control c in content.Controls) c.Dispose();
            content.Controls.Clear();

            var tiles = CardForm.ButtonRow(false);
            tiles.Margin = Ui.Pad(0, 0, 0, 18);
            tiles.Controls.Add(Tile("PC 사용", log.ActiveSeconds, Ui.Text));
            tiles.Controls.Add(Tile("놀이", log.Total(UsageKind.Free), FreeColor));
            tiles.Controls.Add(Tile("퀘스트 집중", log.Total(UsageKind.Quest), QuestColor));
            tiles.Controls.Add(Tile("자리 비움", log.Total(UsageKind.Idle), Ui.SubText));
            content.Controls.Add(tiles);

            if (log.Segments.Count == 0)
            {
                var empty = Ui.Label(day == app.Today ? "아직 오늘 기록이 없어요. PlayTimer가 켜져 있는 동안 기록돼요." : "이날은 기록이 없어요.",
                    Ui.Font(10f), Ui.SubText);
                empty.Margin = Ui.Pad(0, 8, 0, 0);
                content.Controls.Add(empty);
                content.ResumeLayout();
                return;
            }

            content.Controls.Add(Section("하루 타임라인"));
            var timeline = new Timeline(history, log) { Size = new Size(contentWidth, Ui.S(78)), Margin = Ui.Pad(0, 0, 0, 4) };
            content.Controls.Add(timeline);
            var legend = new Legend { Size = new Size(contentWidth, Ui.S(22)), Margin = Ui.Pad(0, 0, 0, 18) };
            content.Controls.Add(legend);
            timeline.HoverChanged += delegate { legend.Hover = timeline.HoverText; legend.Invalidate(); };

            content.Controls.Add(Section("앱별 사용 시간"));
            var apps = log.ByApp();
            int max = apps.Count > 0 ? apps[0].Value[0] + apps[0].Value[1] : 1;
            for (int i = 0; i < apps.Count && i < 12; i++)
            {
                var row = new AppRow(log.NameOf(apps[i].Key), apps[i].Value[0], apps[i].Value[1], max) { Size = new Size(contentWidth, Ui.S(36)) };
                string appId = apps[i].Key;
                row.MouseEnter += delegate { timeline.HighlightApp = appId; };
                row.MouseLeave += delegate { timeline.HighlightApp = null; };
                content.Controls.Add(row);
            }
            if (apps.Count > 12)
            {
                int rest = 0;
                for (int i = 12; i < apps.Count; i++) rest += apps[i].Value[0] + apps[i].Value[1];
                var more = Ui.Label(string.Format("그 외 {0}개 앱 · {1}", apps.Count - 12, Ui.Duration(rest / 60)), Ui.Font(9f), Ui.SubText);
                more.Margin = Ui.Pad(4, 4, 0, 0);
                content.Controls.Add(more);
            }

            content.Controls.Add(Section("사용한 시간대"));
            foreach (var s in log.Sessions(10 * 60))
            {
                var tops = log.TopAppsIn(s.Start, s.End, 3);
                var line = Ui.Label(string.Format("{0} – {1}    {2}    {3}", Clock(s.Start), Clock(s.End), Ui.Duration(s.Seconds / 60), string.Join(", ", tops.ToArray())),
                    Ui.Font(10f), Ui.Text);
                line.MaximumSize = new Size(contentWidth, 0);
                line.Margin = Ui.Pad(4, 0, 0, 8);
                content.Controls.Add(line);
            }

            if (log.Done.Count > 0)
            {
                content.Controls.Add(Section("끝낸 퀘스트"));
                foreach (var d in log.Done)
                {
                    var line = Ui.Label(Clock(d.At) + "    ✓ " + d.Title, Ui.Font(10f), QuestColor);
                    line.Margin = Ui.Pad(4, 0, 0, 8);
                    content.Controls.Add(line);
                }
            }
            content.ResumeLayout();
            body.AutoScrollPosition = new Point(0, scroll);
        }

        static Label Section(string text)
        {
            var l = Ui.Label(text, Ui.Font(10f, FontStyle.Bold), Ui.Text);
            l.Margin = Ui.Pad(0, 6, 0, 10);
            return l;
        }

        Control Tile(string caption, int seconds, Color color)
        {
            var p = new Panel { Size = Ui.S(150, 64), Margin = Ui.Pad(0, 0, 10, 0), BackColor = Ui.Surface };
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Ui.Round(new Rectangle(0, 0, p.Width - 1, p.Height - 1), Ui.S(10)))
                using (var b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, path);
                using (var f = Ui.Font(8.5f))
                    TextRenderer.DrawText(g, caption, f, new Point(Ui.S(12), Ui.S(10)), Ui.SubText);
                using (var f = Ui.Font(14f, FontStyle.Bold))
                    TextRenderer.DrawText(g, seconds < 60 ? "0분" : Ui.Duration(seconds / 60), f, new Point(Ui.S(10), Ui.S(28)), color);
            };
            return p;
        }
    }

    // 최근 7일 막대 그래프. 막대를 누르면 그날로 간다.
    class WeekChart : Control
    {
        readonly UsageHistory history;
        readonly string today;
        string end;
        string selected;
        int hover = -1;
        readonly Dictionary<string, int[]> cache = new Dictionary<string, int[]>();

        public event EventHandler DaySelected;
        public string Selected { get { return selected; } }

        public WeekChart(UsageHistory history, string today)
        {
            this.history = history;
            this.today = today;
            end = today;
            selected = today;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        static DateTime Parse(string d) { return DateTime.ParseExact(d, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); }
        static string Key(DateTime d) { return d.ToString("yyyy-MM-dd"); }

        public void SelectDay(string d)
        {
            selected = d;
            // 고른 날이 보이는 7일 밖이면 그날이 오른쪽 끝에 오도록 옮긴다.
            var e = Parse(end);
            var s = Parse(d);
            if (s > e || s <= e.AddDays(-7)) end = d;
            cache.Clear();
            Invalidate();
        }

        int[] Totals(string d)
        {
            int[] v;
            if (d == today || !cache.TryGetValue(d, out v))
            {
                var log = history.Load(d);
                v = new[] { log.Total(UsageKind.Free), log.Total(UsageKind.Quest) };
                if (d != today) cache[d] = v;
            }
            return v;
        }

        int Column(Point p)
        {
            int c = p.X * 7 / Math.Max(1, Width);
            return c >= 0 && c < 7 ? c : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int c = Column(e.Location); if (c != hover) { hover = c; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int c = Column(e.Location);
            if (c < 0) return;
            string d = Key(Parse(end).AddDays(c - 6));
            if (string.CompareOrdinal(d, today) > 0) return;
            selected = d;
            Invalidate();
            if (DaySelected != null) DaySelected(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var endDate = Parse(end);
            var totals = new int[7][];
            int max = 60 * 60;
            for (int i = 0; i < 7; i++)
            {
                totals[i] = Totals(Key(endDate.AddDays(i - 6)));
                max = Math.Max(max, totals[i][0] + totals[i][1]);
            }

            int colW = Width / 7;
            int labelH = Ui.S(36), valueH = Ui.S(18);
            int chartH = Height - labelH - valueH;
            using (var small = Ui.Font(8.5f))
            using (var bold = Ui.Font(9f, FontStyle.Bold))
            using (var free = new SolidBrush(HistoryForm.FreeColor))
            using (var quest = new SolidBrush(HistoryForm.QuestColor))
            {
                for (int i = 0; i < 7; i++)
                {
                    var date = endDate.AddDays(i - 6);
                    string key = Key(date);
                    bool future = string.CompareOrdinal(key, today) > 0;
                    bool isSel = key == selected;
                    int x = i * colW;

                    if (isSel || i == hover)
                        using (var b = new SolidBrush(isSel ? Color.FromArgb(232, 240, 254) : Ui.SurfaceAlt))
                        using (var path = Ui.Round(new Rectangle(x + Ui.S(4), 0, colW - Ui.S(8), Height - 1), Ui.S(10)))
                            g.FillPath(b, path);

                    int f = totals[i][0], q = totals[i][1];
                    int barW = Math.Min(Ui.S(28), colW / 3);
                    int bx = x + (colW - barW) / 2;
                    int baseY = valueH + chartH;
                    int fh = (int)((double)f / max * (chartH - Ui.S(6)));
                    int qh = (int)((double)q / max * (chartH - Ui.S(6)));
                    if (qh > 0) g.FillRectangle(quest, bx, baseY - qh, barW, qh);
                    if (fh > 0) g.FillRectangle(free, bx, baseY - qh - fh, barW, fh);
                    if (fh + qh == 0 && !future)
                        using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, bx, baseY - 1, bx + barW, baseY - 1);

                    string value = future ? "" : f + q < 60 ? "-" : ShortDuration((f + q) / 60);
                    TextRenderer.DrawText(g, value, small, new Rectangle(x, baseY - qh - fh - valueH, colW, valueH), Ui.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);

                    Color nameColor = isSel ? Ui.Blue : future ? Ui.Line : Ui.SubText;
                    TextRenderer.DrawText(g, ScheduleForm.DayNames[(int)date.DayOfWeek], isSel ? bold : small,
                        new Rectangle(x, baseY + Ui.S(4), colW, Ui.S(16)), nameColor, TextFormatFlags.HorizontalCenter);
                    TextRenderer.DrawText(g, date.Month + "/" + date.Day, small,
                        new Rectangle(x, baseY + Ui.S(19), colW, Ui.S(16)), nameColor, TextFormatFlags.HorizontalCenter);
                }
            }
        }

        static string ShortDuration(int minutes)
        {
            if (minutes < 60) return minutes + "분";
            return minutes % 60 == 0 ? (minutes / 60) + "시간" : string.Format("{0}시간 {1}분", minutes / 60, minutes % 60);
        }
    }

    // 하루를 가로 막대 하나로: 어느 시각에 무엇을 했는지.
    class Timeline : Control
    {
        readonly UsageHistory history;
        readonly DayLog log;
        string highlight;
        UsageSegment hoverSeg;

        public event EventHandler HoverChanged;
        public string HoverText { get; private set; }

        public Timeline(UsageHistory history, DayLog log)
        {
            this.history = history;
            this.log = log;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public string HighlightApp
        {
            get { return highlight; }
            set { if (highlight != value) { highlight = value; Invalidate(); } }
        }

        Rectangle Track() { return new Rectangle(0, Ui.S(4), Width - 1, Ui.S(38)); }

        int X(int offset) { var t = Track(); return t.X + (int)((long)offset * t.Width / 86400); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UsageSegment found = null;
            var t = Track();
            if (t.Contains(e.Location))
            {
                int offset = (int)((long)(e.X - t.X) * 86400 / Math.Max(1, t.Width));
                int slack = (int)(86400L * Ui.S(2) / Math.Max(1, t.Width));
                foreach (var s in log.Segments)
                    if (offset >= s.Start - slack && offset <= s.End + slack) { found = s; if (offset >= s.Start && offset <= s.End) break; }
            }
            if (found == hoverSeg) return;
            hoverSeg = found;
            if (found == null) HoverText = null;
            else
            {
                string kind = found.Kind == UsageKind.Idle ? "자리 비움" : found.Kind == UsageKind.Quest ? "퀘스트: " + found.Quest : "놀이";
                string what = found.Kind == UsageKind.Idle ? "" : log.NameOf(found.App) + "  ·  ";
                var start = history.DayStart(log.Day);
                HoverText = string.Format("{0} – {1}  ·  {2}{3}  ·  {4}", start.AddSeconds(found.Start).ToString("HH:mm"),
                    start.AddSeconds(found.End).ToString("HH:mm"), what, kind, Ui.Duration(Math.Max(1, found.Seconds / 60)));
            }
            Invalidate();
            if (HoverChanged != null) HoverChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverSeg == null) return;
            hoverSeg = null;
            HoverText = null;
            Invalidate();
            if (HoverChanged != null) HoverChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Track();
            using (var path = Ui.Round(t, Ui.S(6)))
            using (var b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, path);

            g.SmoothingMode = SmoothingMode.None;
            foreach (var s in log.Segments)
            {
                Color c = s.Kind == UsageKind.Idle ? HistoryForm.IdleColor : s.Kind == UsageKind.Quest ? HistoryForm.QuestColor : HistoryForm.FreeColor;
                if (highlight != null && s.App != highlight) c = Ui.Mix(c, Ui.Surface, 0.75f);
                if (s == hoverSeg) c = Ui.Mix(c, Color.Black, 0.2f);
                int x1 = X(s.Start), x2 = Math.Max(X(s.End), x1 + 1);
                using (var b = new SolidBrush(c)) g.FillRectangle(b, x1, t.Y, x2 - x1, t.Height);
            }

            // 3시간마다 눈금과 시각
            var start = history.DayStart(log.Day);
            using (var pen = new Pen(Ui.Line))
            using (var f = Ui.Font(8f))
            {
                for (int h = 0; h <= 24; h += 3)
                {
                    int x = X(h * 3600);
                    g.DrawLine(pen, x, t.Bottom + 2, x, t.Bottom + Ui.S(6));
                    string text = start.AddHours(h).ToString("HH:mm");
                    var size = TextRenderer.MeasureText(text, f);
                    int tx = Math.Max(0, Math.Min(Width - size.Width, x - size.Width / 2));
                    TextRenderer.DrawText(g, text, f, new Point(tx, t.Bottom + Ui.S(8)), Ui.SubText);
                }
            }

            // 오늘이면 지금 시각
            if (log.Day == DateTime.Now.AddHours(-start.Hour).ToString("yyyy-MM-dd"))
            {
                int now = (int)(DateTime.Now - start).TotalSeconds;
                if (now >= 0 && now <= 86400)
                    using (var red = new Pen(Ui.Red, Math.Max(1, Ui.S(2)))) g.DrawLine(red, X(now), t.Y - Ui.S(3), X(now), t.Bottom + Ui.S(3));
            }
        }
    }

    // 타임라인 아래 범례. 막대에 마우스를 올리면 그 구간 설명으로 바뀐다.
    class Legend : Control
    {
        public string Hover;

        public Legend()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Surface);
            using (var f = Ui.Font(9f))
            {
                if (Hover != null)
                {
                    TextRenderer.DrawText(g, Hover, f, new Rectangle(0, 0, Width, Height), Ui.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    return;
                }
                int x = 0, box = Ui.S(10), y = (Height - box) / 2;
                var items = new[] { new KeyValuePair<Color, string>(HistoryForm.FreeColor, "놀이"),
                    new KeyValuePair<Color, string>(HistoryForm.QuestColor, "퀘스트 집중"),
                    new KeyValuePair<Color, string>(HistoryForm.IdleColor, "자리 비움") };
                foreach (var it in items)
                {
                    using (var b = new SolidBrush(it.Key)) g.FillRectangle(b, x, y, box, box);
                    x += box + Ui.S(5);
                    var size = TextRenderer.MeasureText(it.Value, f);
                    TextRenderer.DrawText(g, it.Value, f, new Point(x, (Height - size.Height) / 2), Ui.SubText);
                    x += size.Width + Ui.S(14);
                }
                TextRenderer.DrawText(g, "·  막대에 마우스를 올리면 자세히 보여요", f, new Point(x, (Height - TextRenderer.MeasureText("가", f).Height) / 2), Ui.SubText);
            }
        }
    }

    // 앱 한 줄: 이름, 놀이·퀘스트로 나눈 막대, 시간.
    class AppRow : Control
    {
        readonly string name;
        readonly int free, quest, max;
        bool hover;

        public AppRow(string name, int free, int quest, int max)
        {
            this.name = name;
            this.free = free;
            this.quest = quest;
            this.max = Math.Max(1, max);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = Padding.Empty;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(hover ? Ui.SurfaceAlt : Ui.Surface);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int nameW = Ui.S(190), timeW = Ui.S(130);
            using (var f = Ui.Font(10f))
                TextRenderer.DrawText(g, name, f, new Rectangle(Ui.S(6), 0, nameW - Ui.S(10), Height), Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            int barX = nameW, barMax = Width - nameW - timeW - Ui.S(8);
            int barH = Ui.S(10), barY = (Height - barH) / 2;
            int fw = (int)((double)free / max * barMax), qw = (int)((double)quest / max * barMax);
            using (var b = new SolidBrush(HistoryForm.FreeColor)) if (fw > 0) g.FillRectangle(b, barX, barY, fw, barH);
            using (var b = new SolidBrush(HistoryForm.QuestColor)) if (qw > 0) g.FillRectangle(b, barX + fw, barY, qw, barH);

            string detail = Ui.Duration((free + quest) / 60);
            if (free + quest < 60) detail = "1분 미만";
            if (quest > 0 && free > 0) detail += string.Format("  (퀘스트 {0})", Ui.Duration(quest / 60));
            using (var f = Ui.Font(9.5f))
                TextRenderer.DrawText(g, detail, f, new Rectangle(Width - timeW, 0, timeW - Ui.S(6), Height), Ui.SubText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
