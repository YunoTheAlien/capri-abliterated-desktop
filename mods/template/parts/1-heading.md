# A heading taken over

Her lines live under headings in her files. A pack can stand in for one heading and leave the rest of the file as
hers. This one gives her new warm openers, the bit she sometimes puts in front of a line:

```json
"overrides": [
  { "slot": "lines.md > openers warm", "text": "Mm. Hi.\nOh, it's you. Good.\nStill up? Same." }
]
```

1. `slot` is the file, ` > `, then the heading, as `slots.md` writes it.
2. `text` is what goes under the heading, one line per row (`\n` between them). Check-ins are written with `- ` in
   front of each line, as `check-ins.md` has them.
3. Her own file isn't touched. Turn the pack off and her lines are back.
4. Two packs taking the same slot get the conflict box, where you pick which one keeps it.
