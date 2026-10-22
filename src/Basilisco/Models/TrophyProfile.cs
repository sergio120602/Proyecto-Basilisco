using System.Text.Json.Serialization;

namespace Basilisco.Models;

/// <summary>Trofeo según lo difícil que es. El platino es el de tenerlos todos.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrophyTier>))]
public enum TrophyTier { Bronze, Silver, Gold, Platinum }

/// <summary>De dónde salen los trofeos de un juego que no es de Steam.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrophySource>))]
public enum TrophySource { Manual, RetroAchievements, Rpcs3, PlayStation }

/// <summary>Un trofeo del perfil de trofeos de un juego: creado por ti o leído de RetroAchievements, RPCS3 o PlayStation.</summary>
public sealed class OwnTrophy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public TrophyTier Tier { get; set; } = TrophyTier.Bronze;

    /// <summary>True si lo has creado tú (no se borra al volver a leer el servicio vinculado).</summary>
    public bool Custom { get; set; }

    /// <summary>Conseguido según el servicio vinculado.</summary>
    public bool Unlocked { get; set; }

    /// <summary>Conseguido a mano (con la activación manual encendida).</summary>
    public bool ManualUnlocked { get; set; }

    public DateTime? UnlockedAt { get; set; }
    public bool Hidden { get; set; }

    /// <summary>Icono (dirección de internet o archivo) y el de cuando aún no se tiene, si lo hay.</summary>
    public string? Icon { get; set; }
    public string? IconLocked { get; set; }

    /// <summary>Tanto por ciento de los jugadores que lo tienen, si el servicio lo da.</summary>
    public double? Percent { get; set; }

    /// <summary>True cuando el título y la descripción ya están en español (traducidos o porque ya venían así).</summary>
    public bool Translated { get; set; }

    [JsonIgnore]
    public bool IsUnlocked => Unlocked || ManualUnlocked;
}

/// <summary>Perfil de trofeos de un juego (para los que no tienen logros de Steam).</summary>
public sealed class TrophyProfile
{
    public TrophySource Source { get; set; } = TrophySource.Manual;

    /// <summary>Qué juego es en el servicio: el ID de RetroAchievements, la carpeta de RPCS3 o el de PlayStation.</summary>
    public string? SourceId { get; set; }

    /// <summary>Nombre del juego en el servicio, para enseñarlo.</summary>
    public string? SourceName { get; set; }

    /// <summary>Para PlayStation: «trophy» (PS3, PS4, Vita) o «trophy2» (PS5).</summary>
    public string? SourceService { get; set; }

    /// <summary>Con la activación manual encendida, un clic en un trofeo lo da por conseguido (o lo quita).</summary>
    public bool ManualActivation { get; set; } = true;

    public DateTime? Synced { get; set; }
    public List<OwnTrophy> Trophies { get; set; } = [];
}
