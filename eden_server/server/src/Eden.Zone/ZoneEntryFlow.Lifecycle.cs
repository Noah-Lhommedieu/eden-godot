using Eden.Core;
using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Game.Services;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  ZoneEntryFlow — séquence d'entrée en map pilotée par les captures de la
//  zone native (captures/zsniff_cs_entry_success.bin, run test3 18:26).
//
//  Flux prouvé côté fil :
//    C>Z NCID 0  [ticket 22B]          → T_ ServerUseTicket → S>C NCID 473 (hello)
//    C>Z NCID 57 (1er Echo)            → push 302/313/404 + ZW NCID 5 (ResetTime)
//    C>Z NCID 26/24/53/110/113/195/243/256/133/25 → burst EnterNode*
//    C>Z requêtes batch (62,50,152,162,166,164,165,181,213,558,223,258…)
//    C>Z NCID 4  (MigrationDone)       → dump attributs + spawns (sight 298×N)
//    C>Z NCID 1  (MoveSelf)            → NCID 264 echo position
//
//  Les payloads sont replayés depuis la capture et patchés :
//    - charId de référence 200000004 (LE 04c2eb0b) → charId réel
//    - nom "test3" (u16 len + bytes) → nom réel dans NCID 310
// -----------------------------------------------------------------------------
/// <summary>Partiel cycle de vie — annonces de node,
/// persistance/write-behind, déconnexion, fin de migration,
/// ticket, patching des corps relayés (charId/nom/appearance).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Au (re)branchement du lien mission : la mission a perdu
    /// toute son instance-map — la zone rejoue CreateGlobalNode pour
    /// chaque node "global" qu'elle sert (types &lt; 4 dans S_Node.ini :
    /// maps normales — les types 4-19 sont des instances par-perso,
    /// déclarées via CreateNode à l'entrée).</summary>
    private async Task AnnounceNodesAsync()
    {
        _nodeAnnounced = false;
        try
        {
            var nodes = _services.GameData.Nodes
                .Where(n => n.Type is > 0 and < 4).ToList();
            foreach (var n in nodes)
                await _mission.SendNodeCreateAsync(n.Id, n.Type, 0, -1);
            _nodeAnnounced = true;
            EdenLog.Write(
                $"[Zone-M] {nodes.Count} nodes globaux annoncés à la mission");
        }
        catch (Exception ex)
        {
            // Sans l'annonce la mission ne peut router AUCUN perso — le
            // prochain MigrationDone ré-essaiera (_nodeAnnounced reste faux).
            EdenLog.Write($"[Zone-M] annonce nodes échouée : {ex.Message}");
        }
    }
    /// <summary>Marque de fraîcheur des sauvegardes par perso : chaque
    /// demande prend un numéro croissant et l'exécution d'un travail
    /// enfilé est ignorée si une demande plus récente existe. La file
    /// ordonnée peut accumuler du retard (chaque save ≈ secondes avec
    /// les enchants) : sans ça les saves de sessions MORTES drainaient
    /// des heures plus tard et réécrivaient level/exp/hp périmés par-
    /// dessus l'état courant (constat live : //level 20 écrit, puis
    /// écrasé à 9 par un save de la session d'avant).</summary>
    private readonly ConcurrentDictionary<long, long> _saveMarks = new();

    private long NextSaveMark(long charId) =>
        _saveMarks.AddOrUpdate(charId, 1, (_, m) => m + 1);

    private async Task SaveIfLatestAsync(EntryState st, long mark,
        bool caveLogout = false)
    {
        if (_saveMarks.TryGetValue(st.CharId, out var cur) && mark < cur)
            return;
        await SaveCharacterAsync(st, caveLogout);
    }

    /// <summary>Sauvegarde complète de l'état perso — appelée à la
    /// déconnexion, par l'autosave périodique (regen loop) et avant les
    /// migrations. Persiste node/pos/dir/hp/mp/exp/level/class_id en un
    /// UPDATE (SaveFullStateAsync) — l'or n'est PAS réécrit ici
    /// (gold:-1) : chaque mutation le persiste déjà en delta atomique
    /// (AddGoldAsync / tx) et st.Gold serait une valeur périmée —
    /// réécrire un absolu ici écraserait les gains mission/auction
    /// arrivés depuis le dernier mouvement de la zone.</summary>
    private async Task SaveCharacterAsync(EntryState st,
        bool caveLogout = false)
    {
        if (st.CharId == 0) return;
        try
        {
            // CheckLogoutInCave natif (zone @0x0840cc50) : déco sur un
            // nœud « cave » {2,5,7,12,14} → la position persistée est
            // la revive_area (node<<16|clé → point aléatoire dans
            // l'event-area), jamais le nœud instancié — sinon le
            // relog chargerait une scène d'instance sans groupe.
            var saveNode = st.NodeId;
            var saveX = st.PosX;
            var saveZ = st.PosZ;
            var saveH = st.PosY;
            var saveDir = st.Facing;
            if (caveLogout && NodeType(st.NodeId) is 2 or 5 or 7
                    or 12 or 14)
            {
                var p = ResolvePackedAreaPoint(st.ReviveAreaId,
                            sc => sc.ReviveLinks)
                    ?? ResolvePackedAreaPoint(st.LastNormalAreaId,
                            sc => sc.NormalLinks);
                if (p is { } a)
                {
                    EdenLog.Write($"[Zone-Save] logout-in-cave " +
                        $"char=#{st.CharId} node {st.NodeId} " +
                        $"→ save node {a.Node}");
                    (saveNode, saveX, saveZ, saveH, saveDir) = a;
                }
            }
            await _characters.SaveFullStateAsync((int)st.CharId,
                saveNode, saveX, saveZ, saveH, saveDir,
                st.PlayerHp, st.PlayerMp, st.Experience,
                st.Level, st.ClassId, gold: -1, st.ClassLevel,
                maxHp: st.PlayerMaxHp, maxMp: st.PlayerMaxMp);
            // exec+0x114/+0xf0 natifs — dans le même UPDATE de sortie
            // pour que le relog retrouve hall visité + retour.
            await _characters.SaveInstanceVisitAsync((int)st.CharId,
                st.VisitedFamilyInstanceId, st.CrossZoneNode,
                st.CrossZoneX, st.CrossZoneY);
            // « Update Enchant » natif — fige le rest_time courant pour
            // la restauration à la prochaine connexion (sinon la durée
            // repartait à plein à chaque relog).
            foreach (var (eid, status) in st.Statuses)
            {
                // Les statuts de session (persist:false — procs
                // flag 0x1000 natif, talents AA) n'ont JAMAIS de
                // ligne player_enchants : en écrire une ici laissait
                // un reste indélébile (Persisted restait faux →
                // pas de delete au retrait) que le stage « buffs »
                // relisait indéfiniment — stats fantômes.
                if (!status.Persisted) continue;
                await _services.PlayerEnchants.UpsertEnchantAsync(
                    new PlayerEnchantEntity
                    {
                        PlayerId = (int)st.CharId, EffectId = eid,
                        CasterId = status.CasterId,
                        RestTime = status.RestSeconds,
                        // counter +0x30 / cumul stackable persistés —
                        // le restore les re-seed (sinon les stacks
                        // étaient perdus au relog ET gonflés à la
                        // migration suivante).
                        Counter = status.Counter,
                        Level = status.Level,
                        StackCount = status.StackCount,
                    });
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone-Save] échec char={st.CharId} : {ex.Message}");
        }
    }
    /// <summary>Flush write-behind puis sauvegarde complète — les deux
    /// sur la file ordonnée du charId, donc un état sale ne peut pas
    /// écraser le save final. Awaitée à la déconnexion et avant les
    /// migrations (point de durabilité).</summary>
    private async Task PersistStateAsync(EntryState st,
        bool caveLogout = false)
    {
        await _writeBehind.FlushAsync((int)st.CharId);
        // LifelongUpdateAllToMission natif (@0x084236e0, sortie de
        // scène/déco) : flush du pending daily avant le save.
        await FlushLifelongPendingAsync(st);
        var mark = NextSaveMark(st.CharId);
        await _persistence.EnqueueAsync((int)st.CharId,
            _ => SaveIfLatestAsync(st, mark, caveLogout));
        await _persistence.FlushAsync((int)st.CharId);
    }
    /// <summary>Arrêt gracieux de la zone (SIGINT/SIGTERM) : sauvegarde
    /// complète de TOUS les persos en ligne AVANT la vidange de la
    /// boucle et la fermeture des sessions. Sans ça le disconnect
    /// (RunOnLoopAsync sur boucle déjà détruite) lève avant
    /// PersistStateAsync et le process sort sans rien écrire — tout le
    /// progrès depuis l'autosave de 1 min était perdu (constat live
    /// « le serveur ne sauvegarde rien »).</summary>
    public async Task SaveAllOnShutdownAsync()
    {
        var online = _states.Values
            .Where(s => s.CharId != 0 && s.Migrated).ToList();
        var saves = online.Select(async st =>
        {
            try { await PersistStateAsync(st, caveLogout: true); }
            catch (Exception ex)
            {
                EdenLog.Write("[Zone-Save] arrêt : échec " +
                    $"char={st.CharId} : {ex.Message}");
            }
        });
        var all = Task.WhenAll(saves);
        if (await Task.WhenAny(all, Task.Delay(
                TimeSpan.FromSeconds(10))) != all)
            EdenLog.Write("[Zone-Save] arrêt : timeout 10 s");
        EdenLog.Write($"[Zone-Save] arrêt : {online.Count} perso(s) " +
            "sauvegardés");
    }
    /// <summary>NCID 4 MigrationDone — dump des paquets d'entrée
    /// rejoués (sauf les 298 d'un ancien node), spawn des entités de
    /// scène, déclaration du node à la mission, overlay d'état réel,
    /// démarrage des boucles regen/wander.</summary>
    private async Task HandleMigrationDoneAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.CharId == 0) return;
        st.Migrated = true;
        // Bundle d'entrée — snapshot posé par l'install (Phase 2).
        // Les émissions différées sont des OBLIGATIONS (la purge DB
        // est faite, les notices/restores sont dus) : elles se jouent
        // une fois même si EntryDirty a été posé entre-temps ; le
        // drapeau Emitted les exclut des re-migrations suivantes.
        var entry = st.Entry is { Emitted: false } e ? e : null;
        // Exp Ultime — poussée initiale au client (design §4.5) :
        // émise depuis l'état lu hors-boucle (le service peut être
        // absent → zéros, même repli que l'ancien EmitAsync).
        if (entry != null)
            await EmitLimitlessEntryAsync(session, st, entry, token);
        else
            await SendLimitlessAsync(session, st, token);
        // CheckItemDueDate natif (@0x08120550, appelé par
        // MigrationDone::Execute avant le dump) : les items à durée
        // expirée ont été PURGÉS hors-boucle — il ne reste que les
        // notifications 0x5a4/275 à émettre au même point du flux.
        if (entry != null)
            await EmitExpiredNoticesAsync(session, st, entry, token);
        else
            await PurgeExpiredItemsAsync(session, st, token);
        foreach (var hx in EntryReplayData.Map[4])
        {
            var raw = Convert.FromHexString(hx);
            // Les 298 du replay appartiennent au NODE DE LA CAPTURE
            // (S002, cf. NCID 313 node=2 en dur) : ré-entrée portail
            // (NodeChanged) OU login sur une autre map (pos. persistée
            // sur node N≠2 — les coordonnées des maps se recoupent et
            // les NPC de la map de capture apparaissaient en doublon
            // sur la map réelle, bug live « NPC de Limestone sur
            // Aven »). On ne les rejoue que sur le node de la capture.
            if (raw.Length >= 4 &&
                BitConverter.ToUInt16(raw, 0) == 298 &&
                (st.NodeChanged || st.NodeId != ReplayNodeId))
                continue;
            // 280 attr 0x11 du burst = HP du perso CAPTURÉ —
            // fallback uniquement : la DB a déjà chargé les
            // vitals réels (ne pas écraser avec le replay).
            if (st.PlayerHp <= 0 && raw.Length == 20 &&
                BitConverter.ToUInt16(raw, 0) == 280 &&
                BitConverter.ToUInt16(raw, 6) == 0x11)
            {
                st.PlayerHp = BitConverter.ToInt32(raw, 8);
                st.PlayerMaxHp = BitConverter.ToInt32(raw, 12);
            }
            IndexSeenEntity(st, hx);
            await SendPatchedAsync(session, st, hx, token);
        }
        st.NodeChanged = false;
        await SpawnSceneEntitiesAsync(session, st, token);
        // Persiste node+position en DB AVANT MigrationDone — la
        // mission fait GetExecutionerData (SELECT
        // player_characters.node_id) pour router le perso :
        // elle doit lire CE node, déclaré via NodeCreate. Attendu sur
        // la file ordonnée (le fire-and-forget laissait une course
        // entre l'UPDATE et le SELECT de la mission).
        await _persistence.EnqueueAsync((int)st.CharId,
            _ => _characters.UpdateCoordinatesDurableAsync((int)st.CharId,
                st.NodeId, st.PosX, st.PosZ, st.PosY, st.Facing));
        await _persistence.FlushAsync((int)st.CharId);
        if (_mission.Connected)
        {
            if (!_nodeAnnounced)
                await AnnounceNodesAsync();
            // OpenNode natif : CreateNode(charUid, template,
            // parent) lie le perso à SON instance de node —
            // sans ça GetExecutionerAtServer ne trouve pas la
            // zone du perso (node_id DB ↔ node déclaré).
            var nt = _services.GameData.Nodes
                .FirstOrDefault(n => n.Id == st.NodeId);
            await _mission.SendNodeCreateAsync(st.NodeId,
                (short)(nt?.Type ?? 1), (uint)st.CharId, -1);
            await _mission.SendMigrationDoneAsync(
                (uint)st.CharId, (short)st.NodeId, token);
        }
        EdenLog.Write($"[Zone-Entry] MigrationDone -> dump envoyé char=#{st.CharId} node={st.NodeId}");
        try { await SendDungeonPortalsAsync(session, st, token); }
        catch (Exception ex)
        { EdenLog.Write($"[Dungeon] portails : {ex.Message}"); }
        // Overlay live : le replay transporte les valeurs du
        // perso capturé — on réémet gold/classe/vitals/exp DB
        // pour que le client affiche l'état réel.
        await SendEntryStateOverlayAsync(session, st, token);
        // SendAllAchievement natif (mission @0x0808f4d0, appelé au
        // chargement de l'exécutant) : 480 type 0 = succès complétés.
        try { await SendAllAchievementsAsync(session, st, token); }
        catch (Exception ex)
        {
            EdenLog.Write($"[Achievement] synchro entrée " +
                $"char={st.CharId} : {ex.Message}");
        }
        // Purge du registre des enchants de session — la map vit par
        // charId au niveau service tandis que st.Statuses repart à
        // vide à chaque session (portail, relog). Sans ce reset le
        // stage « buffs » du StatPipeline relisait les ids orphelins
        // des procs → stats fantômes sans icône (bug live « proc
        // éternel » : icône partie, stats restées).
        _services.SessionEnchants.TryRemove((int)st.CharId, out _);
        // Régénération HP passive — boucle par perso lancée à
        // l'entrée en zone (annulée à la déconnexion).
        st.LoopCts?.Cancel();
        st.LoopCts = new CancellationTokenSource();
        _ = PlayerRegenLoopAsync(session, st, st.LoopCts.Token);
        // Statuts : tick 1 s (HoT/DoT/expiration) + restauration des
        // enchants persistés (player_enchants — recover enchant
        // natif) + invincibilité de migration (char+0x3c bit7, levée
        // par OnMigrationInvincibleTimeout ~5 s natif).
        _ = StatusTickLoopAsync(session, st, st.LoopCts.Token);
        // NotifyTeamData natif (OnMigrationDone @0x843fba0 →
        // NotifyTeamMemberPos @0x80fc7c0) : 392 positions des
        // coéquipiers migrés — sinon la carte d'équipe reste vide
        // après chaque passage de portail.
        await SendTeamMemberPositionsAsync(session, st, token);
        // Durée native = S_Parameter 349 (1/10 s — « 角色陣亡，復活
        // 無敵時間 » ; la même constante arme l'invincibilité de
        // migration via EffectToLife PK_MIGRATION_EFFECT @0x8108xx).
        st.MigrationInvincibleUntil = DateTime.UtcNow.AddSeconds(
            _services.GameData.Parameters.Get(349, 100f) / 10f);
        // Restauration des enchants persistés — les lignes ont été
        // filtrées et les mortes purgées hors-boucle ; le re-add
        // (Statuses + 299) reste ici, au même point du flux qu'avant.
        try
        {
            if (entry != null)
                await ApplyRestoredEnchantsAsync(session, st, entry,
                    token);
            else
                await RestorePlayerEnchantsAsync(session, st, token);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Status] restore char={st.CharId} : " +
                ex.Message);
        }
        try { await ApplyDevMoveSpeedBuffAsync(session, st, token); }
        catch (Exception ex)
        { EdenLog.Write($"[Status] dev move buff : {ex.Message}"); }
        try { await ApplyDevDungeonPassAsync(session, st, token); }
        catch (Exception ex)
        { EdenLog.Write($"[Status] dev dungeon pass : {ex.Message}"); }
        // État « pierre de portail gratuite » ≤ lvl 80 — OnMigrationDone
        // natif @0x0843fbxx (EffectToLife -0x25c3 durée -2), posé après
        // la restauration des enchants comme dans le flux natif.
        try
        { await SyncBeginnerPortalStateAsync(session, st, token); }
        catch (Exception ex)
        { EdenLog.Write($"[Status] portal state : {ex.Message}"); }
        // Activités recommandées + lifelong : chargées dans le
        // pipeline (reset hebdo mission et reset daily DB déjà
        // faits hors-boucle) — il ne reste que les 554 de reset
        // daily à émettre au même point du flux.
        if (entry != null)
            await EmitLifelongResetsAsync(session, entry, token);
        else
        {
            try
            { await InitRecommendActivitiesAsync(session, st, token); }
            catch (Exception ex)
            {
                EdenLog.Write($"[Recommend] init char={st.CharId} : " +
                    ex.Message);
            }
            try
            { await LoadLifelongAsync(session, st, token); }
            catch (Exception ex)
            {
                EdenLog.Write($"[Lifelong] init char={st.CharId} : " +
                    ex.Message);
            }
        }
        // Le replay d'entrée contient les 296/492/493 du personnage
        // capturé. Après chargement des licences et des enchants, émettre
        // les vrais attributs du personnage courant (équipement +
        // certificats) pour le profil et les formules client. Données
        // du bundle si fraîches — sinon requêtes live (équipement ou
        // classe modifiés entre l'install et MigrationDone).
        if (entry != null && !st.EntryDirty)
            await SendStatBlocksEntryAsync(session, st, entry, token);
        else
            await SendStatBlocksAsync(session, st, st.ClassId,
                st.ClassLevel, token);
        // SendTerritoryData natif (@0x08424ab0, appelé par
        // OnMigrationDone @0x0843fba0) — 516 si le perso est inscrit
        // dans une guerre/battle de territoire.
        try { await SendTerritoryDataAsync(session, st, token); }
        catch (Exception ex)
        {
            EdenLog.Write($"[Territory] send-data char={st.CharId} : " +
                ex.Message);
        }
        // AddClassFromLimit natif à PostInit (KB-03) — premier
        // MigrationDone seulement (≈ construction du CCharacter :
        // login ou entrée dans cette zone, jamais un portail interne).
        // Octroie les classes de base non possédées (reqCharLv=1 :
        // {1,2,3,4,5,19}) et toute classe avancée dont les prérequis
        // sont remplis — 489 + sorts + certs + JPAward par
        // GrantNewClassAsync.
        if (entry != null)
        {
            try
            {
                await UnlockClassesFromLimitAsync(session, st, token);
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Zone-Entry] AddClassFromLimit " +
                    $"char={st.CharId} : {ex.Message}");
            }
        }
        // Le bundle d'entrée est un snapshot one-shot : ce
        // MigrationDone a joué ses émissions différées (Emitted —
        // les re-migrations portail ne les rejouent plus) et ses
        // données sont désormais potentiellement périmées
        // (EntryDirty — les patches/stat blocks retombent sur les
        // requêtes live d'origine, Phase 2).
        if (entry != null) entry.Emitted = true;
        st.EntryDirty = true;
        // Wander idle des mobs — boucle centrale unique
        // (démarre au premier perso migré).
        EnsureMobWanderLoop();
    }
    // La logique ticket (473, validation, ~30-50 opérations SQL) vit
    // désormais dans ZoneEntryFlow.EntryPipeline*.cs — la commande
    // NCID 0 sur la boucle ne fait que poser st.EntryLoad
    // (StartEntryLoad) ; l'install atomique y est repostée.

    /// <summary>Envoie un payload capturé (hex = ncid u16 + corps), patché.</summary>
    private async Task SendPatchedAsync(NetSession session, EntryState st,
        string hex, CancellationToken token)
    {
        var packet = Convert.FromHexString(hex);
        var ncid = BitConverter.ToUInt16(packet, 0);
        var body = packet[2..];

        if (st.CharId != 0 && st.CharId != RefCharId)
            body = PatchCharId(body, st.CharId);
        if (!string.IsNullOrEmpty(st.CharName) && st.CharName != "test3")
            body = PatchName(body, st.CharName);
        // NCID 310 : identité + stats du perso — le replay porte les
        // valeurs du run test3. Layout PROUVÉ par le ctor natif
        // @0x867240d (zone/decompiled.c) — pour un nom de 5 octets
        // (delta = GBK(nom) - 5 sur tous les offsets > 11) :
        //   @13 f32 échelle   @17-18 u8   @19-22 bin4
        //   @23 u16 LEVEL     @25 i64 hp  @33 i64 mp   @41 i32 ?
        //   @45 i32 ?         @49 u8 CLASS @50 u8     @51 bin32 apparence
        // Seuls les champs prouvés sont patchés — écrire ailleurs
        // détruit le paquet (rendu ursun/genre faux/classe absente).
        if (ncid == 310)
        {
            var delta = string.IsNullOrEmpty(st.CharName) ? 0
                : Encoding.GetEncoding("GBK").GetByteCount(st.CharName) - 5;
            // Layout PPROUVÉ par le ctor natif @0x867240d
            // (zone/decompiled.c — CNC_CZ_ClientEnterNodePlayerData) :
            //   @23 u16 = char+0x470 = NIVEAU du perso (checks <0x1e
            //           life-skill — pas la classe ! test1@23=3 == lv3)
            //   @49 u8  = char+0x47c = CLASSE active (le vrai champ —
            //           test1 guerrier à l'entrée = 1, confirmé par les
            //           0x2c 5→3 des changements de classe qui suivent).
            // Écrire la classe @23 cassait les deux (niveau=5, classe=1).
            if (23 + delta + 2 <= body.Length)
                // u16 level @23 — char level (st.Level DB).
                BitConverter.TryWriteBytes(body.AsSpan(23 + delta),
                    (ushort)Math.Max(st.Level, 1));
            // Vitals — ctor natif : +0x1c ← char+0x60 (HP i64) à @25,
            // +0x24 ← char+0x68 (MP i64) à @33, +0x2c ← char+0x70
            // (MaxHP i32) à @41, +0x30 ← char+0x74 (MaxMP i32) à @45.
            // ERREUR HISTORIQUE corrigée : HP était écrit à @33 (slot
            // MP) et MP à @45 (slot MaxMP) — vitals fausses à l'entrée.
            if (25 + delta + 8 <= body.Length)
                BitConverter.TryWriteBytes(body.AsSpan(25 + delta),
                    (long)st.PlayerHp);                     // i64 @25 : HP
            if (33 + delta + 8 <= body.Length)
                BitConverter.TryWriteBytes(body.AsSpan(33 + delta),
                    (long)st.PlayerMp);                     // i64 @33 : MP
            if (41 + delta + 4 <= body.Length)
                BitConverter.TryWriteBytes(body.AsSpan(41 + delta),
                    st.PlayerMaxHp);                        // i32 @41 : MaxHP
            if (45 + delta + 4 <= body.Length)
                BitConverter.TryWriteBytes(body.AsSpan(45 + delta),
                    st.PlayerMaxMp);                        // i32 @45 : MaxMP
            if (49 + delta + 1 <= body.Length)
                // u8 class @49 — classe active (byte, pas u16).
                body[49 + delta] = (byte)Math.Max(st.ClassId, 1);
            var off = 51 + delta; // bin32 apparence @51 — prouvé.
            if (off + 32 <= body.Length)
                Array.Copy(st.Appearance, 0, body, off, 32);
            // @83 u16 = this+0x56 ← char+0x47e = NIVEAU DE CLASSE —
            // jamais patché : le replay envoyait sa valeur (1) et le
            // client affichait « Thieves LV1 » quelle que soit la
            // progression (bug live majeur). @85 i32 = this+0x58 ←
            // char+0x484 = exp de classe ; @105 i32 = this+0x6c ←
            // char+0x488 = class points (CP dépensables).
            // Ligne préchargée par le pipeline d'entrée (Phase 2) —
            // fallback live si le bundle est absent ou périmé.
            var classRow = st.Entry is { ClassRow: not null } e
                         && !st.EntryDirty
                ? e.ClassRow
                : await _services.Classes.GetClassProgressAsync(
                    (int)st.CharId, st.ClassId);
            if (83 + delta + 2 <= body.Length)
                BitConverter.TryWriteBytes(body.AsSpan(83 + delta),
                    (ushort)Math.Max(st.ClassLevel, 1));    // u16 @83 : classLv
            if (85 + delta + 4 <= body.Length)
                BitConverter.TryWriteBytes(body.AsSpan(85 + delta),
                    (int)Math.Min(classRow?.ClassExp ?? 0,
                        int.MaxValue));                     // i32 @85 : class exp
            if (105 + delta + 4 <= body.Length)
                BitConverter.TryWriteBytes(body.AsSpan(105 + delta),
                    classRow?.ClassPoints ?? 0);            // i32 @105 : CP
        }
        // NCID 311 : EnterNodeEquipAndSpell — corps reconstruit en
        // entier : le replay porte l'ÉQUIPEMENT de test3 par conteneur
        // (clés 0..5, bitmap u32 de locs + records item) suivi de sa
        // liste de sorts. Sans reconstruction le panneau profil
        // affichait les items/types d'armure du perso capturé au lieu
        // de l'équipement réel. Format natif vérifié octet par octet
        // sur la capture (Serialize désassemblé, cf. NativeItemCodec).
        if (ncid == 311 && st.CharId != 0)
        {
            // Corps préconstruit par le pipeline d'entrée (Phase 2) —
            // normalisation + octroi des sorts déjà faits hors-boucle.
            // Fallback live si l'équipement a changé depuis l'install
            // (EntryDirty — requêtes comme l'ancien chemin).
            if (st.Entry?.EquipSpellBody is { } prebuilt
                && !st.EntryDirty)
                body = prebuilt;
            else
            {
                await NormalizeEquipmentSlotsAsync(st, token);
                var equipped = await _services.InventoryRepo
                    .GetAllEquipmentEntitiesAsync((int)st.CharId);
                var spells = (await _services.Classes
                        .GetPlayerSpellsAsync((int)st.CharId))
                    .Select(t => ((short)t.Spell, t.Level))
                    .ToList();
                body = Codecs.NativeItemCodec
                    .BuildEnterNodeEquipAndSpell(equipped, spells);
            }
        }
        // NCID 313 : spawn [s16 node][u8][f32 x][f32 z][f32 dir] — la
        // capture a node=2 en dur. Sans patch du node, le client rend la
        // map du replay (2) pendant que le serveur lui envoie les
        // entités du node DB (ex. 1 = Aven) → fusion des NPC/mobs de
        // deux maps (constaté live). Toujours écrire le node courant,
        // la position persistée seulement si elle est connue.
        if (ncid == 313 && body.Length >= 15)
        {
            BitConverter.TryWriteBytes(body.AsSpan(0), st.NodeId);
            if (st.PosX != 0 || st.PosZ != 0)
            {
                BitConverter.TryWriteBytes(body.AsSpan(3), st.PosX);
                BitConverter.TryWriteBytes(body.AsSpan(7), st.PosZ);
                BitConverter.TryWriteBytes(body.AsSpan(11), st.Facing);
            }
        }
        // NCID 302 : le replay porte le mot d'état du perso CAPTURÉ
        // (0x01000000 / 0x01010000 — jamais monté, jamais en combat).
        // Rejoué tel quel au MigrationDone il arrive APRÈS le 302 de
        // migration qui portait le bit9 « mounted » → le client écrase
        // actor+0x3c et perd les records de strafe monture (bug live :
        // « change de map monté = pivot perdu ; ré-équiper répare »).
        // Le natif émet le mot char+0x3a COURANT (NodifyFlagChange) —
        // on réécrit flags/sel/op depuis l'état live ; le seq = un
        // nouvel act (même convention que le 302 de migration).
        if (ncid == 302 && body.Length >= 9)
        {
            var word = PlayerStateWord(st);
            BitConverter.TryWriteBytes(body.AsSpan(4),
                (ushort)(word & 0xffff));       // u16 : bits 0-15 (ride)
            body[6] = (byte)(word >> 16);       // u8 sel : bits 16-23
            body[7] = (byte)(word >> 24);       // u8 op : bits 24-31
            body[8] = st.NextAction();          // u8 : seq char+0x280
        }

        await session.SendPacketAsync(ncid, body, token);
    }
    /// <summary>Normalise le blob d'apparence 32B : la capture native
    /// test3 (humain masculin, rendu correct — zsniff 18:03:25.861)
    /// montre `00*28 + 10 00 04 00` (doc WIRE-EVIDENCE). Le motif
    /// précédent `64 59…` venait d'une lecture à offset faux et rendait
    /// un corps ursun — blob vide ou taille différente → on force la
    /// queue native prouvée.</summary>
    private static void NormalizeAppearance(EntryState st)
    {
        var app = st.Appearance;
        // Blob valide = marqueur `10 00` en [28..29]. Tail `64 59`
        // ([30..31]) = artefact de lecture à offset faux — rendu ursun
        // prouvé ; tails natifs sains : `04 00` (test3), `34 68` (test1).
        if (app[28] == 0x10 && app[29] == 0x00 &&
            !(app[30] == 0x64 && app[31] == 0x59)) return;
        Array.Clear(app, 0, 32);
        app[28] = 0x10; app[30] = 0x04;
    }
    /// <summary>Remplace chaque occurrence de RefCharId (LE) par le charId réel.</summary>
    private static byte[] PatchCharId(byte[] body, uint charId)
    {
        var old = BitConverter.GetBytes(RefCharId);
        for (var i = 0; i <= body.Length - 4; i++)
        {
            if (body[i] != old[0] || body[i + 1] != old[1] ||
                body[i + 2] != old[2] || body[i + 3] != old[3]) continue;
            BitConverter.GetBytes(charId).CopyTo(body, i);
            i += 3;
        }
        return body;
    }
    /// <summary>Remplace le nom "test3" (format u16 len + bytes) par le nom réel.</summary>
    private static byte[] PatchName(byte[] body, string name)
    {
        var oldName = System.Text.Encoding.ASCII.GetBytes("test3");
        var newName = System.Text.Encoding.GetEncoding("GBK").GetBytes(name);
        for (var i = 0; i <= body.Length - 2 - oldName.Length; i++)
        {
            var len = body[i] | (body[i + 1] << 8);
            if (len != oldName.Length) continue;
            var match = true;
            for (var j = 0; j < oldName.Length; j++)
                if (body[i + 2 + j] != oldName[j]) { match = false; break; }
            if (!match) continue;
            var outBuf = new byte[body.Length - oldName.Length + newName.Length];
            body.AsSpan(0, i).CopyTo(outBuf);
            BitConverter.GetBytes((ushort)newName.Length).CopyTo(outBuf, i);
            newName.CopyTo(outBuf, i + 2);
            body.AsSpan(i + 2 + oldName.Length).CopyTo(
                outBuf.AsSpan(i + 2 + newName.Length));
            return outBuf;
        }
        return body;
    }
}
