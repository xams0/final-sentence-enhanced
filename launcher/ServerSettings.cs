using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Lanppart3Launcher
{
    public enum GameMode { BattleRoyale = 0, Knockdown = 1, Duel = 2 }

    public enum TextSource { GameTexts = 0, CustomTexts = 1, Both = 2, Selection = 3 }

    // What a dedicated server plays. Saved as JSON per server, handed to the game as session.lua.
    [DataContract]
    public sealed class ServerSettings
    {
        [DataMember] public string Name = "Serveur Lanppart3";
        [DataMember] public int Port = 7777;
        [DataMember] public GameMode Mode = GameMode.Duel;
        [DataMember] public string Language = "fr";
        [DataMember] public TextSource Texts = TextSource.GameTexts;
        [DataMember] public List<int> SelectedTextIds = new List<int>();
        [DataMember] public List<string> SelectedCustomTexts = new List<string>();
        [DataMember] public bool FillWithBots = true;
        [DataMember] public int Bots = 0;
        [DataMember] public int BotsDifficulty = 2;     // EPGBotsDifficulty
        [DataMember] public int MistakeBehaviour = 1;   // EPGMistakeBehaviour: Rewind, Block, Skip
        [DataMember] public bool CapitalsMatter;
        [DataMember] public bool Roulette;
        [DataMember] public int Lives = 3;
        [DataMember] public int BulletSlots = 6;
        [DataMember] public int InitialBullets = 1;
        [DataMember] public int MaxMistakesPerRound = 3;
        [DataMember] public int RoundDuration = 120;
        [DataMember] public int PauseDuration = 10;
        // A lobby preset (PresetStore) whose rules the server plays, or -1 for the rules above.
        [DataMember] public int Preset = -1;

        // The rules the server plays: its own, or its preset's (kept up to date with the preset).
        public ServerSettings Effective()
        {
            if (Preset < 0 || Preset >= PresetStore.Count) return this;
            var p = PresetStore.Load(Preset);
            var s = Clone();
            s.Mode = p.Mode; s.Roulette = p.Roulette; s.Lives = p.Lives; s.BulletSlots = p.BulletSlots;
            s.InitialBullets = p.InitialBullets; s.MaxMistakesPerRound = p.MaxMistakesPerRound;
            s.FillWithBots = p.FillWithBots; s.Bots = p.Bots; s.BotsDifficulty = p.BotsDifficulty;
            s.MistakeBehaviour = p.MistakeBehaviour; s.CapitalsMatter = p.CapitalsMatter;
            s.RoundDuration = p.RoundDuration; s.PauseDuration = p.PauseDuration;
            return s;
        }

        public static int Capacity(GameMode mode) => mode == GameMode.BattleRoyale ? 40 : mode == GameMode.Knockdown ? 8 : 2;

        public ServerSettings Clone()
        {
            using (var ms = new MemoryStream())
            {
                var json = new DataContractJsonSerializer(typeof(ServerSettings));
                json.WriteObject(ms, this);
                ms.Position = 0;
                return (ServerSettings)json.ReadObject(ms);
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = File.Create(path))
                new DataContractJsonSerializer(typeof(ServerSettings)).WriteObject(fs, this);
        }

        public static ServerSettings Load(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                    return (ServerSettings)new DataContractJsonSerializer(typeof(ServerSettings)).ReadObject(fs) ?? new ServerSettings();
            }
            catch (Exception) { return new ServerSettings(); }
        }

        // session.lua for the game (see mod/Lanppart/Scripts/server.lua).
        public string ToLua(IEnumerable<CustomText> customTexts, IEnumerable<int> gameTextIds)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("-- Written by the Lanppart3 launcher for a dedicated server.");
            sb.AppendLine("return {");
            sb.AppendLine("    Name = " + LuaString(Name) + ",");
            sb.AppendLine("    Mode = " + (int)Mode + ", Language = " + LuaString(Language) + ",");
            sb.AppendLine("    Roulette = " + (Roulette ? "true" : "false") + ", Lives = " + Lives + ",");
            sb.AppendLine("    BulletSlots = " + BulletSlots + ", InitialBullets = " + InitialBullets + ",");
            if (Mode == GameMode.BattleRoyale) sb.AppendLine("    MaxMistakesPerRound = " + MaxMistakesPerRound + ",");
            sb.AppendLine("    Bots = " + (FillWithBots ? "\"fill\"" : Bots.ToString(inv)) + ", BotsDifficulty = " + BotsDifficulty + ",");
            sb.AppendLine("    MistakeBehaviour = " + MistakeBehaviour + ", CapitalsMatter = " + (CapitalsMatter ? "true" : "false") + ",");
            sb.AppendLine("    RoundDuration = " + RoundDuration + ", PauseDuration = " + PauseDuration + ",");
            sb.AppendLine("    TextIds = { " + string.Join(", ", gameTextIds) + " },");
            sb.AppendLine("    Texts = {");
            foreach (var t in customTexts)
            {
                sb.AppendLine("        { Id = " + t.Id + ", Title = " + LuaString(t.Title) + ", Difficulty = " + t.Difficulty +
                              ", Body = " + LuaLongString(TextLayout.Typeable(t.Body)) + " },");
            }
            sb.AppendLine("    },");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // K.O. and Duel with lives play page after page (the mod's pages.lua); otherwise a text plays its first page.
        public bool TurnsPages => !Roulette && (Mode == GameMode.Knockdown || Mode == GameMode.Duel);

        internal static string LuaString(string s) =>
            "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

        static string LuaLongString(string s)
        {
            s = (s ?? "").Replace("\r\n", "\n");
            int level = 1;
            while (s.Contains("]" + new string('=', level) + "]")) level++;
            var eq = new string('=', level);
            return "[" + eq + "[\n" + s + "]" + eq + "]";
        }
    }

    // What a running server plays: the session.lua written at its start or by "Appliquer les réglages" (ToLua).
    public sealed class AppliedSession
    {
        public ServerSettings Rules = new ServerSettings();
        public readonly List<CustomText> Custom = new List<CustomText>();
        public readonly Dictionary<int, string> CustomTitles = new Dictionary<int, string>();
        public readonly List<int> GameTextIds = new List<int>();

        public static AppliedSession Read(string path)
        {
            string lua;
            try { lua = File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException) { return null; }
            var session = new AppliedSession();
            var rules = session.Rules;
            int Int(string key, int fallback)
            {
                var m = Regex.Match(lua, @"\b" + key + @" = (\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
            }
            rules.Mode = (GameMode)Int("Mode", (int)rules.Mode);
            rules.Lives = Int("Lives", rules.Lives);
            rules.RoundDuration = Int("RoundDuration", rules.RoundDuration);
            rules.Roulette = Regex.IsMatch(lua, @"\bRoulette = true");
            var ids = Regex.Match(lua, @"\bTextIds = \{([^}]*)\}");
            if (ids.Success)
                foreach (Match id in Regex.Matches(ids.Groups[1].Value, @"\d+")) session.GameTextIds.Add(int.Parse(id.Value, CultureInfo.InvariantCulture));
            var texts = new Regex(@"\{ Id = (\d+), Title = ""((?:[^""\\]|\\.)*)"", Difficulty = (\d+), Body = \[(=*)\[\n(.*?)\]\4\] \}",
                                  RegexOptions.Singleline);
            foreach (Match t in texts.Matches(lua))
            {
                var title = Regex.Replace(t.Groups[2].Value, @"\\(.)", m => m.Groups[1].Value == "n" ? "\n" : m.Groups[1].Value);
                session.Custom.Add(new CustomText { Title = title, Body = t.Groups[5].Value });
                session.CustomTitles[int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture)] = title;
            }
            return session;
        }
    }
}
