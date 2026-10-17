namespace Basilisco.Models;

/// <summary>Consola a la que pertenece un juego. Decide el borde de su caja.</summary>
public sealed record Platform(string Id, string Name);

public static class Platforms
{
    public static readonly IReadOnlyList<Platform> All =
    [
        new("ps1", "PS1"),
        new("ps2", "PS2"),
        new("ps3", "PS3"),
        new("psp", "PSP"),
        new("xbox", "Xbox"),
        new("x360", "Xbox 360"),
        new("n64", "Nintendo 64"),
        new("gc", "GameCube"),
        new("wii", "Wii"),
        new("wiiu", "Wii U"),
        new("gb", "Game Boy"),
        new("gba", "Game Boy Advance"),
        new("nds", "Nintendo DS"),
        new("pc", "PC"),
    ];

    public static Platform? Find(string? id) => id is null ? null : All.FirstOrDefault(platform => platform.Id == id);
}
