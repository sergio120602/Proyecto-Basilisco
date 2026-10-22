using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Basilisco.Models;

/// <summary>Un miembro de tu familia de Steam (tú incluido), para el menú «Steam familiar».</summary>
/// <param name="SteamId">SteamID64 de la cuenta. Si aún no se ha leído la familia de Steam, «nombre:» y su nombre.</param>
/// <param name="AvatarUrl">Foto de perfil de Steam (para ponerla de imagen), si se sabe.</param>
/// <param name="IsMe">True si es tu cuenta.</param>
public sealed record FamilyMember(string SteamId, string Name, string? AvatarUrl = null, bool IsMe = false)
{
    const string NamePrefix = "nombre:";

    /// <summary>Miembro sacado solo de su nombre (bibliotecas importadas antes de la 0.6.7, sin la familia completa).</summary>
    public static FamilyMember FromName(string name, bool isMe = false) => new(NamePrefix + name, name, IsMe: isMe);

    [JsonIgnore]
    public bool IsKnownAccount => !SteamId.StartsWith(NamePrefix, StringComparison.Ordinal);

    /// <summary>Nombre para los archivos de su imagen (basilisco-data/family): el SteamID, o un resumen del nombre.</summary>
    [JsonIgnore]
    public string Key => IsKnownAccount
        ? SteamId
        : "n" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Name.ToLowerInvariant())))[..12].ToLowerInvariant();

    /// <summary>
    /// ¿Es suyo el juego? Con los dueños leídos de Steam, si está entre ellos (un juego puede ser de varios miembros, y
    /// entonces está en la ficha de cada uno). En bibliotecas antiguas, que solo guardaban quién lo comparte: los que no
    /// comparte nadie son tuyos, y los demás, de quien los comparte.
    /// </summary>
    public bool Owns(Game game)
    {
        if (game.SteamAppId is null)
            return false;
        if (game.SteamOwnerIds.Count > 0 && IsKnownAccount)
            return game.SteamOwnerIds.Contains(SteamId);
        return IsMe
            ? game.SteamOwner is null
            : string.Equals(game.SteamOwner, Name, StringComparison.CurrentCultureIgnoreCase) || game.SteamOwner == SteamId;
    }
}
