using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lanppart3Launcher
{
    // The launcher's pages: dedicated servers, server browser, custom texts.
    public partial class MainWindow
    {
        enum Page { None, Servers, Browser, Texts, Presets }

        static readonly string[] ModeNames = { "Battle royale", "K.O.", "Duel" };
        static readonly string[] Difficulties = { "Très facile", "Facile", "Moyen", "Difficile", "Expert" };
        static readonly string[] MistakeBehaviours = { "Retour en arrière", "Bloquer", "Passer" };
        static readonly string[] TextSources = { "Tous les textes du jeu", "Textes personnalisés", "Jeu + personnalisés", "Sélection…" };
        static readonly string[] DefaultLanguages = { "fr", "en", "de", "es", "pt", "ru" };

        Page page = Page.None;
        ServerInstance selectedServer;
        Action refreshPage;   // updates the live parts of the current page

        static Brush Res(string key) => (Brush)Application.Current.Resources[key];
        static Style StyleRes(string key) => (Style)Application.Current.Resources[key];

        void ShowPage(Page p, string title, UIElement content, Action refresh)
        {
            page = p;
            PageTitle.Text = title;
            PageHost.Content = content;
            refreshPage = refresh;
            InfoPanel.Visibility = Visibility.Collapsed;
            PagePanel.Visibility = Visibility.Visible;
            PageDimmer.Visibility = Visibility.Visible;
            refresh?.Invoke();
        }

        void OnClosePage(object sender, RoutedEventArgs e)
        {
            page = Page.None;
            refreshPage = null;
            PageHost.Content = null;
            PagePanel.Visibility = Visibility.Collapsed;
            PageDimmer.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility = Visibility.Visible;
        }

        static TextBlock Label(string text, double size, string brush, Thickness margin = default(Thickness)) =>
            new TextBlock { Text = text, FontSize = size, Foreground = Res(brush), TextWrapping = TextWrapping.Wrap, Margin = margin };

        static Button Action(string text, RoutedEventHandler click, bool enabled = true)
        {
            var b = new Button { Content = text, Style = StyleRes("ActionButton"), IsEnabled = enabled };
            b.Click += click;
            return b;
        }

        List<string> Languages()
        {
            var langs = TextLibrary.LoadCatalog().Select(t => t.Language)
                .Concat(TextLibrary.LoadCustom().Select(t => t.Language)).Distinct().ToList();
            foreach (var l in DefaultLanguages) if (!langs.Contains(l)) langs.Add(l);
            return langs;
        }

        // ---------------------------------------------------------------- Servers

        void OnShowServers(object sender, RoutedEventArgs e) => ShowServersPage();

        void OnPointServers(object sender, EventArgs e) => ShowInfo("Héberger un serveur",
            "Lance un serveur Lanppart3 sur ce PC : une instance du jeu sans fenêtre ni joueur qui héberge les parties. " +
            "Personne n'y a l'avantage d'un ping nul.\n\n" +
            "Vous choisissez le mode, les règles, la langue et les textes ; les joueurs le trouvent dans « Rejoindre un " +
            "serveur » (réseau local et Radmin VPN) puis vous lancez la partie quand tout le monde est là.\n\n" +
            "Plusieurs serveurs peuvent tourner en même temps (un port chacun). Steam doit être ouvert.");

        void ShowServersPage()
        {
            if (selectedServer == null || !servers.Servers.Contains(selectedServer))
                selectedServer = servers.Servers.FirstOrDefault() ?? servers.Create();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Server tabs
            var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var s in servers.Servers)
            {
                var srv = s;
                var tab = new Button
                {
                    Content = (srv.Running ? "● " : "") + srv.Settings.Name, FontSize = 16, Margin = new Thickness(0, 0, 14, 0),
                    Style = StyleRes("PlainButton"), Tag = srv == selectedServer ? "Selected" : null,
                };
                tab.Click += (o, a) => { selectedServer = srv; ShowServersPage(); };
                tabs.Children.Add(tab);
            }
            var add = new Button { Content = "+ Nouveau serveur", FontSize = 16, Style = StyleRes("PlainButton") };
            add.Click += (o, a) => { selectedServer = servers.Create(); ShowServersPage(); };
            tabs.Children.Add(add);
            root.Children.Add(tabs);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var form = new StackPanel { Margin = new Thickness(0, 0, 18, 10) };
            var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            body.Children.Add(scroll);
            BuildServerForm(form, selectedServer);

            var side = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            Grid.SetColumn(side, 1);
            body.Children.Add(side);

            ShowPage(Page.Servers, "Héberger un serveur", root, () => RefreshServerSide(side, selectedServer));
        }

        void SaveSettings(ServerInstance server) => server.Settings.Save(server.SettingsPath);

        void BuildServerForm(StackPanel form, ServerInstance server)
        {
            var s = server.Settings;
            var o = new OptionsBuilder(form);
            Action rebuild = () => { form.Children.Clear(); BuildServerForm(form, server); };

            o.Section("Serveur");
            var name = o.Row("Nom du serveur", OptionsBuilder.Text(s.Name));
            name.LostFocus += (x, e) => { s.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : s.Name; SaveSettings(server); };
            var port = o.Row("Port", new Stepper(s.Port, 7777, 7999), "Port UDP du serveur (un par serveur).");
            port.ValueChanged += v => { s.Port = v; SaveSettings(server); };

            o.Section("Partie");
            // The game's modes, then the lobby presets (their rules, kept up to date with the presets page).
            var modes = ModeNames.Concat(Enumerable.Range(0, PresetStore.Count).Select(i => PresetStore.Load(i).Name)).ToList();
            var mode = o.Row("Mode de jeu", OptionsBuilder.Combo(modes, s.Preset >= 0 ? ModeNames.Length + s.Preset : (int)s.Mode));
            mode.SelectionChanged += (x, e) =>
            {
                if (mode.SelectedIndex >= ModeNames.Length) s.Preset = mode.SelectedIndex - ModeNames.Length;
                else { s.Preset = -1; s.Mode = (GameMode)mode.SelectedIndex; }
                SaveSettings(server);
                rebuild();
            };
            var rules = s.Effective();

            o.Section("Textes");
            var langs = Languages();
            var lang = o.Row("Langue", OptionsBuilder.Combo(langs, Math.Max(0, langs.IndexOf(s.Language))));
            lang.SelectionChanged += (x, e) => { s.Language = (string)lang.SelectedItem; SaveSettings(server); rebuild(); };
            var source = o.Row("Sélection des textes", OptionsBuilder.Combo(TextSources, (int)s.Texts));
            source.SelectionChanged += (x, e) => { s.Texts = (TextSource)source.SelectedIndex; SaveSettings(server); rebuild(); };
            var catalog = TextLibrary.LoadCatalog().Where(t => t.Language == s.Language && t.Fits(rules.Mode)).ToList();
            var custom = TextLibrary.LoadCustom().Where(t => t.Language == s.Language).ToList();
            if (s.Texts == TextSource.Selection) form.Children.Add(TextPicker(server, catalog, custom));
            int pool = s.Texts == TextSource.GameTexts ? catalog.Count : s.Texts == TextSource.CustomTexts ? custom.Count :
                       s.Texts == TextSource.Both ? catalog.Count + custom.Count : s.SelectedTextIds.Count + s.SelectedCustomTexts.Count;
            o.Note(pool + " texte(s) dans la rotation, tirés au hasard sans répétition." +
                   (catalog.Count == 0 && s.Texts != TextSource.CustomTexts
                       ? " La liste des textes du jeu apparaît après un premier lancement en mode Lanppart3 ou d'un serveur." : ""));

            if (s.Preset < 0)
            {
                BuildRules(o, s, () => SaveSettings(server), rebuild);
                return;
            }
            o.Section("Règles");
            o.Note("Règles du preset « " + PresetStore.Load(s.Preset).Name + " » : " +
                   ModeNames[(int)rules.Mode] + ", " + (rules.Roulette ? "roulette" : rules.Lives + " vies") + ", bots : " +
                   (rules.FillWithBots ? "compléter les places" : rules.Bots.ToString()) + ". Elles se modifient dans « Presets Lanppart ».");
            int preset = s.Preset;
            var edit = Action("Modifier ce preset", (x, e) => { selectedPreset = preset; ShowPresetsPage(); });
            edit.HorizontalAlignment = HorizontalAlignment.Left;
            edit.Margin = new Thickness(18, 6, 0, 0);
            form.Children.Add(edit);
        }

        // Bots, mistakes, roulette or lives, durations: the rules of a server or of a lobby preset.
        void BuildRules(OptionsBuilder o, ServerSettings s, Action save, Action rebuild)
        {
            o.Section("Bots");
            int capacity = ServerSettings.Capacity(s.Mode);
            var botsMode = o.Row("Bots", OptionsBuilder.Combo(new[] { "Compléter les places", "Nombre fixe" }, s.FillWithBots ? 0 : 1));
            botsMode.SelectionChanged += (x, e) => { s.FillWithBots = botsMode.SelectedIndex == 0; save(); rebuild(); };
            if (!s.FillWithBots)
            {
                var bots = o.Row("Nombre de bots", new Stepper(Math.Min(s.Bots, capacity - 1), 0, capacity - 1));
                bots.ValueChanged += v => { s.Bots = v; save(); };
            }
            var diff = o.Row("Difficulté des bots", OptionsBuilder.Combo(Difficulties, s.BotsDifficulty));
            diff.SelectionChanged += (x, e) => { s.BotsDifficulty = diff.SelectedIndex; save(); };
            o.Note(s.FillWithBots ? "Au lancement, les places libres (" + capacity + " au total) sont prises par des bots."
                                  : "Places : " + capacity + " au total.");

            o.Section("Fautes");
            var behaviour = o.Row("Après une faute", OptionsBuilder.Combo(MistakeBehaviours, s.MistakeBehaviour));
            behaviour.SelectionChanged += (x, e) => { s.MistakeBehaviour = behaviour.SelectedIndex; save(); };
            var caps = o.Row("MAJ prises en compte", OptionsBuilder.Check(s.CapitalsMatter));
            caps.Click += (x, e) => { s.CapitalsMatter = caps.IsChecked == true; save(); };
            if (s.Mode == GameMode.BattleRoyale)
            {
                var mistakes = o.Row("Fautes max. par manche", new Stepper(s.MaxMistakesPerRound, 1, 10),
                                     "Atteindre ce nombre de fautes dans une manche déclenche la roulette (ou coûte une vie).");
                mistakes.ValueChanged += v => { s.MaxMistakesPerRound = v; save(); };
            }

            o.Section("Roulette");
            var roulette = o.Row("Jouer à la roulette russe", OptionsBuilder.Check(s.Roulette));
            roulette.Click += (x, e) => { s.Roulette = roulette.IsChecked == true; save(); rebuild(); };
            if (s.Roulette)
            {
                var slots = o.Row("Coups du barillet", new Stepper(s.BulletSlots, 1, 6));
                var initial = o.Row("Balles initiales", new Stepper(Math.Min(s.InitialBullets, s.BulletSlots - 1), 0, s.BulletSlots - 1));
                slots.ValueChanged += v => { s.BulletSlots = v; initial.Max = v - 1; initial.Value = initial.Value; save(); };
                initial.ValueChanged += v => { s.InitialBullets = v; save(); };
            }
            else
            {
                var lives = o.Row("Vies", new Stepper(s.Lives, 1, 6),
                                  "Chaque fois que le jeu déclencherait la roulette ou une élimination, le joueur perd une vie.");
                lives.ValueChanged += v => { s.Lives = v; save(); };
            }

            o.Section("Durée");
            var round = o.Row("Durée de la manche", new Stepper(s.RoundDuration, 30, 900, 10, Stepper.Duration));
            round.ValueChanged += v => { s.RoundDuration = v; save(); };
            if (s.Mode == GameMode.BattleRoyale)
            {
                var pause = o.Row("Durée de la pause", new Stepper(s.PauseDuration, 0, 60, 5, Stepper.Duration));
                pause.ValueChanged += v => { s.PauseDuration = v; save(); };
            }
        }

        // Check list of the language's texts, for "Sélection…".
        UIElement TextPicker(ServerInstance server, List<GameText> catalog, List<CustomText> custom)
        {
            var s = server.Settings;
            var list = new StackPanel();
            void Add(string title, string detail, bool isChecked, Action<bool> toggled)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var check = OptionsBuilder.Check(isChecked);
                System.Windows.Automation.AutomationProperties.SetName(check, title);
                check.Checked += (x, e) => toggled(true);
                check.Unchecked += (x, e) => toggled(false);
                row.Children.Add(check);
                var label = Label(title, 15, "Cream", new Thickness(10, 3, 0, 0));
                label.Cursor = System.Windows.Input.Cursors.Hand;
                label.MouseLeftButtonUp += (x, e) => check.IsChecked = check.IsChecked != true;  // the title ticks it too
                row.Children.Add(label);
                row.Children.Add(Label(detail, 13, "Muted", new Thickness(10, 5, 0, 0)));
                list.Children.Add(row);
            }
            foreach (var t in custom)
                Add(t.Title, "personnalisé · " + t.Rounds + " manches", s.SelectedCustomTexts.Contains(t.File), on =>
                {
                    s.SelectedCustomTexts.Remove(t.File);
                    if (on) s.SelectedCustomTexts.Add(t.File);
                    SaveSettings(server);
                });
            foreach (var t in catalog)
                Add(t.Title, "jeu · difficulté " + t.Difficulty, s.SelectedTextIds.Contains(t.Id), on =>
                {
                    s.SelectedTextIds.Remove(t.Id);
                    if (on) s.SelectedTextIds.Add(t.Id);
                    SaveSettings(server);
                });
            if (list.Children.Count == 0) list.Children.Add(Label("Aucun texte pour cette langue et ce mode.", 15, "Muted"));
            return new Border
            {
                BorderBrush = Res("FieldOrange"), BorderThickness = new Thickness(1), Margin = new Thickness(18, 4, 0, 4),
                Padding = new Thickness(10, 6, 6, 6), Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
                Child = new ScrollViewer { Content = list, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
        }

        void RefreshServerSide(StackPanel side, ServerInstance server)
        {
            var s = server.Settings;
            var rules = s.Effective();
            var key = string.Join("|", server.Running, server.Phase, server.PlayerCount, string.Join(",", server.Players), s.Name,
                                  rules.Mode, rules.Roulette, rules.Lives, s.Port, s.Preset, ModReady);
            if (Equals(side.Tag, key)) return;  // unchanged: keep the buttons as they are (hover, clicks)
            side.Tag = key;
            side.Children.Clear();
            side.Children.Add(Label(s.Name, 22, "Cream"));
            side.Children.Add(Label(server.PhaseText, 17, server.Running ? "CheckGreen" : "Muted", new Thickness(0, 4, 0, 0)));
            int capacity = ServerSettings.Capacity(rules.Mode);
            side.Children.Add(Label((s.Preset >= 0 ? PresetStore.Load(s.Preset).Name + " · " : "") + ModeNames[(int)rules.Mode] +
                                    " · " + (rules.Roulette ? "roulette" : rules.Lives + " vies"), 15, "OptionLabel",
                                    new Thickness(0, 4, 0, 10)));
            if (server.Running)
            {
                side.Children.Add(Label(server.PlayerCount + " / " + capacity + " joueurs", 17, "OptionCaption"));
                foreach (var p in server.Players) side.Children.Add(Label("  " + p, 15, "CreamDim"));
                side.Children.Add(Label("Adresses pour rejoindre :", 14, "Muted", new Thickness(0, 12, 0, 2)));
                foreach (var ip in Discovery.LocalAddresses())
                    side.Children.Add(new TextBox
                    {
                        Text = ip + ":" + s.Port, IsReadOnly = true, FontSize = 14, Foreground = Res("CreamDim"),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                    });
                side.Children.Add(new Border { Height = 14 });
                var waiting = server.Phase == "waiting";
                side.Children.Add(Action("Lancer la partie", (o, e) => { server.Send("start"); StatusText.Text = "Partie lancée sur " + s.Name + "."; },
                                         waiting && server.PlayerCount > 0));
                side.Children.Add(Action("Nouvelle partie", (o, e) => { servers.Apply(server); server.Send("next"); }, server.Phase != "boot"));
                side.Children.Add(Action("Appliquer les réglages", (o, e) =>
                {
                    servers.Apply(server);
                    StatusText.Text = "Réglages appliqués : ils valent dès la prochaine partie de " + s.Name + ".";
                }));
                side.Children.Add(Action("Arrêter le serveur", (o, e) => { servers.Stop(server); RefreshState(); }));
            }
            else
            {
                side.Children.Add(Action("Démarrer le serveur", (o, e) => StartServer(server), ModReady));
                side.Children.Add(Action("Supprimer ce serveur", (o, e) =>
                {
                    servers.Remove(server);
                    selectedServer = null;
                    ShowServersPage();
                }));
                side.Children.Add(Label("Le serveur tourne sans fenêtre. Il reste actif tant que le lanceur est ouvert " +
                                        "(sinon il n'est plus visible dans la liste des serveurs).", 13, "Muted", new Thickness(0, 10, 0, 0)));
            }
        }

        void StartServer(ServerInstance server)
        {
            if (servers.Servers.Any(o => o != server && o.Running && o.Settings.Port == server.Settings.Port))
            {
                StatusText.Text = "Le port " + server.Settings.Port + " est déjà pris par un autre serveur.";
                return;
            }
            try
            {
                servers.Start(server, install);
                StatusText.Text = "Démarrage de " + server.Settings.Name + " (environ 30 secondes)…";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Le serveur n'a pas pu démarrer : " + ex.Message;
            }
            RefreshState();
        }

        // ---------------------------------------------------------------- Browser

        void OnShowBrowser(object sender, RoutedEventArgs e) => ShowBrowserPage();

        void OnPointBrowser(object sender, EventArgs e) => ShowInfo("Rejoindre un serveur",
            "Liste les serveurs Lanppart3 du réseau local et de Radmin VPN, avec leur mode, leurs règles et leurs " +
            "joueurs. Choisissez-en un : le jeu démarre en mode Lanppart3 et s'y connecte tout seul.\n\n" +
            "Un serveur n'apparaît pas ? Entrez son adresse (IP:port) à la main.");

        string manualAddress = "";

        void ShowBrowserPage()
        {
            var root = new DockPanel();
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            var manual = new StackPanel { Orientation = Orientation.Horizontal };
            manual.Children.Add(Label("Adresse du serveur :", 17, "OptionLabel", new Thickness(0, 8, 14, 0)));
            var addr = OptionsBuilder.Text(manualAddress, 260);
            addr.TextChanged += (o, e) => manualAddress = addr.Text;
            manual.Children.Add(addr);
            var join = Action("Rejoindre", (o, e) => JoinServer(NormalizeAddress(addr.Text), null));
            join.Margin = new Thickness(14, 0, 0, 0);
            manual.Children.Add(join);
            bottom.Children.Add(manual);
            bottom.Children.Add(Label("Exemple : 26.12.34.56:7777 (adresse Radmin VPN de l'organisateur) ou 192.168.1.20:7777.", 13, "Muted",
                                      new Thickness(0, 6, 0, 0)));

            var list = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            string lastKey = null;
            ShowPage(Page.Browser, "Rejoindre un serveur", root, () =>
            {
                var current = listener?.Current() ?? new List<ServerInfo>();
                var key = string.Join(";", current.Select(c => c.Address + c.Players + c.Phase + c.Name + c.Lives + c.Roulette));
                if (key == lastKey) return;
                lastKey = key;
                list.Children.Clear();
                if (current.Count == 0)
                    list.Children.Add(Label("Recherche des serveurs sur le réseau local et Radmin VPN…", 17, "CreamDim"));
                foreach (var info in current) list.Children.Add(ServerRow(info));
            });
        }

        static string NormalizeAddress(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            return text.Contains(":") ? text : text + ":7777";
        }

        UIElement ServerRow(ServerInfo info)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Label(info.Name, 21, "Cream"));
            text.Children.Add(Label(info.ModeText + " · " + info.RulesText + " · " + info.Players + " / " + info.Capacity + " joueurs · " +
                                    info.PhaseText, 15, "OptionLabel", new Thickness(0, 2, 0, 0)));
            text.Children.Add(Label(info.Address, 13, "Muted", new Thickness(0, 2, 0, 0)));
            grid.Children.Add(text);
            var join = Action("Rejoindre", (o, e) => JoinServer(info.Address, info),
                              info.Phase == "waiting" && info.Players < info.Capacity);
            join.VerticalAlignment = VerticalAlignment.Center;
            join.ToolTip = info.Phase == "waiting" ? null : "Partie en cours : attendez la suivante.";
            Grid.SetColumn(join, 1);
            grid.Children.Add(join);
            return new Border
            {
                Child = grid, BorderBrush = Res("Frame"), BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 4, 4, 10), Margin = new Thickness(0, 0, 0, 8),
            };
        }

        void JoinServer(string address, ServerInfo info)
        {
            if (address == null) { StatusText.Text = "Entrez l'adresse du serveur (IP:port)."; return; }
            bool lives = info != null && !info.Roulette;
            Launch(true, address, lives);
        }

        // ---------------------------------------------------------------- Lobby presets

        int selectedPreset;

        void OnShowPresets(object sender, RoutedEventArgs e) => ShowPresetsPage();

        void OnPointPresets(object sender, EventArgs e) => ShowInfo("Presets Lanppart",
            "« Lanppart Poules » et « Lanppart Finales » apparaissent dans « Mode de jeu » quand on crée une partie " +
            "(lobby) en mode Lanppart3. Réglez ici toutes leurs règles : mode, roulette ou vies, bots, fautes, durées.\n\n" +
            "Le jeu les relit à chaque lancement : après un changement, relancez le jeu. Choisir un preset dans le jeu " +
            "remplit la fenêtre avec ses règles et les verrouille. Ce sont les presets de l'hôte du lobby qui comptent.");

        void ShowPresetsPage()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 0; i < PresetStore.Count; i++)
            {
                int index = i;
                var tab = new Button
                {
                    Content = PresetStore.Load(index).Name, FontSize = 16, Margin = new Thickness(0, 0, 14, 0),
                    Style = StyleRes("PlainButton"), Tag = index == selectedPreset ? "Selected" : null,
                };
                tab.Click += (o, a) => { selectedPreset = index; ShowPresetsPage(); };
                tabs.Children.Add(tab);
            }
            root.Children.Add(tabs);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var form = new StackPanel { Margin = new Thickness(0, 0, 18, 10) };
            body.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var preset = PresetStore.Load(selectedPreset);
            BuildPresetForm(form, preset);

            var side = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            Grid.SetColumn(side, 1);
            body.Children.Add(side);
            side.Children.Add(Label(preset.Name, 22, "Cream"));
            side.Children.Add(Label(ModeNames[(int)preset.Mode] + " · " + (preset.Roulette ? "roulette" : preset.Lives + " vies"),
                                    15, "OptionLabel", new Thickness(0, 4, 0, 10)));
            side.Children.Add(Label("Les changements sont enregistrés tout de suite. Le jeu les prend en compte à son prochain " +
                                    "lancement en mode Lanppart3.", 14, "CreamDim", new Thickness(0, 0, 0, 14)));
            side.Children.Add(Action("Rétablir par défaut", (o, e) =>
            {
                PresetStore.Reset(selectedPreset);
                StatusText.Text = "Preset « " + PresetStore.Load(selectedPreset).Name + " » rétabli.";
                ShowPresetsPage();
            }));

            ShowPage(Page.Presets, "Presets Lanppart", root, null);
        }

        void BuildPresetForm(StackPanel form, ServerSettings s)
        {
            var o = new OptionsBuilder(form);
            int index = selectedPreset;
            Action save = () => PresetStore.Save(index, s);
            Action rebuild = () => { form.Children.Clear(); BuildPresetForm(form, s); };

            o.Section("Preset");
            var name = o.Row("Nom dans le jeu", OptionsBuilder.Text(s.Name));
            name.LostFocus += (x, e) => { s.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : s.Name; save(); };
            var mode = o.Row("Mode de jeu", OptionsBuilder.Combo(ModeNames, (int)s.Mode));
            mode.SelectionChanged += (x, e) => { s.Mode = (GameMode)mode.SelectedIndex; save(); rebuild(); };
            o.Note("Les textes à taper restent ceux choisis dans la fenêtre du jeu (Sélection des textes).");
            BuildRules(o, s, save, rebuild);
        }

        // ---------------------------------------------------------------- Custom texts

        void OnShowTexts(object sender, RoutedEventArgs e) => ShowTextsPage(null);

        void OnPointTexts(object sender, EventArgs e) => ShowInfo("Textes personnalisés",
            "Écrivez vos propres textes pour les serveurs Lanppart3. Ils restent sur le PC du serveur : les joueurs " +
            "les découvrent au début de la partie, impossible de s'entraîner avant.\n\n" +
            "Une ligne *** sépare les manches.");

        void ShowTextsPage(CustomText selected)
        {
            var texts = TextLibrary.LoadCustom();
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var list = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var newText = new Button { Content = "+ Nouveau texte", FontSize = 17, Style = StyleRes("PlainButton"), HorizontalAlignment = HorizontalAlignment.Left };
            newText.Click += (o, e) => ShowTextsPage(new CustomText { Title = "Nouveau texte" });
            list.Children.Add(newText);
            foreach (var t in texts)
            {
                var text = t;
                var b = new Button
                {
                    Style = StyleRes("PlainButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0),
                    Tag = selected != null && selected.File == t.File ? "Selected" : null,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = t.Title, FontSize = 17 },
                            Label(t.Language + " · " + t.Rounds + " manches · difficulté " + t.Difficulty, 13, "Muted"),
                        },
                    },
                };
                b.Click += (o, e) => ShowTextsPage(text);
                list.Children.Add(b);
            }
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var editor = new StackPanel();
            Grid.SetColumn(editor, 1);
            root.Children.Add(editor);
            if (selected == null)
            {
                editor.Children.Add(Label(texts.Count == 0
                    ? "Aucun texte pour l'instant. Créez-en un avec « + Nouveau texte »."
                    : "Choisissez un texte à modifier, ou créez-en un.", 17, "CreamDim"));
                editor.Children.Add(Label("Dossier : " + TextLibrary.Folder, 13, "Muted", new Thickness(0, 10, 0, 0)));
            }
            else
            {
                var o = new OptionsBuilder(editor);
                var title = o.Row("Titre", OptionsBuilder.Text(selected.Title, 330));
                var langs = Languages();
                var lang = o.Row("Langue", OptionsBuilder.Combo(langs, Math.Max(0, langs.IndexOf(selected.Language)), 160));
                var diff = o.Row("Difficulté", new Stepper(selected.Difficulty, 1, 5));
                var body = new TextBox
                {
                    Style = StyleRes("GameText"), Text = selected.Body.Replace("\n", "\r\n"), AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap, Height = 330, FontSize = 16, Margin = new Thickness(18, 8, 0, 0),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
                editor.Children.Add(body);
                o.Note("Une ligne contenant seulement *** sépare deux manches. Les lignes de plus de 48 caractères sont " +
                       "coupées automatiquement entre deux mots, comme celles des textes du jeu.");
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 6, 0, 0) };
                buttons.Children.Add(Action("Enregistrer", (x, e) =>
                {
                    selected.Title = title.Text.Trim().Length > 0 ? title.Text.Trim() : "Sans titre";
                    selected.Language = (string)lang.SelectedItem;
                    selected.Difficulty = diff.Value;
                    selected.Body = body.Text.Replace("\r\n", "\n").Trim('\n');
                    TextLibrary.Save(selected);
                    StatusText.Text = "Texte « " + selected.Title + " » enregistré.";
                    ShowTextsPage(selected);
                }));
                if (!string.IsNullOrEmpty(selected.File))
                {
                    var del = Action("Supprimer", (x, e) =>
                    {
                        if (MessageBox.Show(this, "Supprimer « " + selected.Title + " » ?", "Lanppart3", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        TextLibrary.Delete(selected);
                        ShowTextsPage(null);
                    });
                    del.Margin = new Thickness(14, 4, 0, 4);
                    buttons.Children.Add(del);
                }
                editor.Children.Add(buttons);
            }
            ShowPage(Page.Texts, "Textes personnalisés", root, null);
        }
    }
}
