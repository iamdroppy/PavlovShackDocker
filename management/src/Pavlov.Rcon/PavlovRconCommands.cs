namespace Pavlov.Rcon;

/// <summary>
/// Typed wrappers for every command the server lists in <c>Help</c>.
/// <c>uniqueId</c> is the Oculus username on Shack and the SteamID64 on PC.
/// Team ids: 0 = blue, 1 = red. Actions return the raw reply; check
/// <see cref="RconResponse.Successful"/> or call <see cref="RconResponse.EnsureSuccess"/>.
/// </summary>
public static class PavlovRconCommands
{
    extension(PavlovRconClient client)
    {
        // ---- Queries -----------------------------------------------------------

        public async Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
        {
            var response = await Run(client, "ServerInfo", cancellationToken).ConfigureAwait(false);
            return response.Read("ServerInfo", PavlovJsonContext.Default.ServerInfo)
                ?? throw new RconCommandFailedException(response);
        }

        public async Task<IReadOnlyList<PlayerDetail>> InspectAllAsync(CancellationToken cancellationToken = default)
        {
            var response = await Run(client, "InspectAll", cancellationToken).ConfigureAwait(false);
            return response.Read("InspectList", PavlovJsonContext.Default.ListPlayerDetail) ?? [];
        }

        public async Task<IReadOnlyList<PlayerDetail>> InspectTeamAsync(int teamId, CancellationToken cancellationToken = default)
        {
            var response = await Run(client, "InspectTeam", cancellationToken, RconCommand.Number(teamId)).ConfigureAwait(false);
            return response.Read("InspectList", PavlovJsonContext.Default.ListPlayerDetail) ?? [];
        }

        /// <summary>Returns null when the player isn't on the server.</summary>
        public async Task<PlayerDetail?> InspectPlayerAsync(string uniqueId, CancellationToken cancellationToken = default)
        {
            var response = await Run(client, "InspectPlayer", cancellationToken, uniqueId).ConfigureAwait(false);
            return response.Successful ? response.Read("PlayerInfo", PavlovJsonContext.Default.PlayerDetail) : null;
        }

        public async Task<IReadOnlyList<PlayerListEntry>> RefreshListAsync(CancellationToken cancellationToken = default)
        {
            var response = await Run(client, "RefreshList", cancellationToken).ConfigureAwait(false);
            return response.Read("PlayerList", PavlovJsonContext.Default.ListPlayerListEntry) ?? [];
        }

        public async Task<IReadOnlyList<MapRotationEntry>> GetMapListAsync(CancellationToken cancellationToken = default)
        {
            var response = await Run(client, "MapList", cancellationToken).ConfigureAwait(false);
            return response.Read("MapList", PavlovJsonContext.Default.ListMapRotationEntry) ?? [];
        }

        /// <summary>Item ids usable with GiveItem/GiveAll, including the current map's items.</summary>
        public Task<IReadOnlyList<string>> GetItemListAsync(CancellationToken cancellationToken = default) =>
            ReadStrings(client, "ItemList", "ItemList", cancellationToken);

        public Task<IReadOnlyList<string>> GetBanListAsync(CancellationToken cancellationToken = default) =>
            ReadStrings(client, "Banlist", "BanList", cancellationToken);

        public Task<IReadOnlyList<string>> GetModeratorListAsync(CancellationToken cancellationToken = default) =>
            ReadStrings(client, "ModeratorList", "ModeratorList", cancellationToken);

        public Task<IReadOnlyList<string>> GetUgcModListAsync(CancellationToken cancellationToken = default) =>
            ReadStrings(client, "UGCModList", "ModList", cancellationToken);

        /// <summary>Command signatures, e.g. <c>"SwitchMap MapId GameMode"</c>.</summary>
        public Task<IReadOnlyList<string>> GetHelpAsync(CancellationToken cancellationToken = default) =>
            ReadStrings(client, "Help", "Help", cancellationToken);

        // ---- Players -----------------------------------------------------------

