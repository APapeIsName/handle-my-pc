using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PlayTimer
{
    // 캘린더 주간 보기처럼 생긴 시간 설정 창.
    // 위쪽에서 요일별 하루 총량을 정하고, 아래 격자에서 드래그로 써도 되는 시간대를 칠한다.
    class ScheduleForm : Form
    {
        // 화면에 보이는 순서: 월 ~ 일
        public static readonly DayOfWeek[] Order = {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };
        public static readonly string[] DayNames = { "일", "월", "화", "수", "목", "금", "토" };

        public static int Gutter { get { return Ui.S(64); } }
        public static int ColWidth { get { return Ui.S(104); } }

        readonly Schedule original;
        readonly Schedule schedule;
        readonly int resetHour;
        readonly DayHeader header;
        readonly ScheduleGrid grid;
        readonly Label statusLabel;
        bool closingConfirmed;

        public event EventHandler Saved;
        public Schedule Result { get { return schedule; } }

        public ScheduleForm(Schedule current, int resetHour)
        {
            original = current.Clone();
            schedule = current.Clone();
            this.resetHour = resetHour;

            Text = "PlayTimer 시간 설정";
            if (Ui.AppIcon != null) Icon = Ui.AppIcon; else ShowIcon = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(9.5f);
            BackColor = Ui.Surface;
            KeyPreview = true;

            int pad = Ui.S(20);
            int gridWidth = Gutter + ColWidth * 7;
            int contentWidth = gridWidth + SystemInformation.VerticalScrollBarWidth;

            var title = Ui.Label("시간 설정", Ui.Font(16f, FontStyle.Bold), Ui.Text);
            title.Location = new Point(pad, Ui.S(16));
            var subtitle = Ui.Label("칠한 시간대 안에서, 요일별 총량까지 쓸 수 있어요. 드래그해서 칠하고, 칠한 곳에서 시작하면 지워져요.",
                Ui.Font(9.5f), Ui.SubText);
            subtitle.Location = new Point(pad, Ui.S(52));
            Controls.Add(title);
            Controls.Add(subtitle);

            header = new DayHeader(schedule, resetHour) { Location = new Point(pad, Ui.S(84)) };
            header.Changed += delegate { OnChanged(); };
            header.DayMenuRequested += ShowDayMenu;
            Controls.Add(header);

            // 나머지 요소 높이를 빼고 화면에 맞게 격자 높이를 정한다.
            int chrome = SystemInformation.CaptionHeight + SystemInformation.FrameBorderSize.Height * 2;
            int fixedPart = header.Bottom + Ui.S(8) + Ui.S(72);
            int available = Screen.FromPoint(Cursor.Position).WorkingArea.Height - chrome - fixedPart - Ui.S(24);
            int gridHeight = Math.Max(Ui.S(240), Math.Min(Ui.S(440), available));

            var scroller = new Panel
            {
                Location = new Point(pad, header.Bottom + Ui.S(8)),
                Size = new Size(contentWidth, gridHeight),
                AutoScroll = true,
                BackColor = Ui.Surface
            };
            grid = new ScheduleGrid(schedule, resetHour) { Location = Point.Empty };
            grid.Changed += delegate { OnChanged(); };
            grid.StatusChanged += delegate { SetStatus(grid.StatusText); };
            scroller.Controls.Add(grid);
            Controls.Add(scroller);

            var divider = new Panel { BackColor = Ui.Line, Location = new Point(0, scroller.Bottom + Ui.S(12)), Size = new Size(contentWidth + pad * 2, 1) };
            Controls.Add(divider);

            statusLabel = Ui.Label("", Ui.Font(9f), Ui.SubText);
            statusLabel.AutoSize = false;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Location = new Point(pad, divider.Bottom + Ui.S(14));
            statusLabel.Size = new Size(contentWidth - Ui.S(330), Ui.S(38));
            Controls.Add(statusLabel);

            var buttons = CardForm.ButtonRow(true);
            var save = new UiButton("저장", ButtonKind.Primary, false);
            save.Click += delegate { Save(); };
            var cancel = new UiButton("취소", ButtonKind.Ghost, false);
            cancel.Click += delegate { Close(); };
            var quick = new UiButton("빠른 설정  ▾", ButtonKind.Secondary, false);
            quick.Click += delegate { QuickMenu().Show(quick, new Point(0, quick.Height + Ui.S(2))); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(quick);
            var bsize = buttons.GetPreferredSize(Size.Empty);
            buttons.Location = new Point(pad + contentWidth - bsize.Width + Ui.S(4), divider.Bottom + Ui.S(14));
            Controls.Add(buttons);

            ClientSize = new Size(contentWidth + pad * 2, statusLabel.Bottom + Ui.S(16));
            SetStatus(null);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.S) { Save(); e.Handled = true; }
            };

            // 처음 열면 칠해 둔 시간대의 시작(없으면 지금 시각) 근처가 보이도록 스크롤한다.
            Shown += delegate
            {
                int y = grid.FirstBlockY();
                if (y < 0) y = grid.CurrentTimeY();
                scroller.AutoScrollPosition = new Point(0, Math.Max(0, y - Ui.S(60)));
            };
        }

        bool Dirty()
        {
            for (int d = 0; d < 7; d++)
            {
                if (original.LimitMinutes[d] != schedule.LimitMinutes[d]) return true;
                for (int s = 0; s < Schedule.SlotsPerDay; s++)
                    if (original.Allowed[d][s] != schedule.Allowed[d][s]) return true;
            }
            return false;
        }

        void OnChanged()
        {
            header.Invalidate();
            grid.Invalidate();
            if (!grid.Dragging) SetStatus(null);
        }

        void SetStatus(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                int week = 0;
                for (int d = 0; d < 7; d++) week += schedule.LimitMinutes[d];
                text = "이번 주 총량 " + Ui.Duration(week) + (Dirty() ? "   ·   저장하지 않은 변경 사항이 있어요" : "");
            }
            statusLabel.Text = text;
        }

        void Save()
        {
            if (Saved != null) Saved(this, EventArgs.Empty);
            closingConfirmed = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closingConfirmed && Dirty() && e.CloseReason == CloseReason.UserClosing)
            {
                int choice = ConfirmDialog.Show(this, "변경 사항을 저장할까요?", "저장하지 않으면 지금 바꾼 시간표가 사라져요.",
                    new[] { "저장", "저장 안 함", "취소" },
                    new[] { ButtonKind.Primary, ButtonKind.Ghost, ButtonKind.Ghost }, 0);
                if (choice == 0) { if (Saved != null) Saved(this, EventArgs.Empty); }
                else if (choice != 1) { e.Cancel = true; return; }
                closingConfirmed = true;
            }
            base.OnFormClosing(e);
        }

        // ── 메뉴 ────────────────────────────────────────────────

        static ContextMenuStrip NewMenu()
        {
            return new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
        }

        void ShowDayMenu(object sender, DayMenuEventArgs e)
        {
            var day = Order[e.Column];
            string name = DayNames[(int)day] + "요일";
            var menu = NewMenu();
            menu.Items.Add(new ToolStripLabel(name) { Font = Ui.Font(9.5f, FontStyle.Bold), ForeColor = Ui.SubText });
            menu.Items.Add(name + " 하루 종일 칠하기", null, delegate { FillDay(day, true); });
            menu.Items.Add(name + " 모두 지우기", null, delegate { FillDay(day, false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("이 설정을 평일(월~금)에 복사", null, delegate { CopyDay(day, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday); });
            menu.Items.Add("이 설정을 주말(토·일)에 복사", null, delegate { CopyDay(day, DayOfWeek.Saturday, DayOfWeek.Sunday); });
            menu.Items.Add("이 설정을 모든 요일에 복사", null, delegate { CopyDay(day, Order); });
            menu.Show(header, e.Location);
        }

        ContextMenuStrip QuickMenu()
        {
            var menu = NewMenu();
            menu.Items.Add("예시: 평일 저녁 2시간 · 주말 4시간", null, delegate { ApplyExample(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("시간대 제한 없애기 (모두 칠하기)", null, delegate { foreach (var d in Order) FillDay(d, true); });
            menu.Items.Add("시간대 모두 지우기", null, delegate { foreach (var d in Order) FillDay(d, false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("월요일 설정을 평일에 복사", null, delegate { CopyDay(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday); });
            menu.Items.Add("토요일 설정을 일요일에 복사", null, delegate { CopyDay(DayOfWeek.Saturday, DayOfWeek.Sunday); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("처음 상태로 되돌리기", null, delegate { CopyFrom(original); });
            return menu;
        }

        void FillDay(DayOfWeek day, bool value)
        {
            for (int s = 0; s < Schedule.SlotsPerDay; s++) schedule.Allowed[(int)day][s] = value;
            OnChanged();
        }

        void CopyDay(DayOfWeek from, params DayOfWeek[] targets)
        {
            foreach (var t in targets)
            {
                if (t == from) continue;
                schedule.LimitMinutes[(int)t] = schedule.LimitMinutes[(int)from];
                Array.Copy(schedule.Allowed[(int)from], schedule.Allowed[(int)t], Schedule.SlotsPerDay);
            }
            OnChanged();
        }

        void CopyFrom(Schedule source)
        {
            for (int d = 0; d < 7; d++)
            {
                schedule.LimitMinutes[d] = source.LimitMinutes[d];
                Array.Copy(source.Allowed[d], schedule.Allowed[d], Schedule.SlotsPerDay);
            }
            OnChanged();
        }

        // 실제 시각(시) 범위를 칠한다. endHour 가 24면 자정까지.
        void SetRange(DayOfWeek day, int startHour, int endHour)
        {
            var slots = schedule.Allowed[(int)day];
            for (int s = 0; s < Schedule.SlotsPerDay; s++) slots[s] = false;
            for (int h = startHour; h < endHour; h++)
                for (int half = 0; half < 2; half++)
                    slots[(((h - resetHour) % 24 + 24) % 24) * 2 + half] = true;
        }

        void ApplyExample()
        {
            foreach (var d in Order)
            {
                bool weekend = d == DayOfWeek.Saturday || d == DayOfWeek.Sunday;
                schedule.LimitMinutes[(int)d] = weekend ? 240 : 120;
                if (weekend) SetRange(d, 10, 24); else SetRange(d, 18, 23);
            }
            OnChanged();
        }
    }

    class DayMenuEventArgs : EventArgs
    {
        public int Column;
        public Point Location;
    }

    // 요일 이름·날짜, 하루 총량 조절기, 칠한 시간대 합계를 보여 주는 머리글.
    class DayHeader : Control
    {
        readonly Schedule schedule;
        readonly int resetHour;
        readonly DurationStepper[] steppers = new DurationStepper[7];
        int hoverCol = -1;

        public event EventHandler Changed;
        public event EventHandler<DayMenuEventArgs> DayMenuRequested;

        public DayHeader(Schedule schedule, int resetHour)
        {
            this.schedule = schedule;
            this.resetHour = resetHour;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(ScheduleForm.Gutter + ScheduleForm.ColWidth * 7, Ui.S(118));
            BackColor = Ui.Surface;

            for (int i = 0; i < 7; i++)
            {
                int d = (int)ScheduleForm.Order[i];
                var stepper = new DurationStepper(schedule.LimitMinutes[d])
                {
                    Location = new Point(ScheduleForm.Gutter + i * ScheduleForm.ColWidth + Ui.S(6), Ui.S(62)),
                    Size = new Size(ScheduleForm.ColWidth - Ui.S(12), Ui.S(30))
                };
                stepper.ValueChanged += delegate
                {
                    schedule.LimitMinutes[d] = stepper.Value;
                    if (Changed != null) Changed(this, EventArgs.Empty);
                };
                steppers[i] = stepper;
                Controls.Add(stepper);
            }
        }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            // 복사·되돌리기 등으로 값이 바뀌었을 수 있으니 조절기를 다시 맞춘다.
            for (int i = 0; i < 7; i++)
                steppers[i].SetValueSilently(schedule.LimitMinutes[(int)ScheduleForm.Order[i]]);
            base.OnInvalidated(e);
        }

        int ColumnAt(Point p)
        {
            if (p.X < ScheduleForm.Gutter || p.Y > Ui.S(58)) return -1;
            int c = (p.X - ScheduleForm.Gutter) / ScheduleForm.ColWidth;
            return c >= 0 && c < 7 ? c : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int c = ColumnAt(e.Location);
            if (c != hoverCol) { hoverCol = c; Cursor = c >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverCol = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int c = ColumnAt(e.Location);
            if (c >= 0 && DayMenuRequested != null)
                DayMenuRequested(this, new DayMenuEventArgs { Column = c, Location = new Point(ScheduleForm.Gutter + c * ScheduleForm.ColWidth, Ui.S(58)) });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var logical = DateTime.Now.AddHours(-resetHour);
            int todayIndex = Array.IndexOf(ScheduleForm.Order, logical.DayOfWeek);
            DateTime monday = logical.Date.AddDays(-todayIndex);
            int colW = ScheduleForm.ColWidth;

            using (var small = Ui.Font(9f))
            using (var tiny = Ui.Font(8.5f))
            using (var date = Ui.Font(15f))
            using (var dateBold = Ui.Font(15f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "총량", small, new Rectangle(0, Ui.S(62), ScheduleForm.Gutter - Ui.S(8), Ui.S(30)), Ui.SubText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "시간대", small, new Rectangle(0, Ui.S(96), ScheduleForm.Gutter - Ui.S(8), Ui.S(18)), Ui.SubText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

                for (int i = 0; i < 7; i++)
                {
                    int d = (int)ScheduleForm.Order[i];
                    int x = ScheduleForm.Gutter + i * colW;
                    bool isToday = i == todayIndex;

                    if (i == hoverCol)
                        using (var b = new SolidBrush(Ui.SurfaceAlt))
                        using (var path = Ui.Round(new Rectangle(x + Ui.S(4), 0, colW - Ui.S(8), Ui.S(58)), Ui.S(8)))
                            g.FillPath(b, path);

                    Color nameColor = isToday ? Ui.Blue : d == 0 ? Ui.Red : Ui.SubText;
                    TextRenderer.DrawText(g, ScheduleForm.DayNames[d] + (i == hoverCol ? " ▾" : ""), small, new Rectangle(x, Ui.S(4), colW, Ui.S(18)), nameColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    var dayDate = monday.AddDays(i);
                    var circle = new Rectangle(x + (colW - Ui.S(34)) / 2, Ui.S(22), Ui.S(34), Ui.S(34));
                    if (isToday)
                        using (var b = new SolidBrush(Ui.Blue)) g.FillEllipse(b, circle);
                    TextRenderer.DrawText(g, dayDate.Day.ToString(), isToday ? dateBold : date, circle,
                        isToday ? Color.White : Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    int allowed = schedule.AllowedMinutes(d);
                    bool tooShort = allowed < schedule.LimitMinutes[d];
                    string text = allowed >= 24 * 60 ? "제한 없음" : allowed == 0 ? "없음" : Ui.Duration(allowed);
                    if (tooShort) text += " ⚠";
                    TextRenderer.DrawText(g, text, tiny, new Rectangle(x, Ui.S(96), colW, Ui.S(18)), tooShort ? Ui.Amber : Ui.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }

    // − 2시간 + 형태의 하루 총량 조절기. 휠, 방향키로도 바꿀 수 있다.
    class DurationStepper : Control
    {
        readonly int Step;
        readonly int max;
        readonly Func<int, string> format;
        int value;
        int hoverZone; // -1 빼기, 1 더하기, 0 없음

        public event EventHandler ValueChanged;

        public DurationStepper(int minutes) : this(minutes, 30, 24 * 60, null) { }

        public DurationStepper(int minutes, int step, int maxMinutes, Func<int, string> format)
        {
            Step = step;
            max = maxMinutes;
            this.format = format;
            value = Clamp(minutes);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = Ui.Font(9.5f, FontStyle.Bold);
        }

        public int Value { get { return value; } }

        int Clamp(int v) { return Math.Max(0, Math.Min(max, v)); }

        public void SetValueSilently(int v)
        {
            v = Clamp(v);
            if (v != value) { value = v; Invalidate(); }
        }

        void Change(int delta)
        {
            int v = Clamp(value + delta);
            if (v == value) return;
            value = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        int Zone(Point p)
        {
            int w = Height;
            return p.X < w ? -1 : p.X > Width - w ? 1 : 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = Zone(e.Location);
            if (z != hoverZone) { hoverZone = z; Cursor = z != 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverZone = 0; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) Change(Zone(e.Location) * Step); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Change(e.Delta > 0 ? Step : -Step); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) Change(Step);
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) Change(-Step);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int w = Height;
            using (var path = Ui.Round(rect, Ui.S(8)))
            {
                using (var b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, path);
                if (hoverZone != 0)
                {
                    var zone = hoverZone < 0 ? new Rectangle(0, 0, w, Height) : new Rectangle(Width - w, 0, w, Height);
                    var clip = g.Clip;
                    g.SetClip(zone);
                    using (var b = new SolidBrush(Ui.Line)) g.FillPath(b, path);
                    g.Clip = clip;
                }
                if (Focused)
                    using (var pen = new Pen(Ui.BlueLight, 2)) g.DrawPath(pen, path);
            }
            using (var sym = Ui.Font(12f))
            {
                TextRenderer.DrawText(g, "−", sym, new Rectangle(0, 0, w, Height), value > 0 ? Ui.Text : Ui.Line,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "+", sym, new Rectangle(Width - w, 0, w, Height), value < max ? Ui.Text : Ui.Line,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            string text = format != null ? format(value)
                : value % 60 == 0 ? (value / 60) + "시간" : (value / 60.0).ToString("0.0") + "시간";
            TextRenderer.DrawText(g, text, Font, new Rectangle(w - Ui.S(4), 0, Width - 2 * w + Ui.S(8), Height),
                value == 0 ? (format == null ? Ui.Red : Ui.SubText) : Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class ScheduleGrid : Control
    {
        static int RowHeight { get { return Ui.S(20); } }
        static int ColW { get { return ScheduleForm.ColWidth; } }
        static int Gutter { get { return ScheduleForm.Gutter; } }

        readonly Schedule schedule;
        readonly int resetHour;
        readonly Timer clock = new Timer();

        bool dragging;
        bool paintValue;
        int anchorCol, anchorRow, curCol, curRow;
        int hoverCol = -1, hoverRow = -1;
        bool[][] snapshot;

        public event EventHandler Changed;
        public event EventHandler StatusChanged;
        public string StatusText { get; private set; }
        public bool Dragging { get { return dragging; } }

        public ScheduleGrid(Schedule schedule, int resetHour)
        {
            this.schedule = schedule;
            this.resetHour = resetHour;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Gutter + ColW * 7, RowHeight * Schedule.SlotsPerDay + 1);
            Cursor = Cursors.Cross;

            clock.Interval = 30000;
            clock.Tick += delegate { Invalidate(); };
            clock.Start();
        }

        public int CurrentTimeY()
        {
            var logical = DateTime.Now.AddHours(-resetHour);
            return (int)(logical.TimeOfDay.TotalMinutes / Schedule.SlotMinutes * RowHeight);
        }

        // 칠해 둔 칸 중 가장 이른 시작 시각의 y. 없으면 -1.
        public int FirstBlockY()
        {
            for (int r = 1; r < Schedule.SlotsPerDay; r++)
                for (int c = 0; c < 7; c++)
                    if (Day(c)[r] && !Day(c)[r - 1]) return r * RowHeight;
            return -1;
        }

        bool[] Day(int col)
        {
            return schedule.Allowed[(int)ScheduleForm.Order[col]];
        }

        bool HitCell(Point p, out int col, out int row)
        {
            col = (p.X - Gutter) / ColW;
            row = p.Y / RowHeight;
            return p.X >= Gutter && col >= 0 && col < 7 && row >= 0 && row < Schedule.SlotsPerDay;
        }

        string SlotTime(int row)
        {
            int minutes = resetHour * 60 + row * Schedule.SlotMinutes;
            return string.Format("{0:00}:{1:00}", (minutes / 60) % 24, minutes % 60);
        }

        string DayRange(int c1, int c2)
        {
            string a = ScheduleForm.DayNames[(int)ScheduleForm.Order[Math.Min(c1, c2)]];
            string b = ScheduleForm.DayNames[(int)ScheduleForm.Order[Math.Max(c1, c2)]];
            return a == b ? a + "요일" : a + "~" + b;
        }

        void SetStatus(string text)
        {
            if (text == StatusText) return;
            StatusText = text;
            if (StatusChanged != null) StatusChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int col, row;
            if (e.Button != MouseButtons.Left || !HitCell(e.Location, out col, out row)) return;
            snapshot = new bool[7][];
            for (int d = 0; d < 7; d++) snapshot[d] = (bool[])schedule.Allowed[d].Clone();
            anchorCol = curCol = col;
            anchorRow = curRow = row;
            paintValue = !Day(col)[row];
            dragging = true;
            Capture = true;
            ApplyDrag();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
            {
                int col, row;
                if (!HitCell(e.Location, out col, out row)) { col = -1; row = -1; }
                if (col != hoverCol || row != hoverRow)
                {
                    hoverCol = col; hoverRow = row;
                    Invalidate();
                    SetStatus(col < 0 ? null : string.Format("{0} {1} – {2}  ·  클릭하거나 끌어서 {3}",
                        DayRange(col, col), SlotTime(row), SlotTime(row + 1), Day(col)[row] ? "지우기" : "칠하기"));
                }
                return;
            }
            int c = Math.Max(0, Math.Min(6, (e.X - Gutter) / ColW));
            int r = Math.Max(0, Math.Min(Schedule.SlotsPerDay - 1, e.Y / RowHeight));
            if (c != curCol || r != curRow) { curCol = c; curRow = r; ApplyDrag(); }

            // 위아래 끝으로 끌면 자동 스크롤
            var parent = Parent as ScrollableControl;
            if (parent != null)
            {
                var pt = parent.PointToClient(PointToScreen(e.Location));
                int scrollY = -parent.AutoScrollPosition.Y;
                if (pt.Y < Ui.S(12)) parent.AutoScrollPosition = new Point(0, Math.Max(0, scrollY - RowHeight));
                else if (pt.Y > parent.ClientSize.Height - Ui.S(12)) parent.AutoScrollPosition = new Point(0, scrollY + RowHeight);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            snapshot = null;
            SetStatus(null);
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging) return;
            hoverCol = hoverRow = -1;
            SetStatus(null);
            Invalidate();
        }

        void ApplyDrag()
        {
            for (int d = 0; d < 7; d++) Array.Copy(snapshot[d], schedule.Allowed[d], Schedule.SlotsPerDay);
            int r1 = Math.Min(anchorRow, curRow), r2 = Math.Max(anchorRow, curRow);
            for (int c = Math.Min(anchorCol, curCol); c <= Math.Max(anchorCol, curCol); c++)
                for (int r = r1; r <= r2; r++)
                    Day(c)[r] = paintValue;
            SetStatus(string.Format("{0} {1} – {2}  {3} 중", DayRange(anchorCol, curCol), SlotTime(r1), SlotTime(r2 + 1), paintValue ? "칠하는" : "지우는"));
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Surface);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int width = Gutter + ColW * 7;
            int midnightRow = ((24 - resetHour) % 24) * 60 / Schedule.SlotMinutes;
            var logical = DateTime.Now.AddHours(-resetHour);
            int todayCol = Array.IndexOf(ScheduleForm.Order, logical.DayOfWeek);

            using (var todayBg = new SolidBrush(Color.FromArgb(248, 250, 255)))
                g.FillRectangle(todayBg, Gutter + todayCol * ColW, 0, ColW, Height);
            if (!dragging && hoverCol >= 0)
                using (var hover = new SolidBrush(Ui.SurfaceAlt))
                    g.FillRectangle(hover, Gutter + hoverCol * ColW + 1, hoverRow * RowHeight + 1, ColW - 1, RowHeight - 1);

            using (var hourPen = new Pen(Ui.Line))
            using (var halfPen = new Pen(Color.FromArgb(241, 243, 244)))
            using (var midnightPen = new Pen(Color.FromArgb(150, 150, 150), Math.Max(1, Ui.S(2))))
            using (var label = Ui.Font(8.5f))
            using (var small = Ui.Font(7.5f))
            {
                for (int r = 0; r <= Schedule.SlotsPerDay; r++)
                {
                    int y = r * RowHeight;
                    bool hour = r % 2 == 0;
                    g.DrawLine(hour ? hourPen : halfPen, Gutter, y, width, y);
                    if (hour && r < Schedule.SlotsPerDay)
                    {
                        bool nextDay = midnightRow > 0 && r >= midnightRow;
                        TextRenderer.DrawText(g, SlotTime(r), label, new Rectangle(0, y - Ui.S(1), Gutter - Ui.S(8), Ui.S(16)),
                            nextDay ? Color.FromArgb(170, 170, 170) : Ui.SubText, TextFormatFlags.Right | TextFormatFlags.Top);
                    }
                }
                if (midnightRow > 0)
                {
                    int y = midnightRow * RowHeight;
                    g.DrawLine(midnightPen, Gutter, y, width, y);
                    TextRenderer.DrawText(g, "다음날", small, new Rectangle(0, y + Ui.S(14), Gutter - Ui.S(8), Ui.S(14)),
                        Color.FromArgb(170, 170, 170), TextFormatFlags.Right | TextFormatFlags.Top);
                }
                for (int c = 0; c <= 7; c++)
                    g.DrawLine(hourPen, Gutter + c * ColW, 0, Gutter + c * ColW, Height);
            }

            // 칠한 시간대를 캘린더 일정처럼 둥근 블록으로 그린다.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var block = new SolidBrush(Ui.BlueLight))
            using (var bold = Ui.Font(8.5f, FontStyle.Bold))
            using (var thin = Ui.Font(8f))
            {
                for (int c = 0; c < 7; c++)
                {
                    var day = Day(c);
                    int r = 0;
                    while (r < Schedule.SlotsPerDay)
                    {
                        if (!day[r]) { r++; continue; }
                        int start = r;
                        while (r < Schedule.SlotsPerDay && day[r]) r++;
                        var rect = new Rectangle(Gutter + c * ColW + Ui.S(3), start * RowHeight + 1, ColW - Ui.S(7), (r - start) * RowHeight - 2);
                        using (var path = Ui.Round(rect, Ui.S(6)))
                            g.FillPath(block, path);
                        var textRect = new Rectangle(rect.X + Ui.S(6), rect.Y + Ui.S(2), rect.Width - Ui.S(8), Ui.S(16));
                        TextRenderer.DrawText(g, SlotTime(start) + " – " + SlotTime(r), bold, textRect, Color.White,
                            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
                        if (r - start >= 3)
                        {
                            textRect.Offset(0, Ui.S(16));
                            TextRenderer.DrawText(g, Ui.Duration((r - start) * Schedule.SlotMinutes), thin, textRect,
                                Color.FromArgb(220, 232, 253), TextFormatFlags.Left | TextFormatFlags.Top);
                        }
                    }
                }
            }

            // 드래그 중인 범위 테두리
            if (dragging)
            {
                int c1 = Math.Min(anchorCol, curCol), c2 = Math.Max(anchorCol, curCol);
                int r1 = Math.Min(anchorRow, curRow), r2 = Math.Max(anchorRow, curRow);
                var sel = new Rectangle(Gutter + c1 * ColW + 1, r1 * RowHeight + 1, (c2 - c1 + 1) * ColW - 2, (r2 - r1 + 1) * RowHeight - 2);
                using (var pen = new Pen(paintValue ? Ui.Blue : Ui.Red, Math.Max(1, Ui.S(2))) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, sel);
            }

            // 지금 시각 빨간 선
            {
                int y = CurrentTimeY();
                int x = Gutter + todayCol * ColW;
                using (var red = new Pen(Ui.Red, Math.Max(1, Ui.S(2))))
                using (var redBrush = new SolidBrush(Ui.Red))
                {
                    g.DrawLine(red, x, y, x + ColW, y);
                    g.FillEllipse(redBrush, x - Ui.S(5), y - Ui.S(5), Ui.S(10), Ui.S(10));
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) clock.Dispose();
            base.Dispose(disposing);
        }
    }
}
