#!/bin/sh
# Capri's Linux installer: the Linux way of doing what Capri-Setup.exe does on Windows. It installs her for this user
# only, with no root and no password, and gives her a launcher in the app menu. Uninstalling removes her program and
# launchers and keeps her settings, notes and chats. make-installer.sh puts a build of her after the last line of this
# script; copied into her folder as `uninstall`, the same script with nothing after it only uninstalls.
#
#   sh Capri-<version>-linux-x64.run              install, asking about a desktop shortcut and starting her
#   sh Capri-<version>-linux-x64.run --silent     install with no questions (add --desktop for the shortcut)
#   sh Capri-<version>-linux-x64.run --uninstall  remove her (--silent works here too)
#   sh Capri-<version>-linux-x64.run --verify     only check the file isn't damaged
#   sh Capri-<version>-linux-x64.run --extract D  unpack her into folder D and install nothing
#
# Exit codes, as Windows' silent setup: 0 done, 1 failed (reason on stderr), 2 a command it can't read.
set -u

VERSION='@VERSION@'
PAYLOAD_SHA256='@PAYLOAD_SHA256@'
MARKER_LINE='__CAPRI_PAYLOAD_BELOW__'

home=${HOME:?HOME is not set}
data_home=${XDG_DATA_HOME:-$home/.local/share}
dest=$home/.local/opt/capri
apps=$data_home/applications
launcher=$apps/capri.desktop
icon=$data_home/icons/hicolor/256x256/apps/capri.png
bin=$home/.local/bin/capri
self=$0

silent=; desktop=; mode=install; extract_to=
case $(basename -- "$self") in uninstall|capri-uninstall) mode=uninstall ;; esac
while [ $# -gt 0 ]; do
    case $1 in
        --silent) silent=1 ;;
        --desktop) desktop=yes ;;
        --no-desktop) desktop=no ;;
        --uninstall) mode=uninstall ;;
        --verify) mode=verify ;;
        --extract) [ $# -ge 2 ] || { echo "--extract needs a folder." >&2; exit 2; }; mode=extract; extract_to=$2; shift ;;
        -h|--help) sed -n '2,15p' "$self" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Capri's installer doesn't know \"$1\". Try --help." >&2; exit 2 ;;
    esac
    shift
done

# Talking to them: the terminal when there is one, a zenity window when she was opened from a file manager, and
# nothing at all when silent (answers are then the defaults: no shortcut, don't start her).
gui=
if [ -z "$silent" ] && [ ! -t 0 ] && command -v zenity >/dev/null 2>&1 && { [ -n "${DISPLAY:-}" ] || [ -n "${WAYLAND_DISPLAY:-}" ]; }; then gui=1; fi
say() { if [ -n "$gui" ]; then zenity --info --title="Capri" --no-wrap --text="$1" 2>/dev/null; else printf '%s\n' "$1"; fi; }
fail() {
    if [ -n "$gui" ]; then zenity --error --title="Capri" --no-wrap --text="$1" 2>/dev/null; fi
    printf '%s\n' "$1" >&2
    exit 1
}
ask() {   # ask "question" default(yes|no)
    if [ -n "$silent" ]; then [ "$2" = yes ]; return; fi
    if [ -n "$gui" ]; then zenity --question --title="Capri" --no-wrap --text="$1" 2>/dev/null; return; fi
    if [ ! -t 0 ]; then [ "$2" = yes ]; return; fi
    if [ "$2" = yes ]; then printf '%s [Y/n] ' "$1"; else printf '%s [y/N] ' "$1"; fi
    read -r answer || answer=
    case $answer in [Yy]*) return 0 ;; [Nn]*) return 1 ;; *) [ "$2" = yes ] ;; esac
}

