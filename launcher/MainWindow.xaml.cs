using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Lanppart3Launcher
{
    public partial class MainWindow : Window
    {
        enum Session { None, Starting, Running }

        static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

        GameInstall install;
        Session session = Session.None;
        bool sessionModded;
        DateTime sessionStart;
        readonly DispatcherTimer watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        readonly ServerManager servers = new ServerManager();
        readonly BeaconSender beacons = new BeaconSender();
        readonly Updater updater = new Updater();
        DateTime lastUpdateCheck;
        bool updating, closingForUpdate;
        BeaconListener listener;

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(AppData.Root);
            Updater.CleanUp();
            ModRuntime.Prepare();
            install = GameInstall.Find(null);
            VersionText.Text = "Lanppart3 · mod non officiel pour Final Sentence · lanceur v" + Updater.CurrentText;
            try { listener = new BeaconListener(); }
            catch (SocketException) { listener = null; }  // port taken: no server list, joining by address still works
            watch.Tick += (s, e) => WatchGame();
            watch.Start();
            RemoveOldModFiles();
            try { PresetStore.WriteLua(); }  // the mod's copy of the lobby presets
            catch (IOException) { }
            ShowWhy();
            RefreshState();
            CheckForUpdate();
        }

        // ---- Updates ----

        async void CheckForUpdate()
        {
            lastUpdateCheck = DateTime.Now;
            await updater.CheckAsync();
            RefreshState();
        }

        async void OnUpdate(object sender, RoutedEventArgs e)
        {
            if (updating) return;
            updating = true;
            UpdateButton.IsEnabled = false;
            try
            {
                await updater.InstallLatestAsync(percent => Dispatcher.Invoke(() =>
                    StatusText.Text = "Téléchargement de la mise à jour… " + percent + " %"));
                // The new launcher has started: it picks up the running servers (pid.txt).
                closingForUpdate = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = "La mise à jour a échoué : " + ex.Message;
                UpdateButton.IsEnabled = true;
            }
            updating = false;
        }

        // The V1/V2 packages installed the mod into the game's folder: the game would load it twice.
        void RemoveOldModFiles()
        {
            if (install == null || GameInstall.IsGameRunning()) return;
            try
            {
                if (install.RemoveOldModFiles())
                    StatusText.Text = "L'ancienne installation du mod (V2) a été retirée du dossier du jeu : " +
                                      "le jeu y est de nouveau d'origine.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                StatusText.Text = "Les fichiers de l'ancienne version du mod (dossier ue4ss, dwmapi.dll) n'ont pas pu être " +
                                  "retirés du dossier du jeu : supprimez-les à la main.";
            }
        }

        // The game can be started from the launcher: found, and the launcher known to be up to date (or
        // GitHub unreachable: a LAN party without internet still plays).
        bool CanPlay => install != null && updater.State != UpdateState.Available && updater.State != UpdateState.Checking;

        // ... with the mod.
        bool ModReady => CanPlay && ModRuntime.Available;

        // ---- Texts ----

        void ShowInfo(string title, string text)
        {
            if (page != Page.None) return;  // a page is open: keep it
            InfoTitle.Text = title;
            InfoText.Text = text;
        }

        void OnPointNormal(object sender, EventArgs e) => ShowInfo("Mode normal",
            "Lance Final Sentence tel quel, comme depuis Steam. Le mod Lanppart3 est désactivé : parties rapides, " +
            "classements, ligues et lobbys utilisent les serveurs officiels.\n\n" +
            "À choisir pour jouer en ligne avec tout le monde.");

        void OnPointLanppart(object sender, EventArgs e) => ShowInfo("Mode Lanppart3",
            "Lance le jeu avec le mod Lanppart3. Toute partie lancée depuis un lobby (Partie personnalisée, " +
            "Créer nouveau) est hébergée par le PC de l'hôte du lobby au lieu des serveurs officiels.\n\n" +
            "Les règles deviennent libres : roulette russe ou vies dans tous les modes, presets Lanppart Poules " +
            "et Lanppart Finales (réglables dans « Presets Lanppart »).\n\n" +
            "Tous les joueurs du lobby doivent lancer le jeu dans ce mode.");

        void OnPointWhy(object sender, EventArgs e) => ShowWhy();

        void ShowWhy() => ShowInfo("Pourquoi deux modes ?",
            "En ligne, ce sont les serveurs officiels qui appliquent les règles : impossible d'y ajouter des vies " +
            "ou d'enlever la roulette du Duel. Le mod fait donc jouer un PC du groupe comme serveur : celui de " +
            "l'hôte du lobby, ou un serveur Lanppart3 dédié.\n\n" +
            "Pour que vos parties habituelles restent intactes, le mod ne s'active que lorsque le jeu est lancé " +
            "par ce lanceur en mode Lanppart3. Lancé depuis Steam ou en mode normal, c'est le jeu original.");

        void OnPointHowTo(object sender, EventArgs e) => ShowInfo("Jouer en Lanppart3",
            "Avec un serveur dédié : l'organisateur ouvre « Héberger un serveur » et règle la partie ; les joueurs " +
            "ouvrent « Rejoindre un serveur » et choisissent le serveur ; l'organisateur lance la partie.\n\n" +
            "Sans serveur : tout le monde lance le jeu en mode Lanppart3, l'hôte ouvre « Partie personnalisée », " +
            "les autres rejoignent son lobby et tout le monde clique « Prêt(e) ». Décocher « Jouer à la roulette " +
            "russe » fait apparaître la ligne Vies.");

        void OnPointFolder(object sender, EventArgs e) => ShowInfo("Dossier du jeu",
            install == null
                ? "Final Sentence n'a pas été trouvé. Cliquez pour indiquer son dossier (celui qui contient TheEnd.exe)."
                : install.Root + "\n\nCliquez pour l'ouvrir dans l'explorateur.");

        // ---- State ----

        // The player's own game (dedicated servers run the same executable).
        bool PlayerGameRunning()
        {
            var serverIds = servers.Servers.Where(s => s.Running).Select(s => s.Process.Id).ToHashSet();
            return Process.GetProcessesByName(GameInstall.GameProcessName).Any(p => !serverIds.Contains(p.Id));
        }

        void RefreshState()
        {
            bool running = PlayerGameRunning();
            bool ready = CanPlay && session == Session.None && !running;
            NormalButton.IsEnabled = ready;
            ModButton.IsEnabled = ready && ModReady;
            BrowserButton.IsEnabled = ModReady;
            HostButton.IsEnabled = ModReady;
            UpdatePanel.Visibility = updater.State == UpdateState.Available ? Visibility.Visible : Visibility.Collapsed;
            if (updater.State == UpdateState.Available && !updating) UpdateButton.Content = "Installer la " + updater.LatestTag;

            if (install == null)
            {
                GameText.Text = "Final Sentence introuvable : indiquez son dossier avec « Dossier du jeu ».";
            }
            else
            {
                string mod = !ModRuntime.Available ? "mod Lanppart3 indisponible (" + ModRuntime.Problem + ")"
                           : updater.State == UpdateState.Unreachable ? "mod Lanppart3 v" + Updater.CurrentText + " (mises à jour non vérifiées : GitHub injoignable)"
                           : updater.State == UpdateState.Available ? "mod Lanppart3 v" + Updater.CurrentText + " périmé"
                           : "mod Lanppart3 v" + Updater.CurrentText;
                int serverCount = servers.Servers.Count(s => s.Running);
                GameText.Text = install.Root + "  ·  " + mod + (serverCount > 0 ? "  ·  " + serverCount + " serveur(s) actif(s)" : "");
            }

            if (session == Session.Starting)
                StatusText.Text = "Lancement de Final Sentence" + (sessionModded ? " en mode Lanppart3…" : "…");
            else if (session == Session.Running)
                StatusText.Text = sessionModded ? "Partie en cours en mode Lanppart3." : "Partie en cours en mode normal.";
            else if (running)
                StatusText.Text = "Final Sentence est déjà lancé. Quittez-le pour changer de mode.";
            else if (updater.State == UpdateState.Checking)
                StatusText.Text = "Recherche de mises à jour…";
            else if (updater.State == UpdateState.Available && !updating)
                StatusText.Text = "Une nouvelle version du lanceur est disponible (" + updater.LatestTag + ") : installez-la pour jouer.";
            else if (StatusText.Text.StartsWith("Recherche de mises à jour"))
                StatusText.Text = "Choisissez un mode.";
            else if (StatusText.Text.Length == 0)
                StatusText.Text = "Choisissez un mode.";
        }

        void WatchGame()
        {
            bool running = PlayerGameRunning();
            if (session == Session.Starting)
            {
                if (running) session = Session.Running;
                else if (DateTime.Now - sessionStart > StartTimeout) EndSession("Le jeu ne s'est pas lancé.");
            }
            else if (session == Session.Running && !running)
            {
                EndSession(sessionModded ? "Partie terminée." : "Partie terminée.");
            }

            servers.Refresh();
            foreach (var s in servers.Servers.Where(s => s.Running && s.Phase != s.LastPhase))
            {
                s.LastPhase = s.Phase;
                if (s.Phase == "waiting") StatusText.Text = s.Settings.Name + " attend les joueurs.";
                else if (s.Phase == "playing") StatusText.Text = "Partie en cours sur " + s.Settings.Name + ".";
            }
            var live = servers.Servers.Where(s => s.Running && s.Status.Count > 0).ToList();
            if (live.Count > 0) beacons.Send(live);
            WriteServersForGame();
            if (DateTime.Now - lastUpdateCheck > TimeSpan.FromMinutes(30)) CheckForUpdate();
            refreshPage?.Invoke();
            RefreshState();
        }

        // The servers heard, for the game's own server list (mod: browser.lua). First line: the time written, so
        // the game can tell a list from a closed launcher.
        void WriteServersForGame()
        {
            if (listener == null) return;
            var lines = new List<string> { DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() };
            foreach (var s in listener.Current())
                lines.Add(string.Join("	", s.Address, s.Name.Replace("	", " "), s.ModeText, s.RulesText, s.Players, s.Capacity,
                                      s.Phase, s.Roulette ? 1 : 0));
            try { File.WriteAllLines(Path.Combine(AppData.Root, "servers_seen.txt"), lines); }
            catch (IOException) { }
        }

        void EndSession(string message)
        {
            session = Session.None;
            sessionModded = false;
            StatusText.Text = message;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        }

        // join: a server to connect to once in the main menu (address:port); lives: that server plays with lives.
        void Launch(bool modded, string join = null, bool lives = false)
        {
            if (!CanPlay || PlayerGameRunning() || (modded && !ModReady)) { RefreshState(); return; }
            if (modded && !GameInstall.IsSteamRunning())
            {
                StatusText.Text = "Ouvrez Steam d'abord : le jeu en a besoin, même en mode Lanppart3.";
                return;
            }
            try
            {
                if (!modded)
                {
                    install.Launch();
                }
                else
                {
                    var orders = new Dictionary<string, string> { ["LANPPART3_ID"] = "player" };
                    if (join != null)
                    {
                        orders["LANPPART3_JOIN"] = join;
                        orders["LANPPART3_LIVES"] = lives ? "1" : "0";
                    }
                    install.LaunchModded("", orders, false);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Le jeu n'a pas pu être lancé : " + ex.Message;
                return;
            }
            session = Session.Starting;
            sessionModded = modded;
            sessionStart = DateTime.Now;
            RefreshState();
            WindowState = WindowState.Minimized;
        }

        // ---- Actions ----

        void OnLaunchNormal(object sender, RoutedEventArgs e) => Launch(false);

        void OnLaunchLanppart(object sender, RoutedEventArgs e) => Launch(true);

        void OnGameFolder(object sender, RoutedEventArgs e)
        {
            if (install != null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + install.Root + "\"") { UseShellExecute = true });
                return;
            }
            var dialog = new OpenFileDialog
            {
                Title = "Dossier de Final Sentence : choisissez TheEnd.exe",
                Filter = "Final Sentence (TheEnd.exe)|TheEnd.exe",
            };
            if (dialog.ShowDialog(this) != true) return;
            var found = GameInstall.Find(Path.GetDirectoryName(dialog.FileName));
            if (found == null)
            {
                StatusText.Text = "Ce dossier ne contient pas Final Sentence.";
                return;
            }
            install = found;
            StatusText.Text = "";
            RefreshState();
        }

        void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        void OnQuit(object sender, RoutedEventArgs e) => Close();

        // Drags the borderless window from its background (not from buttons, fields, lists...).
        void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            for (var d = e.OriginalSource as DependencyObject; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Control && !(d is Window)) return;
                if (!(d is System.Windows.Media.Visual)) break;
            }
            DragMove();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            var running = servers.Servers.Where(s => s.Running).ToList();
            if (running.Count > 0 && !closingForUpdate)
            {
                var answer = MessageBox.Show(this,
                    running.Count + " serveur(s) Lanppart3 tourne(nt) encore.\n\n" +
                    "Oui : les arrêter et quitter.\nNon : les laisser tourner (ils ne seront plus visibles dans la liste " +
                    "des serveurs ; on peut encore les rejoindre par leur adresse).\nAnnuler : ne pas quitter.",
                    "Lanppart3 Final Sentence", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (answer == MessageBoxResult.Yes) foreach (var s in running) servers.Stop(s);
            }
            listener?.Dispose();
            beacons.Dispose();
        }
    }
}
