using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Basilisco.Models;

namespace Basilisco.Customization;

/// <summary>Una opción de consola en los desplegables («Sin consola» tiene Id null).</summary>
public sealed record PlatformOption(string? Id, string Name)
{
    public static readonly PlatformOption None = new(null, "Sin consola");

    public static readonly IReadOnlyList<PlatformOption> All =
        [None, .. Platforms.All.Select(platform => new PlatformOption(platform.Id, platform.Name))];

    public static PlatformOption Of(string? id) => All.FirstOrDefault(option => option.Id == id) ?? None;
}

/// <summary>Un juego recién importado y la consola elegida para él.</summary>
public sealed class PlatformRow(Game game, PlatformOption selected) : INotifyPropertyChanged
{
    PlatformOption _selected = selected;

    public Game Game { get; } = game;
    public string Name => Game.Name;
    public IReadOnlyList<PlatformOption> Options => PlatformOption.All;

    public PlatformOption Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Menú que aparece tras importar uno o varios juegos para decir de qué consola es cada uno.
/// La consola decide el borde de la caja y en qué pestaña de consola aparece.
/// </summary>
public partial class PlatformPickerPanel : UserControl
{
    List<PlatformRow> _rows = [];

    /// <summary>Juegos y consola elegida para cada uno.</summary>
    public event Action<List<(Game Game, string? Platform)>>? Saved;

    public bool IsOpen => Visibility == Visibility.Visible;

    public PlatformPickerPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;

        foreach (var option in PlatformOption.All)
        {
            var button = new Button { Content = option.Name, Style = (Style)FindResource("PillButton"), Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => SetAll(option);
            AllButtons.Children.Add(button);
        }
    }

    /// <param name="defaultPlatform">Consola propuesta para los que todavía no tienen (la pestaña de consola abierta, si la hay).</param>
    public void Open(IReadOnlyList<Game> games, string? defaultPlatform)
    {
        _rows = games.Select(game => new PlatformRow(game, PlatformOption.Of(game.Platform ?? defaultPlatform))).ToList();
        GamesList.ItemsSource = _rows;
        SubtitleText.Text = (games.Count == 1 ? $"Has importado «{games[0].Name}». " : $"Has importado {games.Count} juegos. ") +
                            "Cada caja llevará el borde de su consola. Puedes cambiarla cuando quieras en Personalizar (E).";

        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    void SetAll(PlatformOption option)
    {
        foreach (var row in _rows)
            row.Selected = option;
    }

    void Save()
    {
        var result = _rows.Select(row => (row.Game, row.Selected.Id)).ToList();
        Close();
        Saved?.Invoke(result);
    }

    void Close()
    {
        _rows = [];
        GamesList.ItemsSource = null;
        Visibility = Visibility.Collapsed;
    }

    void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    void OnSkipClick(object sender, RoutedEventArgs e) => Close();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Save(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
