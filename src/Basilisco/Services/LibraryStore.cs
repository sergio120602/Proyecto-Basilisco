using System.IO;
using System.Text.Json;
using Basilisco.Models;

namespace Basilisco.Services;

public sealed class LibraryStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string FilePath { get; } = Path.Combine(AppInfo.DataDirectory, "library.json");

    public Library Load()
    {
        if (!File.Exists(FilePath))
            return new Library();

        try
        {
            var json = File.ReadAllText(FilePath);
            var library = JsonSerializer.Deserialize<Library>(json, Options) ?? new Library();
            if (library.Version < Library.CurrentVersion)
            {
                Migrate(library);
                Save(library);
            }
            return library;
        }
        catch (Exception)
        {
            // Guardamos una copia del archivo dañado para no perder la biblioteca al volver a guardar.
            try { File.Copy(FilePath, $"{FilePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak", overwrite: true); }
            catch (IOException) { }
            return new Library();
        }
    }

    /// <summary>Pone al día una biblioteca de una versión anterior de Basilisco.</summary>
    static void Migrate(Library library)
    {
        if (library.Version < 2)
        {
            // Hasta la 0.1.6, «gba» era la Game Boy (se llamó Game Boy Advance al principio y luego se renombró).
            // Ahora la Game Boy es «gb» y «gba» queda para la Game Boy Advance de verdad.
            foreach (var game in library.Games.Where(game => game.Platform == "gba"))
                game.Platform = "gb";

            // Sus franjas también: borders/gba.png era la de Game Boy.
            foreach (var suffix in new[] { "", "-top" })
            {
                var from = Path.Combine(AppInfo.BordersDirectory, $"gba{suffix}.png");
                var to = Path.Combine(AppInfo.BordersDirectory, $"gb{suffix}.png");
                try
                {
                    if (File.Exists(from) && !File.Exists(to))
                        File.Move(from, to);
                }
                catch (IOException) { }
            }
        }
        library.Version = Library.CurrentVersion;
    }

    public bool Save(Library library)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(library, Options));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
