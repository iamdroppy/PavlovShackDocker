# Shack Control: Pavlov server manager

A mobile-first web app for managing the Pavlov Shack server over RCON. It's built with
Blazor Server (interactive server rendering) on .NET 10 and C# 14, styled with Tailwind CSS v4.
Sign-in uses only the server's RCON password.

| Page | What you can do |
|---|---|
| **Overview** | See the map, mode, round state, live scores, players and ping. Rotate the map, pause or resume, reset SND, clear vehicles. Give cash or items to a whole team. Shut down the server. |
| **Players** | See the live roster with search and team filter. Each player's sheet has: kick, ban, kill, slap, gag, switch team, give or set cash, give items, set skin, teleport, moderator rights, and TTT role, karma and credits. Bans and moderators can be added by ID. |
| **Maps** | See the map rotation and add, remove or play maps now (stock or mod.io `UGC…`). Manage mod.io mods. Create, pause, edit and delete **end-of-game rules**, with live progress and recent results. |
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

3. Open <http://localhost:8118> and sign in with `RCON_PASSWORD`.

To open it from your phone on the same network, set `MANAGER_BIND=0.0.0.0` in `.env`, then run
`podman compose up -d`. It serves plain HTTP. Before exposing it to the internet, put a TLS
reverse proxy (Caddy, Traefik, nginx) in front and set `MANAGER_TRUST_PROXY=true`.

The manager reaches RCON over the compose network at `pavlov:9100`. If only the manager uses
RCON, you can remove the RCON port mapping from the `pavlov` service.

## End-of-game rules (Hangfire)

A rule ends the match, by rotating to the next map, as soon as **any** of its limits is reached:

| Limit | Example | Who wins |
|---|---|---|
| Time limit | 20 min | The side that's ahead when time runs out (or a draw) |
| Lead | 10% ahead, once the leader has 10 pts | The leader. 33 vs 30 is exactly 10%. The minimum score stops 1–0 ending a match. |
| Score limit | 50 pts | The first side to reach it |

- **Which matches.** A rule can cover every map, one map, one game mode, or one map in one mode.
  Rotation rows on the Maps page show which rule applies to them.
- **Team modes** compare Blue and Red. Free-for-all modes compare the two best players.
- **Hangfire jobs.** Each enabled rule is a Hangfire recurring job (`end-of-game-<id>`, cron
  `*/5 * * * * *`, so every 5 s). Creating or resuming a rule adds its job; pausing or deleting
  removes it.
- **Only one rule ends a match.** After it rotates the map, all rules wait 45 s for the next map to
  load.
- **How time is counted.** RCON has no match clock, so the manager counts from when it saw the map,
  the mode or the scores change. After a manager restart mid-match, the clock starts at the restart.
  The panel then says "since the manager joined".
- **Storage.** Rules and the last 30 results live in `end-of-game.json` on the `manager-data`
  volume (`/data`). Hangfire itself uses in-memory storage: its jobs are rebuilt from the rules on
  startup, and job history is kept for 30 minutes.
- **Running unattended.** Background jobs need RCON even when nobody is signed in. Compose passes
  `RCON_PASSWORD` to the manager as `Rcon__Password` for that. Remove the variable to keep the
  password in browsers only. Rules then pause after a manager restart until someone signs in, and
  the panel warns about it.

### Job dashboard

The Hangfire dashboard is at <http://localhost:8118/hangfire> (the **Jobs** button on the Maps
page). It uses the **same sign-in** as the app: opening it signed out sends you to the login page,
where you enter the RCON password, and then back to the dashboard. Its API endpoints are locked the
same way. Each check shows its result there, for example
`datacenter TDM 12–9 at 06:41: playing on` or
`Match ended: Blue wins 10–5. Led by 100%, limit 10%. Rotated to the next map.`

## Security

- **No separate accounts.** Whoever knows the RCON password can manage the server. The password
  is checked by actually signing in to RCON.
- **The password isn't stored by the app.** It isn't written to disk or put in the cookie. It
  only comes from your sign-in, or from `Rcon__Password` when you run rules unattended. After the
  manager restarts, or after the RCON password changes, every browser signs in again: cookies
  carry a session generation that is checked on every request and every 30 s in open tabs.
- **The Hangfire dashboard** (`/hangfire`) needs the same sign-in as the app.
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
│   │   └── EndOfGame/             rules, evaluator, match tracker, Hangfire job and scheduler
│   └── Components/                Layout, Pages, UI (Sheet, PlayerSheet, Icon, …)
└── tests/Pavlov.Rcon.Tests/       xUnit v3: protocol (fake RCON server) and end-of-game logic
```

Run the app locally against the server's published RCON port (`127.0.0.1:9100`):

```bash
dotnet run --project src/Pavlov.Manager
```

It serves <http://localhost:5299>. Rules are saved under `src/Pavlov.Manager/data/` (git-ignored).
Add `--Rcon:Password=…` to let rules run without signing in. Each build regenerates the Tailwind CSS with npm; the first build
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
