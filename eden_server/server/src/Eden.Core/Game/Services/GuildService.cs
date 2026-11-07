using Eden.Core.Database.Repositories;
using Eden.Core.Logging;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de guildes/familles — correspond a CFamily / CFamilyDBO.
/// Creation, membres, rangs, niveau de guilde, coffre de guilde,
/// taxes (FamilyLevyTaxes).
/// </summary>
public sealed class GuildService
{
    public sealed class Guild
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int LeaderCharacterId { get; set; }
        public int Level { get; set; } = 1;
        public long Experience { get; set; }
        public long Treasury { get; set; }      // gold de guilde
        /// <summary>Id d'emblème (`guilds.emblem`, -1 = aucun —
        /// transmis au client dans ClientChangeFamily s16).</summary>
        public int Emblem { get; set; } = -1;
        /// <summary>Node du hall de famille — <c>family.house_open</c>
        /// natif (0 = pas de hall, 501/502 = node d'instance ;
        /// <c>SFamily+0x3c</c> zone). Mis à jour par le push ZW 35.</summary>
        public short HouseOpen { get; set; }
        /// <summary>Nom du chef de famille — <c>SFamily+0x14</c> natif
        /// (mission : nom de l'exec du leader ; vecteur 3 du 502).</summary>
        public string LeaderName { get; set; } = string.Empty;
        /// <summary>Taxe commerciale — <c>family.business_tax</c>
        /// (<c>SFamily+0x32</c> zone, vecteur 5 du 502).</summary>
        public short BusinessTax { get; set; }
        /// <summary>Les 9 titres de famille (member, leader,
        /// cadre_2..8) — vecteur <c>fam+0x124</c> natif, émis tel quel
        /// dans la réponse membres de ville (503).</summary>
        public string[] Titles { get; set; } = new string[9];
        /// <summary>Bits de privilège par action (14 entrées, index
        /// dans l'ordre du SELECT natif LoadFamily :
        /// 0=channel,1=invite,2=kickout,3=editinfo,4=setcadre,
        /// 5=invite25r,6=fixequip,7=msgboard,8=delmsg,9=treasury,
        /// 10=repair,11=storage01,12=storage02,13=storage03) —
        /// vecteur <c>fam+0xe8</c> natif, testé par
        /// <c>CFamily::CheckPrivilige</c> mission.</summary>
        public int[] Privileges { get; set; } = new int[14];
        public List<GuildMember> Members { get; } = new();
        public int MaxMembers => 20 + Level * 10;
    }

    public sealed class GuildMember
    {
        public int CharacterId { get; set; }
        public string Name { get; set; } = string.Empty;
        public GuildRank Rank { get; set; }
        /// <summary>Rang natif brut 0-7 (<c>family_cadre_members.rank</c>)
        /// — transmis tel quel dans ClientChangeFamily i32 (fam+0x1c
        /// natif) ; <see cref="Rank"/> reste l'enum historique.</summary>
        public int FamilyRank { get; set; }
        /// <summary>Niveau du perso au chargement — exec+0x5c natif
        /// (colonne <c>level</c>) ; les handlers préfèrent l'EntryState
        /// live quand le membre est en ligne sur cette zone.</summary>
        public short Level { get; set; }
        /// <summary>Node du perso au chargement — exec+0x30 natif
        /// (colonne <c>node_id</c>) ; idem, l'EntryState prime en
        /// ligne.</summary>
        public short NodeId { get; set; }
        /// <summary>Flag <c>player_characters.quit</c> — exec+0xac
        /// natif ; la requête de membres de ville (175) exclut les
        /// membres avec quit == 1.</summary>
        public short Quit { get; set; }
        public long Contribution { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    public enum GuildRank
    {
        Leader = 0,
        Officer = 1,
        Veteran = 2,
        Member = 3,
        Recruit = 4,
    }

    private readonly Dictionary<int, Guild> _guilds = new();
    private readonly Dictionary<int, int> _guildByCharacter = new();
    /// <summary>Révision d'affectation famille par perso — équivalent
    /// du compteur `char+0x4de` natif : incrémenté à chaque
    /// création/adhésion/départ/changement de rang et renvoyé dans
    /// ClientChangeFamily (le client invalide son cache).</summary>
    private readonly Dictionary<int, int> _familyRevision = new();
    private int _nextGuildId = 1;
    private SocialRepository? _repo;
    /// <summary>Seuils d'XP famille par niveau — <c>c_level.ini</c>
    /// col11 (CLevelData+0x30 natif) indexé par le niveau de la ligne.
    /// Injecté au boot depuis <c>GameData.Levels</c> ; clé absente =
    /// ligne c_level manquante → <c>GainExp</c> natif retourne 0
    /// (aucun gain).</summary>
    private IReadOnlyDictionary<int, int>? _familyExpCurve;

    /// <summary>Révision courante de la famille du perso
    /// (char+0x4de natif — le client compare pour rafraîchir).</summary>
    public int GetFamilyRevision(int characterId)
        => _familyRevision.GetValueOrDefault(characterId);

    /// <summary>char+0x4de++ natif — appelé par toute mutation
    /// d'affectation famille du perso.</summary>
    private void BumpFamilyRevision(int characterId)
        => _familyRevision[characterId] =
            (_familyRevision.GetValueOrDefault(characterId) + 1) & 0xff;

    /// <summary>Branche la persistance PostgreSQL.</summary>
    public void Attach(SocialRepository repo) => _repo = repo;

    /// <summary>Branche la courbe d'XP famille — map
    /// <c>level → c_level[level].FamilyExpNeeded</c> (col11), la
    /// requête <c>CLevelQuery(level+1)</c> du natif.</summary>
    public void AttachFamilyExpCurve(
        IReadOnlyDictionary<int, int> thresholdByLevel) =>
        _familyExpCurve = thresholdByLevel;

    /// <summary>Resynchronise le cache famille depuis la table
    /// <c>family</c> — <b>upsert auto-réparant</b> : les guildes déjà
    /// en cache voient leurs scalaires rafraîchis
    /// (<c>house_open</c>, niveau, nom, chef, taxe) et les guildes
    /// absentes sont insérées complètement (membres compris). Le
    /// cache n'est normalement alimenté qu'au boot
    /// (<see cref="LoadAsync"/>) et par les pushes ZW — un UPDATE
    /// direct (outillage, données de test) ou un échec du chargement
    /// initial restait invisible sinon : recherche de ville 174 vide
    /// et entrée de hall 176 « Can't find family » (observé live).
    /// Appelée sur les chemins déjà rate-limités (3 s / 500 ms
    /// natifs) — une requête + une par guilde manquante.</summary>
    public async Task RefreshHouseOpensAsync()
    {
        if (_repo == null) return;
        var rows = await _repo.GetGuildsAsync();
        foreach (var r in rows)
        {
            if (_guilds.TryGetValue(r.Id, out var g))
            {
                g.HouseOpen = r.HouseOpen;
                g.Level = r.Level;
                g.Name = r.Name;
                g.LeaderName = r.LeaderName;
                g.BusinessTax = r.BusinessTax;
                continue;
            }
            var guild = await BuildGuildAsync(r);
            _guilds[guild.Id] = guild;
            if (guild.Id >= _nextGuildId) _nextGuildId = guild.Id + 1;
            EdenLog.Write($"[Guilds] famille #{guild.Id} " +
                $"'{guild.Name}' insérée au resync " +
                $"(house_open={guild.HouseOpen}, " +
                $"{guild.Members.Count} membres)");
        }
    }

    /// <summary>Recharge guildes + membres depuis la base (au boot).
    /// Résiliente : une guilde dont la ligne ou les membres ne se
    /// chargent pas est sautée avec un log — les autres peuplent
    /// quand même le cache (avant : une exception Dapper dans la
    /// boucle laissait <c>_guilds</c> entièrement vide, erreur
    /// avalée par l'appelant fire-and-forget — bug live
    /// 2026-10-26).</summary>
    public async Task LoadAsync()
    {
        if (_repo == null) return;
        var rows = await _repo.GetGuildsAsync();
        var loaded = 0;
        foreach (var g in rows)
        {
            try
            {
                var guild = await BuildGuildAsync(g);
                _guilds[g.Id] = guild;
                if (g.Id >= _nextGuildId) _nextGuildId = g.Id + 1;
                loaded++;
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Guilds] chargement famille " +
                    $"#{g.Id} '{g.Name}' : {ex.Message}");
            }
        }
        EdenLog.Write($"[Guilds] {loaded}/{rows.Count} " +
            "familles chargées");
    }

    /// <summary>Construit une <see cref="Guild"/> complète (scalaires
    /// + membres) depuis une ligne <c>family</c> — partagée par
    /// <see cref="LoadAsync"/> et <see cref="RefreshHouseOpensAsync"/>.
    /// Enregistre aussi le mapping charId→familyId
    /// (<c>_guildByCharacter</c>) pour chaque membre.</summary>
    private async Task<Guild> BuildGuildAsync(
        SocialRepository.GuildRow g)
    {
        var guild = new Guild
        {
            Id = g.Id, Name = g.Name, LeaderCharacterId = g.LeaderId,
            LeaderName = g.LeaderName, Level = g.Level,
            Experience = g.Experience, Treasury = g.Treasury,
            Emblem = g.Emblem, HouseOpen = g.HouseOpen,
            BusinessTax = g.BusinessTax,
            Titles = g.Titles.Length == 9 ? g.Titles
                : Enumerable.Repeat(string.Empty, 9).ToArray(),
            Privileges = g.Privileges.Length == 14 ? g.Privileges
                : new int[14],
        };
        foreach (var m in await _repo!.GetGuildMembersAsync(g.Id))
        {
            guild.Members.Add(new GuildMember
            {
                CharacterId = m.CharacterId,
                Name = m.Name,
                Rank = (GuildRank)m.Rank,
                FamilyRank = m.Rank,
                // Colonnes int4 → int ; les champs domaine restent
                // s16 comme le natif (exec+0x5c/+0x30/+0xac).
                Level = (short)m.Level, NodeId = (short)m.NodeId,
                Quit = (short)m.Quit,
                Contribution = m.Contribution,
            });
            _guildByCharacter[m.CharacterId] = g.Id;
        }
        return guild;
    }

    /// <summary>Crée une guilde — l'id vient de la base
    /// (<c>INSERT … RETURNING id</c>), donc l'appel est async de bout
    /// en bout : pas de sync-over-async sur le chemin de jeu.</summary>
    public async Task<Guild> CreateGuildAsync(int leaderId, string name,
        int leaderLevel)
    {
        await LeaveGuildAsync(leaderId);
        var guild = new Guild
        {
            Id = _nextGuildId++,
            Name = name,
            LeaderCharacterId = leaderId,
        };
        guild.Members.Add(new GuildMember
        {
            CharacterId = leaderId,
            Name = $"Player{leaderId}",
            Rank = GuildRank.Leader,
        });
        if (_repo != null)
            guild.Id = await _repo.CreateGuildAsync(name, leaderId);
        _guilds[guild.Id] = guild;
        _guildByCharacter[leaderId] = guild.Id;
        BumpFamilyRevision(leaderId);
        return guild;
    }

    public async Task<bool> AddMemberAsync(int guildId, int characterId,
        string name, GuildRank rank = GuildRank.Recruit)
    {
        if (!_guilds.TryGetValue(guildId, out var g)) return false;
        if (g.Members.Count >= g.MaxMembers) return false;
        if (_guildByCharacter.ContainsKey(characterId)) return false;
        g.Members.Add(new GuildMember
        {
            CharacterId = characterId, Name = name, Rank = rank,
        });
        _guildByCharacter[characterId] = guildId;
        if (_repo != null)
            await _repo.AddGuildMemberAsync(guildId, characterId,
                (short)rank);
        BumpFamilyRevision(characterId);
        return true;
    }

    public async Task<bool> LeaveGuildAsync(int characterId)
    {
        if (!_guildByCharacter.TryGetValue(characterId, out var gid)) return false;
        var g = _guilds[gid];
        g.Members.RemoveAll(m => m.CharacterId == characterId);
        _guildByCharacter.Remove(characterId);
        if (_repo != null)
            await _repo.RemoveGuildMemberAsync(gid, characterId);
        BumpFamilyRevision(characterId);
        if (g.Members.Count == 0) _guilds.Remove(gid);
        else if (g.LeaderCharacterId == characterId)
            g.LeaderCharacterId = g.Members[0].CharacterId;
        return true;
    }

    public bool SetRank(int leaderId, int targetId, GuildRank rank)
    {
        if (!_guildByCharacter.TryGetValue(leaderId, out var gid)) return false;
        var g = _guilds[gid];
        if (g.LeaderCharacterId != leaderId) return false;
        var m = g.Members.Find(x => x.CharacterId == targetId);
        if (m == null) return false;
        if (rank == GuildRank.Leader)
        {
            g.LeaderCharacterId = targetId;
            var old = g.Members.Find(x => x.CharacterId == leaderId)!;
            old.Rank = GuildRank.Officer;
        }
        m.Rank = rank;
        // Cas 9 du dispatch famille natif : changement de rang →
        // bump de la révision du perso ciblé (char+0x4de++).
        BumpFamilyRevision(targetId);
        return true;
    }

    /// <summary>Ajoute de l'XP de guilde — <c>CFamily::GainExp</c>
    /// mission @0x081beec0 : seuil = <c>CLevelQuery(level+1)+0x30</c>
    /// (c_level col11) ; ligne absente → aucun gain. Niveau &lt; 3 :
    /// si l'EXP bankée vaut déjà le seuil un nouveau gain est rejeté,
    /// sinon exp += gain puis <b>un seul</b> seuil est soustrait et
    /// <c>SetLevel(level+1)</c> est appelé (qui déclenche
    /// <c>CheckBuildingLevelUp</c> côté natif — re-joué par
    /// l'appelant zone). Niveau ≥ 3 : jamais de level-up par cette
    /// voie — l'EXP est clampée à [0, seuil] (la montée passe par une
    /// quête d'upgrade). Le cap formule (AllFormulaParas+0x718) n'a
    /// pas de source dans EDENFR — la borne effective est la fin de
    /// la table c_level (seuils vides dès le niveau 15).</summary>
    public void AddExperience(int guildId, long amount)
    {
        if (!_guilds.TryGetValue(guildId, out var g)) return;
        if (_familyExpCurve == null
            || !_familyExpCurve.TryGetValue(g.Level + 1,
                out var threshold))
            return; // CLevelQuery(level+1) == 0 → return 0
        if (g.Experience == threshold && g.Level < 3 && amount > 0)
            return; // déjà au seuil — le natif rejette le gain
        g.Experience += amount;
        if (g.Level < 3)
        {
            if (threshold <= g.Experience)
            {
                g.Experience -= threshold;
                g.Level++;
            }
        }
        else if (threshold < g.Experience)
            g.Experience = threshold;
        else if (g.Experience < 0)
            g.Experience = 0;
    }

    /// <summary>Variante persistante — crédite l'exp de famille puis
    /// écrit <c>family.lv/exp</c> (équivalent du TC natif
    /// « gain_family_exp N » zone→mission, CFamily::GainExp côté
    /// mission). Utilisée par les ordres de travail qui rapportent
    /// de l'exp famille (S_FamilyCollect col6 / S_FamilyCombine col3).</summary>
    public async Task AddExperienceAsync(int guildId, long amount)
    {
        AddExperience(guildId, amount);
        if (_repo != null && _guilds.TryGetValue(guildId, out var g))
            await _repo.UpdateGuildAsync(guildId, g.Level,
                g.Experience, g.Treasury, g.Emblem);
    }

    /// <summary>
    /// Preleve des taxes — CCharacter::FamilyLevyTaxes /
    /// FamilyLevyBusinessTaxes.
    /// </summary>
    public long LevyTax(int guildId, int taxPercent, long amount)
    {
        if (!_guilds.TryGetValue(guildId, out var g)) return 0;
        var tax = amount * taxPercent / 100;
        g.Treasury += tax;
        return tax;
    }

    /// <summary>Plafond natif de trésorerie
    /// (<c>SFamily::MaxTreasuryValue</c> = 0x3b9ac9ff, .rodata zone
    /// @0x888fb34).</summary>
    public const long MaxTreasuryValue = 999_999_999;

    /// <summary>Don à la trésorerie — zone 159
    /// <c>CNE_CZ_ZoneServerFamilyDonateMoney::Execute</c> : amount&gt;0,
    /// or suffisant (0x388), treasury+amount ≤ MaxTreasuryValue
    /// (0x370). Retourne le code d'erreur natif, 0 si succès —
    /// l'or est débité par l'appelant après succès.</summary>
    public int CanDonate(int characterId, int amount, long gold)
    {
        var g = GetGuildOf(characterId);
        if (g is null || amount <= 0) return -1;
        if (gold < amount) return 0x388;
        if (g.Treasury + amount > MaxTreasuryValue) return 0x370;
        return 0;
    }

    /// <summary>Crédite la trésorerie après un don validé
    /// (<c>family.treasury += amount</c> — persisté, le natif passe
    /// par la mission TC FamilyDonateMoney).</summary>
    public async Task DonateAsync(int characterId, int amount)
    {
        var g = GetGuildOf(characterId);
        if (g is null || amount <= 0) return;
        g.Treasury = Math.Min(g.Treasury + amount, MaxTreasuryValue);
        if (_repo != null)
            await _repo.UpdateGuildAsync(g.Id, g.Level, g.Experience,
                g.Treasury, g.Emblem);
    }

    /// <summary>Taxe d'or prélevée sur un gain —
    /// <c>CCharacter::FamilyLevyTaxes(amount)</c> natif : si le perso
    /// a une famille (char+0x5c) et que la trésorerie + taxe tient
    /// sous <see cref="MaxTreasuryValue"/>, la famille reçoit
    /// <c>(int)(amount*10 * 0.01f)</c> = amount/10 en trésorerie.
    /// Le retour natif (montant taxé) est ignoré par ExecAwardCmd —
    /// le joueur garde la totalité ; la taxe est un bonus famille.
    /// Retourne la taxe créditée (0 hors famille/plafond).</summary>
    public async Task<long> LevyGoldTaxAsync(int characterId,
        int amount)
    {
        var g = GetGuildOf(characterId);
        if (g is null || amount <= 0) return 0;
        var tax = amount / 10;
        if (tax <= 0 || g.Treasury + tax > MaxTreasuryValue) return 0;
        g.Treasury += tax;
        if (_repo != null)
            await _repo.UpdateGuildAsync(g.Id, g.Level, g.Experience,
                g.Treasury, g.Emblem);
        return tax;
    }

    /// <summary>Taxe de ville perçue sur un ordre de travail —
    /// <c>CCharacter::FamilyLevyBusinessTaxes</c> @0x0840c960 : crédite
    /// la famille VISITÉE de <paramref name="tax"/> si elle existe, a un
    /// <c>business_tax</c> ≠ 0 (fam+0x32), et si le plafond
    /// <see cref="MaxTreasuryValue"/> n'est pas dépassé (natif : accumulateur
    /// fam+0x34 flushé ensuite en trésorerie, ici crédit direct). Rend
    /// le nom de la guilde créditée (pour le texte 0xd15) ou null.</summary>
    public async Task<string?> CreditBusinessTaxAsync(int guildId, int tax)
    {
        if (tax <= 0 || !_guilds.TryGetValue(guildId, out var g))
            return null;
        if (g.BusinessTax == 0 || g.Treasury >= MaxTreasuryValue
            || g.Treasury + tax > MaxTreasuryValue) return null;
        g.Treasury += tax;
        if (_repo != null)
            await _repo.UpdateGuildAsync(g.Id, g.Level, g.Experience,
                g.Treasury, g.Emblem);
        return g.Name;
    }

    /// <summary>Distribue le tresor aux membres.</summary>
    public long DistributeTreasury(int guildId, int actorId)
    {
        if (!_guilds.TryGetValue(guildId, out var g)) return 0;
        if (g.LeaderCharacterId != actorId) return 0;
        if (g.Members.Count == 0 || g.Treasury == 0) return 0;
        var share = g.Treasury / g.Members.Count;
        g.Treasury = 0;
        return share;
    }

    public Guild? GetGuild(int guildId)
        => _guilds.TryGetValue(guildId, out var g) ? g : null;
    /// <summary>Toutes les familles du cache zone — équivalent de la
    /// map <c>CZoneServer+0x460</c> native (itérée par
    /// FamilyTownQuery 174 pour les ids de <c>Ins+0x47c</c>).</summary>
    public IEnumerable<Guild> AllGuilds() => _guilds.Values;
    public Guild? GetGuildOf(int characterId)
        => _guildByCharacter.TryGetValue(characterId, out var id) ? _guilds[id] : null;

    /// <summary>Membre de guilde du perso (null hors guilde) — rang
    /// pour ClientChangeFamily i32 (fam+0x1c natif).</summary>
    public GuildMember? GetMember(int characterId)
        => GetGuildOf(characterId)?.Members
            .FirstOrDefault(m => m.CharacterId == characterId);

    /// <summary><c>CFamily::CheckPrivilige</c> natif — teste le bit
    /// <paramref name="privIndex"/> de <c>fam+0xe8</c> contre le masque
    /// de rang du membre. Règles natives : action &gt;=12 (index 9-12)
    /// exige family level ≥ 3, index 13 exige ≥ 5, 14 exige ≥ 6 ;
    /// leader toujours autorisé ; masque cadre rank→bit
    /// (1→1, 2→2, 3→4, 4→8, 5→0x10, 9→0x100), membre sans rang →
    /// 0x80. Retourne vrai si <c>priv[index] &amp; mask != 0</c>.
    /// </summary>
    public bool CheckPrivilege(Guild g, GuildMember m, int privIndex)
    {
        if (privIndex < 0 || privIndex >= g.Privileges.Length)
            return false;
        // Barrière de niveau natif : privilèges 11-14 (idx 9-13)
        // n'ouvrent qu'à partir du niveau de famille requis.
        if (privIndex >= 9)
        {
            int required = privIndex <= 11 ? 3 : privIndex == 12 ? 5 : 6;
            if (g.Level < required) return false;
        }
        if (m.CharacterId == g.LeaderCharacterId) return true;
        int mask = m.FamilyRank switch
        {
            1 => 1, 2 => 2, 3 => 4, 4 => 8, 5 => 0x10,
            9 => 0x100, _ => 0x80,
        };
        return (g.Privileges[privIndex] & mask) != 0;
    }

    /// <summary>
    /// Débite la trésorerie de guilde — équivalent du débit natif fait
    /// par le MissionServer sur `FamilyFixEquip` (trésorerie de
    /// <c>family.treasury</c>) : refuse si fonds insuffisants, sinon
    /// débite et persiste (<c>guilds.treasury</c>).
    /// </summary>
    public async Task<bool> TrySpendTreasuryAsync(int guildId,
        long amount)
    {
        if (!_guilds.TryGetValue(guildId, out var g)) return false;
        if (g.Treasury < amount) return false;
        g.Treasury -= amount;
        if (_repo != null)
            await _repo.UpdateGuildAsync(guildId, g.Level, g.Experience,
                g.Treasury, g.Emblem);
        return true;
    }

    // ---------- Pushes monde (ZW 31/32/33, natifs 154/155/156) ----------
    // Le monde détient la persistance famille (tables `family`,
    // `player_characters.family_id`, `family_cadre_members`) ; ces
    // méthodes ne touchent QUE le cache zone (SFamily de
    // CZoneServer+0x45c natif).

    /// <summary>ZW NewFamily — la famille vient d'être créée par le
    /// monde (<c>CPlayerFactory::NewFamily</c> : enregistre SFamily +
    /// membre leader). familyId&lt;0 = refus (rien à appliquer).</summary>
    public void ApplyNewFamily(int familyId, string name,
        int leaderId, string leaderName)
    {
        if (familyId <= 0) return;
        if (!_guilds.TryGetValue(familyId, out var g))
        {
            g = new Guild { Id = familyId, Name = name,
                LeaderCharacterId = leaderId, LeaderName = leaderName };
            _guilds[familyId] = g;
            if (familyId >= _nextGuildId) _nextGuildId = familyId + 1;
        }
        else { g.Name = name; g.LeaderName = leaderName; }
        ApplyChangeFamily(leaderId, leaderName, familyId, rank: 0);
    }

    /// <summary>ZW ChangeFamily — affectation d'un membre :
    /// familyId&gt;0 → membre ajouté/rang maj ; familyId=0 → départ.
    /// Équivalent de <c>CNE_ZW_ZoneServerChangeFamily::Execute</c>
    /// (zone @0x82ad140) + le bump de <c>char+0x4de</c>.</summary>
    public void ApplyChangeFamily(int charId, string name,
        int familyId, int rank)
    {
        // Retire l'ancienne affectation le cas échéant.
        if (_guildByCharacter.TryGetValue(charId, out var oldId)
            && oldId != familyId
            && _guilds.TryGetValue(oldId, out var old))
            old.Members.RemoveAll(m => m.CharacterId == charId);
        if (familyId <= 0)
        {
            _guildByCharacter.Remove(charId);
            BumpFamilyRevision(charId);
            return;
        }
        if (!_guilds.TryGetValue(familyId, out var g))
        {
            // Famille encore inconnue de la zone (push avant réplique
            // complète) — stub rempli par le chargement/nom au prochain
            // push ; le nom sera corrigé par un NewFamily ultérieur.
            g = new Guild { Id = familyId, Name = string.Empty };
            _guilds[familyId] = g;
        }
        var member = g.Members.Find(m => m.CharacterId == charId);
        if (member is null)
        {
            member = new GuildMember { CharacterId = charId };
            g.Members.Add(member);
        }
        member.Name = name;
        // Rang natif brut 0-7 : 0 = membre simple (le chef est
        // leader_id de la famille), 1-7 = cadre.
        member.FamilyRank = rank;
        member.Rank = charId == g.LeaderCharacterId
            ? GuildRank.Leader
            : (rank > 0 ? GuildRank.Officer : GuildRank.Member);
        _guildByCharacter[charId] = familyId;
        BumpFamilyRevision(charId);
    }

    /// <summary>ZW RemoveFamily — dissolution : retire la famille et
    /// retourne les charId affectés (pour émettre 346 à chacun).</summary>
    public IReadOnlyList<int> ApplyRemoveFamily(int familyId)
    {
        if (!_guilds.TryGetValue(familyId, out var g)) return [];
        var ids = g.Members.Select(m => m.CharacterId).ToList();
        foreach (var id in ids)
        {
            _guildByCharacter.Remove(id);
            BumpFamilyRevision(id);
        }
        _guilds.Remove(familyId);
        return ids;
    }
}
