using System.Collections.Concurrent;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;

namespace Eden.Core.Game.Classes;

/// <summary>
/// Service des certificats de classe (« License Knowledge ») —
/// miroir fidèle de l'état natif `CCharacter` :
/// <code>
///   +0xaf0 vector&lt;SCharacterLicense&gt;  licences possédées {id,loc}
///   +0xafc s16[8]  enchant appliqué par slot (0 inutilisé)
///   +0xb08 s16[8]  licence posée par slot (1..7)
///   +0xb14 s16[2]  enchant de combo actif par moitié (1-4 / 4-7)
/// </code>
/// Logique reproduite de `SetCurrentClassLicense` (@0x82714f0),
/// `SetOtherClassLicense` (@0x82717f0), `ReplaceSpellStones`
/// (@0x8418cd0) et `InvokeSpellStonesSet` (@0x8418aa0).
///
/// Capacité native : `niveau_personnage / 10 + 1` slots actifs,
/// erreur client `0x325` au dépassement (les deux fonctions).
/// Chaque licence posée applique l'enchant de son item
/// (`CItemData+0x60` = col23 `UseEnchantId`) via
/// `EffectToLife(…,-2,0x1000)` ; les combos résolus par
/// <c>QueryEquipSetEnchant</c> s'appliquent de même.
/// </summary>
public sealed class LicenseService
{
    /// <summary>Capacité native : `charLevel/10 + 1` slots
    /// (`SetCurrentClassLicense` : `(lv/10)+1 <= occupied` → 0x325 ;
    /// `SetOtherClassLicense` : même borne avant d'écrire lic_N).
    /// </summary>
    public static int CapacityFor(int characterLevel)
        => Math.Min(7, characterLevel / 10 + 1);

    /// <summary>Argument affiché dans le message 0x325 — le natif passe
    /// <c>lv/10 + 2</c> (texte seulement ; la règle reste
    /// <see cref="CapacityFor"/>, KB-18).</summary>
    public static int DisplayedCapacityFor(int characterLevel)
        => characterLevel / 10 + 2;

    /// <summary>Erreur native de dépassement de capacité —
    /// `ClientCommandFailReturn(0x325, …, lv/10+2)` (affichage seul).</summary>
    public const int ErrorOverCapacity = 0x325;

    /// <summary>État runtime par personnage — les 4 vecteurs natifs.</summary>
    public sealed class SpellStoneState
    {
        /// <summary>`+0xaf0` : licences possédées {itemId, loc} —
        /// alimenté par `player_license` au login.</summary>
        public List<(short ItemId, byte Loc)> Owned { get; } = new();
        /// <summary>`+0xb08` : licence posée par slot (index 1..7).</summary>
        public short[] SlotLicense { get; } = new short[8];
        /// <summary>`+0xafc` : enchant actif par slot (index 1..7).</summary>
        public short[] SlotEnchant { get; } = new short[8];
        /// <summary>`+0xb14` : enchant de combo actif — [0] moitié
        /// slots 1-4, [1] moitié slots 4-7.</summary>
        public short[] SetEnchant { get; } = new short[2];
    }

    private readonly ConcurrentDictionary<int, SpellStoneState>
        _states = new();
    private readonly ILicenseRepository _licenses;
    private readonly IReadOnlyDictionary<int, ItemParser.ItemDef>
        _itemDefs;
    private readonly EquipSetParser.EquipSetPool _pool;
    private readonly EnchantService _enchants;

    /// <summary>Construit le service — `itemEnchants` = dict
    /// itemId → enchantId (col23) pour résoudre les récompenses de
    /// combo et l'enchant posé par licence. `enchantDescriptions`
    /// (T_Enchant) n'est plus lu : les bonus natifs viennent des
    /// commandes, pas du texte — l'ancien parsing « EVA +N% »
    /// comptait l'esquive en double avec AttrDodgeRateRate (2088).
    /// Le paramètre reste pour compatibilité des appelants.</summary>
    public LicenseService(ILicenseRepository licenses,
        IReadOnlyDictionary<int, ItemParser.ItemDef> itemDefs,
        EquipSetParser.EquipSetPool pool,
        EnchantService enchants,
        IReadOnlyDictionary<int, string>? enchantDescriptions = null)
    {
        _licenses = licenses;
        _itemDefs = itemDefs;
        _pool = pool;
        _enchants = enchants;
        _ = enchantDescriptions;
    }

