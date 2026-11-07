using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>Commandes texte admin/GM — NCID 3
/// <c>CNE_CZ_ZoneServerTextCommand</c> <c>[u16 len][utf8 text]</c>.
/// Désasm <c>Execute</c> @0x8295120 :
///   - l'entité doit être un personnage (type 7) ;
///   - gate : <c>char+0x63c (privilege) > 0</c> — sinon bail silencieux
///     puis TextOutputIndex(0x5d0) ;
///   - le texte est dispatché à CZoneServerTextCommandTable
///     (commandes natives : set_money, change_class, add_class,
///     transport_char_revive_point, announce, get_player_info…).
/// Sous-ensemble implémenté ici, tous appliqués côté serveur avec
/// feedback via 266 ClientTextOutput <c>[u8 canal][str]</c>.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Dispatch d'une commande texte — gate privilège natif
    /// (silencieux si 0), premier token = nom de commande
    /// (préfixes '/', '!', '.' tolérés), puis la sous-table.</summary>
    private async Task HandleTextCommandAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || !st.Migrated) return;
        if (payload.Length < 2) return;
        var len = BitConverter.ToUInt16(payload, 0);        // u16 : len
        if (payload.Length < 2 + len) return;
        var text = Encoding.UTF8.GetString(payload, 2, len).Trim();

        // Gate natif : privilege <= 0 -> commande ignorée (le client
        // reçoit l'index 0x5d0 = "commande inconnue/refusée").
        if (st.Privilege <= 0)
        {
            EdenLog.Write($"[Zone-Entry] TextCommand refusé " +
                $"char={st.CharId} privilege=0 : '{text}'");
            await SendTextIndexAsync(session, 0x5d0, token);
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        var cmd = parts[0].TrimStart('/', '!', '.').ToLowerInvariant(); var args = parts.Skip(1).ToArray();
        EdenLog.Write($"[Zone-Entry] TextCommand char={st.CharId} " +
            $"priv={st.Privilege} : '{text}'");

        if (cmd == "vip") { await SendAdminTextAsync(session, await Features.Admin.AdminBridge.RunVipTextAsync(session.SessionId, args), token); return; }
        switch (cmd)
        {
            case "help":
                // Help détaillé : commandes locales puis tout le
                // registre GM (S_RootCmds + maison). Envoyé en plusieurs
                // lignes 266 — une seule ligne tronque l'affichage.
                foreach (var line in BuildHelpLines())
                    await SendAdminTextAsync(session, line, token);
                break;

            case "heal":
                st.PlayerHp = st.PlayerMaxHp;
                st.PlayerMp = st.PlayerMaxMp;
                await SendVitalHpAsync(session, st.CharId, st.PlayerHp,
                    st.PlayerMaxHp, token);
                await SendVitalMpAsync(session, st.CharId, st.PlayerMp,
                    st.PlayerMaxMp, token);
                await SendAdminTextAsync(session,
                    $"heal → {st.PlayerHp}/{st.PlayerMaxHp}", token);
                break;

            case "god" or "invincible":
                st.GodMode = !st.GodMode;
                await SendAdminTextAsync(session,
                    $"god mode : {(st.GodMode ? "ON" : "OFF")}", token);
                break;

            case "trait" or "herotrait":
                // trait <0-8> — assigne le trait héroïque (create_flag
                // → enchant stats 52000+n) aux persos créés avant le
                // sélecteur (bug live : aucun trait assignable).
                // Persiste directement (le StatPipeline relit la DB
                // juste après — un write-behind servirait l'ancienne
                // valeur), repousse le 9058 et rafraîchit stats+vitals.
                if (args.Length >= 1 && int.TryParse(args[0], out var tr)
                    && tr is >= 0 and <= 8)
                {
                    await _services.Characters.SetCreationFlagAsync(
                        (int)st.CharId, tr);
                    st.CreateFlag = tr;
                    await session.SendAsync(
                        new Handlers.CNC_CZ_ClientHeroTrait
                        { Trait = (byte)tr }, token);
                    var (mhp, mmp) = await ProfileStatEmitter.SendAsync(
                        session, _services, (int)st.CharId, st.RaceId,
                        st.ClassId, st.Level, token);
                    st.PlayerMaxHp = mhp;
                    st.PlayerMaxMp = mmp;
                    st.PlayerHp = Math.Min(st.PlayerHp, mhp);
                    st.PlayerMp = Math.Min(st.PlayerMp, mmp);
                    await SendVitalHpAsync(session, st.CharId,
                        st.PlayerHp, st.PlayerMaxHp, token);
                    await SendVitalMpAsync(session, st.CharId,
                        st.PlayerMp, st.PlayerMaxMp, token);
                    await SendAdminTextAsync(session,
                        $"trait -> {tr}", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage: //trait <0-8>", token);
                break;

            case "team":
                // team — crée une équipe solo (CForceTeam natif) pour
                // tester les flux exigeant un groupe : consentement
                // evaluate TeamOperation [4][3], loot partagé…
                if (_services.Teams.GetTeamOf((int)st.CharId) != null)
                    _services.Teams.LeaveTeam((int)st.CharId);
                var gmTeam = _services.Teams.CreateTeam((int)st.CharId,
                    st.CharName, st.Level, st.ClassId);
                await SendAdminTextAsync(session,
                    $"team #{gmTeam.Id} créée (leader {st.CharId})",
                    token);
                break;

            case "service" or "svc":
                // service <type> [ref] [extra] — appelle directement la
                // table NPC-service → UI (RunNpcServiceAsync) sans
                // dialogue, pour tester les paquets 579/496/… hors NPC.
                if (args.Length < 1 || !int.TryParse(args[0], out var svcT))
                {
                    await SendAdminTextAsync(session,
                        "usage: //service <type> [ref] [extra]", token);
                    break;
                }
                {
                    var sRef = args.Length > 1 && int.TryParse(args[1], out var r)
                        ? r : 0;
                    var sExtra = args.Length > 2 && int.TryParse(args[2], out var e)
                        ? e : 0;
                    await RunNpcServiceAsync(session, st, svcT, sRef, sExtra,
                        token);
                }
                break;

            case "tele" or "tp" or "warp":
                // tele <x> <z> — déplacement dans le node courant,
                // même payload que le teleport de position MoveSelf.
                if (args.Length >= 2 &&
                    float.TryParse(args[0],
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var tx) &&
                    float.TryParse(args[1],
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var tz))
                {
                    st.PosX = tx;
                    st.PosZ = tz;
                    // Même séquence que le reposition revive : 313
                    // spawn `[i32 node][u8][f32 x][f32 z][f32 facing]`
                    // pour replacer le perso côté client.
                    await session.SendAsync(
                        new CNC_CZ_ClientEnterNodeNodeData
                        {
                            Field1 = st.NodeId, Field2 = 1,
                            Field3 = tx, Field4 = tz, Field5 = st.Facing,
                        }, token);
                    await SendAdminTextAsync(session,
                        $"tele -> {tx:F0} {tz:F0}", token);
                    // Resynchronise la vue : un joueur immobile après
                    // //tele n'envoie aucun MoveSelf → les autres
                    // joueurs/entités de la zone d'arrivée n'étaient
                    // jamais annoncés (multi-joueurs invisibles).
                    await UpdateSightAsync(session, st, token,
                        force: true);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage: //tele <x> <z>", token);
                break;

            case "set_money" or "setmoney" or "sm":
                // set_money <type> <amount> natif — on prend le dernier
                // argument comme montant d'or. Après l'UPDATE, le natif
                // émet un 280 ClientUpdateAttr (CCharacter::GainGold
                // @0x842a890 : attr u16 = 2, valeur i32 = char+0x4b0)
                // pour rafraîchir l'or affiché côté client.
                if (args.Length >= 1 && int.TryParse(args[^1], out var g))
                {
                    st.Gold = g;   // synchro mémoire AVANT la DB —
                    // sinon le prochain achat écrasait le set (bug live).
                    await _services.Characters.SetGoldAsync(
                        (int)st.CharId, g);
                    await SendAttrAsync(session, st.CharId, 2, g, 0,
                        token);
                    await SendAdminTextAsync(session, $"gold={g}", token);
                }
                break;

            case "give" or "give_item":
                // give <itemId> [n] — octroi GM : même chemin que la
                // remise de quête (AddItemAsync + 274/291 + trésor au
                // sol si sac plein + notif ItemCollect).
                if (args.Length >= 1 &&
                    int.TryParse(args[0], out var gItem))
                {
                    var gn = args.Length >= 2 &&
                        int.TryParse(args[1], out var v) ? v : 1;
                    await GiveQuestItemAsync(session, st, gItem,
                        Math.Max(1, gn));
                    await SendAdminTextAsync(session,
                        $"give {gItem} x{Math.Max(1, gn)}", token);
                }
                break;

            case "gain_exp":
                // gain_exp <n> — `TC_GainExp` natif → GainExp : le
                // pipeline de boucle joue level-ups, vitals, 600/284 —
                // le chemin GmHandlers (DB seule) laissait
                // st.Experience périmé : le kill suivant réécrasait
                // l'ancienne valeur en DB (grant perdu).
                if (args.Length >= 1 && long.TryParse(args[0], out var xp)
                    && xp > 0)
                {
                    var ups = await GrantCharExpAsync(session, st, xp);
                    await SendAdminTextAsync(session,
                        $"exp += {xp} → lv{st.Level} exp={st.Experience}" +
                        (ups > 0 ? $" (+{ups} lv)" : ""), token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : gain_exp <n>", token);
                break;

            case "gain_class_point" or "gain_cp":
                // gain_class_point <n> — `TC_GainClassPoint` natif
                // (@0x083cfbf0) → GainClassPoint : même pipeline que
                // clevel (284, paliers, AA, certs).
                if (args.Length >= 1 && int.TryParse(args[0], out var cpts)
                    && cpts > 0)
                {
                    await GrantClassPointAsync(session, st, cpts);
                    await SendAdminTextAsync(session,
                        $"class_point += {cpts} → clv={st.ClassLevel}",
                        token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : gain_class_point <n>", token);
                break;

            case "self_use_effect":
                // self_use_effect <effectId> <restSec> —
                // `TC_SelfUseEffect` @0x083d1bb0 → EffectToLife(self,
                // self, eid, rest, …, true, 0, 1, 0, 0) : applique
                // l'enchant S_Enchant à soi (buffs de test).
                if (args.Length >= 2 && int.TryParse(args[0], out var eid)
                    && int.TryParse(args[1], out var rest))
                {
                    if (_services.Enchants.Get(eid) == null)
                    {
                        await SendAdminTextAsync(session,
                            $"effect {eid} hors S_Enchant", token);
                        break;
                    }
                    await AddPlayerEnchantAsync(session, st,
                        (int)st.CharId, eid, rest, 1, token);
                    await SendAdminTextAsync(session,
                        $"self_use_effect {eid} rest={rest}", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : self_use_effect <effectId> <restSec>",
                        token);
                break;

            case "remove_enchant":
                // remove_enchant <effectId> [teamFlag] —
                // `TC_RemoveEnchant` @0x083d4f60 → RemoveEnchantByID
                // (ici : soi seul — le flag équipe natif étendait aux
                // membres locaux).
                if (args.Length >= 1 && int.TryParse(args[0], out var reid))
                {
                    var had = st.Statuses.ContainsKey(
                        (int)(short)reid & 0xffff);
                    await RemovePlayerEnchantAsync(session, st, reid,
                        token);
                    await SendAdminTextAsync(session,
                        had ? $"enchant {reid} retiré"
                            : $"enchant {reid} absent", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : remove_enchant <effectId>", token);
                break;

            case "clone_monster" or "clone_monster_locate":
                // clone_monster <mobId> [count] — `TC_CloneMonster`
                // @0x083dbc60 : spawn de mob(s) à la position du GM
                // (le locate/around natifs ajoutent x/z — même
                // pipeline SpawnGroup que SummonInEventArea).
                if (args.Length >= 1 && int.TryParse(args[0], out var mobId))
                {
                    var cnt = args.Length >= 2 &&
                        int.TryParse(args[1], out var cn)
                        ? Math.Clamp(cn, 1, 8) : 1;
                    var spawn = new Eden.Core.Data.Parsers.SceneParser
                        .MonsterSpawn
                    {
                        MonsterId = mobId, Count = cnt,
                        X = st.PosX, Y = st.PosZ,
                        // Hauteur réelle sous le GM (BornMonster natif
                        // résout le sol en interne) — Z=0 laissait les
                        // clones flotter sous les maps surélevées.
                        Z = _services.Live.GroundHeightAt(
                            _services.Scenes.GetScene(
                                $"S{st.NodeId:D3}"),
                            st.PosX, st.PosZ),
                    };
                    var spawned = _services.Live.SpawnGroup(
                        st.NodeId, st.InstanceGroup, spawn).ToList();
                    foreach (var e in spawned)
                        await BroadcastSeeMobAsync(st.NodeId,
                            st.InstanceGroup, e, token);
                    await SendAdminTextAsync(session,
                        spawned.Count > 0
                            ? $"clone_monster {mobId} ×{spawned.Count}"
                            : $"mob {mobId} hors S_Monster", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : clone_monster <mobId> [count]", token);
                break;

            case "npc":
                // npc <npcId> — extension Eden (miroir du verbe de
                // scène BornNpcByTID) : fait naître un NPC template à
                // la position du GM — nécessaire aux NPC jamais placés
                // dans les scènes (commissaire-priseur 53003).
                if (args.Length >= 1 && int.TryParse(args[0], out var npcId)
                    && _services.Live.NpcSpawnable(npcId))
                {
                    var e = _services.Live.SpawnNpc(st.NodeId,
                        st.InstanceGroup, npcId, st.PosX, st.PosZ,
                        _services.Live.GroundHeightAt(
                            _services.Scenes.GetScene(
                                $"S{st.NodeId:D3}"),
                            st.PosX, st.PosZ),
                        st.Facing);
                    await BroadcastSeeMobAsync(st.NodeId,
                        st.InstanceGroup, e, token);
                    await SendAdminTextAsync(session,
                        $"npc {npcId} né", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : npc <npcId>", token);
                break;

            case "transport_node" or "goto":
                // transport_node <nodeId> — commande native de
                // téléportation de carte ; `goto` natif prend 2
                // coordonnées → routé vers tele.
                if (cmd == "goto" && args.Length >= 2 &&
                    float.TryParse(args[0],
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var gx) &&
                    float.TryParse(args[1],
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var gz))
                {
                    // goto <x> <z> natif = déplacement dans le node —
                    // même corps 313 que //tele.
                    st.PosX = gx;
                    st.PosZ = gz;
                    await session.SendAsync(
                        new CNC_CZ_ClientEnterNodeNodeData
                        {
                            Field1 = st.NodeId, Field2 = 1,
                            Field3 = gx, Field4 = gz,
                            Field5 = st.Facing,
                        }, token);
                    await SendAdminTextAsync(session,
                        $"goto -> {gx:F0} {gz:F0}", token);
                    await UpdateSightAsync(session, st, token,
                        force: true);
                    break;
                }
                if (args.Length >= 1 && int.TryParse(args[0], out var tn))
                {
                    var scene = _services.Scenes.GetScene($"S{tn:D3}");
                    if (scene == null)
                    {
                        await SendAdminTextAsync(session,
                            $"S{tn:D3} sans scene", token);
                        break;
                    }
                    var (tx2, ty2, tz2) = ResolveAdminArrival(scene);
                    await MigrateToNodeAsync(session, st, tn,
                        tx2, ty2, tz2, token);
                    await SendAdminTextAsync(session,
                        $"-> node {tn}", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : transport_node <nodeId> | goto <x> <z>",
                        token);
                break;

            case "restore":
                // restore — commande native `TC_Restore` @0x083e5b90 :
                // récupération complète HP/MP (miroir de //heal).
                st.PlayerHp = st.PlayerMaxHp;
                st.PlayerMp = st.PlayerMaxMp;
                await SendVitalHpAsync(session, st.CharId, st.PlayerHp,
                    st.PlayerMaxHp, token);
                await SendVitalMpAsync(session, st.CharId, st.PlayerMp,
                    st.PlayerMaxMp, token);
                await SendAdminTextAsync(session, "restored", token);
                break;

            case "get_license":
                // get_license <id> — `TC_GetLicense` @0x083d21e0 →
                // CPlayerFactory::GetLicense : certificat de classe
                // dans la table player_license + écho 509 {s16,u8}.
                if (args.Length >= 1 && int.TryParse(args[0], out var lic))
                {
                    var already = _services.Licenses.StateOf(
                        (int)st.CharId).Owned.Any(
                            o => o.ItemId == unchecked((short)lic));
                    await _services.Licenses.GrantLicenseAsync(
                        (int)st.CharId, unchecked((short)lic));
                    if (!already)
                        await session.SendAsync(
                            new CNC_CZ_ClientPlayerLicense
                            {
                                Licenses =
                                    { (unchecked((short)lic), 0) },
                            }, token);
                    await SendAdminTextAsync(session,
                        $"license {lic} ok", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : get_license <id>", token);
                break;

            case "add_class" or "ac":
                if (args.Length >= 1 && int.TryParse(args[0], out var ac)
                    && ac is > 0 and < 100)
                {
                    // KB-04 : même octroi complet que eden-add-class.
                    var added = await GrantNewClassAsync(session, st, ac,
                        token);
                    await SendAdminTextAsync(session,
                        added ? $"class {ac} ok" : $"class {ac} déjà possédée",
                        token);
                }
                break;

            case "change_class" or "cc":
                if (args.Length >= 1 && int.TryParse(args[0], out var cc))
                    await HandleChangeClassAsync(session, st,
                        new[] { (byte)cc }, token);
                break;

            case "go" or "transport" or "tpa":
                // go <nodeId> — même séquence de migration qu'un portail.
                if (args.Length >= 1 && int.TryParse(args[0], out var node))
                {
                    var dest = _services.Scenes.GetScene($"S{node:D3}");
                    if (dest == null)
                    {
                        await SendAdminTextAsync(session,
                            $"S{node:D3} sans scene", token);
                        break;
                    }
                    var (dx, dy, dz) = ResolveAdminArrival(dest);
                    await MigrateToNodeAsync(session, st, node,
                        dx, dy, dz, token);
                    await SendAdminTextAsync(session,
                        $"-> node {node}", token);
                }
                break;

            case "level" or "lvl" or "set_level":
                // level <n> — miroir du SetLevel natif (@0x841c4xx) :
                // descente → +0x470=0 puis boucle GainExp(seuil S_Level)
                // par niveau. Le pipeline GrantCharExpAsync joue les
                // effets visibles (284, vitals, sorts généraux) — un
                // UPDATE sec ne rafraîchissait rien en jeu (bug live).
                if (args.Length >= 1 && int.TryParse(args[0], out var lv)
                    && lv is > 0 and <= 200)
                {
                    if (lv <= st.Level)
                    {
                        // Descente (ou niveau identique) : le natif
                        // remet level/exp à 0 puis ré-alimente.
                        await _services.Characters.UpdateStatsAsync(
                            (int)st.CharId, st.PlayerHp, st.PlayerMp,
                            0, 1);
                        st.Level = 1;
                        st.Experience = 0;
                        // Même garde que GrantCharExpAsync : l'UPDATE
                        // est direct — invalide les saves périmés en file.
                        _ = NextSaveMark(st.CharId);
                    }
                    var need = 0L;
                    for (var l = st.Level; l < lv; l++)
                        need += _services.Progression.ExpForLevel(l);
                    if (need <= 0)
                        await SendAdminTextAsync(session,
                            $"level déjà = {st.Level}", token);
                    else
                    {
                        await GrantCharExpAsync(session, st,
                            need - Math.Min(need, st.Experience));
                        await SendAdminTextAsync(session,
                            $"level -> {st.Level}", token);
                    }
                    // Cap natif class_lv ≤ char_lv : après une descente,
                    // les lignes classlist (et le miroir class_lv) ne
                    // doivent pas rester au-dessus — sinon le HUD
                    // affiche « perso|classe » incohérent au relog.
                    if (st.ClassLevel > st.Level)
                        st.ClassLevel = st.Level;
                    await _persistence.EnqueueAsync((int)st.CharId,
                        _ => _services.Classes.ClampClassLevelsAsync(
                            (int)st.CharId, st.Level));
                }
                break;

            case "clevel" or "class_level" or "clvl" or "set_class_level":
                // clevel <n> — miroir du SetClassLevel natif
                // (@0x841c9c0) : pour chaque niveau entre le niveau
                // courant et la cible, le natif appelle
                // GainClassPoint(seuil du niveau) — le pipeline
                // COMPLET (284 level-up, sorts de palier, AA,
                // certificats multiples de 10, 489, attrs) — pas un
                // UPDATE sec. Sans ça rien n'était visible en jeu et
                // les certs de palier n'étaient pas octroyés.
                if (args.Length >= 1 && int.TryParse(args[0], out var clv)
                    && clv is > 0 and <= 200)
                {
                    var cp = await _services.Classes
                        .GetClassProgressAsync((int)st.CharId, st.ClassId);
                    if (cp == null)
                    {
                        await SendAdminTextAsync(session,
                            "classe non possédée — //add_class", token);
                        break;
                    }
                    // Cap natif : class_lv ≤ char_lv (+0x470) — au-delà,
                    // GainClassPoint clampe l'exp à seuil-1.
                    var target = Math.Min(clv, Math.Max(st.Level, 1));
                    if (target < cp.ClassLevel)
                    {
                        // Descente : le natif remet +0x47e=0 et +0x484=0
                        // puis ré-alimente GainClassPoint niveau par
                        // niveau (les octrois sont idempotents).
                        await _services.Classes.SetClassProgressAsync(
                            (int)st.CharId, st.ClassId, 1, 0);
                        st.ClassLevel = 1;
                        cp = await _services.Classes
                            .GetClassProgressAsync((int)st.CharId,
                                st.ClassId);
                    }
                    // Exp cumulée pour atteindre `target` depuis le
                    // niveau courant — mêmes seuils que la boucle
                    // interne (S_Level.ClassExpNeeded, col 10).
                    var need = 0L;
                    var fromLv = Math.Max(cp?.ClassLevel ?? 1, 1);
                    for (var l = fromLv; l < target; l++)
                        need += _services.GameData.Levels
                            .FirstOrDefault(r => r.Level == l + 1)
                            ?.ClassExpNeeded ?? 0;
                    need = Math.Max(need - (cp?.ClassExp ?? 0), 0);
                    if (need <= 0 && fromLv >= target)
                        await SendAdminTextAsync(session,
                            $"class_level déjà = {fromLv}", token);
                    else
                    {
                        // Une seule passe du pipeline : la boucle
                        // interne joue tous les paliers (284, sorts,
                        // AA, certs+509, 489) — équivalent à la boucle
                        // SetClassLevel native.
                        await GrantClassPointAsync(session, st,
                            Math.Max(need, 1));
                        await SendAdminTextAsync(session,
                            $"class_level {fromLv} -> {st.ClassLevel}" +
                            (clv > target
                                ? $" (cap niveau perso {st.Level})" : ""),
                            token);
                    }
                }
                break;

            case "kill":
                // kill [mobId] — achève les mobs vivants du node+
                // instance courant (id S_Monster si fourni, sinon le
                // plus proche) par le chemin de mort normal
                // KillMobAsync : compteurs DoD, loot, exp, broadcast
                // identiques à un vrai kill. Outil de validation.
                {
                    var alive = _services.Live
                        .MobsOf(st.NodeId, st.InstanceGroup)
                        .Where(m => m.Alive);
                    var targets = args.Length >= 1 &&
                        int.TryParse(args[0], out var mid)
                        ? alive.Where(m => m.MobId == mid).ToList()
                        : alive.OrderBy(m =>
                            (m.X - st.PosX) * (m.X - st.PosX) +
                            (m.Z - st.PosZ) * (m.Z - st.PosZ))
                            .Take(1).ToList();
                    if (targets.Count == 0)
                    {
                        await SendAdminTextAsync(session,
                            "aucun mob cible", token);
                        break;
                    }
                    foreach (var m in targets)
                        await KillMobAsync(session, st, m, token,
                            0, m.Hp);
                    await SendAdminTextAsync(session,
                        $"kill x{targets.Count}", token);
                }
                break;

            case "suicide":
                // suicide — tue le GM lui-même par le chemin de mort
                // joueur normal (outil de validation de la fenêtre de
                // mort / revive).
                if (!st.Dead)
                {
                    await PlayerDeathAsync(session, st, 0u, token);
                    await SendAdminTextAsync(session, "suicide", token);
                }
                break;

            case "worldboss" or "wb":
                // worldboss — statut des 26 boss fixes (vivant / mort +
                // compte à rebours de respawn / node non peuplé).
                foreach (var line in WorldBossStatusLines(
                    _services.Live.WorldBossStatus(), DateTime.UtcNow))
                    await SendAdminTextAsync(session, line, token);
                break;

            case "mutant":
                // mutant [mobId 60761-60766] [région 0-5] — force
                // l'apparition d'un mutant (sinon le planificateur).
                {
                    var mid = args.Length >= 1 &&
                        int.TryParse(args[0], out var m1) ? m1 : 60761;
                    var reg = args.Length >= 2 &&
                        int.TryParse(args[1], out var r1) ? r1 : 0;
                    _mutants ??= new Eden.Core.Game.WorldBoss.MutantScheduler(
                        MutantOptionsFromConfig(), Random.Shared,
                        DateTime.UtcNow);
                    var act = _mutants.ForceSpawn(mid, reg, DateTime.UtcNow);
                    if (act == null)
                    {
                        await SendAdminTextAsync(session,
                            "mutant inconnu ou déjà actif", token);
                        break;
                    }
                    await ApplyMutantActionAsync(act);
                    await SendAdminTextAsync(session,
                        $"mutant #{mid} → node {act.NodeId} " +
                        $"({act.X:F0},{act.Y:F0})", token);
                }
                break;

            case "pos" or "get_player_locate" or "locate":
                await SendAdminTextAsync(session,
                    $"node={st.NodeId} x={st.PosX:F1} y={st.PosZ:F1}",
                    token);
                break;

            case "gain_lifelong_gold" or "gain_lifelong":
                // /gain_lifelong_gold <n> — commande native
                // (@0x0888284b) : zone relaie « GMGainLifelong
                // Money = %d » vers la mission qui crédite
                // char+0xc54 (cap 9999999) puis pousse 555.
                // Chez nous : crédit direct + 555 absolu.
                if (args.Length >= 1 && int.TryParse(args[0],
                        out var llg))
                {
                    st.LifelongGold = await _services.LifelongRepo
                        .AddGoldAsync((int)st.CharId, llg);
                    await session.SendAsync(
                        new CNC_CZ_ClientUpdateLifelongGold
                        { Value = st.LifelongGold }, token);
                    await SendAdminTextAsync(session,
                        $"lifelong_gold += {llg} → {st.LifelongGold}",
                        token);
                }
                else
                {
                    await SendAdminTextAsync(session,
                        "usage : gain_lifelong_gold <n>", token);
                }
                break;

            case "gain_prestige_exp":
                // /gain_prestige_exp <id> <exp> — commande native
                // (aide zone @0x08887154) : AddPrestigeExp → DBA
                // « GainExp Prestige » → mission. Chez nous : ZW 636
                // gain_prestige → monde persiste + 60 + miroir retour.
                if (args.Length >= 2 && int.TryParse(args[0], out var pid)
                    && int.TryParse(args[1], out var pexp))
                {
                    await GainPrestigeExpAsync(st, (short)pid, pexp,
                        token);
                    await SendAdminTextAsync(session,
                        $"prestige[{pid}] += {pexp}", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : gain_prestige_exp <id> <exp>", token);
                break;

            case "set_prestige_level":
                // /set_prestige_level <id> <level> — commande native
                // (aide zone @0x088870ec) : « SetLevel Prestige »
                // mission → UPDATE level + broadcast.
                if (args.Length >= 2 && int.TryParse(args[0], out var plid)
                    && int.TryParse(args[1], out var plv))
                {
                    await SetPrestigeLevelAsync(st, (short)plid,
                        (short)plv, token);
                    await SendAdminTextAsync(session,
                        $"prestige[{plid}] level = {plv}", token);
                }
                else
                    await SendAdminTextAsync(session,
                        "usage : set_prestige_level <id> <level>", token);
                break;

            case "update_closet":
                // TC_UpdateCloset natif (S_RootCmds lv4) : enregistre
                // les couleurs équipées dans le slot de la classe
                // courante puis réémet le 488.
                if (args.Length >= 1 && int.TryParse(args[0], out var cix)
                    && cix is >= 0 and < 16)
                {
                    await HandleUpdateClosetCommandAsync(
                        session, st, cix, token);
                    await SendAdminTextAsync(session,
                        $"update_closet → slot {cix}", token);
                }
                else
                {
                    await SendAdminTextAsync(session,
                        "usage : update_closet <index>", token);
                }
                break;

            default:
                // Sondes de debug conservées (s298, s298b, s269, s271,
                // sall) puis rejet natif pour le reste.
                if (cmd.StartsWith('s'))
                {
                    await HandleProbeCommandAsync(session, st, cmd, token);
                    break;
                }
                // Registre GM (clone_item, gain_gold, help, find_item,
                // give, list_class, learn_spell…) — le switch local ne
                // couvre que les commandes historiques ; sans cette
                // délégation TOUTE la table S_RootCmds retombait sur
                // 0x5d0 « inconnue » alors que les handlers étaient
                // enregistrés (bug live : la moitié des commandes
                // « ne marchaient pas »).
                {
                    var resp = await _services.Gm.ExecuteAsync(
                        cmd + (args.Length > 0
                            ? " " + string.Join(' ', args) : ""),
                        st.Privilege,
                        new Eden.Core.Game.Services.GmCommandService
                            .Context((int)st.CharId, session, token,
                                st));
                    if (resp != null)
                        await SendAdminTextAsync(session, resp, token);
                    else
                        await SendTextIndexAsync(session, 0x5d0, token);
                }
                break;
        }
    }

    /// <summary>Lignes du <c>//help</c> : commandes locales en clair
    /// avec usage + exemples, puis la liste GM registre (noms
    /// S_RootCmds/maison) — <c>ListCommands()</c> énumère ini et
    /// handlers pour ne documenter que l'implémenté.</summary>
    private IEnumerable<string> BuildHelpLines()
    {
        yield return "== Commandes ==";
        yield return "set_money <n> — or | level <n> — niveau perso " +
            "| clevel <n> — niveau de classe";
        yield return "add_class <id> — débloque une classe " +
            "(1-61, //list_class) | change_class <id> — la joue";
        yield return "go <nodeId> — change de carte | tele <x> <z> — " +
            "déplace dans la carte | pos — position";
        yield return "heal — soin complet | god — invincible on/off " +
            "| trait <0-8> — trait héroïque | vip <compte> [jours|off]";
        yield return "clone_item <itemId> [qty] — donne un item " +
            "| find_item <nom|id> — cherche l'id d'un item";
        yield return "learn_spell <spellId> — apprend un sort " +
            "| find_spell <nom|id>";
        yield return "gain_gold <n> | gain_exp <n> | " +
            "gain_aa_point <n> | gain_ap_point <n> | " +
            "gain_ep_point <n> | gain_skill_point <n> | set_level <n>";
        yield return "create_guild <nom> [lv] [501|502] — guilde+hall " +
            "| guild_level [famId] <n> | open_hall [node]";
        yield return "give_building <tid> | building_level <loc> <n> " +
            "| place_npc <npcId> | replace_npc <npcId> (hall)";
        yield return "== Registre GM complet ==";
        var names = _services.Gm.ListCommands()
            .Where(c => c.Implemented)
            .Select(c => c.Name);
        var line = "";
        foreach (var n in names)
        {
            if ((line + n).Length > 70)
            {
                yield return line.Trim();
                line = "";
            }
            line += n + " ";
        }
        if (line.Length > 0) yield return line.Trim();
        yield return "Prefixe : // ou / — ex: //clone_item 30001 5";
    }

    /// <summary>Arrivée admin (//go, //transport_node) : aux pieds du
    /// PNJ transporteur (FuncId 8) de la scène — comme le transport
    /// natif (cf. ZoneEntryFlow.Services) ; Positions[0] tombait sur une
    /// falaise en S002. Repli : Positions[0], puis première GATEWAY.</summary>
    private (float X, float Y, float Z) ResolveAdminArrival(
        SceneParser.Scene scene)
    {
        var tp = scene.Npcs.FirstOrDefault(sp =>
            _services.GameData.Npcs.FirstOrDefault(n => n.Id == sp.NpcId)
                ?.FuncId == 8);
        if (tp != null) return (tp.X, tp.Y, tp.Z);
        if (scene.Positions.Count > 0) return scene.Positions[0];
        if (scene.EventAreas.FirstOrDefault(
            a => a.Type == "GATEWAY") is { } ga)
            return (ga.X, ga.Y, ga.Z);
        return (100, 100, 0);
    }

    /// <summary>Feedback texte admin — 266 ClientTextOutput
    /// <c>[u8 canal][str]</c> (désasm Serialize @0x807f160 : u8 puis str).
    /// Canal 0x9b=155 — valeur constante utilisée par tous les appels
    /// natifs au ctor <c>CNC_CZ_ClientTextOutput(str, short)</c>
    /// (ex. 0x80d0c92, 0x81715a9 : l'argument short vaut toujours 0x9b).</summary>
    private static Task SendAdminTextAsync(NetSession session, string text,
        CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientTextOutput
        {
            Field1 = 155,        // u8 : canal 0x9b (constant natif)
            Field2 = text,       // str : message
        }, token);

    /// <summary>Lignes d'affichage du statut des world boss (GM).</summary>
    internal static IEnumerable<string> WorldBossStatusLines(
        IEnumerable<Eden.Core.Game.WorldBoss.WorldBossCatalog.StatusRow> rows,
        DateTime now) =>
        rows.Select(r => r.State switch
        {
            Eden.Core.Game.WorldBoss.WorldBossCatalog.BossState.Alive =>
                $"#{r.MobId} S{r.NodeId:D3} : vivant",
            Eden.Core.Game.WorldBoss.WorldBossCatalog.BossState.Dead =>
                $"#{r.MobId} S{r.NodeId:D3} : mort, retour dans " +
                $"{Math.Max(0, (int)((r.RespawnAt ?? now) - now).TotalSeconds)} s",
            _ => $"#{r.MobId} S{r.NodeId:D3} : node non chargé",
        });
}
