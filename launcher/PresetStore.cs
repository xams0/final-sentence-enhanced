using System.Globalization;
using System.IO;
using System.Text;

namespace Lanppart3Launcher
{
    // The two lobby presets of the game's mode list (Lanppart Poules, Lanppart Finales): their rules are set
    // here and written to %APPDATA%\Lanppart3\presets.lua, which the mod reads when the game starts
    // (mod/Lanppart/Scripts/presets.lua). Only the rule fields of ServerSettings are used.
    public static class PresetStore
    {
        public const int Count = 2;
        static readonly string[] Keys = { "poules", "finales" };

        static string Dir => Path.Combine(AppData.Root, "presets");
        static string PathOf(int index) => Path.Combine(Dir, Keys[index] + ".json");
        public static string LuaPath => Path.Combine(AppData.Root, "presets.lua");

        public static ServerSettings Default(int index) => index == 0
            ? new ServerSettings
            {
                Name = "Lanppart Poules", Mode = GameMode.BattleRoyale, Roulette = false, Lives = 3, BulletSlots = 6,
                InitialBullets = 1, MaxMistakesPerRound = 3, FillWithBots = false, Bots = 0, BotsDifficulty = 2,
                MistakeBehaviour = 1, RoundDuration = 240, PauseDuration = 10,
            }
            : new ServerSettings
            {
                Name = "Lanppart Finales", Mode = GameMode.Duel, Roulette = false, Lives = 3, BulletSlots = 6,
                InitialBullets = 1, MaxMistakesPerRound = 3, FillWithBots = false, Bots = 0, BotsDifficulty = 2,
                MistakeBehaviour = 1, RoundDuration = 120, PauseDuration = 10,
            };

        public static ServerSettings Load(int index) =>
            File.Exists(PathOf(index)) ? ServerSettings.Load(PathOf(index)) : Default(index);

        public static void Save(int index, ServerSettings preset)
        {
            preset.Save(PathOf(index));
            WriteLua();
        }

        public static void Reset(int index)
        {
            if (File.Exists(PathOf(index))) File.Delete(PathOf(index));
            WriteLua();
        }

        // presets.lua: a Lua list of the presets, in the mode list's order.
        public static void WriteLua()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("-- Written by the Lanppart3 launcher (Presets Lanppart): the lobby presets of the mode list.");
            sb.AppendLine("return {");
            for (int i = 0; i < Count; i++)
            {
                var p = Load(i);
                sb.AppendLine("    {");
                sb.AppendLine("        Name = " + ServerSettings.LuaString(p.Name) + ", Mode = " + (int)p.Mode + ",");
                sb.AppendLine("        Roulette = " + (p.Roulette ? "true" : "false") + ", Lives = " + p.Lives +
                              ", BulletSlots = " + p.BulletSlots + ", InitialBullets = " + p.InitialBullets + ",");
                sb.AppendLine("        MaxMistakesPerRound = " + p.MaxMistakesPerRound + ", Bots = " +
                              (p.FillWithBots ? -1 : p.Bots).ToString(inv) + ", BotsDifficulty = " + p.BotsDifficulty + ",");
                sb.AppendLine("        MistakeBehaviour = " + p.MistakeBehaviour + ", CapitalsMatter = " + (p.CapitalsMatter ? "true" : "false") + ",");
                sb.AppendLine("        RoundDuration = " + p.RoundDuration + ", PauseDuration = " + p.PauseDuration + ",");
                sb.AppendLine("    },");
            }
            sb.AppendLine("}");
            Directory.CreateDirectory(AppData.Root);
            File.WriteAllText(LuaPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
