using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lanppart3Launcher
{
    // The launcher's pages: server browser, dedicated servers, lobby presets, custom texts.
    public partial class MainWindow
    {
        static readonly string[] ModeNames = { "Battle royale", "K.O.", "Duel" };
        static readonly string[] Difficulties = { "Très facile", "Facile", "Moyen", "Difficile", "Expert" };
        static readonly string[] MistakeBehaviours = { "Retour en arrière", "Bloquer", "Passer" };
        static readonly string[] TextSources = { "Tous les textes du jeu", "Textes personnalisés", "Jeu + personnalisés", "Sélection…" };
        static readonly string[] DefaultLanguages = { "fr", "en", "de", "es", "pt", "ru" };

        ServerInstance selectedServer;
        Action refreshPage;   // updates the live parts of the current page

        static Brush Res(string key) => (Brush)Application.Current.Resources[key];
        static Style StyleRes(string key) => (Style)Application.Current.Resources[key];

        // Shows a page on the left; key: its entry in the tab's menu.
        void ShowPage(string key, string title, UIElement content, Action refresh)
        {
            PageTitle.Text = title;
            PageHost.Content = content;
            refreshPage = refresh;
            MarkPage(key);
            refresh?.Invoke();
        }

        static TextBlock Label(string text, double size, string brush, Thickness margin = default(Thickness)) =>
            new TextBlock { Text = Typo(text), FontSize = size, Foreground = Res(brush), TextWrapping = TextWrapping.Wrap, Margin = margin };

        // French typography: no line break between « » and their words, nor before : ; ? !
        internal static string Typo(string text) => text == null ? null : text
            .Replace("« ", "« ").Replace(" »", " »")
            .Replace(" :", " :").Replace(" ;", " ;").Replace(" ?", " ?").Replace(" !", " !");

        static Button Action(string text, RoutedEventHandler click, bool enabled = true)
        {
            var b = new Button { Content = text, Style = StyleRes("ActionButton"), IsEnabled = enabled };
            b.Click += click;
            return b;
        }

        static Rectangle HLine(double top = 0, double bottom = 12) =>
            new Rectangle { Style = StyleRes("HLine"), Margin = new Thickness(0, top, 0, bottom) };

        // Two columns, the side one (state and actions) behind a vertical line.
        static Grid WithSide(UIElement main, FrameworkElement side, double sideWidth = 330)
        {
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sideWidth) });
            if (main is FrameworkElement m) m.Margin = new Thickness(0, 0, 14, 0);  // (its scroll bar off the line)
            body.Children.Add(main);
            var line = new Rectangle { Style = StyleRes("VLine") };
            Grid.SetColumn(line, 1);
            body.Children.Add(line);
            Grid.SetColumn(side, 2);
            body.Children.Add(side);
            return body;
        }

        // Tabs of a page (servers, presets) above a line.
        static StackPanel SubTabs(out WrapPanel tabs)
        {
            var head = new StackPanel();
            tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(tabs);
            head.Children.Add(HLine());
            return head;
        }

        static Button SubTab(string text, bool selected) => new Button
        {
            Content = text, FontSize = 18, Margin = new Thickness(0, 0, 18, 0),
            Style = StyleRes("PlainButton"), Tag = selected ? "Selected" : null,
        };

        static ScrollViewer Scroll(UIElement content) =>
            new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        List<string> Languages()
        {
            var langs = TextLibrary.LoadCatalog().Select(t => t.Language)
                .Concat(TextLibrary.LoadCustom().Select(t => t.Language)).Distinct().ToList();
            foreach (var l in DefaultLanguages) if (!langs.Contains(l)) langs.Add(l);
            return langs;
        }

        // ---------------------------------------------------------------- Servers

        void ShowServersPage()
        {
            if (selectedServer == null || !servers.Servers.Contains(selectedServer))
                selectedServer = servers.Servers.FirstOrDefault() ?? servers.Create();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(SubTabs(out var tabs));
            foreach (var s in servers.Servers)
            {
                var srv = s;
                var tab = SubTab((srv.Running ? "● " : "") + srv.Settings.Name, srv == selectedServer);
                tab.Click += (o, a) => { selectedServer = srv; ShowServersPage(); };
                tabs.Children.Add(tab);
            }
            var add = new Button { Content = "+ Nouveau serveur", FontSize = 18, Style = StyleRes("PlainButton") };
            add.Click += (o, a) => { selectedServer = servers.Create(); ShowServersPage(); };
            tabs.Children.Add(add);

            var form = new StackPanel { Margin = new Thickness(0, 0, 22, 10) };
            BuildServerForm(form, selectedServer);
            var side = new StackPanel { Margin = new Thickness(22, 0, 4, 0) };
            var body = WithSide(Scroll(form), Scroll(side));
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            ShowPage("servers", "Mes serveurs", root, () => RefreshServerSide(side, selectedServer));
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
                   (rules.FillWithBots ? "compléter les places" : rules.Bots.ToString()) + ". Elles se modifient dans SERVEUR › Presets Lanppart.");
            int preset = s.Preset;
            var edit = Action("Modifier ce preset", (x, e) => { selectedPreset = preset; GoTo(Tab.Server, "presets"); });
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
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                var check = OptionsBuilder.Check(isChecked);
                System.Windows.Automation.AutomationProperties.SetName(check, title);
                check.Checked += (x, e) => toggled(true);
                check.Unchecked += (x, e) => toggled(false);
                row.Children.Add(check);
                var label = Label(title, 16, "Cream", new Thickness(10, 3, 0, 0));
                label.Cursor = System.Windows.Input.Cursors.Hand;
                label.MouseLeftButtonUp += (x, e) => check.IsChecked = check.IsChecked != true;  // the title ticks it too
                row.Children.Add(label);
                row.Children.Add(Label(detail, 14, "Muted", new Thickness(10, 5, 0, 0)));
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
            if (list.Children.Count == 0) list.Children.Add(Label("Aucun texte pour cette langue et ce mode.", 16, "Muted"));
            return new Border
            {
                BorderBrush = Res("FieldOrange"), BorderThickness = new Thickness(1), Margin = new Thickness(18, 6, 0, 4),
                Padding = new Thickness(10, 6, 6, 6), Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
                Child = new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
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
            side.Children.Add(Label(s.Name, 23, "Cream"));
            side.Children.Add(Label(server.PhaseText, 18, server.Running ? "CheckGreen" : "Muted", new Thickness(0, 4, 0, 0)));
            int capacity = ServerSettings.Capacity(rules.Mode);
            side.Children.Add(Label((s.Preset >= 0 ? PresetStore.Load(s.Preset).Name + " · " : "") + ModeNames[(int)rules.Mode] +
                                    " · " + (rules.Roulette ? "roulette" : rules.Lives + " vies"), 16, "OptionLabel",
                                    new Thickness(0, 4, 0, 0)));
            side.Children.Add(HLine(12, 12));
            if (server.Running)
            {
                side.Children.Add(Label(server.PlayerCount + " / " + capacity + " joueurs", 18, "OptionCaption"));
                foreach (var p in server.Players) side.Children.Add(Label("  " + p, 16, "CreamDim"));
                side.Children.Add(Label("Adresses pour rejoindre :", 15, "Muted", new Thickness(0, 12, 0, 2)));
                foreach (var ip in Discovery.LocalAddresses())
                    side.Children.Add(new TextBox
                    {
                        Text = ip + ":" + s.Port, IsReadOnly = true, FontSize = 15, Foreground = Res("CreamDim"),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                    });
                side.Children.Add(HLine(12, 12));
                var waiting = server.Phase == "waiting";
                side.Children.Add(Action("Lancer la partie", (o, e) => { server.Send("start"); StatusText.Text = "Partie lancée sur " + s.Name + "."; },
                                         waiting && server.PlayerCount > 0));
                side.Children.Add(Action("Nouvelle partie", (o, e) => { servers.Apply(server); server.Send("next"); }, server.Phase != "boot"));
                side.Children.Add(Action("Appliquer les réglages", (o, e) =>
                {
                    servers.Apply(server);
                    StatusText.Text = "Réglages appliqués : ils valent dès la prochaine partie de " + s.Name + ".";
                }));
                side.Children.Add(HLine(8, 8));
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
                                        "(sinon il n'est plus visible dans la liste des serveurs).", 15, "Muted", new Thickness(0, 12, 0, 0)));
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

        string manualAddress = "";

        void ShowBrowserPage()
        {
            var root = new DockPanel();
            var bottom = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            bottom.Children.Add(HLine(0, 10));
            bottom.Children.Add(new TextBlock { Text = "Un serveur n'apparaît pas ?", Style = StyleRes("SectionCaption") });
            var manual = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            manual.Children.Add(Label("Adresse du serveur :", 18, "OptionLabel", new Thickness(0, 8, 14, 0)));
            var addr = OptionsBuilder.Text(manualAddress, 280);
            addr.TextChanged += (o, e) => manualAddress = addr.Text;
            manual.Children.Add(addr);
            var join = Action("Rejoindre", (o, e) => JoinServer(NormalizeAddress(addr.Text), null));
            join.Margin = new Thickness(14, 0, 0, 0);
            manual.Children.Add(join);
            bottom.Children.Add(manual);
            bottom.Children.Add(Label("Exemple : 26.12.34.56:7777 (adresse Radmin VPN de l'organisateur) ou 192.168.1.20:7777.", 15, "Muted",
                                      new Thickness(0, 6, 0, 0)));

            var head = Label("", 16, "CreamDim", new Thickness(0, 0, 0, 12));
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            var list = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            root.Children.Add(Scroll(list));
            string lastKey = null;
            ShowPage("browser", "Rechercher un serveur", root, () =>
            {
                var current = listener?.Current() ?? new List<ServerInfo>();
                var key = string.Join(";", current.Select(c => c.Address + c.Players + c.Phase + c.Name + c.Lives + c.Roulette));
                if (key == lastKey) return;
                lastKey = key;
                list.Children.Clear();
                head.Text = listener == null ? "La recherche est indisponible (port 47777 déjà pris) : entrez l'adresse du serveur en bas."
                          : current.Count == 0 ? "Recherche des serveurs sur le réseau local et Radmin VPN…"
                          : current.Count + " serveur(s) trouvé(s). Choisissez-en un : le jeu démarre en mode Lanppart3 et s'y connecte tout seul.";
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
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Label(info.Name, 22, "Cream"));
            text.Children.Add(Label(info.ModeText + " · " + info.RulesText + " · " + info.Players + " / " + info.Capacity + " joueurs · " +
                                    info.PhaseText, 16, "OptionLabel", new Thickness(0, 2, 0, 0)));
            text.Children.Add(Label(info.Address, 14, "Muted", new Thickness(0, 2, 0, 0)));
            grid.Children.Add(text);
            var join = Action("Rejoindre", (o, e) => JoinServer(info.Address, info),
                              info.Phase == "waiting" && info.Players < info.Capacity);
            join.VerticalAlignment = VerticalAlignment.Center;
            join.Margin = new Thickness(16, 0, 0, 0);
            join.ToolTip = info.Phase == "waiting" ? null : "Partie en cours : attendez la suivante.";
            Grid.SetColumn(join, 1);
            grid.Children.Add(join);
            return new Border
            {
                Child = grid, BorderBrush = Res("FrameDim"), BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 6, 4, 12), Margin = new Thickness(0, 0, 0, 8),
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

        void ShowPresetsPage()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(SubTabs(out var tabs));
            for (int i = 0; i < PresetStore.Count; i++)
            {
                int index = i;
                var tab = SubTab(PresetStore.Load(index).Name, index == selectedPreset);
                tab.Click += (o, a) => { selectedPreset = index; ShowPresetsPage(); };
                tabs.Children.Add(tab);
            }

            var form = new StackPanel { Margin = new Thickness(0, 0, 22, 10) };
            var preset = PresetStore.Load(selectedPreset);
            BuildPresetForm(form, preset);
            var side = new StackPanel { Margin = new Thickness(22, 0, 4, 0) };
            var body = WithSide(Scroll(form), side);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            side.Children.Add(Label(preset.Name, 23, "Cream"));
            side.Children.Add(Label(ModeNames[(int)preset.Mode] + " · " + (preset.Roulette ? "roulette" : preset.Lives + " vies"),
                                    16, "OptionLabel", new Thickness(0, 4, 0, 0)));
            side.Children.Add(HLine(12, 12));
            side.Children.Add(Label("Les changements sont enregistrés tout de suite. Le jeu les prend en compte à son prochain " +
                                    "lancement en mode Lanppart3, les serveurs à leur prochaine partie.", 15, "CreamDim",
                                    new Thickness(0, 0, 0, 14)));
            side.Children.Add(Action("Rétablir par défaut", (o, e) =>
            {
                PresetStore.Reset(selectedPreset);
                StatusText.Text = "Preset « " + PresetStore.Load(selectedPreset).Name + " » rétabli.";
                ShowPresetsPage();
            }));

            ShowPage("presets", "Presets Lanppart", root, null);
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
            o.Note("Dans un lobby, les textes à taper restent ceux choisis dans la fenêtre du jeu (Sélection des textes).");
            BuildRules(o, s, save, rebuild);
        }

        // ---------------------------------------------------------------- Custom texts

        void ShowTextsPage(CustomText selected)
        {
            var texts = TextLibrary.LoadCustom();
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var list = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var newText = new Button { Content = "+ Nouveau texte", FontSize = 18, Style = StyleRes("PlainButton"), HorizontalAlignment = HorizontalAlignment.Left };
            newText.Click += (o, e) => ShowTextsPage(new CustomText { Title = "Nouveau texte" });
            list.Children.Add(newText);
            list.Children.Add(HLine(10, 0));
            foreach (var t in texts)
            {
                var text = t;
                var b = new Button
                {
                    Style = StyleRes("PlainButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0),
                    Tag = selected != null && selected.File == t.File ? "Selected" : null,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = t.Title, FontSize = 18, TextWrapping = TextWrapping.Wrap },
                            Label(t.Language + " · " + t.Rounds + " manches · difficulté " + t.Difficulty, 14, "Muted"),
                        },
                    },
                };
                b.Click += (o, e) => ShowTextsPage(text);
                list.Children.Add(b);
            }
            root.Children.Add(Scroll(list));
            var line = new Rectangle { Style = StyleRes("VLine"), Margin = new Thickness(0, 0, 22, 0) };
            Grid.SetColumn(line, 1);
            root.Children.Add(line);

            var editor = new StackPanel();
            var editorScroll = Scroll(editor);
            Grid.SetColumn(editorScroll, 2);
            root.Children.Add(editorScroll);
            if (selected == null)
            {
                editor.Children.Add(Label(texts.Count == 0
                    ? "Aucun texte pour l'instant. Créez-en un avec « + Nouveau texte »."
                    : "Choisissez un texte à modifier, ou créez-en un.", 18, "CreamDim"));
                editor.Children.Add(Label("Ces textes restent sur ce PC : seuls les serveurs lancés d'ici peuvent les utiliser. " +
                                          "Les joueurs les découvrent au début de la partie.", 16, "CreamDim", new Thickness(0, 10, 0, 0)));
                editor.Children.Add(Label("Dossier : " + TextLibrary.Folder, 15, "Muted", new Thickness(0, 10, 0, 0)));
            }
            else
            {
                var o = new OptionsBuilder(editor);
                var title = o.Row("Titre", OptionsBuilder.Text(selected.Title, 380));
                var langs = Languages();
                var lang = o.Row("Langue", OptionsBuilder.Combo(langs, Math.Max(0, langs.IndexOf(selected.Language)), 160));
                var diff = o.Row("Difficulté", new Stepper(selected.Difficulty, 1, 5));
                var body = new TextBox
                {
                    Style = StyleRes("GameText"), Text = selected.Body.Replace("\n", "\r\n"), AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap, Height = 280, FontSize = 17, Margin = new Thickness(18, 8, 0, 0),
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
            ShowPage("texts", "Textes personnalisés", root, null);
        }
    }
}
