# Shack Control: Pavlov server manager

A mobile-first web app for managing the Pavlov Shack server over RCON. It's built with
Blazor Server (interactive server rendering) on .NET 10 and C# 14, styled with Tailwind CSS v4.
Sign-in uses only the server's RCON password.

| Page | What you can do |
|---|---|
| **Overview** | See the map, mode, round state, live scores, players and ping. Rotate the map, pause or resume, reset SND, clear vehicles. Give cash or items to a whole team. Shut down the server. |
| **Players** | See the live roster with search and team filter. Each player's sheet has: kick, ban, kill, slap, gag, switch team, give or set cash, give items, set skin, teleport, moderator rights, and TTT role, karma and credits. Bans and moderators can be added by ID. |
| **Maps** | See the map rotation and add, remove or play maps now (stock or mod.io `UGC…`). Manage mod.io mods. |
| **Server** | Set the server name, max players, PIN, whitelist, ammo type, time limit, competitive mode, name tags, bots, balance table, verbose logging, and TTT switches. |
| **Console** | Send any command from the server's `Help` list, with autocomplete, history (↑/↓) and highlighted JSON replies. |

Phones get a bottom tab bar and bottom-sheet actions. Wide screens get a sidebar and dialogs.

## Run it

The manager is the `manager` service in the root [compose.yaml](../compose.yaml).

1. Set `RCON_PASSWORD` in the root `.env`, which turns RCON on in the game server.
2. Build and start everything:

   ```bash
   podman compose up -d --build
   ```

3. Open <http://localhost:8080> and sign in with `RCON_PASSWORD`.

To open it from your phone on the same network, set `MANAGER_BIND=0.0.0.0` in `.env`, then run
`podman compose up -d`. It serves plain HTTP. Before exposing it to the internet, put a TLS
reverse proxy (Caddy, Traefik, nginx) in front and set `MANAGER_TRUST_PROXY=true`.

The manager reaches RCON over the compose network at `pavlov:9100`. If only the manager uses
RCON, you can remove the RCON port mapping from the `pavlov` service.

## Security

- **No separate accounts.** Whoever knows the RCON password can manage the server. The password
  is checked by actually signing in to RCON.
- **The password stays in memory only.** It isn't written to disk or put in the cookie. After the
  manager restarts, or after the RCON password changes, every browser signs in again: cookies
  carry a session generation that is checked on every request and every 30 s in open tabs.
- **Cookie:** HttpOnly and `SameSite=Strict`, valid for 12 h (sliding). Forms use antiforgery
  tokens.
- **Guessing is slowed down.** 5 wrong passwords from one IP within 10 minutes lock that IP out
  for 10 minutes.
- **Command injection is blocked.** Arguments with line breaks or stray spaces are rejected, so a
  value typed into a form can't become a second RCON command.
- **Player names are untrusted.** Blazor encodes them, and the console's JSON highlighter
  HTML-encodes every piece before coloring it.
- **Audit log.** Every command is logged (`podman logs pavlov-manager`). `SetPin` values are
  masked.

The warning `Storing keys in a directory ... that may not be persisted` at startup is expected.
Sessions don't survive a restart anyway.

## Develop

```text
management/
├── PavlovManager.slnx
├── Directory.Build.props          net10.0, C# 14, nullable, warnings as errors
├── Dockerfile                     node (Tailwind) → sdk (publish) → aspnet (runtime)
├── src/Pavlov.Rcon/               RCON client library (no ASP.NET dependency)
├── src/Pavlov.Manager/            Blazor app
│   ├── Styles/app.css             Tailwind source and design tokens → wwwroot/app.css (generated)
│   ├── Services/                  RconSession, ServerMonitor, RconActions, LoginThrottle, toasts/dialogs
│   └── Components/                Layout, Pages, UI (Sheet, PlayerSheet, Icon, …)
└── tests/Pavlov.Rcon.Tests/       xUnit v3, including a fake RCON server
```

Run the app locally against the server's published RCON port (`127.0.0.1:9100`):

```bash
dotnet run --project src/Pavlov.Manager
```

It serves <http://localhost:5299>. Each build regenerates the Tailwind CSS with npm; the first build
runs `npm ci`. For live CSS while you edit markup, run this in `src/Pavlov.Manager`:

```bash
npm run watch:css
```

To point it at another server: `--Rcon:Host=… --Rcon:Port=…`, or the `Rcon__Host` / `Rcon__Port`
environment variables.

Run the tests:

```bash
dotnet test
```

## Using the library

```csharp
await using var rcon = new PavlovRconClient(new() { Host = "127.0.0.1", Port = 9100, Password = "…" });

var info = await rcon.GetServerInfoAsync();            // C# 14 extension members
foreach (var player in await rcon.InspectAllAsync())
    Console.WriteLine($"{player.DisplayName} {player.KDA} ${player.Cash}");

await rcon.GiveItemAsync("some_player", "ak47");
var reply = await rcon.ExecuteAsync(RconCommand.Parse("SwitchMap datacenter SND"));
```

The client keeps one long-lived connection, as the wiki recommends. It queues commands and spaces
them 100 ms apart. It reconnects when the server dropped an idle socket. It never reuses a socket
after a timeout, so a late reply can't be taken as the answer to the next command.

## RCON behaviour worth knowing

These were checked against a live Shack server:

- **Handshake.** The server sends `Password: `. The client answers with the lowercase MD5 hex of
  the password, with no line break. The server replies `Authenticated=1` or `Authenticated=0`.
- **Replies** are tab-indented JSON ending in `\r\n`. They can arrive split across reads, so the
  client frames them by matching braces.
- **Unknown commands get no reply at all**, and neither do commands sent during a map change. The
  console checks names against `Help`. `Help` itself isn't in that list.
- `Banlist` answers as `BanList`. Empty `Banlist` and `ModeratorList` replies come with
  `"Successful": false`.
- **Settings can be written but not read.** There's no getter for competitive mode, whitelist,
  bots and the like, so the Server page shows the last value set from the manager.
- **`SetReplayMode`** is in `Help` but not on the wiki. Use it from the Console.

## Credits

- The protocol and command reference come from the Pavlov wiki:
  [Rcon Overview and Commands](https://pavlovwiki.com/index.php/Rcon_Overview_and_Commands),
  [ItemIDs](https://pavlovwiki.com/index.php/ItemIDs) and
  [Setting up a dedicated server](https://pavlovwiki.com/index.php/Setting_up_a_dedicated_server).
- I looked at
  [PavlovVR-Rcon](https://github.com/RainOrigami/PavlovVR-Rcon) (C# client) and
  [PavlovRconWebserver](https://github.com/devinSpitz/PavlovRconWebserver) (web manager) for their
  protocol details and feature set. No code was copied.