    /// <summary>État runtime du personnage (créé à la demande).</summary>
    public SpellStoneState StateOf(int charId)
        => _states.GetOrAdd(charId, _ => new SpellStoneState());

    /// <summary>Charge `player_license` au login — remplit
    /// <c>Owned</c> (+0xaf0) et reconstruit les slots 1..7 de la
    /// classe active (loc&gt;0 → SlotLicense/SlotEnchant, miroir du
    /// `CLicense::Load` natif à l'entrée).</summary>
    public async Task LoadAsync(int charId,
        CancellationToken token = default)
    {
        var st = StateOf(charId);
        st.Owned.Clear();
        Array.Clear(st.SlotLicense);
        Array.Clear(st.SlotEnchant);
        Array.Clear(st.SetEnchant);
        foreach (var lic in await _licenses.GetLicensesAsync(charId))
        {
            st.Owned.Add((lic.ItemId, (byte)lic.Loc));
            if (lic.Loc is >= 1 and <= 7)
            {
                st.SlotLicense[lic.Loc] = lic.ItemId;
                st.SlotEnchant[lic.Loc] =
                    (short)EnchantOf(lic.ItemId);
            }
        }
    }

    /// <summary>Enchant d'une licence — `CItemData+0x60` =
    /// col23 <see cref="ItemParser.ItemDef.UseEnchantId"/> lu par
    /// `ReplaceSpellStones` (puVar5[0x18]). Les ids de certificats
    /// dépassent 32767 (33xxx/34xxx) : le wire s16 les transporte en
    /// négatif, on normalise en u16 pour le lookup item.</summary>
    public int EnchantOf(int itemId)
        => _itemDefs.TryGetValue(itemId & 0xFFFF, out var def)
            ? def.UseEnchantId : 0;

    /// <summary>`SetCurrentClassLicense` natif — applique les
    /// sélections {licence→slot} sur la classe ACTIVE. Retourne les
    /// licences modifiées (écho du 509) ou
    /// <see cref="ErrorOverCapacity"/> si le nombre de slots occupés
    /// dépasse `charLevel/10 + 1`. Le natif déplace les licences en
    /// conflit de slot vers loc=0 et les inclut dans l'écho.</summary>
    public async Task<(int Error, List<(short ItemId, byte Loc)> Changed)>
        SetCurrentClassLicenseAsync(int charId, int characterLevel,
            IReadOnlyList<(short LicenseId, byte Slot)> selections)
    {
        var st = StateOf(charId);
        var changed = new List<(short ItemId, byte Loc)>();
        var capacity = CapacityFor(characterLevel);
        var error = 0;

        foreach (var (licId, rawSlot) in selections)
        {
            var idx = st.Owned.FindIndex(o => o.ItemId == licId);
            if (idx < 0) continue;           // licence non possédée
            var slot = (byte)(rawSlot & 7);  // `byte & 7` natif
            var old = st.Owned[idx];

            if (slot == 0)
            {
                // Déséquipe : ReplaceSpellStones(oldLoc, 0) —
                // retire enchant + efface les combos actifs.
                if (old.Loc is >= 1 and <= 7)
                    ReplaceSpellStones(st, old.Loc, 0);
                st.Owned[idx] = (licId, 0);
                changed.Add((licId, 0));
                await _licenses.SetLicenseLocAsync(charId, licId, 0);
                continue;
            }

            // Passe de conflit (boucle LAB_082715a7) : toute autre
            // licence au même slot → loc=0 + echo + persistance ;
            // la licence cible voit son ancien enchant retiré.
            var occupied = 0;
            for (var i = 0; i < st.Owned.Count; i++)
            {
                var o = st.Owned[i];
                if (o.ItemId == licId)
                {
                    if (o.Loc != 0)
                        ReplaceSpellStones(st, o.Loc, 0);
                    continue;
                }
                if (o.Loc == slot)
                {
                    st.Owned[i] = (o.ItemId, 0);
                    ReplaceSpellStones(st, slot, 0);
                    changed.Add((o.ItemId, 0));
                    await _licenses.SetLicenseLocAsync(
                        charId, o.ItemId, 0);
                    continue;
                }
                if (o.Loc != 0) occupied++;
            }
            // Capacité : les slots déjà occupés + le nouveau doivent
            // tenir dans `lv/10+1` (condition native `cap <= occupied`
            // → rejet 0x325 pour CETTE sélection puis passage à la
            // suivante — le natif ne stoppe pas la boucle).
            if (occupied >= capacity)
            {
                error = ErrorOverCapacity;
                continue;
            }

            ReplaceSpellStones(st, slot, licId);
            st.Owned[idx] = (licId, slot);
            changed.Add((licId, slot));
            await _licenses.SetLicenseLocAsync(charId, licId,
                (short)slot);
        }
        return (error, changed);
    }

