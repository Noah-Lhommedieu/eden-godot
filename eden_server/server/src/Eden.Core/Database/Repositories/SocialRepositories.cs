using Dapper;
using Npgsql;
using Eden.Core.Game.Family;

namespace Eden.Core.Database.Repositories;

/// <summary>Persistance guildes, amis, couples, familiers, raccourcis.</summary>
public sealed class SocialRepository
{
    private readonly NpgsqlDataSource _db;

    public SocialRepository(NpgsqlDataSource db)
        => _db = db;

    private NpgsqlConnection Open() => _db.CreateConnection();

    // ---------- Guildes (tables natives `family` +                  ----------
    // ---------- `player_characters.family_id` / `family_cadre_`     ----------
    // ---------- `members` — le monde en est propriétaire ; la zone  ----------
    // ---------- ne fait que lire au boot et débiter la trésorerie). ----------

    /// <summary>Ligne famille — sous-ensemble du SELECT natif
    /// <c>CFamilyDBO::LoadFamily</c> : <c>leader_name</c> via join sur
    /// <c>player_characters</c> (mission : <c>CFamily+0x14</c> = nom
    /// de l'exec du leader), <c>titles</c> = les 9 titres
    /// (member_title, leader_title, cadre_title_2..8 — vecteur
    /// <c>fam+0x124</c> natif).</summary>
    public sealed record GuildRow(int Id, string Name, int LeaderId,
        string LeaderName, int Level, long Experience, long Treasury,
        int Emblem, short HouseOpen, short BusinessTax,
        string[] Titles, int[] Privileges);
    /// <summary>Ligne membre — <c>name</c> = given_name,
    /// <c>level</c>/<c>node_id</c> = exec+0x5c/+0x30 natifs,
    /// <c>quit</c> = exec+0xac (filtre `!= 1` de la requête de
    /// membres de ville 175). <b>Tous les champs numériques sont
    /// <c>int</c></b> : les colonnes remontent en int4 PostgreSQL →
    /// <c>System.Int32</c> et le mapper de constructeur Dapper exige
    /// la signature exacte — des paramètres <c>short</c>/<c>long</c>
    /// faisaient échouer la matérialisation
    /// (<c>InvalidOperationException</c>, cache guilde vide — bug
    /// live 2026-10-26).</summary>
    public sealed record GuildMemberRow(int GuildId, int CharacterId,
        string Name, int Rank, int Contribution, int Level,
        int NodeId, int Quit);

    /// <summary>Familles actives — <c>family WHERE delete_time = 0</c>
    /// (le natif marque la dissolution par un timestamp).
    /// <c>house_open</c> = node du hall de famille (0/501/502).</summary>
    public async Task<IReadOnlyList<GuildRow>> GetGuildsAsync()
    {
        using var db = Open();
        var rows = await db.QueryAsync(
            "SELECT f.id, f.name, f.leader_id, " +
            "COALESCE(p.given_name, '') AS leader_name, " +
            "f.lv AS level, f.exp AS experience, f.treasury, f.emblem, " +
            "f.house_open, f.business_tax, f.member_title, " +
            "f.leader_title, f.cadre_title_2, f.cadre_title_3, " +
            "f.cadre_title_4, f.cadre_title_5, f.cadre_title_6, " +
            "f.cadre_title_7, f.cadre_title_8, " +
            // Ordre du SELECT natif LoadFamily (mission) : le vecteur
            // fam+0xe8 est indexé dans cet ordre —
            // 0=channel,1=invite,2=kickout,3=editinfo,4=setcadre,
            // 5=invite25r,6=fixequip,7=msgboard,8=delmsg,9=treasury,
            // 10=repair,11=storage01,12=storage02,13=storage03.
            "f.channel_privilege, f.invite_privilege, " +
            "f.kickout_privilege, f.editinfo_privilege, " +
            "f.setcadre_privilege, f.invite25r_privilege, " +
            "f.fixequip_privilege, f.msgboard_privilege, " +
            "f.delmsg_privilege, f.treasury_privilege, " +
            "f.repair_privilege, f.storage01_privilege, " +
            "f.storage02_privilege, f.storage03_privilege " +
            "FROM family f " +
            "LEFT JOIN player_characters p ON p.id = f.leader_id " +
            "WHERE f.delete_time = 0");
        return rows.Select(r =>
        {
            var d = (IDictionary<string, object?>)r;
            string S(string c) => (string)(d[c] ?? "");
            return new GuildRow(
                Convert.ToInt32(d["id"]), S("name"),
                Convert.ToInt32(d["leader_id"]),
                S("leader_name"), Convert.ToInt32(d["level"]),
                Convert.ToInt64(d["experience"]),
                Convert.ToInt64(d["treasury"]),
                Convert.ToInt32(d["emblem"]),
                Convert.ToInt16(d["house_open"]),
                Convert.ToInt16(d["business_tax"]),
                [S("member_title"), S("leader_title"),
                 S("cadre_title_2"), S("cadre_title_3"),
                 S("cadre_title_4"), S("cadre_title_5"),
                 S("cadre_title_6"), S("cadre_title_7"),
                 S("cadre_title_8")],
                [Convert.ToInt32(d["channel_privilege"]),
                 Convert.ToInt32(d["invite_privilege"]),
                 Convert.ToInt32(d["kickout_privilege"]),
                 Convert.ToInt32(d["editinfo_privilege"]),
                 Convert.ToInt32(d["setcadre_privilege"]),
                 Convert.ToInt32(d["invite25r_privilege"]),
                 Convert.ToInt32(d["fixequip_privilege"]),
                 Convert.ToInt32(d["msgboard_privilege"]),
                 Convert.ToInt32(d["delmsg_privilege"]),
                 Convert.ToInt32(d["treasury_privilege"]),
                 Convert.ToInt32(d["repair_privilege"]),
                 Convert.ToInt32(d["storage01_privilege"]),
                 Convert.ToInt32(d["storage02_privilege"]),
                 Convert.ToInt32(d["storage03_privilege"])]);
        }).ToList();
    }

