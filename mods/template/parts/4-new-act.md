# A new act

An act is something that happens to her, like being petted or praised. A pack can add one of its own:

```json
"adds": {
  "acts": [ { "name": "coffee", "kind": "food", "like": 3 } ]
}
```

1. `name`: lowercase letters, numbers and spaces or dashes. Not one of hers: give hers a mood term instead.
2. `kind`: what sort of act it is, like `food`, `touch` or `words`. Left out, it's `other`.
3. `like`: how much she likes it, -5 to 5.
4. It goes when the pack is off. Her mood log names the pack beside it.
