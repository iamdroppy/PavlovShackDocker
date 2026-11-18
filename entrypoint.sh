#!/usr/bin/env bash
# Installs/updates the Pavlov dedicated server with SteamCMD, applies the config
# mounted at /config, then starts the server. Steps follow
# https://pavlovwiki.com/index.php/Setting_up_a_dedicated_server
# Any arguments given to the container are passed on to PavlovServer.sh.
set -euo pipefail

STEAM_BRANCH="${STEAM_BRANCH:-shack}"
GAME_PORT="${GAME_PORT:-7777}"
QUERY_PORT="${QUERY_PORT:-$((GAME_PORT + 400))}"
RCON_PORT="${RCON_PORT:-9100}"
RCON_PASSWORD="${RCON_PASSWORD:-}"
SERVER_KEY="${SERVER_KEY:-}"
UPDATE_ON_START="${UPDATE_ON_START:-true}"
VALIDATE="${VALIDATE:-false}"

STEAMCMD="$HOME/Steam/steamcmd.sh"
SDK_DIR="$HOME/steamworks"
SERVER_DIR="$HOME/pavlovserver"
SAVED_DIR="$SERVER_DIR/Pavlov/Saved"
CONFIG_DIR="$SAVED_DIR/Config"
SRC_CONFIG="/config"

log() { printf '[entrypoint] %s\n' "$*"; }
die() { printf '[entrypoint] ERROR: %s\n' "$*" >&2; exit 1; }

# SteamCMD sometimes fails transiently (network, self-update); retry a few times.
run_steamcmd() {
  local attempt
  for attempt in 1 2 3; do
    if "$STEAMCMD" "$@"; then
      return 0
    fi
    log "steamcmd failed (attempt $attempt/3), retrying in 10s"
    sleep 10
  done
  return 1
}

# The server always uses the port 400 above the game port; compose needs both
# published, so catch a mismatch early instead of an unreachable server.
(( QUERY_PORT == GAME_PORT + 400 )) \
  || die "QUERY_PORT ($QUERY_PORT) must be GAME_PORT + 400 ($((GAME_PORT + 400)))"

# --- Install / update -------------------------------------------------------
if [[ "$UPDATE_ON_START" == "true" || ! -f "$SERVER_DIR/PavlovServer.sh" ]]; then
  log "Updating Steamworks SDK Redist (app 1007)"
  run_steamcmd +force_install_dir "$SDK_DIR" +login anonymous +app_update 1007 +quit \
    || die "SteamCMD could not update app 1007"

  validate=()
  [[ "$VALIDATE" == "true" ]] && validate=(validate)
  log "Updating Pavlov server (app 622970, branch '$STEAM_BRANCH')"
  run_steamcmd +force_install_dir "$SERVER_DIR" +login anonymous \
    +app_update 622970 -beta "$STEAM_BRANCH" "${validate[@]}" +quit \
    || die "SteamCMD could not install/update app 622970 (branch '$STEAM_BRANCH')"
else
  log "UPDATE_ON_START=false, skipping SteamCMD update"
fi

[[ -f "$SERVER_DIR/PavlovServer.sh" ]] || die "PavlovServer.sh not found in $SERVER_DIR"

# --- Post-install steps from the wiki --------------------------------------
SDK_SO="$SDK_DIR/linux64/steamclient.so"
[[ -f "$SDK_SO" ]] || die "steamclient.so not found at $SDK_SO"
install -D -m 0755 "$SDK_SO" "$HOME/.steam/sdk64/steamclient.so"
install -D -m 0755 "$SDK_SO" "$SERVER_DIR/Pavlov/Binaries/Linux/steamclient.so"
chmod +x "$SERVER_DIR/PavlovServer.sh"
# Saved/maps is where Shack downloads mod.io maps.
mkdir -p "$SAVED_DIR/Logs" "$SAVED_DIR/maps" "$CONFIG_DIR/LinuxServer"

# --- Config -----------------------------------------------------------------
# /config is the source of truth: its files overwrite the copies in the volume
# on every start.
GAME_INI="$CONFIG_DIR/LinuxServer/Game.ini"
if [[ -f "$SRC_CONFIG/Game.ini" ]]; then
  if [[ -n "$SERVER_KEY" ]]; then
    # Put the key from SERVER_KEY right under the section header, replacing any
    # key lines in the file. The wiki's setup step names the line "ServerKey",
    # its key reference calls it "ApiKey"; write both, unknown keys are ignored.
    awk '
      BEGIN { key = ENVIRON["SERVER_KEY"]; found = 0 }
      /^[[:space:]]*(ServerKey|ApiKey)[[:space:]]*=/ { next }
      { print }
      /^\[\/Script\/Pavlov\.DedicatedServer\]/ {
        print "ServerKey=" key; print "ApiKey=" key; found = 1
      }
      END {
        if (!found) {
          print "[/Script/Pavlov.DedicatedServer]"
          print "ServerKey=" key; print "ApiKey=" key
        }
      }
    ' "$SRC_CONFIG/Game.ini" > "$GAME_INI"
    log "Applied Game.ini with server key from SERVER_KEY"
  else
    cp "$SRC_CONFIG/Game.ini" "$GAME_INI"
    log "Applied Game.ini (SERVER_KEY not set: the server will not be listed unless Game.ini has a key)"
  fi
else
  log "No Game.ini in $SRC_CONFIG, the server uses its own defaults"
fi

RCON_FILE="$CONFIG_DIR/RconSettings.txt"
if [[ -n "$RCON_PASSWORD" ]]; then
  printf 'Password=%s\nPort=%s\n' "$RCON_PASSWORD" "$RCON_PORT" > "$RCON_FILE"
  log "RCON enabled on TCP port $RCON_PORT"
elif [[ -f "$SRC_CONFIG/RconSettings.txt" ]]; then
  cp "$SRC_CONFIG/RconSettings.txt" "$RCON_FILE"
  log "Applied RconSettings.txt"
else
  rm -f "$RCON_FILE"
  log "RCON_PASSWORD not set, RCON disabled"
fi

for list in mods.txt blacklist.txt whitelist.txt; do
  if [[ -f "$SRC_CONFIG/$list" ]]; then
    cp "$SRC_CONFIG/$list" "$CONFIG_DIR/$list"
    log "Applied $list"
  fi
done

# --- Start ------------------------------------------------------------------
cd "$SERVER_DIR"
log "Starting PavlovServer (branch '$STEAM_BRANCH') on port $GAME_PORT"
exec ./PavlovServer.sh -PORT="$GAME_PORT" "$@"
