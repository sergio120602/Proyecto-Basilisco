using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Basilisco.Carousel;
using Basilisco.Models;
using Basilisco.Services;

namespace Basilisco;

/// <summary>
/// «Ver sus logros» de un miembro de Steam familiar (desde su menú de personalizar, con el clic derecho): todos sus juegos,
/// cada uno con su plataforma y su barra de logros, y al elegir uno, sus logros. Si el juego lo tenéis varios, cada logro
/// dice quién lo tiene. En tu caso salen todos tus juegos, también los que no son de Steam (con su perfil de trofeos).
/// </summary>
public partial class MainWindow
{
    FamilyMember? _memberView;
    bool _memberGameOpen;
    CancellationTokenSource? _memberWork;

    /// <summary>SteamID del miembro (si es una cuenta conocida), para pedir sus logros a Steam.</summary>
    string? SteamIdOf(FamilyMember member) => member.IsKnownAccount ? member.SteamId : member.IsMe ? MySteamId() : null;

    /// <summary>Sus juegos: los suyos de Steam; en tu caso, también los que no son de Steam.</summary>
    List<Game> MemberGames(FamilyMember member) =>
        Games.Where(game => member.Owns(game) || member.IsMe && game.SteamAppId is null)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    static string PlatformLabel(Game game) => game.SteamAppId is not null ? "Steam" : Platforms.Find(game.Platform)?.Name ?? "Sin consola";

    async Task OpenMemberAchievementsAsync(FamilyMember member)
    {
        if (AchievementsPage.IsVisible)
            return;
        _memberView = member;
        ShowMemberGames();
        AnimatePage(open: true);
        await Task.CompletedTask;
    }

    void OnMemberGamesClick(object sender, RoutedEventArgs e) => ShowMemberGames();

    /// <summary>La lista de juegos del miembro, con una barra de logros en cada uno (se van rellenando según llegan de Steam).</summary>
    void ShowMemberGames()
    {
        if (_memberView is not { } member)
            return;
        _memberGameOpen = false;
        _memberWork?.Cancel();
        var cancellation = (_memberWork = new CancellationTokenSource()).Token;
        _trophyGame = null;
        CloseTrophyEditor();
        TrophyTools.Visibility = Visibility.Collapsed;
        MemberGamesButton.Visibility = Visibility.Collapsed;
        AchievementsPageKind.Text = member.IsMe ? "MIS LOGROS" : "LOGROS DE STEAM FAMILIAR";
        AchievementsPageTitle.Text = member.IsMe && member.Name != "Tú" ? $"{member.Name} (tú)" : member.Name;
        AchievementsList.Children.Clear();
        AchievementsScroll.ScrollToTop();

        var games = MemberGames(member);
        var steamId = SteamIdOf(member);
        var rows = new List<(Game Game, Action<AchievementProgress?, string?> Update)>();
        foreach (var game in games)
        {
            var (row, update) = MemberGameRow(game);
            AchievementsList.Children.Add(row);
            rows.Add((game, update));
        }

        void Summary()
        {
            var known = rows.Select(r => Progress(r.Game)).OfType<AchievementProgress>().Where(p => p.Total > 0).ToList();
            AchievementsPageSummary.Text = $"{games.Count} {(games.Count == 1 ? "juego" : "juegos")} · {known.Sum(p => p.Unlocked)} logros conseguidos · " +
                                           $"{known.Count(p => p.Complete)} al 100 %" +
                                           (steamId is null ? "\nPara ver sus logros de Steam, lee tu familia de Steam (botón Steam → Steam Familiar)." : "");
        }

        AchievementProgress? Progress(Game game)
        {
            if (game.SteamAppId is { } appId)
                return steamId is null ? null : SteamAchievements.Cached(appId, steamId);
            var trophies = game.Trophies?.Trophies ?? [];
            return new AchievementProgress(trophies.Count(t => t.IsUnlocked), trophies.Count, DateTime.Now);
        }

        foreach (var (game, update) in rows)
            update(Progress(game), game.SteamAppId is not null && steamId is null ? "Sin datos de su cuenta" : null);
        Summary();
        if (games.Count == 0)
            AchievementsPageSummary.Text = "No hay juegos suyos en la biblioteca.";

        // Lo que falte o sea viejo se pide a Steam, de cuatro en cuatro.
        if (steamId is null || _settings.SteamApiKey is not { Length: > 0 } key)
            return;
        var pending = rows.Where(r => r.Game.SteamAppId is { } appId && SteamAchievements.IsStale(appId, steamId)).ToList();
        _ = FetchMemberProgressAsync(pending, steamId, key, Summary, cancellation);
    }

