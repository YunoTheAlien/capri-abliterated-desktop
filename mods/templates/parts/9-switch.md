# A switch of its own, and a multiplier

A pack can't run code, so a switch in her Toggles window, or a multiplier on an act, comes from a small
code mod beside it. `night-owl-switch.capri-code` does both:

```csharp
capri.Toggles.Add("Sleepy pats", false, Changed);

private void Changed(bool on)
{
    if (on) capri.Mood.Multiply("sleepy pats", "petted", 0.5);
    else capri.Mood.Remove("sleepy pats");
}
```

1. Its header says `// permissions: mood` and `// requires: api 6`.
2. A multiplier is 0.25 to 2 and comes after every add. Its name shows in her mood log.
3. She asks before a code mod runs, and asks again when the file changes. Its switch and terms go when it stops.
