using Eden.Core.Game.Fortune;

namespace Eden.Zone;

/// <summary>Registre des sacs de fortune côté zone — miroir de la map
/// native <c>CZoneServer+0x124</c> remplie par les pushes
/// <c>CNE_ZW_ZoneServerUpdateFortuneBag</c> (ZW 26, Execute zone
/// @0x082afxxx) en 3 phases : 0 = <see cref="FortuneUpdating"/> posé
/// + maps vidées ; 1 = records ajoutés ; 2 = verrou levé.
/// Le verrou bloque l'ouverture de sacs pendant un rechargement —
/// texte natif 0x638 (ChanceLuckyBagStart/OpenFortuneBag).
/// Implémentation double-buffer : les records de la phase 1
/// s'accumulent dans une map de travail publiée en phase 2 — un
/// <see cref="Find"/> concurrent (fil handler) ne voit jamais une map
/// en cours de mutation.</summary>
public sealed class FortuneBagRegistry
{
    private volatile Dictionary<int, FortuneBagDef> _bags = new();
    private Dictionary<int, FortuneBagDef>? _pending;

    /// <summary>Verrou de rechargement natif
    /// (<c>CZoneServer::FortuneUpdating</c>).</summary>
    public bool FortuneUpdating { get; private set; }

    /// <summary>Nombre de sacs chargés (diagnostic).</summary>
    public int Count => _bags.Count;

    /// <summary>Applique un paquet ZW 26 : phase 0 vide + verrouille,
    /// phase 1 insère les records, phase 2 déverrouille et publie
    /// (Execute zone @0x082afxxx). Doit être appelé sur le fil de la
    /// boucle de zone.</summary>
    public void Apply(short phase,
        IReadOnlyList<FortuneBagWire.WireRecord> records)
    {
        switch (phase)
        {
            case 0:
                _pending = new Dictionary<int, FortuneBagDef>();
                FortuneUpdating = true;
                break;
            case 1:
                FortuneBagWire.Apply(_pending ?? _bags, records);
                break;
            case 2:
                if (_pending != null)
                {
                    _bags = _pending;
                    _pending = null;
                }
                FortuneUpdating = false;
                break;
        }
    }

    /// <summary>Recherche native dans la map +0x124.</summary>
    public FortuneBagDef? Find(int bagId)
        => _bags.TryGetValue(bagId, out var bag) ? bag : null;
}