# Anything running from her folder: Capri herself, her input helper, or a bridge script started from there.
running() {
    for proc in /proc/[0-9]*; do
        exe=$(readlink "$proc/exe" 2>/dev/null) || exe=
        case $exe in "$dest"/*) return 0 ;; esac
        cmd=$(tr '\0' '\n' 2>/dev/null < "$proc/cmdline" | head -n 3) || cmd=
        # Not this script: run from her folder as `uninstall`, it and every subshell it forks carry that path.
        case $cmd in *"$dest/uninstall"*) continue ;; esac
        case $cmd in "$dest"/*|*"
$dest"/*) return 0 ;; esac
    done
    return 1
}
# Like Windows, she's given a few seconds to finish closing; one still open is never killed, because her unsaved chat
# and notes are in it.
wait_closed() {
    tries=0
    while running; do
        tries=$((tries + 1))
        [ $tries -gt 6 ] && fail "Capri is still open. Quit her (right-click her > Quit), then run this again."
        sleep 0.5
    done
}

ours() { [ -f "$1" ] && grep -q '^X-Capri-Install=1$' "$1" 2>/dev/null; }

payload_line() { awk -v m="$MARKER_LINE" '$0 == m { print NR + 1; exit }' "$self"; }

check_payload() {
    case $PAYLOAD_SHA256 in @*) fail "This copy of the installer has no Capri inside it. Download the .run file again." ;; esac
    start=$(payload_line)
    [ -n "$start" ] || fail "This installer is damaged (no Capri inside). Download it again."
    got=$(tail -n +"$start" "$self" | sha256sum | cut -d' ' -f1)
    [ "$got" = "$PAYLOAD_SHA256" ] || fail "This installer is damaged or incomplete (its checksum doesn't match). Download it again."
}

unpack() {   # unpack <folder>
    mkdir -p "$1" || fail "Couldn't make $1."
    tail -n +"$(payload_line)" "$self" | tar -xzf - -C "$1" || fail "Couldn't unpack Capri into $1. Is the disk full?"
}

uninstall() {
    if [ ! -d "$dest" ]; then say "Capri isn't installed for this user, so there's nothing to remove."; exit 0; fi
    [ -f "$dest/.capri-install" ] || fail "$dest wasn't made by this installer, so it was left alone."
    ask "Remove Capri? Her settings, notes and chats in ${XDG_CONFIG_HOME:-$home/.config}/Capri are kept." yes || exit 0
    wait_closed
    if ours "$launcher"; then rm -f "$launcher"; rm -f "$icon"; fi
    desk=$(desktop_dir)
    if [ -n "$desk" ] && ours "$desk/capri.desktop"; then rm -f "$desk/capri.desktop"; fi
    if [ -L "$bin" ]; then case $(readlink "$bin") in "$dest"/*) rm -f "$bin" ;; esac; fi
    rm -rf "$dest" || fail "Couldn't remove everything in $dest."
    refresh_menus
    say "Capri has been removed.

Her settings, notes and chats were kept in ${XDG_CONFIG_HOME:-$home/.config}/Capri. Delete that folder too to remove everything."
    exit 0
}

desktop_dir() {
    d=
    if command -v xdg-user-dir >/dev/null 2>&1; then d=$(xdg-user-dir DESKTOP 2>/dev/null); fi
    [ -n "$d" ] && [ "$d" != "$home" ] && [ -d "$d" ] && printf '%s' "$d"
}

refresh_menus() {
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" >/dev/null 2>&1
    command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$data_home/icons/hicolor" >/dev/null 2>&1
    return 0
}

write_launcher() {   # write_launcher <file>
    cat > "$1" <<EOF
[Desktop Entry]
Type=Application
Name=Capri
Comment=Desktop companion
Exec="$dest/Capri"
Path=$dest
Icon=capri
Terminal=false
Categories=Utility;
StartupWMClass=Capri
Actions=uninstall;
X-Capri-Install=1
X-Capri-Version=$VERSION

[Desktop Action uninstall]
Name=Uninstall Capri
Exec="$dest/uninstall"
EOF
}

# The system's library list. ldconfig lives in /sbin on most distros, which an ordinary user's PATH can leave out; with
# no list at all, nothing is reported rather than everything.
system_libraries() {
    for tool in ldconfig /sbin/ldconfig /usr/sbin/ldconfig; do
        list=$("$tool" -p 2>/dev/null) && [ -n "$list" ] && { printf '%s\n' "$list"; return 0; }
    done
    return 0
}

# What she needs from the system and a self-contained build doesn't carry. Missing ones are told, not fatal: she may
# still start, and the list says what to install if she doesn't.
missing_libraries() {
    {
        # libcoreclrtraceptprovider.so is .NET's optional tracing hook; .NET carries on without its library.
        for f in "$dest/Capri" "$dest"/*.so "$dest/native/capri-input"; do
            case $f in */libcoreclrtraceptprovider.so) continue ;; esac
            [ -f "$f" ] && ldd "$f" 2>/dev/null
        done | awk '/not found/ { print $1 }'
        known=$(system_libraries)
        if [ -n "$known" ]; then
            for lib in libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1; do
                printf '%s\n' "$known" | grep -q "$lib" || echo "$lib"
            done
            grep -q '"System.Globalization.Invariant": true' "$dest/Capri.runtimeconfig.json" 2>/dev/null \
                || printf '%s\n' "$known" | grep -q 'libicuuc\.so' || echo "libicu (ICU)"
        fi
        [ -x /usr/bin/python3 ] || echo "python3 (/usr/bin/python3, for her bridges)"
    } | sort -u
}