    /// <summary>Membres — <c>player_characters.family_id</c> + rang
    /// cadre natif (<c>family_cadre_members</c>, 0 par défaut). Tri par
    /// charId croissant comme la map native <c>fam+0xf8</c>.</summary>
    public async Task<IReadOnlyList<GuildMemberRow>> GetGuildMembersAsync(
        int guildId)
    {
        using var db = Open();
        return (await db.QueryAsync<GuildMemberRow>(
            "SELECT c.family_id AS guild_id, c.id AS character_id, " +
            "c.given_name AS name, COALESCE(cm.rank, 0) AS rank, " +
            "c.family_contribution_point AS contribution, " +
            "c.level, c.node_id, c.quit " +
            "FROM player_characters c " +
            "LEFT JOIN family_cadre_members cm " +
            "  ON cm.family_id = c.family_id AND cm.player_id = c.id " +
            "WHERE c.family_id = @guildId AND c.deleted_time = 0 " +
            "ORDER BY c.id",
            new { guildId })).AsList();
    }

    /// <summary>Création directe — utilisée par les tests/outils ; le
    /// flux client passe par ZW FamilyCreation (monde).</summary>
    public async Task<int> CreateGuildAsync(string name, int leaderId)
    {
        using var db = Open();
        var id = await db.ExecuteScalarAsync<int>(
            "INSERT INTO family (id, name, leader_id, be_create_time, lv) " +
            "VALUES ((SELECT COALESCE(MAX(id),0)+1 FROM family), " +
            "@name, @leaderId, extract(epoch from now())::int, 1) " +
            "RETURNING id", new { name, leaderId });
        await AddGuildMemberAsync(id, leaderId, 0);
        return id;
    }

