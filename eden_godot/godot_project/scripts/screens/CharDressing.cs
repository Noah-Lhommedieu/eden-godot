using Godot;
using EdenClient.Game;

namespace EdenClient.Screens;

/// <summary>
/// Habillage du personnage pour l'écran de sélection — délègue la
/// visibilité à <see cref="CharVisual.ApplyLook"/> (même passe qu'en
/// jeu : visage + cheveux + tenue) afin que le sélecteur montre la
/// <b>vraie tenue</b> du perso.
///
/// Convention des meshes du mega-GLB <c>char/model/&lt;L&gt;.glb</c> :
///   - <c>#&lt;L&gt;FACE</c> / <c>#MHAIR</c> : visage (+ oreilles) —
///     la texture <c>&lt;L&gt;FACES&lt;style&gt;&lt;variante&gt;</c>
///     porte les yeux (posée par CharVisual.ApplyFaceVariant) ;
///   - <c>#&lt;L&gt;000</c>   : corps peau de base (G/C/F/L seulement);
///   - <c>#&lt;L&gt;1xx</c>   : coiffures (mesh le plus proche de
///     hairStyle) ;
///   - <c>#&lt;L&gt;2xx</c> + <c>S&lt;L&gt;2xx</c> : tenue +
///     peau exposée — suffixe résolu depuis l'équipement réel via
///     4pequip.ini (code/model → mesh « #&lt;L&gt;2xx ») ou repli
///     débutant « 202 » (<see cref="CharVisual.StarterOutfits"/>).
///     NB : « 298/299 » = sous-vêtements/costume caché (K2999x) —
///     ne JAMAIS les hardcoder : le joueur y voyait une tenue hors
///     de propos.
/// </summary>
public static class CharDressing
{
    /// <summary>Montre visage + cheveux + tenue de l'équipement réel
    /// (<paramref name="worn"/> loc→itemId — Preview de la liste
    /// persos, ou <c>PlayerEquipment.Worn</c> en zone ; vide/null →
    /// tenue de débutant). <paramref name="lookup"/> : itemId →
    /// (Code, ModelCode) c_item — peut être null si
    /// <paramref name="worn"/> est vide. Retourne le nombre de
    /// meshes rendus visibles. <paramref name="classId"/>/<paramref
    /// name="style"/> (KB-10) : classe du perso — sans armure portée la
    /// tenue de base est celle de SA classe, plus le repli « 202 »
    /// (Guerrier) pour tout le monde.</summary>
    public static int Apply(Node3D body, byte[]? appearance,
        IReadOnlyDictionary<int, int>? worn = null,
        Func<int, (string Code, string ModelCode)?>? lookup = null,
        int classId = 0, int style = 0)
    {
        var letter = CharModelSpec.BodyStem(appearance);
        var hair = appearance is { Length: >= 29 }
            ? Protocol.AppearanceCodec.Decode(appearance).HairStyle
            : 0;
        var shown = CharVisual.ApplyLook(body, letter, hair,
            CharVisual.OutfitSuffixes(letter, worn, lookup, classId, style));
        // ApplyLook montre des tenues cachées au teintage de
        // BuildBody (filtre Visible de TintMeshes) — les re-teinter
        // avec les specs 4pequip du porté (cache idempotent).
        CharVisual.RetintVisible(body,
            CharVisual.OutfitRows(letter, worn, lookup, classId, style));
        return shown;
    }
}
