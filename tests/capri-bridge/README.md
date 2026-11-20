# Test bridge: typing to a running Capri from a script

For testing only. `capri-test-bridge.capri-code` is an ordinary code mod. It uses only the public mod API (`Chat.Send`,
`Sending`, `Replied`, `ToolUsing`, `ToolUsed`, `Busy`), so Capri's own code doesn't change for it, and it works the same on
Windows, Mac and Linux. `capri-say` drops a line into the mod's folder and prints what she hears, says and does until
she's quiet again.

```
python3 tests/capri-bridge/capri-say --install          # copies the mod into ~/.config/Capri/mods
#   then: right-click her > Capri Mainframe > Mods, approve Test Bridge
python3 tests/capri-bridge/capri-say --status
python3 tests/capri-bridge/capri-say "switch to Files"
python3 tests/capri-bridge/capri-say --watch             # everything as it happens, Ctrl+C to stop
```

Output looks like:

```
08:22:05 sent: minimize Calculator
08:22:05 heard (mod): minimize Calculator
08:22:05 tool  app_window: minimize calculator
08:22:05 done  app_window: <what the tool returned>
08:22:05 CAPRI (reply): ...
08:22:11 idle
```

Exit codes: 0 she answered and went quiet, 1 the mod isn't running, 2 timed out (default 120 s, `--timeout`).

While the mod is on, any program running as you can type to her, the same as you can. Turn it off or delete
`~/.config/Capri/mods/capri-test-bridge.capri-code` when you're done. `Chat.Send` never answers her asking first: a tool
she has to check with you still waits for your own yes.

## Without a real brain

`fake-brain.py` is a pretend model on 127.0.0.1 that answers with fixed choices: "switch to / minimize / close X"
call `app_window`, and "list windows" calls `list_windows`. Use it in a throwaway profile, never your own:

```
python3 tests/capri-bridge/fake-brain.py 8765 &
# in <profile>/Capri/cross.json: "BrainServer": "http://127.0.0.1:8765", "ServerModel": "fake"
```

Run Capri with `HOME`/`XDG_CONFIG_HOME` pointing at the throwaway profile (and under `xvfb-run` to keep it off your
desktop), then pass `--folder <profile>/Capri` to `capri-say`.
