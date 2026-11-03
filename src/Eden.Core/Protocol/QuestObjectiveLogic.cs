using EdenClient.Data.Tables;

namespace EdenClient.Protocol;

/// <summary>Logique pure (sans Godot, testée par NetTests) des objectifs
/// de quête côté client : anneaux du tracker, item de quête utilisable
/// depuis l'objectif (« Use »), repères monde des objectifs d'aire et
/// d'item positionnel (plan futur-feature/01 étapes 3 et 7).</summary>
public static class QuestObjectiveLogic
{
    /// <summary>Compteur affiché d'un objectif (« n/req »). Les refs
    /// (« questtracker le vrai 2 », « drop item, actualisation… ») le
    /// montrent à jour dès que l'item est en sac : pour un objectif lié à
    /// un item on prend max(compteur 391, quantité possédée) ; la clé
    /// <c>ItemCollect</c> (mid×1e6+100000) est PARTAGÉE entre les items
    /// d'une quête → son compteur est ignoré, seul le sac compte. Borné
    /// à <paramref name="req"/>.</summary>
    public static int Progress(MissionObjectives.Kind kind, int counter,
        int itemId, int owned, int req)
    {
        var c = kind == MissionObjectives.Kind.ItemCollect ? owned
            : itemId > 0 ? Math.Max(counter, owned) : counter;
        return Math.Clamp(c, 0, Math.Max(1, req));
    }

    /// <summary>Les objectifs à compteur (kill/collecte/livraison) se
    /// terminent par « n/req » aligné à droite dans le tracker ; parler/
    /// voyager n'en ont pas (ref « le vrai.png »).</summary>
    public static bool ShowsCount(MissionObjectives.Kind k) => k is
        MissionObjectives.Kind.Kill or MissionObjectives.Kind.Treasure
        or MissionObjectives.Kind.ItemGot or MissionObjectives.Kind.ItemGiven
        or MissionObjectives.Kind.ItemCollect;

    /// <summary>La quête est « à rendre » quand tous ses objectifs sont
    /// remplis (FMS 4 natif, mais aussi dès que le client le voit —
    /// l'état 4 peut arriver après le dernier compteur).</summary>
    public static bool AllDone(IEnumerable<(int Progress, int Req)> objs)
    {
        var any = false;
        foreach (var (p, r) in objs)
        {
            any = true;
            if (p < r) return false;
        }
        return any;
    }

    /// <summary>L'item de quête d'un objectif est-il « utilisable » ?
    /// Même critère que le double-clic inventaire (UseType ≠ 0) ou un
    /// enchant d'usage (S_Item col23).</summary>
    public static bool IsUsable(int useType, int useEnchantId) =>
        useType != 0 || useEnchantId > 0;

    /// <summary>Premier stack (sac puis slot croissants) portant l'item.</summary>
    public static (int Bag, int Slot)? FindStack(
        IEnumerable<(int Bag, int Slot, int ItemId)> slots, int itemId)
    {
        (int Bag, int Slot)? best = null;
        foreach (var (bag, slot, id) in slots)
        {
            if (id != itemId) continue;
            if (best == null || (bag, slot).CompareTo(best.Value) < 0)
                best = (bag, slot);
        }
        return best;
    }

    /// <summary>Site d'usage d'un item de quête : (node, clé de
    /// position, mob invoqué). Décode les 4 blocs de commandes d'un
    /// enchant c_enchant (cols 9+7k : id + 6 vars) ; ids 1075/2045 =
    /// SummonInEventArea « mob, compte, node, clé » (serveur
    /// <c>QuestItemRules</c>, CEC_SummonInEventArea::Init @0x0855a590).
    /// Null si aucun bloc positionnel (1074 sans node = au lanceur).</summary>
    public static (int Node, int PosKey, int MobId)? UseSiteOf(
        IReadOnlyList<string> enchantRaw)
    {
        for (var k = 0; k < 4; k++)
        {
            var at = 9 + k * 7;
            if (at + 4 >= enchantRaw.Count) break;
            if (!int.TryParse(enchantRaw[at], out var cmd) ||
                cmd is not (1075 or 2045)) continue;
            int V(int i) => at + i < enchantRaw.Count &&
                int.TryParse(enchantRaw[at + i], out var v) ? v : 0;
            var (mob, node, key) = (V(1), V(3), V(4));
            if (mob > 0 && node > 0 && key > 0) return (node, key, mob);
        }
        return null;
    }

    /// <summary>Point monde d'une aire de voyage (AreaTravel
    /// node×100+keyId) : l'event_area de la gateway de clé
    /// <paramref name="keyId"/>. Coordonnées sol Eden (x, y).</summary>
    public static (float X, float Y)? AreaTravelPoint(
        SceneTable.Scene scene, int keyId)
    {
        foreach (var g in scene.Gateways)
        {
            if (g.KeyId != keyId) continue;
            var a = scene.EventAreas.FirstOrDefault(e =>
                e.Id == g.EventAreaId);
            if (a != null) return (a.X, a.Y);
        }
        return null;
    }

    /// <summary>Point monde d'un site d'usage : première ligne
    /// [position] du groupe <paramref name="posKey"/>.</summary>
    public static (float X, float Y)? UseSitePoint(
        SceneTable.Scene scene, int posKey) =>
        scene.PositionGroups.TryGetValue(posKey, out var l) && l.Count > 0
            ? (l[0].X, l[0].Y) : null;
}
