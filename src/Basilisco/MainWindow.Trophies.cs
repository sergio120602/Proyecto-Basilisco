using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Basilisco.Models;
using Basilisco.Services;

namespace Basilisco;

/// <summary>
/// Perfil de trofeos de los juegos que no tienen logros de Steam: en el sitio de la barra de logros, y con la misma página.
/// Los trofeos se crean a mano (de uno en uno o varios a la vez, numerados) o se leen de RetroAchievements, RPCS3 o
/// PlayStation Network. Con la activación manual, un clic en un trofeo lo da por conseguido. Lo que viene en inglés se
/// traduce al español.
/// </summary>
public partial class MainWindow
{
    Game? _trophyGame;
    CancellationTokenSource? _trophyWork;

    static string SourceName(TrophySource source) => source switch
    {
        TrophySource.RetroAchievements => "RetroAchievements",
        TrophySource.Rpcs3 => "RPCS3",
        TrophySource.PlayStation => "PlayStation",
        _ => "creados por ti",
    };

    // ---- La barra ----

    /// <summary>La barra de un juego sin logros de Steam: sus trofeos, o una invitación a crearlos.</summary>
    void ShowOwnTrophiesBar(Game game)
    {
        var trophies = game.Trophies?.Trophies ?? [];
        if (trophies.Count == 0)
        {
            SetAchievementsMessage("Trofeos · pulsa aquí para crear los de este juego o traerlos de RetroAchievements, RPCS3 o PlayStation");
            return;
        }
        var source = game.Trophies!.Source == TrophySource.Manual ? "Trofeos" : $"Trofeos · {SourceName(game.Trophies.Source)}";
        SetAchievements(new AchievementProgress(trophies.Count(t => t.IsUnlocked), trophies.Count, DateTime.Now), source);
    }

    // ---- La página ----

    async Task OpenTrophyProfileAsync(Game game)
    {
        if (AchievementsPage.IsVisible)
            return;
        _trophyGame = game;
        AchievementsPageKind.Text = "PERFIL DE TROFEOS";
        AchievementsPageTitle.Text = game.Name;
        TrophyTools.Visibility = Visibility.Visible;
        CloseTrophyEditor();
        AchievementsScroll.ScrollToTop();
        FillTrophyProfile();
        AnimatePage(open: true);

        // Si está vinculado, se vuelve a leer el servicio (cada media hora como mucho) y se traduce lo que falte.
        var profile = game.Trophies;
        if (profile is not null && profile.Source != TrophySource.Manual && (profile.Synced is null || DateTime.Now - profile.Synced > TimeSpan.FromMinutes(30)))
            await SyncTrophiesAsync(game, quiet: true);
        await TranslateProfileAsync(game);
    }

    void FillTrophyProfile()
    {
        if (_trophyGame is not { } game)
            return;
        var profile = game.Trophies;
        var trophies = profile?.Trophies ?? [];
        ManualActivationToggle.IsChecked = profile?.ManualActivation ?? true;
        var linked = profile is not null && profile.Source != TrophySource.Manual;
        TrophySyncButton.Visibility = TrophyUnlinkButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
        TrophyUnlinkButton.Content = linked ? $"Desvincular {SourceName(profile!.Source)}" : "Desvincular";

        if (trophies.Count == 0)
        {
            AchievementsList.Children.Clear();
            AchievementsPageSummary.Text = "Este juego aún no tiene trofeos. Créalos tú (de uno en uno o varios a la vez) o tráelos de RetroAchievements, RPCS3 o PlayStation.";
            return;
        }

        var entries = trophies
            .Select(t => (new AchievementInfo(t.Id, t.Title, t.Description, t.Hidden, t.Icon, t.IconLocked, t.IsUnlocked, t.UnlockedAt, t.Percent), t.Tier, (OwnTrophy?)t))
            .ToList();
        FillTrophySections(game.Name, entries, "trofeos", byPercent: profile!.Source == TrophySource.RetroAchievements,
            onToggle: profile.ManualActivation ? ToggleTrophy : null);
        if (linked)
            AchievementsPageSummary.Text += $"\nDe {SourceName(profile.Source)}" + (profile.SourceName is { Length: > 0 } name ? $": {name}" : "") +
                                            (profile.Synced is { } synced ? $" · leído el {synced:dd/MM/yyyy HH:mm}" : "");
    }

