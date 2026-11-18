# Pavlov Shack dedicated server on Podman

A Pavlov Shack (Quest) dedicated server in a container, built from the
[Pavlov wiki setup guide](https://pavlovwiki.com/index.php/Setting_up_a_dedicated_server).

- `Dockerfile`: Ubuntu 24.04 with the wiki's packages (`gdb curl lib32gcc-s1 libc++-dev unzip`).
  It also installs SteamCMD, the Steamworks SDK Redist (app 1007) and a `steam` user.
- `entrypoint.sh`: when the container starts, this script installs or updates the server
  (app 622970, branch `shack`) and runs the wiki's post-install steps (`steamclient.so`,
  `chmod`, `Saved/maps`). It then applies your config and starts `PavlovServer.sh`.
- `compose.yaml`: ports, volumes and settings.
- `config/`: your `Game.ini`, plus optional `mods.txt`, `blacklist.txt` and `whitelist.txt`.

## Quick start

```bash
cp .env.example .env
```

1. Get a server key. In game, go to **Settings > General > Server Key Generator** and get a
   6-digit PIN. Exchange the PIN for a key at <https://api-key.vankrupt.net/>, then put the
   key in `.env` as `SERVER_KEY=`. Without a key the server is not listed.
2. Set `RCON_PASSWORD` in `.env` if you want RCON.
3. Edit `config/Game.ini`. Set the server name, max players and map rotation.
4. Start the server:

```bash
podman compose up -d --build
```

```bash
podman compose logs -f
```

The first start downloads the server (a few GB). Later starts only download updates.

## Day to day

| Task | Command |
|---|---|
| Apply a changed `Game.ini` or `.env` | `podman compose up -d --force-recreate` |
| Restart (also pulls server updates) | `podman compose restart` |
| Stop | `podman compose down` |
| Shell in the container | `podman exec -it pavlov-shack bash` |
| Read the game log | `podman exec pavlov-shack tail -f pavlovserver/Pavlov/Saved/Logs/Pavlov.log` |
| Rebuild the image (new SteamCMD/base) | `podman compose build --pull` |
| Delete the server install | `podman compose down -v` |

Files in `config/` are copied into the server on every start, so `config/` is the source of
truth. Bans written to `blacklist.txt` from inside the game are overwritten on the next start
if you also keep a `config/blacklist.txt`.

Other branches: set `STEAM_BRANCH=shack_beta` for the Shack release candidate. The same image
also runs the PC server (`default` or `beta_server`).

Extra launch arguments go in `command:` in `compose.yaml`, for example `["-COMPETITIVE=1"]`.

## Ports

Open or forward these ports on your firewall or router:

| Port | Protocol | Purpose |
|---|---|---|
| 7777 (`GAME_PORT`) | UDP + TCP | Game. TCP lets Shack clients download mod.io maps |
| 8177 (`GAME_PORT` + 400) | UDP + TCP | Server status helper |
| 9100 (`RCON_PORT`) | TCP | RCON. Bound to 127.0.0.1 unless `RCON_BIND=0.0.0.0` |

To change the game port, set `GAME_PORT` and set `QUERY_PORT` to `GAME_PORT + 400`. The
container refuses to start if they don't match. For several servers on one host, copy the
folder and give each copy its own `GAME_PORT`, `QUERY_PORT` and `RCON_PORT`. Use a different
`name:` in each `compose.yaml` too.

## Notes

- **Resources.** The wiki suggests about 2 GB RAM and a 2.5 GHz core for 10 players. For 24
  players it suggests 4 GB and 4 GHz. On macOS the Podman VM needs at least that much memory:
  `podman machine stop && podman machine set --memory 4096 && podman machine start`.
- **Architecture.** The server is x86_64 only. On Apple Silicon the image runs under
  emulation, which is slow and not guaranteed to work.
- **macOS networking.** The server runs inside the Podman VM. Podman forwards the published
  ports from your Mac, so forward your router to the Mac's IP.
- **Start on boot (Linux).** Podman restart policies need `podman-restart.service`:
  `systemctl --user enable --now podman-restart.service` for rootless, or
  `sudo systemctl enable --now podman-restart.service` for rootful.
- **libc++.** The wiki says to delete `/usr/lib/x86_64-linux-gnu/libc++.so`. On Ubuntu 24.04
  that file is a linker script, and the Shack server fails to start without a file of that
  name. The image replaces it with a symlink to `libc++.so.1` instead.
- **Harmless log noise.** Expect `LoadSubsystemModule([STEAM]) failed` (Shack uses Oculus, not
  Steam), OpenXR-Loader errors and `xdg-user-dir: not found`.
- **Server key line.** The wiki's key step uses `ServerKey=`, but its option list calls it
  `ApiKey`. With `SERVER_KEY` set, the entrypoint writes both lines into `Game.ini`.
- `compose.yaml` works with `podman compose` (which uses `docker-compose` or `podman-compose`).
