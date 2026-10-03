# Already pasted Art Roller into your mod?

We are aware of a handful of mods that copied the Art Roller code out of Into the Spireverse before it was its own mod - if this applies to you, read this page. Two copies of Art Roller patching the same card could interfere with each other. Switching to this mod is thus recommended - but we have taken precautions to ensure it is optional. Compatibility should be preserved for a mod that has the Art Roller code duplicated, in a user that also has the new Art Roller mod subscribed.

**Until you switch,** Art Roller reads your copy's recolors on its own, from `res://{YourModId}/ArtRoller/`, and personal saves from `user://card_hsv_data` and your mod's `ArtRoller` folder. Your cards keep their look for players who have both installed. 

**To switch:**

1. Delete your copy of the Art Roller code. It is usually a folder with these classes:
   - `CardArtRoller` and `CardHsvData`
   - `CardShaderHelper`
   - `CardModelPortraitPatch` and `NCardPatch`
   - `NCardLibraryVerticalSlidersPatch` and `PortraitSearchBox`
   - `AltArtContext` and `CardLibraryCharacterContextPatch`, if you copied the reprint support

   Delete the copied `color_adjust.gdshader` too.
2. Remove the `CardArtRoller.RegisterAllFromDirectory(...)` call from your mod initializer.
3. Leave your `.hsv` files where they are. If they are in `res://{YourModId}/ArtRoller/`, nothing else is needed. If not, register the folder as in [Setting up your mod](developer-mode.md#setting-up-your-mod).
4. Follow [Setting up your mod](developer-mode.md#setting-up-your-mod): add the dependency, and the NuGet package reference if your code calls Art Roller.
5. If your own code called the copied classes, point it at `ArtRoller.CardArtRoller`. The save methods now take a whole recolor instead of a list of numbers: `SaveHsvForCard(key, data)` and `SaveDefaultHsvForCard(key, data)`.

Your existing recolors should keep working unchanged, both before converting to the new Art Roller, and after.