    /// <summary>`SetOtherClassLicense` natif — écrit le layout
    /// lic_1..7 d'une classe NON active. Les sélections sont filtrées
    /// aux licences possédées (le `Execute` natif ne pousse que les
    /// ids trouvés dans +0xaf0). Chaque sélection valide (slot 1..7)
    /// consomme un cran de capacité `charLevel/10+1` — dépassement →
    /// 0x325. Retourne le layout final (0 = slot vide).</summary>
    public int ValidateOtherClassLayout(int charId, int characterLevel,
        IReadOnlyList<(short LicenseId, byte Slot)> selections,
        int[] layout /* int[7] */)
    {
        var st = StateOf(charId);
        Array.Clear(layout);
        var capacity = CapacityFor(characterLevel);
        var used = 0;
        var error = 0;
        foreach (var (licId, slot) in selections)
        {
            if (st.Owned.All(o => o.ItemId != licId)) continue;
            if ((byte)(slot - 1) >= 7) continue;  // slot hors 1..7
            // Le natif erre sur chaque sélection au-delà de la
            // capacité puis continue la boucle (pas d'arrêt).
            if (used >= capacity)
            {
                error = ErrorOverCapacity;
                continue;
            }
            layout[slot - 1] = licId;
            used++;
        }
        return error;
    }

    /// <summary>`InvokeSpellStonesSet` natif (NCID 183) — le dernier
    /// u16 est le flag de moitié (0 = slots 1-4, 1 = slots 4-7),
    /// retiré du vecteur avant validation. Chaque id doit être posé
    /// dans la moitié correspondante ; le set trié est résolu via
    /// <see cref="EquipSetParser.EquipSetPool"/> (vtable+0x188).
    /// Retourne l'enchant de combo appliqué (0 = refus/aucun).</summary>
    public short InvokeSpellStonesSet(int charId,
        IReadOnlyList<ushort> idsWithFlag)
    {
        if (idsWithFlag.Count < 3) return 0;
        var st = StateOf(charId);
        var flag = idsWithFlag[^1];
        if (flag > 1) return 0;
        var ids = idsWithFlag.Take(idsWithFlag.Count - 1).ToList();
        if (ids.Count == 0) return 0;

        // Validation native : flag 0 → ids ⊆ slots 1..4,
        // flag 1 → ids ⊆ slots 4..7 (slot 4 partagé).
        var lo = flag == 0 ? 1 : 4;
        var hi = flag == 0 ? 4 : 7;
        foreach (var id in ids)
        {
            var found = false;
            for (var s = lo; s <= hi; s++)
                if (st.SlotLicense[s] == (short)id) { found = true; break; }
            if (!found) return 0;
        }

        // L'ancien combo de la moitié est retiré puis le nouveau
        // appliqué (début de fonction natif : RemoveEnchantByID +
        // clear b14[flag]).
        if (st.SetEnchant[flag] != 0)
            st.SetEnchant[flag] = 0;

        var enchant = (short)_pool.QueryEnchant(
            ids.Select(i => i).ToArray());
        if (enchant == 0) return 0;
        st.SetEnchant[flag] = enchant;
        return enchant;
    }

