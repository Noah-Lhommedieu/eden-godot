using Eden.Core.Game.Family;
using Eden.Core.Game.Services;
using Eden.Core.Game.Work;
using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Cycle de vie des bâtiments de production dans les villes de famille
/// (lot E4) — orchestration zone des mécanismes prouvés :
///
/// <list type="bullet">
/// <item><c>*Shop::OpenShop</c> natif (zone @0x0857a080) : à
/// l'ouverture works d'une station de hall, le serveur résout le
/// bâtiment par le NPC parlé (item 0x2a dont +0x9c = net id → base de
/// groupe de la loc → item 0x29), rejette les contrats expirés
/// (due_date ≠ 0 écoulée → 0xc56), et arme op+0x10 = uid bâtiment et
/// op+0x18 = BusinessTax de la famille visitée.</item>
/// <item><c>ToCombine</c>/<c>ToCollect</c> : gates au démarrage
/// (bâtiment absent → 0x251, niveau insuffisant → 0x392, durabilité
/// &lt; 1 → 0xa09) puis <b>usure au démarrage</b> pour les recettes
/// non instantanées (`rand &lt; chance` → `-amount`).</item>
/// <item><c>EndCombine</c>/<c>EndCollect</c>/<c>EndSummon</c> : bloc
/// famille exécuté même sur échec (work+0x28 ≠ 0) — EXP bâtiment
/// (`GainBuildingExpByItemID` → `UpdateBuildingExp` mission : seuil
/// c_level[level+1] cols 12-17, soustraction d'un seuil, template
/// suivant, `level &lt; family.Level`), EXP famille vers le
/// propriétaire de la ville, et <b>usure à la fin</b> pour les
/// instantanés (`rand &lt;= chance`).</item>
/// <item><c>CFamily::CheckBuildingLevelUp</c> (mission @0x081b1f40,
/// appelé par <c>SetLevel</c>) : re-évalue les 0x29 dont l'EXP bankée
/// atteint le seuil, et synchronise tid/level quand la ligne level+1
/// n'existe plus.</item>
/// </list>
///
/// La logique pure vit dans
/// <see cref="FamilyBuildingWorkRules"/> ; ce partiel fait la
/// persistance (<see cref="SocialRepository"/>) et le push du
/// conteneur (484, miroir du ZMis FamilyContainerUpdate natif).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Résout le bâtiment de station courant (op+0x10) dans
    /// le conteneur 0 de la famille visitée. Rend la ligne
    /// <see cref="FamilyBuildingItem"/> ou null (bâtiment disparu —
    /// l'appelant émet 0x251).</summary>
    private async Task<FamilyBuildingItem?> ResolveWorksBuildingAsync(
        EntryState st)
    {
        if (st.WorkOpBuildingItemId == 0
            || st.VisitedFamilyInstanceId == 0
            || _services.Social == null)
            return null;
        var c0 = await _services.Social.GetFamilyBuildingsAsync(
            st.VisitedFamilyInstanceId, 0);
        return c0.FirstOrDefault(b =>
            b.Id == st.WorkOpBuildingItemId
            && b.ItemType == FamilyBuildingWorkRules.BuildingItemType);
    }

    /// <summary>Usure d'un bâtiment — <c>SFamily::UpdateBuildingDurability</c>
    /// → <c>CFamily::UpdateBuildingDurability</c> mission : durabilité
    /// bornée à [0, MaxDurability du template], persistée, puis le
    /// conteneur est repoussé aux membres de la famille présents dans
    /// le hall (ZMis FamilyContainerUpdate natif).</summary>
    private async Task WearBuildingAsync(NetSession session,
        EntryState st, int familyId, FamilyBuildingItem bld,
        int delta, CancellationToken token)
    {
        if (_services.Social == null || delta == 0) return;
        var max = _services.GameData.Buildings
            .TryGetValue(bld.Tid, out var def) ? def.MaxDurability : 0;
        var newDur = (short)Math.Clamp(bld.Durability + delta, 0,
            Math.Max(max, 0));
        if (newDur == bld.Durability) return;
        await _services.Social.UpdateBuildingDurabilityByIdAsync(
            familyId, bld.Id, newDur);
        bld.Durability = newDur;
        EdenLog.Write($"[Zone-Family] Work wear fm={familyId} " +
            $"bld={bld.Id} dur={newDur}");
        await RefreshHallBuildingsAsync(session, st, familyId, token);
    }

    /// <summary>Recharge le conteneur 0 et repousse le 484 au
    /// demandeur + aux membres de la famille migrés sur le node —
    /// équivalent du FamilyContainerUpdate natif poussé après chaque
    /// mutation de bâtiment (usure, level-up).</summary>
    private async Task RefreshHallBuildingsAsync(NetSession session,
        EntryState st, int familyId, CancellationToken token)
    {
        if (_services.Social == null) return;
        var items = await _services.Social.GetFamilyBuildingsAsync(
            familyId, 0);
        await session.SendPacketAsync(484,
            Eden.Core.Game.Family.FamilyPackets.FamilyContainerData(1,
                0, items), token);
        if (_services.Guilds.GetGuild(familyId) is { } guild)
            await PushContainerRefreshToHallMembersAsync(st, guild, 0,
                items, token);
    }

    /// <summary>Bloc famille de fin d'ordre — <c>EndCombine</c>/
    /// <c>EndCollect</c>/<c>EndSummon</c> natifs. Appelé APRÈS le
    /// tirage des produits et la détermination du succès (work.WorkId
    /// &lt; 0 possible : le natif exécute le bloc même sur échec).
    /// Ordre natif : EXP bâtiment → EXP famille (ville visitée) →
    /// usure (recettes instantanées seulement).</summary>
    private async Task CompleteFamilyWorkAsync(NetSession session,
        EntryState st, WorkSlot work, CancellationToken token)
    {
        if (work.FmId == 0 || _services.Social == null) return;
        // Ins+0x460 (familles chargées) + Ins+0x47c (instanciées) —
        // chez nous : la famille existe ET a un hall ouvert.
        var guild = _services.Guilds.GetGuild(work.FmId);
        if (guild == null || guild.HouseOpen == 0)
        {
            EdenLog.Write($"[Zone-Family] Work fini char={st.CharId}" +
                $" : famille {work.FmId} introuvable/non instanciée");
            return;
        }
        var id = Math.Abs(work.WorkId);
        var collect = WorkTypes.IsCollect(work.WorkType)
            && _services.GameData.WorkCollectById
                .TryGetValue(id, out var cw) ? cw : null;
        var combine = WorkTypes.IsCombine(work.WorkType)
            && _services.GameData.WorkCombineById
                .TryGetValue(id, out var cb) ? cb : null;
        if (collect == null && combine == null) return;
        var recipeBldExp = collect?.BuildingExp ?? combine!.BuildingExp;
        var recipeFamExp = collect?.FamilyExp ?? combine!.FamilyExp;
        var instant = collect?.Instant ?? combine!.Instant;
        var wearChance = collect?.BuildingDamageChance
            ?? combine!.BuildingDamageChance;
        var wearAmount = collect?.BuildingDamage ?? combine!.BuildingDamage;

        // 1) EXP bâtiment — GainBuildingExpByItemID natif : l'uid du
        // bâtiment est porté par l'ordre (work+0x2c) ; l'item doit
        // être un 0x29 dont le type de station (S_Item col9 = EquipLoc
        // chez nous) ∈ 0x18..0x1d.
        if (work.BuildingItemId != 0)
        {
            var c0 = await _services.Social.GetFamilyBuildingsAsync(
                work.FmId, 0);
            var bld = c0.FirstOrDefault(b =>
                b.Id == work.BuildingItemId
                && b.ItemType
                    == FamilyBuildingWorkRules.BuildingItemType);
            if (bld != null)
            {
                var station = _services.GameData.Items.FirstOrDefault(
                    i => i.Id == (bld.ItemTid & 0xffff))?.EquipLoc ?? 0;
                if (FamilyBuildingWorkRules.IsStationType(station))
                {
                    var row = _services.GameData.Levels.FirstOrDefault(
                        l => l.Level == bld.Level + 1);
                    var threshold = row != null
                        ? row.BuildingExps[FamilyBuildingWorkRules
                            .StationIndex(station)]
                        : 0;
                    var (nextTid, nextLevel) =
                        NextBuildingTemplate(bld.Tid);
                    var gain = FamilyBuildingWorkRules.BuildingExpGain(
                        work.ExtBuildingRate, recipeBldExp);
                    var r = FamilyBuildingWorkRules.ApplyBuildingExp(
                        bld.Exp, bld.Tid, bld.Level, guild.Level,
                        threshold, gain, nextTid, nextLevel);
                    if (r.Changed)
                    {
                        bld.Exp = r.Exp; bld.Tid = r.Tid;
                        bld.Level = (short)r.Level;
                        await _services.Social
                            .UpdateBuildingProgressAsync(work.FmId,
                                bld.Id, r.Tid, (short)r.Level, r.Exp);
                        if (r.LeveledUp)
                            EdenLog.Write($"[Zone-Family] Bâtiment " +
                                $"{bld.Id} fm={work.FmId} → tpl {r.Tid}" +
                                $" lv{r.Level}");
                        await RefreshHallBuildingsAsync(session, st,
                            work.FmId, token);
                    }
                }
            }
        }

        // 2) EXP famille — SFamily::GainExp vers la famille de la
        // ville (work+0x28), pas celle du joueur. Un level-up déclenche
        // la re-évaluation des bâtiments (CheckBuildingLevelUp via
        // SetLevel natif).
        if (recipeFamExp > 0)
        {
            var before = guild.Level;
            await _services.Guilds.AddExperienceAsync(work.FmId,
                recipeFamExp);
            if (guild.Level != before)
                await EvaluateFamilyBuildingsAsync(session, st, guild,
                    token);
        }

        // 3) Usure des recettes instantanées — End* natif :
        // rand <= chance → -amount (forme inclusive).
        if (instant && work.BuildingItemId != 0 && wearAmount > 0
            && FamilyBuildingWorkRules.RollWear(Random.Shared,
                wearChance, inclusive: true))
        {
            var c0 = await _services.Social.GetFamilyBuildingsAsync(
                work.FmId, 0);
            var bld = c0.FirstOrDefault(b =>
                b.Id == work.BuildingItemId
                && b.ItemType
                    == FamilyBuildingWorkRules.BuildingItemType);
            if (bld != null)
                await WearBuildingAsync(session, st, work.FmId, bld,
                    -wearAmount, token);
        }
    }

    /// <summary><c>CFamily::CheckBuildingLevelUp</c> natif — appelé par
    /// <c>SetLevel</c> : re-évalue les bâtiments de production du
    /// conteneur 0 après une hausse de niveau de la famille (seuil
    /// atteint → level-up, ou synchronisation tid/level quand la ligne
    /// c_level[level+1] n'existe plus). Persiste chaque item modifié
    /// puis repousse le conteneur.</summary>
    private async Task EvaluateFamilyBuildingsAsync(NetSession session,
        EntryState st, GuildService.Guild guild, CancellationToken token)
    {
        if (_services.Social == null) return;
        var c0 = await _services.Social.GetFamilyBuildingsAsync(
            guild.Id, 0);
        var changed = false;
        foreach (var bld in c0)
        {
            if (bld.ItemType != FamilyBuildingWorkRules.BuildingItemType)
                continue;
            var station = _services.GameData.Items.FirstOrDefault(
                i => i.Id == (bld.ItemTid & 0xffff))?.EquipLoc ?? 0;
            if (!FamilyBuildingWorkRules.IsStationType(station))
                continue;
            var row = _services.GameData.Levels.FirstOrDefault(
                l => l.Level == bld.Level + 1);
            var threshold = row != null
                ? row.BuildingExps[FamilyBuildingWorkRules
                    .StationIndex(station)]
                : 0;
            var (nextTid, nextLevel) = NextBuildingTemplate(bld.Tid);
            var r = FamilyBuildingWorkRules.EvaluateLevelUp(bld.Exp,
                bld.Tid, bld.Level, guild.Level, row != null,
                threshold, nextTid, nextLevel);
            if (!r.Changed) continue;
            bld.Exp = r.Exp; bld.Tid = r.Tid;
            bld.Level = (short)r.Level;
            await _services.Social.UpdateBuildingProgressAsync(
                guild.Id, bld.Id, r.Tid, (short)r.Level, r.Exp);
            changed = true;
            EdenLog.Write($"[Zone-Family] CheckLevelUp fm={guild.Id} " +
                $"bld={bld.Id} → tpl {r.Tid} lv{r.Level}");
        }
        if (changed)
            await RefreshHallBuildingsAsync(session, st, guild.Id, token);
    }

    /// <summary>Template suivant de la chaîne S_Buliding — la colonne
    /// « précédent » (col5/PrevTid) de la table forme une chaîne dont
    /// le natif mémorise le pointeur <c>building+0x30</c>. En cas de
    /// candidats multiples on prend le niveau le plus bas (fidélité à
    /// l'ordre de fichier de la table).</summary>
    private (int Tid, int Level) NextBuildingTemplate(int tid)
    {
        var next = _services.GameData.Buildings
            .Where(kv => kv.Value.PrevTid == tid)
            .OrderBy(kv => kv.Value.Level).ThenBy(kv => kv.Key)
            .Select(kv => (Tid: kv.Key, Level: kv.Value.Level))
            .FirstOrDefault();
        return next;
    }
}
