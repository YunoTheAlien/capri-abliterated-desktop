# A whole file taken over

Add `"whole": true` and name just the file. The pack's text stands in for all of it, headings and all:

```json
"overrides": [
  { "slot": "chess.md", "whole": true, "text": "# Chess, after midnight\n\n## When she wins\n\nMate. Now we can both sleep.\n" }
]
```

1. Write every heading you want her to use. One left out has no lines while the pack is on.
2. A heading without `"whole"` is the usual way: only the parts you change. Use a whole file when you mean to replace
   all of it.
