using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Moteur d'enchantements/procs — correspond a CEnchantMgr + CEC_Enchant*.
///
/// Evalue les enchants attaches a un equipement/buff quand un evenement de
/// combat survient (attaque, coup recu, critique...) et retourne les sorts
/// a declencher. Les regles proviennent de S_Enchant.ini
/// (~19.8k entrees) + S_ItemMallEnchant.ini, recouvertes par l'overlay
/// Reforged (c_enchant/c_itemmallenchant — table canonique du client
/// joué, cf. <see cref="ApplyClientOverlay"/>).
/// </summary>
public sealed class EnchantService
{
    private readonly Dictionary<int, EnchantParser.EnchantDef> _enchants;

    /// <summary>Map groupe → ids d'enchant —
    /// <c>ElfPetEnchantGroupMap</c> natif : clé = `enchant+0x94` =
    /// S_Enchant col 6 (datové : les enchants integrate pet y
    /// portent 6-11).</summary>
    private readonly Dictionary<int, int[]> _elfEnchantGroups;

    public EnchantService(IEnumerable<EnchantParser.EnchantDef> enchants)
    {
        var list = enchants.ToList();
        _enchants = list.ToDictionary(e => e.Id);
        _elfEnchantGroups = list
            .Where(e => e.Raw.Length > 6 &&
                int.TryParse(e.Raw[6], out var g) && g > 0)
            .GroupBy(e => int.Parse(e.Raw[6]))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToArray());
    }

    public static EnchantService LoadFromDataDir(string decodedDir)
    {
        var all = EnchantParser.Parse(
            Path.Combine(decodedDir, "S_Enchant.ini"));
        // S_ItemMallEnchant.ini : table enchant des items cash shop —
        // schéma identique, ids distincts (monture mall 44025 → 55850
        // = Ride 2134|253010). Sans elle l'enchant d'usage de ces
        // items est introuvable : la monture tombait dans le chemin
        // consommable et était MANGÉE au clic droit (bug live).
        var mall = Path.Combine(decodedDir, "S_ItemMallEnchant.ini");
        if (File.Exists(mall))
        {
            var known = all.Select(e => e.Id).ToHashSet();
            all.AddRange(EnchantParser.Parse(mall)
                .Where(e => !known.Contains(e.Id)));
        }
        all = ApplyClientOverlay(all, decodedDir);
        AddDevMoveSpeedEnchant(all);
        return new EnchantService(all);
    }

    /// <summary>Enchant synthétique « vitesse dev » : une seule
    /// commande 2051|pct (AttrMovement) — aucun S_Enchant natif n'est
    /// un 2051 pur à +200. Id/pct viennent de ServerConfig
    /// (DevMoveSpeedBuffId/Pct) ; sans effet si l'id existe déjà
    /// (config pointant un vrai enchant) ou vaut 0.</summary>
    public static void AddDevMoveSpeedEnchant(
        List<EnchantParser.EnchantDef> defs)
    {
        var id = ServerConfig.DevMoveSpeedBuffId;
        if (id <= 0 || defs.Any(d => d.Id == id)) return;
        var raw = new string[62];
        Array.Fill(raw, "");
        raw[0] = id.ToString();
        raw[1] = "E9999";
        raw[4] = "1";
        raw[5] = "DevMoveSpeed";
        raw[7] = "1";
        raw[9] = "2051";
        raw[10] = ServerConfig.DevMoveSpeedBuffPct.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        defs.Add(new EnchantParser.EnchantDef
        {
            Id = id, GroupCode = raw[1], Level = 1, Name = raw[5],
            Enabled = true, Raw = raw,
        });
    }

    /// <summary>Overlay « client Reforged » de la table enchant —
    /// politique canonique (lot 12) : le client joué affiche les
    /// tooltips générés depuis SA table c_enchant ; les blocs de
    /// commandes (cols 9+7k) y ont été rééquilibrés (2056 lignes
    /// divergentes sur 2437 — ex. talent Sacred Duty 54813 :
    /// S_=<c>2122|-3</c> aggro vs c_=<c>2083|||0.5</c> = +0.5 %
    /// M-ATK/pt). Appliquer S_ tout en affichant c_ rendait les
    /// talents « sans stats » : la ligne Reforged <b>remplace</b> la
    /// ligne serveur de même id, et les ids nouveaux (1094)
    /// s'ajoutent. Schéma FR `|V.2|60|` = S_ `|V.3|61|` SANS la colonne
    /// elf-pet (index 6) : `EnchantParser.NormalizeRow` la réinsère
    /// (KB-01) pour que les index S_ restent valides. `c_itemmallenchant.ini` est traité de même.
    /// Désactivation : EDEN_REFORGED_DB=off.
    /// Retourne la liste remplacée (même instance mutée + ids
    /// ajoutés).</summary>
    public static List<EnchantParser.EnchantDef> ApplyClientOverlay(
        List<EnchantParser.EnchantDef> serverDefs, string decodedDir)
    {
        var dbDir = Data.ReforgedDataLocator.FindDbDir(
            decodedDir, AppContext.BaseDirectory);
        if (dbDir == null) return serverDefs;
        var byId = new Dictionary<int, int>(serverDefs.Count);
        for (var i = 0; i < serverDefs.Count; i++)
            byId[serverDefs[i].Id] = i;
        var replaced = 0; var added = 0; var files = 0;
        foreach (var name in new[] { "c_enchant.ini",
                     "c_itemmallenchant.ini" })
        {
            var path = Data.ReforgedDataLocator.FindFile(name,
                decodedDir, AppContext.BaseDirectory);
            if (path == null) continue;
            List<EnchantParser.EnchantDef> client;
            try { client = EnchantParser.Parse(path); }
            catch (Exception ex)
            {
                Eden.Core.Logging.EdenLog.Write(
                    $"[Enchant] overlay {path} ignoré : {ex.Message}");
                continue;
            }
            files++;
            foreach (var def in client)
            {
                if (byId.TryGetValue(def.Id, out var idx))
                { serverDefs[idx] = def; replaced++; }
                else { serverDefs.Add(def); byId[def.Id] =
                    serverDefs.Count - 1; added++; }
            }
        }
        if (files > 0)
            Eden.Core.Logging.EdenLog.Write(
                $"[Enchant] overlay client ({dbDir}) : " +
                $"{replaced} remplacés, {added} ajoutés " +
                $"sur {serverDefs.Count}");
        return serverDefs;
    }

    /// <summary>Evenement de combat declenchant l'evaluation d'un enchant.</summary>
    public enum TriggerEvent
    {
        OnAttack,
        OnBeAttacked,
        OnCritical,
        OnKill,
        OnCast,
        Generic // trigger type 8208 etc. — condition interne
    }

    public sealed record ProcResult(int EnchantId, int SpellId, int Chance);

    /// <summary>
    /// Evalue un enchant pour un evenement donne.
    /// Retourne les sorts a appliquer si le jet de chance reussit.
    /// </summary>
    public IReadOnlyList<ProcResult> Evaluate(
        int enchantId, TriggerEvent evt, int stacks = 0)
    {
        if (!_enchants.TryGetValue(enchantId, out var e) || !e.Enabled)
            return Array.Empty<ProcResult>();

        var results = new List<ProcResult>();

        // Sort applique principal — sa propre chance est AppliedChance (col 39)
        if (e.AppliedSpellId > 0 && Roll(e.AppliedChance))
            results.Add(new ProcResult(e.Id, e.AppliedSpellId, e.AppliedChance));

        // Sorts secondaires (cols 23/24)
        if (e.SpellId1 > 0 && Roll(e.ProcChance))
            results.Add(new ProcResult(e.Id, e.SpellId1, e.ProcChance));
        if (e.SpellId2 > 0 && Roll(e.ProcChance))
            results.Add(new ProcResult(e.Id, e.SpellId2, e.ProcChance));

        return results;
    }

    public EnchantParser.EnchantDef? Get(int enchantId)
        => _enchants.TryGetValue(enchantId, out var e) ? e : null;

    /// <summary>Infos de monture d'un enchant — commande 2134
    /// (CEC_Ride, `rideKey|flag|seats`), 2140 (CEC_RideSpell — buffs de
    /// monture de combat, jusqu'à 6 ids), 2051 (bonus vitesse %).
    /// Preuve : désasm CheckRideSpellBuff @0x81023f0 (*piVar2==0x85c
    /// =2140), enchant 58991 «Flame Soul Puma» = `2134|14010|1` +
    /// `2051|40`. `CEC_Ride::Init` @0x854f960 : arg1 = rideKey
    /// (this+8), arg2 = flag (this+0xc, &1 → bit rejeté), arg3 =
    /// sièges (this+0xd, min 1), arg4 = type ride (this+0xe →
    /// char+0x824 — ≠0 = monture de combat : le natif garde le ride
    /// quand le perso attaque ; =0 → `RemoveEnchantByState(9)` à
    /// l'engage, désasm @0x84f08c3). La commande est détectée par
    /// scan des champs Raw — les codes et les params sont
    /// entremêlés dans S_Enchant.</summary>
    public sealed record RideInfo(int RideKey, int Flag, int Seats,
        int SpeedPct, int RideType, IReadOnlyList<int> CombatBuffs);

    /// <summary>Retourne les infos de ride si l'enchant porte la
    /// commande 2134 — le clic droit sur l'item monture toggle le
    /// ride au lieu de consommer l'item.</summary>
    public RideInfo? GetRideInfo(int enchantId)
    {
        var e = Get(enchantId);
        if (e == null) return null;
        int rideKey = 0, flag = 0, seats = 0, speed = 0, type = 0;
        var buffs = new List<int>();
        var raw = e.Raw;
        for (var i = 0; i < raw.Length; i++)
        {
            if (!int.TryParse(raw[i], out var code)) continue;
            switch (code)
            {
                case 2134:
                    if (i + 1 < raw.Length &&
                        int.TryParse(raw[i + 1], out var k)) rideKey = k;
                    // arg2 = flag (&1 → char+0x4ad bit1, sérialisé dans
                    // le 283), arg3 = sièges (planché à 1 natif).
                    if (i + 2 < raw.Length &&
                        int.TryParse(raw[i + 2], out var fl)) flag = fl;
                    if (i + 3 < raw.Length &&
                        int.TryParse(raw[i + 3], out var st)) seats = st;
                    // arg4 = type (char+0x824) : montures de combat
                    // « 戰鬥座騎 » portent 1 ou 2, montures simples 0.
                    if (i + 4 < raw.Length &&
                        int.TryParse(raw[i + 4], out var ty)) type = ty;
                    break;
                case 2051 when i + 1 < raw.Length &&
                    int.TryParse(raw[i + 1], out var sp):
                    speed = sp;
                    break;
                case 2140:
                    for (var j = i + 1;
                        j < raw.Length && buffs.Count < 6 &&
                        int.TryParse(raw[j], out var b); j++)
                        buffs.Add(b);
                    break;
            }
        }
        return rideKey != 0
            ? new RideInfo(rideKey, flag, Math.Max(seats, 1), speed,
                type, buffs)
            : null;
    }

    /// <summary>L'enchant est-il annulable par clic droit client
    /// (NCID 85) — gardes de `CEffectFactory::CancelPlayerEnchant`
    /// @0x805fe00 : `SEffectData+8` (S_Enchant col 7) bit0 = « buff »
    /// requis, `+0xc` (col 8) bit 0x10 = « can't abandon » refusé.
    /// Datové : 52105 (buff clerc) c7=1 annulable ; 52026 (debuff
    /// DEF-200) c7=2 → non annulable ; montures 58991/55850 c7=1.</summary>
    public bool CanCancelByClient(int enchantId)
    {
        var e = Get(enchantId);
        if (e == null || e.Raw.Length <= 8) return false;
        if (!int.TryParse(e.Raw[7], out var flag1) || (flag1 & 1) == 0)
            return false;
        if (int.TryParse(e.Raw[8], out var flag2) && (flag2 & 0x10) != 0)
            return false;
        return true;
    }

    /// <summary>Enchant « debuff » — S_Enchant col7 (u16 →
    /// SEffectData+8) bit1. Filtre de type de
    /// <c>lapis::CCheckBufType::operator()</c> @0x0852f2e0 utilisé par
    /// EnchantKillRandom(Force) : type 2 ne retient que les enchants
    /// à bit1 posé, tout autre type ceux à bit1 clair.</summary>
    public bool IsDebuff(int enchantId)
    {
        var e = Get(enchantId);
        return e != null && e.Raw.Length > 7 &&
            int.TryParse(e.Raw[7], out var f) && (f & 2) != 0;
    }

    /// <summary>Enchant non dissipable — S_Enchant col8 (u16 →
    /// SEffectData+0xc) bit 0x10 : exclu des candidats de
    /// <c>CEC_EnchantKillRandom::Execute</c> @0x0852ea60 (la variante
    /// Force @0x0852eed0 ne teste pas ce bit).</summary>
    public bool IsUndispellable(int enchantId)
    {
        var e = Get(enchantId);
        return e != null && e.Raw.Length > 8 &&
            int.TryParse(e.Raw[8], out var f) && (f & 0x10) != 0;
    }
    /// <summary>« Type » de l'enchant — S_Enchant col38 (0-based) →
    /// SEffectData+0x112 natif : clé de `CEnchantMgr::FindByType`
    /// @0x8563140. Datové : les familles 163/165 = changements de
    /// taille (49803/49804 « Enlarge/Shrink »), 770 = buffs
    /// d'« Ancient Mage » (51559-51561) — les EnchantCancel de quête
    /// les ciblent par type (51323 `1822|770|770|1`).</summary>
    public int GetTypeId(int enchantId)
    {
        var e = Get(enchantId);
        return e != null && e.Raw.Length > 38 &&
            int.TryParse(e.Raw[38], out var t) ? t : 0;
    }

    /// <summary>« Grade » de l'enchant — S_Enchant col39 (0-based) →
    /// SEffectData+0x114 : filtre `grade ≤ flag` de
    /// <c>ILifeEntity::RemoveEnchantByType</c> @0x847b380 (datové :
    /// 49803/51559 c39=1 annulés par flag 1-2 de 49854/51323).</summary>
    public int GetGrade(int enchantId)
    {
        var e = Get(enchantId);
        return e != null && e.Raw.Length > 39 &&
            int.TryParse(e.Raw[39], out var g) ? g : 0;
    }

    /// <summary>FindByType natif de <c>CEnchantMgr::Add</c>
    /// @0x08569a50 : parmi les enchants actifs de MÊME type (col38),
    /// un grade supérieur — ou égal avec niveau supérieur — fait
    /// REFUSER le nouvel enchant ; sinon les existants plus faibles
    /// sont remplacés (300 + retrait). `active` = paires
    /// (effectId → niveau courant) ; type 0 = donnée absente, jamais
    /// de conflit. Retourne false = rejet.</summary>
    public bool TypeAddAllowed(int newId, int newLevel,
        IEnumerable<KeyValuePair<int, int>> active,
        out List<int> replaced)
    {
        replaced = new List<int>();
        var type = GetTypeId(newId);
        if (type == 0) return true;
        var grade = GetGrade(newId);
        foreach (var kv in active)
        {
            if (kv.Key == newId || GetTypeId(kv.Key) != type) continue;
            var g = GetGrade(kv.Key);
            if (g > grade || (g == grade && kv.Value > newLevel))
                return false;
            replaced.Add(kv.Key);
        }
        return true;
    }

    /// <summary>L'enchant refuse tout re-add tant qu'il est actif —
    /// S_Enchant col8 (u16 → SEffectData+0xd) bit 0x100 : dans
    /// <c>CEnchantMgr::Add</c> @0x08569a50 un recast d'un enchant
    /// flaggé retourne NULL (ni refresh ni 299 — ignoré
    /// silencieusement).</summary>
    public bool BlocksReapply(int enchantId)
    {
        var e = Get(enchantId);
        return e != null && e.Raw.Length > 8 &&
            int.TryParse(e.Raw[8], out var f) && (f & 0x100) != 0;
    }

    /// <summary>Nombre maximal de cumuls de l'enchant — S_Enchant
    /// col59 → SEffectData+0x12e (défaut natif 1 quand la colonne est
    /// vide/0 — <c>CSpellDBO::LoadAllEffectData</c> @0x08394b10). Un
    /// recast incrémente le compteur CEnchant+0x30 jusqu'à ce plafond
    /// (et les stats cumulent) ; sous le plafond le recast = refresh
    /// de durée sans cumul.</summary>
    public int GetMaxStack(int enchantId)
    {
        var e = Get(enchantId);
        if (e == null || e.Raw.Length <= 59) return 1;
        return int.TryParse(e.Raw[59], out var n) && n > 0 ? n : 1;
    }

    /// <summary>Groupes d'enchants elf — clé de
    /// l'<c>ElfPetEnchantGroupMap</c> natif
    /// (<c>CGlobalInfo::InitElfPetSpellGroupMap</c> @0x80db390 :
    /// itère la query enchant — vtable IGameData+0x80 =
    /// <c>CGameData::GetAllEnchants</c> — et groupe par
    /// `enchant+0x94` = S_Enchant col 6). Les livres « 祈禱書 »
    /// (commandes 6036/6038 ChangePetIntegrateSpell(+)) stockent des
    /// <b>ids d'enchant</b> dans integrate_spell_* — ex. groupes 6-11
    /// = 祈願 I-III (61507-61536+).</summary>
    /// <returns>Ids d'enchant du groupe (vide si absent).</returns>
    public IReadOnlyList<int> GetElfEnchantGroup(int group)
        => _elfEnchantGroups.TryGetValue(group, out var ids)
            ? ids : Array.Empty<int>();

    /// <summary>Bloc de commande d'usage : id numérique + 6 variables.
    /// Preuve : objdump de `CSpellDBO::DecodeEffectCommand` @0x8380ad0
    /// (fonction en timeout dans l'export Ghidra) — la table de
    /// correspondance numéro→nom est extraite en lisant le premier
    /// `oss << "Nom"` de chaque branche de dispatch ; en-tête natif de
    /// Enchant.ini : 指令N編號 suivi de 6×指令N變數. Les vars sont des
    /// doubles natifs — conservés en double (les données réelles ont
    /// des fractionnels : « 0.5 », « 1.5 », « -2.5 » — les tronquer
    /// annulait des bonus de talents).</summary>
    public sealed record UseCommand(int CommandId, double[] Args);

    /// <summary>Lit les commandes d'un enchant d'usage (S_Enchant :
    /// blocs de 7 colonnes — id en col 9+7k puis 6 vars en
    /// cols 10+7k..15+7k). Un slot vide/non numérique est sauté
    /// (continue) et non fatal : le natif itère la carte complète —
    /// des enchants réels ont un slot de commande vide au milieu et
    /// un slot rempli ensuite.
    /// Cmd 160 : la donnée embarque une commande IMBRIQUÉE —
    /// `160|sousId|a1..a5` se relit comme `sousId` avec les 5 args
    /// restants (observé : `160|2085|…`, `160|2066|…`, `160|2150|…`).
    /// Ids décodés utiles : 1001=HP min max, 1011=MP min max,
    /// 1004/3024=HPPlusRate (%), 1014/3025=MPPlusRate (%),
    /// 1805=ItemTransportArea nodeId areaKey, 1921=EffectNextID
    /// enchId chance, 1822=EnchantCancel, 2134=Ride, 2140/1122=
    /// RideSpell, 2051=AttrMovement (%), 9022=OpenShop.</summary>
    public IReadOnlyList<UseCommand> GetUseCommands(int enchantId)
    {
        var e = Get(enchantId);
        if (e == null) return Array.Empty<UseCommand>();
        var raw = e.Raw;
        // 4 blocs natifs EXACTS (CEnchantQuery::Deserialize @0x85ecf00
        // : reserve(4) + 4×(u32 cmd + 6 double) = cols 9-36). Un 5ᵉ
        // bloc lirait les champs déclencheur cols 37-43 (chance/event/
        // durée) comme une fausse commande.
        var list = new List<UseCommand>(4);
        for (var k = 0; k < 4; k++)
        {
            var at = 9 + k * 7;
            if (at >= raw.Length ||
                !int.TryParse(raw[at], out var cmd) || cmd <= 0)
                continue;
            var args = new double[6];
            for (var i = 0; i < 6 && at + 1 + i < raw.Length; i++)
                if (double.TryParse(raw[at + 1 + i],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var dv))
                    args[i] = dv;
            // Cmd 160 = wrapper de commande imbriquée : var0 = le vrai
            // id de commande, vars1-5 = ses arguments.
            if (cmd == 160 && args[0] > 0)
                list.Add(new UseCommand((int)args[0],
                    args.Skip(1).ToArray()));
            else
                list.Add(new UseCommand(cmd, args));
        }
        return list;
    }

    private static bool Roll(int chance)
        => chance >= 100 || (chance > 0 && Random.Shared.Next(100) < chance);

    /// <summary>Commandes MultiStroke « passives » de l'enchant —
    /// blocs NON listés dans les slots déclenchés col40 (ceux-ci
    /// vivent via <see cref="GetProc"/>), filtrés aux ids
    /// {1031, 2031, 2032}. Datové : ~78 % des lignes 2031 ont
    /// col40/41 vides — ce sont les « +N % Double/Triple Hit » des
    /// talents, buffs et certificats ; chaque commande est un roll
    /// indépendant à l'attaque (spéc plan 2.4.2). Args =
    /// `[chance1, facteur1, chance2, facteur2]` à arg0..arg3 —
    /// corrélé 1:1 aux tooltips T_Enchant (2031|15|50 = « +15 % »,
    /// 2031|3|50|100|30 = « 3 % triple ») ; le champ `+0x10` lu par
    /// `CEC_MultiStroke::Init` @0x8545f20 sert de masque de hit-type
    /// natif mais les données réelles y portent la chance.</summary>
    public IReadOnlyList<UseCommand> GetAttackCommands(int enchantId)
    {
        var e = Get(enchantId);
        if (e == null) return Array.Empty<UseCommand>();
        // Slots déclenchés col40 (indices 1-based) — exclus du scan
        // passif : ils votent déjà via GetProc.
        HashSet<int>? triggered = null;
        if (!string.IsNullOrWhiteSpace(e.TriggerSlotsRaw))
            foreach (var tok in e.TriggerSlotsRaw.Split(';',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
                if (int.TryParse(tok, out var s))
                    (triggered ??= new HashSet<int>()).Add(s);
        var list = new List<UseCommand>(2);
        var raw = e.Raw;
        for (var k = 0; k < 4; k++)
        {
            if (triggered != null && triggered.Contains(k + 1))
                continue;
            var at = 9 + k * 7;
            if (at >= raw.Length ||
                !int.TryParse(raw[at], out var cmd) || cmd <= 0)
                continue;
            if (cmd is not (1031 or 2031 or 2032)) continue;
            var args = new double[6];
            for (var i = 0; i < 6 && at + 1 + i < raw.Length; i++)
                if (double.TryParse(raw[at + 1 + i],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var dv))
                    args[i] = dv;
            list.Add(new UseCommand(cmd, args));
        }
        return list;
    }

    /// <summary>Définition d'un proc d'équipement — les champs
    /// déclencheur de S_Enchant cols 40-43 (CEnchantData+0x34 set /
    /// +0x4c event / +0x50 chance / +0x54 durée natifs, dispatché par
    /// <c>CSpellDBO::LoadAllEffectData</c> @0x8394b10 dans le vecteur
    /// de commandes de l'événement : +0x44 attaque (evt 1/2),
    /// +0x5c coup reçu (3/4), +0x8c kill (6/7), +0xec cible (5),
    /// +0x98 être-tué (8/9)).</summary>
    public sealed record ProcDef(int EnchantId, int Event, int Chance,
        int DurationSec, IReadOnlyList<UseCommand> Commands)
    {
        /// <summary>Événement « attaque » natif (AttackToSelf 1 /
        /// AttackToOpponent 2 — OnAttackEnchant).</summary>
        public bool IsAttackEvent => Event is 1 or 2;
        /// <summary>Événement « coup reçu » (HurtToSelf 3 /
        /// HurtToOpponent 4 — OnBeAttackEnchant/OnHurtEnchant).</summary>
        public bool IsHurtEvent => Event is 3 or 4;
        /// <summary>Événement « cible tuée » (KillToSelf 6 /
        /// KillToOpponent 7 — OnKillEnchant).</summary>
        public bool IsKillEvent => Event is 6 or 7;
        /// <summary>Événement « porteur tué » (BeKillToSelf 8 /
        /// BeKillToOpponent 9 — OnBeKillEnchant).</summary>
        public bool IsBeKillEvent => Event is 8 or 9;
        /// <summary>Cible native du sous-effet — noms exacts du switch
        /// `LoadAllEffectData` @0x8394b10 : 1=AttackToSelf,
        /// 2=AttackToOpponent, 3=HurtToSelf, 4=HurtToOpponent,
        /// 5=TargetToSelf, 6=KillToSelf, 7=KillToOpponent,
        /// 8=BeKillToSelf, 9=BeKillToOpponent. Le bit0 de
        /// `SEffectData+0x118` (posé pour les cas 1/4/5/6/9) fait
        /// basculer l'entité primaire des commandes dans
        /// `CEnchant::On*` — la convention « self = porteur » reste
        /// constante : ToSelf = {1,3,5,6,8}, ToOpponent = {2,4,7,9}.
        /// (Un `Event & 1` pariait self sur 7/9 et opponent sur 6/8 :
        /// les buffs kill-procs n'étaient jamais appliqués — bug
        /// live 47977 event6 `2048|360`.)</summary>
        public bool TargetsSelf => Event is 1 or 3 or 5 or 6 or 8;
    }

    /// <summary>Retourne la définition de proc si l'enchant a un
    /// déclencheur (event col41 ≠ 0 et au moins un slot déclenché en
    /// col40), null sinon. Les commandes retournées sont UNIQUEMENT
    /// celles des slots listés en col40 — le natif les sort du
    /// vecteur passif vers le set de déclenchement
    /// (CEnchantQuery::Deserialize @0x85ecf00 : `*slotPtr = 0` après
    /// insertion dans le set).</summary>
    public ProcDef? GetProc(int enchantId)
    {
        var e = Get(enchantId);
        if (e == null || e.TriggerEvent == 0 ||
            string.IsNullOrWhiteSpace(e.TriggerSlotsRaw))
            return null;
        var slots = new List<UseCommand>(2);
        foreach (var tok in e.TriggerSlotsRaw.Split(';',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            // Indices 1-based dans les 4 blocs cmd+6 vars (9+7k) —
            // le natif n'en lit que 4 (Deserialize : 4 × 7 colonnes).
            if (!int.TryParse(tok, out var slot) ||
                slot < 1 || slot > 4) continue;
            var at = 9 + (slot - 1) * 7;
            var raw = e.Raw;
            if (at >= raw.Length ||
                !int.TryParse(raw[at], out var cmd) || cmd <= 0)
                continue;
            var args = new double[6];
            for (var i = 0; i < 6 && at + 1 + i < raw.Length; i++)
                if (double.TryParse(raw[at + 1 + i],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var dv))
                    args[i] = dv;
            slots.Add(new UseCommand(cmd, args));
        }
        return slots.Count == 0 ? null
            : new ProcDef(enchantId, e.TriggerEvent, e.TriggerChance,
                e.TriggerDuration, slots);
    }
}
