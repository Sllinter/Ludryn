using System.Text.Json;

namespace Ludryn.Services;

public static class CustomGameService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "custom-games.json");

    private static readonly CustomGameStore Store = Load();

    public static IReadOnlyList<CustomGameConfig> GetGames() => Store.Games.ToList();

    public static CustomGameConfig Add(
        string title,
        string executablePath,
        string arguments,
        string platform)
    {
        var existing = Store.Games.FirstOrDefault(game =>
            string.Equals(game.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(game.Arguments, arguments, StringComparison.Ordinal));

        if (existing is not null)
        {
            existing.Title = title;
            existing.Platform = platform;
            Save();
            return existing;
        }

        var game = new CustomGameConfig
        {
            Id = $"custom-game-{Guid.NewGuid():N}",
            Title = title,
            ExecutablePath = executablePath,
            Arguments = arguments,
            Platform = platform,
            AddedAt = DateTime.Now
        };
        Store.Games.Add(game);
        Save();
        return game;
    }

    public static bool Remove(string id)
    {
        var removed = Store.Games.RemoveAll(game =>
            string.Equals(game.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Save();
        }

        return removed;
    }

    private static CustomGameStore Load()
    {
        try
        {
            return File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<CustomGameStore>(File.ReadAllText(ConfigPath)) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            LudrynLogger.Error("library", "Falha ao carregar os jogos adicionados pelo usuario.", ex);
            return new();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Store, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (Exception ex)
        {
            LudrynLogger.Error("library", "Falha ao salvar os jogos adicionados pelo usuario.", ex);
        }
    }
}

public sealed class CustomGameStore
{
    public List<CustomGameConfig> Games { get; set; } = [];
}

public sealed class CustomGameConfig
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Platform { get; set; } = "PC";
    public DateTime AddedAt { get; set; }
}
