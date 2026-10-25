using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Basilisco.Models;
using Basilisco.Services;
using Microsoft.Win32;

namespace Basilisco.Customization;

/// <summary>Un juego encontrado en la lista para revisar.</summary>
public sealed class ImportRow(ImportedGame game, bool alreadyInLibrary) : INotifyPropertyChanged
{
    bool _isSelected = !alreadyInLibrary;

    public ImportedGame Game { get; } = game;
    public string Name => Game.Name;
    public bool CanImport { get; } = !alreadyInLibrary;

    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!CanImport)
                parts.Add("Ya está en tu biblioteca");
            if (Platforms.Find(Game.Platform) is { } platform && Game.Platform != "pc")
                parts.Add(platform.Name);
            if (Game.Detail is { Length: > 0 } detail)
                parts.Add(detail);
            return string.Join(" · ", parts);
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

/// <summary>Lo decidido al importar: los juegos y de qué emulador o tienda son.</summary>
public sealed record LauncherImportResult(EmulatorInfo? Emulator, StoreInfo? Store, List<ImportedGame> Games);

/// <summary>
/// Importar juegos desde un emulador o una tienda de PC. Con un emulador: dónde está (se busca solo, o se elige), cómo se
/// le pasa el juego y las carpetas de juegos (las que ya tiene en su configuración y las que añadas). Con una tienda, se
/// leen directamente sus juegos instalados. Después se revisa lo encontrado antes de importar.
/// </summary>
public partial class LauncherImportPanel : UserControl
{
    static readonly Brush ErrorBrush = Brushes.Firebrick;
    static Brush InfoBrush => (Brush)Application.Current.Resources["MutedBrush"];
    static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9D, 0x52));

    AppSettings _settings = new();
    Func<ImportedGame, bool> _isInLibrary = _ => false;
    EmulatorInfo? _emulator;
    StoreInfo? _store;
    List<string> _configured = [];
    List<ImportRow> _rows = [];
    CancellationTokenSource? _work;

    public event Action<LauncherImportResult>? ImportRequested;

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public LauncherImportPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void OpenEmulator(EmulatorInfo emulator, AppSettings settings, Func<ImportedGame, bool> isInLibrary)
    {
        _emulator = emulator;
        _store = null;
        _settings = settings;
        _isInLibrary = isInLibrary;
        TitleText.Text = $"Importar desde {emulator.Name}";
        SubtitleText.Text = emulator.Id switch
        {
            "retroarch" => "Los juegos de tus listas de RetroArch (las que crea «Importar contenido»), cada uno con su núcleo y su consola.",
            "rpcs3" => "Los juegos de PS3 que ya conoce RPCS3 (de disco e instalados) y los de las carpetas que añadas.",
            "kyty" => "Juegos de PS5 (carpetas con eboot.bin y sce_sys/param.json). KytyPS5 está en desarrollo: no todos funcionan.",
            _ => $"Juegos de {emulator.Console}: los de las carpetas que ya tiene {emulator.Name} y los de las que añadas.",
        };
        EmulatorOptions.Visibility = Visibility.Visible;
        FoldersSection.Visibility = emulator.Id == "retroarch" ? Visibility.Collapsed : Visibility.Visible;
        ExeBox.Text = Emulators.FindExe(emulator, settings) ?? "";
        ArgumentsBox.Text = settings.EmulatorArguments.GetValueOrDefault(emulator.Id) ?? emulator.DefaultArguments;
        UpdateExeStatus();
        ShowSetup();
        Show();
    }

    public void OpenStore(StoreInfo store, AppSettings settings, Func<ImportedGame, bool> isInLibrary)
    {
        _store = store;
        _emulator = null;
        _settings = settings;
        _isInLibrary = isInLibrary;
        TitleText.Text = $"Importar desde {store.Name}";
        SubtitleText.Text = store.Description;
        EmulatorOptions.Visibility = Visibility.Collapsed;
        Show();
        // De una tienda no hay nada que elegir: se buscan sus juegos directamente.
        _ = LoadAsync();
    }

    void Show()
    {
        SetStatus("");
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    void ShowSetup()
    {
        SetupView.Visibility = Visibility.Visible;
        ResultsView.Visibility = Visibility.Collapsed;
        RefreshFolders();
    }

    List<string> UserFolders => _emulator is { } emulator ? _settings.EmulatorFolders.GetValueOrDefault(emulator.Id) ?? [] : [];

    /// <summary>Las carpetas de juegos: las del emulador (de su configuración) y las añadidas aquí, que se pueden quitar.</summary>
    void RefreshFolders()
    {
        if (_emulator is not { } emulator)
            return;
        _configured = Emulators.ConfiguredFolders(emulator, ExeBox.Text.Trim() is { Length: > 0 } exe && File.Exists(exe) ? exe : null);
        FoldersList.Items.Clear();
        foreach (var folder in _configured)
            FoldersList.Items.Add(FolderRow(folder, $"de {emulator.Name}", remove: null));
        foreach (var folder in UserFolders.Where(folder => !_configured.Contains(folder, StringComparer.OrdinalIgnoreCase)))
        {
            var chosen = folder;
            FoldersList.Items.Add(FolderRow(folder, "añadida", () =>
            {
                UserFolders.RemoveAll(f => string.Equals(f, chosen, StringComparison.OrdinalIgnoreCase));
                _settings.Save();
                RefreshFolders();
            }));
        }
        NoFoldersText.Visibility = FoldersList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static FrameworkElement FolderRow(string folder, string origin, Action? remove)
    {
        var row = new DockPanel { Margin = new Thickness(10, 5, 6, 5) };
        if (remove is not null)
        {
            var button = new Button { Content = "Quitar", Style = (Style)Application.Current.Resources["PillButton"], Margin = new Thickness(10, 0, 0, 0) };
            button.Click += (_, _) => remove();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        var tag = new TextBlock { Text = origin, FontSize = 12, Foreground = InfoBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(tag, Dock.Right);
        row.Children.Add(tag);
        row.Children.Add(new TextBlock
        {
            Text = folder, FontSize = 14, Foreground = (Brush)Application.Current.Resources["TextBrush"], VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return row;
    }

    void UpdateExeStatus()
    {
        if (_emulator is not { } emulator)
            return;
        var exe = ExeBox.Text.Trim();
        if (exe.Length == 0)
        {
            ExeStatus.Text = $"No se ha encontrado {emulator.Name} en este PC: elige su ejecutable ({string.Join(" o ", emulator.ExeNames.Take(2))}).";
            ExeStatus.Foreground = ErrorBrush;
        }
        else if (!File.Exists(exe))
        {
            ExeStatus.Text = "Ese archivo no existe.";
            ExeStatus.Foreground = ErrorBrush;
        }
        else
        {
            ExeStatus.Text = $"{emulator.Name} encontrado.";
            ExeStatus.Foreground = OkBrush;
        }
    }

    void OnExeChanged(object sender, TextChangedEventArgs e)
    {
        UpdateExeStatus();
        if (IsOpen)
            RefreshFolders();
    }

    void OnChooseExeClick(object sender, RoutedEventArgs e)
    {
        if (_emulator is not { } emulator)
            return;
        var dialog = new OpenFileDialog
        {
            Title = $"¿Dónde está {emulator.Name}?",
            Filter = $"{emulator.Name} ({string.Join(";", emulator.ExeNames)})|{string.Join(";", emulator.ExeNames)}|Programas (*.exe)|*.exe",
        };
        if (ExeBox.Text.Trim() is { Length: > 0 } current && Path.GetDirectoryName(current) is { } folder && Directory.Exists(folder))
            dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            ExeBox.Text = dialog.FileName;
    }

    void OnDefaultArgumentsClick(object sender, RoutedEventArgs e)
    {
        if (_emulator is { } emulator)
            ArgumentsBox.Text = emulator.DefaultArguments;
    }

    void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (_emulator is not { } emulator)
            return;
        var dialog = new OpenFolderDialog { Title = $"Carpeta con juegos de {emulator.Console}", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        var folders = _settings.EmulatorFolders.TryGetValue(emulator.Id, out var list) ? list : _settings.EmulatorFolders[emulator.Id] = [];
        foreach (var folder in dialog.FolderNames.Where(folder => !folders.Contains(folder, StringComparer.OrdinalIgnoreCase)))
            folders.Add(folder);
        _settings.Save();
        RefreshFolders();
    }

    /// <summary>Guarda lo elegido del emulador (dónde está y cómo se abre) y busca sus juegos.</summary>
    async Task LoadAsync()
    {
        _work?.Cancel();
        var work = _work = new CancellationTokenSource();
        LoadButton.IsEnabled = false;
        SetStatus("Buscando juegos…");
        try
        {
            ImportedGames found;
            if (_emulator is { } emulator)
            {
                var exe = ExeBox.Text.Trim();
                if (exe.Length > 0 && File.Exists(exe))
                    _settings.EmulatorPaths[emulator.Id] = exe;
                var arguments = ArgumentsBox.Text.Trim();
                if (arguments.Length == 0 || arguments == emulator.DefaultArguments)
                    _settings.EmulatorArguments.Remove(emulator.Id);
                else
                    _settings.EmulatorArguments[emulator.Id] = arguments;
                if (emulator.Id == "rpcs3" && File.Exists(exe))
                    _settings.Rpcs3Folder ??= Path.GetDirectoryName(exe);
                _settings.Save();
                var folders = _configured.Concat(UserFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var exePath = File.Exists(exe) ? exe : null;
                found = await Task.Run(() => Emulators.Scan(emulator, exePath, folders, work.Token), work.Token);
                if (exePath is null)
                    found.Notes.Insert(0, $"sin {emulator.Name} no se podrán abrir: elige dónde está antes de jugar");
            }
            else if (_store is { } store)
            {
                found = await Task.Run(() => StoreLibraries.Scan(store), work.Token);
            }
            else
            {
                return;
            }
            if (work.IsCancellationRequested)
                return;
            ShowResults(found);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetStatus($"No se han podido leer los juegos: {ex.Message}", error: true);
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    void ShowResults(ImportedGames found)
    {
        _rows = found.Games.Select(game => new ImportRow(game, _isInLibrary(game))).ToList();
        ResultsList.ItemsSource = _rows;
        SetupView.Visibility = Visibility.Collapsed;
        ResultsView.Visibility = Visibility.Visible;
        BackButton.Visibility = _emulator is null ? Visibility.Collapsed : Visibility.Visible;

        var already = _rows.Count(row => !row.CanImport);
        SummaryText.Text = (_rows.Count == 1 ? "1 juego" : $"{_rows.Count:N0} juegos") + (already > 0 ? $" · {already:N0} ya importados" : "");
        ImportButton.IsEnabled = _rows.Any(row => row.CanImport);
        var notes = string.Join(" · ", found.Notes);
        SetStatus(notes.Length > 0 ? char.ToUpper(notes[0]) + notes[1..] + "." : "", error: _rows.Count == 0);
    }

    void Import()
    {
        var selected = _rows.Where(row => row.CanImport && row.IsSelected).Select(row => row.Game).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Marca al menos un juego para importar.", error: true);
            return;
        }
        var result = new LauncherImportResult(_emulator, _store, selected);
        Close();
        ImportRequested?.Invoke(result);
    }

    public void Close()
    {
        if (!IsOpen)
            return;
        _work?.Cancel();
        _rows = [];
        ResultsList.ItemsSource = null;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? ErrorBrush : InfoBrush;
    }

    void SetAll(bool selected)
    {
        foreach (var row in _rows.Where(row => row.CanImport))
            row.IsSelected = selected;
    }

    void OnLoadClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    void OnBackClick(object sender, RoutedEventArgs e) => ShowSetup();

    void OnSelectAllClick(object sender, RoutedEventArgs e) => SetAll(true);

    void OnSelectNoneClick(object sender, RoutedEventArgs e) => SetAll(false);

    void OnImportClick(object sender, RoutedEventArgs e) => Import();

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    void OnSizeChanged(object sender, SizeChangedEventArgs e) => Scroll.MaxHeight = Math.Max(300, ActualHeight - 120);

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter when ResultsView.Visibility == Visibility.Visible:
                Import();
                break;
            case Key.Enter when Keyboard.FocusedElement is not TextBox:
                _ = LoadAsync();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
