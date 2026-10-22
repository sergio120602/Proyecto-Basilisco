using System.Globalization;
using System.Text;

namespace Basilisco.Services;

/// <summary>
/// Búsqueda de juegos por nombre para el buscador de arriba: encuentra los nombres que más se parecen a lo escrito, sin
/// importar mayúsculas, tildes ni signos, con números romanos o en cifras («Final Fantasy 7» encuentra «Final Fantasy VII»)
/// y aunque haya alguna letra de más o de menos («holow knigt» encuentra «Hollow Knight»).
/// </summary>
public static class GameSearch
{
    /// <summary>Los <paramref name="max"/> elementos cuyo nombre más se parece a <paramref name="query"/>, del más parecido al menos.</summary>
    public static List<T> Find<T>(IEnumerable<T> items, Func<T, string> name, string query, int max)
    {
        var q = Normalize(query);
        if (q.Length == 0)
            return [];
        var words = q.Split(' ');
        return items
            .Select(item => (Item: item, Name: name(item), Score: Score(Normalize(name(item)), q, words)))
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score).ThenBy(entry => entry.Name.Length).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(entry => entry.Item)
            .ToList();
    }

    static int Score(string name, string query, string[] words)
    {
        if (name.Length == 0)
            return 0;
        if (name == query)
            return 1000;
        if (name.StartsWith(query, StringComparison.Ordinal))
            return 900;

        var nameWords = name.Split(' ');
        if (nameWords.Any(word => word.StartsWith(query, StringComparison.Ordinal)))
            return 800;
        if (name.Contains(query, StringComparison.Ordinal))
            return 700;

        // Cada palabra escrita es el principio de alguna del nombre («zel oca» → «Zelda Ocarina of Time»).
        if (words.All(word => nameWords.Any(n => n.StartsWith(word, StringComparison.Ordinal))))
            return 600;

        // Con alguna letra mal: cada palabra escrita (de 4 letras o más) se parece a una del nombre.
        var fuzzy = 0;
        foreach (var word in words)
        {
            var allowed = word.Length >= 7 ? 2 : word.Length >= 4 ? 1 : 0;
            var best = nameWords.Min(n => Distance(word, n.Length > word.Length ? n[..word.Length] : n));
            var whole = nameWords.Min(n => Distance(word, n));
            var distance = Math.Min(best, whole);
            if (distance > allowed)
                return SubsequenceScore(name, query);
            fuzzy += 10 - distance * 3;
        }
        return 400 + fuzzy;
    }

    /// <summary>Las letras escritas aparecen en orden en el nombre («hk» no; «hlwknght» sí): lo menos parecido que se acepta.</summary>
    static int SubsequenceScore(string name, string query)
    {
        var compact = query.Replace(" ", "");
        if (compact.Length < 4)
            return 0;
        var i = 0;
        foreach (var c in name)
        {
            if (i < compact.Length && c == compact[i])
                i++;
        }
        return i == compact.Length ? 200 : 0;
    }

    /// <summary>Cuántas letras hay que cambiar, poner o quitar para pasar de una palabra a otra.</summary>
    static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    static readonly Dictionary<string, string> Roman = new()
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
        ["xi"] = "11", ["xii"] = "12", ["xiii"] = "13", ["xv"] = "15", ["xvi"] = "16",
    };

    /// <summary>En minúsculas, sin tildes, solo letras y números separados por un espacio, y los números romanos en cifras.</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => Roman.GetValueOrDefault(word, word));
        return string.Join(' ', words);
    }
}
