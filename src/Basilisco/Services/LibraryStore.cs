using System.IO;
using System.Text.Json;
using Basilisco.Models;

namespace Basilisco.Services;

public sealed class LibraryStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string FilePath { get; } = Path.Combine(AppInfo.DataDirectory, "library.json");

    public List<Game> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<LibraryFile>(json, Options)?.Games ?? [];
        }
        catch (Exception)
        {
            // Guardamos una copia del archivo dañado para no perder la biblioteca al volver a guardar.
            try { File.Copy(FilePath, $"{FilePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak", overwrite: true); }
            catch (IOException) { }
            return [];
        }
    }

    public bool Save(List<Game> games)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new LibraryFile { Games = games }, Options));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    sealed class LibraryFile
    {
        public int Version { get; set; } = 1;
        public List<Game> Games { get; set; } = [];
    }
}
