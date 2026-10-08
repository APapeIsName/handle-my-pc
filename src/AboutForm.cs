using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PlayTimer
{
    // 앱 자체에 대한 설정과 상태(업데이트 확인 등). %LOCALAPPDATA%\PlayTimer\app.txt
    class AppPrefs
    {
        public bool AutoCheckUpdates = true;
        public long LastCheckUtcTicks;
        public string LastRunVersion = "";

        public DateTime LastCheck
        {
            get { return LastCheckUtcTicks > 0 ? new DateTime(LastCheckUtcTicks, DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue; }
        }

        public static AppPrefs Load(string path)
        {
            var p = new AppPrefs();
            if (!File.Exists(path)) return p;
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                if (key == "autoCheckUpdates") p.AutoCheckUpdates = val == "1";
                else if (key == "lastCheck") long.TryParse(val, out p.LastCheckUtcTicks);
                else if (key == "lastRunVersion") p.LastRunVersion = val;
            }
            return p;
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new[] {
                    "autoCheckUpdates=" + (AutoCheckUpdates ? "1" : "0"),
                    "lastCheck=" + LastCheckUtcTicks,
                    "lastRunVersion=" + LastRunVersion
                });
            }
            catch { }
        }
    }

    // "PlayTimer 정보" 창: 현재 버전, 업데이트 확인·설치, 바뀐 점, 도움 링크.
    class AboutForm : Form
    {
        readonly TrayApp app;
        readonly Panel card;
        readonly Label statusTitle, statusSub, notesTitle;
        readonly TextBox notes;
        readonly UiButton checkButton, updateButton, pageButton;
        readonly CheckBox autoBox;
        Color statusColor = Ui.SubText;

        public AboutForm(TrayApp app)
        {
            this.app = app;
            Text = "PlayTimer 정보";
            if (Ui.AppIcon != null) Icon = Ui.AppIcon; else ShowIcon = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(9.5f);
            BackColor = Ui.Surface;
            KeyPreview = true;

            int pad = Ui.S(24), width = Ui.S(520), inner = width - pad * 2;

            var logo = new PictureBox { Location = new Point(pad, Ui.S(22)), Size = Ui.S(56, 56), SizeMode = PictureBoxSizeMode.Zoom };
            var big = Ui.LargeIcon();
            if (big != null) logo.Image = big;
            Controls.Add(logo);
            var name = Ui.Label("PlayTimer", Ui.Font(17f, FontStyle.Bold), Ui.Text);
            name.Location = new Point(logo.Right + Ui.S(14), Ui.S(20));
            var version = Ui.Label("버전 " + Updater.CurrentText, Ui.Font(10f), Ui.SubText);
            version.Location = new Point(logo.Right + Ui.S(16), Ui.S(56));
            Controls.Add(name);
            Controls.Add(version);
            var tagline = Ui.Label("PC를 켜면 원래 하려던 일부터. 습관과 시간 관리를 돕는 타이머예요.", Ui.Font(9.5f), Ui.SubText);
            tagline.Location = new Point(pad, Ui.S(92));
            Controls.Add(tagline);

            // 업데이트 상태 카드
            card = new Panel { Location = new Point(pad, Ui.S(124)), Size = new Size(inner, Ui.S(118)), BackColor = Ui.SurfaceAlt };
            card.Paint += PaintCard;
            statusTitle = Ui.Label("", Ui.Font(11.5f, FontStyle.Bold), Ui.Text);
            statusTitle.Location = Ui.P(36, 16);
            statusSub = Ui.Label("", Ui.Font(9f), Ui.SubText);
            statusSub.Location = Ui.P(36, 42);
            statusSub.MaximumSize = new Size(inner - Ui.S(52), 0);
            card.Controls.Add(statusTitle);
            card.Controls.Add(statusSub);
            var row = CardForm.ButtonRow(false);
            row.Location = Ui.P(30, 70);
            updateButton = new UiButton("지금 업데이트", ButtonKind.Primary, false);
            updateButton.Click += delegate { app.InstallUpdate(this); };
            checkButton = new UiButton("업데이트 확인", ButtonKind.Secondary, false);
            checkButton.Click += delegate { app.CheckForUpdates(true); };
            pageButton = new UiButton("릴리스 페이지", ButtonKind.Ghost, false);
            pageButton.Click += delegate
            {
                var u = app.LatestUpdate;
                Updater.OpenPage(u != null ? u.PageUrl : "https://github.com/" + Updater.Repo + "/releases");
            };
            foreach (var b in new[] { updateButton, checkButton, pageButton }) { b.Height = Ui.S(34); row.Controls.Add(b); }
            card.Controls.Add(row);
            Controls.Add(card);

            autoBox = new CheckBox
            {
                Text = "새 버전을 자동으로 확인 (켜져 있을 때 12시간마다)",
                AutoSize = true,
                Checked = app.Prefs.AutoCheckUpdates,
                Location = new Point(pad, card.Bottom + Ui.S(12)),
                ForeColor = Ui.Text
            };
            autoBox.CheckedChanged += delegate { app.Prefs.AutoCheckUpdates = autoBox.Checked; app.SavePrefs(); };
            Controls.Add(autoBox);

            notesTitle = Ui.Label("", Ui.Font(10f, FontStyle.Bold), Ui.Text);
            notesTitle.Location = new Point(pad, autoBox.Bottom + Ui.S(18));
            Controls.Add(notesTitle);
            notes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Ui.Surface,
                ForeColor = Ui.Text,
                Font = Ui.Font(9.5f),
                Location = new Point(pad, notesTitle.Top + Ui.S(28)),
                Size = new Size(inner, Ui.S(170)),
                TabStop = false
            };
            Controls.Add(notes);

            var divider = new Panel { BackColor = Ui.Line, Location = new Point(0, notes.Bottom + Ui.S(14)), Size = new Size(width, 1) };
            Controls.Add(divider);

            var links = CardForm.ButtonRow(false);
            links.Location = new Point(pad - Ui.S(3), divider.Bottom + Ui.S(12));
            links.Controls.Add(Link("GitHub", delegate { Updater.OpenPage("https://github.com/" + Updater.Repo); }));
            links.Controls.Add(Link("문제 신고·제안", delegate { Updater.OpenPage("https://github.com/" + Updater.Repo + "/issues/new"); }));
            links.Controls.Add(Link("데이터 폴더 열기", delegate { Open(app.DataDir); }));
            links.Controls.Add(Link("설정 파일(config.ini)", delegate { Open(app.ConfigPath); }));
            Controls.Add(links);

            ClientSize = new Size(width, links.Bottom + Ui.S(16));
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };

            app.UpdateStateChanged += OnUpdateState;
            FormClosed += delegate { app.UpdateStateChanged -= OnUpdateState; };
            Refresh2();
        }

        void OnUpdateState(object sender, EventArgs e)
        {
            if (!IsDisposed) Refresh2();
        }

        static LinkLabel Link(string text, EventHandler onClick)
        {
            var l = new LinkLabel
            {
                Text = text,
                AutoSize = true,
                LinkColor = Ui.Blue,
                ActiveLinkColor = Ui.Blue,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = Ui.Pad(3, 0, 14, 0)
            };
            l.LinkClicked += delegate { onClick(l, EventArgs.Empty); };
            return l;
        }

        static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        static string Ago(DateTime t)
        {
            if (t == DateTime.MinValue) return "아직 확인 안 함";
            var span = DateTime.Now - t;
            if (span.TotalMinutes < 1) return "방금 확인";
            if (t.Date == DateTime.Today) return "오늘 " + t.ToString("HH:mm") + " 확인";
            if (t.Date == DateTime.Today.AddDays(-1)) return "어제 " + t.ToString("HH:mm") + " 확인";
            return t.ToString("M월 d일 HH:mm") + " 확인";
        }

        void Refresh2()
        {
            var latest = app.LatestUpdate;
            bool checking = app.CheckingUpdate;
            bool newer = Updater.IsNewer(latest);
            string current = Updater.CurrentText;

            if (checking)
            {
                statusColor = Ui.SubText;
                statusTitle.Text = "새 버전을 확인하고 있어요…";
                statusSub.Text = "GitHub에서 최신 릴리스를 가져오는 중이에요.";
            }
            else if (newer)
            {
                statusColor = Ui.Green;
                statusTitle.Text = "새 버전 " + Updater.Text(latest.Version) + "이(가) 나왔어요";
                statusSub.Text = "잠깐 꺼졌다가 새 버전으로 다시 켜져요. 데이터와 설정은 그대로예요.";
            }
            else if (app.UpdateError != null)
            {
                statusColor = Ui.Amber;
                statusTitle.Text = "업데이트를 확인하지 못했어요";
                statusSub.Text = "인터넷 연결을 확인해 주세요. (" + app.UpdateError + ")";
            }
            else if (latest != null)
            {
                statusColor = Ui.Blue;
                statusTitle.Text = "최신 버전을 쓰고 있어요";
                statusSub.Text = "버전 " + current + " · " + Ago(app.Prefs.LastCheck);
            }
            else
            {
                statusColor = Ui.SubText;
                statusTitle.Text = "버전 " + current;
                statusSub.Text = Ago(app.Prefs.LastCheck);
            }

            updateButton.Visible = newer && !checking;
            checkButton.Enabled = !checking;
            checkButton.Text = checking ? "확인 중…" : "업데이트 확인";
            checkButton.FitToText();
            checkButton.Height = Ui.S(34);

            if (newer)
            {
                notesTitle.Text = "새 버전 " + Updater.Text(latest.Version) + "에서 바뀐 점";
                notes.Text = latest.Notes.Length > 0 ? Updater.Plain(latest.Notes).Replace("\n", "\r\n") : "릴리스 페이지에서 확인해 주세요.";
            }
            else
            {
                notesTitle.Text = "이 버전(" + current + ")에서 바뀐 점";
                string text = Updater.Plain(Updater.ChangelogFor(current));
                notes.Text = text.Length > 0 ? text.Replace("\n", "\r\n") : "기록된 변경 사항이 없어요.";
            }
            notes.Select(0, 0);
            card.Invalidate();
        }

        void PaintCard(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(new Rectangle(0, 0, card.Width - 1, card.Height - 1), Ui.S(12)))
            using (var b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, path);
            using (var b = new SolidBrush(statusColor)) g.FillEllipse(b, Ui.S(16), Ui.S(22), Ui.S(10), Ui.S(10));
        }
    }
}
