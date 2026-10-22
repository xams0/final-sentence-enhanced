using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Lanppart3Launcher
{
    // A text written for Lanppart3 tournaments. Only the server has it: players discover it when the game
    // starts (the server sends the session text to them).
    public sealed class CustomText
    {
        public string File;            // file name in the library folder
        public string Title = "";
        public string Language = "fr";
        public int Difficulty = 1;
        public string Body = "";       // rounds separated by a "***" line

        // Stable id from the file name, far above the game's own ids.
        public int Id
        {
            get
            {
                uint h = 2166136261;
                foreach (char c in (File ?? "").ToLowerInvariant()) { h ^= c; h *= 16777619; }
                return 9000000 + (int)(h % 900000);
            }
        }

        public int Rounds => Body.Replace("\r\n", "\n").Split('\n').Count(l => l.Trim() == "***") + 1;
    }

    // A text of the game's own database, as listed by the mod (catalog.txt).
    public sealed class GameText
    {
        public int Id;
        public string Language;
        public int Difficulty;
        public string Modes;    // "0,1,2"
        public string Title;
        public bool Fits(GameMode mode) => string.IsNullOrEmpty(Modes) || Modes.Split(',').Contains(((int)mode).ToString());
    }

    // Custom texts live in %APPDATA%\Lanppart3\Textes as plain text files:
    //   Titre: ...        Langue: fr        Difficulte: 1
    //   (blank line)
    //   rounds, separated by a line "***"
    public static class TextLibrary
    {
        public static string Folder => Path.Combine(AppData.Root, "Textes");
        public static string CatalogPath => Path.Combine(AppData.Root, "catalog.txt");

        public static List<CustomText> LoadCustom()
        {
            Directory.CreateDirectory(Folder);
            var list = new List<CustomText>();
            foreach (var path in Directory.GetFiles(Folder, "*.txt").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try { list.Add(Parse(Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8))); }
                catch (IOException) { }
            }
            return list;
        }

        static CustomText Parse(string file, string content)
        {
            var t = new CustomText { File = file, Title = Path.GetFileNameWithoutExtension(file) };
            var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
            int i = 0;
            for (; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Trim().Length == 0) { i++; break; }
                int colon = line.IndexOf(':');
                if (colon < 0) break;  // no header
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                if (key == "titre") t.Title = value;
                else if (key == "langue") t.Language = value;
                else if (key.StartsWith("difficult") && int.TryParse(value, out var d)) t.Difficulty = d;
                else break;
            }
            if (i > lines.Count) i = lines.Count;
            t.Body = string.Join("\n", lines.Skip(i)).Trim('\n');
            return t;
        }

        public static void Save(CustomText t)
        {
            Directory.CreateDirectory(Folder);
            if (string.IsNullOrEmpty(t.File)) t.File = UniqueFileName(t.Title);
            var content = "Titre: " + t.Title + "\nLangue: " + t.Language + "\nDifficulte: " + t.Difficulty + "\n\n" +
                          t.Body.Replace("\r\n", "\n").Trim('\n') + "\n";
            File.WriteAllText(Path.Combine(Folder, t.File), content, new UTF8Encoding(false));
        }

        public static void Delete(CustomText t)
        {
            var path = Path.Combine(Folder, t.File ?? "");
            if (File.Exists(path)) File.Delete(path);
        }

        static string UniqueFileName(string title)
        {
            var name = new string((title ?? "texte").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            if (name.Length == 0) name = "texte";
            var file = name + ".txt";
            for (int n = 2; File.Exists(Path.Combine(Folder, file)); n++) file = name + " " + n + ".txt";
            return file;
        }

        // Game texts listed by the mod the last time the game ran with it ("lang\tid\tdifficulty\tmodes\ttitle").
        public static List<GameText> LoadCatalog()
        {
            var list = new List<GameText>();
            if (!File.Exists(CatalogPath)) return list;
            foreach (var line in File.ReadAllLines(CatalogPath, Encoding.UTF8))
            {
                var f = line.Split('\t');
                if (f.Length < 5 || !int.TryParse(f[1], out var id)) continue;
                int.TryParse(f[2], out var difficulty);
                list.Add(new GameText { Language = f[0], Id = id, Difficulty = difficulty, Modes = f[3], Title = f[4] });
            }
            return list;
        }
    }

    public static class AppData
    {
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lanppart3");
    }
}
