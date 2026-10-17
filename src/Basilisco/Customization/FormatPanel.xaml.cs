using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
    List<FormatRow> _rows = [];
    CoverFormat _format;

    public event Action<FormatResult>? Applied;

    public bool IsOpen => Visibility == Visibility.Visible;

    public FormatPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <param name="selected">Juego marcado al abrir (el que está en el centro del carrusel), si hay.</param>
    public void Open(IReadOnlyList<Game> games, Game? selected)
    {
        _rows = games.Select(game => new FormatRow(game, game == selected)).ToList();
        GamesList.ItemsSource = _rows;

        // Se propone el formato contrario al del juego seleccionado: lo normal es querer cambiarlo.
        _format = selected?.Format == CoverFormat.Portrait ? CoverFormat.Square : CoverFormat.Portrait;
        SyncToggles();
        UpdateCount();

        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    void SyncToggles()
    {
        SquareToggle.IsChecked = _format == CoverFormat.Square;
        PortraitToggle.IsChecked = _format == CoverFormat.Portrait;
    }

    void UpdateCount()
    {
        var count = _rows.Count(row => row.IsSelected);
        CountText.Text = count == 1 ? "1 JUEGO MARCADO" : $"{count} JUEGOS MARCADOS";
        ApplyButton.IsEnabled = count > 0;
    }

    void SetAll(bool selected)
    {
        foreach (var row in _rows)
            row.IsSelected = selected;
        UpdateCount();
    }

    void Apply()
    {
        var games = _rows.Where(row => row.IsSelected).Select(row => row.Game).ToList();
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
