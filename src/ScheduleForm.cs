using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        public const int Gutter = 64;
        public const int ColWidth = 96;

        readonly Schedule schedule;
        readonly ScheduleGrid grid;
        readonly NumericUpDown[] limitInputs = new NumericUpDown[7];
        readonly Label[] allowedLabels = new Label[7];
        bool updatingInputs;

        public Schedule Result { get { return schedule; } }

        public ScheduleForm(Schedule current, int resetHour)
        {
            schedule = current.Clone();

            Text = "PlayTimer - 시간 설정";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 9f);
            BackColor = Color.White;
            int gridWidth = Gutter + ColWidth * 7;
            ClientSize = new Size(gridWidth + SystemInformation.VerticalScrollBarWidth + 24, 680);

            var help = new Label
            {
                Text = "위: 요일별 하루 최대 사용 시간    아래: 써도 되는 시간대 (드래그해서 칠하기 / 칠한 곳에서 시작하면 지우기)",
                ForeColor = Color.DimGray,
                Location = new Point(12, 10),
                AutoSize = true
            };
            Controls.Add(help);

            var header = new Panel { Location = new Point(12, 34), Size = new Size(gridWidth, 86) };
            var today = DateTime.Now.AddHours(-resetHour).DayOfWeek;
            header.Controls.Add(new Label { Text = "하루 총량\n(시간)", Location = new Point(0, 30), Size = new Size(Gutter - 4, 34), ForeColor = Color.DimGray });
            for (int i = 0; i < 7; i++)
            {
                int d = (int)Order[i];
                int x = Gutter + i * ColWidth;
                bool isToday = Order[i] == today;
                header.Controls.Add(new Label
                {
                    Text = DayNames[d] + (isToday ? " (오늘)" : ""),
                    Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold),
                    ForeColor = isToday ? Color.FromArgb(26, 115, 232) : d == 0 ? Color.FromArgb(217, 48, 37) : d == 6 ? Color.FromArgb(26, 115, 232) : Color.Black,
                    Location = new Point(x, 0),
                    Size = new Size(ColWidth, 24),
                    TextAlign = ContentAlignment.MiddleCenter
                });
                var input = new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = 24,
                    DecimalPlaces = 1,
                    Increment = 0.5m,
                    Location = new Point(x + 14, 30),
                    Width = ColWidth - 28,
                    TextAlign = HorizontalAlignment.Center,
                    Tag = d
                };
                input.ValueChanged += OnLimitChanged;
                limitInputs[d] = input;
                header.Controls.Add(input);
                var allowed = new Label
                {
                    Location = new Point(x, 60),
                    Size = new Size(ColWidth, 20),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("맑은 고딕", 8f)
                };
                allowedLabels[d] = allowed;
                header.Controls.Add(allowed);
            }
            Controls.Add(header);

            var scroller = new Panel
            {
                Location = new Point(12, 124),
                Size = new Size(gridWidth + SystemInformation.VerticalScrollBarWidth, 480),
                AutoScroll = true,
                BorderStyle = BorderStyle.None
            };
            grid = new ScheduleGrid(schedule, resetHour) { Location = new Point(0, 0) };
            grid.Changed += delegate { RefreshInputs(); };
            scroller.Controls.Add(grid);
            Controls.Add(scroller);

            var buttons = new FlowLayoutPanel
            {
                Location = new Point(12, 616),
                Size = new Size(ClientSize.Width - 24, 50),
                FlowDirection = FlowDirection.LeftToRight
            };
            buttons.Controls.Add(MakeButton("월 → 평일 복사", delegate { CopyDay(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday); }));
            buttons.Controls.Add(MakeButton("토 → 일 복사", delegate { CopyDay(DayOfWeek.Saturday, DayOfWeek.Sunday); }));
            buttons.Controls.Add(MakeButton("모두 칠하기", delegate { FillAll(true); }));
            buttons.Controls.Add(MakeButton("모두 지우기", delegate { FillAll(false); }));
            var save = MakeButton("저장", delegate { DialogResult = DialogResult.OK; Close(); });
            save.BackColor = Color.FromArgb(26, 115, 232);
            save.ForeColor = Color.White;
            save.Margin = new Padding(40, 3, 3, 3);
            buttons.Controls.Add(save);
            buttons.Controls.Add(MakeButton("취소", delegate { DialogResult = DialogResult.Cancel; Close(); }));
            Controls.Add(buttons);
            AcceptButton = null;

            RefreshInputs();

            // 처음 열면 지금 시각 근처가 보이도록 스크롤한다.
            Shown += delegate
            {
                int y = grid.CurrentTimeY() - 120;
                scroller.AutoScrollPosition = new Point(0, Math.Max(0, y));
            };
        }

        static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(6, 0, 6, 0)
            };
            b.Click += onClick;
            return b;
        }

        void OnLimitChanged(object sender, EventArgs e)
        {
            if (updatingInputs) return;
            var input = (NumericUpDown)sender;
            schedule.LimitMinutes[(int)input.Tag] = (int)(input.Value * 60);
            RefreshInputs();
        }

        void CopyDay(DayOfWeek from, params DayOfWeek[] targets)
        {
            foreach (var t in targets)
            {
                schedule.LimitMinutes[(int)t] = schedule.LimitMinutes[(int)from];
                Array.Copy(schedule.Allowed[(int)from], schedule.Allowed[(int)t], Schedule.SlotsPerDay);
            }
            grid.Invalidate();
            RefreshInputs();
        }

        void FillAll(bool value)
        {
            for (int d = 0; d < 7; d++)
                for (int s = 0; s < Schedule.SlotsPerDay; s++)
                    schedule.Allowed[d][s] = value;
            grid.Invalidate();
            RefreshInputs();
        }

        void RefreshInputs()
        {
            updatingInputs = true;
            for (int d = 0; d < 7; d++)
            {
                limitInputs[d].Value = Math.Min(24m, schedule.LimitMinutes[d] / 60m);
                int allowed = schedule.AllowedMinutes(d);
                allowedLabels[d].Text = "시간대 " + FormatHours(allowed);
                allowedLabels[d].ForeColor = allowed < schedule.LimitMinutes[d] ? Color.FromArgb(230, 120, 0) : Color.DimGray;
            }
            updatingInputs = false;
        }

        static string FormatHours(int minutes)
        {
            if (minutes % 60 == 0) return (minutes / 60) + "h";
            return string.Format("{0}h {1}m", minutes / 60, minutes % 60);
        }
    }

    class ScheduleGrid : Control
    {
        const int RowHeight = 20;

        static readonly Color BlockColor = Color.FromArgb(66, 133, 244);

        readonly Schedule schedule;
        readonly int resetHour;
        readonly Timer clock = new Timer();

        bool dragging;
        bool paintValue;
        int anchorCol, anchorRow;
        bool[][] snapshot;

        public event EventHandler Changed;

        public ScheduleGrid(Schedule schedule, int resetHour)
        {
            this.schedule = schedule;
            this.resetHour = resetHour;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Size = new Size(ScheduleForm.Gutter + ScheduleForm.ColWidth * 7, RowHeight * Schedule.SlotsPerDay + 1);
            Font = new Font("맑은 고딕", 8.5f);
            Cursor = Cursors.Hand;

            clock.Interval = 30000;
            clock.Tick += delegate { Invalidate(); };
            clock.Start();
        }

        public int CurrentTimeY()
        {
            var logical = DateTime.Now.AddHours(-resetHour);
            return (int)(logical.TimeOfDay.TotalMinutes / Schedule.SlotMinutes * RowHeight);
        }

        bool HitCell(Point p, out int col, out int row)
        {
            col = (p.X - ScheduleForm.Gutter) / ColW;
            row = p.Y / RowHeight;
            return p.X >= ScheduleForm.Gutter && col >= 0 && col < 7 && row >= 0 && row < Schedule.SlotsPerDay;
        }

        static int ColW { get { return ScheduleForm.ColWidth; } }

        bool[] Day(int col)
        {
            return schedule.Allowed[(int)ScheduleForm.Order[col]];
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int col, row;
            if (e.Button != MouseButtons.Left || !HitCell(e.Location, out col, out row)) return;
            snapshot = new bool[7][];
            for (int d = 0; d < 7; d++) snapshot[d] = (bool[])schedule.Allowed[d].Clone();
            anchorCol = col;
            anchorRow = row;
            paintValue = !Day(col)[row];
            dragging = true;
            Capture = true;
            ApplyDrag(col, row);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            int col = Math.Max(0, Math.Min(6, (e.X - ScheduleForm.Gutter) / ColW));
            int row = Math.Max(0, Math.Min(Schedule.SlotsPerDay - 1, e.Y / RowHeight));
            ApplyDrag(col, row);

            // 위아래 끝으로 끌면 자동 스크롤
            var parent = Parent as ScrollableControl;
            if (parent != null)
            {
                var pt = parent.PointToClient(PointToScreen(e.Location));
                int scrollY = -parent.AutoScrollPosition.Y;
                if (pt.Y < 10) parent.AutoScrollPosition = new Point(0, Math.Max(0, scrollY - RowHeight));
                else if (pt.Y > parent.ClientSize.Height - 10) parent.AutoScrollPosition = new Point(0, scrollY + RowHeight);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            snapshot = null;
            Invalidate();
        }

        void ApplyDrag(int col, int row)
        {
            for (int d = 0; d < 7; d++) Array.Copy(snapshot[d], schedule.Allowed[d], Schedule.SlotsPerDay);
            for (int c = Math.Min(anchorCol, col); c <= Math.Max(anchorCol, col); c++)
                for (int r = Math.Min(anchorRow, row); r <= Math.Max(anchorRow, row); r++)
                    Day(c)[r] = paintValue;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        string SlotTime(int row)
        {
            int minutes = resetHour * 60 + row * Schedule.SlotMinutes;
            return string.Format("{0:00}:{1:00}", (minutes / 60) % 24, minutes % 60);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            int gutter = ScheduleForm.Gutter;
            int width = gutter + ColW * 7;
            int midnightRow = ((24 - resetHour) % 24) * 60 / Schedule.SlotMinutes;

            using (var hourPen = new Pen(Color.FromArgb(218, 220, 224)))
            using (var halfPen = new Pen(Color.FromArgb(241, 243, 244)))
            using (var midnightPen = new Pen(Color.FromArgb(150, 150, 150), 2))
            using (var labelBrush = new SolidBrush(Color.FromArgb(112, 117, 122)))
            using (var nextDayBrush = new SolidBrush(Color.FromArgb(170, 170, 170)))
            {
                var todayCol = Array.IndexOf(ScheduleForm.Order, DateTime.Now.AddHours(-resetHour).DayOfWeek);
                using (var todayBg = new SolidBrush(Color.FromArgb(248, 250, 255)))
                    g.FillRectangle(todayBg, gutter + todayCol * ColW, 0, ColW, Height);

                for (int r = 0; r <= Schedule.SlotsPerDay; r++)
                {
                    int y = r * RowHeight;
                    bool hour = r % 2 == 0;
                    g.DrawLine(hour ? hourPen : halfPen, gutter, y, width, y);
                    if (hour && r < Schedule.SlotsPerDay)
                    {
                        bool nextDay = midnightRow > 0 && r >= midnightRow;
                        g.DrawString(SlotTime(r), Font, nextDay ? nextDayBrush : labelBrush, 8, y + 1);
                    }
                }
                if (midnightRow > 0)
                {
                    int y = midnightRow * RowHeight;
                    g.DrawLine(midnightPen, gutter, y, width, y);
                    using (var small = new Font("맑은 고딕", 7f))
                        g.DrawString("다음날", small, nextDayBrush, 8, y + 12);
                }
                for (int c = 0; c <= 7; c++)
                    g.DrawLine(hourPen, gutter + c * ColW, 0, gutter + c * ColW, Height);
            }

            // 칠한 시간대를 캘린더 일정처럼 둥근 블록으로 그린다.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var block = new SolidBrush(BlockColor))
            using (var text = new Font("맑은 고딕", 8.5f, FontStyle.Bold))
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
                        var rect = new Rectangle(gutter + c * ColW + 3, start * RowHeight + 1, ColW - 7, (r - start) * RowHeight - 2);
                        using (var path = RoundRect(rect, 5))
                            g.FillPath(block, path);
                        string label = SlotTime(start) + " ~ " + SlotTime(r);
                        g.DrawString(label, text, Brushes.White, rect.X + 4, rect.Y + 2);
                    }
                }
            }

            // 지금 시각 빨간 선
            {
                var logical = DateTime.Now.AddHours(-resetHour);
                int col = Array.IndexOf(ScheduleForm.Order, logical.DayOfWeek);
                int y = CurrentTimeY();
                int x = gutter + col * ColW;
                using (var red = new Pen(Color.FromArgb(234, 67, 53), 2))
                using (var redBrush = new SolidBrush(Color.FromArgb(234, 67, 53)))
                {
                    g.DrawLine(red, x, y, x + ColW, y);
                    g.FillEllipse(redBrush, x - 5, y - 5, 10, 10);
                }
            }
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (r.Height < d) d = Math.Max(2, r.Height);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) clock.Dispose();
            base.Dispose(disposing);
        }
    }
}