    /// <summary>`ReplaceSpellStones` natif — pose/retire la licence
    /// du slot : efface l'enchant précédent, réinitialise les deux
    /// combos actifs (le natif purge `+0xb14` à chaque changement de
    /// slot), applique l'enchant du nouvel item si non déjà actif.</summary>
    private void ReplaceSpellStones(SpellStoneState st, byte slot,
        short licId)
    {
        if (slot == 0 || slot >= 8) return;
        if (st.SlotLicense[slot] == licId) return;
        if (st.SlotEnchant[slot] != 0)
        {
            st.SlotEnchant[slot] = 0;
            st.SlotLicense[slot] = 0;
        }
        // Les deux combos sont invalidés par tout changement de slot.
        st.SetEnchant[0] = 0;
        st.SetEnchant[1] = 0;
        if (licId != 0)
        {
            var enchant = (short)EnchantOf(licId);
            if (enchant != 0)
            {
                st.SlotEnchant[slot] = enchant;
                st.SlotLicense[slot] = licId;
            }
        }
    }

    /// <summary>Persiste une loc — `UPDATE player_license SET loc`
    /// natif. Utilisé par la bascule de classe (ChangeClass) qui
    /// réécrit les loc en masse.</summary>
    public Task SetLocAsync(int charId, short itemId, short loc)
        => _licenses.SetLicenseLocAsync(charId, itemId, loc);

    /// <summary>`CPlayerFactory::GetLicense` natif — octroie la
    /// licence du palier : INSERT player_license(loc=0), puis
    /// suppression de la licence remplacée (`CItemData+0x8c`,
    /// <see cref="ItemParser.ItemDef.LicenseReplaceId"/>).</summary>
    public async Task GrantLicenseAsync(int charId, short licId)
    {
        var st = StateOf(charId);
        if (st.Owned.Any(o => o.ItemId == licId)) return;
        await _licenses.AddLicenseAsync(charId, licId, 0);
        st.Owned.Add((licId, 0));
        // Même normalisation u16 qu'EnchantOf — les certs 33xxx
        // arrivent en s16 négatif.
        if (_itemDefs.TryGetValue(licId & 0xFFFF, out var def) &&
            def.LicenseReplaceId != 0)
        {
            var old = (short)def.LicenseReplaceId;
            var idx = st.Owned.FindIndex(o => o.ItemId == old);
            if (idx >= 0)
            {
                var loc = st.Owned[idx].Loc;
                if (loc is >= 1 and <= 7)
                    ReplaceSpellStones(st, loc, 0);
                st.Owned.RemoveAt(idx);
                await _licenses.RemoveLicenseAsync(charId, old);
            }
        }
    }

