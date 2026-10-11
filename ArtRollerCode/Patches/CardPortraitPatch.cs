using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace ArtRoller.Patches;

[HarmonyPatch(typeof(CardModel))]
public class CardModelPortraitPatch
{
    private static bool _bypass;

    /// <summary>The card's own art, ignoring any roll's portrait override.</summary>
    public static string OriginalPortraitPath(CardModel card)
    {
        _bypass = true;
        try
        {
            return card.PortraitPath;
        }
        finally
        {
            _bypass = false;
        }
    }

    [HarmonyPatch(nameof(CardModel.PortraitPath), MethodType.Getter)]
    [HarmonyPrefix]
    static bool OverridePortraitPath(CardModel __instance, ref string __result)
    {
        if (_bypass) return true;

        var hsv = CardArtRoller.Resolve(__instance);
        if (hsv != null && !string.IsNullOrWhiteSpace(hsv.PortraitPath))
        {
            __result = hsv.PortraitPath;
            return false;
        }

        return true;
    }

    // 2026-10-10: Borealis Cards 2 overrides PortraitPath without calling base, so the patch above
    // never ran and its rolled cards drew blank. No card can override Portrait, so this patch
    // always runs. Priority.First also skips BaseLib's CustomPortraitPath prefix.
    [HarmonyPatch(nameof(CardModel.Portrait), MethodType.Getter)]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    static bool OverridePortrait(CardModel __instance, ref Texture2D __result)
    {
        var hsv = CardArtRoller.Resolve(__instance);
        if (hsv == null || string.IsNullOrWhiteSpace(hsv.PortraitPath)) return true;

        __result = ResourceLoader.Load<Texture2D>(hsv.PortraitPath, null, ResourceLoader.CacheMode.Reuse);
        return false;
    }
}
