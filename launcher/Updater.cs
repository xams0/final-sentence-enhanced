using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lanppart3Launcher
{
    public enum UpdateState { Checking, UpToDate, Available, Unreachable }

    // Updates come from the GitHub releases of the project: the latest release's tag (v3.1.0...) is its
    // version, and its asset Lanppart3-Final-Sentence.exe is the new launcher (the mod is inside).
    // An outdated launcher can't start the game: every player of a LAN party runs the same mod.
    public sealed class Updater
    {
        public const string Repository = "xams0/final-sentence-enhanced";
        public const string AssetName = "Lanppart3-Final-Sentence.exe";
        const string UserAgent = "Lanppart3-Launcher";

        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        public static string CurrentText => Current.ToString(3);

        public UpdateState State { get; private set; } = UpdateState.Checking;
        public Version Latest { get; private set; }
        public string LatestTag { get; private set; }

        static string ExePath => Process.GetCurrentProcess().MainModule.FileName;
        static string OldExePath => ExePath + ".old";

        static Updater()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        // Reads the latest release's tag: from the redirect of /releases/latest (no rate limit), else from the
        // API (60 requests an hour per address; that redirect can lag behind a new release for a while).
        public async Task CheckAsync()
        {
            try
            {
                LatestTag = await TagFromRedirect() ?? await TagFromApi();
                if (LatestTag == null) { State = UpdateState.UpToDate; return; }  // no release published
                if (!Version.TryParse(LatestTag.TrimStart('v', 'V'), out var latest)) { State = UpdateState.Unreachable; return; }
                Latest = latest;
                State = Normalize(latest) > Normalize(Current) ? UpdateState.Available : UpdateState.UpToDate;
            }
            catch (Exception ex) when (ex is WebException || ex is IOException)
            {
                State = UpdateState.Unreachable;
            }
        }

        static HttpWebRequest Request(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = 15000;
            return request;
        }

        static async Task<string> TagFromRedirect()
        {
            var request = Request("https://github.com/" + Repository + "/releases/latest");
            request.AllowAutoRedirect = false;
            using (var response = (HttpWebResponse)await request.GetResponseAsync())
            {
                var location = response.Headers[HttpResponseHeader.Location] ?? "";
                int tagAt = location.LastIndexOf("/tag/", StringComparison.Ordinal);
                return tagAt < 0 ? null : Uri.UnescapeDataString(location.Substring(tagAt + 5));
            }
        }

        static async Task<string> TagFromApi()
        {
            var request = Request("https://api.github.com/repos/" + Repository + "/releases/latest");
            request.Accept = "application/vnd.github+json";
            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var match = Regex.Match(await reader.ReadToEndAsync(), "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    return match.Success ? match.Groups[1].Value : null;
                }
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                return null;  // no release
            }
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        // Downloads the latest launcher next to this one, replaces this one with it and starts it.
        // The running executable can be renamed (not deleted): it is removed by the next start (CleanUp).
        public async Task InstallLatestAsync(Action<int> progress)
        {
            var url = "https://github.com/" + Repository + "/releases/download/" + Uri.EscapeDataString(LatestTag) + "/" + AssetName;
            var newPath = ExePath + ".new";
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            using (var response = (HttpWebResponse)await request.GetResponseAsync())
            using (var input = response.GetResponseStream())
            using (var output = File.Create(newPath))
            {
                var buffer = new byte[81920];
                long total = response.ContentLength, done = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, n);
                    done += n;
                    if (total > 0) progress((int)(done * 100 / total));
                }
            }
            var version = FileVersionInfo.GetVersionInfo(newPath);
            if (version.ProductName == null || !version.ProductName.StartsWith("Lanppart3"))
            {
                File.Delete(newPath);
                throw new InvalidDataException("le fichier téléchargé n'est pas le lanceur Lanppart3");
            }
            if (File.Exists(OldExePath)) File.Delete(OldExePath);
            File.Move(ExePath, OldExePath);
            File.Move(newPath, ExePath);
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(ExePath) });
        }

        // Leftovers of a previous update.
        public static void CleanUp()
        {
            foreach (var path in new[] { OldExePath, ExePath + ".new" })
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }  // still closing: next time
            }
        }
    }
}