    /// <summary>Contribution stats des enchants actifs — licences
    /// posées (`+0xafc`) + combos (`+0xb14`). Chaque enchant est lu
    /// via <see cref="EnchantService.GetPassiveCommands"/> : le natif
    /// SORT du vecteur passif les blocs listés en col40
    /// (`CEnchantQuery::Deserialize` @0x85ecf00 — `*slotPtr = 0` après
    /// transfert dans le set de déclenchement), donc les enchants à
    /// proc ne donnent leurs bonus que lorsque l'événement survient —
    /// pas en stats permanentes. Seules les commandes portant sur les
    /// canaux du pipeline sont sommées par
    /// <see cref="ApplyEnchantCommands"/> ; les ids combat/proc
    /// restent visibles dans <see cref="ActiveEnchantIds"/> pour le
    /// dispatch de procs (`st.ProcEnchantIds`, voir
    /// ZoneEntryFlow.License.cs).</summary>
    public StatContribution ProvideStats(int charId)
    {
        var st = StateOf(charId);
        var c = new StatContribution();
        // Distinct() = FindEnchant natif : deux licences dont les
        // items pointent le même enchant ne le cumulent pas deux fois.
        foreach (var id in ActiveEnchantIds(charId).Distinct())
            ApplyEnchantCommands(c, _enchants.GetPassiveCommands(id));
        return c;
    }

    /// <summary>Ids d'enchant actifs (slots + combos) — miroir de
    /// `FindEnchant`/`+0xafc`/`+0xb14` pour l'évaluation de procs.</summary>
    public IEnumerable<int> ActiveEnchantIds(int charId)
    {
        var st = StateOf(charId);
        for (var s = 1; s <= 7; s++)
            if (st.SlotEnchant[s] != 0)
                yield return unchecked((ushort)st.SlotEnchant[s]);
        if (st.SetEnchant[0] != 0)
            yield return unchecked((ushort)st.SetEnchant[0]);
        if (st.SetEnchant[1] != 0)
            yield return unchecked((ushort)st.SetEnchant[1]);
    }

