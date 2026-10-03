# Art Roller

Art Roller is a tool which allows you to recolor, mirror and re-art any card in Slay the Spire 2 - mostly intended for modders to ship their own card recolors and alterations.

- **For players:** Art Roller is an editor in the Compendium for changing how any card looks: colors, brightness, contrast, and more.
- **For mod authors:** Art Roller is also a shared library that allows you to ship your recolors and art changes. Many mods reuse base-game art for their own cards, and Art Roller can help save time and size on disk by rendering changes at runtime rather than needing to ship altered arts as image assets.

Requires **BaseLib**.

## Settings

Art Roller adds three settings to its page in the mod settings:

- **Load Personal Edits** (on by default): shows the edits you have saved. Turn it off to see every card exactly as its mod ships it. Your edits are kept and come back when you turn it on again.
- **Personal Edit Mode** (off by default): adds the editor to the Compendium. See [Personal Edit Mode](https://github.com/Shadowfall-Team/ArtRoller/blob/main/docs/personal-edit-mode.md).
- **Developer Mode** (off by default): everything in Personal Edit Mode, plus the tools for making recolors to ship with a mod. See [Developer Mode](https://github.com/Shadowfall-Team/ArtRoller/blob/main/docs/developer-mode.md).

## Documentation

- [Already pasted Art Roller into your mod?](https://github.com/Shadowfall-Team/ArtRoller/blob/main/docs/migrating.md): Guide for switching from a copied version of the code to this mod as a dependency.
- [The `.hsv` format](https://github.com/Shadowfall-Team/ArtRoller/blob/main/docs/hsv-format.md): Auto-generated explanation of the format of the stored files.
