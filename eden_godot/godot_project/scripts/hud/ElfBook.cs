using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// État client du tableau de familiers (elf pets) — modèle pur (pas
/// de nœud Godot), rempli par <see cref="ElfBridge"/> depuis les
/// paquets 412/413/414/418/513/567/568/588 et lu par
/// <see cref="ElfWindow"/>.
///
/// Sémantique native des champs (NativeElfCodec serveur +
/// ZoneEntryFlow.Elf.cs) :
///   - <see cref="ElfRecord.Status"/> : 0 repos, 1 mix/intégration,
///     2 invoqué, 7/8/9 loterie ;
///   - <see cref="ElfRecord.Quality"/> = GrowType du partner
///     (CPartnerData+0x14) — pas une « qualité » loot ;
///   - HP = « vitalité » affichée (pas de faim/humeur sur le fil —
///     le snapshot natif ne les transporte pas) ;
///   - <see cref="SummonedLoc"/> suit le 513 ; un elf status==2 est
///     cohérent avec SummonedLoc.
/// </summary>
public sealed class ElfBook
{
    /// <summary>Instance partagée (pont elf + fenêtre inventaire —
    /// l'ack 277 des moves de gear pet y est appliqué localement).</summary>
    public static ElfBook Shared { get; } = new();

    /// <summary>Elfs par loc (0..9 — le tableau natif en tient 10).</summary>
    public List<ElfRecord> Elfs { get; } = new();
    /// <summary>Loc de l'elf invoqué (−1 = aucun — piloté par 513).</summary>
    public short SummonedLoc { get; private set; } = -1;
    /// <summary>Template de l'elf en slot d'intégration (567 — 0
    /// = aucun).</summary>
    public int MixTemplateId { get; private set; }
    /// <summary>Cast de fusion en cours (574 → ticks restants ; 0 =
    /// inactif — annulé par 575).</summary>
    public int UpgradeTicks { get; private set; }
    /// <summary>Stats dérivées du niveau (418 types 8-11 :
    /// attack/hit/crit/atkspd — émis au level-up natif).</summary>
    public int[] CombatStats { get; } = new int[4];
    /// <summary>Items d'équipement portés par les elfs (424).</summary>
    public List<ZoneEnterNodeElfContainer.ElfEquipSlot> Equip { get; } = new();
    /// <summary>Notifié après chaque mutation (UI refresh).</summary>
    public event Action? Changed;

    /// <summary>Snapshot 412 — remplace le tableau entier.</summary>
    public void ApplySnapshot(ZoneEnterNodeElfInfo m)
    {
        Elfs.Clear();
        Elfs.AddRange(m.Elfs);
        SummonedLoc = m.Elfs.FirstOrDefault(e => e.Status == 2)?.Loc
            ?? (short)-1;
        Changed?.Invoke();
    }

    /// <summary>413 — un elf ajouté (usage d'œuf).</summary>
    public void ApplyAdd(ZoneElfAdd m)
    {
        if (m.Elf == null) return;
        Elfs.RemoveAll(e => e.Loc == m.Elf.Loc || e.ElfId == m.Elf.ElfId);
        Elfs.Add(m.Elf);
        Elfs.Sort((a, b) => a.Loc.CompareTo(b.Loc));
        Changed?.Invoke();
    }

    /// <summary>414 — un elf quitte le tableau (libération /
    /// scellement / fusion consommée).</summary>
    public void ApplyReturnOk(ZoneElfReturnOk m)
    {
        if (Elfs.RemoveAll(e => e.Loc == m.Loc) > 0 &&
            SummonedLoc == m.Loc)
            SummonedLoc = -1;
        Changed?.Invoke();
    }

    /// <summary>418 — attribut unitaire. Types natifs émis :
    /// 1 exp, 2 hp, 8-11 stats niveau, 12 nom.</summary>
    public void ApplyUpdateAttr(ZoneElfUpdateAttr m)
    {
        var e = ByLoc(m.Loc);
        if (e == null) return;
        switch (m.AttrType)
        {
            case 1:  e.Exp = m.Value; break;
            case 2:  e.Hp = m.Value; break;
            case 8:  CombatStats[0] = m.Value; break;
            case 9:  CombatStats[1] = m.Value; break;
            case 10: CombatStats[2] = m.Value; break;
            case 11: CombatStats[3] = m.Value; break;
            case 12: e.Name = m.Text; break;
        }
        Changed?.Invoke();
    }

    /// <summary>513 — raccourci pet : flag 1 = invoqué à
    /// <paramref name="m"/>.Loc, 0 = renvoyé.</summary>
    public void ApplyShortCut(ZonePetShortCut m)
    {
        var e = ByLoc(m.Loc);
        if (m.Flag != 0)
        {
            SummonedLoc = m.Loc;
            if (e != null) e.Status = 2;
        }
        else
        {
            if (SummonedLoc == m.Loc) SummonedLoc = -1;
            if (e != null) e.Status = 0;
        }
        Changed?.Invoke();
    }