    async Task FetchMemberProgressAsync(List<(Game Game, Action<AchievementProgress?, string?> Update)> rows, string steamId, string key, Action summary,
                                        CancellationToken cancellation)
    {
        // De dos en dos y con una pausa: si se pide demasiado seguido, el cortafuegos de Steam corta («Access Denied»).
        using var gate = new SemaphoreSlim(2);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        string? blocked = null;
        var tasks = rows.Select(async row =>
        {
            try
            {
                await gate.WaitAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                var progress = await SteamAchievements.FetchAsync(row.Game.SteamAppId!.Value, steamId, key, stop.Token);
                if (!cancellation.IsCancellationRequested)
                    row.Update(progress, null);
                await Task.Delay(150, stop.Token);
            }
            catch (AchievementsException ex) when (ex.Problem is AchievementsProblem.NotPublic or AchievementsProblem.Throttled)
            {
                // Si su perfil no los enseña (o Steam frena), no sirve seguir preguntando juego por juego.
                blocked ??= ex.Message;
                stop.Cancel();
            }
            catch (AchievementsException)
            {
                if (!cancellation.IsCancellationRequested)
                    row.Update(null, "Steam no da los logros de este juego");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
        if (cancellation.IsCancellationRequested)
            return;
        summary();
        if (blocked is not null)
        {
            foreach (var row in rows.Where(r => SteamAchievements.Cached(r.Game.SteamAppId!.Value, steamId) is null))
                row.Update(null, blocked.Contains("frenado") ? "Pendiente: Steam ha frenado las consultas" : "Steam no enseña sus logros");
            AchievementsPageSummary.Text += "\n" + blocked;
        }
    }

    /// <summary>Una fila: la portada, el nombre, la plataforma y la barra de logros. Devuelve también cómo actualizar su barra.</summary>
    (FrameworkElement Row, Action<AchievementProgress?, string?> Update) MemberGameRow(Game game)
    {
        var card = new Border
        {
            Margin = new Thickness(6, 5, 6, 5), Padding = new Thickness(12, 10, 18, 10), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = "Ver los logros de este juego",
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

        var cover = ImageStore.GetPath(game, game.Format, Face.Front) is { } path ? CoverArt.LoadImage(path, 140) : null;
        var thumb = new Border
        {
            Width = 46, Height = 64, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 16, 0),
            Background = cover is null ? new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x30)) : new ImageBrush(cover) { Stretch = Stretch.UniformToFill },
        };
        grid.Children.Add(thumb);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        name.Children.Add(new TextBlock { Text = game.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        name.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 2, 10, 3), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(game.SteamAppId is not null ? Color.FromRgb(0x1B, 0x3A, 0x5C) : Color.FromRgb(0x3A, 0x2E, 0x52)),
            Child = new TextBlock { Text = PlatformLabel(game), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
        });
        grid.Children.Add(name);

        // La barra, como la de abajo: azul con degradado y dorada al 100 %.
        var bar = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        Grid.SetColumn(bar, 2);
        var label = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD3, 0xE0)) };
        var percent = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        var top = new DockPanel { Margin = new Thickness(2, 0, 2, 5) };
        DockPanel.SetDock(percent, Dock.Right);
        top.Children.Add(percent);
        top.Children.Add(label);
        bar.Children.Add(top);
        var track = new Grid();
        var fill = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        var rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        track.ColumnDefinitions.Add(fill);
        track.ColumnDefinitions.Add(rest);
        var filled = new Border { CornerRadius = new CornerRadius(5), Background = SteamBlue };
        track.Children.Add(filled);
        bar.Children.Add(new Border
        {
            Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), ClipToBounds = true, Child = track,
        });
        grid.Children.Add(bar);
        card.Child = grid;

        void Update(AchievementProgress? progress, string? message)
        {
            if (progress is null || progress.Total == 0)
            {
                label.Text = message ?? (progress is null ? "Cargando…" : game.SteamAppId is null ? "Sin trofeos (créalos en su perfil de trofeos)" : "Sin logros");
                percent.Text = "";
                fill.Width = new GridLength(0, GridUnitType.Star);
                rest.Width = new GridLength(1, GridUnitType.Star);
                return;
            }
            var noun = game.SteamAppId is null ? "trofeos" : "logros";
            label.Text = progress.Complete ? $"¡Todos! {progress.Total} de {progress.Total} {noun}" : $"{progress.Unlocked} de {progress.Total} {noun}";
            percent.Text = progress.Complete ? "100 %" : $"{(progress.Fraction * 100 < 1 && progress.Unlocked > 0 ? 1 : Math.Floor(progress.Fraction * 100)):0} %";
            percent.Foreground = progress.Complete ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x6B)) : Brushes.White;
            fill.Width = new GridLength(progress.Fraction, GridUnitType.Star);
            rest.Width = new GridLength(1 - progress.Fraction, GridUnitType.Star);
            filled.Background = progress.Complete ? Gold : SteamBlue;
            card.BorderBrush = progress.Complete ? Gold : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }

        card.MouseLeftButtonUp += (_, _) => _ = ShowMemberGameAsync(game);
        return (card, Update);
    }

    /// <summary>
    /// Los logros de un juego del miembro. Si el juego lo tienen también otros de la familia (tú, si miras a otro; los demás
    /// dueños, si te miras a ti), cada logro dice quién lo tiene.
    /// </summary>
    async Task ShowMemberGameAsync(Game game)
    {
        if (_memberView is not { } member)
            return;
        _memberGameOpen = true;
        _memberWork?.Cancel();
        var cancellation = (_memberWork = new CancellationTokenSource()).Token;
        MemberGamesButton.Content = member.IsMe ? "←  Mis juegos" : $"←  Juegos de {member.Name}";
        MemberGamesButton.Visibility = Visibility.Visible;
        AchievementsPageTitle.Text = game.Name;
        AchievementsScroll.ScrollToTop();

        // Los que no son de Steam: su perfil de trofeos, como al abrirlo desde su barra.
        if (game.SteamAppId is not { } appId)
        {
            AchievementsPageKind.Text = "PERFIL DE TROFEOS";
            _trophyGame = game;
            TrophyTools.Visibility = Visibility.Visible;
            FillTrophyProfile();
            await TranslateProfileAsync(game);
            return;
        }

        AchievementsPageKind.Text = member.IsMe ? "MIS LOGROS · STEAM" : $"LOGROS DE {member.Name.ToUpperInvariant()} · STEAM";
        TrophyTools.Visibility = Visibility.Collapsed;
        AchievementsList.Children.Clear();
        AchievementsPageSummary.Text = "Cargando los logros…";
        if (_settings.SteamApiKey is not { Length: > 0 } key || SteamIdOf(member) is not { } steamId)
        {
            AchievementsPageSummary.Text = _settings.SteamApiKey is { Length: > 0 }
                ? "Para ver sus logros, lee tu familia de Steam (botón Steam → Steam Familiar)."
                : "Para ver los logros, guarda tu clave de la Web API de Steam (botón Steam).";
            return;
        }

        // Con quién se compara: los demás de la familia que también lo tienen (y de los que se sabe la cuenta).
        var others = FamilyMembers()
            .Where(other => other.SteamId != member.SteamId && other.Owns(game) && SteamIdOf(other) is not null && (member.IsMe || other.IsMe))
            .ToList();
        try
        {
            var mine = await SteamAchievements.DetailsAsync(appId, steamId, key, cancellation);
            var theirs = new List<(FamilyMember Member, HashSet<string> Unlocked)>();
            foreach (var other in others)
            {
                try
                {
                    var list = await SteamAchievements.DetailsAsync(appId, SteamIdOf(other)!, key, cancellation);
                    theirs.Add((other, list.Where(a => a.Unlocked).Select(a => a.ApiName).ToHashSet()));
                }
                catch (AchievementsException)
                {
                }
            }
            if (cancellation.IsCancellationRequested)
                return;

            void Fill(List<AchievementInfo> list)
            {
                if (list.Count == 0)
                {
                    AchievementsList.Children.Clear();
                    AchievementsPageSummary.Text = "Este juego no tiene logros en Steam.";
                    return;
                }
                string Who(FamilyMember m) => m.IsMe ? "Tú" : m.Name;
                Func<AchievementInfo, string?>? compare = theirs.Count == 0 ? null : a =>
                    string.Join("   ", new[] { $"{Who(member)} {(a.Unlocked ? "✓" : "✗")}" }
                        .Concat(theirs.Select(t => $"{Who(t.Member)} {(t.Unlocked.Contains(a.ApiName) ? "✓" : "✗")}")));
                FillTrophySections(game.Name, list.Select(a => (a, a.Tier, (OwnTrophy?)null)).ToList(), "logros", byPercent: true, compare: compare);
                if (theirs.Count > 0)
                {
                    var both = list.Count(a => a.Unlocked && theirs.All(t => t.Unlocked.Contains(a.ApiName)));
                    AchievementsPageSummary.Text += $"\nComparado con {string.Join(" y ", theirs.Select(t => $"{Who(t.Member)} ({t.Unlocked.Count} de {list.Count})"))}" +
                                                    $" · {both} los tenéis {(theirs.Count == 1 ? "los dos" : "todos")}";
                }
            }

            Fill(mine);
            var translated = await TranslateSteamAsync(mine);
            if (translated != mine && !cancellation.IsCancellationRequested && _memberGameOpen && AchievementsPage.IsVisible)
            {
                var offset = AchievementsScroll.VerticalOffset;
                Fill(translated);
                AchievementsScroll.ScrollToVerticalOffset(offset);
            }
        }
        catch (AchievementsException ex)
        {
            AchievementsPageSummary.Text = ex.Message;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