install() {
    [ "$(uname -m)" = x86_64 ] || fail "This Capri is built for 64-bit x86 (x86_64). This computer is $(uname -m)."
    check_payload
    if [ -e "$dest" ] && [ ! -f "$dest/.capri-install" ] && [ -n "$(ls -A "$dest" 2>/dev/null)" ]; then
        fail "There's already a folder at $dest that isn't this Capri. Move it out of the way, then install again."
    fi
    if [ -f "$dest/.capri-install" ]; then
        had=$(sed -n 's/^Capri desktop companion //p' "$dest/.capri-install")
        ask "Capri ${had:-} is installed. Replace her with Capri $VERSION? Her settings, notes and chats are kept." yes || exit 0
    else
        ask "Install Capri $VERSION for $(id -un)? She goes in $dest, with a launcher in your app menu." yes || exit 0
    fi
    if [ -z "$desktop" ]; then if ask "Put a Capri shortcut on the desktop too?" no; then desktop=yes; else desktop=no; fi; fi
    wait_closed

    # Unpacked next to her folder and swapped in at the end, so a failed unpack leaves the old one working.
    mkdir -p "$(dirname -- "$dest")" || fail "Couldn't make $(dirname -- "$dest")."
    new=$dest.new.$$; old=$dest.old.$$
    rm -rf "$new"
    unpack "$new"
    [ -x "$new/Capri" ] || { rm -rf "$new"; fail "The unpacked Capri is incomplete. Download the installer again."; }
    head -n "$(( $(payload_line) - 2 ))" "$self" > "$new/uninstall" && chmod 755 "$new/uninstall" \
        || { rm -rf "$new"; fail "Couldn't write her uninstaller."; }
    printf 'Capri desktop companion %s\n' "$VERSION" > "$new/.capri-install"
    if [ -e "$dest" ]; then
        mv "$dest" "$old" || { rm -rf "$new"; fail "Couldn't move the old Capri aside. Is something still using her folder?"; }
    fi
    if ! mv "$new" "$dest"; then
        [ -e "$old" ] && mv "$old" "$dest"
        rm -rf "$new"
        fail "Couldn't put the new Capri in place; the old one was kept."
    fi
    rm -rf "$old"

    mkdir -p "$apps" "$(dirname -- "$icon")"
    [ -f "$dest/capri.png" ] && cp "$dest/capri.png" "$icon"
    write_launcher "$launcher" || fail "Capri is installed, but her app-menu launcher couldn't be saved in $apps."
    shortcut_note=
    if [ "$desktop" = yes ]; then
        desk=$(desktop_dir)
        if [ -n "$desk" ]; then
            write_launcher "$desk/capri.desktop" && chmod 755 "$desk/capri.desktop"
            # GNOME asks before running a desktop file it hasn't been told to trust.
            command -v gio >/dev/null 2>&1 && gio set "$desk/capri.desktop" metadata::trusted true >/dev/null 2>&1
        else
            shortcut_note="
There's no desktop folder for this user, so no desktop shortcut was made."
        fi
    fi
    # A `capri` command, unless one already exists that isn't this one (a distro package, say): that one is left alone.
    if [ -L "$bin" ] || ! command -v capri >/dev/null 2>&1; then
        mkdir -p "$(dirname -- "$bin")" && ln -sfn "$dest/Capri" "$bin"
    fi
    refresh_menus

    lacking=$(missing_libraries)
    note="Capri $VERSION is installed. Open her from your app menu.$shortcut_note"
    if [ -n "$lacking" ]; then
        note="$note

She may not start until these are installed with your system's package manager:
$(printf '%s\n' "$lacking" | sed 's/^/  /')"
    fi
    if [ -n "$silent" ]; then printf '%s\n' "$note"; exit 0; fi
    say "$note"
    if ask "Start Capri now?" yes; then
        (cd "$dest" && setsid "$dest/Capri" >/dev/null 2>&1 < /dev/null &)
    fi
    exit 0
}

case $mode in
    uninstall) uninstall ;;
    verify) check_payload; echo "Capri $VERSION installer is intact."; exit 0 ;;
    extract) check_payload; unpack "$extract_to"; echo "Capri $VERSION unpacked into $extract_to."; exit 0 ;;
    install)
        if [ -z "$(payload_line)" ] || case $PAYLOAD_SHA256 in @*) true ;; *) false ;; esac; then
            # Her own copy in her folder: nothing to install, so this is the uninstaller.
            uninstall
        fi
        install ;;
esac
exit 0
