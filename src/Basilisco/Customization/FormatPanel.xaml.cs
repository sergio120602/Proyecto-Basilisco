using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Basilisco.Models;

namespace Basilisco.Customization;

/// <summary>Un juego en la lista del menú de formato.</summary>
public sealed class FormatRow(Game game, bool selected) : INotifyPropertyChanged
{
    bool _isSelected = selected;

    public Game Game { get; } = game;
    public string Name => Game.Name;
    public string FormatText => Game.Format == CoverFormat.Portrait ? "Rectangular" : "Cuadrada";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Lo decidido en el menú de formato.</summary>
/// <param name="AutoFetch">Poner imágenes por defecto en los juegos cuyo formato nuevo esté vacío.</param>
public sealed record FormatResult(List<Game> Games, CoverFormat Format, bool AutoFetch);

/// <summary>Menú para cambiar el formato de la caja (cuadrada o rectangular) de uno, varios o todos los juegos.</summary>
public partial class FormatPanel : UserControl
{
    /// <summary>Los filtros de tienda, siempre a la vista (aunque aún no haya juegos de esa tienda).</summary>
    static readonly (string Id, string Name)[] Stores =
        [("steam", "Steam"), ("epic", "Epic Games"), ("gog", "GOG"), ("ea", "EA (Origin)"), ("battlenet", "Battle.net")];

    List<FormatRow> _rows = [];
    CoverFormat _format;

    /// <summary>Lo que se ve en la lista: null (todos), «consola:&lt;id&gt;» o «tienda:&lt;id&gt;».</summary>
    string? _filter;

    public event Action<FormatResult>? Applied;

    public bool IsOpen => Visibility == Visibility.Visible;

    public FormatPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Tienda del juego: «steam» para los de Steam y, si no, la de la que se importó (o null).</summary>
    static string? StoreOf(Game game) => game.SteamAppId is not null ? "steam" : game.Store;

    /// <param name="games">Toda la biblioteca (los filtros del menú eligen qué se ve).</param>
    /// <param name="selected">Juego marcado al abrir (el que está en el centro del carrusel), si hay.</param>
    public void Open(IReadOnlyList<Game> games, Game? selected)
    {
        _rows = games.Select(game => new FormatRow(game, game == selected)).ToList();
        _filter = null;
        BuildFilters(games);
        ShowRows();

        // Se propone el formato contrario al del juego seleccionado: lo normal es querer cambiarlo.
        _format = selected?.Format == CoverFormat.Portrait ? CoverFormat.Square : CoverFormat.Portrait;
        SyncToggles();

        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    /// <summary>
    /// Los botones de filtro: «Todas», una por cada consola que tenga juegos (en el orden de siempre) y, debajo, las
    /// tiendas. Cada uno dice cuántos juegos tiene.
    /// </summary>
    void BuildFilters(IReadOnlyList<Game> games)
    {
        ConsoleFilters.Children.Clear();
        StoreFilters.Children.Clear();
        ConsoleFilters.Children.Add(FilterButton($"Todas ({games.Count})", null));
        foreach (var platform in Platforms.All)
        {
            var count = games.Count(game => game.Platform == platform.Id);
            if (count > 0)
                ConsoleFilters.Children.Add(FilterButton($"{platform.Name} ({count})", "consola:" + platform.Id));
        }
        foreach (var (id, name) in Stores)
            StoreFilters.Children.Add(FilterButton($"{name} ({games.Count(game => StoreOf(game) == id)})", "tienda:" + id));
    }

    ToggleButton FilterButton(string text, string? filter)
    {
        var button = new ToggleButton { Style = (Style)FindResource("FilterToggle"), Content = text, Tag = filter };
        button.Click += (_, _) =>
        {
            _filter = filter;
            ShowRows();
        };
        return button;
    }

    bool Matches(Game game) =>
        _filter is null
        || _filter.StartsWith("consola:", StringComparison.Ordinal) && game.Platform == _filter["consola:".Length..]
        || _filter.StartsWith("tienda:", StringComparison.Ordinal) && StoreOf(game) == _filter["tienda:".Length..];

    /// <summary>Los juegos del filtro elegido (los marcados que quedan ocultos no cuentan ni se cambian).</summary>
    List<FormatRow> Shown => _rows.Where(row => Matches(row.Game)).ToList();

    void ShowRows()
    {
        foreach (var button in ConsoleFilters.Children.OfType<ToggleButton>().Concat(StoreFilters.Children.OfType<ToggleButton>()))
            button.IsChecked = (string?)button.Tag == _filter;
        GamesList.ItemsSource = Shown;
        UpdateCount();
    }

    void SyncToggles()
    {
        SquareToggle.IsChecked = _format == CoverFormat.Square;
        PortraitToggle.IsChecked = _format == CoverFormat.Portrait;
    }

    void UpdateCount()
    {
        var count = Shown.Count(row => row.IsSelected);
        CountText.Text = count == 1 ? "1 JUEGO MARCADO" : $"{count} JUEGOS MARCADOS";
        ApplyButton.IsEnabled = count > 0;
    }

    void SetAll(bool selected)
    {
        foreach (var row in Shown)
            row.IsSelected = selected;
        UpdateCount();
    }

    void Apply()
    {
        var games = Shown.Where(row => row.IsSelected).Select(row => row.Game).ToList();
        if (games.Count == 0)
            return;

        var result = new FormatResult(games, _format, AutoFetchBox.IsChecked == true);
        Close();
        Applied?.Invoke(result);
    }

    void Close()
    {
        _rows = [];
        GamesList.ItemsSource = null;
        Visibility = Visibility.Collapsed;
    }

    void OnSquareClick(object sender, RoutedEventArgs e)
    {
        _format = CoverFormat.Square;
        SyncToggles();
    }

    void OnPortraitClick(object sender, RoutedEventArgs e)
    {
        _format = CoverFormat.Portrait;
        SyncToggles();
    }

    void OnRowClick(object sender, RoutedEventArgs e) => UpdateCount();

    void OnSelectAllClick(object sender, RoutedEventArgs e) => SetAll(true);

    void OnSelectNoneClick(object sender, RoutedEventArgs e) => SetAll(false);

    void OnApplyClick(object sender, RoutedEventArgs e) => Apply();

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Apply(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
