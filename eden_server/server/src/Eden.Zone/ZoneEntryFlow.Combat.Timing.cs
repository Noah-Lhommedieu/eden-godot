using Eden.Core.Data.Parsers;
using Eden.Zone.Codecs;

namespace Eden.Zone;

/// <summary>Partiel combat — délais natifs swing→impact.
///
/// Le natif n'utilise AUCUN délai fixe : le swing (cast/mêlée) dure
/// jusqu'à la clef `hit` de la motion KFM (`CKfm::GetMotionTime`
/// zone@0x080e0b00 sur Motion.ini), puis un éventuel projectile vole
/// `distance / flySpeed` (`CSpellToLife::GetBlastTime` zone@0x084f7db0,
/// `DefaultFlySpeed` = 40 u/s .data@0x089abae2). L'ensemble est armé
/// en décisecondes (`CEventSpellBlasting` @0x0866d6e0). La logique
/// pure (résolution des délais) vit dans <see cref="MotionParser"/>
/// pour rester testable sans serveur live.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Délai swing d'un sort en ms — délégué à
    /// <see cref="MotionParser.BlastSwingMs"/> avec le kfId RÉSOLU
    /// (<paramref name="blastId"/>, sortie de BlastWireId = le kf
    /// réellement joué côté client, pas la référence de pool col3).</summary>
    private int SwingDelayMs(EntryState st, SpellParser.SpellDef? def,
        int blastId) =>
        MotionParser.BlastSwingMs(_services.GameData.MotionTimings,
            MotionParser.KfmForGender(st.GenderId), blastId,
            st.WeaponKfIds);

    /// <summary>Délai `hit` de la motion `kfId` en ms (attaque arme) —
    /// <see cref="MotionParser.KfHitMs"/>.</summary>
    private int KfHitDelayMs(EntryState st, int kfId) =>
        MotionParser.KfHitMs(_services.GameData.MotionTimings,
            MotionParser.KfmForGender(st.GenderId), kfId);

    /// <summary>Temps de vol d'un projectile de sort en ms —
    /// <see cref="MotionParser.ProjectileFlyMs"/> avec la distance
    /// caster→mob (plan (X,Y) mob ↔ (PosX,PosZ), hauteur mob.Z).</summary>
    private static int FlyDelayMs(EntryState st,
        ZoneLiveWorld.LiveMob mob, SpellParser.SpellDef? def)
    {
        if (def is not { HasProjectile: true }) return 0;
        var dx = mob.X - st.PosX; var dy = mob.Y - st.PosZ;
        var dist = Math.Sqrt(dx * dx + dy * dy + mob.Z * mob.Z);
        return MotionParser.ProjectileFlyMs(dist, def.FlySpeed);
    }

    /// <summary>Délai de vol pour un kind d'arme à distance (arc,
    /// arbalète, fusil — le natif ajoute dist/DefaultFlySpeed au
    /// CEventDelayDamage quand l'attaque est projectile).</summary>
    internal static bool IsRangedWeaponKind(int kind) =>
        kind is 0x1c or 0x1d or 0x3f or 0x40 or 0x1f or 0x42;

    private static int MeleeFlyDelayMs(EntryState st,
        ZoneLiveWorld.LiveMob mob)
    {
        var dx = mob.X - st.PosX; var dy = mob.Y - st.PosZ;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        return MotionParser.ProjectileFlyMs(dist, 0);
    }
}