        public Task<RconResponse> KickAsync(string uniqueId, CancellationToken cancellationToken = default) =>
            Run(client, "Kick", cancellationToken, uniqueId);

        /// <summary>Kicks and permanently bans (writes blacklist.txt).</summary>
        public Task<RconResponse> BanAsync(string uniqueId, CancellationToken cancellationToken = default) =>
            Run(client, "Ban", cancellationToken, uniqueId);

        public Task<RconResponse> UnbanAsync(string uniqueId, CancellationToken cancellationToken = default) =>
            Run(client, "Unban", cancellationToken, uniqueId);

        public Task<RconResponse> KillAsync(string uniqueId, CancellationToken cancellationToken = default) =>
            Run(client, "Kill", cancellationToken, uniqueId);

        /// <summary>Direct damage that ignores armour; can be lethal.</summary>
        public Task<RconResponse> SlapAsync(string uniqueId, int damage, CancellationToken cancellationToken = default) =>
            Run(client, "Slap", cancellationToken, uniqueId, RconCommand.Number(damage));

        /// <summary>Blocks (true) or allows (false) the player's voice chat.</summary>
        public Task<RconResponse> GagAsync(string uniqueId, bool gagged, CancellationToken cancellationToken = default) =>
            Run(client, "Gag", cancellationToken, uniqueId, RconCommand.Bool(gagged));

        /// <summary>Kills the player and moves them; they lose cash, score and gear.</summary>
        public Task<RconResponse> SwitchTeamAsync(string uniqueId, int teamId, CancellationToken cancellationToken = default) =>
            Run(client, "SwitchTeam", cancellationToken, uniqueId, RconCommand.Number(teamId));

        public Task<RconResponse> GiveItemAsync(string uniqueId, string itemId, CancellationToken cancellationToken = default) =>
            Run(client, "GiveItem", cancellationToken, uniqueId, itemId);

        public Task<RconResponse> GiveAllAsync(int teamId, string itemId, CancellationToken cancellationToken = default) =>
            Run(client, "GiveAll", cancellationToken, RconCommand.Number(teamId), itemId);

        public Task<RconResponse> GiveCashAsync(string uniqueId, int amount, CancellationToken cancellationToken = default) =>
            Run(client, "GiveCash", cancellationToken, uniqueId, RconCommand.Number(amount));

        public Task<RconResponse> GiveTeamCashAsync(int teamId, int amount, CancellationToken cancellationToken = default) =>
            Run(client, "GiveTeamCash", cancellationToken, RconCommand.Number(teamId), RconCommand.Number(amount));

        /// <summary>Sets cash to 0-16000.</summary>
        public Task<RconResponse> SetCashAsync(string uniqueId, int amount, CancellationToken cancellationToken = default) =>
            Run(client, "SetCash", cancellationToken, uniqueId, RconCommand.Number(Math.Clamp(amount, 0, 16000)));

        /// <summary>Lasts until the player leaves, switches team or the map changes.</summary>
        public Task<RconResponse> SetPlayerSkinAsync(string uniqueId, string skinId, CancellationToken cancellationToken = default) =>
            Run(client, "SetPlayerSkin", cancellationToken, uniqueId, skinId);

        /// <summary>Moves <paramref name="uniqueId"/> to <paramref name="targetUniqueId"/>; both must be alive.</summary>
        public Task<RconResponse> TeleportAsync(string uniqueId, string targetUniqueId, CancellationToken cancellationToken = default) =>
            Run(client, "Teleport", cancellationToken, uniqueId, targetUniqueId);

        /// <summary>Admin menu access and vote-kick immunity (writes mods.txt).</summary>
        public Task<RconResponse> AddModeratorAsync(string uniqueId, CancellationToken cancellationToken = default) =>
            Run(client, "AddMod", cancellationToken, uniqueId);

        public Task<RconResponse> RemoveModeratorAsync(string uniqueId, CancellationToken cancellationToken = default) =>
            Run(client, "RemoveMod", cancellationToken, uniqueId);

