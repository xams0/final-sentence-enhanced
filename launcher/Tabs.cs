using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Lanppart3Launcher
{
    // The window's tabs (JOUER, SERVEUR, INFOS): each has its own menu on the right, and the menu's
    // pages open on the left.
    public partial class MainWindow
    {
        enum Tab { Play, Server, Info }

        // A menu entry: opens a page (on the left) or does something (start the game).
        sealed class Entry
        {
            public string Key, Title, Subtitle;
            public Action Run;
            public bool IsPage = true;
        }

        sealed class Group
        {
            public string Caption;
            public Entry[] Entries;
        }

        Tab tab;
        readonly Dictionary<Tab, string> lastPage = new Dictionary<Tab, string>();
        readonly Dictionary<string, Button> menuButtons = new Dictionary<string, Button>();

        Group[] MenuOf(Tab t)
        {
            switch (t)
            {
                case Tab.Play:
                    return new[]
                    {
                        new Group { Caption = "Lancer le jeu", Entries = new[]
                        {
                            new Entry { Key = "lanppart", Title = "Mode Lanppart3", IsPage = false, Run = () => Launch(true),
                                        Subtitle = "Avec le mod : lobbys entre amis, vies, presets" },
                            new Entry { Key = "normal", Title = "Mode normal", IsPage = false, Run = () => Launch(false),
                                        Subtitle = "Le jeu d'origine, serveurs officiels" },
                        } },
                        new Group { Caption = "Serveurs Lanppart3", Entries = new[]
                        {
                            new Entry { Key = "browser", Title = "Rechercher un serveur", Run = ShowBrowserPage,
                                        Subtitle = "Réseau local et Radmin VPN" },
                        } },
                    };
                case Tab.Server:
                    return new[]
                    {
                        new Group { Caption = "Organiser", Entries = new[]
                        {
                            new Entry { Key = "servers", Title = "Mes serveurs", Run = ShowServersPage,
                                        Subtitle = "Héberger, régler, lancer les parties" },
                            new Entry { Key = "presets", Title = "Presets Lanppart", Run = ShowPresetsPage,
                                        Subtitle = "Règles des Poules et des Finales" },
                            new Entry { Key = "texts", Title = "Textes personnalisés", Run = () => ShowTextsPage(null),
                                        Subtitle = "Vos textes, gardés sur ce PC" },
                        } },
                    };
                default:
                    return new[]
                    {
                        new Group { Caption = "Comprendre", Entries = new[]
                        {
                            new Entry { Key = "why", Title = "Pourquoi deux modes ?", Run = () => ShowInfoPage("why") },
                            new Entry { Key = "howto", Title = "Jouer en Lanppart3", Run = () => ShowInfoPage("howto") },
                            new Entry { Key = "hosting", Title = "Organiser une partie", Run = () => ShowInfoPage("hosting") },
                        } },
                        new Group { Caption = "Installation", Entries = new[]
                        {
                            new Entry { Key = "folder", Title = "Dossier du jeu", Run = () => ShowInfoPage("folder") },
                            new Entry { Key = "about", Title = "Version", Run = () => ShowInfoPage("about") },
                        } },
                    };
            }
        }

        static readonly Dictionary<Tab, string[]> TabHeaders = new Dictionary<Tab, string[]>
        {
            [Tab.Play] = new[] { "Jouer", "Lancer le jeu ou rejoindre un serveur Lanppart3." },
            [Tab.Server] = new[] { "Serveur", "Organiser des parties : serveurs dédiés, règles et textes." },
            [Tab.Info] = new[] { "Infos", "Comment marche Lanppart3, et l'installation." },
        };

        static readonly Dictionary<Tab, string> DefaultPage = new Dictionary<Tab, string>
        {
            [Tab.Play] = "browser", [Tab.Server] = "servers", [Tab.Info] = "why",
        };

        void OnTabPlay(object sender, RoutedEventArgs e) => SelectTab(Tab.Play);
        void OnTabServer(object sender, RoutedEventArgs e) => SelectTab(Tab.Server);
        void OnTabInfo(object sender, RoutedEventArgs e) => SelectTab(Tab.Info);

        void SelectTab(Tab t, string pageKey = null)
        {
            tab = t;
            TabPlay.Tag = t == Tab.Play ? "Selected" : null;
            TabServer.Tag = t == Tab.Server ? "Selected" : null;
            TabInfo.Tag = t == Tab.Info ? "Selected" : null;
            MenuTitle.Text = TabHeaders[t][0];
            MenuText.Text = Typo(TabHeaders[t][1]);

            MenuHost.Children.Clear();
            menuButtons.Clear();
            var groups = MenuOf(t);
            Entry first = null;
            for (int g = 0; g < groups.Length; g++)
            {
                if (g > 0) MenuHost.Children.Add(new System.Windows.Shapes.Rectangle { Style = StyleRes("HLine"), Margin = new Thickness(0, 10, 0, 6) });
                MenuHost.Children.Add(new TextBlock { Text = groups[g].Caption, Style = (Style)Application.Current.Resources["SectionCaption"] });
                foreach (var entry in groups[g].Entries)
                {
                    var en = entry;
                    var content = new StackPanel();
                    content.Children.Add(new TextBlock { Text = en.Title });
                    if (en.Subtitle != null)
                        content.Children.Add(new TextBlock { Text = Typo(en.Subtitle), FontSize = 14, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                    var b = new Button { Style = StyleRes("MenuEntry"), Content = content };
                    b.Click += (o, e) => en.Run();
                    MenuHost.Children.Add(b);
                    menuButtons[en.Key] = b;
                    if (en.IsPage && first == null) first = en;
                }
            }
            var key = pageKey ?? (lastPage.TryGetValue(t, out var last) ? last : DefaultPage[t]);
            var open = groups.SelectMany(g => g.Entries).FirstOrDefault(en => en.Key == key && en.IsPage) ?? first;
            RefreshState();
            open?.Run();
        }

        // Called by every page: the page's menu entry stays framed.
        void MarkPage(string key)
        {
            lastPage[tab] = key;
            foreach (var kv in menuButtons) kv.Value.Tag = kv.Key == key ? "Selected" : null;
        }

        // Opens a page of another tab (e.g. "Modifier ce preset" from a server).
        void GoTo(Tab t, string key)
        {
            if (tab != t) SelectTab(t, key);
            else MenuOf(t).SelectMany(g => g.Entries).First(en => en.Key == key).Run();
        }

        // ---------------------------------------------------------------- Info pages

        void ShowInfoPage(string key)
        {
            var body = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
            void Para(string text) => body.Children.Add(new TextBlock
            {
                Text = Typo(text), FontSize = 18, Foreground = Res("CreamDim"), TextWrapping = TextWrapping.Wrap,
                LineHeight = 27, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 0, 0, 14),
            });
            void Caption(string text)
            {
                if (body.Children.Count > 0)
                    body.Children.Add(new System.Windows.Shapes.Rectangle { Style = StyleRes("HLine"), Margin = new Thickness(0, 4, 0, 12) });
                body.Children.Add(new TextBlock { Text = text, Style = (Style)Application.Current.Resources["SectionCaption"], Margin = new Thickness(0, 0, 0, 8) });
            }
            string title;
            switch (key)
            {
                case "why":
                    title = "Pourquoi deux modes ?";
                    Para("En ligne, ce sont les serveurs officiels qui appliquent les règles : impossible d'y ajouter des vies " +
                         "ou d'enlever la roulette du Duel. Le mod fait donc jouer un PC du groupe comme serveur : celui de " +
                         "l'hôte du lobby, ou un serveur Lanppart3 dédié.");
                    Para("Pour que vos parties habituelles restent intactes, le mod ne s'active que lorsque le jeu est lancé " +
                         "par ce lanceur en mode Lanppart3. Lancé depuis Steam ou en mode normal, c'est le jeu original.");
                    Caption("Mode normal");
                    Para("Lance Final Sentence tel quel, comme depuis Steam : parties rapides, classements, ligues et lobbys " +
                         "utilisent les serveurs officiels. À choisir pour jouer en ligne avec tout le monde.");
                    Caption("Mode Lanppart3");
                    Para("Lance le jeu avec le mod. Toute partie lancée depuis un lobby (Partie personnalisée, Créer nouveau) " +
                         "est hébergée par le PC de l'hôte du lobby au lieu des serveurs officiels. Les règles deviennent " +
                         "libres : roulette russe ou vies dans tous les modes, presets Lanppart Poules et Lanppart Finales. " +
                         "Tous les joueurs du lobby doivent lancer le jeu dans ce mode.");
                    break;
                case "howto":
                    title = "Jouer en Lanppart3";
                    Caption("Avec un serveur dédié");
                    Para("L'organisateur ouvre SERVEUR › Mes serveurs, règle la partie et démarre le serveur. Les joueurs " +
                         "ouvrent JOUER › Rechercher un serveur et choisissent le serveur : le jeu démarre et s'y connecte tout " +
                         "seul. Quand tout le monde est là, l'organisateur clique « Lancer la partie ».");
                    Caption("Sans serveur, depuis un lobby");
                    Para("Tout le monde lance le jeu en mode Lanppart3. L'hôte ouvre « Partie personnalisée », les autres " +
                         "rejoignent son lobby et tout le monde clique « Prêt(e) ». Décocher « Jouer à la roulette russe » " +
                         "fait apparaître la ligne Vies.");
                    Caption("Les vies");
                    Para("Chaque fois que le jeu déclencherait la roulette ou une élimination, le joueur perd une vie ; il " +
                         "n'est éliminé qu'à 0. En K.O., le texte est rallongé (avec les autres textes choisis, ou répété) " +
                         "pour que la partie dure jusqu'au dernier survivant.");
                    break;
                case "hosting":
                    title = "Organiser une partie";
                    Caption("Serveurs dédiés");
                    Para("Un serveur Lanppart3 est une instance du jeu sans fenêtre ni joueur, sur ce PC, qui héberge les " +
                         "parties : personne n'y a l'avantage d'un ping nul. Vous choisissez le mode, les règles, la langue et " +
                         "les textes ; les joueurs le trouvent dans leur liste (réseau local et Radmin VPN). Plusieurs " +
                         "serveurs peuvent tourner en même temps, un port chacun. Steam doit être ouvert.");
                    Para("Le serveur reste actif tant que le lanceur est ouvert. « Appliquer les réglages » vaut dès la " +
                         "prochaine partie ; « Nouvelle partie » en ouvre une tout de suite.");
                    Caption("Presets Lanppart");
                    Para("« Lanppart Poules » et « Lanppart Finales » apparaissent dans « Mode de jeu » des lobbys en mode " +
                         "Lanppart3 et dans les modes des serveurs. Le jeu les relit à chaque lancement : après un changement, " +
                         "relancez-le. Ce sont les presets de l'hôte du lobby qui comptent.");
                    Caption("Textes personnalisés");
                    Para("Ils restent sur le PC du serveur : les joueurs les découvrent au début de la partie, impossible de " +
                         "s'entraîner avant. Une ligne *** sépare les manches.");
                    break;
                case "folder":
                    title = "Dossier du jeu";
                    Para(install == null
                        ? "Final Sentence n'a pas été trouvé. Indiquez son dossier (celui qui contient TheEnd.exe)."
                        : install.Root);
                    var folder = Action(install == null ? "Choisir le dossier du jeu" : "Ouvrir le dossier", (o, e) => OnGameFolder());
                    folder.HorizontalAlignment = HorizontalAlignment.Left;
                    body.Children.Add(folder);
                    if (install != null)
                    {
                        var other = Action("Choisir un autre dossier", (o, e) => PickGameFolder());
                        other.HorizontalAlignment = HorizontalAlignment.Left;
                        other.Margin = new Thickness(0, 10, 0, 0);
                        body.Children.Add(other);
                    }
                    break;
                default:
                    title = "Version";
                    Para("Lanceur et mod Lanppart3 v" + Updater.CurrentText + ".");
                    Para(updater.State == UpdateState.Available ? "Une nouvelle version est disponible : " + updater.LatestTag + "."
                       : updater.State == UpdateState.Unreachable ? "Mises à jour non vérifiées : GitHub injoignable."
                       : updater.State == UpdateState.Checking ? "Recherche de mises à jour…"
                       : "C'est la dernière version.");
                    Para("Lanppart3 est un mod non officiel pour Final Sentence. Les mises à jour viennent de " +
                         "github.com/xams0/final-sentence-enhanced ; le lanceur les propose tout seul.");
                    break;
            }
            ShowPage(key, title, new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, null);
        }

        void OnGameFolder()
        {
            if (install == null) { PickGameFolder(); return; }
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + install.Root + "\"") { UseShellExecute = true });
        }

        void PickGameFolder()
        {
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
            if (tab == Tab.Info) ShowInfoPage("folder");
        }

        // ---------------------------------------------------------------- Window size

        // The layout is drawn at 1440 x 840 and scaled: open at most of the screen, keeping that shape.
        void SizeToScreen()
        {
            var area = SystemParameters.WorkArea;
            double ratio = 1440.0 / 840.0;
            double width = Math.Min(area.Width * 0.86, area.Height * 0.86 * ratio);
            Width = Math.Max(MinWidth, width);
            Height = Width / ratio;
        }

        void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximized();

        void ToggleMaximized()
        {
            // (Borderless: keep the taskbar visible when maximized.)
            MaxHeight = SystemParameters.WorkArea.Height + 12;
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurer" : "Agrandir";
        }
    }
}
