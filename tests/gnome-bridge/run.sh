#!/usr/bin/env bash
# Her GNOME window helper against a real GNOME Shell: a headless one on a private session bus, with its own settings,
# extensions and cache folders, so the desktop you're logged into is never touched. Needs gnome-shell, gsettings,
# gdbus, dbus-run-session and a GTK app (gnome-calculator by default). Usage: bash tests/gnome-bridge/run.sh [app] [word]
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
app=${1:-gnome-calculator}
word=${2:-calculator}
work=$(mktemp -d "${TMPDIR:-/tmp}/capri-gnome-XXXXXX")
trap 'rm -rf "$work"' EXIT
dotnet build "$root/tests/gnome-bridge/GnomeBridgeChecks.csproj" -c Release -p:UseAppHost=false --nologo -v quiet -o "$work/bin" >/dev/null
export CAPRI_DLL="$work/bin/NativeBridgeChecks.dll" CAPRI_APP="$app" CAPRI_WORD="$word" CAPRI_SOCKET="capri-gnome-test-$$"
export XDG_DATA_HOME="$work/data" XDG_CONFIG_HOME="$work/config" XDG_CACHE_HOME="$work/cache" XDG_STATE_HOME="$work/state"
export XDG_CURRENT_DESKTOP=GNOME XDG_SESSION_TYPE=wayland
unset DISPLAY WAYLAND_DISPLAY
dotnet "$CAPRI_DLL" offline
dbus-run-session -- bash -c '
    set -euo pipefail
    # What Capri does on her first start: write the helper and turn it on. GNOME loads it when it starts, as at log-in.
    dotnet "$CAPRI_DLL" install
    mkdir -p "$XDG_CACHE_HOME"
    gnome-shell --headless --wayland --no-x11 --virtual-monitor 1280x800 --wayland-display "$CAPRI_SOCKET" > "$XDG_CACHE_HOME/shell.log" 2>&1 &
    shell=$!
    trap "kill \$shell 2>/dev/null || true" EXIT
    for i in $(seq 1 150); do
        if gdbus introspect --session --dest org.gnome.Shell --object-path /app/capri/Gnome 2>/dev/null | grep -q app.capri.Gnome; then break; fi
        sleep 0.2
    done
    gdbus introspect --session --dest org.gnome.Shell --object-path /app/capri/Gnome | grep -q app.capri.Gnome || { echo "FAIL GNOME Shell never loaded the helper"; tail -30 "$XDG_CACHE_HOME/shell.log"; exit 1; }
    echo "PASS GNOME Shell loaded the helper from the user extensions folder"
    WAYLAND_DISPLAY="$CAPRI_SOCKET" GDK_BACKEND=wayland "$CAPRI_APP" > /dev/null 2>&1 &
    WAYLAND_DISPLAY="$CAPRI_SOCKET" dotnet "$CAPRI_DLL" live "$CAPRI_WORD" || { tail -30 "$XDG_CACHE_HOME/shell.log"; exit 1; }
'
