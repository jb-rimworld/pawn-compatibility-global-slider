# Changelog

## 1.1.0 (2026-11-24)
- Built and tested on RimWorld 1.7, against the 1.7-ready Pawn Compatibility Editor (also checked without it).
- Auto mode and its target are now saved per save. The global settings only set the defaults for new games; a save without values takes them once, and two saves can differ.
- New option: leave lovers, spouses, fiances and blood relatives (siblings and cousins too) alone, for Auto and for Apply.
- Removing the mod mid-save leaves no error: the Auto values live in a plain node of the save instead of a game component.
- A missing Pawn Compatibility Editor is a warning, not an error, and Auto does nothing quietly without it.
- The About page links the base mod so players without it get a subscribe prompt.
- English and German texts for all new settings.
