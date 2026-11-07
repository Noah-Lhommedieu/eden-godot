using System.Collections.Concurrent;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Hall de famille — CNE_CZ_ZoneServerFamilyInstanceCreate (173) et
/// CNE_CZ_ZoneServerFamilyInstanceGetData (132) natifs.
///
/// 173 (<c>CPlayerFactory::FamilyInstanceCreate</c> @0x80f0790) :
/// la zone vérifie type&lt;2, niveau ≥30, appartenance famille, chef,
/// house_open, niveau famille et trésorerie — puis relaie la commande
/// texte « FamilyInstanceCreate fm_id = %d type = %hd vip = %hhd » au
/// MissionServer via CDBAgent. Chez nous : <c>family_instance_create
/// &lt;charId&gt; &lt;type&gt; &lt;vip&gt;</c> par ZW 636 vers le
/// monde, qui rejoue les gates d'instance natifs
/// (<c>CFamily::InstanceCreate</c> @0x81bf660 : ≥30 membres, cap 49
/// villes, famille non recyclée, nodes 501/502) puis persiste
/// <c>family.house_open</c> et pousse ZW 35.
///
/// 132 (<c>Execute @0x8256ee0</c>) : exige <c>char+0xaac != 0</c>
/// (hall visité — posé par Mission_TC_VisitFamily) ET
/// <c>IsInFamilyInstance</c> (node type 4) ; cherche la SFamily dans
/// la map d'instances <c>Ins+0x460</c> et émet 487
/// <c>CNC_CZ_ClientFamilyInstanceData</c> (conteneur 0 des
/// bâtiments — voir <see cref="Core.Game.Family.FamilyPackets"/>).
///
/// 176 (<c>Execute @0x8259d30</c>) : entrée/sortie du hall —
/// <c>CZoneServer::VisitFamilyInstance</c> @0x8171440 pose
/// <c>char+0xaac</c> et transporte vers le node
/// <c>family.house_open</c> (501/502, type 4) ; la sortie
/// (familyId=-1) ramène au point CrossZoneData (char+0xa98).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Rate-limit natif 500 ms — char+0x650/0x654 est le
    /// MÊME champ partagé par les NCID 173 (create) et 176
    /// (town enter/leave), désasm @0x8259d30.</summary>
    private readonly ConcurrentDictionary<int, DateTimeOffset>
        _familyInstanceOpAt = new();

    /// <summary>173 `ZoneServerFamilyInstanceCreate` [s16 type][u8 vip]
    /// — gates zone de <c>CPlayerFactory::FamilyInstanceCreate</c>
    /// dans l'ordre natif, puis relais vers le monde.</summary>
    private async Task<bool> HandleFamilyInstanceCreateAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        EdenLog.Write($"[FamilyInstance] NCID173 reçu char=" +
            $"{st.CharId} len={payload.Length} hex=" +
            $"{Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 16)))}");
        if (payload.Length < 3) return true;
        var type = BitConverter.ToInt16(payload, 0); // s16 — type de hall
        var vip = payload[2];                        // u8 — vip (10 M vs 500 k)
        // param_2 < 2 natif — type invalide = silence.
        if (type is < 0 or >= 2) return true;
        // Rate-limit char+0x650/0x654 natif (~500 ms).
        var now = DateTimeOffset.UtcNow;
        if (_familyInstanceOpAt.TryGetValue((int)st.CharId,
                out var last)
            && now - last < TimeSpan.FromMilliseconds(500))
            return true;
        _familyInstanceOpAt[(int)st.CharId] = now;
        // char+0x470 < 0x1e → 0x599 (niveau perso < 30).
        if (st.Level < 30)
        {
            await SendTextIndexAsync(session, 0x599, ct);
            return true;
        }
        // Lookup SFamily par char+0x5c (family_id) — absente = silence.
        var guild = _services.Guilds.GetGuildOf((int)st.CharId);
        if (guild == null) return true;
        EdenLog.Write($"[FamilyInstance] gate zone : char={st.CharId} " +
            $"fam={guild.Id} leader={guild.LeaderCharacterId} " +
            $"lv={guild.Level} house={guild.HouseOpen} " +
            $"treasury={guild.Treasury} type={type} vip={vip}");
        // fam+0x10 != char+0x34 → 0x598 (pas le chef de famille).
        if (guild.LeaderCharacterId != (int)st.CharId)
        {
            await SendTextIndexAsync(session, 0x598, ct);
            return true;
        }
        // fam+0x3c != 0 → 0x91a (house_open déjà posé).
        if (guild.HouseOpen != 0)
        {
            await SendTextIndexAsync(session, 0x91a, ct);
            return true;
        }
        // fam+0x18 < 3 → 0x59a (niveau famille).
        if (guild.Level < 3)
        {
            await SendTextIndexAsync(session, 0x59a, ct);
            return true;
        }
        // fam+0x2c < coût → 0x6ae (500 k standard / 10 M vip).
        if (guild.Treasury < (vip == 0 ? 500_000 : 10_000_000))
        {
            await SendTextIndexAsync(session, 0x6ae, ct);
            return true;
        }
        EdenLog.Write(
            $"[FamilyInstance] relais monde : char={st.CharId} " +
            $"fam={guild.Id} lv={guild.Level} treasury={guild.Treasury} " +
            $"type={type} vip={vip}");
        // Natif : CTalkOp 0x43 requis (char+0x670 — opération ouverte
        // par le dialogue NPC « créer le hall »). Non modélisé chez
        // nous — le monde rejoue l'intégralité des gates mission
        // (membres, cap, recyclage) avant de muter.
        await _world.SendTextCommandAsync(
            $"family_instance_create {st.CharId} {type} {vip}", ct);
        return true;
    }

    /// <summary>132 `ZoneServerFamilyInstanceGetData` (vide) — le natif
    /// exige <c>char+0xaac != 0</c> (silence sinon), retrouve la SFamily
    /// de l'instance visitée et émet 487 avec le conteneur 0 des
    /// bâtiments (<c>family_building_container</c>). Map absente =
    /// silence natif (pas de paquet d'erreur).</summary>
    private async Task<bool> HandleFamilyInstanceGetDataAsync(
        NetSession session, EntryState st, CancellationToken ct)
    {
        var familyId = st.VisitedFamilyInstanceId;
        if (familyId == 0) return true;   // char+0xaac == 0 → silence
        // IsInFamilyInstance natif : node courant type 4 requis —
        // après la sortie, +0xaac persiste mais la requête est rejetée.
        if (!IsInFamilyInstanceNode(st)) return true;
        var guild = _services.Guilds.GetGuild(familyId);
        if (guild == null) return true;   // map Ins+0x460 sans l'entrée
        if (_services.Social == null) return true;
        var buildings = await _services.Social
            .GetFamilyBuildingsAsync(familyId, 0);
        await session.SendPacketAsync(487,
            Eden.Core.Game.Family.FamilyPackets.FamilyInstanceData(
                familyId, guild.Name, (short)guild.Level,
                guild.HouseOpen, buildings), ct);
        return true;
    }

    /// <summary>Vrai si le perso est physiquement dans un hall de
    /// famille — <c>CCharacter::IsInFamilyInstance</c> natif
    /// @0x84098d0 : type du node courant == 4.</summary>
    private bool IsInFamilyInstanceNode(EntryState st) =>
        _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type == 4;

    /// <summary>176 `ZoneServerFamilyTownEnter` [i32 familyId] —
    /// entrée/sortie du hall de famille
    /// (<c>CNE_CZ_ZoneServerFamilyTownEnter::Execute</c> @0x8259d30).
    /// familyId == -1 dans un hall → retour au node sauvegardé
    /// (char+0xa98) ; sinon gates →
    /// <c>CZoneServer::VisitFamilyInstance</c> @0x8171440 : char+0xaac
    /// = familyId puis transport vers <c>family.house_open</c>.</summary>
    private async Task<bool> HandleFamilyTownEnterAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 4) return true;
        var familyId = BitConverter.ToInt32(payload, 0); // i32 (-1 = quitter)
        // IsInNewerTown natif (node 94/0x5e ou 768/0x300) → 0x207f.
        if (st.NodeId is 94 or 768)
        {
            await SendTextIndexAsync(session, 0x207f, ct);
            return true;
        }
        // IsEncaged natif → DebugLog seulement (prison non modélisée).
        // Rate-limit char+0x650/0x654 — champ partagé avec le 173.
        var now = DateTimeOffset.UtcNow;
        if (_familyInstanceOpAt.TryGetValue((int)st.CharId,
                out var last)
            && now - last < TimeSpan.FromMilliseconds(500))
            return true;
        _familyInstanceOpAt[(int)st.CharId] = now;
        // IsInFamilyInstance && familyId==-1 →
        // TransportCharacterToNode(char+0xa98) : retour au point de
        // départ mémorisé (aire NORMALE aléatoire du node).
        if (familyId == -1 && IsInFamilyInstanceNode(st))
        {
            var back = st.CrossZoneNode != 0 ? st.CrossZoneNode
                : st.ReviveAreaId >> 16; // fallback +0xa98==0 natif
            if (back <= 0) back = 1;
            var (bx, by, bz) = ResolveInstanceEntryPos(
                _services.Scenes.GetScene($"S{back:D3}"),
                Random.Shared);
            EdenLog.Write($"[Zone-Family] TownEnter char=" +
                $"{st.CharId} : sortie hall -> node {back} " +
                $"({bx:F1},{by:F1})");
            await MigrateToNodeAsync(session, st, back, bx, by, bz,
                ct);
            return true;
        }
        // Équipe à capacité raid (team+0x54 ≥ 0xb) → 0x1430.
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team != null && team.MaxSize >= 11)
        {
            await SendTextIndexAsync(session, 0x1430, ct);
            return true;
        }
        // char+0xbb4 (requête versus en cours) → 0xd1f.
        if (st.VersusPending)
        {
            await SendTextIndexAsync(session, 0xd1f, ct);
            return true;
        }
        // --- CZoneServer::VisitFamilyInstance @0x8171440 ---
        // char+0x3c & 4 (mort) → 0xb80.
        if (st.Dead)
        {
            await SendTextIndexAsync(session, 0xb80, ct);
            return true;
        }
        // type du node courant ∉ {1, 3, 4} → 0xb89.
        var nodeType = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1;
        if (nodeType is not (1 or 3 or 4))
        {
            await SendTextIndexAsync(session, 0xb89, ct);
            return true;
        }
        // Map d'instances Ins+0x460 sans l'entrée →
        // CNC_CZ_ClientTextOutput 266 [u8 0x9b][str] — texte brut.
        // Resync house_open du cache avant le lookup (même cause que
        // la recherche 174 — UPDATE direct invisible sinon → silence).
        await _services.Guilds.RefreshHouseOpensAsync();
        var guild = _services.Guilds.GetGuild(familyId);
        if (guild == null)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutput
            {
                Field1 = 0x9b, Field2 = "Can't find family",
            }, ct);
            return true;
        }
        // SFamily+0x3c == 0 → retour silencieux natif.
        if (guild.HouseOpen == 0) return true;
        // Mission_TC_VisitFamily natif : char+0xaac = familyId puis
        // TransportCharacterToNode(SFamily+0x3c = house_open).
        st.VisitedFamilyInstanceId = familyId;
        var (ex, ey, ez) = ResolveInstanceEntryPos(
            _services.Scenes.GetScene($"S{guild.HouseOpen:D3}"),
            Random.Shared);
        EdenLog.Write($"[Zone-Family] TownEnter char={st.CharId} : " +
            $"visite famille {familyId} -> node {guild.HouseOpen} " +
            $"({ex:F1},{ey:F1})");
        await MigrateToNodeAsync(session, st, guild.HouseOpen,
            ex, ey, ez, ct);
        return true;
    }

    /// <summary>174 `ZoneServerFamilyTownQuery`
    /// [s16 lvlMin][s16 lvlMax][i32 buildingMask][str nameFilter] —
    /// <c>CPlayerFactory::FamilyTownQuery</c> zone @0x8110830 :
    /// rate-limit **3 s** sur le champ partagé char+0x650, candidats =
    /// familles instanciées (house_open ≠ 0 ≡ set Ins+0x47c), filtre
    /// niveau inclusif (fam+0x18), filtre nom = sous-chaîne des noms
    /// des items NPC du conteneur 0 (Ins+0x490, InsertTownNpcMap
    /// @0x8178260), masque bâtiment = GetBuildingValue @0x8464870
    /// (type S_Buliding 4..9 → bits 1,2,4,8,0x10,0x20). Réponse 502
    /// émise par tranches de 10 + un dernier paquet toujours émis.</summary>
    private async Task<bool> HandleFamilyTownQueryAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 8) return true;
        var reader = new InBinStream(payload);
        var levelMin = reader.ReadShort();   // s16 — lvlMin (+0x12)
        var levelMax = reader.ReadShort();   // s16 — lvlMax (+0x14)
        var buildingMask = reader.ReadInt(); // i32 — buildingMask (+0x18)
        // str — nameFilter (+0x1c) : u16 len + octets Big5.
        var nameFilter = reader.Remaining > 0
            ? reader.ReadString() : string.Empty;
        // Rate-limit 3 s — char+0x650 partagé avec 173/176/175
        // (Execute @0x825a890 : 3 < sec || (sec == 3 && µs > 0)).
        var now = DateTimeOffset.UtcNow;
        if (_familyInstanceOpAt.TryGetValue((int)st.CharId,
                out var last) && now - last <= TimeSpan.FromSeconds(3))
            return true;
        _familyInstanceOpAt[(int)st.CharId] = now;
        if (_services.Social == null) return true;
        // Resync house_open du cache : le set Ins+0x47c natif reflète
        // l'état live des instances — chez nous un UPDATE direct de
        // family.house_open (outillage/tests) sinon invisible jusqu'au
        // reboot → recherche vide (bug live 2026-10-26).
        await _services.Guilds.RefreshHouseOpensAsync();
        // Candidats = set Ins+0x47c natif : familles instanciées —
        // chez nous Guild.HouseOpen != 0, ordre croissant (std::set).
        var candidates = _services.Guilds.AllGuilds()
            .Where(g => g.HouseOpen != 0).OrderBy(g => g.Id).ToList();
        var buildings = candidates.Count == 0
            ? new Dictionary<int,
                List<Eden.Core.Game.Family.FamilyBuildingItem>>()
            : await _services.Social.GetFamilyBuildingsAsync(
                candidates.Select(g => g.Id).ToArray());
        var results = SelectTownQueryEntries(candidates, buildings,
            _services.GameData.Items, _services.GameData.BuildingTypes,
            levelMin, levelMax, buildingMask, nameFilter);
        // Chunk natif : un 502 par tranche de 10 familles, puis un
        // dernier paquet TOUJOURS émis (même vide).
        for (var i = 0; i < results.Count; i += 10)
            await session.SendPacketAsync(502,
                Eden.Core.Game.Family.FamilyPackets.FamilyTownQuery(
                    results.Skip(i).Take(10).ToList()), ct);
        if (results.Count % 10 == 0)
            await session.SendPacketAsync(502,
                Eden.Core.Game.Family.FamilyPackets.FamilyTownQuery([]),
                ct);
        return true;
    }

    /// <summary>Sélection pure de
    /// <c>CPlayerFactory::FamilyTownQuery</c> (zone @0x8110830) +
    /// gate du ctor 502 : conteneur 0 absent → famille exclue ;
    /// niveau inclusif ; filtre nom = sous-chaîne des noms d'items
    /// NPC (item_type 0x2a, subtype ≠ 0x16 — Ins+0x490) ; masque =
    /// GetBuildingValue @0x8464870.</summary>
    internal static List<
        Eden.Core.Game.Family.FamilyPackets.FamilyTownQueryEntry>
        SelectTownQueryEntries(
            IEnumerable<Eden.Core.Game.Services.GuildService.Guild>
                candidates,
            IReadOnlyDictionary<int,
                List<Eden.Core.Game.Family.FamilyBuildingItem>>
                buildings,
            IReadOnlyList<Eden.Core.Data.Parsers.ItemParser.ItemDef>
                items,
            IReadOnlyDictionary<int, short> buildingTypes,
            short levelMin, short levelMax, int buildingMask,
            string nameFilter)
    {
        var results = new List<
            Eden.Core.Game.Family.FamilyPackets.FamilyTownQueryEntry>();
        foreach (var g in candidates)
        {
            // Gate du ctor 502 : fam+0x7c sans clé 0 → famille exclue.
            if (!buildings.TryGetValue(g.Id, out var bld)
                || bld.Count == 0) continue;
            // Filtre niveau inclusif : param_2 ≤ fam+0x18 ≤ param_3.
            if (g.Level < levelMin || g.Level > levelMax) continue;
            // Filtre nom : string::find sur les noms des items NPC
            // (item_type 0x2a, subtype != 0x16) du conteneur 0 —
            // Ins+0x490 natif (clé = nom de l'item, item+8→proto+8).
            if (nameFilter.Length > 0 && !bld.Any(b =>
                    b.ItemType == 0x2a
                    && items.FirstOrDefault(i => i.Id == b.ItemTid)
                        is { } def && def.Kind != 0x16
                    && def.Name.Contains(nameFilter,
                        StringComparison.Ordinal)))
                continue;
            // Masque bâtiment : mask==0 accepte ; sinon exige un item
            // type 0x29 dont GetBuildingValue(type S_Buliding) & mask.
            if (buildingMask != 0 && !bld.Any(b =>
                    b.ItemType == 0x29
                    && buildingTypes.TryGetValue(b.Tid, out var bt)
                    && (GetBuildingValueBit(bt) & buildingMask) != 0))
                continue;
            results.Add(new(
                g.Id, (short)g.Level, g.Name, g.LeaderName,
                (uint)Math.Max(0, g.Experience), g.BusinessTax,
                g.Members.Count, g.HouseOpen, bld));
        }
        return results;
    }

    /// <summary>Bit de masque d'un type de bâtiment S_Buliding —
    /// switch natif de <c>CFamilyItem::GetBuildingValue</c>
    /// (zone @0x8464870) : 4→1, 5→2, 6→4, 7→8, 8→0x10, 9→0x20.</summary>
    private static int GetBuildingValueBit(short buildingType)
        => buildingType switch
        {
            4 => 1, 5 => 2, 6 => 4, 7 => 8, 8 => 0x10, 9 => 0x20,
            _ => 0,
        };

    /// <summary>175 `ZoneServerFamilyTownMemberQuery` [i32 famId] —
    /// <c>TC_FamilyTownMemberQuery</c> mission @0x820a670 : rate-limit
    /// 500 ms (même champ char+0x650), famille absente = silence,
    /// membres triés par charId (map fam+0xf8), exclus si
    /// <c>quit == 1</c> (exec+0xac) ou EnableSearch = 0 (exec+0xe2 —
    /// flag live de session ; défaut natif = 1) ; rang = 1 chef,
    /// sinon rang cadre (family_cadre_members), sinon 0. → 503.</summary>
    private async Task<bool> HandleFamilyTownMemberQueryAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 4) return true;
        var familyId = BitConverter.ToInt32(payload, 0); // i32 — fam_id
        // Rate-limit 500 ms — champ char+0x650 partagé.
        var now = DateTimeOffset.UtcNow;
        if (_familyInstanceOpAt.TryGetValue((int)st.CharId,
                out var last)
            && now - last < TimeSpan.FromMilliseconds(500))
            return true;
        _familyInstanceOpAt[(int)st.CharId] = now;
        var guild = _services.Guilds.GetGuild(familyId);
        if (guild == null) return true;  // GetFamily NULL → silence
        var members = new List<
            Eden.Core.Game.Family.FamilyPackets.FamilyTownMemberEntry>();
        foreach (var m in guild.Members
                     .OrderBy(m => m.CharacterId))
        {
            // exec+0xac == 1 natif = colonne quit (exclu).
            if (m.Quit == 1) continue;
            var live = _states.Values
                .FirstOrDefault(e => e.CharId == m.CharacterId);
            var online = _services.Sessions
                .FindByCharacter(m.CharacterId);
            // exec+0xe2 = EnableSearch (char+0x794, défaut ctor = 1) —
            // l'exclusion n'est connue que pour les membres en ligne.
            if (online is { SearchVisible: false }) continue;
            byte rank = m.CharacterId == guild.LeaderCharacterId
                ? (byte)1                        // chef → 1 natif
                : (byte)Math.Clamp(m.FamilyRank, 0, 255); // cadre/0
            members.Add(new(
                m.CharacterId, m.Name,
                live != null ? (short)live.Level : m.Level,
                rank, live?.NodeId ?? m.NodeId));
        }
        await session.SendPacketAsync(503,
            Eden.Core.Game.Family.FamilyPackets.FamilyTownMemberQuery(
                members, guild.Titles), ct);
        return true;
    }

    /// <summary>Position d'arrivée d'un transport vers un node —
    /// <c>TransportCharacterToNode</c> natif : NormalArea aléatoire du
    /// template + <c>CNodeTemplate::SetValidCoordInArea</c> (point
    /// uniforme dans le rayon). Repli : première position de la
    /// scène, puis première aire GATEWAY.</summary>
    internal static (float X, float Y, float Z) ResolveInstanceEntryPos(
        SceneParser.Scene? scene, Random rng)
    {
        if (scene == null) return (100f, 100f, 0f);
        var normals = scene.EventAreas
            .Where(a => a.Type == "NORMAL").ToList();
        if (normals.Count > 0)
        {
            var n = normals[rng.Next(normals.Count)];
            var angle = rng.NextSingle() * MathF.Tau;
            var r = n.Radius * MathF.Sqrt(rng.NextSingle());
            return (n.X + r * MathF.Cos(angle),
                n.Y + r * MathF.Sin(angle), n.Z);
        }
        if (scene.Positions.Count > 0) return scene.Positions[0];
        var gw = scene.EventAreas
            .FirstOrDefault(a => a.Type == "GATEWAY");
        return gw != null ? (gw.X, gw.Y, gw.Z) : (100f, 100f, 0f);
    }

    // ===== NPCs du hall + conteneurs bâtiments (130/131) =====
    // Référence native : SFamily::InitFamilyNpc @0x84657d0 (spawn
    // housekeeper + NPCs du conteneur 0 à l'init du node),
    // CNE_CZ_ZoneServerFamilyContainerPutItem::Execute @0x8255c00
    // et ...GetItem::Execute @0x82541c0. L'op courante du perso doit
    // être subtype 0x3b (CFamilyBuildingOp — posée par le service de
    // dialogue 11 "family_building", voir ZoneEntryFlow.Dialogue.cs).

    /// <summary>Spawn des NPCs du hall de famille visité —
    /// <c>SFamily::InitFamilyNpc</c> natif. Ces entités n'appartiennent
    /// PAS à la liste du node (partagée entre visiteurs de familles
    /// différentes) : elles vivent dans la vue du perso comme entités
    /// « rejouées » (st.Replayed) — la passe de sight les annonce et
    /// les retire par distance, sans fuite vers les autres
    /// joueurs.</summary>
    private async Task SpawnFamilyHallNpcsAsync(NetSession session,
        EntryState st, CancellationToken ct)
    {
        st.FamilyNpcsLoaded = true;
        if (_services.Social == null) return;
        var familyId = st.VisitedFamilyInstanceId;
        var guild = _services.Guilds.GetGuild(familyId);
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        EdenLog.Write($"[FamilyHall] spawn char={st.CharId} " +
            $"fam={familyId} node={st.NodeId} " +
            $"guild={(guild != null ? $"lv{guild.Level} house={guild.HouseOpen}" : "null")} " +
            $"scene={(scene != null ? "ok" : "null")}");
        if (guild == null || scene == null) return;
        // Auto-réparation des housekeepers de bâtiment (natif : posés
        // avec le bâtiment, FamilyBuildingPutItemFromInventroy
        // @0x808a770) — les anciennes lignes posées par GM n'en
        // avaient pas. Aucun PNJ de service n'est ajouté ici.
        await Handlers.GmHallSetupHandlers.PopulateAsync(_services,
            familyId, guild.Level, (short)st.NodeId,
            includeStationNpcs: false, reset: false);
        var items = await _services.Social
            .GetFamilyBuildingsAsync(familyId, 0);
        var gameData = _services.GameData;

        // --- Housekeeper — GetCenterBuildingHouseKeeperPos natif :
        // chaîne S_Buliding du hall (base tid 1 pour le node 501,
        // 0x33=51 pour 502), montée tant que le niveau suivant est ≤
        // au niveau de la famille ; le NPC vient de l'item col6 →
        // S_Item col35 (FamilyNpcTid) ; position = slot loc 100
        // (aire 0x32) + offset 4PHouse du modèle (code col1).
        var houseCode = "";
        var baseTid = st.NodeId == 501 ? 1 : 51;
        var cur = baseTid;
        for (var i = 0; i < 8; i++)
        {
            var next = gameData.Buildings.FirstOrDefault(kv =>
                kv.Value.PrevTid == cur && kv.Value.Level <= guild.Level);
            if (next.Key == 0) break;
            cur = next.Key;
        }
        if (gameData.Buildings.TryGetValue(cur, out var hdef))
        {
            houseCode = hdef.Code;
            var npcItem = gameData.Items
                .FirstOrDefault(i => i.Id == hdef.NpcItemTid);
            var pos = ResolveHallSlot(scene, houseCode,
                FamilyHallRules.HouseKeeperLoc);
            EdenLog.Write($"[FamilyHall] housekeeper fam={familyId} " +
                $"bld={cur} npcItem={hdef.NpcItemTid} " +
                $"npcTid={npcItem?.FamilyNpcTid ?? 0} " +
                $"pos={(pos?.X.ToString("F1") ?? "null")}," +
                $"{pos?.Y.ToString("F1") ?? ""}");
            if (npcItem is { FamilyNpcTid: > 0 } && pos != null)
                InjectHallNpc(st, npcItem.FamilyNpcTid,
                    pos.Value.X, pos.Value.Y, pos.Value.Z,
                    pos.Value.Facing);
        }
        else
            EdenLog.Write($"[FamilyHall] bâtiment tid={cur} absent " +
                $"de Buildings ({gameData.Buildings.Count} rows)");

        // --- NPCs posés dans le hall — conteneur 0, items 0x2a/0x52
        // à leurs coordonnées persistées (BornNPC natif utilise
        // item+0x88/+0x8c/+0x90 = x/y/face_dir). tid = NPC template
        // (proto+0xc natif — stocké tel quel dans la colonne tid).
        // L'item est indexé par le handle du NPC (item+0x9c natif) —
        // c'est le lien qu'utilise GetBuildingItemByNpcNetID pour
        // remonter au bâtiment de la station (base de groupe de loc).
        var byLoc = items.GroupBy(i => i.Loc)
            .ToDictionary(g => g.Key, g => g.First());
        var npcCount = 0;
        foreach (var b in items.Where(b => b.ItemType is 0x2a or 0x52
                     && b.Loc != FamilyHallRules.HolyLoc))
        {
            if (b.Tid > 0)
            {
                // Position = fonction pure du slot (aire [position] +
                // offsets 4PHouse, GetNpcPositionByNpcLoc natif
                // @0x84662b0) : on la recalcule pour les slots de
                // groupe — les lignes posées par GM portaient la
                // position du GM (hors carte, parfois).
                var (nx, ny, nf) = (b.X, b.Y, b.FaceDir);
                if (b.Loc < 50 && b.Loc % 10 != 0
                    && byLoc.TryGetValue(FamilyHallRules.GroupBase(b.Loc),
                        out var grpBase)
                    && grpBase.ItemType == 0x29
                    && gameData.Buildings.TryGetValue(grpBase.Tid,
                        out var grpDef)
                    && ResolveHallSlot(scene, grpDef.Code, b.Loc)
                        is { } slotPos)
                {
                    if (MathF.Abs(slotPos.X - nx) > 0.5f
                        || MathF.Abs(slotPos.Y - ny) > 0.5f)
                        EdenLog.Write($"[FamilyHall] npc {b.Tid} loc={b.Loc}" +
                            $" position persistée ({nx:F0},{ny:F0}) → " +
                            $"slot natif ({slotPos.X:F0},{slotPos.Y:F0})");
                    (nx, ny, nf) = (slotPos.X, slotPos.Y, slotPos.Facing);
                }
                var h = InjectHallNpc(st, b.Tid, nx, ny, 0, nf);
                st.HallNpcItems[h] = b;
                npcCount++;
                EdenLog.Write($"[FamilyHall] npc tid={b.Tid} loc={b.Loc} " +
                    $"@({nx:F0},{ny:F0}) handle=0x{h:x8}");
            }
        }
        EdenLog.Write($"[FamilyHall] {npcCount} PNJ de conteneur injectés " +
            $"fam={familyId}");
        InjectHallToolVendors(st, scene);

        // --- Statues de la famille — CZoneServer::InitStatue natif
        // @0x816f8f0 : les slots [npc] négatifs (templates 0xd675-9)
        // ne sont jamais des NPCs ; la statue ne naît que si la ligne
        // statues_scenes a un nom (BornStatue no-op sur nom vide).
        var statueRows = await _services.Social.GetStatuesAsync(
            WorldServerId, st.NodeId, familyId);
        if (statueRows.Count > 0)
        {
            var slots = scene.StatueSlots
                .ToDictionary(t => t.TemplateId);
            foreach (var row in statueRows.Where(r => r.HasName))
                InjectHallStatue(st, row, slots);
        }
    }

    /// <summary>Id worldserver natif (CGlobalInfo::WorldServerID) —
    /// constante 1 comme le monde (Eden.World FamilyHandlers).</summary>
    private const int WorldServerId = 1;

    /// <summary>Handle sight d'une statue — plage dédiée
    /// 0x88F0_xxxx indexée par template (un seul hall visité à la
    /// fois → unique dans la vue du perso).</summary>
    private static uint StatueHandle(int template) =>
        0x88F0_0000u | (uint)template;

    /// <summary>Enregistre une statue dans la vue du perso — entité
    /// rejouée 298 dont le DisplayName est le CSV natif de BornStatue
    /// ("nom,genre,face,hair,weapon,+0x14,classe,style,hide"). Kind=12
    /// (CStatue hérite CMonster natif) ; le client reconnaît la statue
    /// à l'EntityId 54901-54905 (désasm client @0x70ce18).</summary>
    private void InjectHallStatue(EntryState st,
        SocialRepository.StatueRow row,
        IReadOnlyDictionary<int, SceneParser.StatueSlot> slots)
    {
        if (!slots.TryGetValue(row.TemplateId, out var slot)) return;
        var h = StatueHandle(row.TemplateId);
        st.Replayed.TryRemove(h, out _);
        var cmd = BuildSeeMob(h, row.TemplateId, true,
            slot.X, slot.Y, 0, slot.Facing, 0);
        cmd.DisplayName = FamilyHallRules.BuildStatueCsv(
            row.PlayerName, row.GenderId, row.Face, row.Hair,
            row.WeaponActive, slot.Param, row.ClassId, row.Style,
            row.HideEquip);
        // Équipements du clone — NotifyStatue natif @0x849b3xx les
        // packe dans les champs optionnels du 298 : paires u16
        // (BornStatue → entité+0x60/0x68/0x70/0x74, décodés par
        // CStatue::Init client @0x775f00 en slots 1-8) ; +0x48 natif
        // = GetBossID → low16 = slot 10 (shoot), high16 = action
        // figée ; +0x44 = action<<16 ; +0x3c = transform.
        cmd.Hp = (int)(row.EquipHead | row.EquipChest << 16);
        cmd.MaxHp = (int)(row.EquipPants | row.EquipGlove << 16);
        cmd.Unk34 = row.EquipFeet | row.EquipBack << 16;
        cmd.Unk38 = row.EquipRight | row.EquipLeft << 16;
        cmd.Unk3C = row.TransformId;
        cmd.Unk44 = row.ActionId << 16;
        cmd.Unk48 = row.EquipShoot | row.ActionId << 16;
        var s = new OutBinStream();
        cmd.Serialize(s);
        st.Replayed[h] = s.ToArray();
        st.EntityPos[h] = (slot.X, slot.Y);
    }

    /// <summary>Position d'un slot de hall — base = ligne
    /// <c>[position]</c> de la scène dont col7 == aire de la loc
    /// (<see cref="FamilyHallRules.PositionArea"/>), + offsets du
    /// modèle de maison (4PHouse.ini) — formule native de
    /// <c>SFamily::GetNpcPositionByNpcLoc</c> @0x84662b0.</summary>
    private (float X, float Y, float Z, float Facing)? ResolveHallSlot(
        SceneParser.Scene scene, string? houseCode, short loc)
    {
        if (!scene.PositionGroups.TryGetValue(
                FamilyHallRules.PositionArea(loc), out var pg)
            || pg.Count == 0) return null;
        var b = pg[0];
        if (houseCode != null && _services.GameData.HouseLayouts
                .TryGetValue(houseCode, out var offsets))
            return FamilyHallRules.ResolveNpcPosition(b.X, b.Y, b.Z,
                b.Facing, loc, offsets);
        return (b.X, b.Y, b.Z, b.Facing);
    }

    /// <summary>Enregistre un NPC de hall dans la vue du perso —
    /// entrée Replayed (corps 298 sérialisé) que la passe de sight
    /// émet/retire selon la distance, comme les NPCs rejoués.
    /// Retourne le handle alloué (commandes GM de placement).</summary>
    private uint InjectHallNpc(EntryState st, int npcTid, float x,
        float y, float z, float facing)
    {
        var h = _services.Live.AllocNpcHandle(npcTid);
        var cmd = BuildSeeMob(h, npcTid, false, x, y, z, facing, 0);
        var s = new OutBinStream();
        cmd.Serialize(s);
        st.Replayed[h] = s.ToArray();
        st.HandleToEntity[h] = npcTid;
        st.EntityPos[h] = (x, y);
        return h;
    }

    /// <summary>Retire de la vue le NPC de hall rejoué le plus proche
    /// de (x, y) dans <paramref name="maxRadius"/> — purge
    /// Replayed/EntityPos/HandleToEntity/Sent. Retourne le handle
    /// retiré (0 si aucun) pour que l'appelant émette le 269.</summary>
    private uint RemoveHallNpcFromView(EntryState st, float x,
        float y, float maxRadius)
    {
        uint best = 0;
        var bestD = maxRadius * maxRadius;
        foreach (var (h, pos) in st.EntityPos)
        {
            if (!st.Replayed.ContainsKey(h)) continue;
            var d = (pos.X - x) * (pos.X - x) +
                (pos.Y - y) * (pos.Y - y);
            if (d < bestD) { bestD = d; best = h; }
        }
        if (best == 0) return 0;
        st.Replayed.TryRemove(best, out _);
        st.EntityPos.TryRemove(best, out _);
        st.HandleToEntity.TryRemove(best, out _);
        st.Sent.TryRemove(best, out _);
        return best;
    }

    /// <summary>L'op courante du perso est le dialogue bâtiment de
    /// famille (subtype natif 0x3b = CFamilyBuildingOp) ET le NPC est
    /// encore à portée de parole (TargetInTalkingRange natif ≤ 8u —
    /// hors portée l'op est détruite sans message).</summary>
    private bool HasFamilyBuildingOp(EntryState st)
    {
        if (st.ShopOpType != 0x3b || st.ShopOpNpcHandle == 0)
            return false;
        if (st.EntityPos.TryGetValue((uint)st.ShopOpNpcHandle,
                out var np)
            && MathF.Sqrt((np.X - st.PosX) * (np.X - st.PosX)
                + (np.Y - st.PosZ) * (np.Y - st.PosZ)) <= 8f)
            return true;
        st.ShopOpType = -1;
        st.ShopOpNpcHandle = 0;
        st.ShopOpNpcUid = 0;
        return false;
    }

    /// <summary>130 `ZoneServerFamilyContainerPutItem`
    /// [s16 mode][s16 container][s16 loc][s16 bag][s16 slot] —
    /// dépose d'un item de l'inventaire dans un conteneur du hall.
    /// Gates natifs de l'Execute @0x8255c00 +
    /// <c>CheckPutItemFromInventory</c> @0x82559b0 : op 0x3b à portée,
    /// famille du perso = famille instanciée, kinds par conteneur
    /// (1→0x29, 2→0x2a/0x52, 0@0x33→subtype 0x16), item non lié
    /// (0x67f), CheckFamilyItemDelete/CheckNpcSizeLimit pour le
    /// conteneur 0, capacité 40 des conteneurs annexes.</summary>
    private async Task<bool> HandleFamilyContainerPutAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 10 || _services.Social == null) return true;
        var mode = BitConverter.ToInt16(payload, 0);      // +0x12
        var container = BitConverter.ToInt16(payload, 2); // +0x14
        var loc = BitConverter.ToInt16(payload, 4);       // +0x16
        var bag = BitConverter.ToInt16(payload, 6);       // +0x18
        var slot = BitConverter.ToInt16(payload, 8);      // +0x1a
        if (!HasFamilyBuildingOp(st)) return true;
        var guild = _services.Guilds.GetGuildOf((int)st.CharId);
        if (guild == null || guild.HouseOpen == 0) return true;
        if (mode != 0) return true;   // seul le mode inventaire existe

        var item = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, bag, slot);
        if (item == null) return true;
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == item.ItemId);
        var kind = def?.Kind ?? 0;

        // CheckPutItemFromInventory — restrictions de kind.
        if (container == 1 && kind != 0x29) return true;
        if (container == 2 && kind is not (0x2a or 0x52)) return true;
        // Holy loc 0x33 : le gate natif teste le SUBTYPE de l'item
        // (virtual itemdef+0x10 == 0x16 — col9 S_Item), pas le kind.
        // Seuls les items sacrés 0x52 et certains contrats 0x2a ont
        // subtype 0x16 ; les items kind==0x16 (armes) sont refusés.
        if (container == 0 && loc == FamilyHallRules.HolyLoc
            && def?.EquipLoc != 0x16) return true;
        if (item.IsBound)   // clone+0x24 & 0x10 natif → 0x67f
        {
            await SendTextIndexAsync(session, 0x67f, ct);
            return true;
        }

        var c0 = (await _services.Social.GetFamilyBuildingsAsync(
                guild.Id, 0)).ToDictionary(b => b.Loc);
        GameDataService.BuildingDef? GetDef(int tid) =>
            _services.GameData.Buildings.TryGetValue(tid, out var d)
                ? d : null;

        switch (container)
        {
            case 0:
                if (!FamilyHallRules.CheckItemLoc((short)kind, loc, c0))
                {
                    // Natif : texte 1 seulement pour un NPC sans
                    // bâtiment au groupe (sinon silence).
                    if (kind == 0x2a)
                        await SendTextIndexAsync(session, 1, ct);
                    return true;
                }
                if (!FamilyHallRules.CheckFamilyItemDelete(c0, loc,
                        GetDef))
                {
                    await SendTextIndexAsync(session, 0x535, ct);
                    return true;
                }
                if (!FamilyHallRules.CheckNpcSizeLimit(c0, loc, GetDef))
                {
                    await SendTextIndexAsync(session, 0x8ef, ct);
                    return true;
                }
                break;
            case 1:
                if ((await _services.Social.GetFamilyBuildingsAsync(
                        guild.Id, 1)).Count
                    >= FamilyHallRules.StorageCapacity)
                {
                    await SendTextIndexAsync(session, 0xa01, ct);
                    return true;
                }
                break;
            case 2:
                if ((await _services.Social.GetFamilyBuildingsAsync(
                        guild.Id, 2)).Count
                    >= FamilyHallRules.StorageCapacity)
                {
                    await SendTextIndexAsync(session, 0xa02, ct);
                    return true;
                }
                break;
            default:
                return true;
        }

        // Position pour les items de hall du conteneur 0
        // (GetNpcPositionByNpcLoc natif via la scène + 4PHouse).
        // Exception native : la ligne 0x29 du bâtiment est persistée
        // avec x=y=face=0.0 (PutItemInFamilyBuilding de
        // FamilyBuildingPutItemFromInventroy @0x808a770 — les floats
        // finaux sont des littéraux 0 ; seuls les NPC portent une
        // position).
        float x = 0, y = 0, face = 0;
        if (container == 0 && kind != 0x29)
        {
            var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
            // Holy (loc 0x33) : GetHolyItemPosition @0x8465d60 —
            // record [position] de la scène à l'aire 0x38 tel quel,
            // SANS offsets 4PHouse (houseCode=null dans
            // ResolveHallSlot). Sinon GetNpcPositionByNpcLoc : code
            // maison du bâtiment à la base de groupe.
            var houseCode = loc == FamilyHallRules.HolyLoc ? null
                : c0.TryGetValue(FamilyHallRules.GroupBase(loc),
                        out var baseItem)
                    && _services.GameData.Buildings.TryGetValue(
                        baseItem.Tid, out var bd) ? bd.Code : null;
            var pos = scene != null
                ? ResolveHallSlot(scene, houseCode, loc) : null;
            if (pos != null)
            {
                x = pos.Value.X; y = pos.Value.Y; face = pos.Value.Facing;
            }
        }

        // tid persisté : NPC → son template (proto+0x124 natif, = col35
        // S_Item) ; bâtiment → son tid S_Buliding (même colonne
        // conditionnelle — FamilyBuildingPutItemFromInventroy @0x808a770
        // écrit GetBuildingData(item+0x124) dans la colonne tid, pas le
        // tid du contrat) ; sinon le tid de l'item lui-même.
        (int Tid, GameDataService.BuildingDef Def)? bld = null;
        WorldParsers.NpcDef? npcTpl = null;
        var storeTid = item.ItemId;
        if (kind is 0x2a or 0x52)
        {
            storeTid = def?.FamilyNpcTid ?? item.ItemId;
            // Level de la ligne NPC = proto+0xc natif (le record
            // GameData+0x120 du tid résolu), pas celui du contrat.
            npcTpl = _services.GameData.Npcs.FirstOrDefault(
                n => n.Id == storeTid);
        }
        else if (kind == 0x29)
        {
            if (def == null) return true;
            bld = FamilyHallRules.ResolveBuildingPut(
                def.FamilyBuildingTid, guild.Level,
                _services.GameData.Buildings,
                _services.GameData.NextBuildingTid);
            if (bld == null) return true;   // rejet muet natif
            storeTid = bld.Value.Tid;
        }

        // Transaction native : EradicateItem(inventaire) +
        // PutItemInFamilyBuilding + ClientDropItem au client.
        await _services.Inventory.RemoveItemAsync(
            (int)st.CharId, bag, slot, 1);
        await _services.Social.DeleteFamilyBuildingItemAsync(
            guild.Id, container, loc);   // remplacement éventuel
        // due_date natif (InsertItemToBuilding @0x81b0d80) : conteneur
        // 0 + kinds 0x2a/0x52 avec durée d'item (CItemData+0x70 =
        // DurationSeconds) → now + durée ; tout le reste → 0. Les
        // contrats NPC d'EDENFR ont 3 j / 7 j — à l'échéance les
        // *Shop::OpenShop refusent le service (texte 0xc56).
        var dueDate = container == 0
            ? Eden.Core.Game.Family.FamilyBuildingWorkRules.DueDateFor(
                (short)kind, def?.DurationSeconds ?? 0,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            : 0;
        await _services.Social.InsertFamilyBuildingItemAsync(
            new Eden.Core.Game.Family.FamilyBuildingItem
            {
                ItemTid = (short)item.ItemId, Tid = storeTid,
                ItemType = (short)kind,
                // Durabilité/niveau natifs : 0x29 → def+0x20
                // (max_durability col8) et def+0x10 (level col4) de
                // S_Buliding ; 0x2a/0x52 → itemdef+0xc (durabilité du
                // contrat) et proto NPC +0xc (level).
                Durability = (short)Math.Min(
                    bld?.Def.MaxDurability
                    ?? (kind is 0x2a or 0x52
                        ? def?.MaxDurability ?? item.Durability
                        : item.Durability), 0x7fff),
                ContainerIndex = container, Loc = loc,
                X = x, Y = y, FaceDir = face,
                Level = (short)(bld?.Def.Level ?? npcTpl?.Level ?? 0),
                DueDate = dueDate,
            }, guild.Id);
        _services.NotifyItemChanged((int)st.CharId, item.ItemId);

        // Housekeeper auto (natif, conteneur 0 uniquement) : un
        // bâtiment avec item NPC (S_Buliding col6 → +0x18) insère
        // aussitôt une ligne 0x2a au slot base+1 — item_tid = l'item
        // NPC, tid = le template NPC (CNpcItem+0x124), position via
        // GetNpcPositionByNpcLoc(loc+1, code maison). Échec de
        // résolution → rejet du put entier côté natif ; ici la ligne
        // est déjà posée — on exige la résolution AVANT d'insérer.
        if (bld is { Def.NpcItemTid: > 0 } && container == 0)
        {
            var npcContract = _services.GameData.Items
                .FirstOrDefault(i => i.Id == bld.Value.Def.NpcItemTid);
            var npcTid = npcContract?.FamilyNpcTid ?? 0;
            var npcTemplate = npcTid > 0
                ? _services.GameData.Npcs.FirstOrDefault(
                    n => n.Id == npcTid) : null;
            var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
            var npcPos = scene != null
                ? ResolveHallSlot(scene, bld.Value.Def.Code,
                    (short)(loc + 1)) : null;
            if (npcContract != null && npcTemplate != null
                && npcPos != null)
            {
                await _services.Social.InsertFamilyBuildingItemAsync(
                    new Eden.Core.Game.Family.FamilyBuildingItem
                    {
                        ItemTid = (short)bld.Value.Def.NpcItemTid,
                        Tid = npcTid, ItemType = 0x2a,
                        Durability = (short)Math.Min(
                            npcContract.MaxDurability, 0x7fff),
                        ContainerIndex = 0, Loc = (short)(loc + 1),
                        X = npcPos.Value.X, Y = npcPos.Value.Y,
                        FaceDir = npcPos.Value.Facing,
                        Level = (short)npcTemplate.Level,
                        // due_date comme la ligne posée (durée du
                        // contrat NPC, InsertItemToBuilding @0x81b0d80).
                        DueDate = Eden.Core.Game.Family
                            .FamilyBuildingWorkRules.DueDateFor(0x2a,
                                npcContract.DurationSeconds,
                                DateTimeOffset.UtcNow
                                    .ToUnixTimeSeconds()),
                    }, guild.Id);
                var handle = InjectHallNpc(st, npcTid, npcPos.Value.X,
                    npcPos.Value.Y, npcPos.Value.Z,
                    npcPos.Value.Facing);
                var seeBytes = st.Replayed[handle];
                foreach (var (sid, ost) in _states)
                {
                    if (!ost.Migrated || ost.NodeId != st.NodeId
                        || ost.InstanceGroup != st.InstanceGroup
                        || ost.CharId == st.CharId
                        || !guild.Members.Any(
                            m => (int)m.CharacterId == ost.CharId)
                        || ost.Session is not { } s) continue;
                    ost.Replayed[handle] = seeBytes;
                    ost.HandleToEntity[handle] = npcTid;
                    ost.EntityPos[handle] = (npcPos.Value.X,
                        npcPos.Value.Y);
                    try { await s.SendPacketAsync(298, seeBytes, ct); }
                    catch { /* session morte */ }
                }
            }
            else
            {
                EdenLog.Write($"[Zone-Family] ContainerPut " +
                    $"char={st.CharId} bâtiment {bld.Value.Tid} posé " +
                    $"mais housekeeper irrésolu " +
                    $"(item={bld.Value.Def.NpcItemTid})");
            }
        }

        // Refresh du conteneur : ZMis FamilyContainerUpdate natif
        // pousse le delta (486 put/485 delete) à TOUS les membres de
        // la famille présents sur le node du hall (Execute @0x8315cb0
        // — itère SFamily::GetNode × map des membres +0xb0). On réémet
        // le 484 complet (format prouvé, mêmes données de conteneur)
        // au demandeur et aux autres membres migrés du node.
        var items484 = await _services.Social
            .GetFamilyBuildingsAsync(guild.Id, container);
        await session.SendPacketAsync(484,
            Eden.Core.Game.Family.FamilyPackets.FamilyContainerData(1,
                container, items484), ct);
        await PushContainerRefreshToHallMembersAsync(st, guild,
            container, items484, ct);
        EdenLog.Write($"[Zone-Family] ContainerPut char={st.CharId} " +
            $"item={item.ItemId} -> c{container} loc={loc}");
        return true;
    }

    /// <summary>131 `ZoneServerFamilyContainerGetItem`
    /// [s16 container][s16 loc][s16 bag][s16 slot] — retrait d'un item
    /// du conteneur de hall vers l'inventaire. Gates natifs de
    /// l'Execute @0x82541c0 : op 0x3b, famille du perso ; conteneur 0 →
    /// CheckFamilyItemDelete (0x535) ; puis
    /// FamilyBuildingContainerDeleteItem → insert inventaire.</summary>
    private async Task<bool> HandleFamilyContainerGetAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 8 || _services.Social == null) return true;
        var container = BitConverter.ToInt16(payload, 0); // +0x12
        var loc = BitConverter.ToInt16(payload, 2);       // +0x14
        var bag = BitConverter.ToInt16(payload, 4);       // +0x16
        var slot = BitConverter.ToInt16(payload, 6);      // +0x18
        if (!HasFamilyBuildingOp(st)) return true;
        var guild = _services.Guilds.GetGuildOf((int)st.CharId);
        if (guild == null || guild.HouseOpen == 0) return true;

        var items = await _services.Social.GetFamilyBuildingsAsync(
            guild.Id, container);
        var item = items.FirstOrDefault(b => b.Loc == loc);
        if (item == null) return true;
        if (container == 0)
        {
            var c0 = items.ToDictionary(b => b.Loc);
            GameDataService.BuildingDef? GetDef(int tid) =>
                _services.GameData.Buildings.TryGetValue(tid, out var d)
                    ? d : null;
            if (!FamilyHallRules.CheckFamilyItemDelete(c0, loc, GetDef))
            {
                await SendTextIndexAsync(session, 0x535, ct);
                return true;
            }
        }

        var added = await _services.Inventory.AddItemAsync(
            (int)st.CharId, item.ItemTid & 0xffff, 1, bag);
        if (!added.Success) return true;
        await _services.Social.DeleteFamilyBuildingItemAsync(
            guild.Id, container, loc);
        _services.NotifyItemChanged((int)st.CharId,
            item.ItemTid & 0xffff);
        var items484 = await _services.Social
            .GetFamilyBuildingsAsync(guild.Id, container);
        await session.SendPacketAsync(484,
            Eden.Core.Game.Family.FamilyPackets.FamilyContainerData(1,
                container, items484), ct);
        // Même push aux autres membres présents dans le hall que pour
        // le put (485 delete natif ↔ refresh du conteneur).
        await PushContainerRefreshToHallMembersAsync(st, guild,
            container, items484, ct);
        EdenLog.Write($"[Zone-Family] ContainerGet char={st.CharId} " +
            $"item={item.ItemTid & 0xffff} <- c{container} loc={loc}");
        return true;
    }

    /// <summary>`ZMis FamilyContainerUpdate` natif : la maj d'un
    /// conteneur de hall est poussée à tous les membres de la famille
    /// migrés sur le node (Execute @0x8315cb0 — l'itération native
    /// filtre les chars du node par la map des membres SFamily+0xb0).
    /// Repli fidèle : réémission du 484 complet (codec prouvé) —
    /// les deltas 485/486 portent les mêmes données.</summary>
    private async Task PushContainerRefreshToHallMembersAsync(
        EntryState st,
        Eden.Core.Game.Services.GuildService.Guild guild,
        int container,
        IReadOnlyList<Eden.Core.Game.Family.FamilyBuildingItem> items,
        CancellationToken ct)
    {
        var memberIds = guild.Members
            .Select(m => (int)m.CharacterId).ToHashSet();
        foreach (var (sid, ost) in _states)
        {
            if (!ost.Migrated || ost.NodeId != st.NodeId ||
                ost.InstanceGroup != st.InstanceGroup) continue;
            if (ost.CharId == st.CharId) continue;
            if (!memberIds.Contains((int)ost.CharId)) continue;
            if (ost.Session is not { } s) continue;
            try
            {
                await s.SendPacketAsync(484,
                    Eden.Core.Game.Family.FamilyPackets
                        .FamilyContainerData(1, (short)container, items),
                    ct);
            }
            catch { /* session morte */ }
        }
    }

    // ================= Statues de famille (161) =================
    // CNE_CZ_ZoneServerFamilyStatue::Execute @0x8257f10 +
    // CPlayerFactory::SetStatue @0x81099a0 + CNode::BornStatue
    // @0x84af050 + CStatueDBO::SaveStatueToDB @0x836ff30.

    /// <summary>161 `ZoneServerFamilyStatue`
    /// [i32 targetCharId][i32 position][str name][i32 rot][f32 frame]
    /// — pose/retrait d'une statue de membre dans le hall. Gates
    /// natifs : chef de famille seul (0x382), perso physiquement dans
    /// le hall de SA famille (0x38e) ; position 0 = retrait par nom
    /// (0x38f "%s" si absente → 0x38b) ; positions 1-4 = pose :
    /// famille niveau ≥4 (positions ≤2 si niveau &lt;6), cible en
    /// ligne (0x38e), trésorerie ≥500 000 (0x38c) → statue persistée
    /// + spawn + broadcast famille 0xb4e + 0x38a.</summary>
    private async Task<bool> HandleFamilyStatueAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (_services.Social == null) return true;
        var r = new InBinStream(payload);
        var targetId = r.ReadInt();    // +0x14 : membre à statufier
        var position = r.ReadInt();    // +0x18 : 0 = retirer, 1-4 = poser
        var name = r.ReadString();     // +0x1c : nom cherché (retrait)
        var rot = r.ReadInt();         // +0x20 : u16 lu → action_id
        var frame = r.ReadFloat();     // +0x24 : frame_key
        if (position < 0 || position > 4) return true;
        var guild = _services.Guilds.GetGuildOf((int)st.CharId);
        if (guild == null) return true;
        // fam+0x10 == char+0x34 natif — chef seul.
        if (guild.LeaderCharacterId != (int)st.CharId)
        {
            await SendTextIndexAsync(session, 0x382, ct);
            return true;
        }
        // IsInFamilyInstance + node courant == node famille → 0x38e.
        if (!IsInFamilyInstanceNode(st)
            || st.VisitedFamilyInstanceId != guild.Id
            || st.NodeId != guild.HouseOpen)
        {
            await SendTextIndexAsync(session, 0x38e, ct);
            return true;
        }
        if (position == 0)
            await RemoveFamilyStatueAsync(session, st, guild.Id, name,
                ct);
        else
            await PlaceFamilyStatueAsync(session, st, guild, targetId,
                position, rot, frame, ct);
        return true;
    }

    /// <summary>161 position 0 — retrait : la statue dont le nom
    /// persiste = <paramref name="name"/> est vidée (BornStatue no-op
    /// natif sur nom vide → despawn) et le 269 part aux visiteurs du
    /// hall ; absente → 0x38f "%s".</summary>
    private async Task RemoveFamilyStatueAsync(NetSession session,
        EntryState st, int familyId, string name,
        CancellationToken ct)
    {
        var rows = await _services.Social!.GetStatuesAsync(
            WorldServerId, st.NodeId, familyId);
        var row = rows.FirstOrDefault(r => r.HasName
            && FamilyHallRules.IsStatueTemplate(r.TemplateId)
            && r.PlayerName == name);
        if (row == null)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x38f, Field2 = name }, ct);   // "%s" natif
            return;
        }
        await _services.Social.SaveStatueAsync(WorldServerId, st.NodeId,
            row.TemplateId, familyId, row with { PlayerName = "" });
        await DespawnHallStatueAsync(st, familyId, row.TemplateId, ct);
        await SendTextIndexAsync(session, 0x38b, ct);
    }

    /// <summary>161 positions 1-4 — pose : niveau famille ≥4 (≤2
    /// statues si niveau &lt;6), cible en ligne (0x38e), trésorerie
    /// (0x38c) → SaveStatue + BornStatue pour les visiteurs + textes
    /// 0xb4e (famille) / 0x38a (demandeur).</summary>
    private async Task PlaceFamilyStatueAsync(NetSession session,
        EntryState st, GuildService.Guild guild, int targetId,
        int position, int rot, float frame, CancellationToken ct)
    {
        if (guild.Level < 4) return;              // fam+0x18 natif
        if (guild.Level < 6 && position > 2) return;
        // GetCharacter natif — la cible doit être en ligne.
        var target = _states.Values.FirstOrDefault(o =>
            o.CharId == (uint)targetId && o.Migrated);
        if (target == null)
        {
            await SendTextIndexAsync(session, 0x38e, ct);
            return;
        }
        if (guild.Treasury < FamilyHallRules.StatueCost)
        {
            await SendTextIndexAsync(session, 0x38c, ct);
            return;
        }
        var ch = await _services.Characters
            .GetCharacterByIdAsync(targetId);
        if (ch == null) return;
        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        var template = FamilyHallRules.StatueModelForPos(position);
        if (scene == null || scene.StatueSlots.All(
                t => t.TemplateId != template)) return;
        if (!await _services.Guilds.TrySpendTreasuryAsync(guild.Id,
                FamilyHallRules.StatueCost)) return;

        // Apparence persistée — clone du perso (SetStatue natif copie
        // genre/face/hair/équipements depuis char+0x4a6+).
        var eq = await LoadStatueEquipsAsync(targetId);
        // Colonnes DB = champs CStatueDB natifs : head..back =
        // vec[1..6], equip_left = vec[8], equip_right = vec[7]
        // (SetStatue @0x81099a0 écrit vec[8]→+0x48 « left » et
        // vec[7]→+0x4a « right » — stockage inversé).
        var row = new SocialRepository.StatueRow(template,
            ch.GivenName, ch.GenderId, ch.FaceStyle, ch.HairStyle, 1,
            ch.ClassId, ch.Style, 0, (ushort)rot, frame,
            eq.GetValueOrDefault(1), eq.GetValueOrDefault(2),
            eq.GetValueOrDefault(3), eq.GetValueOrDefault(4),
            eq.GetValueOrDefault(5), eq.GetValueOrDefault(6),
            eq.GetValueOrDefault(8), eq.GetValueOrDefault(7),
            eq.GetValueOrDefault(10), 0, 0);
        await _services.Social!.SaveStatueAsync(WorldServerId, st.NodeId,
            template, guild.Id, row);

        // BornStatue natif : l'ancienne pose du slot est tuée puis la
        // nouvelle est annoncée aux visiteurs du hall.
        var slots = scene.StatueSlots.ToDictionary(t => t.TemplateId);
        foreach (var o in _states.Values.Where(o => o.Migrated
                     && o.NodeId == st.NodeId
                     && o.InstanceGroup == st.InstanceGroup
                     && o.VisitedFamilyInstanceId == guild.Id))
        {
            await RemoveStatueFromViewAsync(o, template, ct);
            InjectHallStatue(o, row, slots);
        }
        await BroadcastFamilyTextIndexAsync(st, guild.Id, 0xb4e, ct);
        await SendTextIndexAsync(session, 0x38a, ct);
        EdenLog.Write($"[Zone-Family] Statue posée char={st.CharId} " +
            $"target={targetId} pos={position} tpl={template}");
    }

    /// <summary>Template ids des équipements portés — locs natifs
    /// {1..8, 10} (SetStatue @0x81099a0 remplit un vec u16[11] indexé
    /// par loc et n'en retient que vec[1..8]) ; le conteneur
    /// d'apparence (groupe 1, equipment2) est prioritaire item par
    /// item sur l'équipé (groupe 0) — merge natif char+0x4fc.</summary>
    private async Task<Dictionary<int, int>> LoadStatueEquipsAsync(
        int charId)
    {
        var map = new Dictionary<int, int>();
        foreach (var loc in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10 })
        {
            var e = await _services.InventoryRepo
                .GetEquipmentItemAsync(charId, 1, loc)
                ?? await _services.InventoryRepo
                    .GetEquipmentItemAsync(charId, 0, loc);
            if (e != null) map[loc] = e.ItemId;
        }
        return map;
    }

    /// <summary>Retire la statue du slot de la vue d'un visiteur —
    /// 269 si elle était annoncée (natif : BornStatue tue l'entité
    /// existante au même uid avant de renaître).</summary>
    private static async Task RemoveStatueFromViewAsync(EntryState o,
        int template, CancellationToken ct)
    {
        var h = StatueHandle(template);
        o.Replayed.TryRemove(h, out _);
        if (o.Session == null || !o.Sent.TryRemove(h, out _)) return;
        o.EntityPos.TryRemove(h, out _);
        try
        {
            await o.Session.SendPacketAsync(269,
                BitConverter.GetBytes(h), ct);
        }
        catch { /* session morte */ }
    }

    /// <summary>Retrait d'une statue pour tous les visiteurs du hall
    /// de la famille (despawn du BornStatue natif sur nom vidé).</summary>
    private async Task DespawnHallStatueAsync(EntryState st,
        int familyId, int template, CancellationToken ct)
    {
        foreach (var o in _states.Values.Where(o => o.Migrated
                     && o.NodeId == st.NodeId
                     && o.InstanceGroup == st.InstanceGroup
                     && o.VisitedFamilyInstanceId == familyId))
            await RemoveStatueFromViewAsync(o, template, ct);
    }

    /// <summary>Broadcast d'un index de texte à tous les membres de la
    /// famille en ligne — BroadCastTextIndexToFamilyMember natif.</summary>
    private async Task BroadcastFamilyTextIndexAsync(EntryState st,
        int familyId, short index, CancellationToken ct)
    {
        var (op, payload) = PreparePacket(new CNC_CZ_ClientTextOutputIndex
        { Field1 = index, Field2 = "" });
        foreach (var o in _states.Values.Where(o => o.Migrated))
        {
            if (_services.Guilds.GetGuildOf((int)o.CharId)?.Id
                    != familyId || o.Session == null) continue;
            await SendPreparedToAsync(o.Session, op, payload, ct);
        }
    }
}
