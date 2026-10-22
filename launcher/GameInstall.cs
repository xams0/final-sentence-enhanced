using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Lanppart3Launcher
{
    // A Final Sentence installation. Nothing of the mod is ever written into it: the launcher starts the game
    // itself and injects the mod (ModRuntime, Injector), so the game started from Steam is always the original.
    public sealed class GameInstall
    {
        public const int SteamAppId = 2413950;
        public const string GameProcessName = "TheEndClient-Win64-Shipping";

        public string Root { get; }
        public string Win64 => Path.Combine(Root, "TheEnd", "Binaries", "Win64");
        public string Executable => Path.Combine(Win64, GameProcessName + ".exe");

        GameInstall(string root) { Root = root; }

        public static bool IsGameRoot(string dir) =>
            !string.IsNullOrEmpty(dir) &&
            File.Exists(Path.Combine(dir, "TheEnd", "Binaries", "Win64", GameProcessName + ".exe"));

        // The launcher's own folder, else the Steam libraries, else null.
        public static GameInstall Find(string preferred)
        {
            var candidates = new List<string> { preferred, AppDomain.CurrentDomain.BaseDirectory };
            candidates.AddRange(SteamLibraries().Select(l => Path.Combine(l, "steamapps", "common", "Final Sentence")));
            var root = candidates.FirstOrDefault(IsGameRoot);
            return root == null ? null : new GameInstall(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar));
        }

        public static string SteamPath()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                var path = key?.GetValue("SteamPath") as string;
                return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
            }
        }

        static IEnumerable<string> SteamLibraries()
        {
            var steam = SteamPath();
            if (steam == null) yield break;
            yield return steam;
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }

        public static bool IsSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

        public static bool IsGameRunning() => Process.GetProcessesByName(GameProcessName).Length > 0;

        public bool IsSteamInstall => SteamLibraries().Any(l =>
            string.Equals(Path.GetFullPath(Path.Combine(l, "steamapps", "common", "Final Sentence")).TrimEnd('\\'),
                          Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        // The original game: Steam's own copy through Steam (overlay, invitations), any other copy through
        // its own launcher.
        public void Launch()
        {
            if (IsSteamInstall)
                Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(Path.Combine(Root, "TheEnd.exe")) { UseShellExecute = true, WorkingDirectory = Root });
        }

        // The game with the mod. Started directly, the game uses the running Steam client: SteamAppId tells
        // it which game it is (Steam itself sets it for the games it starts). LANPPART3_* are the mod's orders.
        public Process LaunchModded(string arguments, IDictionary<string, string> orders, bool hidden)
        {
            var environment = new Dictionary<string, string>
            {
                ["SteamAppId"] = SteamAppId.ToString(),
                ["SteamGameId"] = SteamAppId.ToString(),
                ["LANPPART3_LAUNCHER"] = Updater.CurrentText,
            };
            foreach (var o in orders) environment[o.Key] = o.Value;
            return Injector.Start(Executable, arguments, Win64, environment, ModRuntime.Dll, hidden);
        }

        // Files of the V1/V2 packages, which installed the mod into the game's folder. The game would load
        // that copy as well as the injected one: they go.
        public bool RemoveOldModFiles()
        {
            // Only an installation of our package: UE4SS with the Lanppart mod alone (not someone's own mods).
            var mods = Path.Combine(Win64, "ue4ss", "Mods");
            if (!Directory.Exists(Path.Combine(mods, "Lanppart"))) return false;
            if (Directory.GetDirectories(mods).Any(d => Path.GetFileName(d) != "Lanppart")) return false;
            foreach (var name in new[] { "dwmapi.dll", "dwmapi.dll.lanppart3" })
            {
                var path = Path.Combine(Win64, name);
                if (File.Exists(path)) File.Delete(path);
            }
            var appId = Path.Combine(Win64, "steam_appid.txt");
            if (File.Exists(appId) && File.ReadAllText(appId).Trim() == SteamAppId.ToString()) File.Delete(appId);
            Directory.Delete(Path.Combine(Win64, "ue4ss"), true);
            foreach (var f in Directory.GetFiles(Win64, "lanppart_*")) File.Delete(f);
            return true;
        }
    }
}
