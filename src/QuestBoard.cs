using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PlayTimer
{
    // "오늘의 퀘스트" 창. PC를 켜면 먼저 떠서, 원래 하려던 일을 고르게 한다.
    class QuestBoard : Form
    {
        readonly TrayApp app;
        readonly Label subtitle, levelLabel, xpLabel, gateHint;
        readonly Panel xpBar, intro;
        readonly Panel listHost;
        readonly FlowLayoutPanel list;
        readonly CheckBox lockBox;
        readonly UiButton closeButton;

        public QuestBoard(TrayApp app, bool atStart)
        {
            this.app = app;

            Text = "PlayTimer 퀘스트";
            if (Ui.AppIcon != null) Icon = Ui.AppIcon; else ShowIcon = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(9.5f);
            BackColor = Ui.Surface;
            KeyPreview = true;

            int pad = Ui.S(24);
            int width = Ui.S(600);

            var title = Ui.Label("오늘의 퀘스트", Ui.Font(16f, FontStyle.Bold), Ui.Text);
            title.Location = new Point(pad, Ui.S(18));
            subtitle = Ui.Label("", Ui.Font(9.5f), Ui.SubText);
            subtitle.Location = new Point(pad, Ui.S(54));
            Controls.Add(title);
            Controls.Add(subtitle);

            levelLabel = Ui.Label("", Ui.Font(12f, FontStyle.Bold), Ui.Blue);
            xpLabel = Ui.Label("", Ui.Font(8.5f), Ui.SubText);
            xpBar = new Panel { Size = Ui.S(140, 8), BackColor = Ui.Surface };
            xpBar.Paint += PaintXp;
            Controls.Add(levelLabel);
            Controls.Add(xpLabel);
            Controls.Add(xpBar);
            xpBar.Location = new Point(width - pad - xpBar.Width, Ui.S(48));

            int y = Ui.S(86);
            intro = new Panel { Location = new Point(pad, y), Size = new Size(width - pad * 2, Ui.S(58)), BackColor = Color.FromArgb(232, 240, 254) };
            var introText = Ui.Label("컴퓨터를 켠 이유가 뭐예요? 할 퀘스트를 골라 ▶ 시작을 누르면,\n그 퀘스트에 정한 앱만 쓸 수 있는 집중 모드가 돼요.",
                Ui.Font(9.5f), Color.FromArgb(23, 78, 166));
            introText.Location = Ui.P(14, 10);
            intro.Controls.Add(introText);
            Controls.Add(intro);
            if (atStart) y += intro.Height + Ui.S(12); else intro.Visible = false;

            listHost = new Panel
            {
                Location = new Point(pad, y),
                Size = new Size(width - pad * 2, Ui.S(atStart ? 330 : 400)),
                AutoScroll = true,
                BackColor = Ui.Surface
            };
            list = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Ui.Surface,
                Location = Point.Empty
            };
            listHost.Controls.Add(list);
            Controls.Add(listHost);

            var add = new UiButton("+  퀘스트 추가", ButtonKind.Secondary, false);
            add.Location = new Point(pad, listHost.Bottom + Ui.S(10));
            add.Click += delegate { Edit(null); };
            Controls.Add(add);

            var divider = new Panel { BackColor = Ui.Line, Location = new Point(0, add.Bottom + Ui.S(16)), Size = new Size(width, 1) };
            Controls.Add(divider);

            lockBox = new CheckBox
            {
                Text = "일일 퀘스트를 끝내야 자유 시간이 열려요",
                AutoSize = true,
                Location = new Point(pad, divider.Bottom + Ui.S(14)),
                ForeColor = Ui.Text,
                Checked = app.Quests.LockFreeTime
            };
            lockBox.CheckedChanged += delegate
            {
                app.Quests.LockFreeTime = lockBox.Checked;
                app.SaveQuests();
                app.NotifyQuestsChanged();
            };
            Controls.Add(lockBox);
            gateHint = Ui.Label("", Ui.Font(8.5f), Ui.SubText);
            gateHint.Location = new Point(pad + Ui.S(18), lockBox.Bottom + Ui.S(2));
            Controls.Add(gateHint);

            closeButton = new UiButton("자유 시간", ButtonKind.Primary, false);
            closeButton.Click += delegate { Close(); };
            closeButton.Location = new Point(width - pad - closeButton.Width, divider.Bottom + Ui.S(14));
            Controls.Add(closeButton);

            ClientSize = new Size(width, gateHint.Bottom + Ui.S(18));

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.N) { Edit(null); e.Handled = true; }
            };

            app.QuestsChanged += OnQuestsChanged;
            FormClosed += delegate { app.QuestsChanged -= OnQuestsChanged; };
            Rebuild();
        }

        void OnQuestsChanged(object sender, EventArgs e)
        {
            // 행 안의 버튼을 누른 처리 도중일 수 있으니, 그 처리가 끝난 뒤에 목록을 다시 만든다.
            if (IsDisposed) return;
            if (IsHandleCreated) BeginInvoke((MethodInvoker)Rebuild); else Rebuild();
        }

        void PaintXp(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, xpBar.Width - 1, xpBar.Height - 1);
            using (var path = Ui.Round(r, r.Height / 2))
            using (var b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, path);
            int w = (int)(r.Width * (app.Quests.Xp % QuestStore.XpPerLevel) / (double)QuestStore.XpPerLevel);
            if (w > 0)
                using (var path = Ui.Round(new Rectangle(0, 0, Math.Max(w, r.Height), r.Height), r.Height / 2))
                using (var b = new SolidBrush(Ui.Blue)) g.FillPath(b, path);
        }

        public void Rebuild()
        {
            var store = app.Quests;
            string day = app.Today;
            var dow = app.TodayDow;

            int done = store.DailyDone(day, dow), total = store.DailyFor(dow).Count;
            int streak = store.CurrentStreak(day, app.Yesterday);
            var parts = new List<string> { app.DayLabel };
            if (total > 0) parts.Add(string.Format("일일 퀘스트 {0}/{1}", done, total));
            if (streak > 0) parts.Add("연속 " + streak + "일");
            subtitle.Text = string.Join("  ·  ", parts.ToArray());

            levelLabel.Text = "Lv " + store.Level;
            xpLabel.Text = string.Format("{0} / {1} XP", store.Xp % QuestStore.XpPerLevel, QuestStore.XpPerLevel);
            int right = ClientSize.Width - Ui.S(24);
            levelLabel.Location = new Point(right - levelLabel.PreferredWidth, Ui.S(18));
            xpBar.Location = new Point(right - xpBar.Width, Ui.S(48));
            xpLabel.Location = new Point(right - xpLabel.PreferredWidth, Ui.S(60));
            xpBar.Invalidate();

            bool gated = store.LockFreeTime && store.HasUnfinishedDaily(day, dow);
            gateHint.Text = !store.LockFreeTime ? "끄면 퀘스트와 상관없이 정해 둔 시간 안에서 자유롭게 쓸 수 있어요."
                : gated ? string.Format("남은 일일 퀘스트 {0}개를 끝내면 자유 시간이 열려요.", total - done)
                : "오늘 일일 퀘스트를 다 했어요. 자유 시간이 열렸어요.";
            gateHint.ForeColor = gated ? Ui.Amber : Ui.SubText;
            closeButton.Text = gated ? "닫기" : "자유 시간";
            closeButton.FitToText();
            closeButton.Left = ClientSize.Width - Ui.S(24) - closeButton.Width;

            list.SuspendLayout();
            foreach (Control c in list.Controls) c.Dispose();
            list.Controls.Clear();
            int rowWidth = listHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Ui.S(2);

            var daily = store.Quests.FindAll(q => q.Daily);
            daily.Sort((a, b) => Order(a, day, dow).CompareTo(Order(b, day, dow)));
            var todo = store.Quests.FindAll(q => !q.Daily && q.VisibleOn(day));
            todo.Sort((a, b) => a.IsDone(day).CompareTo(b.IsDone(day)));

            if (store.Quests.Count == 0)
            {
                var empty = Ui.Label("아직 퀘스트가 없어요.\n아래 '+ 퀘스트 추가'로 매일 하고 싶은 일이나 오늘 할 일을 적어 보세요.\n예: 영어 공부 30분, 운동 기록하기, 블로그 글 쓰기",
                    Ui.Font(10f), Ui.SubText);
                empty.Margin = Ui.Pad(4, 24, 0, 0);
                list.Controls.Add(empty);
            }
            if (daily.Count > 0)
            {
                list.Controls.Add(Section("일일 퀘스트"));
                foreach (var q in daily) list.Controls.Add(new QuestRow(app, q, rowWidth));
            }
            if (todo.Count > 0)
            {
                list.Controls.Add(Section("할 일"));
                foreach (var q in todo) list.Controls.Add(new QuestRow(app, q, rowWidth));
            }
            list.ResumeLayout();
        }

        // 오늘 할 것 → 오늘 끝낸 것 → 오늘은 쉬는 것
        static int Order(Quest q, string day, DayOfWeek dow)
        {
            if (!q.ScheduledOn(dow)) return 2;
            return q.IsDone(day) ? 1 : 0;
        }

        static Label Section(string text)
        {
            var l = Ui.Label(text, Ui.Font(9f, FontStyle.Bold), Ui.SubText);
            l.Margin = Ui.Pad(4, 14, 0, 6);
            return l;
        }

        public void Edit(Quest quest)
        {
            app.EditQuest(this, quest);
        }
    }

    // 퀘스트 한 줄: 완료 체크, 이름, 요약, 진행 막대, 시작/편집 버튼.
    class QuestRow : Control
    {
        readonly TrayApp app;
        readonly Quest quest;
        readonly UiButton startButton, editButton;
        bool hoverCheck;

        public QuestRow(TrayApp app, Quest quest, int width)
        {
            this.app = app;
            this.quest = quest;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(width, Ui.S(66));
            Margin = Padding.Empty;
            BackColor = Ui.Surface;

            bool focusing = app.Focus != null && app.Focus.Quest == quest;
            bool done = quest.IsDone(app.Today);
            startButton = new UiButton(focusing ? "그만" : "▶  시작", focusing ? ButtonKind.Ghost : ButtonKind.Primary, false);
            startButton.Height = Ui.S(32);
            startButton.FitToText();
            startButton.Height = Ui.S(32);
            startButton.Visible = !done;
            startButton.Click += delegate
            {
                if (app.Focus != null && app.Focus.Quest == quest) app.StopFocus(true);
                else app.StartFocus(quest);
            };
            editButton = new UiButton("편집", ButtonKind.Ghost, false);
            editButton.Size = Ui.S(56, 32);
            editButton.Click += delegate
            {
                var board = FindForm();
                app.EditQuest(board, quest);
            };
            Controls.Add(startButton);
            Controls.Add(editButton);
            editButton.Location = new Point(Width - editButton.Width - Ui.S(4), (Height - editButton.Height) / 2);
            startButton.Location = new Point(editButton.Left - startButton.Width - Ui.S(6), (Height - startButton.Height) / 2);
        }

        Rectangle CheckRect()
        {
            int s = Ui.S(24);
            return new Rectangle(Ui.S(8), (Height - s) / 2, s, s);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var r = CheckRect();
            r.Inflate(Ui.S(6), Ui.S(6));
            bool h = r.Contains(e.Location);
            if (h != hoverCheck) { hoverCheck = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverCheck = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var r = CheckRect();
            r.Inflate(Ui.S(6), Ui.S(6));
            if (r.Contains(e.Location))
            {
                if (quest.IsDone(app.Today)) app.UncompleteQuest(quest);
                else app.CompleteQuest(quest);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (!CheckRect().Contains(e.Location)) app.EditQuest(FindForm(), quest);
        }

        string Summary(bool scheduledToday)
        {
            string day = app.Today;
            var parts = new List<string>();
            parts.Add(quest.Daily ? quest.DaysText() : "할 일");
            if (!scheduledToday) parts.Add("오늘은 쉬는 날");
            if (quest.TargetMinutes > 0)
                parts.Add(string.Format("{0} / 목표 {1}", Ui.Duration((int)(quest.Progress(day) / 60)), Ui.Duration(quest.TargetMinutes)));
            else if (quest.Progress(day) >= 60)
                parts.Add(Ui.Duration((int)(quest.Progress(day) / 60)) + " 집중");
            parts.Add(quest.Apps.Count == 0 ? "앱 제한 없음" : "앱: " + string.Join(", ", quest.Apps.ToArray()));
            return string.Join("  ·  ", parts.ToArray());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            string day = app.Today;
            bool done = quest.IsDone(day);
            bool scheduled = quest.ScheduledOn(app.TodayDow);
            bool focusing = app.Focus != null && app.Focus.Quest == quest;

            if (focusing)
                using (var b = new SolidBrush(Color.FromArgb(230, 244, 234)))
                using (var path = Ui.Round(new Rectangle(0, Ui.S(2), Width - 1, Height - Ui.S(5)), Ui.S(8)))
                    g.FillPath(b, path);

            var check = CheckRect();
            if (done)
            {
                using (var b = new SolidBrush(Ui.Green)) g.FillEllipse(b, check);
                using (var pen = new Pen(Color.White, Math.Max(2, Ui.S(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] {
                        new Point(check.X + check.Width * 27 / 100, check.Y + check.Height * 52 / 100),
                        new Point(check.X + check.Width * 44 / 100, check.Y + check.Height * 68 / 100),
                        new Point(check.X + check.Width * 74 / 100, check.Y + check.Height * 35 / 100) });
            }
            else
                using (var pen = new Pen(hoverCheck ? Ui.Green : Color.FromArgb(189, 193, 198), Math.Max(2, Ui.S(2))))
                    g.DrawEllipse(pen, check);

            int x = check.Right + Ui.S(14);
            int textWidth = (startButton.Visible ? startButton.Left : editButton.Left) - x - Ui.S(8);
            Color titleColor = done || !scheduled ? Ui.SubText : Ui.Text;
            using (var titleFont = Ui.Font(11f, done ? FontStyle.Bold | FontStyle.Strikeout : FontStyle.Bold))
                TextRenderer.DrawText(g, quest.Title, titleFont, new Rectangle(x, Ui.S(10), textWidth, Ui.S(24)), titleColor,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (var small = Ui.Font(8.5f))
                TextRenderer.DrawText(g, focusing ? "집중 중  ·  " + Summary(scheduled) : Summary(scheduled), small,
                    new Rectangle(x, Ui.S(36), textWidth, Ui.S(18)), focusing ? Ui.Green : Ui.SubText,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (quest.TargetMinutes > 0 && !done)
            {
                double frac = Math.Min(1, quest.Progress(day) / (quest.TargetMinutes * 60.0));
                var bar = new Rectangle(x, Height - Ui.S(10), textWidth, Ui.S(4));
                using (var b = new SolidBrush(Ui.SurfaceAlt)) g.FillRectangle(b, bar);
                using (var b = new SolidBrush(Ui.Green)) g.FillRectangle(b, bar.X, bar.Y, (int)(bar.Width * frac), bar.Height);
            }

            using (var pen = new Pen(Ui.SurfaceAlt)) g.DrawLine(pen, x, Height - 1, Width, Height - 1);
        }
    }

    // 둥근 알약 모양 선택 버튼(요일, 반복 종류).
    class Chip : Control
    {
        bool selected, hover;

        public Chip(string text)
        {
            Text = text;
            Font = Ui.Font(9.5f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            var size = TextRenderer.MeasureText(text, Font);
            Size = new Size(Math.Max(Ui.S(40), size.Width + Ui.S(24)), Ui.S(32));
            Margin = Ui.Pad(0, 0, 6, 0);
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color back = selected ? Ui.Blue : hover ? Ui.Line : Ui.SurfaceAlt;
            using (var path = Ui.Round(r, r.Height / 2))
            using (var b = new SolidBrush(back)) g.FillPath(b, path);
            TextRenderer.DrawText(g, Text, Font, r, selected ? Color.White : Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // 퀘스트 추가/편집 창.
    class QuestEditor : CardForm
    {
        readonly Quest quest;
        readonly TextBox nameBox, appBox;
        readonly Label nameError;
        readonly Chip dailyChip, onceChip;
        readonly Chip[] dayChips = new Chip[7];
        readonly FlowLayoutPanel daysRow, appsRow;
        readonly DurationStepper target;
        public bool Deleted;

        public QuestEditor(Quest source, bool isNew) : base(Ui.Surface, true, false)
        {
            quest = source.Clone();
            KeyPreview = true;

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Ui.Pad(26, 22, 26, 20),
                BackColor = Color.Transparent
            };
            int inner = Ui.S(440);

            var title = Ui.Label(isNew ? "퀘스트 추가" : "퀘스트 편집", Ui.Font(14f, FontStyle.Bold), Ui.Text);
            title.Margin = Ui.Pad(0, 0, 0, 16);
            stack.Controls.Add(title);
            MakeDraggable(stack);
            MakeDraggable(title);

            stack.Controls.Add(Caption("이름"));
            nameBox = new TextBox { Text = quest.Title, Width = inner, Font = Ui.Font(11f), BorderStyle = BorderStyle.FixedSingle, Margin = Ui.Pad(0, 0, 0, 2) };
            stack.Controls.Add(nameBox);
            nameError = Ui.Label("", Ui.Font(8.5f), Ui.Red);
            nameError.Margin = Ui.Pad(0, 0, 0, 12);
            stack.Controls.Add(nameError);

            stack.Controls.Add(Caption("반복"));
            var kindRow = Row();
            dailyChip = new Chip("매일 하는 습관");
            onceChip = new Chip("한 번만 (할 일)");
            dailyChip.Click += delegate { SetDaily(true); };
            onceChip.Click += delegate { SetDaily(false); };
            kindRow.Controls.Add(dailyChip);
            kindRow.Controls.Add(onceChip);
            kindRow.Margin = Ui.Pad(0, 0, 0, 8);
            stack.Controls.Add(kindRow);

            daysRow = Row();
            for (int i = 0; i < 7; i++)
            {
                int d = (int)ScheduleForm.Order[i];
                var chip = new Chip(ScheduleForm.DayNames[d]) { Selected = quest.Days[d] };
                chip.Click += delegate
                {
                    chip.Selected = !chip.Selected;
                    quest.Days[d] = chip.Selected;
                };
                dayChips[d] = chip;
                daysRow.Controls.Add(chip);
            }
            daysRow.Margin = Ui.Pad(0, 0, 0, 14);
            stack.Controls.Add(daysRow);

            stack.Controls.Add(Caption("목표 시간"));
            var targetRow = Row();
            target = new DurationStepper(quest.TargetMinutes, 10, 600, m => m == 0 ? "없음" : Ui.Duration(m))
            {
                Size = Ui.S(170, 32),
                Margin = Ui.Pad(0, 0, 10, 0)
            };
            targetRow.Controls.Add(target);
            var targetHint = Ui.Label("채우면 알려 줘요. 없으면 직접 체크해요.", Ui.Font(8.5f), Ui.SubText);
            targetHint.Margin = Ui.Pad(0, 8, 0, 0);
            targetRow.Controls.Add(targetHint);
            targetRow.Margin = Ui.Pad(0, 0, 0, 14);
            stack.Controls.Add(targetRow);

            stack.Controls.Add(Caption("집중할 때 쓸 앱"));
            appsRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(inner, 0),
                WrapContents = true,
                BackColor = Color.Transparent,
                Margin = Ui.Pad(0, 0, 0, 6)
            };
            stack.Controls.Add(appsRow);

            var addRow = Row();
            var running = new UiButton("실행 중인 앱에서 고르기  ▾", ButtonKind.Secondary, false);
            running.Margin = Ui.Pad(0, 0, 8, 0);
            running.Click += delegate { RunningMenu().Show(running, new Point(0, running.Height + Ui.S(2))); };
            appBox = new TextBox { Width = Ui.S(120), Font = Ui.Font(10f), BorderStyle = BorderStyle.FixedSingle, Margin = Ui.Pad(0, 6, 6, 0) };
            var addApp = new UiButton("추가", ButtonKind.Ghost, false);
            addApp.Size = Ui.S(56, 38);
            addApp.Margin = Padding.Empty;
            addApp.Click += delegate { AddApp(appBox.Text); appBox.Text = ""; };
            appBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { AddApp(appBox.Text); appBox.Text = ""; e.SuppressKeyPress = true; }
            };
            addRow.Controls.Add(running);
            addRow.Controls.Add(appBox);
            addRow.Controls.Add(addApp);
            addRow.Margin = Ui.Pad(0, 0, 0, 4);
            stack.Controls.Add(addRow);
            var appHint = Ui.Label("비워 두면 앱 제한 없이 시간만 재요. 이름은 프로세스 이름이에요 (예: chrome, code, notion).",
                Ui.Font(8.5f), Ui.SubText);
            appHint.MaximumSize = new Size(inner, 0);
            appHint.Margin = Ui.Pad(0, 0, 0, 20);
            stack.Controls.Add(appHint);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            if (!isNew)
            {
                var delete = new UiButton("삭제", ButtonKind.Ghost, false) { TextColor = Ui.Red };
                delete.Margin = Ui.Pad(0, 0, 0, 0);
                delete.Click += delegate
                {
                    int c = ConfirmDialog.Show(this, "퀘스트를 삭제할까요?", "'" + quest.Title + "' 퀘스트가 사라져요. 되돌릴 수 없어요.",
                        new[] { "삭제", "취소" }, new[] { ButtonKind.Danger, ButtonKind.Ghost }, 1);
                    if (c == 0) { Deleted = true; DialogResult = DialogResult.OK; Close(); }
                };
                buttons.Controls.Add(delete);
            }
            var cancel = new UiButton("취소", ButtonKind.Ghost, false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            var save = new UiButton("저장", ButtonKind.Primary, false);
            save.Click += delegate { Save(); };
            var spacer = new Panel { Width = 1, Height = 1, BackColor = Color.Transparent };
            buttons.Controls.Add(spacer);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            stack.Controls.Add(buttons);
            Controls.Add(stack);

            SetDaily(quest.Daily);
            RebuildApps();

            Load += delegate
            {
                // 저장·취소는 오른쪽 끝에 붙인다.
                int used = 0;
                foreach (Control c in buttons.Controls) if (c != spacer) used += c.Width + c.Margin.Horizontal;
                spacer.Width = Math.Max(1, inner - used - spacer.Margin.Horizontal);
                ClientSize = stack.GetPreferredSize(Size.Empty);
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            };
            Shown += delegate { nameBox.Focus(); nameBox.SelectAll(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                else if (e.Control && e.KeyCode == Keys.S) { Save(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && ActiveControl == nameBox) { Save(); e.SuppressKeyPress = true; }
            };
        }

        public Quest Result { get { return quest; } }

        static Label Caption(string text)
        {
            var l = Ui.Label(text, Ui.Font(9f, FontStyle.Bold), Ui.SubText);
            l.Margin = Ui.Pad(0, 0, 0, 6);
            return l;
        }

        static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
        }

        void SetDaily(bool daily)
        {
            quest.Daily = daily;
            dailyChip.Selected = daily;
            onceChip.Selected = !daily;
            daysRow.Visible = daily;
        }

        void AddApp(string name)
        {
            name = AppGuard.Normalize(name);
            if (name.Length == 0 || quest.Apps.Contains(name)) return;
            quest.Apps.Add(name);
            RebuildApps();
        }

        void RebuildApps()
        {
            appsRow.SuspendLayout();
            foreach (Control c in appsRow.Controls) c.Dispose();
            appsRow.Controls.Clear();
            if (quest.Apps.Count == 0)
            {
                var none = Ui.Label("앱 제한 없음", Ui.Font(9.5f), Ui.SubText);
                none.Margin = Ui.Pad(0, 4, 0, 4);
                appsRow.Controls.Add(none);
            }
            foreach (var app in quest.Apps)
            {
                string name = app;
                var chip = new Chip(name + "   ×") { Selected = true };
                chip.Margin = Ui.Pad(0, 0, 6, 6);
                chip.Click += delegate { quest.Apps.Remove(name); RebuildApps(); };
                appsRow.Controls.Add(chip);
            }
            appsRow.ResumeLayout();
            if (IsHandleCreated) ClientSize = Controls[0].GetPreferredSize(Size.Empty);
        }

        ContextMenuStrip RunningMenu()
        {
            var menu = new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
            var apps = AppGuard.RunningApps();
            if (apps.Count == 0) menu.Items.Add(new ToolStripLabel("창이 열린 앱이 없어요") { ForeColor = Ui.SubText });
            foreach (var a in apps)
            {
                string name = a.Key;
                string titleText = a.Value.Length > 40 ? a.Value.Substring(0, 40) + "…" : a.Value;
                var item = new ToolStripMenuItem(name + "    " + titleText, null, delegate { AddApp(name); });
                item.Enabled = !quest.Apps.Contains(name);
                menu.Items.Add(item);
            }
            return menu;
        }

        void Save()
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                nameError.Text = "이름을 적어 주세요.";
                nameBox.Focus();
                return;
            }
            if (quest.Daily && Array.IndexOf(quest.Days, true) < 0)
            {
                nameError.Text = "요일을 하나 이상 골라 주세요.";
                return;
            }
            quest.Title = name;
            quest.TargetMinutes = target.Value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
