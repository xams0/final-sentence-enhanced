using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Lanppart3Launcher
{
    // The mod (UE4SS + the Lanppart scripts) travels inside the launcher: mod.zip, built by mod/package.sh.
    // It is unpacked to %APPDATA%\Lanppart3\runtime\<version>-<hash> and injected into the games the launcher
    // starts (Injector), so the game's own folder is never touched.
    public static class ModRuntime
    {
        const string Payload = "mod.zip";

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        static string dir;

        // Folder of the unpacked mod (null until Prepare succeeded).
        public static string Dir => dir;
        public static string Dll => dir == null ? null : Path.Combine(dir, "UE4SS.dll");
        public static bool Available => dir != null && File.Exists(Dll);
        public static string Problem { get; private set; }

        static string RuntimeRoot => Path.Combine(AppData.Root, "runtime");

        // Unpacks the mod if this version isn't there yet, and removes the other versions no game uses.
        public static void Prepare()
        {
            try
            {
                using (var zip = Assembly.GetExecutingAssembly().GetManifestResourceStream(Payload))
                {
                    if (zip == null) { Problem = "le lanceur ne contient pas le mod"; return; }
                    string hash;
                    using (var sha = SHA256.Create())
                        hash = string.Concat(sha.ComputeHash(zip).Take(4).Select(b => b.ToString("x2")));
                    zip.Position = 0;
                    var target = Path.Combine(RuntimeRoot, Version + "-" + hash);
                    if (!File.Exists(Path.Combine(target, "UE4SS.dll")))
                    {
                        var temp = target + ".tmp";
                        if (Directory.Exists(temp)) Directory.Delete(temp, true);
                        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
                            archive.ExtractToDirectory(temp);
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        Directory.Move(temp, target);
                    }
                    dir = target;
                }
                RemoveOldVersions();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                Problem = ex.Message;
            }
        }

        // Older versions, unless a game (player or server) still has them loaded. "dev" is the developer's copy.
        static void RemoveOldVersions()
        {
            foreach (var old in Directory.GetDirectories(RuntimeRoot))
            {
                var name = Path.GetFileName(old);
                if (string.Equals(old.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || name == "dev") continue;
                try
                {
                    // A loaded UE4SS.dll can't be opened for writing: leave that version whole.
                    var dll = Path.Combine(old, "UE4SS.dll");
                    if (File.Exists(dll)) using (File.Open(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    Directory.Delete(old, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }
    }
}
