namespace Pavlov.Rcon;

public sealed record CatalogEntry(string Id, string Name, string? Note = null);

/// <summary>Known ids from the Pavlov wiki (Setting up a dedicated server, ItemIDs, Rcon pages).</summary>
public static class PavlovCatalog
{
    public const int BlueTeam = 0;
    public const int RedTeam = 1;

    public static IReadOnlyList<CatalogEntry> GameModes { get; } =
    [
        new("TDM", "Team Deathmatch"),
        new("DM", "Deathmatch"),
        new("SND", "Search & Destroy"),
        new("KOTH", "King of the Hill"),
        new("GUN", "Gun Game"),
        new("OITC", "One in the Chamber"),
        new("TANKTDM", "Tank TDM"),
        new("WW2GUN", "WW2 Gun Game"),
        new("TTT", "Trouble in Terrorist Town"),
        new("TTTclassic", "TTT Classic"),
        new("ZWV", "Zombies"),
        new("INFECTION", "Infection"),
        new("HIDE", "Hide"),
        new("PUSH", "Push"),
        new("PH", "Prop Hunt"),
    ];

    public static IReadOnlyList<CatalogEntry> StockMaps { get; } =
    [
        new("datacenter", "Datacenter"),
        new("sand", "Sand"),
        new("bridge", "Bridge"),
        new("containeryard", "Container Yard"),
        new("ogcontainers", "OG Containers", "Shack"),
        new("haguenau", "Haguenau", "Shack"),
        new("foundation", "Foundation", "Shack"),
        new("santorini", "Santorini"),
        new("station", "Station"),
        new("industry", "Industry"),
        new("siberia", "Prison Break"),
        new("hospital", "Hospital", "Zombies"),
        new("killhouse", "Killhouse"),
        new("range", "Range"),
        new("tutorial", "Tutorial"),
    ];

    public static IReadOnlyList<string> Skins { get; } =
    [
        "aurora", "clown", "cop", "farmer", "german", "kevin", "naked", "nato", "nato1", "nato2", "nato3", "nato4",
        "prisoner", "russian", "russian1", "russian2", "russian3", "russian4", "soviet", "us",
    ];

    /// <summary>Case-sensitive.</summary>
    public static IReadOnlyList<string> TttRoles { get; } =
    [
        "Innocent", "Traitor", "Detective", "Mercenary", "Glitch", "Assassin", "Jester", "Survivalist", "Sheriff", "Tank",
        "LoneWolf", "Zombie", "Hypnotist", "Soulmate", "Psychopath",
    ];

    public static IReadOnlyList<CatalogEntry> AmmoTypes { get; } =
    [
        new("0", "Unlimited", "No ammo boxes"),
        new("1", "Limited generic", "One box per weapon class"),
        new("2", "Limited specific", "One box per weapon"),
        new("3", "Custom", "The map decides"),
        new("4", "Limited special", "Only special weapons limited"),
        new("5", "Boxless", "Limited generic, no boxes"),
    ];

    public static string GameModeName(string id) =>
        GameModes.FirstOrDefault(mode => mode.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

    public static string MapName(string id) =>
        StockMaps.FirstOrDefault(map => map.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name
        ?? (id.StartsWith("UGC", StringComparison.OrdinalIgnoreCase) ? $"mod.io {id[3..]}" : id);

    public static string TeamName(int teamId) => teamId switch
    {
        BlueTeam => "Blue",
        RedTeam => "Red",
        _ => $"Team {teamId}",
    };
}
