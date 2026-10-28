using System.Windows;
using Basilisco.Customization;
using Basilisco.Models;
using Basilisco.Services;

namespace Basilisco;

/// <summary>
/// Grupo «Nº Jugadores»: los juegos según cuántos pueden jugar a la vez (1, 2, 3-4, 5 o más). El número sale de la base de
/// datos de LaunchBox (la que ya se descarga para las carátulas, sin internet) o se pone a mano en Personalizar.
/// </summary>
public partial class MainWindow
{
    /// <summary>El grupo que se está viendo: 1, 2, 3 (3-4), 5 (5 o más) o 0 (sin datos); null si ninguno.</summary>
    int? _activePlayers;

    static int PlayersGroup(Game game) => game.Players switch
    {
        null or <= 0 => 0,
        1 => 1,
        2 => 2,
        3 or 4 => 3,
        _ => 5,
    };

    static string PlayersName(int group) => group switch
    {
        1 => "1 jugador",
        2 => "2 jugadores",
        3 => "3-4 jugadores",
        5 => "5 o más jugadores",
        _ => "Sin datos de jugadores",
    };

    void OnPlayersTabClick(object sender, RoutedEventArgs e)
    {
        RefreshFilterTabs();
        _ = OpenPlayersStripAsync();
    }

    /// <summary>La franja negra (como la de consolas y sagas) con cada grupo y cuántos juegos tiene.</summary>
    async Task OpenPlayersStripAsync()
    {
        await FillPlayersAsync();
        static string Count(int count) => count == 1 ? "1 juego" : $"{count} juegos";

        var tiles = new List<FilterTile>
        {
            new("Todos los juegos", null, Count(Games.Count), _activePlayers is null, () => SelectView(null, null)),
        };
        foreach (var group in new[] { 1, 2, 3, 5, 0 })
        {
            var count = Games.Count(game => PlayersGroup(game) == group);
            if (count == 0 && group != _activePlayers)
                continue;
            var chosen = group;
            tiles.Add(new FilterTile(PlayersName(group), null, $"{PlayersName(group)} · {Count(count)}", group == _activePlayers,
                () => SelectView(null, null, players: chosen)));
        }
        if (!LaunchBoxDatabase.IsDownloaded)
            tiles.Add(new FilterTile("¿De dónde sale?", null, "Descarga LaunchBox (Personalizar → Buscar en LaunchBox) o ponlo en Personalizar (E)", false,
                () => ShowToast("El número de jugadores sale de LaunchBox: descárgalo desde Personalizar → Buscar en LaunchBox, o escríbelo en Personalizar (E)"),
                IsAction: true));

        var scale = _uiScale.ScaleX;
        var top = (HeaderBar.Margin.Top + HeaderBar.ActualHeight * scale) / scale + 6;
        FilterStrip.Open(tiles, top, _padMode, "Nº JUGADORES");
    }

    /// <summary>
    /// Busca en LaunchBox el número de jugadores de los juegos que aún no lo tienen (una sola vez por juego: los que no
    /// aparecen no se vuelven a buscar). La ficha se lee de una pasada para todos.
    /// </summary>
    async Task FillPlayersAsync()
    {
        var missing = Games.Where(game => game.Players is null && !game.PlayersLooked).ToList();
        if (missing.Count == 0 || !LaunchBoxDatabase.IsDownloaded)
            return;
        ShowToast("Buscando en LaunchBox cuántos jugadores tiene cada juego…");
        if (!await LaunchBoxDatabase.TryLoadAsync(CancellationToken.None))
            return;
        var found = await Task.Run(() =>
        {
            var matches = missing.Select(game => (Game: game, Match: LaunchBoxDatabase.FindGame(game.Name, LaunchBoxDatabase.PlatformName(game.Platform))))
                .Where(entry => entry.Match is not null).ToList();
            var players = LaunchBoxDatabase.MaxPlayers(matches.Select(entry => entry.Match!.Id).ToList());
            return matches.Where(entry => players.ContainsKey(entry.Match!.Id)).Select(entry => (entry.Game, Players: players[entry.Match!.Id])).ToList();
        });
        foreach (var game in missing)
            game.PlayersLooked = true;
        foreach (var (game, players) in found)
            game.Players ??= players;
        Save();
        RefreshFilterTabs();
        var left = missing.Count - found.Count;
        ShowToast(left == 0 ? $"Número de jugadores de {found.Count} juegos" : $"Número de jugadores de {found.Count} juegos · {left} sin datos (ponlo en Personalizar)");
    }
}
