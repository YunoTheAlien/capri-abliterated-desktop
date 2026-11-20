// Capri's window helper for GNOME. GNOME on Wayland lets no app see or control another app's windows, so Capri ships
// this small extension of her own. It runs inside GNOME Shell and does exactly four things for her, over the session
// bus, on this computer only: tells her where the windows are (so she can sit on them), and switches to, minimizes or
// closes one when she is asked to. Nothing leaves the machine and nothing else in GNOME is changed.
//
// Capri writes this file into ~/.local/share/gnome-shell/extensions/ with a few lines above and below it for the GNOME
// version she finds (cross/Gnome.cs). Removing that folder removes it; without Capri running it only sits there.
//
// It is written to run on GNOME 40 and later, so no class fields, no ?. and no ??.

const CAPRI_PATH = '/app/capri/Gnome';
const CAPRI_FACE = 'app.capri.Gnome';
const CAPRI_XML = '<node><interface name="app.capri.Gnome">' +
    '<method name="Hello"><arg type="s" direction="out"/></method>' +
    '<method name="Subscribe"><arg type="s" direction="out"/></method>' +
    '<method name="Act"><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="b" direction="out"/></method>' +
    '</interface></node>';
// At most this many Capris (or whatever else) hear the window list at once.
const CAPRI_LISTENERS = 4;

class CapriWindows {
    constructor() {
        this._exported = null;
        this._listeners = new Map();
        this._windows = new Map();
        this._global = [];
        this._pending = 0;
        this._grabbed = null;
    }

    enable() {
        this._exported = Gio.DBusExportedObject.wrapJSObject(CAPRI_XML, this);
        this._exported.export(Gio.DBus.session, CAPRI_PATH);
        const display = global.display;
        this._connect(display, 'window-created', (d, w) => { this._watch(w); this._soon(); });
        this._connect(display, 'restacked', () => this._soon());
        this._connect(display, 'notify::focus-window', () => this._soon());
        this._connect(display, 'grab-op-begin', (d, w) => { this._grabbed = w || null; });
        this._connect(display, 'grab-op-end', () => { this._grabbed = null; this._soon(); });
        this._connect(global.workspace_manager, 'active-workspace-changed', () => this._soon());
        this._connect(Main.layoutManager, 'monitors-changed', () => this._soon());
        for (const w of this._all()) this._watch(w);
    }

    disable() {
        if (this._pending) GLib.source_remove(this._pending);
        this._pending = 0;
        for (const [object, id] of this._global) object.disconnect(id);
        this._global = [];
        for (const [w, ids] of this._windows) for (const id of ids) w.disconnect(id);
        this._windows.clear();
        for (const watch of this._listeners.values()) Gio.bus_unwatch_name(watch);
        this._listeners.clear();
        if (this._exported) this._exported.unexport();
        this._exported = null;
        this._grabbed = null;
    }

    // Which helper this is, so Capri can tell an old one still loaded until the next log-in.
    Hello() {
        return JSON.stringify({ helper: '@HELPER@' });
    }

    // The caller hears the window list from now on, as calls to Windows(s) and Moved(s) at its own name, until it
    // leaves the bus. It gets the list straight away as the reply.
    SubscribeAsync(params, invocation) {
        const name = invocation.get_sender();
        if (name && !this._listeners.has(name)) {
            if (this._listeners.size >= CAPRI_LISTENERS) {
                invocation.return_dbus_error('app.capri.Gnome.Busy', 'Too many listeners.');
                return;
            }
            const watch = Gio.bus_watch_name_on_connection(Gio.DBus.session, name, Gio.BusNameWatcherFlags.NONE, null, () => {
                const gone = this._listeners.get(name);
                if (gone !== undefined) Gio.bus_unwatch_name(gone);
                this._listeners.delete(name);
            });
            this._listeners.set(name, watch);
        }
        invocation.return_value(new GLib.Variant('(s)', [this._list()]));
    }

