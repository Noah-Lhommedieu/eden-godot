using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Partial <see cref="ZoneEntryFlow"/> — apprentissage des sorts de
/// familier (livres de sorts pet, items UseType 6 / kind 44).
///
/// Portage du pipeline natif :
/// <list type="number">
/// <item><c>UseItemToElfControl::CheckExecute</c> @0x84ec120 — la loc
/// elf arrive dans <c>this+0x30</c> (param_7 du paquet 19 = field8) ;
/// slot valide dans le vecteur elf char+0x8ac puis
/// <c>CEffectFactory::CheckEffectToElf</c>.</item>
/// <item><c>CEC_ChangePetBattleSpell::CanExecute</c> @0x851b120 /
/// <c>CEC_ChangePetBattleSpellPlus::CanExecute</c> @0x851ae70 —
/// mix_star ≥ needStar (texte 0x18dd=6365), pet non invoqué
/// (elf+0x58 == NULL → texte 0x1831=6193), et pour les livres
/// directs (mode 0) refus si le sort est déjà appris
/// (texte 0x1830=6192, arg «%s» = nom de l'elf).</item>
/// <item><c>Execute</c> @0x851b060 / @0x851afb0 →
/// <c>CKFElf::LearnSpell</c> @0x8455980 (vecteur battle elf+0x40 si
/// sélecteur 0, intégré elf+0x4c sinon ; mode 0 = affectation directe,
/// sinon tirage uniforme dans [min,max] hors sorts déjà appris) et
/// <c>CKFElf::LearnSpellFromGroup</c> @0x8455ec0 (choix d'un des 3
/// groupes par <c>random()%3</c>, puis tirage dans le vecteur du
/// groupe — ElfPetSpellGroupMap pour la bataille, clé S_Spell col34
/// famille 8 ; ElfPetEnchantGroupMap pour l'intégré, clé S_Enchant
/// col6 — hors sorts déjà appris).</item>
/// <item>LearnSpell émet le DBA « Update Elf Spell » puis le 588
/// <c>CNC_CZ_ClientUpdateElfSpell</c> [loc][n][battle][n][integrate]
/// (Serialize : vecteurs s16 à taille variable).</item>
/// </list>
///
/// Les 6 variables de la commande enchant (S_Enchant blocs col 9+7k) :
/// 6035/6036 = [slot 1-5, mode, spell, min, max, needStar] ;
/// 6037/6038 = [slot 1-5, groupe1, groupe2, groupe3, needStar].
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Commandes d'enchant des livres de sorts pet —
    /// <c>CEffectFactory::DecodeEffectCommand</c> natif.</summary>
    private const int ElfCmdBattleSpell = 6035;       // ChangePetBattleSpell
    private const int ElfCmdIntegrateSpell = 6036;    // ChangePetIntegrateSpell
    private const int ElfCmdBattleSpellPlus = 6037;   // ChangePetBattleSpellPlus
    private const int ElfCmdIntegrateSpellPlus = 6038;// ChangePetIntegrateSpellPlus

    /// <summary>Point d'entrée livre de sorts — routé sur la boucle
    /// par le callback <c>ZoneServices.ElfBookApply</c> (le handler
    /// ItemUse stateless y délègue). Rejoue CheckExecute +
    /// CheckEffectToElf (phase de validation de toutes les commandes)
    /// puis EffectToElf (exécution). Retourne vrai si le livre peut
    /// être consommé — un refus natif ne consomme pas l'item.</summary>
    private async Task<bool> ApplyElfSpellBookAsync(NetSession session,
        EntryState st, int enchantId, int elfLoc, CancellationToken token)
    {
        await EnsureElfsLoadedAsync(session, st, token);
        // CheckExecute @0x84ec120 : index < taille du vecteur elf et
        // slot non nul. Notre modèle = dict par loc (0-9).
        if (elfLoc < 0 ||
            !st.ElfSlots.TryGetValue((short)elfLoc, out var elf))
        {
            return false;
        }
        var cmds = _services.Enchants.GetUseCommands(enchantId);
        if (cmds.Count == 0) return false;

        // Phase CheckEffectToElf : TOUTES les commandes validées avant
        // la moindre mutation (early-return natif sur le premier
        // échec).
        foreach (var c in cmds)
        {
            if (!await CheckElfSpellCommandAsync(session, st, elf, c,
                    token))
            {
                return false;
            }
        }
        // Phase EffectToElf : exécution commande par commande.
        foreach (var c in cmds)
        {
            if (!await ExecElfSpellCommandAsync(session, st, elf, c,
                    token))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Validation d'une commande de livre — port de
    /// <c>CEC_ChangePet*Spell(*)::CanExecute</c>. Émet le texte
    /// système natif correspondant au refus.</summary>
    private async Task<bool> CheckElfSpellCommandAsync(NetSession session,
        EntryState st, ElfSlot elf,
        Eden.Core.Game.Services.EnchantService.UseCommand c,
        CancellationToken token)
    {
        var (isPlus, selector, needStar, mode, spell, _) =
            DecodeElfBookArgs(c);
        // elf+0x38 (mix_star) < this+0x12 → 0x18dd (Plus : this+0x10).
        if (elf.MixStar < needStar)
        {
            await SendTextIndexAsync(session, 0x18dd, token);
            return false;
        }
        // elf+0x58 (status) non nul → 0x1831 « le pet doit être au
        // repos » — apprentissage bloqué en mix (1) comme invoqué (2).
        if (elf.Status != 0 || st.ElfPetHandle != 0 &&
            _services.Live.GetMob(st.ElfPetHandle)?.ElfLoc == elf.Loc)
        {
            await SendTextIndexAsync(session, 0x1831, token);
            return false;
        }
        // Livre direct (mode 0) : sort déjà appris → 0x1830 «%s».
        // (Plus : pas de check — le tirage évite les doublons ; mode
        // != 0 : idem, le tirage garantit la non-duplication.)
        if (!isPlus && mode == 0 &&
            SpellVector(elf, selector).Contains(spell))
        {
            await SendTextIndexArgsAsync(session, 0x1830,
                string.IsNullOrEmpty(elf.GivenName)
                    ? elf.Def?.Name ?? ""
                    : elf.GivenName, token);
            return false;
        }
        return true;
    }

    /// <summary>Exécution d'une commande — <c>Execute</c> →
    /// <c>CKFElf::LearnSpell</c> / <c>LearnSpellFromGroup</c>, puis
    /// persistance « Update Elf Spell » + 588.</summary>
    private async Task<bool> ExecElfSpellCommandAsync(NetSession session,
        EntryState st, ElfSlot elf,
        Eden.Core.Game.Services.EnchantService.UseCommand c,
        CancellationToken token)
    {
        var (isPlus, selector, _, mode, spell, bounds) =
            DecodeElfBookArgs(c);
        var vec = SpellVector(elf, selector);
        var slot = (int)c.Args[0] - 1;                // slot 1-5 → 0-4
        if ((uint)slot >= (uint)vec.Length) return false;

        int learned;
        if (isPlus)
        {
            // LearnSpellFromGroup : random()%3 choisit le groupe, puis
            // tirage dans le vecteur du groupe hors doublons.
            var group = (int)c.Args[1 + Random.Shared.Next(3)];
            var pool = selector == 0
                ? _services.GameData.Spells
                    .Where(s => s.Family == 8 && s.ElfGroup == group)
                    .Select(s => s.Id)
                : _services.Enchants.GetElfEnchantGroup(group);
            var candidates = pool.Where(p => !vec.Contains(p)).ToList();
            if (candidates.Count == 0) return false;
            learned = candidates[Random.Shared.Next(candidates.Count)];
        }
        else if (mode == 0)
        {
            learned = spell;                          // affectation directe
        }
        else
        {
            // LearnSpell : tirage uniforme dans [min,max] hors vec
            // (natif : re-pick dans toute la plage jusqu'à non-doublon
            // — équivalent à un tirage uniforme sur les candidats).
            var candidates = Enumerable
                .Range(bounds.Min, bounds.Max - bounds.Min + 1)
                .Where(v => !vec.Contains(v)).ToList();
            if (candidates.Count == 0) return false;
            learned = candidates[Random.Shared.Next(candidates.Count)];
        }
        vec[slot] = learned;

        // DBA natif « Update Elf Spell » + 588 ClientUpdateElfSpell.
        if (_services.ElfRepo != null)
        {
            var eid = elf.ElfId;
            var battle = elf.Battle.ToArray();
            var integrate = elf.Integrate.ToArray();
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.ElfRepo!.UpdateElfSpellsAsync(
                    eid, battle, integrate));
        }
        await session.SendPacketAsync(588,
            Codecs.NativeElfCodec.BuildUpdateElfSpell(
                elf.Loc, elf.Battle, elf.Integrate), token);
        return true;
    }

    /// <summary>Décode les variables de la commande en intent
    /// (positionnels natifs this+8..+0x12).</summary>
    /// <returns>(isPlus, sélecteur vecteur 0=battle/1=intégré,
    /// needStar, mode, spell, bornes min/max).</returns>
    private static (bool IsPlus, int Selector, short NeedStar, int Mode,
        int Spell, (int Min, int Max) Bounds) DecodeElfBookArgs(
            Eden.Core.Game.Services.EnchantService.UseCommand c)
    {
        // Args[0] = slot 1-5 — géré par l'appelant.
        var a = c.Args;
        int Arg(int i) => i < a.Length ? (int)a[i] : 0;
        return c.CommandId switch
        {
            ElfCmdBattleSpell =>
                (false, 0, (short)Arg(5), Arg(1), Arg(2),
                    (Arg(3), Arg(4))),
            ElfCmdIntegrateSpell =>
                (false, 1, (short)Arg(5), Arg(1), Arg(2),
                    (Arg(3), Arg(4))),
            ElfCmdBattleSpellPlus =>
                (true, 0, (short)Arg(4), 0, 0, (0, 0)),
            ElfCmdIntegrateSpellPlus =>
                (true, 1, (short)Arg(4), 0, 0, (0, 0)),
            _ => (false, 0, (short)0, 0, 0, (0, 0)),
        };
    }

    /// <summary>Vecteur de sorts de l'elf — battle (+0x40) si
    /// sélecteur 0, intégré (+0x4c) sinon.</summary>
    private static int[] SpellVector(ElfSlot elf, int selector)
        => selector == 0 ? elf.Battle : elf.Integrate;
}