    /// <summary>Traduit les commandes d'enchant en contribution —
    /// switch par <b>id numérique</b> : le mapping var→commande est
    /// prouvé par objdump du dispatch
    /// `CSpellDBO::DecodeEffectCommand` @0x8380ad0 (FALLBACK obligé —
    /// la fonction est en timeout dans l'export) et la sémantique par
    /// les `CEC_*::Execute` correspondants.
    ///
    /// Modes natifs (« AttrX &lt;mode&gt; &lt;val&gt; ») :
    ///   mode 0 → valeur flat dans l'accumulateur ;
    ///   mode 1 → `ROUND(S_SpellReference[niveau du sort].réf × var
    ///             × 0.01)` — <paramref name="spellRef"/> est la ligne
    ///             du niveau du sort actif (player_enchants.level) ;
    ///   mode 2 → canal pourcentage.
    /// Public : partagé avec les stages « classrank », « titles »,
    /// « buffs » et « ride » (EffectToLife natif).</summary>
    public static void ApplyEnchantCommands(StatContribution c,
        IReadOnlyList<EnchantService.UseCommand> cmds,
        SpellReferenceParser.LevelCost? spellRef = null)
    {
        foreach (var cmd in cmds)
        {
            double V(int i) => i < cmd.Args.Length ? cmd.Args[i] : 0;
            // Mode 1 natif : ROUND(référence du palier × var × 0.01)
            // — la table est requise (erreur native « Spellreference
            // LV not found » → pas d'application) → 0 sans référence.
            int Scaled(Func<SpellReferenceParser.LevelCost, int> pick,
                int i)
                => spellRef.HasValue
                    ? (int)Math.Round(pick(spellRef.Value) * V(i) * 0.01,
                        MidpointRounding.AwayFromZero)
                    : 0;
            switch (cmd.CommandId)
            {
                case 1004: // HPPlusRate — % de soin/plafond (titres).
                    c.Add(StatIndex.Hp, pct: V(0)); break;
                case 2001: // AttrMaxHPPlus — plafond flat (pre).
                    c.Add(StatIndex.Hp, pre: V(0)); break;
                case 2002: // AttrMaxHPRate — % plafond HP.
                    c.Add(StatIndex.Hp, pct: V(0)); break;
                case 2003: // AttrMaxHP — flat post.
                    c.Add(StatIndex.Hp, post: V(0)); break;
                case 1014: case 2013: // MPPlusRate / AttrMaxMPRate
                    c.Add(StatIndex.Mp, pct: V(0)); break;
                case 2011: // AttrMaxMPPlus — flat (pre).
                    c.Add(StatIndex.Mp, pre: V(0)); break;
                case 2014: // AttrMaxMP — flat (post).
                    c.Add(StatIndex.Mp, post: V(0)); break;
                case 2040: // AttrCriticalRate 2 / AttrMagicCriticalRate 2
                    // — % multiplicatif du numérateur crit
                    // (kinddata+0x104/+0x110, natif stocke arg×0.01).
                    c.AddSecondary(critRatePct: V(0),
                        magicCritRatePct: V(1));
                    break;
                case 2043: // AttrCriticalDamage 0 / AttrMagicCriticalDamage 0
                    // — stats joueur directes (kinddata+0xfc/+0x108) ;
                    // le crit dmg magique PAR SORT est cmd 2099.
                    c.AddSecondary(critDamage: V(0),
                        magicCritDamage: V(1));
                    break;
                case 2048: // AttrCriticalRate 0 / AttrMagicCriticalRate 0
                    c.AddSecondary(critRating: V(0),
                        magicCritRating: V(1));
                    break;
                case 2051: // AttrMovement — % déplacement (float
                    // possible dans les données → tronqué, les valeurs
                    // réelles sont entières).
                    c.AddSecondary(moveSpeedPct: V(0)); break;
                case 2056: // AttrAttackSpeedPlus 0 — flat.
                case 2058: // AttrAttackSpeedPlus 1 — même accumulateur
                    // flat natif (CEC_AttrAttackSpeedPlus::Execute :
                    // mode 0 ET 1 → kinddata+0x124 += arg).
                    c.AddSecondary(attackSpeed: V(0)); break;
                case 2068: // AttrAttackSpeedPlus 2 — % attaque
                    // (kinddata+0x12c, natif arg×0.01).
                    c.AddSecondary(attackSpeedPct: V(0)); break;
                case 2069: // AttrCastTimePlus 2 — % cast
                    // (kinddata+0x13c, natif arg×0.01).
                    c.AddSecondary(castSpeedPct: V(0)); break;
                case 2081: // AttrBase Str/Dex/Int/Spr/Luk mode 1 —
                    // spellref cols 22/18/19/20/21 → post (+0x540..).
                    if (spellRef.HasValue)
                    {
                        c.Add(StatIndex.Str, post: Scaled(r => r.StrRef, 0));
                        c.Add(StatIndex.Agi, post: Scaled(r => r.DexRef, 1));
                        c.Add(StatIndex.Int, post: Scaled(r => r.IntRef, 2));
                        c.Add(StatIndex.Wis, post: Scaled(r => r.SprRef, 3));
                        c.Add(StatIndex.Lck, post: Scaled(r => r.LukRef, 4));
                    }
                    break;
                case 2082: // mode 1 : DEF→ref+0x58, Hit→+0x5c,
                    // Dodge→+0x5e, P-ATK→coef phys (+0x08),
                    // M-ATK→coef mag (+0x0c).
                    c.AddSecondary(
                        defence: Scaled(r => r.DefenceRef, 0),
                        hitRating: Scaled(r => r.HitRef, 1),
                        dodgeRating: Scaled(r => r.DodgeRef, 2));
                    if (spellRef.HasValue)
                        c.AddAttack(phys: Scaled(r => r.PhysicalCoef, 3),
                            magic: Scaled(r => r.MagicCoef, 4));
                    break;
                case 2083: // AttrPhysicoDamageRate / AttrDefenceRate /
                    // AttrMagicDamageRate — % (kinddata +0xe0/+0xf0/+0xe8).
                    c.AddSecondary(physAttackPct: V(0),
                        defencePct: V(1),
                        magicAttackPct: V(2));
                    break;
                case 2084: // mode 0 : P-ATK / DEF / M-ATK flats
                    // (+0xdc/+0xec/+0xe4).
                    c.AddAttack(phys: V(0), magic: V(2));
                    c.AddSecondary(defence: V(1));
                    break;
                case 2085: // AttrBase mode 2 → pct (titres « 全能力提高 »).
                    c.Add(StatIndex.Str, pct: V(0));
                    c.Add(StatIndex.Agi, pct: V(1));
                    c.Add(StatIndex.Int, pct: V(2));
                    c.Add(StatIndex.Wis, pct: V(3));
                    c.Add(StatIndex.Lck, pct: V(4));
                    break;
                case 2086: // AttrBase mode 0 → POST natif (+0x540.. —
                    // la valeur s'ajoute après le % classe, pas dedans).
                    c.Add(StatIndex.Str, post: V(0));
                    c.Add(StatIndex.Agi, post: V(1));
                    c.Add(StatIndex.Int, post: V(2));
                    c.Add(StatIndex.Wis, post: V(3));
                    c.Add(StatIndex.Lck, post: V(4));
                    break;
                case 2087: // AttrHitRate 0 / AttrDodgeRate 0 — flats.
                    c.AddSecondary(hitRating: V(0),
                        dodgeRating: V(1));
                    break;
                case 2095: // AttrResist <4+i> — résistances
                    // élémentaires 4-9 (kinddata+6..+0x10).
                    for (var i = 0; i < 6; i++)
                        if (V(i) != 0)
                            c.AddResist(4 + i, (int)V(i));
                    break;
                case 2101: // AttrResist <1+i> — résistances
                    // physiques 1-3 (kinddata+0..+4).
                    for (var i = 0; i < 3; i++)
                        if (V(i) != 0)
                            c.AddResist(1 + i, (int)V(i));
                    break;
                case 2117: // AttrBlock — % blocage (kinddata+0x154).
                    c.AddSecondary(blockPct: V(0)); break;
                case 2038: // HealPlus — désasm 0x8392e28 : arg0 →
                    // soins prodigués (mode 2), arg1 → soins reçus
                    // (mode 3).
                    c.AddSecondary(healGivenPct: V(0),
                        healTakenPct: V(1));
                    break;
                case 2047: // HealPlus modes 4/5 (CEC_HealPlus::Execute
                    // @0x08535480) : arg0 → soins prodigués FLAT
                    // (+0x144), arg1 → soins reçus FLAT (+0x14c) —
                    // même dispatch d'args que 2038 (KB-05).
                    c.AddSecondary(healGivenFlat: V(0),
                        healTakenFlat: V(1));
                    break;
                case 2044: // AttrCriticalDamage / AttrMagicCriticalDamage
                    // — dispatch natif arg0→« AttrCriticalDamage »,
                    // arg1→« AttrMagicCriticalDamage » (désasm
                    // 0x838e8d8) : même sémantique que 2043.
                    c.AddSecondary(critDamage: V(0),
                        magicCritDamage: V(1));
                    break;
                case 2052: // AttrWeaponAttackSpeedLevel <cat 0..5> —
                    // chaque arg remplit les accums d'une catégorie
                    // d'arme (CEC_AttrWeaponAttackSpeedLevel::Execute
                    // @0x085149d0 : kinddata+0x414..+0x434, signe
                    // NÉGATIF = attaque plus rapide — ex. certificat
                    // corsaire « VIT ATQ +8 % » = 2052|-8). Le pipeline
                    // n'a pas de canal par catégorie : rapproché au
                    // % global attackSpeed en signe inversé.
                    c.AddSecondary(attackSpeedPct:
                        -(V(0) + V(1) + V(2) + V(3) + V(4) + V(5)));
                    break;
                case 2053: // dispatch natif par arg : arg0→« AttrCastTime
                    // » (kinddata+0x138 — retranché du temps de cast dans
                    // GetBuffCastTimeRate/CCharFormula::CastTime : NÉGATIF
                    // = cast plus court — certificat « VIT INV +5 % » =
                    // 2053|-5), args1-4→« AttrWeaponCastSpeedRate » /
                    // « AttrWeaponAttackSpeedRate » (taux % par type
                    // d'arme — rapproché au % global).
                    c.AddSecondary(castSpeed: -V(0),
                        attackSpeedPct: V(1) + V(2) + V(3) + V(4));
                    break;
                case 2088: // AttrHitRateRate / AttrDodgeRateRate —
                    // strings natives des branches arg0/arg1 (désasm
                    // 0x838f1a2) : % multiplicatif hit/dodge — PAS
                    // « SpellAttack » (corrigé).
                    c.AddSecondary(hitRatePct: V(0),
                        dodgeRatePct: V(1));
                    break;
                case 2102: case 2103: case 2104:
                    // AttrWeaponDamageRate — chaque arg = un kind
                    // d'arme : kind = 0x11 + (cmd−2102)*6 + argIndex
                    // (CEC_AttrWeaponDamageRate::Execute @0x8518710,
                    // range check kind−0x11 < 0x11).
                    for (var i = 0; i < 6; i++)
                        if (V(i) != 0)
                            c.AddWeaponDamageRate(
                                (cmd.CommandId - 2102) * 6 + i, V(i));
                    break;
                case 2108: // AttrShieldBlock — % blocage au bouclier
                    // (rapproché au canal blocage).
                    c.AddSecondary(blockPct: V(0)); break;
                case 2109: // AttrWeaponParryRate — % parade (rapproché
                    // au canal parade).
                    c.AddSecondary(parry: V(0)); break;
                case 2015: // CostMPPlus mode 0 — coût MP flat (négatif
                    // = réduction), consommé au check MP du cast.
                    c.AddSecondary(mpCost: V(0)); break;
                case 2122: // AttrHateRate — % d'aggro générée.
                    c.AddSecondary(hateRatePct: V(0)); break;
                // ===== Effets combat/proc — sans canal stats : le
                // natif les évalue dans le dispatch d'événements ou au
                // calcul des dégâts, JAMAIS comme accumulateur passif.
                // Cas explicites pour documenter la décision par id. =====
                case 1031: case 2031: case 2032: // MultiStroke — coups
                    // extra roulés à l'attaque (GetAttackCommands +
                    // ProcSources), pas un stat de profil.
                case 2033: case 2034: case 2036: // DamageAddPlus(Plus) —
                    // modifie les dégâts par coup dans AffectDamage
                    // (@0x085203c0) : combat pur (ex. certificats
                    // « DÉG PHY reçus −N % » 52508/52678/56110/56111 —
                    // réduction par hit, non couverte par le pipeline).
                case 2055: // AttrWeaponCriticalLevel — blocs normalement
                    // transférés en col40 (proc stun « Étude du Sonar »,
                    // « Prisonnier de la Mort ») ; passif : niveau de
                    // crit par arme — pas de canal kinddata équivalent.
                case 2061: // AttrCastFailRate — procs « statut Trivial »
                    // (56112/56113, evt4) — combat pur.
                case 2145: // OnAttackBuffMagicCritical — hook runtime
                    // « quand crit magique → poser enchant » (46308/09) :
                    // enregistre un déclencheur, pas un accumulateur.
                case 2151: // OnAttackBuffPerry — hook « quand parade →
                    // poser enchant » (56114/56115 — récupération PV).
                case 2152: // OnBuffEndBuff — chaîne runtime de buffs.
                case 2035: case 2105: case 2112: case 2144: case 2146:
                case 2148: case 2149: // OnAttackBuff*/OnBeAttack* —
                    // hooks de déclenchement similaires, aucun accum.
                    break;
                // 2059/2069 cast-time déjà couverts, 2092
                // ResistDamage, 2155 AttrType (tag de condition —
                // pas un modificateur), 1823 EnchantKills (dissipation
                // proc), 29xx drop/XP, 3002/3012 régén/5s : pas de canal
                // pipeline aujourd'hui — ignorés fidèlement.
            }
        }
    }
}
