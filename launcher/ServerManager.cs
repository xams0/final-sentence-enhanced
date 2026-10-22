using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Lanppart3Launcher
{
    // A dedicated server: a game instance without window, sound or player, started with LANPPART3_SERVER
    // pointing at its folder. The launcher writes session.lua and command.txt there; the server reports in
    // status.txt (see mod/Lanppart/Scripts/server.lua).
    public sealed class ServerInstance
    {
        public string Id;
        public ServerSettings Settings;
        public Process Process;
        public DateTime Started;
        public Dictionary<string, string> Status = new Dictionary<string, string>();
        public string LastPhase;  // phase already reported to the user

        string PidPath => Path.Combine(Dir, "pid.txt");

        // A server started by an earlier launcher session that still runs.
        public void Reattach()
        {
            try
            {
                if (!File.Exists(PidPath) || !int.TryParse(File.ReadAllText(PidPath).Trim(), out var pid)) return;
                var p = Process.GetProcessById(pid);
                if (p.ProcessName != Path.GetFileNameWithoutExtension(ServerManager.ExeName)) return;
                Process = p;
                Started = p.StartTime;
                ReadStatus();
            }
            catch (ArgumentException) { }       // gone
            catch (InvalidOperationException) { }
        }

        public void RememberProcess() => File.WriteAllText(PidPath, Process.Id.ToString());

        public string Dir => Path.Combine(AppData.Root, "servers", Id);
        public string SettingsPath => Path.Combine(Dir, "settings.json");

        public bool Running
        {
            get { try { return Process != null && !Process.HasExited; } catch (InvalidOperationException) { return false; } }
        }

        public string Phase => Status.TryGetValue("phase", out var p) ? p : (Running ? "boot" : "stopped");
        public int PlayerCount => Status.TryGetValue("count", out var c) && int.TryParse(c, out var n) ? n : 0;
        public string[] Players => Status.TryGetValue("players", out var p) && p.Length > 0 ? p.Split('\t') : new string[0];

        public string PhaseText
        {
            get
            {
                if (!Running) return "Arrêté";
                switch (Phase)
                {
                    case "boot": case "ready": return "Démarrage…";
                    case "hosting": return "Ouverture de la partie…";
                    case "waiting": return "En attente des joueurs";
                    case "playing": return "Partie en cours";
                    case "ended": return "Partie terminée";
                    default: return Phase;
                }
            }
        }

        public void ReadStatus()
        {
            var path = Path.Combine(Dir, "status.txt");
            if (!File.Exists(path)) return;
            try
            {
                var status = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) status[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
                Status = status;
            }
            catch (IOException) { }  // being rewritten
        }

        public void Send(string command) => File.WriteAllText(Path.Combine(Dir, "command.txt"), command + "\n");
    }

    public sealed class ServerManager
    {
        public const string ExeName = "TheEndClient-Win64-Shipping.exe";
        public readonly List<ServerInstance> Servers = new List<ServerInstance>();

        public ServerManager()
        {
            // Servers configured earlier (stopped).
            var root = Path.Combine(AppData.Root, "servers");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
            {
                var settings = Path.Combine(dir, "settings.json");
                if (!File.Exists(settings)) continue;
                var server = new ServerInstance { Id = Path.GetFileName(dir), Settings = ServerSettings.Load(settings) };
                server.Reattach();
                Servers.Add(server);
            }
        }

        public ServerInstance Create()
        {
            int n = 1;
            while (Servers.Any(s => s.Id == "server" + n)) n++;
            var port = 7777;
            while (Servers.Any(s => s.Settings.Port == port)) port++;
            var server = new ServerInstance
            {
                Id = "server" + n,
                Settings = new ServerSettings { Name = "Serveur Lanppart3 " + n, Port = port },
            };
            Servers.Add(server);
            server.Settings.Save(server.SettingsPath);
            return server;
        }

        public void Remove(ServerInstance server)
        {
            Stop(server);
            Servers.Remove(server);
            try { Directory.Delete(server.Dir, true); } catch (IOException) { }
        }

        // Session file for the server: the chosen texts (the catalog's game texts, the custom texts).
        public void WriteSession(ServerInstance server)
        {
            var s = server.Settings.Effective();
            var custom = new List<CustomText>();
            var ids = new List<int>();
            if (s.Texts == TextSource.CustomTexts || s.Texts == TextSource.Both)
                custom.AddRange(TextLibrary.LoadCustom().Where(t => t.Language == s.Language));
            if (s.Texts == TextSource.GameTexts || s.Texts == TextSource.Both)
                ids.AddRange(TextLibrary.LoadCatalog().Where(t => t.Language == s.Language && t.Fits(s.Mode)).Select(t => t.Id));
            if (s.Texts == TextSource.Selection)
            {
                ids.AddRange(s.SelectedTextIds);
                custom.AddRange(TextLibrary.LoadCustom().Where(t => s.SelectedCustomTexts.Contains(t.File)));
            }
            Directory.CreateDirectory(server.Dir);
            server.Settings.Save(server.SettingsPath);
            File.WriteAllText(Path.Combine(server.Dir, "session.lua"), s.ToLua(custom, ids.Distinct()), new UTF8Encoding(false));
        }

        public void Start(ServerInstance server, GameInstall install)
        {
            if (server.Running) return;
            WriteSession(server);
            foreach (var f in new[] { "status.txt", "command.txt" })
            {
                var p = Path.Combine(server.Dir, f);
                if (File.Exists(p)) File.Delete(p);
            }
            server.Status = new Dictionary<string, string>();
            server.Process = install.LaunchModded("-nullrhi -nosound -unattended -port=" + server.Settings.Port,
                new Dictionary<string, string> { ["LANPPART3_SERVER"] = server.Dir, ["LANPPART3_ID"] = server.Id }, true);
            server.Started = DateTime.Now;
            server.RememberProcess();
        }

        public void Stop(ServerInstance server)
        {
            if (!server.Running) return;
            server.Send("stop");
            if (!server.Process.WaitForExit(5000))
            {
                try { server.Process.Kill(); } catch (InvalidOperationException) { }
            }
        }

        // Settings changed while running: the server uses them from its next session.
        public void Apply(ServerInstance server)
        {
            WriteSession(server);
            if (server.Running) server.Send("reload");
        }

        public void Refresh()
        {
            foreach (var s in Servers) if (s.Running) s.ReadStatus();
        }

        public bool AnyStarting => Servers.Any(s => s.Running && (s.Phase == "boot") && DateTime.Now - s.Started < TimeSpan.FromMinutes(2));
    }
}
