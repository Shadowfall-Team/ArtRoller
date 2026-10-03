# The `.hsv` format

A recolor is a JSON file named after its key. Every number is the editor's slider value divided by 100, so for every adjustment `1` means unchanged, offsets and hue shifts included. Fields a file leaves out are neutral.

`hue`, `saturation`, `value`, `gamma`, `red`, `red_offset`, `green`, `green_offset`, `blue`, `blue_offset`, `contrast`, `tint` (hex color), `flip_h`, `flip_v`, `portrait_path`.

`selective_1`, `selective_2` and `selective_3` are objects with `color` (hex color), `width`, `shift`, `saturation`, `brightness` and `softness`. `width` and `softness` shape the set rather than adjust it: `width` 0 turns the set off, and `softness` runs from 0 (hard edge) to 1 (fades from the target itself), default 0.5.

## Keys and reprints

A recolor is keyed by card id, e.g. `CARD.MYMOD-FIREBALL.hsv`.

A base-game card reprinted into your character's pool keeps the base game's id, so a plain key would recolor it for everyone. When a card is shown by a character from a different mod than the card, Art Roller first looks for a scoped key, `CARD.HAVOC@MYMOD-MY_CHARACTER.hsv`, then falls back to the plain key. In a run the card's owner decides; in the Compendium the selected character tab decides. The editor saves scoped keys on its own when you edit a reprint from your character's tab.

Registered folders are searched first, in registration order, then the automatically found ones. The first match wins.
