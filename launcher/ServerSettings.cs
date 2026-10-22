using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

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
                              ", Body = " + LuaLongString(Typeable(t.Body)) + " },");
            }
            sb.AppendLine("    },");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // Typographic characters a keyboard doesn't type (pasted from a word processor): their plain forms.
        static string Typeable(string s) => (s ?? "")
            .Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u201C', '"').Replace('\u201D', '"')
            .Replace("\u2026", "...").Replace('\u00A0', ' ').Replace('\u202F', ' ').Replace('\u2013', '-').Replace('\u2014', '-');

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
}