        // ---- Maps & match ------------------------------------------------------

        public Task<RconResponse> SwitchMapAsync(string mapId, string gameMode, CancellationToken cancellationToken = default) =>
            Run(client, "SwitchMap", cancellationToken, mapId, gameMode);

        public Task<RconResponse> RotateMapAsync(CancellationToken cancellationToken = default) =>
            Run(client, "RotateMap", cancellationToken);

        /// <summary>Appends to the rotation (writes Game.ini).</summary>
        public Task<RconResponse> AddMapRotationAsync(string mapId, string gameMode, CancellationToken cancellationToken = default) =>
            Run(client, "AddMapRotation", cancellationToken, mapId, gameMode);

        /// <summary>Removes the first match; both values are case-sensitive.</summary>
        public Task<RconResponse> RemoveMapRotationAsync(string mapId, string gameMode, CancellationToken cancellationToken = default) =>
            Run(client, "RemoveMapRotation", cancellationToken, mapId, gameMode);

        public Task<RconResponse> ResetSndAsync(CancellationToken cancellationToken = default) =>
            Run(client, "ResetSND", cancellationToken);

        /// <summary>Pauses for 1-3600 seconds; 0 resumes after a 15 s countdown.</summary>
        public Task<RconResponse> PauseMatchAsync(int seconds, CancellationToken cancellationToken = default) =>
            Run(client, "PauseMatch", cancellationToken, RconCommand.Number(Math.Clamp(seconds, 0, 3600)));

        public Task<RconResponse> SetTimeLimitAsync(int amount, CancellationToken cancellationToken = default) =>
            Run(client, "SetTimeLimit", cancellationToken, RconCommand.Number(amount));

        public Task<RconResponse> ClearEmptyVehiclesAsync(CancellationToken cancellationToken = default) =>
            Run(client, "ClearEmptyVehicles", cancellationToken);

        public Task<RconResponse> SetReplayModeAsync(int modeId, CancellationToken cancellationToken = default) =>
            Run(client, "SetReplayMode", cancellationToken, RconCommand.Number(modeId));

        public Task<RconResponse> ShutdownServerAsync(CancellationToken cancellationToken = default) =>
            Run(client, "ShutdownServer", cancellationToken);

        // ---- Server settings (persisted to Game.ini) ----------------------------

        public Task<RconResponse> UpdateServerNameAsync(string name, CancellationToken cancellationToken = default) =>
            client.ExecuteAsync(RconCommand.CreateWithText("UpdateServerName", name), cancellationToken);

        public Task<RconResponse> SetMaxPlayersAsync(int maxPlayers, CancellationToken cancellationToken = default) =>
            Run(client, "SetMaxPlayers", cancellationToken, RconCommand.Number(maxPlayers));

        /// <summary>Sets a 1-9999 PIN, or removes it when <paramref name="pin"/> is null.</summary>
        public Task<RconResponse> SetPinAsync(int? pin, CancellationToken cancellationToken = default) =>
            Run(client, "SetPin", cancellationToken, pin is { } value ? RconCommand.Number(value) : null);

        /// <summary>0 unlimited, 1 limited generic, 2 limited specific, 3 custom, 4 limited special, 5 boxless.</summary>
        public Task<RconResponse> SetLimitedAmmoTypeAsync(int ammoType, CancellationToken cancellationToken = default) =>
            Run(client, "SetLimitedAmmoType", cancellationToken, RconCommand.Number(ammoType));

        public Task<RconResponse> EnableCompModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Run(client, "EnableCompMode", cancellationToken, RconCommand.Bool(enabled));

        public Task<RconResponse> EnableWhitelistAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Run(client, "EnableWhitelist", cancellationToken, RconCommand.Bool(enabled));

        public Task<RconResponse> EnableVerboseLoggingAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Run(client, "EnableVerboseLogging", cancellationToken, RconCommand.Bool(enabled));

