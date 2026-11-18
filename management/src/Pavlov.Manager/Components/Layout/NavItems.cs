using Microsoft.AspNetCore.Components.Routing;

namespace Pavlov.Manager.Components.Layout;

public sealed record NavItem(string Href, string Label, string Icon, NavLinkMatch Match = NavLinkMatch.Prefix);

public static class NavItems
{
    public static IReadOnlyList<NavItem> All { get; } =
    [
        new("", "Overview", "overview", NavLinkMatch.All),
        new("players", "Players", "players"),
        new("maps", "Maps", "map"),
        new("settings", "Server", "server"),
        new("console", "Console", "console"),
    ];
}
