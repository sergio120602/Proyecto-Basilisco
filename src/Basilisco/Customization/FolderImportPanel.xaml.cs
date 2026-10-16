using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>Una fila de la lista de juegos encontrados.</summary>
/// <param name="root">Carpeta escaneada: la ruta se enseña relativa a ella para que se vea qué ejecutable se ha elegido.</param>
public sealed class FoundGameRow(FoundGame game, bool alreadyInLibrary, string root) : INotifyPropertyChanged
{
    bool _isSelected = !alreadyInLibrary;

    public FoundGame Game { get; } = game;
    public string Name => Game.Name;
    public bool CanImport { get; } = !alreadyInLibrary;

    public string Detail
    {
        get
        {
            var path = Path.GetRelativePath(root, Game.Path);
            return CanImport ? path : $"Ya está en tu biblioteca · {path}";
        }
    }

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

/// <summary>Lo que el usuario ha decidido importar de la carpeta.</summary>
/// <param name="NewFilterName">Si ha pedido agrupar los juegos en un filtro nuevo, su nombre.</param>
public sealed record FolderImportResult(List<FoundGame> Games, string? NewFilterName);

/// <summary>
/// Escanea una carpeta en segundo plano y enseña los juegos encontrados para elegir cuáles importar.
/// </summary>
public partial class FolderImportPanel : UserControl
{
    CancellationTokenSource? _scan;
    List<FoundGameRow> _rows = [];
    string _folderName = "";

    public event Action<FolderImportResult>? ImportRequested;

    public bool IsOpen => Visibility == Visibility.Visible;

    public FolderImportPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <param name="isInLibrary">Para marcar los juegos que ya estaban importados.</param>
    /// <param name="activeFilterName">Filtro seleccionado ahora mismo, al que irán también los juegos.</param>
    public async void Open(string folder, Func<string, bool> isInLibrary, string? activeFilterName)
    {
        _scan?.Cancel();
        var scan = _scan = new CancellationTokenSource();

        _folderName = GameScanner.PrettifyName(new DirectoryInfo(folder).Name.TrimEnd(Path.DirectorySeparatorChar, ':'));
        FolderText.Text = folder;
        FolderText.ToolTip = folder;
        ActiveFilterText.Text = activeFilterName is null ? "" : $"Los juegos también se añadirán a «{activeFilterName}», el filtro que tienes seleccionado.";
        ActiveFilterText.Visibility = activeFilterName is null ? Visibility.Collapsed : Visibility.Visible;
        ScanStatusText.Text = "Buscando juegos…";
        ScanningView.Visibility = Visibility.Visible;
        ResultsView.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;
        // El foco en el panel hace que Esc y Enter le lleguen a él.
        _ = Dispatcher.BeginInvoke(() => Focus(), System.Windows.Threading.DispatcherPriority.Input);

        var progress = new Progress<ScanProgress>(p =>
        {
            if (!scan.IsCancellationRequested)
                ScanStatusText.Text = $"Buscando juegos…   {p.Folders:N0} carpetas revisadas · {Plural(p.Games, "juego encontrado", "juegos encontrados")}";
        });

        List<FoundGame> found;
        try
        {
            found = await Task.Run(() => GameScanner.Scan(folder, progress, scan.Token), scan.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (scan.IsCancellationRequested)
            return;

        ShowResults(found.Select(game => new FoundGameRow(game, isInLibrary(game.Path), folder)).ToList());
    }

    void ShowResults(List<FoundGameRow> rows)
    {
        _rows = rows;
        ResultsList.ItemsSource = rows;
        ScanningView.Visibility = Visibility.Collapsed;
        ResultsView.Visibility = Visibility.Visible;

        var duplicates = rows.Count(row => !row.CanImport);
        var hasNew = rows.Count > duplicates;
        SummaryText.Text = rows.Count switch
        {
            0 => "No se ha encontrado ningún juego en esta carpeta.",
            _ when duplicates == 0 => Plural(rows.Count, "juego encontrado", "juegos encontrados"),
            _ when !hasNew => $"{Plural(rows.Count, "juego encontrado", "juegos encontrados")}, pero ya están todos en tu biblioteca.",
            _ => $"{Plural(rows.Count, "juego encontrado", "juegos encontrados")} · {duplicates} ya en tu biblioteca",
        };

        ListBorder.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SelectionButtons.Visibility = hasNew ? Visibility.Visible : Visibility.Collapsed;
        CreateFilterToggle.Visibility = hasNew ? Visibility.Visible : Visibility.Collapsed;
        CreateFilterToggle.Content = $"Agruparlos en un filtro nuevo «{_folderName}»";
        CreateFilterToggle.IsChecked = false;
        ImportButton.Visibility = hasNew ? Visibility.Visible : Visibility.Collapsed;
    }

    void Import()
    {
        var selected = _rows.Where(row => row.CanImport && row.IsSelected).Select(row => row.Game).ToList();
        if (selected.Count == 0)
        {
            SummaryText.Text = "Marca al menos un juego para importar.";
            return;
        }

        var filterName = CreateFilterToggle.IsChecked == true ? _folderName : null;
        Close();
        ImportRequested?.Invoke(new FolderImportResult(selected, filterName));
    }

    void Close()
    {
        _scan?.Cancel();
        _scan = null;
        _rows = [];
        ResultsList.ItemsSource = null;
        Visibility = Visibility.Collapsed;
    }

    static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count:N0} {many}";

    void SetAll(bool selected)
    {
        foreach (var row in _rows.Where(row => row.CanImport))
            row.IsSelected = selected;
    }

    void OnSelectAllClick(object sender, RoutedEventArgs e) => SetAll(true);

    void OnSelectNoneClick(object sender, RoutedEventArgs e) => SetAll(false);

    void OnImportClick(object sender, RoutedEventArgs e) => Import();

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when ResultsView.Visibility == Visibility.Visible: Import(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