    /// <summary>Adhésion — <c>player_characters.family_id</c> +
    /// rang cadre éventuel.</summary>
    public async Task AddGuildMemberAsync(int guildId, int characterId, short rank)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE player_characters SET family_id = @guildId, " +
            "add_family_time = extract(epoch from now())::int " +
            "WHERE id = @characterId",
            new { guildId, characterId });
        if (rank > 0)
            await db.ExecuteAsync(
                "INSERT INTO family_cadre_members " +
                "(family_id, player_id, rank) " +
                "VALUES (@guildId,@characterId,@rank) " +
                "ON CONFLICT (family_id, player_id) " +
                "DO UPDATE SET rank = @rank",
                new { guildId, characterId, rank });
    }

    /// <summary>Départ — <c>family_id = 0</c> + retrait du rang
    /// cadre.</summary>
    public async Task RemoveGuildMemberAsync(int guildId, int characterId)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE player_characters SET family_id = 0 " +
            "WHERE id = @characterId AND family_id = @guildId",
            new { guildId, characterId });
        await db.ExecuteAsync(
            "DELETE FROM family_cadre_members WHERE family_id=@guildId " +
            "AND player_id=@characterId",
            new { guildId, characterId });
    }

    /// <summary>Mise à jour trésorerie/niveau — <c>family</c>
    /// (lv, exp, treasury, emblem).</summary>
    public async Task UpdateGuildAsync(int guildId, int level, long experience,
        long treasury, int emblem)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family SET lv=@level, exp=@experience, " +
            "treasury=@treasury, emblem=@emblem WHERE id=@guildId",
            new { guildId, level, experience, treasury, emblem });
    }

    /// <summary>Lecture du conteneur bâtiments d'un hall de famille —
    /// SELECT natif <c>CFamilyDBO::LoadFamilyBuilding</c> (mission
    /// @0x817c2b0) sur <c>family_building_container</c>. La zone s'en
    /// sert pour émettre <c>ClientFamilyInstanceData</c> (487) ; les
    /// lignes sont triées par <c>loc</c> comme la map native.</summary>
    public async Task<List<FamilyBuildingItem>> GetFamilyBuildingsAsync(
        int familyId, short containerIndex)
    {
        using var db = Open();
        var rows = await db.QueryAsync<BuildingRow>(
            "SELECT id, item_tid, tid, item_type, durability, " +
            "container_index, loc, x, y, face_dir, level, exp, due_date " +
            "FROM family_building_container " +
            "WHERE family_id = @familyId " +
            "AND container_index = @containerIndex ORDER BY loc",
            new { familyId, containerIndex });
        return rows.Select(r => new FamilyBuildingItem
        {
            Id = r.id, ItemTid = (short)r.item_tid, Tid = r.tid,
            ItemType = (short)r.item_type, Durability = (short)r.durability,
            ContainerIndex = r.container_index, Loc = r.loc,
            X = (float)r.x, Y = (float)r.y, FaceDir = (float)r.face_dir,
            Level = (short)r.level, Exp = r.exp, DueDate = r.due_date,
        }).ToList();
    }

    /// <summary>Fixe la durabilité d'un bâtiment du conteneur 0 —
    /// <c>CFamily::UpdateBuildingDurability</c> mission natif
    /// (TC_FamilyBuildingRepair @0x81ef3d0 : remet la durabilité au
    /// max du template après débit de la trésorerie).</summary>
    public async Task SetFamilyBuildingDurabilityAsync(int familyId,
        short loc, short durability)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family_building_container " +
            "SET durability = @durability " +
            "WHERE family_id = @familyId AND container_index = 0 " +
            "AND loc = @loc",
            new { familyId, loc, durability });
    }

    /// <summary>Usure/maj de durabilité d'un bâtiment par son uid
    /// (colonne <c>id</c> — cible de <c>GetBuildingItemByItemNetID</c>
    /// natif, work+0x2c). Appelé par l'usure des stations de travail
    /// (<c>SFamily::UpdateBuildingDurability</c> →
    /// <c>CFamily::UpdateBuildingDurability</c> mission).</summary>
    public async Task UpdateBuildingDurabilityByIdAsync(int familyId,
        int id, short durability)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family_building_container " +
            "SET durability = @durability " +
            "WHERE family_id = @familyId AND id = @id",
            new { familyId, id, durability });
    }

    /// <summary>Level-up d'un bâtiment — <c>CFamily::UpdateBuildingExp</c>
    /// mission : écrit ensemble <c>tid</c> (template suivant),
    /// <c>level</c> (col4 du nouveau template) et <c>exp</c> (reste
    /// après soustraction du seuil) en un UPDATE.</summary>
    public async Task UpdateBuildingProgressAsync(int familyId, int id,
        int tid, short level, int exp)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family_building_container " +
            "SET tid = @tid, level = @level, exp = @exp " +
            "WHERE family_id = @familyId AND id = @id",
            new { familyId, id, tid, level = (int)level, exp });
    }

    /// <summary>Insertion d'un item dans un conteneur de hall —
    /// <c>CItemDBO::PutItemInFamilyBuilding</c> natif (appelé par
    /// <c>CItemFactory::FamilyBuildingPutItemFromInventroy</c>
    /// @0x808a770 après <c>EradicateItem</c>). L'id est alloué par
    /// <see cref="ItemSerialAllocator"/> (espace global natif).</summary>
    public async Task<int> InsertFamilyBuildingItemAsync(
        FamilyBuildingItem item, int familyId)
    {
        var id = await ItemSerialAllocator.NextAsync(_db);
        using var db = Open();
        await db.ExecuteAsync(
            "INSERT INTO family_building_container " +
            "(id, family_id, item_tid, tid, item_type, durability, " +
            "container_index, loc, x, y, face_dir, level, exp, " +
            "due_date) VALUES " +
            "(@id, @familyId, @itemTid, @tid, @itemType, " +
            "@durability, @containerIndex, @loc, @x, @y, @faceDir, " +
            "@level, @exp, @dueDate)",
            new
            {
                id, familyId,
                // item_tid = u16 natif (mission +0xc) — colonne integer
                // PG : on stocke la valeur non signée, la relecture
                // (short) récupère les mêmes 16 bits.
                itemTid = (int)(ushort)item.ItemTid, tid = item.Tid,
                itemType = (int)item.ItemType,
                durability = (int)item.Durability,
                containerIndex = (int)item.ContainerIndex,
                loc = (int)item.Loc, x = item.X, y = item.Y,
                faceDir = item.FaceDir, level = (int)item.Level,
                exp = item.Exp, dueDate = item.DueDate,
            });
        return id;
    }

    /// <summary>Suppression d'un item du conteneur de hall —
    /// <c>CItemDBO::EradicateItem</c>/<c>DeleteFamilyBuildingItem</c>
    /// natif (retrait vers l'inventaire, NCID 131).</summary>
    public async Task DeleteFamilyBuildingItemAsync(int familyId,
        short containerIndex, short loc)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "DELETE FROM family_building_container " +
            "WHERE family_id = @familyId " +
            "AND container_index = @containerIndex AND loc = @loc",
            new { familyId, containerIndex = (int)containerIndex,
                  loc = (int)loc });
    }

    /// <summary>Dissolution — <c>family.delete_time</c> + sortie de
    /// tous les membres + retrait des rangs cadre (miroir du
    /// TC_DismissFamily natif).</summary>
    public async Task DisbandGuildAsync(int guildId)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family SET delete_time = " +
            "extract(epoch from now())::int WHERE id = @guildId",
            new { guildId });
        await db.ExecuteAsync(
            "UPDATE player_characters SET family_id = 0, " +
            "family_comment = '', family_contribution_point = 0, " +
            "family_msgboard_state = 0 WHERE family_id = @guildId",
            new { guildId });
        await db.ExecuteAsync(
            "DELETE FROM family_cadre_members WHERE family_id = @guildId",
            new { guildId });
    }

    /// <summary>Ouverture du hall de famille — SQL natif
    /// <c>CFamilyDBO::UpdateInstanceNodeID</c> (mission @0x8179430) :
    /// <c>UPDATE family SET house_open = %hd WHERE id = %d</c>
    /// (node = 0/501/502).</summary>
    public async Task SetGuildHouseOpenAsync(int guildId, short node)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family SET house_open = @node WHERE id = @guildId",
            new { guildId, node });
    }

    /// <summary>Niveau d'un item du conteneur 0 — colonne
    /// <c>family_building_container.level</c> (CFamilyItem+0x98 natif,
    /// le niveau de bâtiment qui borne le cap de NPCs via
    /// <c>CheckNpcSizeLimit</c>).</summary>
    public async Task SetFamilyBuildingLevelAsync(int familyId,
        short loc, short level)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family_building_container SET level = @level " +
            "WHERE family_id = @familyId AND container_index = 0 " +
            "AND loc = @loc",
            new { familyId, loc, level });
    }

    /// <summary>Remplace le contenu d'un slot du conteneur 0 (NPC de
    /// hall) — met à jour <c>item_tid</c> (l'item contrat) et
    /// <c>tid</c> (le template S_Npc spawné) en conservant
    /// loc/position/orientation.</summary>
    public async Task UpdateFamilyBuildingItemAsync(int familyId,
        short loc, int itemTid, int tid)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE family_building_container " +
            "SET item_tid = @itemTid, tid = @tid " +
            "WHERE family_id = @familyId AND container_index = 0 " +
            "AND loc = @loc",
            new { familyId, loc, itemTid, tid });
    }

    /// <summary>Conteneur 0 des bâtiments pour UN ENSEMBLE de familles
    /// — utilisé par la recherche de ville (174) qui itère toutes les
    /// familles instanciées en une seule requête.</summary>
    public async Task<Dictionary<int, List<FamilyBuildingItem>>>
        GetFamilyBuildingsAsync(IReadOnlyCollection<int> familyIds)
    {
        var map = new Dictionary<int, List<FamilyBuildingItem>>();
        if (familyIds.Count == 0) return map;
        using var db = Open();
        var rows = await db.QueryAsync<BuildingRow>(
            "SELECT family_id, id, item_tid, tid, item_type, " +
            "durability, container_index, loc, x, y, face_dir, level, " +
            "exp, due_date FROM family_building_container " +
            "WHERE container_index = 0 AND family_id = ANY(@familyIds) " +
            "ORDER BY loc",
            new { familyIds = familyIds.ToArray() });
        foreach (var r in rows)
        {
            var list = map.TryGetValue(r.family_id, out var l)
                ? l : map[r.family_id] = new List<FamilyBuildingItem>();
            list.Add(new FamilyBuildingItem
            {
                Id = r.id, ItemTid = (short)r.item_tid, Tid = r.tid,
                ItemType = (short)r.item_type,
                Durability = (short)r.durability,
                ContainerIndex = r.container_index, Loc = r.loc,
                X = (float)r.x, Y = (float)r.y, FaceDir = (float)r.face_dir,
                Level = (short)r.level, Exp = r.exp, DueDate = r.due_date,
            });
        }
        return map;
    }

    // ---- Statues de famille (statues_scenes — NCID 161) ----
    // Schéma natif : PK (worldserver_id, node_id, template_id,
    // family_id) ; la position n'est PAS en DB — elle vient du slot
    // [npc] de la scène (GetStatueInThisServer @0x8370ca0).
    // CStatueDBO::LoadStatueFromDB @0x8370080 (SELECT) +
    // CStatueDBO::SaveStatueToDB @0x836ff30 (commande UpdateStatue
    // → CExecutionerDBO::UpdateStatue mission @0x81620e0 :
    // INSERT si absent puis UPDATE).

    /// <summary>Ligne <c>statues_scenes</c> — apparence persistée de
    /// la statue d'un membre (clone du perso au moment de la pose).</summary>
    public sealed record StatueRow(int TemplateId, string PlayerName,
        int GenderId, int Face, int Hair, int WeaponActive, int ClassId,
        int Style, int HideEquip, int ActionId, double FrameKey,
        int EquipHead, int EquipChest, int EquipPants, int EquipGlove,
        int EquipFeet, int EquipBack, int EquipLeft, int EquipRight,
        int EquipShoot, int RideId, int TransformId)
    {
        /// <summary>Statue active — le natif supprime en vidant le
        /// nom (position 0 de 161), la ligne reste.</summary>
        public bool HasName => PlayerName.Length > 0;
    }

    /// <summary>Statues d'une famille sur un node — SELECT natif par
    /// (world, node, template) élargi à la famille entière (nos
    /// visiteurs du hall filtré par famille, pas par node
    /// d'instance).</summary>
    public async Task<IReadOnlyList<StatueRow>> GetStatuesAsync(
        int worldId, int nodeId, int familyId)
    {
        using var db = Open();
        var rows = await db.QueryAsync<StatueRowD>(
            "SELECT template_id, player_name, gender_id, face, hair, " +
            "weapon_active, class_id, style, hide_equip, action_id, " +
            "frame_key, equip_head, equip_chest, equip_pants, " +
            "equip_glove, equip_feet, equip_back, equip_left, " +
            "equip_right, equip_shoot, ride_id, transform_id " +
            "FROM statues_scenes WHERE worldserver_id = @worldId " +
            "AND node_id = @nodeId AND family_id = @familyId",
            new { worldId, nodeId, familyId });
        return rows.Select(r => new StatueRow(r.template_id,
            r.player_name ?? "", r.gender_id, r.face, r.hair,
            r.weapon_active, r.class_id, r.style, r.hide_equip,
            r.action_id, r.frame_key, r.equip_head, r.equip_chest,
            r.equip_pants, r.equip_glove, r.equip_feet, r.equip_back,
            r.equip_left, r.equip_right, r.equip_shoot, r.ride_id,
            r.transform_id)).ToList();
    }

    /// <summary>Pose/maj d'une statue — INSERT si absente puis UPDATE
    /// (UpdateStatue mission natif @0x81620e0). <paramref name="name"/>
    /// vide = retrait (position 0 de 161).</summary>
    public async Task SaveStatueAsync(int worldId, int nodeId,
        int templateId, int familyId, StatueRow s)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "INSERT INTO statues_scenes (worldserver_id, node_id, " +
            "template_id, family_id, player_name, gender_id, face, " +
            "hair, weapon_active, class_id, style, hide_equip, " +
            "action_id, frame_key, equip_head, equip_chest, " +
            "equip_pants, equip_glove, equip_feet, equip_back, " +
            "equip_left, equip_right, equip_shoot, ride_id, " +
            "transform_id) VALUES (@worldId, @nodeId, @templateId, " +
            "@familyId, @name, @gender, @face, @hair, @weapon, " +
            "@classId, @style, @hide, @action, @frame, @head, " +
            "@chest, @pants, @glove, @feet, @back, @left, @right, " +
            "@shoot, @ride, @transform) ON CONFLICT (worldserver_id, " +
            "node_id, template_id, family_id) DO UPDATE SET " +
            "player_name = @name, gender_id = @gender, face = @face, " +
            "hair = @hair, weapon_active = @weapon, " +
            "class_id = @classId, style = @style, hide_equip = @hide, " +
            "action_id = @action, frame_key = @frame, " +
            "equip_head = @head, equip_chest = @chest, " +
            "equip_pants = @pants, equip_glove = @glove, " +
            "equip_feet = @feet, equip_back = @back, " +
            "equip_left = @left, equip_right = @right, " +
            "equip_shoot = @shoot, ride_id = @ride, " +
            "transform_id = @transform",
            new
            {
                worldId, nodeId, templateId, familyId,
                name = s.PlayerName, gender = s.GenderId, face = s.Face,
                hair = s.Hair, weapon = s.WeaponActive,
                classId = s.ClassId, style = s.Style, hide = s.HideEquip,
                action = s.ActionId, frame = s.FrameKey,
                head = s.EquipHead, chest = s.EquipChest,
                pants = s.EquipPants, glove = s.EquipGlove,
                feet = s.EquipFeet, back = s.EquipBack,
                left = s.EquipLeft, right = s.EquipRight,
                shoot = s.EquipShoot, ride = s.RideId,
                transform = s.TransformId,
            });
    }

    // ---- Territoires (territory_war — NCIDs 189/190/225) ----
    // Schéma natif : territory_id PK, own_family_id (0 = libre),
    // own_times (victoires consécutives) — chargé par
    // CTerritoryQuery::LoadDB mission ("SELECT territory_id,
    // own_family_id, own_times FROM territory_war").

    /// <summary>Ligne <c>territory_war</c> : propriété courante d'un
    /// territoire de guerre (les ids 301+ des family battles n'ont pas
    /// de ligne — propriétaire toujours 0).</summary>
    public sealed record TerritoryWarRow(int TerritoryId,
        int OwnFamilyId, int OwnTimes);

    /// <summary>Tous les territoires persistés — équivalent du
    /// <c>SELECT territory_id, own_family_id, own_times FROM
    /// territory_war</c> natif (init CTerritoryQuery mission).</summary>
    public async Task<IReadOnlyList<TerritoryWarRow>>
        GetTerritoryWarsAsync()
    {
        using var db = Open();
        return (await db.QueryAsync<TerritoryWarRow>(
            "SELECT territory_id, own_family_id, own_times " +
            "FROM territory_war ORDER BY territory_id")).AsList();
    }

    /// <summary>Ligne <c>familybattle_ranks</c> : classement par phase
    /// d'un round d'élimination de familles (unique
    /// family_id+phase_type+phase_index) — table native chargée par
    /// <c>CMissionDBO::LoadFamilyBattleRank</c>.</summary>
    public sealed record FamilyBattleRankRow(int FamilyId,
        string FamilyName, int FamilyHonor, int PhaseType,
        int PhaseIndex, int Energy);

    /// <summary>Classements d'élimination ordonnés par phase puis
    /// index — alimente le 561 type 0 (paires nom+énergie itérées par
    /// <c>TC_FamilyBattleJudgeQuery</c> @0x820a1b0).</summary>
    public async Task<IReadOnlyList<FamilyBattleRankRow>>
        GetFamilyBattleRanksAsync()
    {
        using var db = Open();
        return (await db.QueryAsync<FamilyBattleRankRow>(
            "SELECT family_id, family_name, family_honor, " +
            "phase_type, phase_index, energy FROM familybattle_ranks " +
            "ORDER BY phase_type, phase_index")).AsList();
    }

    /// <summary>Persiste le vainqueur d'une guerre de territoire —
    /// équivalent de la mise à jour <c>territory_war</c> faite par la
    /// fin de guerre native (own_family_id = famille gagnante,
    /// own_times incrémenté).</summary>
    public async Task UpdateTerritoryWarAsync(int territoryId,
        int ownFamilyId, int ownTimes)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "INSERT INTO territory_war " +
            "(territory_id, own_family_id, own_times) " +
            "VALUES (@tid, @fam, @times) " +
            "ON DUPLICATE KEY UPDATE own_family_id = @fam, " +
            "own_times = @times",
            new { tid = territoryId, fam = ownFamilyId,
                times = ownTimes });
    }

    /// <summary>Ligne brute statues_scenes pour Dapper.</summary>
    private sealed class StatueRowD
    {
        public int template_id { get; set; }
        public string? player_name { get; set; }
        public int gender_id { get; set; }
        public int face { get; set; } public int hair { get; set; }
        public int weapon_active { get; set; }
        public int class_id { get; set; } public int style { get; set; }
        public int hide_equip { get; set; }
        public int action_id { get; set; } public double frame_key { get; set; }
        public int equip_head { get; set; } public int equip_chest { get; set; }
        public int equip_pants { get; set; } public int equip_glove { get; set; }
        public int equip_feet { get; set; } public int equip_back { get; set; }
        public int equip_left { get; set; } public int equip_right { get; set; }
        public int equip_shoot { get; set; }
        public int ride_id { get; set; } public int transform_id { get; set; }
    }

    /// <summary>Ligne brute <c>family_building_container</c> — mêmes
    /// colonnes que le SELECT natif <c>LoadFamilyBuilding</c>.</summary>
    private sealed class BuildingRow
    {
        public int family_id { get; set; }
        public int id { get; set; } public int item_tid { get; set; }
        public int tid { get; set; }
        public int item_type { get; set; } public int durability { get; set; }
        public short container_index { get; set; } public short loc { get; set; }
        public double x { get; set; } public double y { get; set; }
        public double face_dir { get; set; }
        public int level { get; set; } public int exp { get; set; }
        public int due_date { get; set; }
    }

    // ---------- Amis ----------

    /// <summary>Une ligne de la table native <c>friends</c> —
    /// <c>friend_type</c> : 0 = blacklist, 1 = j'ai ajouté l'autre,
    /// 2 = l'autre m'a ajouté, 3 = mutuel
    /// (<c>CExecutionerDBO::LoadFriendData</c>/AddFriendData,
    /// mission/decompiled.c).</summary>
    public sealed record FriendRelationRow(int FriendId, int FriendType);

    /// <summary>Lecture native <c>friends</c> — tous les types
    /// (mutuels, invitations pendantes ET blacklist ; le tri se fait
    /// sur <c>friend_type</c> comme le CFriend natif).</summary>
    public async Task<IReadOnlyList<FriendRelationRow>>
        GetFriendRelationsAsync(int characterId)
    {
        using var db = Open();
        return (await db.QueryAsync<FriendRelationRow>(
            "SELECT friend_id, friend_type FROM friends " +
            "WHERE player_id=@characterId",
            new { characterId })).AsList();
    }

    /// <summary>Type de relation (player_id → friend_id) — NULL si
    /// pas de ligne. Équivalent <c>CExecutionerDBO::GetFriendType</c>
    /// natif (lookup DB quand la cible n'est pas chargée).</summary>
    public async Task<int?> GetFriendTypeAsync(int playerId, int friendId)
    {
        using var db = Open();
        return await db.QueryFirstOrDefaultAsync<int?>(
            "SELECT friend_type FROM friends " +
            "WHERE player_id=@playerId AND friend_id=@friendId",
            new { playerId, friendId });
    }

    /// <summary>Upsert <c>friends</c> — équivalent AddFriendData +
    /// UpdateFriendData natifs (INSERT puis UPDATE au changement de
    /// type, ex. 1→3 quand l'invitation devient mutuelle).</summary>
    public async Task SetFriendTypeAsync(int playerId, int friendId,
        int type)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "INSERT INTO friends (player_id, friend_id, friend_type) " +
            "VALUES (@playerId,@friendId,@type) " +
            "ON CONFLICT (player_id, friend_id) " +
            "DO UPDATE SET friend_type=@type",
            new { playerId, friendId, type });
    }

    /// <summary>Supprime UNE direction — <c>DeleteFriendData</c> natif
    /// (le RemoveMember bilatéral est fait par deux appels).</summary>
    public async Task DeleteFriendAsync(int playerId, int friendId)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "DELETE FROM friends WHERE player_id=@playerId " +
            "AND friend_id=@friendId",
            new { playerId, friendId });
    }

    // ---------- Couples ----------

    public sealed record CoupleRow(int Id, int Partner1Id, int Partner2Id,
        int Affection, DateTime? WeddingDate);

    public async Task<CoupleRow?> GetCoupleAsync(int characterId)
    {
        using var db = Open();
        return await db.QuerySingleOrDefaultAsync<CoupleRow>(
            "SELECT id, partner1_id, partner2_id, affection, wedding_date " +
            "FROM player_couples WHERE partner1_id=@characterId " +
            "OR partner2_id=@characterId", new { characterId });
    }

    public async Task<int> CreateCoupleAsync(int partner1Id, int partner2Id)
    {
        using var db = Open();
        return await db.ExecuteScalarAsync<int>(
            "INSERT INTO player_couples (partner1_id, partner2_id) " +
            "VALUES (@partner1Id,@partner2Id) RETURNING id",
            new { partner1Id, partner2Id });
    }

    public async Task UpdateCoupleAsync(int coupleId, int affection,
        DateTime? weddingDate)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "UPDATE player_couples SET affection=@affection, " +
            "wedding_date=@weddingDate WHERE id=@coupleId",
            new { coupleId, affection, weddingDate });
    }

    public async Task DeleteCoupleAsync(int coupleId)
    {
        using var db = Open();
        await db.ExecuteAsync(
            "DELETE FROM player_couples WHERE id=@coupleId",
            new { coupleId });
    }

    // ---------- Raccourcis ----------

    public sealed record ShortcutRow(int CharacterId, short Page,
        short Slot, int Type, int RefId);

    /// <summary>Barres de raccourcis de la classe courante — table
    /// native `shortcut_menu` (unique character_id+page+class,
    /// index1-10 = i32 packé reçu du client, 0 = vide). Chaque classe
    /// du perso possède ses propres placements (filtre sur
    /// player_characters.class_id).</summary>
    public Task<IReadOnlyList<ShortcutRow>> GetShortcutsAsync(
        int characterId)
        => GetShortcutsAsync(characterId, null);

    /// <summary>Surcharge avec classe explicite — le client
    /// GetShortcutOfPage (55) demande une classe précise pendant le
    /// changement de classe : filtrer sur player_characters.class_id
    /// risquait de renvoyer les barres de la mauvaise classe si la
    /// requête arrivait avant l'UPDATE de la classe active.</summary>
    public async Task<IReadOnlyList<ShortcutRow>> GetShortcutsAsync(
        int characterId, int? classId)
    {
        using var db = Open();
        var cols = string.Join(", ",
            Enumerable.Range(1, 10).Select(i => $"index{i}"));
        var rows = await db.QueryAsync(
            $"SELECT page, {cols} FROM shortcut_menu " +
            "WHERE character_id=@characterId AND class = " +
            "COALESCE(@classId, (SELECT class_id FROM player_characters " +
            "WHERE id=@characterId))",
            new { characterId, classId });
        var list = new List<ShortcutRow>();
        foreach (var r in rows)
        {
            var d = (IDictionary<string, object?>)r;
            var page = Convert.ToInt16(d["page"]);
            for (var i = 0; i < 10; i++)
            {
                var v = Convert.ToInt32(d[$"index{i + 1}"]);
                // 0 = vide (natif) ; -1 = valeur stockée par les vieux
                // handlers qui persistaient le -1 "empty" du client —
                // le filtrer évite des slots fantômes 0xFFFFFFFF.
                if (v != 0 && v != -1)
                    list.Add(new ShortcutRow(characterId, page,
                        (short)i, 0, v));
            }
        }
        return list;
    }

    /// <summary>Toutes les barres par classe — `shortcut_menu` page 0
    /// de chaque classe (le 510 ClientChangeClassShortcut porte un
    /// mask u16 par classe = les 10 slots de la barre principale).
    /// Retourne classId → int[10] des i32 packés.</summary>
    public async Task<Dictionary<int, int[]>> GetAllClassShortcutsAsync(
        int characterId)
    {
        using var db = Open();
        var cols = string.Join(", ",
            Enumerable.Range(1, 10).Select(i => $"index{i}"));
        var rows = await db.QueryAsync(
            $"SELECT class, {cols} FROM shortcut_menu " +
            "WHERE character_id=@characterId AND page=0",
            new { characterId });
        var map = new Dictionary<int, int[]>();
        foreach (var r in rows)
        {
            var d = (IDictionary<string, object?>)r;
            var slots = new int[10];
            for (var i = 0; i < 10; i++)
                slots[i] = Convert.ToInt32(d[$"index{i + 1}"]);
            map[Convert.ToInt32(d["class"])] = slots;
        }
        return map;
    }

    /// <summary>Toutes les pages de raccourcis d'UNE classe —
    /// miroir du `CNE_DBA_ClientShortcut` natif qui charge
    /// `shortcut_menu` de la classe active dans char+0x684
    /// (vector&lt;vector&lt;int&gt;&gt; indexé par page, 10 i32 par
    /// page, -1 = vide). Retourne page → int[10].</summary>
    public async Task<Dictionary<short, int[]>> GetClassShortcutPagesAsync(
        int characterId, int classId)
    {
        using var db = Open();
        var cols = string.Join(", ",
            Enumerable.Range(1, 10).Select(i => $"index{i}"));
        var rows = await db.QueryAsync(
            $"SELECT page, {cols} FROM shortcut_menu " +
            "WHERE character_id=@characterId AND class=@classId " +
            "ORDER BY page",
            new { characterId, classId });
        var map = new Dictionary<short, int[]>();
        foreach (var r in rows)
        {
            var d = (IDictionary<string, object?>)r;
            var slots = new int[10];
            for (var i = 0; i < 10; i++)
                slots[i] = Convert.ToInt32(d[$"index{i + 1}"]);
            map[Convert.ToInt16(d["page"])] = slots;
        }
        return map;
    }

    /// <summary>Placement d'un raccourci — upsert natif
    /// (character_id, page, class courante) avec index{slot+1}=refId
    /// packé. `type` ignoré : la colonne native stocke le pack
    /// directement (natif : SShortcut i32).</summary>
    public async Task SetShortcutAsync(int characterId, short page, short slot,
        int type, int refId)
    {
        if (slot is < 0 or > 9) return;
        var col = $"index{slot + 1}";
        using var db = Open();
        await db.ExecuteAsync(
            "INSERT INTO shortcut_menu (character_id, page, class, " +
            $"{col}) SELECT @characterId, @page, class_id, @refId " +
            "FROM player_characters WHERE id=@characterId " +
            "ON CONFLICT (character_id,page,class) " +
            $"DO UPDATE SET {col}=@refId",
            new { characterId, page, refId });
    }

    /// <summary>Supprime le raccourci (page, slot) de la classe
    /// courante — index{slot+1} ← 0, la ligne est conservée comme
    /// dans le natif.</summary>
    public async Task DeleteShortcutAsync(int characterId, short page, short slot)
    {
        if (slot is < 0 or > 9) return;
        var col = $"index{slot + 1}";
        using var db = Open();
        await db.ExecuteAsync(
            $"UPDATE shortcut_menu SET {col}=0 " +
            "WHERE character_id=@characterId AND page=@page AND class = " +
            "(SELECT class_id FROM player_characters WHERE id=@characterId)",
            new { characterId, page });
    }

    /// <summary>Efface toutes les occurrences d'une valeur packée dans
    /// les barres de la classe courante — correspond au retrait
    /// old_field natif de CCharacter::UpdateShortcut (drag d'une icône
    /// : l'ancien slot se vide quand la valeur bouge).</summary>
    public async Task ClearShortcutValueAsync(int characterId, int packedValue)
    {
        var sets = string.Join(", ",
            Enumerable.Range(1, 10).Select(i =>
                $"index{i} = CASE WHEN index{i}=@v THEN 0 ELSE index{i} END"));
        using var db = Open();
        await db.ExecuteAsync(
            $"UPDATE shortcut_menu SET {sets} " +
            "WHERE character_id=@characterId AND class = " +
            "(SELECT class_id FROM player_characters WHERE id=@characterId)",
            new { characterId, v = packedValue });
    }

    // ---------- Coffre de famille (`family_storage`) — déplacements ----------
    // ---------- atomiques inventaire ↔ coffre. Le flux zone joignait ----------
    // ---------- deux repositories sur deux connexions (DELETE inventory ----------
    // ---------- puis INSERT family_storage / DELETE family_storage puis   ----
    // ---------- INSERT inventory1) : un crash entre les deux dupliquait ou ----
    // ---------- perdait l'item. Ici : une connexion + une transaction.    ----

    /// <summary>Dépôt coffre famille : DELETE de la ligne inventaire
    /// (deux buffers ping-pong) puis INSERT dans <c>family_storage</c>
    /// en UNE transaction — même liste de colonnes que
    /// <c>FamilyBankRepository.InsertItemAsync</c>, serial
    /// <c>item.Id</c> conservé (espace d'ids inter-tables natif).
    /// <paramref name="fromCharId"/> = propriétaire d'origine (le
    /// CharacterId de <paramref name="item"/> porte déjà le
    /// family_id). Retourne false si la ligne source n'existait plus
    /// — rollback intégral (rien n'est écrit).</summary>
    public async Task<bool> MoveItemToFamilyStorageAsync(int fromCharId,
        Eden.Core.Game.Inventory.ItemEntity item)
    {
        using var db = Open();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            // 1. Inventaire source — verrous pris d'abord (ordre
            //    déterministe : inventory1/2 puis family_storage).
            var removed = 0;
            foreach (var table in new[] { "inventory1", "inventory2" })
            {
                removed = await db.ExecuteAsync(
                    $"DELETE FROM {table} WHERE id=@Id AND player_id=@C",
                    new { Id = item.Id, C = fromCharId }, tx);
                if (removed > 0) break;
            }
            if (removed == 0)
            {
                await tx.RollbackAsync();
                return false;
            }

            // 2. Coffre destination — colonnes natives complètes.
            const string sql = @"
                INSERT INTO family_storage
                    (id, item_id, durability, maker, identify,
                     embedded_amount, embedded_id1, embedded_id2,
                     embedded_id3, embedded_id4, embedded_id5,
                     embedded_id6, loc, create_time, due_date, combo_id,
                     strengthen, cur_maxdurability, bind, family_id,
                     container_index,
                     extra_combo_id1, extra_combo_id2, extra_combo_id3,
                     extra_combo_id4, extra_combo_id5, extra_combo_id6,
                     extra_combo_id7, extra_combo_id8, extra_combo_id9,
                     witchcraft, quality, plus_parameter, color,
                     train_level, train_exp,
                     train_slot1_type, train_slot1_level,
                     train_slot2_type, train_slot2_level,
                     train_slot3_type, train_slot3_level,
                     extra_combo_id10, extra_combo_id11, gold_enchant,
                     magicbox_enchant1, magicbox_enchant2,
                     magicbox_enchant3,
                     magicbox_energy, magicbox_starlevel, unbind)
                VALUES
                    (@Id, @ItemId, @Durability, @Maker, @Identify,
                     @EmbeddedAmount, @EmbeddedId1, @EmbeddedId2,
                     @EmbeddedId3, @EmbeddedId4, @EmbeddedId5,
                     @EmbeddedId6, @Slot, @CreateTime, @DueDateInt,
                     @Combo, @EnchantLevel, @MaxDurability, @BindInt,
                     @CharacterId, @BagId,
                     @Ec1, @Ec2, @Ec3, @Ec4, @Ec5, @Ec6, @Ec7, @Ec8,
                     @Ec9, @Witchcraft, @Quality, @PlusParameter, @Color,
                     @TrainLevel, @TrainExp,
                     @TrainSlot1Type, @TrainSlot1Level,
                     @TrainSlot2Type, @TrainSlot2Level,
                     @TrainSlot3Type, @TrainSlot3Level,
                     @Ec10, @Ec11, @GoldEnchant,
                     @MagicBoxEnchant1, @MagicBoxEnchant2,
                     @MagicBoxEnchant3,
                     @MagicBoxEnergy, @MagicBoxStarLevel, 0)";
            await db.ExecuteAsync(sql, new
            {
                item.Id, item.ItemId, item.Durability, item.Maker,
                item.Identify,
                item.EmbeddedAmount,
                item.EmbeddedId1, item.EmbeddedId2, item.EmbeddedId3,
                item.EmbeddedId4, item.EmbeddedId5, item.EmbeddedId6,
                item.Slot, item.CreateTime, DueDateInt = item.DueDate,
                item.Combo, item.EnchantLevel, item.MaxDurability,
                BindInt = item.IsBound ? 1 : 0,
                item.CharacterId, item.BagId,
                Ec1 = item.ExtraCombos[0], Ec2 = item.ExtraCombos[1],
                Ec3 = item.ExtraCombos[2], Ec4 = item.ExtraCombos[3],
                Ec5 = item.ExtraCombos[4], Ec6 = item.ExtraCombos[5],
                Ec7 = item.ExtraCombos[6], Ec8 = item.ExtraCombos[7],
                Ec9 = item.ExtraCombos[8],
                item.Witchcraft, item.Quality, item.PlusParameter,
                item.Color, item.TrainLevel, item.TrainExp,
                item.TrainSlot1Type, item.TrainSlot1Level,
                item.TrainSlot2Type, item.TrainSlot2Level,
                item.TrainSlot3Type, item.TrainSlot3Level,
                Ec10 = item.ExtraComboId10, Ec11 = item.ExtraComboId11,
                item.GoldEnchant,
                item.MagicBoxEnchant1, item.MagicBoxEnchant2,
                item.MagicBoxEnchant3,
                item.MagicBoxEnergy, item.MagicBoxStarLevel,
            }, tx);
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Retrait coffre famille : DELETE de la ligne
    /// <c>family_storage</c> (garde <c>id</c> — 0 ligne si un autre
    /// membre a retiré entre-temps → rollback) puis INSERT dans
    /// <c>inventory1</c> en UNE transaction. Nouveau serial alloué
    /// comme <c>InventoryRepository.AddItemAsync</c> (comportement
    /// conservé : <paramref name="item"/> reçoit le nouvel id).
    /// <paramref name="item"/> est déjà re-ciblée
    /// (CharacterId/BagId/Slot inventaire). Retourne false si la
    /// ligne coffre a disparu.</summary>
    public async Task<bool> MoveFamilyStorageItemToInventoryAsync(
        Eden.Core.Game.Inventory.ItemEntity item)
    {
        using var db = Open();
        await db.OpenAsync();
        var oldId = item.Id;
        // Séquence item_serial (migration 012) — atomique
        // cross-process ; un serial consommé puis rollback laisse
        // juste un trou d'ids, sans conséquence.
        item.Id = await ItemSerialAllocator.NextAsync(_db);
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            // 1. Coffre source d'abord (family_storage puis
            //    inventory1 — ordre déterministe par rapport au dépôt :
            //    les deux chemins suppriment la source avant d'écrire
            //    la destination).
            var removed = await db.ExecuteAsync(
                "DELETE FROM family_storage WHERE id = @Id",
                new { Id = oldId }, tx);
            if (removed == 0)
            {
                await tx.RollbackAsync();
                return false;
            }

            // 2. Inventaire destination — même liste de colonnes que
            //    InventoryRepository.AddItemAsync.
            const string sql = @"
                INSERT INTO inventory1
                    (id, player_id, container_index, loc, item_id,
                     durability, cur_maxdurability, strengthen, bind,
                     create_time, due_date, quality, embedded_amount,
                     embedded_id1, embedded_id2, embedded_id3,
                     embedded_id4, embedded_id5, embedded_id6,
                     combo_id, witchcraft, identify,
                     extra_combo_id1, extra_combo_id2, extra_combo_id3,
                     extra_combo_id4, extra_combo_id5, extra_combo_id6,
                     extra_combo_id7, extra_combo_id8, extra_combo_id9)
                VALUES (@Id, @CharacterId, @BagId, @Slot, @ItemId,
                    @Durability, @MaxDurability, @EnchantLevel,
                    @IsBoundI, @CreateTime, @DueDate, @Quality,
                    @EmbeddedAmount,
                    @EmbeddedId1, @EmbeddedId2, @EmbeddedId3,
                    @EmbeddedId4, @EmbeddedId5, @EmbeddedId6,
                    @Combo, @Witchcraft, @Identify,
                    @EC1, @EC2, @EC3, @EC4, @EC5, @EC6, @EC7, @EC8,
                    @EC9)";
            await db.ExecuteAsync(sql, new
            {
                item.Id, item.CharacterId, item.BagId, item.Slot,
                item.ItemId, item.Durability, item.MaxDurability,
                item.EnchantLevel,
                IsBoundI = item.IsBound ? 1 : 0,
                CreateTime = item.CreateTime != 0 ? item.CreateTime
                    : (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                item.DueDate, item.Quality, item.EmbeddedAmount,
                item.EmbeddedId1, item.EmbeddedId2, item.EmbeddedId3,
                item.EmbeddedId4, item.EmbeddedId5, item.EmbeddedId6,
                item.Combo, item.Witchcraft, item.Identify,
                EC1 = item.ExtraCombos[0], EC2 = item.ExtraCombos[1],
                EC3 = item.ExtraCombos[2], EC4 = item.ExtraCombos[3],
                EC5 = item.ExtraCombos[4], EC6 = item.ExtraCombos[5],
                EC7 = item.ExtraCombos[6], EC8 = item.ExtraCombos[7],
                EC9 = item.ExtraCombos[8],
            }, tx);
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