    // Window ids come from Mutter and are never reused while GNOME runs, so an old id can't land on a new window.
    Act(id, action) {
        const w = this._all().find(one => String(one.get_id()) === id);
        if (!w) return false;
        if (action === 'switch_to') Main.activateWindow(w);
        else if (action === 'minimize') w.minimize();
        else if (action === 'close') w.delete(global.get_current_time());
        else return false;
        return true;
    }

    _connect(object, signal, handler) {
        this._global.push([object, object.connect(signal, handler)]);
    }

    _watch(w) {
        if (!w || this._windows.has(w) || !this._wanted(w)) return;
        const ids = [];
        const on = (signal, handler) => ids.push(w.connect(signal, handler));
        on('position-changed', () => {
            if (this._grabbed === w) this._send('Moved', JSON.stringify(this._row(w)));
            else this._soon();
        });
        on('size-changed', () => this._soon());
        on('notify::minimized', () => this._soon());
        on('notify::title', () => this._soon());
        on('notify::fullscreen', () => this._soon());
        on('workspace-changed', () => this._soon());
        on('unmanaged', () => {
            const mine = this._windows.get(w);
            if (mine) for (const id of mine) w.disconnect(id);
            this._windows.delete(w);
            if (this._grabbed === w) this._grabbed = null;
            this._soon();
        });
        this._windows.set(w, ids);
    }

    _wanted(w) {
        const type = w.get_window_type();
        return (type === Meta.WindowType.NORMAL || type === Meta.WindowType.DIALOG || type === Meta.WindowType.MODAL_DIALOG)
            && !w.is_override_redirect();
    }

    _all() {
        const found = [];
        for (const actor of global.get_window_actors()) {
            const w = actor.get_meta_window();
            if (w && this._wanted(w)) found.push(w);
        }
        return found;
    }

    _shown(w) {
        if (w.minimized) return false;
        return w.is_on_all_workspaces() || w.get_workspace() === global.workspace_manager.get_active_workspace();
    }

    // [id, pid, x, y, w, h, flags, title, class], the same row her KDE bridge sends. Flags: 1 minimized, 2 full screen,
    // 4 on this workspace, 8 focused, 16 an ordinary app window.
    _row(w) {
        const r = w.get_frame_rect();
        const normal = w.get_window_type() === Meta.WindowType.NORMAL && !w.is_skip_taskbar();
        const flags = (w.minimized ? 1 : 0) | (w.is_fullscreen() ? 2 : 0) | (this._shown(w) ? 4 : 0)
            | (global.display.focus_window === w ? 8 : 0) | (normal ? 16 : 0);
        const kind = w.get_wm_class() || w.get_gtk_application_id() || '';
        return [String(w.get_id()), w.get_pid(), r.x, r.y, r.width, r.height, flags, w.get_title() || '', kind];
    }

    // [right edge of the desktop, rows top of the stack first].
    _list() {
        let right = 0;
        for (let i = 0; i < global.display.get_n_monitors(); i++) {
            const m = global.display.get_monitor_geometry(i);
            right = Math.max(right, m.x + m.width);
        }
        const rows = global.display.sort_windows_by_stacking(this._all()).reverse().map(w => this._row(w));
        return JSON.stringify([right, rows]);
    }

    // Changes come in bursts (a window opening restacks, refocuses and resizes), so they're sent once, shortly after.
    _soon() {
        if (this._pending || this._listeners.size === 0) return;
        this._pending = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 40, () => {
            this._pending = 0;
            this._send('Windows', this._list());
            return GLib.SOURCE_REMOVE;
        });
    }

    _send(member, json) {
        for (const name of this._listeners.keys()) {
            Gio.DBus.session.call(name, CAPRI_PATH, CAPRI_FACE, member, new GLib.Variant('(s)', [json]), null,
                Gio.DBusCallFlags.NO_AUTO_START, 1000, null, (connection, result) => {
                    try { connection.call_finish(result); } catch (e) { }
                });
        }
    }
}
