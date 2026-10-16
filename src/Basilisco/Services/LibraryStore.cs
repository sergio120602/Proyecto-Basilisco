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
            return JsonSerializer.Deserialize<Library>(json, Options) ?? new Library();
        }
        catch (Exception)
        {
            // Guardamos una copia del archivo dañado para no perder la biblioteca al volver a guardar.
            try { File.Copy(FilePath, $"{FilePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak", overwrite: true); }
            catch (IOException) { }
            return new Library();
        }
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
