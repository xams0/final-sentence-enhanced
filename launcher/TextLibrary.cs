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

        public TextLayout Layout => TextLayout.Of(Body);
    }

    // A custom text as the mod lays it out on the typewriter (texts.lua GameFormat, pages.lua Split): characters
    // no keyboard types replaced, lines of at most 48 characters cut between words, blank lines dropped, rounds
    // separated by a "***" line. The typewriter draws the session text on one page of 81 rows, a blank row
    // between two rounds: the mod cuts texts into pages of at most 80 rows.
    public sealed class TextLayout
    {
        public const int MaxLine = 48;
        public const int PageRows = 80;

        public readonly List<List<string>> Rounds = new List<List<string>>();
        public readonly List<int> PageRowCounts = new List<int>();
        public readonly List<int> PageRoundCounts = new List<int>();
        public readonly List<string> Replaced = new List<string>();  // the replaced characters found

        public int Lines => Rounds.Sum(r => r.Count);
        public int Pages => PageRowCounts.Count;
        public int LongestRound => Rounds.Count == 0 ? 0 : Rounds.Max(r => r.Count);
        public int LongestRoundChars => Rounds.Count == 0 ? 0 : Rounds.Max(r => r.Sum(l => l.Length + 1));

        static readonly Dictionary<string, string> Untypeable = new Dictionary<string, string>
        {
            ["•"] = "-", ["·"] = "-", ["–"] = "-", ["—"] = "-", ["…"] = "...",
            ["«"] = "\"", ["»"] = "\"", ["“"] = "\"", ["”"] = "\"", ["‘"] = "'", ["’"] = "'",
            ["œ"] = "oe", ["Œ"] = "OE", ["æ"] = "ae", ["Æ"] = "AE", [" "] = " ", [" "] = " ",
        };

        public static string Typeable(string s)
        {
            s = s ?? "";
            foreach (var kv in Untypeable) s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        public static TextLayout Of(string body)
        {
            var layout = new TextLayout();
            body = body ?? "";
            foreach (var kv in Untypeable)
                if (body.Contains(kv.Key)) layout.Replaced.Add(kv.Key == " " || kv.Key == " " ? "espace insécable" : kv.Key);
            var current = new List<string>();
            foreach (var raw in Typeable(body).Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Trim() == "***")
                {
                    if (current.Count > 0) layout.Rounds.Add(current);
                    current = new List<string>();
                }
                else if (line.Length > 0) current.AddRange(Wrap(line));
            }
            if (current.Count > 0) layout.Rounds.Add(current);

            int rows = 0, roundsOnPage = 0;
            foreach (var round in layout.Rounds)
                for (int first = 0; first < round.Count; first += PageRows)
                {
                    int lines = Math.Min(PageRows, round.Count - first);
                    int needed = lines + (roundsOnPage > 0 ? 1 : 0);
                    if (rows + needed > PageRows)
                    {
                        layout.PageRowCounts.Add(rows);
                        layout.PageRoundCounts.Add(roundsOnPage);
                        rows = roundsOnPage = 0;
                        needed = lines;
                    }
                    rows += needed;
                    roundsOnPage++;
                }
            if (roundsOnPage > 0)
            {
                layout.PageRowCounts.Add(rows);
                layout.PageRoundCounts.Add(roundsOnPage);
            }
            return layout;
        }

        static IEnumerable<string> Wrap(string line)
        {
            if (line.Length <= MaxLine) { yield return line; yield break; }
            var current = "";
            foreach (var w in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var word = w;
                while (word.Length > MaxLine)  // (a word longer than a line: cut it)
                {
                    if (current.Length > 0) { yield return current; current = ""; }
                    yield return word.Substring(0, MaxLine);
                    word = word.Substring(MaxLine);
                }
                if (current.Length == 0) current = word;
                else if (current.Length + 1 + word.Length <= MaxLine) current += " " + word;
                else { yield return current; current = word; }
            }
            if (current.Length > 0) yield return current;
        }
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
