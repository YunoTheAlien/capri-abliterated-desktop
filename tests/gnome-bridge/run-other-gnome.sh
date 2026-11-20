#!/usr/bin/env bash
# Her GNOME window helper on another distro's GNOME, in a throwaway Docker container: the helper she'd write for that
# GNOME is put in a test user's extensions folder, a headless GNOME Shell loads it, and gdbus drives it against
# gnome-calculator. Needs Docker and network for apt.
#   bash tests/gnome-bridge/run-other-gnome.sh ubuntu:22.04 42     (GNOME 42, the pre-45 style)
#   bash tests/gnome-bridge/run-other-gnome.sh ubuntu:24.04 46     (GNOME 46, the module style)
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
image=${1:?image}; shell=${2:?gnome major}
work=$(mktemp -d "${TMPDIR:-/tmp}/capri-gnome-$shell-XXXXXX")
trap 'rm -rf "$work"' EXIT
dotnet run --project "$root/tests/gnome-bridge/GnomeBridgeChecks.csproj" -c Release -p:UseAppHost=false -- files "$work/capri-windows@capriai.app" "$shell" | tail -1
cat > "$work/inside.sh" <<'INSIDE'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq >/dev/null
apt-get install -y -qq --no-install-recommends gnome-shell gnome-calculator dbus dbus-user-session libglib2.0-bin dconf-service python3 python3-dbusmock libgl1-mesa-dri >/dev/null 2>&1
# GNOME Shell expects a system bus, as on any real install.
mkdir -p /run/dbus && dbus-daemon --system --fork
# and logind, which a container has no systemd for: python-dbusmock's stand-in, the way GNOME's own tests do it.
python3 -m dbusmock --system --template logind >/tmp/logind.log 2>&1 &
for i in $(seq 1 50); do gdbus introspect --system --dest org.freedesktop.login1 --object-path /org/freedesktop/login1 >/dev/null 2>&1 && break; sleep 0.2; done
echo "INFO $(gnome-shell --version)"
useradd -m tester
install -d -o tester -m 700 /tmp/run-tester
install -d -o tester /home/tester/.local/share/gnome-shell/extensions
cp -r /helper/capri-windows@capriai.app /home/tester/.local/share/gnome-shell/extensions/
chown -R tester /home/tester/.local
cat > /tmp/check.py <<'PY'
import ast, json, subprocess, sys, time
def call(method, *args):
    out = subprocess.run(["gdbus", "call", "--session", "--dest", "org.gnome.Shell", "--object-path", "/app/capri/Gnome", "--method", "app.capri.Gnome." + method, *args], capture_output=True, text=True)
    if out.returncode: raise RuntimeError(out.stderr.strip())
    text = out.stdout.strip()
    if text in ("(true,)", "(false,)"): return text == "(true,)"
    return ast.literal_eval(text)[0]
def rows():
    return json.loads(call("Subscribe"))[1]
def wait(ok, what):
    for _ in range(100):
        try:
            found = [r for r in rows() if ok(r)]
            if found: return found[0]
        except RuntimeError: pass
        time.sleep(0.2)
    print("FAIL " + what + ": " + str(rows())); sys.exit(1)
print("PASS hello " + call("Hello"))
app = wait(lambda r: "alculator" in (r[7] + r[8]) and r[4] > 0 and r[6] & 4, "the calculator is listed with a size")
print("PASS listed: id %s pid %s at %s,%s %sx%s flags %s title %r class %r" % tuple(app[:2] + app[2:6] + [app[6], app[7], app[8]]))
assert call("Act", app[0], "minimize") is True
wait(lambda r: r[0] == app[0] and r[6] & 1, "minimized"); print("PASS minimize")
assert call("Act", app[0], "switch_to") is True
wait(lambda r: r[0] == app[0] and not r[6] & 1 and r[6] & 8, "restored and focused"); print("PASS switch_to restores and focuses")
assert call("Act", "999999999", "close") is False; print("PASS an unknown id does nothing")
assert call("Act", app[0], "close") is True
for _ in range(50):
    if not any(r[0] == app[0] for r in rows()): break
    time.sleep(0.2)
else:
    print("FAIL close"); sys.exit(1)
print("PASS close")
PY
cat > /tmp/session.sh <<'SESSION'
set -u
gsettings set org.gnome.shell enabled-extensions "['capri-windows@capriai.app']"
gnome-shell --headless --wayland --no-x11 --virtual-monitor 1280x800 --wayland-display capri-t > /tmp/run-tester/shell.log 2>&1 &
for i in $(seq 1 150); do gdbus call --session --dest org.gnome.Shell --object-path /app/capri/Gnome --method app.capri.Gnome.Hello >/dev/null 2>&1 && break; sleep 0.2; done
WAYLAND_DISPLAY=capri-t GDK_BACKEND=wayland gnome-calculator >/dev/null 2>&1 &
python3 /tmp/check.py || { grep -iE "capri|JS ERROR|extension" /tmp/run-tester/shell.log | head -20; exit 1; }
SESSION
chmod 644 /tmp/session.sh
su tester -c 'export HOME=/home/tester XDG_RUNTIME_DIR=/tmp/run-tester XDG_CURRENT_DESKTOP=GNOME; cd ~; dbus-run-session -- bash /tmp/session.sh'
INSIDE
docker run --rm -v "$work:/helper:ro" "$image" bash /helper/inside.sh