    /// <summary>
    /// Monta las secciones de la página: el platino arriba (el del juego o, si no tiene, el de conseguirlos todos) y después
    /// oro, plata y bronce. Lo comparten los logros de Steam y el perfil de trofeos.
    /// </summary>
    /// <param name="byPercent">True si los trofeos se han repartido por el porcentaje de jugadores (Steam, RetroAchievements).</param>
    /// <param name="onToggle">Con la activación manual: lo que pasa al pulsar un trofeo propio.</param>
    /// <param name="compare">Al comparar un juego de varios miembros: quién tiene cada logro.</param>
    void FillTrophySections(string gameName, List<(AchievementInfo Info, TrophyTier Tier, OwnTrophy? Own)> entries, string noun, bool byPercent,
                            Action<OwnTrophy>? onToggle = null, Func<AchievementInfo, string?>? compare = null)
    {
        AchievementsList.Children.Clear();
        var regular = entries.Where(e => e.Tier != TrophyTier.Platinum).ToList();
        var unlocked = entries.Count(e => e.Info.Unlocked);
        var percent = entries.Count == 0 ? 0 : (int)Math.Floor(100.0 * unlocked / entries.Count);
        AchievementsPageSummary.Text = $"{unlocked} de {entries.Count} {noun} · {(unlocked == entries.Count ? 100 : percent)} %   ·   " +
            string.Join("   ", new[] { TrophyTier.Gold, TrophyTier.Silver, TrophyTier.Bronze }
                .Select(tier => $"{TierName(tier)} {entries.Count(e => e.Tier == tier && e.Info.Unlocked)}/{entries.Count(e => e.Tier == tier)}"));

        FrameworkElement Make((AchievementInfo Info, TrophyTier Tier, OwnTrophy? Own) entry, bool wide)
        {
            var card = Card(entry.Info, entry.Tier, wide, compare?.Invoke(entry.Info));
            if (entry.Own is { } own)
                Interactive(card, own, onToggle);
            return card;
        }

        AchievementsList.Children.Add(Section("PLATINO", null));
        var platinums = entries.Where(e => e.Tier == TrophyTier.Platinum).ToList();
        if (platinums.Count > 0)
        {
            foreach (var platinum in platinums)
                AchievementsList.Children.Add(Make(platinum, wide: true));
        }
        else
        {
            // Sin platino propio: el de tenerlos todos.
            var all = regular.Count > 0 && regular.All(e => e.Info.Unlocked);
            var platinum = new AchievementInfo("", "Platino", $"Consigue todos los {noun} de {gameName}.", false, null, null,
                all, all ? regular.Max(e => e.Info.UnlockedAt) : null, null);
            AchievementsList.Children.Add(Card(platinum, TrophyTier.Platinum, wide: true));
        }

        foreach (var tier in new[] { TrophyTier.Gold, TrophyTier.Silver, TrophyTier.Bronze })
        {
            var group = regular.Where(e => e.Tier == tier)
                .OrderByDescending(e => e.Info.Unlocked).ThenBy(e => e.Info.GlobalPercent ?? 100).ToList();
            if (group.Count == 0)
                continue;
            var range = !byPercent ? null : tier switch
            {
                TrophyTier.Gold => "difíciles · menos del 15 % de los jugadores",
                TrophyTier.Silver => "dificultad media · del 15 al 50 %",
                _ => "fáciles · la mitad de los jugadores o más",
            };
            AchievementsList.Children.Add(Section($"{TierName(tier).ToUpperInvariant()}  ·  {group.Count(e => e.Info.Unlocked)} de {group.Count}", range));
            var cards = new WrapPanel();
            foreach (var entry in group)
                cards.Children.Add(Make(entry, wide: false));
            AchievementsList.Children.Add(cards);
        }
    }