    /// <summary>567 — template de l'elf en mix d'un joueur (on ne
    /// suit que le self — le bridge filtre par charId).</summary>
    public void ApplyMixElf(int mixTemplateId)
    {
        MixTemplateId = mixTemplateId;
        foreach (var e in Elfs)
            if (e.Status == 1 && e.TemplateId != mixTemplateId)
                e.Status = 0;
        if (mixTemplateId != 0)
        {
            var mixElf = Elfs.FirstOrDefault(e =>
                e.TemplateId == mixTemplateId);
            if (mixElf != null) mixElf.Status = 1;
        }
        Changed?.Invoke();
    }

    /// <summary>568 — résultat de fusion : nouvelles grow/star/spells.</summary>
    public void ApplyMixResult(ZoneUpdateElfMixElf m)
    {
        var e = ByLoc(m.Loc);
        if (e == null) return;
        e.MixStar = m.MixStar;
        for (var i = 0; i < 4 && i < m.Grow.Count; i++)
            e.Grow[i] = m.Grow[i];
        for (var i = 0; i < 5 && i < m.Battle.Count; i++)
            e.Battle[i] = m.Battle[i];
        for (var i = 0; i < 5 && i < m.Integrate.Count; i++)
            e.Integrate[i] = m.Integrate[i];
        UpgradeTicks = 0;
        Changed?.Invoke();
    }

    /// <summary>588 — sorts après apprentissage d'un livre.</summary>
    public void ApplySpellUpdate(ZoneUpdateElfSpell m)
    {
        var e = ByLoc(m.Loc);
        if (e == null) return;
        for (var i = 0; i < 5; i++)
        {
            e.Battle[i] = i < m.Battle.Count ? m.Battle[i] : 0;
            e.Integrate[i] = i < m.Integrate.Count ? m.Integrate[i] : 0;
        }
        Changed?.Invoke();
    }

    /// <summary>574/575 — début/annulation du cast de fusion
    /// (ticks serveur 100 ms).</summary>
    public void ApplyUpgradeCasting(short ticks) { UpgradeTicks = ticks; Changed?.Invoke(); }
    /// <summary>575 — cast annulé.</summary>
    public void ApplyUpgradeCancel() { UpgradeTicks = 0; Changed?.Invoke(); }

    /// <summary>424 — conteneurs d'équipement : items par (elfLoc,
    /// slot). Remplace l'ensemble du snapshot.</summary>
    public void ApplyContainer(ZoneEnterNodeElfContainer m)
    {
        Equip.Clear();
        Equip.AddRange(m.Items);
        Changed?.Invoke();
    }

    /// <summary>Applique localement un move 277 réussi impliquant le
    /// conteneur elf (type 6) — sémantique serveur :
    /// <c>MoveToElfContainerAsync</c> / <c>MoveFromElfContainerAsync</c>
    /// / <c>SwitchElfContainerSlotsAsync</c> (l'occupant de la
    /// destination prend la place libérée à la source). Retourne le
    /// nouvel itemId (0 = vide) des slots SAC src et dst, à appliquer
    /// par l'appelant ; sans objet pour la partie non-sac.</summary>
    public (int SrcBagItem, int DstBagItem) ApplyPetSwitch(
        short srcType, short srcSlot, short srcIdx,
        short dstType, short dstSlot, short dstIdx,
        int srcBagItem, int dstBagItem)
    {
        var r = EdenClient.Game.PetClientRules.ApplySwitch(Equip,
            srcType, srcSlot, srcIdx, dstType, dstSlot, dstIdx,
            srcBagItem, dstBagItem);
        Changed?.Invoke();
        return r;
    }

    /// <summary>Items portés par l'elf de loc donné.</summary>
    public IEnumerable<ZoneEnterNodeElfContainer.ElfEquipSlot>
        EquipOf(int loc) => Equip.Where(i => i.ElfLoc == loc);

    /// <summary>Elf par loc, null si absent.</summary>
    public ElfRecord? ByLoc(int loc) =>
        Elfs.FirstOrDefault(e => e.Loc == loc);

    /// <summary>Elf actuellement invoqué (status 2 ou SummonedLoc).</summary>
    public ElfRecord? Summoned =>
        ByLoc(SummonedLoc) ??
        Elfs.FirstOrDefault(e => e.Status == 2);

    /// <summary>Stem GLB du familier invoqué (C_Partner → « M144 ») —
    /// pour que le registry résolve le modèle du pet −2537 du self.</summary>
    public string SummonedModelStem()
    {
        var s = Summoned;
        return s == null ? "" : PartnerTable.ModelStemFor(s.TemplateId);
    }

    /// <summary>Vide tout (déconnexion / changement de perso).</summary>
    public void Clear()
    {
        Elfs.Clear();
        SummonedLoc = -1;
        MixTemplateId = 0;
        UpgradeTicks = 0;
        Changed?.Invoke();
    }
}
