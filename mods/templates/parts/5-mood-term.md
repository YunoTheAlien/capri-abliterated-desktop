# A mood term

A term changes how much an act is worth to her while the pack is on. It works on hers and on the pack's own:

```json
"adds": {
  "mood": [ { "act": "coffee", "add": 1 }, { "act": "petted", "add": -1 } ]
}
```

1. `add` is from -3 to 3. Two packs adding to the same act both count.
2. Her mood log shows each term by the pack's name, so you can see the sum.
3. A multiplier (worth times 0.25 to 2) needs a code mod: see [9-switch.md](9-switch.md).
