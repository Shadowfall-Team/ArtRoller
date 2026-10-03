using ArtRoller.Patches;
using MegaCrit.Sts2.Core.Models;

namespace ArtRoller;

/// <summary>
/// Works out which character a card is being shown "as", so a card reprinted into another
/// character's pool can carry its own art roll without affecting the character it came from.
///
/// A reprinted card is the *same* CardModel type as the original, so its id is shared. Scoping
/// the art roll by character keeps one id usable for both: Havoc drafted by the Ironclad renders as
/// the base game intends, while Havoc drafted by a modded character can be recolored to match.
/// </summary>
public static class ArtContext
{
    /// <summary>
    /// Separates card id from character id in a scoped art roll key, e.g.
    /// <c>CARD.HAVOC@INTOTHESPIREVERSE-SHADOW_IRONCLAD</c>. Chosen because it cannot occur in a
    /// ModelId, so an unscoped key can never be mistaken for a scoped one.
    /// </summary>
    public const char ScopeSeparator = '@';

    public static string ScopedKey(string cardId, CharacterModel character) =>
        $"{cardId}{ScopeSeparator}{character.Id.Entry}";

    /// <summary>
    /// A card is a reprint when it comes from a different assembly than the character showing it.
    /// A card a mod defines for its own character is unambiguous already, and scoping it would
    /// just add a second key to keep in sync.
    /// </summary>
    public static bool NeedsScoping(CardModel card, CharacterModel character) =>
        card.GetType().Assembly != character.GetType().Assembly;

    /// <summary>The key to save this card under right now: scoped for a reprint, plain otherwise.</summary>
    public static string KeyFor(CardModel card)
    {
        string cardId = card.Id.ToString();
        var character = For(card);
        return character != null && NeedsScoping(card, character)
            ? ScopedKey(cardId, character)
            : cardId;
    }

    /// <summary>
    /// The character this card is being shown as, or null when there is none.
    ///
    /// In combat and anywhere else the card has a real owner, the owner's character decides. In the
    /// compendium the cards are canonical and ownerless, so the library's selected pool filter
    /// decides instead - see <see cref="CardLibraryContext"/>.
    /// </summary>
    public static CharacterModel? For(CardModel? card)
    {
        if (card == null) return null;

        // Owner asserts mutability and throws CanonicalModelException on a canonical card, so the
        // check has to come first rather than relying on a null return.
        if (!card.IsCanonical && card.Owner?.Character is { } owned)
            return owned;

        return CardLibraryContext.ViewedCharacter;
    }
}
