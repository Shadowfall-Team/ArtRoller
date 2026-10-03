# Developer Mode

This mode is intended for mod authors making recolors to ship with their mod. It has everything in [Personal Edit Mode](personal-edit-mode.md), plus:

- **Save Default:** writes the card's settings as a file to ship with your mod, into the designated output folder.
- **Clear Default:** deletes that file from the output folder and sets the card back to showing its original settings.
- **Output folder** and **...**: choose where Save Default writes. Point it at the `ArtRoller` folder you create in your mod's project; see [Workflow](#workflow).
- **Key:** the file name the buttons write to. A key ending in `@CHARACTER` is a reprint, which applies only while that character shows the card. See [Keys and reprints](hsv-format.md#keys-and-reprints).

## Setting up your mod

1. Add Art Roller to your manifest's dependencies:

   ```json
   "dependencies": [{"id": "BaseLib", "min_version": "3.4.7"}, {"id": "ArtRoller", "min_version": "1.0.0"}]
   ```

2. (OPTIONAL) Reference Art Roller from your `.csproj` through NuGet, the same way as BaseLib. You need this only if your code calls Art Roller, such as patching it, or registering your own custom file storage folders in step 3. 

   ```xml
   <PackageReference Include="Shadowfall.Sts2.ArtRoller" Version="1.0.0" PrivateAssets="All" ExcludeAssets="runtime" />
   ```

3. Art Roller reads recolors from `res://{YourModId}/ArtRoller/` and portrait search art from `res://{YourModId}/images/card_portraits/` automatically. For other folders, register them in your mod initializer (this requires the reference being performed in step 2):

   ```csharp
   using ArtRoller;

   CardArtRoller.RegisterDefaultsDirectory("res://MyMod/SomewhereElse");
   CardArtRoller.RegisterPortraitDirectory("res://MyMod/art/portraits");
   ```

   Art Roller will detect portraits that you have included in your mod but are not called anywhere else. This means a modder could choose to use Art Roller to further edit their own custom artwork, or use it as a convenient in-game art hookup tool.

4. Add `*.hsv` to `include_filter` in your `export_presets.cfg`, so they are picked up in your export .pck.

## Workflow

1. In your mod's project, create a folder named `ArtRoller` inside your mod's resource folder. 
2. In-game, turn on **Developer Mode**, and set the output folder to that folder with the **...** button.
3. In the Compendium, open a card, adjust it, and press **Save Default**. The change shows immediately.
4. Rebuild your mod to pack the new files.
5. From then on your mod loads the new recolors as its shipped defaults. On your own machine, a personal edit made with **Save** still takes priority over the default for the same card. Press **Clear** on that card, or turn off **Load Personal Edits** with both editor modes off, to see the true shipped default. Personal edits are stored in the game's user data folder under `ArtRoller`, which on Windows is `%APPDATA%\SlayTheSpire2\ArtRoller`.
