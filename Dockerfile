# Pavlov Shack (Quest) dedicated server image.
# Follows https://pavlovwiki.com/index.php/Setting_up_a_dedicated_server
# The game server itself is downloaded/updated by SteamCMD when the container
# starts (into a volume), so the image stays small and updates need no rebuild.
FROM docker.io/library/ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive
ARG STEAM_UID=1000
ARG STEAM_GID=1000

# Wiki "System setup and software installation" (Ubuntu >= 22.10):
#   gdb curl lib32gcc-s1 libc++-dev unzip
# plus ca-certificates (TLS for SteamCMD) and tini (PID 1, forwards SIGTERM).
# Wiki "Post Installation Steps" says to delete /usr/lib/x86_64-linux-gnu/libc++.so.
# On Ubuntu 24.04 that file is a linker script (not loadable at runtime), but the
# Shack binaries need a file named libc++.so ("libc++.so => not found" once it is
# deleted). So replace it with a symlink to the real library instead.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates curl gdb lib32gcc-s1 libc++-dev unzip tini \
 && rm -rf /var/lib/apt/lists/* \
 && ln -sf libc++.so.1 /usr/lib/x86_64-linux-gnu/libc++.so

# Dedicated "steam" user, as in the wiki. Ubuntu images ship a default
# "ubuntu" user on UID 1000, so remove it first.
RUN (userdel --remove ubuntu 2>/dev/null || true) \
 && groupadd --gid "${STEAM_GID}" steam \
 && useradd --create-home --uid "${STEAM_UID}" --gid "${STEAM_GID}" --shell /bin/bash steam

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod 0755 /usr/local/bin/entrypoint.sh

USER steam
WORKDIR /home/steam

# SteamCMD in ~/Steam (wiki layout), self-updated at build time, plus the
# Steamworks SDK Redist (app 1007) that provides steamclient.so.
RUN mkdir -p Steam pavlovserver steamworks .steam/sdk64 \
 && curl -fsSL "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz" \
      | tar -xz -C Steam \
 && ./Steam/steamcmd.sh +force_install_dir /home/steam/steamworks +login anonymous +app_update 1007 +quit \
 && test -f /home/steam/steamworks/linux64/steamclient.so

ENV STEAM_BRANCH=shack \
    GAME_PORT=7777 \
    RCON_PORT=9100 \
    UPDATE_ON_START=true \
    VALIDATE=false

VOLUME ["/home/steam/pavlovserver"]

# Game port and the port 400 above it: UDP for play, TCP for Shack map downloads.
# 9100/tcp is RCON.
EXPOSE 7777/udp 7777/tcp 8177/udp 8177/tcp 9100/tcp

STOPSIGNAL SIGTERM
ENTRYPOINT ["/usr/bin/tini", "-g", "--", "/usr/local/bin/entrypoint.sh"]