        public Task<RconResponse> ShowNametagsAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Run(client, "ShowNametags", cancellationToken, RconCommand.Bool(enabled));

        /// <summary>Fills the server with bots up to the player limit.</summary>
        public Task<RconResponse> SetBotsEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Run(client, "SetBotsEnabled", cancellationToken, RconCommand.Bool(enabled));

        /// <summary><c>user/repo/branch</c> of a repo containing BalancingTable.csv (not a full URL).</summary>
        public Task<RconResponse> SetBalanceTableUrlAsync(string repository, CancellationToken cancellationToken = default) =>
            Run(client, "SetBalanceTableURL", cancellationToken, repository);

        // ---- mod.io mods -------------------------------------------------------

        public Task<RconResponse> UgcAddModAsync(string ugcId, CancellationToken cancellationToken = default) =>
            Run(client, "UGCAddMod", cancellationToken, NormalizeUgc(ugcId));

        public Task<RconResponse> UgcRemoveModAsync(string ugcId, CancellationToken cancellationToken = default) =>
            Run(client, "UGCRemoveMod", cancellationToken, NormalizeUgc(ugcId));

        public Task<RconResponse> UgcClearModListAsync(CancellationToken cancellationToken = default) =>
            Run(client, "UGCClearModList", cancellationToken);

        // ---- Trouble in Terrorist Town -----------------------------------------

        /// <summary>Resets everyone's karma to 1200.</summary>
        public Task<RconResponse> TttFlushKarmaAsync(CancellationToken cancellationToken = default) =>
            Run(client, "TTTFlushKarma", cancellationToken);

        public Task<RconResponse> TttSetKarmaAsync(string uniqueId, int karma, CancellationToken cancellationToken = default) =>
            Run(client, "TTTSetKarma", cancellationToken, uniqueId, RconCommand.Number(karma));

        public Task<RconResponse> TttGiveCreditsAsync(string uniqueId, int credits, CancellationToken cancellationToken = default) =>
            Run(client, "TTTGiveCredits", cancellationToken, uniqueId, RconCommand.Number(credits));

        /// <summary>Role ids are case-sensitive, see <see cref="PavlovCatalog.TttRoles"/>.</summary>
        public Task<RconResponse> TttSetRoleAsync(string uniqueId, string roleId, CancellationToken cancellationToken = default) =>
            Run(client, "TTTSetRole", cancellationToken, uniqueId, roleId);

        public Task<RconResponse> TttEndRoundAsync(int winningTeamId, CancellationToken cancellationToken = default) =>
            Run(client, "TTTEndRound", cancellationToken, RconCommand.Number(winningTeamId));

        public Task<RconResponse> TttPauseTimerAsync(bool paused, CancellationToken cancellationToken = default) =>
            Run(client, "TTTPauseTimer", cancellationToken, RconCommand.Bool(paused));

        public Task<RconResponse> TttAlwaysEnableSkinMenuAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Run(client, "TTTAlwaysEnableSkinMenu", cancellationToken, RconCommand.Bool(enabled));
    }

    /// <summary>Accepts <c>3267926</c> or <c>UGC3267926</c>.</summary>
    public static string NormalizeUgc(string id)
    {
        var trimmed = id.Trim();
        return trimmed.StartsWith("UGC", StringComparison.OrdinalIgnoreCase) ? "UGC" + trimmed[3..] : "UGC" + trimmed;
    }

    private static Task<RconResponse> Run(PavlovRconClient client, string name, CancellationToken cancellationToken, params ReadOnlySpan<string?> arguments) =>
        client.ExecuteAsync(RconCommand.Create(name, arguments), cancellationToken);

    private static async Task<IReadOnlyList<string>> ReadStrings(PavlovRconClient client, string command, string property, CancellationToken cancellationToken)
    {
        var response = await Run(client, command, cancellationToken).ConfigureAwait(false);
        return response.Read(property, PavlovJsonContext.Default.ListString) ?? [];
    }
}
