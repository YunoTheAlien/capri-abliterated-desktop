# Numbers she reads

Some headings hold numbers rather than lines, like `What she likes` or `How she reacts` in a personality. A pack takes
them over the same way. This one gives Sassy a longer repeat window, so doing the same thing to her again counts as
again for 30 to 90 minutes instead of 20 to 60:

```json
"overrides": [
  { "slot": "personalities/Sassy.md > How she reacts", "text": "flirt: 3\ncringe: 25\n...\nrepeat window: 30 to 90\n..." }
]
```

1. Copy the whole heading from her file and change the numbers you want. The full text is in `night-owl.capri-mod`.
2. `slots.md` says which headings hold numbers.
