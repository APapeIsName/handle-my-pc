using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PlayTimer
{
    class UpdateInfo
    {
        public Version Version;
        public string Tag = "";
        public string PageUrl = "";
        public string Notes = "";
        public DateTime Published;
    }

    // GitHub 릴리스로 새 버전을 확인하고, 설치 스크립트로 업데이트한다.
    static class Updater
    {
        public const string Repo = "APapeIsName/handle-my-pc";
        const string InstallScriptUrl = "https://raw.githubusercontent.com/" + Repo + "/HEAD/install.ps1";

        public static Version Current
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string Text(Version v)
        {
            return v == null ? "?" : string.Format("{0}.{1}.{2}", v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public static string CurrentText { get { return Text(Current); } }

        public static bool IsNewer(UpdateInfo info)
        {
            if (info == null || info.Version == null) return false;
            var cur = Current;
            return new Version(info.Version.Major, info.Version.Minor, Math.Max(0, info.Version.Build))
                 > new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
        }

        // 최신 릴리스 정보를 가져온다. 실패하면 예외.
        public static UpdateInfo FetchLatest()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
            var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
            req.UserAgent = "PlayTimer/" + CurrentText;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = reader.ReadToEnd();
            return Parse(json);
        }

        public static UpdateInfo Parse(string json)
        {
            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success) throw new InvalidDataException("릴리스 정보를 읽지 못했어요.");
            var info = new UpdateInfo { Tag = tag.Groups[1].Value };
            var num = Regex.Match(info.Tag, "(\\d+)\\.(\\d+)(?:\\.(\\d+))?");
            if (num.Success)
                info.Version = new Version(int.Parse(num.Groups[1].Value), int.Parse(num.Groups[2].Value),
                    num.Groups[3].Success ? int.Parse(num.Groups[3].Value) : 0);
            var page = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/tag/[^\"]+)\"");
            info.PageUrl = page.Success ? page.Groups[1].Value : "https://github.com/" + Repo + "/releases/latest";
            var body = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (body.Success) info.Notes = Unescape(body.Groups[1].Value).Trim();
            var date = Regex.Match(json, "\"published_at\"\\s*:\\s*\"([^\"]+)\"");
            DateTime published;
            if (date.Success && DateTime.TryParse(date.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out published))
                info.Published = published.ToLocalTime();
            return info;
        }

        // JSON 문자열 이스케이프를 푼다.
        static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        // exe 안에 넣어 둔 CHANGELOG.md 에서 해당 버전 부분만 꺼낸다.
        public static string ChangelogFor(string version)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PlayTimer.CHANGELOG.md"))
                {
                    if (stream == null) return "";
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return Section(reader.ReadToEnd(), version);
                }
            }
            catch { return ""; }
        }

        public static string Section(string changelog, string version)
        {
            var m = Regex.Match(changelog, "(?ms)^## " + Regex.Escape(version) + "\\b[^\\n]*\\n(.*?)(?=^## |\\z)");
            return m.Success ? m.Groups[1].Value.Replace("\r", "").Trim() : "";
        }

        // 마크다운 목록을 화면에 보이기 좋게: "- " → "•  ", "### 제목" → "제목"
        public static string Plain(string markdown)
        {
            var sb = new StringBuilder();
            foreach (var raw in markdown.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("### ")) line = line.Substring(4);
                else if (line.StartsWith("- ") || line.StartsWith("* ")) line = "•  " + line.Substring(2);
                else if (line.StartsWith("  - ")) line = "     ◦ " + line.Substring(4);
                line = line.Replace("**", "").Replace("`", "");
                sb.AppendLine(line);
            }
            return sb.ToString().Trim();
        }

        // 백그라운드에서 확인하고, 결과는 UI 스레드에서 돌려준다.
        public static void CheckAsync(SynchronizationContext ui, Action<UpdateInfo, Exception> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                Exception error = null;
                try { info = FetchLatest(); }
                catch (Exception ex) { error = ex; }
                ui.Post(delegate { done(info, error); }, null);
            });
        }

        // 설치 스크립트를 숨은 PowerShell로 실행한다. 스크립트가 지금 프로세스를 끄고 새 버전을 설치해 다시 켠다.
        // 실패하면 원래 프로그램을 다시 켠다. 기록은 %TEMP%\PlayTimer-update.log
        public static void StartUpdate()
        {
            string script =
                "$log = Join-Path $env:TEMP 'PlayTimer-update.log'\n" +
                "Start-Transcript -Path $log -Force | Out-Null\n" +
                "try {\n" +
                "  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12\n" +
                "  Start-Sleep -Seconds 2\n" +
                "  Invoke-Expression (Invoke-RestMethod -UseBasicParsing '" + InstallScriptUrl + "')\n" +
                "} catch {\n" +
                "  Write-Host $_\n" +
                "  $exe = Join-Path $env:LOCALAPPDATA 'Programs\\PlayTimer\\PlayTimer.exe'\n" +
                "  if (-not (Get-Process -Name PlayTimer -ErrorAction SilentlyContinue) -and (Test-Path $exe)) { Start-Process $exe }\n" +
                "} finally { Stop-Transcript | Out-Null }\n";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        public static void OpenPage(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
