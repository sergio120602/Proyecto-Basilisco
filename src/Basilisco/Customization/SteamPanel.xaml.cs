using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Basilisco.Services;

namespace Basilisco.Customization;

/// <summary>Un juego de Steam en la lista para revisar.</summary>
public sealed class SteamRow(SteamApp app, bool alreadyInLibrary) : INotifyPropertyChanged
{
    bool _isSelected = !alreadyInLibrary;

    public SteamApp App { get; } = app;
    public string Name => App.Name;
    public bool CanImport { get; } = !alreadyInLibrary;

    public string Detail
    {
        get
        {
            var parts = new List<string>
            {
                App.Source == SteamSource.Family ? $"Steam Familiar{(App.Owner is null ? "" : $" · de {App.Owner}")}" : "Tuyo",
                App.Installed ? "Instalado" : "No instalado",
            };
            if (App.PlaytimeMinutes > 0)
                parts.Add(App.PlaytimeMinutes < 60 ? $"{App.PlaytimeMinutes} min jugados" : $"{App.PlaytimeMinutes / 60} h jugadas");
            if (!CanImport)
                parts.Insert(0, "Ya está en tu biblioteca");
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

/// <summary>Lo decidido en la ventana de Steam.</summary>
public sealed record SteamImportResult(List<SteamApp> Apps);

/// <summary>
/// Lo que se sabe de la familia tras leer Steam: sus miembros (vacío si no se ha pedido Steam Familiar) y quién tiene cada
/// juego. Sirve para el menú «Steam familiar», también con los juegos que ya estaban en la biblioteca.
/// </summary>
/// <param name="Owners">SteamID de los que tienen cada juego (por su AppID).</param>
public sealed record SteamFamilyInfo(List<Basilisco.Models.FamilyMember> Members, Dictionary<int, List<string>> Owners);

/// <summary>
/// Importa la cuenta de Steam: los juegos instalados (leídos del PC), toda la biblioteca (Web API) y los de
/// Steam Familiar (con el token de sesión). Junta las tres fuentes y deja revisar antes de importar.
/// </summary>
public partial class SteamPanel : UserControl
{
    static readonly Brush ErrorBrush = Brushes.Firebrick;
    static Brush InfoBrush => (Brush)Application.Current.Resources["MutedBrush"];
    static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));

    AppSettings _settings = new();
    Func<SteamApp, bool> _isInLibrary = _ => false;
    SteamAccount? _account;
    List<SteamRow> _rows = [];
    CancellationTokenSource? _work;

    /// <summary>Se ha leído Steam: quién tiene cada juego y, con Steam Familiar, los miembros de la familia.</summary>
    public event Action<SteamFamilyInfo>? FamilyLoaded;

    // El token de sesión solo vive en memoria mientras Basilisco está abierto: da acceso a la cuenta.
    static string? _sessionToken;

    public event Action<SteamImportResult>? ImportRequested;

    public bool IsOpen => Visibility == Visibility.Visible;

    public SteamPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <param name="isInLibrary">Para marcar los juegos que ya estaban importados.</param>
    public void Open(AppSettings settings, Func<SteamApp, bool> isInLibrary)
    {
        _settings = settings;
        _isInLibrary = isInLibrary;
        _account = SteamLocal.Accounts().FirstOrDefault();

        AccountText.Text = SteamLocal.SteamPath() is null
            ? "No se ha encontrado Steam en este PC. Puedes importar tu biblioteca por internet con la clave o el token."
            : _account is null
                ? "Steam está instalado, pero no hay ninguna cuenta con la sesión iniciada en este PC."
                : $"Cuenta de Steam: {_account.PersonaName}";

        ApiKeyBox.Text = settings.SteamApiKey ?? "";
        TokenBox.Text = _sessionToken ?? "";
        InstalledBox.IsEnabled = SteamLocal.SteamPath() is not null;
        InstalledBox.IsChecked = InstalledBox.IsEnabled;
        ShowSetup();
        UpdateOptions();
        UpdateTokenStatus();
        SetStatus("");
        Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
    }

    void ShowSetup()
    {
        SetupView.Visibility = Visibility.Visible;
        ResultsView.Visibility = Visibility.Collapsed;
    }

    void UpdateOptions()
    {
        OwnedOptions.Visibility = OwnedBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TokenOptions.Visibility = FamilyBox.IsChecked == true || OwnedBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    (string Token, string? SteamId, DateTime? Expires)? CurrentToken => SteamWeb.ParseToken(TokenBox.Text);

    void UpdateTokenStatus()
    {
        if (TokenBox.Text.Trim().Length == 0)
        {
            TokenStatus.Text = "";
            return;
        }

        if (CurrentToken is not { } token)
        {
            TokenStatus.Text = "Esto no parece un token. Copia todo el texto de la página (empieza por {\"success\":1…).";
            TokenStatus.Foreground = ErrorBrush;
            return;
        }

        if (token.Expires is { } expires && expires <= DateTime.Now)
        {
            TokenStatus.Text = "Este token ya ha caducado. Vuelve a abrir la página y copia el nuevo.";
            TokenStatus.Foreground = ErrorBrush;
            return;
        }

        TokenStatus.Text = token.Expires is { } until ? $"Token válido hasta las {until:HH:mm} del {until:dd/MM}." : "Token pegado.";
        TokenStatus.Foreground = OkBrush;
    }

    async Task LoadAsync()
    {
        var wantInstalled = InstalledBox.IsChecked == true;
        var wantOwned = OwnedBox.IsChecked == true;
        var wantFamily = FamilyBox.IsChecked == true;
        if (!wantInstalled && !wantOwned && !wantFamily)
        {
            SetStatus("Marca al menos una opción.", error: true);
            return;
        }

        var key = ApiKeyBox.Text.Trim();
        var token = CurrentToken is { } parsed && (parsed.Expires is null || parsed.Expires > DateTime.Now) ? parsed : ((string, string?, DateTime?)?)null;
        var steamId = token?.Item2 ?? _account?.SteamId;

        if (wantOwned && key.Length == 0 && token is null)
        {
            SetStatus("Para toda la biblioteca pega tu clave de la Web API o el token de sesión.", error: true);
            return;
        }
        if (wantFamily && token is null)
        {
            SetStatus("Para Steam Familiar hace falta el token de sesión (y que no haya caducado).", error: true);
            return;
        }
        if ((wantOwned || wantFamily) && steamId is null)
        {
            SetStatus("No se sabe qué cuenta de Steam usar: inicia sesión en Steam en este PC o pega el token.", error: true);
            return;
        }

        // La clave se guarda (no caduca y solo permite leer datos públicos); el token, solo en memoria.
        if (key.Length > 0 && key != _settings.SteamApiKey)
        {
            _settings.SteamApiKey = key;
            _settings.Save();
        }
        _sessionToken = token?.Item1;

        _work?.Cancel();
        var cancellation = (_work = new CancellationTokenSource()).Token;
        LoadButton.IsEnabled = false;
        var apps = new Dictionary<int, SteamApp>();
        var notes = new List<string>();
        var members = new List<Basilisco.Models.FamilyMember>();
        // Los tuyos que vienen con la familia: sin «toda la biblioteca» no se ofrecen, pero se sabe que son tuyos.
        var otherOwners = new Dictionary<int, IReadOnlyList<string>>();
        try
        {
            if (wantInstalled)
            {
                SetStatus("Leyendo los juegos instalados…");
                foreach (var app in SteamLocal.InstalledApps(_account?.SteamId))
                    apps[app.AppId] = app;
            }

            if (wantOwned)
            {
                SetStatus("Pidiendo tu biblioteca a Steam…");
                foreach (var app in await SteamWeb.OwnedGamesAsync(steamId!, key.Length > 0 ? key : null, key.Length > 0 ? null : token?.Item1, cancellation))
                    Merge(apps, app);
            }

            if (wantFamily)
            {
                SetStatus("Pidiendo la biblioteca de tu familia a Steam…");
                try
                {
                    var (familyName, family, familyMembers) = await SteamWeb.FamilyGamesAsync(steamId!, token!.Value.Item1, key.Length > 0 ? key : null, cancellation);
                    members = familyMembers;
                    foreach (var app in family)
                    {
                        if (app.Source == SteamSource.Own && !wantOwned && !apps.ContainsKey(app.AppId))
                            otherOwners[app.AppId] = app.OwnerIds ?? [];
                        else
                            Merge(apps, app);
                    }
                    if (familyName is { Length: > 0 })
                        notes.Add($"familia «{familyName}»");
                }
                catch (ArtProviderException ex) when (apps.Count > 0)
                {
                    // Si lo demás ha funcionado, se enseña lo que hay y se avisa del fallo de la familia.
                    notes.Add($"no se ha podido leer la familia: {ex.Message}");
                }
            }
        }
        catch (ArtProviderException ex)
        {
            SetStatus(ex.Message, error: true);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }

        var owners = apps.Values.Concat(otherOwners.Select(entry => new SteamApp(entry.Key, "", SteamSource.Own, false, null, null, 0, entry.Value)))
            .Where(app => app.OwnerIds is { Count: > 0 })
            .ToDictionary(app => app.AppId, app => app.OwnerIds!.Distinct().ToList());
        FamilyLoaded?.Invoke(new SteamFamilyInfo(members, owners));

        ShowResults(apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), notes);
    }

    /// <summary>Junta un juego con lo que ya se sabía de él (instalado, horas jugadas, dueño…).</summary>
    static void Merge(Dictionary<int, SteamApp> apps, SteamApp app)
    {
        if (!apps.TryGetValue(app.AppId, out var known))
        {
            apps[app.AppId] = app with { Installed = SteamLocal.IsInstalled(app.AppId) };
            return;
        }

        // Si es tuyo según la biblioteca, es tuyo aunque lo instalara alguien de la familia.
        var source = known.Source == SteamSource.Own || app.Source == SteamSource.Own ? SteamSource.Own : SteamSource.Family;
        apps[app.AppId] = known with
        {
            Source = source,
            Owner = source == SteamSource.Family ? app.Owner ?? known.Owner : null,
            OwnerIds = (known.OwnerIds ?? []).Concat(app.OwnerIds ?? []).Distinct().ToList() is { Count: > 0 } ids ? ids : null,
            PlaytimeMinutes = Math.Max(known.PlaytimeMinutes, app.PlaytimeMinutes),
            Name = known.Name.Length > 0 ? known.Name : app.Name,
        };
    }

    void ShowResults(List<SteamApp> apps, List<string> notes)
    {
        _rows = apps.Select(app => new SteamRow(app, _isInLibrary(app))).ToList();
        ResultsList.ItemsSource = _rows;
        SetupView.Visibility = Visibility.Collapsed;
        ResultsView.Visibility = Visibility.Visible;

        var family = apps.Count(app => app.Source == SteamSource.Family);
        var already = _rows.Count(row => !row.CanImport);
        SummaryText.Text = $"{apps.Count:N0} juegos" + (family > 0 ? $" · {family:N0} de la familia" : "") + (already > 0 ? $" · {already:N0} ya importados" : "");
        ImportButton.IsEnabled = _rows.Any(row => row.CanImport);
        SetStatus(notes.Count > 0 ? char.ToUpper(string.Join(" · ", notes)[0]) + string.Join(" · ", notes)[1..] : "");
    }

    void Import()
    {
        var selected = _rows.Where(row => row.CanImport && row.IsSelected).Select(row => row.App).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Marca al menos un juego para importar.", error: true);
            return;
        }

        var result = new SteamImportResult(selected);
        Close();
        ImportRequested?.Invoke(result);
    }

    void Close()
    {
        _work?.Cancel();
        _rows = [];
        ResultsList.ItemsSource = null;
        Visibility = Visibility.Collapsed;
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

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    void OnOptionsChanged(object sender, RoutedEventArgs e) => UpdateOptions();

    void OnTokenChanged(object sender, TextChangedEventArgs e) => UpdateTokenStatus();

    void OnGetKeyClick(object sender, RoutedEventArgs e) => OpenUrl(SteamWeb.ApiKeyUrl);

    void OnGetTokenClick(object sender, RoutedEventArgs e) => OpenUrl(SteamWeb.TokenUrl);

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
