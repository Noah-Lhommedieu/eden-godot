using Eden.Core.Game.Modifiers;

namespace Eden.Zone.Features.Modifiers;

/// <summary>
/// Construit le <see cref="ModifierContext"/> d'un joueur à partir de son état de zone : type de classe
/// (normale 1-20 / éveillée 21-60) et bonus de charmes actifs (somme des statuts). Un seul endroit
/// pour cette traduction — les systèmes de jeu appellent le moteur avec ce contexte.
/// </summary>
internal static class PlayerModifierContext
{
    public static ClassKind ClassKindOf(int classId) => classId switch
    {
        >= 21 and <= 60 => ClassKind.Awakened,
        >= 1 and <= 20 => ClassKind.Normal,
        _ => ClassKind.None,
    };

    public static ModifierContext For(ZoneEntryFlow.EntryState st, MapKindResolver? kinds = null)
    {
        int exp = 0, cls = 0, awakened = 0;
        foreach (var s in st.Statuses.Values)
        {
            exp += s.ExpRateBonus; cls += s.CpRateBonus; awakened += s.WakenCpBonus;
        }
        return new ModifierContext(CharId: (int)st.CharId, MapId: st.NodeId,
            MapKind: kinds?.KindOf(st.NodeId) ?? Eden.Core.Game.Maps.MapKind.Unknown,
            ClassKind: ClassKindOf(st.ClassId), CharmExpPercent: exp,
            CharmClassPercent: cls, CharmAwakenedPercent: awakened,
            IsVip: VipDirectory.IsVip(st.AccountId));
    }
}