    /// <summary>Un trofeo del perfil: con la activación manual se pulsa para conseguirlo; los tuyos se cambian o borran con el clic derecho.</summary>
    void Interactive(FrameworkElement card, OwnTrophy trophy, Action<OwnTrophy>? onToggle)
    {
        if (onToggle is not null)
        {
            card.Cursor = Cursors.Hand;
            card.ToolTip = trophy.IsUnlocked ? "Clic para quitarlo" : "Clic para darlo por conseguido";
            card.MouseLeftButtonUp += (_, _) => onToggle(trophy);
        }
        if (!trophy.Custom)
            return;

        var menu = new ContextMenu();
        foreach (var tier in Enum.GetValues<TrophyTier>())
        {
            var chosen = tier;
            menu.Items.Add(Item($"Dificultad: {TierName(tier)}", () => ChangeTier(trophy, chosen), isChecked: trophy.Tier == tier));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Eliminar trofeo", () => DeleteTrophy(trophy)));
        card.ContextMenu = menu;
    }

    void ToggleTrophy(OwnTrophy trophy)
    {
        if (trophy.Unlocked && !trophy.ManualUnlocked)
        {
            ShowToast("Este trofeo lo tienes según el servicio vinculado: no se puede quitar a mano");
            return;
        }
        trophy.ManualUnlocked = !trophy.ManualUnlocked;
        trophy.UnlockedAt = trophy.ManualUnlocked ? DateTime.Now : trophy.Unlocked ? trophy.UnlockedAt : null;
        TrophiesChanged();
        ShowToast(trophy.ManualUnlocked ? $"Trofeo conseguido: «{trophy.Title}»" : $"Trofeo quitado: «{trophy.Title}»");
    }

    void ChangeTier(OwnTrophy trophy, TrophyTier tier)
    {
        trophy.Tier = tier;
        TrophiesChanged();
    }

    void DeleteTrophy(OwnTrophy trophy)
    {
        _trophyGame?.Trophies?.Trophies.Remove(trophy);
        TrophiesChanged();
        ShowToast($"Trofeo eliminado: «{trophy.Title}»");
    }

    /// <summary>Guarda y vuelve a pintar la página y la barra.</summary>
    void TrophiesChanged()
    {
        Save();
        var offset = AchievementsScroll.VerticalOffset;
        FillTrophyProfile();
        AchievementsScroll.ScrollToVerticalOffset(offset);
        if (_trophyGame is { } game && _achievementsGame == game)
            ShowOwnTrophiesBar(game);
    }

    void OnManualActivationClick(object sender, RoutedEventArgs e)
    {
        if (_trophyGame is not { } game)
            return;
        (game.Trophies ??= new TrophyProfile()).ManualActivation = ManualActivationToggle.IsChecked == true;
        TrophiesChanged();
        ShowToast(game.Trophies.ManualActivation ? "Activación manual encendida: pulsa un trofeo para conseguirlo" : "Activación manual apagada");
    }

    // ---- Formularios ----

    void ShowTrophyEditor(UIElement content)
    {
        TrophyEditor.Child = content;
        TrophyEditor.Visibility = Visibility.Visible;
    }

    void CloseTrophyEditor()
    {
        _trophyWork?.Cancel();
        TrophyEditor.Child = null;
        TrophyEditor.Visibility = Visibility.Collapsed;
        if (AchievementsPage.IsVisible)
            Focus();
    }

    static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xBF)), Margin = new Thickness(2, 10, 0, 6),
    };

    static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD3, 0xE0)), Margin = new Thickness(2, 6, 0, 0),
    };

    TextBox Field(string text = "", bool multiline = false)
    {
        var box = new TextBox { Text = text, Style = (Style)FindResource("FieldTextBox"), FontSize = 15 };
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.Height = 130;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.VerticalContentAlignment = VerticalAlignment.Top;
        }
        return box;
    }

    Button Pill(string text, Action onClick, bool primary = false)
    {
        var button = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "PillButton"), Margin = new Thickness(0, 0, 10, 0), Focusable = false };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Los cuatro botones de dificultad; devuelve el panel y cómo leer la elegida.</summary>
    (Panel Panel, Func<TrophyTier> Selected) TierPicker(TrophyTier initial = TrophyTier.Bronze)
    {
        var panel = new WrapPanel();
        var selected = initial;
        var toggles = new List<ToggleButton>();
        foreach (var tier in Enum.GetValues<TrophyTier>())
        {
            var chosen = tier;
            var toggle = new ToggleButton { Content = TierName(tier), Style = (Style)FindResource("FilterToggle"), IsChecked = tier == initial, Focusable = false };
            toggle.Click += (_, _) =>
            {
                selected = chosen;
                foreach (var other in toggles)
                    other.IsChecked = other == toggle;
            };
            toggles.Add(toggle);
            panel.Children.Add(toggle);
        }
        return (panel, () => selected);
    }

    static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    void OnCreateTrophyClick(object sender, RoutedEventArgs e)
    {
        var title = Field();
        var description = Field();
        var (tiers, selected) = TierPicker();
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "Nuevo trofeo", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        form.Children.Add(Label("TÍTULO"));
        form.Children.Add(title);
        form.Children.Add(Label("DESCRIPCIÓN (OPCIONAL)"));
        form.Children.Add(description);
        form.Children.Add(Label("DIFICULTAD"));
        form.Children.Add(tiers);
        void Create()
        {
            if (title.Text.Trim() is not { Length: > 0 } name)
            {
                title.Focus();
                return;
            }
            AddTrophies([(name, description.Text.Trim(), selected())]);
            CloseTrophyEditor();
        }
        title.KeyDown += (_, args) => { if (args.Key == Key.Enter) Create(); };
        form.Children.Add(Row(Pill("Crear trofeo", Create, primary: true), Pill("Cancelar", CloseTrophyEditor)));
        ShowTrophyEditor(form);
        title.Focus();
    }

    void OnCreateTrophiesClick(object sender, RoutedEventArgs e)
    {
        var text = Field(multiline: true);
        var (tiers, selected) = TierPicker();
        var preview = Note("");
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "Crear varios trofeos", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        form.Children.Add(Note("Escríbelos numerados, en líneas o seguidos: «1. Trofeo primero, 2. Trofeo segundo, 3. Trofeo tercero». " +
                               "Para darle a uno otra dificultad, ponla entre paréntesis al final: «4. Jefe final (oro)»."));
        form.Children.Add(Label("TROFEOS"));
        form.Children.Add(text);
        form.Children.Add(preview);
        form.Children.Add(Label("DIFICULTAD DE TODOS (SALVO LOS QUE LLEVEN LA SUYA)"));
        form.Children.Add(tiers);
        text.TextChanged += (_, _) =>
        {
            var count = ParseTrophyList(text.Text, selected()).Count;
            preview.Text = count == 0 ? "" : count == 1 ? "Se creará 1 trofeo." : $"Se crearán {count} trofeos.";
        };
        void Create()
        {
            var items = ParseTrophyList(text.Text, selected());
            if (items.Count == 0)
            {
                text.Focus();
                return;
            }
            AddTrophies(items);
            CloseTrophyEditor();
        }
        form.Children.Add(Row(Pill("Crear trofeos", Create, primary: true), Pill("Cancelar", CloseTrophyEditor)));
        ShowTrophyEditor(form);
        text.Focus();
    }

    /// <summary>
    /// Lee una lista numerada: «1. Uno, 2. Dos» o una por línea («1) Uno», «2 - Dos»…). Sin números, cada línea es uno.
    /// Una dificultad entre paréntesis al final («(oro)», «[plata]») cambia la de ese trofeo.
    /// </summary>
    static List<(string Title, string Description, TrophyTier Tier)> ParseTrophyList(string text, TrophyTier fallback)
    {
        // Con números, van seguidos (1, 2, 3…): se corta solo donde aparece el siguiente, así un «nivel 10» no parte el trofeo.
        var items = new List<string>();
        if (Regex.Match(text, @"^\s*(\d{1,3})\s*[.,):\-]\s*") is { Success: true } first)
        {
            var number = int.Parse(first.Groups[1].Value);
            var position = first.Index + first.Length;
            while (true)
            {
                var next = new Regex($@"(?:^|[\s,;]){number + 1}\s*[.,):\-]\s*", RegexOptions.Multiline).Match(text, position);
                if (!next.Success)
                {
                    items.Add(text[position..]);
                    break;
                }
                items.Add(text[position..next.Index]);
                position = next.Index + next.Length;
                number++;
            }
        }
        else
        {
            items.AddRange(text.Split('\n'));
        }

        var result = new List<(string, string, TrophyTier)>();
        foreach (var raw in items)
        {
            var item = raw.Trim().TrimEnd(',', ';', '.').Trim();
            if (item.Length == 0)
                continue;
            var tier = fallback;
            if (Regex.Match(item, @"[\(\[]\s*(bronce|plata|oro|platino)\s*[\)\]]\s*$", RegexOptions.IgnoreCase) is { Success: true } match)
            {
                tier = match.Groups[1].Value.ToLowerInvariant() switch
                {
                    "platino" => TrophyTier.Platinum, "oro" => TrophyTier.Gold, "plata" => TrophyTier.Silver, _ => TrophyTier.Bronze,
                };
                item = item[..match.Index].Trim();
            }
            // «Título: descripción» separa la descripción.
            var colon = item.IndexOf(':');
            result.Add(colon > 0 && colon < item.Length - 1 ? (item[..colon].Trim(), item[(colon + 1)..].Trim(), tier) : (item, "", tier));
        }
        return result;
    }

    void AddTrophies(List<(string Title, string Description, TrophyTier Tier)> items)
    {
        if (_trophyGame is not { } game || items.Count == 0)
            return;
        var profile = game.Trophies ??= new TrophyProfile();
        foreach (var (title, description, tier) in items)
            profile.Trophies.Add(new OwnTrophy { Title = title, Description = description, Tier = tier, Custom = true, Translated = true });
        TrophiesChanged();
        ShowToast(items.Count == 1 ? $"Trofeo creado: «{items[0].Title}»" : $"{items.Count} trofeos creados");
    }

    // ---- Vincular servicios ----

    /// <summary>Lista de juegos encontrados para elegir uno y vincularlo.</summary>
    void ShowGameChoices(StackPanel form, List<TrophyGame> games, Action<TrophyGame> onChosen, string empty)
    {
        if (games.Count == 0)
        {
            form.Children.Add(Note(empty));
            return;
        }
        var list = new ListBox { MaxHeight = 260, Margin = new Thickness(0, 10, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        foreach (var game in games)
        {
            var row = new DockPanel { Margin = new Thickness(4, 3, 4, 3) };
            var detail = new TextBlock { Text = game.Detail, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xBF)), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(detail, Dock.Right);
            row.Children.Add(detail);
            row.Children.Add(new TextBlock { Text = game.Name, FontSize = 15, Foreground = Brushes.White });
            list.Items.Add(new ListBoxItem { Content = row, Tag = game });
        }
        list.SelectedIndex = 0;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: TrophyGame chosen }) onChosen(chosen); };
        form.Children.Add(list);
        form.Children.Add(Row(Pill("Vincular este juego", () => { if (list.SelectedItem is ListBoxItem { Tag: TrophyGame chosen }) onChosen(chosen); }, primary: true),
            Pill("Cancelar", CloseTrophyEditor)));
    }

    CancellationToken NewTrophyWork()
    {
        _trophyWork?.Cancel();
        return (_trophyWork = new CancellationTokenSource()).Token;
    }

    void Link(Game game, TrophySource source, TrophyGame chosen)
    {
        var profile = game.Trophies ??= new TrophyProfile();
        profile.Source = source;
        profile.SourceId = chosen.Id;
        profile.SourceName = chosen.Name;
        profile.SourceService = chosen.Service;
        profile.Synced = null;
        CloseTrophyEditor();
        _ = SyncAndTranslateAsync(game);
    }

    async Task SyncAndTranslateAsync(Game game)
    {
        await SyncTrophiesAsync(game, quiet: false);
        await TranslateProfileAsync(game);
    }

    // RetroAchievements

    void OnRetroAchievementsClick(object sender, RoutedEventArgs e)
    {
        if (_trophyGame is not { } game)
            return;
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "RetroAchievements", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var consoles = RetroAchievements.Consoles(game.Platform);
        if (consoles.Length == 0)
        {
            form.Children.Add(Note($"RetroAchievements no tiene logros de {(Platforms.Find(game.Platform)?.Name ?? "juegos sin consola")}. " +
                                   "Tiene NES, Super Nintendo, Nintendo 64, Game Boy, GBA, DS, GameCube, Wii, PS1, PS2, PSP y Arcade."));
            form.Children.Add(Row(Pill("Cerrar", CloseTrophyEditor)));
            ShowTrophyEditor(form);
            return;
        }

        if (_settings.RetroAchievementsUser is not { Length: > 0 } user || _settings.RetroAchievementsApiKey is not { Length: > 0 } key)
        {
            var userBox = Field(_settings.RetroAchievementsUser ?? "");
            var keyBox = Field();
            var status = Note("Tu usuario y la clave de la Web API de tu cuenta (en retroachievements.org → Ajustes → «Web API Key»).");
            form.Children.Add(status);
            form.Children.Add(Label("USUARIO"));
            form.Children.Add(userBox);
            form.Children.Add(Label("CLAVE DE LA WEB API"));
            form.Children.Add(keyBox);
            async Task SignIn()
            {
                status.Text = "Comprobando la cuenta…";
                try
                {
                    var name = await RetroAchievements.SignInAsync(userBox.Text.Trim(), keyBox.Text.Trim(), NewTrophyWork());
                    _settings.RetroAchievementsUser = name;
                    _settings.RetroAchievementsApiKey = keyBox.Text.Trim();
                    _settings.Save();
                    OnRetroAchievementsClick(sender, e);
                }
                catch (TrophyServiceException ex)
                {
                    status.Text = ex.Message;
                }
                catch (OperationCanceledException)
                {
                }
            }
            form.Children.Add(Row(Pill("Iniciar sesión", () => _ = SignIn(), primary: true),
                Pill("Abrir la página de la clave", () => OpenUrl("https://retroachievements.org/settings")), Pill("Cancelar", CloseTrophyEditor)));
            ShowTrophyEditor(form);
            userBox.Focus();
            return;
        }

        var search = Field(game.Name);
        var results = new StackPanel();
        var info = Note($"Sesión iniciada como {user}. Elige el juego de RetroAchievements:");
        form.Children.Add(info);
        form.Children.Add(Label("BUSCAR"));
        form.Children.Add(search);
        form.Children.Add(results);
        async Task Search()
        {
            results.Children.Clear();
            results.Children.Add(Note("Buscando…"));
            try
            {
                var games = await RetroAchievements.SearchAsync(search.Text.Trim(), consoles, user, key, NewTrophyWork());
                results.Children.Clear();
                ShowGameChoices(results, games, chosen => Link(game, TrophySource.RetroAchievements, chosen),
                    "No se ha encontrado ningún juego con logros con ese nombre. Prueba con su nombre en inglés.");
            }
            catch (TrophyServiceException ex)
            {
                results.Children.Clear();
                results.Children.Add(Note(ex.Message));
            }
            catch (OperationCanceledException)
            {
            }
        }
        search.KeyDown += (_, args) => { if (args.Key == Key.Enter) _ = Search(); };
        form.Children.Add(Row(Pill("Buscar", () => _ = Search()), Pill("Cerrar sesión", () =>
        {
            _settings.RetroAchievementsApiKey = null;
            _settings.Save();
            OnRetroAchievementsClick(sender, e);
        })));
        ShowTrophyEditor(form);
        _ = Search();
    }

    // RPCS3

    void OnRpcs3Click(object sender, RoutedEventArgs e)
    {
        if (_trophyGame is not { } game)
            return;
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "RPCS3", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var trophyFolder = Rpcs3Trophies.TrophyFolder(_settings.Rpcs3Folder);
        form.Children.Add(Note(trophyFolder is null
            ? "Elige la carpeta de RPCS3 (la que tiene rpcs3.exe y dev_hdd0). Basilisco lee de ahí los trofeos que guarda el emulador."
            : $"Trofeos de RPCS3 en {trophyFolder}"));
        void Choose()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Elegir la carpeta de RPCS3" };
            if (dialog.ShowDialog(this) != true)
                return;
            if (Rpcs3Trophies.TrophyFolder(dialog.FolderName) is null)
            {
                ShowToast("En esa carpeta no hay trofeos de RPCS3 (dev_hdd0/home/…/trophy)");
                return;
            }
            _settings.Rpcs3Folder = dialog.FolderName;
            _settings.Save();
            OnRpcs3Click(sender, e);
        }
        if (trophyFolder is not null)
        {
            var games = Rpcs3Trophies.Games(trophyFolder);
            var sorted = GameSearch.Find(games, g => g.Name, game.Name, games.Count);
            sorted.AddRange(games.Except(sorted).OrderBy(g => g.Name));
            ShowGameChoices(form, sorted, chosen => Link(game, TrophySource.Rpcs3, chosen), "RPCS3 aún no tiene trofeos de ningún juego (se crean al jugar).");
        }
        form.Children.Add(Row(Pill(trophyFolder is null ? "Elegir carpeta de RPCS3…" : "Cambiar carpeta…", Choose, primary: trophyFolder is null), Pill("Cerrar", CloseTrophyEditor)));
        ShowTrophyEditor(form);
    }

    // PlayStation

    void OnPlayStationClick(object sender, RoutedEventArgs e)
    {
        if (_trophyGame is not { } game)
            return;
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "PlayStation · PSN Profiles", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var psnProfiles = Pill("Abrir en PSN Profiles", () => OpenUrl($"https://psnprofiles.com/search/games?q={Uri.EscapeDataString(game.Name)}"));

        if (!PlayStationTrophies.IsConnected)
        {
            var code = Field();
            var status = Note("PSN Profiles no deja que los programas lean su web, así que Basilisco lee tus trofeos directamente de PlayStation Network " +
                              "(de donde los saca PSN Profiles). Inicia sesión en playstation.com en el navegador, abre la página del código, copia lo que sale " +
                              "y pégalo aquí. El código solo se guarda en memoria mientras Basilisco está abierto.");
            form.Children.Add(status);
            form.Children.Add(Label("CÓDIGO DE SESIÓN (NPSSO)"));
            form.Children.Add(code);
            async Task Connect()
            {
                if (PlayStationTrophies.ParseNpsso(code.Text) is not { } npsso)
                {
                    status.Text = "Eso no parece el código. Pega lo que sale en la página del código (o solo el valor de «npsso»).";
                    return;
                }
                status.Text = "Conectando con PlayStation…";
                try
                {
                    await PlayStationTrophies.ConnectAsync(npsso, NewTrophyWork());
                    OnPlayStationClick(sender, e);
                }
                catch (TrophyServiceException ex)
                {
                    status.Text = ex.Message;
                }
                catch (OperationCanceledException)
                {
                }
            }
            form.Children.Add(Row(Pill("Conectar", () => _ = Connect(), primary: true), Pill("Abrir la página del código", () => OpenUrl(PlayStationTrophies.NpssoUrl)),
                psnProfiles, Pill("Cancelar", CloseTrophyEditor)));
            ShowTrophyEditor(form);
            code.Focus();
            return;
        }

        var search = Field(game.Name);
        var results = new StackPanel();
        form.Children.Add(Note("Conectado a PlayStation. Elige el juego entre los tuyos:"));
        form.Children.Add(Label("BUSCAR"));
        form.Children.Add(search);
        form.Children.Add(results);
        async Task Search()
        {
            results.Children.Clear();
            results.Children.Add(Note("Buscando…"));
            try
            {
                var games = await PlayStationTrophies.SearchAsync(search.Text.Trim(), NewTrophyWork());
                results.Children.Clear();
                ShowGameChoices(results, games, chosen => Link(game, TrophySource.PlayStation, chosen),
                    "No tienes trofeos de ningún juego con ese nombre en PlayStation.");
            }
            catch (TrophyServiceException ex)
            {
                results.Children.Clear();
                results.Children.Add(Note(ex.Message));
            }
            catch (OperationCanceledException)
            {
            }
        }
        search.KeyDown += (_, args) => { if (args.Key == Key.Enter) _ = Search(); };
        form.Children.Add(Row(Pill("Buscar", () => _ = Search()), psnProfiles));
        ShowTrophyEditor(form);
        _ = Search();
    }

    static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    void OnTrophySyncClick(object sender, RoutedEventArgs e)
    {
        if (_trophyGame is { } game)
            _ = SyncAndTranslateAsync(game);
    }

    void OnTrophyUnlinkClick(object sender, RoutedEventArgs e)
    {
        if (_trophyGame?.Trophies is not { } profile || profile.Source == TrophySource.Manual)
            return;
        var source = SourceName(profile.Source);
        profile.Trophies.RemoveAll(t => !t.Custom);
        profile.Source = TrophySource.Manual;
        profile.SourceId = profile.SourceName = profile.SourceService = null;
        profile.Synced = null;
        TrophiesChanged();
        ShowToast($"Desvinculado de {source}: quedan los trofeos que creaste tú");
    }

    /// <summary>
    /// Vuelve a leer los trofeos del servicio vinculado. Se quedan los que creaste tú y, de los del servicio, lo que
    /// conseguiste a mano.
    /// </summary>
    async Task SyncTrophiesAsync(Game game, bool quiet)
    {
        if (game.Trophies is not { Source: not TrophySource.Manual, SourceId: { } id } profile)
            return;
        if (!quiet && _trophyGame == game)
            AchievementsPageSummary.Text = $"Leyendo los trofeos de {SourceName(profile.Source)}…";

        List<OwnTrophy> fetched;
        try
        {
            var cancellation = NewTrophyWork();
            switch (profile.Source)
            {
                case TrophySource.RetroAchievements:
                    if (_settings.RetroAchievementsUser is not { } user || _settings.RetroAchievementsApiKey is not { } key)
                        throw new TrophyServiceException("Inicia sesión en RetroAchievements para leer los logros.");
                    fetched = (await RetroAchievements.TrophiesAsync(id, user, key, cancellation)).Trophies;
                    break;
                case TrophySource.Rpcs3:
                    fetched = Rpcs3Trophies.Read(id)?.Trophies ?? throw new TrophyServiceException("No se encuentran los trofeos de RPCS3 de este juego.");
                    break;
                default:
                    if (!PlayStationTrophies.IsConnected)
                    {
                        if (!quiet)
                            ShowToast("Conéctate a PlayStation (con tu código de sesión) para volver a leer los trofeos");
                        FillTrophyProfileIfShown(game);
                        return;
                    }
                    fetched = await PlayStationTrophies.TrophiesAsync(id, profile.SourceService ?? "trophy", cancellation);
                    break;
            }
        }
        catch (TrophyServiceException ex)
        {
            if (!quiet)
                ShowToast(ex.Message);
            FillTrophyProfileIfShown(game);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var previous = profile.Trophies.Where(t => !t.Custom).ToDictionary(t => t.Id);
        foreach (var trophy in fetched)
        {
            if (previous.TryGetValue(trophy.Id, out var old) && old.ManualUnlocked && !trophy.Unlocked)
            {
                trophy.ManualUnlocked = true;
                trophy.UnlockedAt = old.UnlockedAt;
            }
        }
        profile.Trophies = [.. fetched, .. profile.Trophies.Where(t => t.Custom)];
        profile.Synced = DateTime.Now;
        Save();
        FillTrophyProfileIfShown(game);
        if (_achievementsGame == game)
            ShowOwnTrophiesBar(game);
        if (!quiet)
            ShowToast($"{fetched.Count} trofeos de {SourceName(profile.Source)}");
    }

    void FillTrophyProfileIfShown(Game game)
    {
        if (_trophyGame == game && AchievementsPage.IsVisible && TrophyTools.IsVisible)
            FillTrophyProfile();
    }

    /// <summary>Traduce al español los trofeos del servicio que vienen en inglés (los tuyos se quedan como los escribiste).</summary>
    async Task TranslateProfileAsync(Game game)
    {
        if (game.Trophies is not { } profile)
            return;
        var pending = profile.Trophies.Where(t => !t.Custom && !t.Translated).ToList();
        if (pending.Count == 0)
            return;
        var translations = await TrophyTranslator.TranslateAsync(pending.SelectMany(t => new[] { t.Title, t.Description }).Where(text => text.Length > 0), CancellationToken.None);
        var changed = false;
        foreach (var trophy in pending)
        {
            // Hecho cuando cada texto se ha traducido o no estaba en inglés; si no (sin conexión, cupo agotado), otro día.
            bool Done(string text) => text.Length == 0 || translations.ContainsKey(text) || !TrophyTranslator.LooksEnglish(text);
            trophy.Translated = Done(trophy.Title) && Done(trophy.Description);
            if (translations.TryGetValue(trophy.Title, out var title))
                trophy.Title = title;
            if (translations.TryGetValue(trophy.Description, out var description))
                trophy.Description = description;
            changed = true;
        }
        if (!changed)
            return;
        Save();
        FillTrophyProfileIfShown(game);
    }

    /// <summary>Los logros de Steam que solo existen en inglés, en español (para enseñarlos; no se guardan en la biblioteca).</summary>
    async Task<List<AchievementInfo>> TranslateSteamAsync(List<AchievementInfo> list)
    {
        var translations = await TrophyTranslator.TranslateAsync(list.SelectMany(a => new[] { a.Name, a.Description }).Where(text => text.Length > 0), CancellationToken.None);
        if (translations.Count == 0)
            return list;
        return list.Select(a => a with
        {
            Name = translations.GetValueOrDefault(a.Name, a.Name),
            Description = translations.GetValueOrDefault(a.Description, a.Description),
        }).ToList();
    }
}
