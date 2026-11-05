#!/bin/sh
# Runs Trackarr as PUID:PGID instead of root, so files in /config are owned by your user on the host
# (Unraid convention: 99:100 = nobody:users). Existing installs created /config as root, so ownership is
# fixed on start, only for files that don't already belong to PUID.
set -e

PUID="${PUID:-99}"
PGID="${PGID:-100}"

if [ "$(id -u)" = "0" ]; then
    mkdir -p /config
    find /config \( ! -user "$PUID" -o ! -group "$PGID" \) -exec chown "$PUID:$PGID" {} + 2>/dev/null || true

    # The runtime wants a writable HOME; the numeric user may have no passwd entry.
    export HOME=/tmp
    exec gosu "$PUID:$PGID" dotnet /app/Trackarr.dll "$@"
fi

# Already started as a non-root user (e.g. docker run --user): just run.
exec dotnet /app/Trackarr.dll "$@"
