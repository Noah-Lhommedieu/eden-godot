using Eden.Core.Database.Repositories;
using Eden.Core.Logging;
using Eden.Zone.Combat;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    // ==================== Chemin d'éveil (destiny road) ====================
    //
    // Transcription du sous-système natif :
    //   CPlayerFactory::DestinyRoadAction  zone @0x0810d610 (dispatch op 0-4)
    //   CPlayerFactory::DestinyDiceEvent   zone @0x0810d190 (effet de case)
    //   CPlayerFactory::NotifyUpdateDestinyRoad zone @0x08104710
    //     (= 589 ClientUpdateDestinyRoad [s16 type][s16 v1][s16 v2]
    //      + commande DBA « Update DestinyRoad type=%hd value1=%hd
    //      value2=%hd » — dans notre architecture la zone persiste
    //      directement via DestinyRoadRepository, pas de relais
    //      mission : la mission C# n'a pas de CExecutionerData).
    //   CCharacter::CreateDestinyRoad      zone @0x084222e0 / @0x084218b0
    //   CCharacter::UseDestinyRoadItem     zone @0x08422520
    //   CCharacter::UseDestinyFunction     zone @0x08418430
    //   CCharacter::GainDp                 zone @0x0840b0c0
    //   CMonster::GiveDp / GiveTeamDp      zone @0x08491850 / @0x8493fxx
    //   CPlayerFactory::DestingBornMonster zone @0x0810ce70
    //   CEventDestinyVitAdd::Execute       zone @0x08198050 (+1 vit / 72 s)
    //   SDestinyRoad::Serialize            zone @0x0863b750 (NCID 534)
    //   CExecutionerData::UpdateDestinyRoad mission @0x083331f0 — types :
    //     0=step, 1=dice_time, 2=exp_buff, 3=dice_point,
    //     4=function_state, 5=slot[v2]=v1, 6=flag reroll, 7=destiny_vit.

    /// <summary>Scènes où les actions destin 0/1/4 sont permises —
    /// natif : `scene type ∈ {1,3,4}` sinon texte 0x184e + 590.</summary>
    private bool DestinySceneAllowed(EntryState st)
    {
        var t = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1;
        return t is 1 or 3 or 4;
    }

    /// <summary>Scène instance-versus (type 0x11) — refusée à l'op 4
    /// natif (byte0x3b&amp;0x40 → texte 0xe9a + 590[4]).</summary>
    private bool InVersusScene(EntryState st)
        => (_services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 1) == 0x11;

    /// <summary>Coût en Destiny Points d'une avancée —
    /// S_Parameter 376 (`AllFormulaParas+0x5e0` natif, 1250).</summary>
    private int DestinyDiceCost()
        => (int)NativeCombatFormulas.ResolveParameters(
            _services.GameData.Parameters).DpRollCost;

    /// <summary>Cap de vitalité destin = 100 × S_Parameter 569
    /// (CEventDestinyVitAdd : `100.0f × paras+0x8e4` → 1000 natif).</summary>
    private int DestinyVitCap()
        => (int)(100f * NativeCombatFormulas.ResolveParameters(
            _services.GameData.Parameters).DpVitCapMul);

    /// <summary>Cap de la réserve DP = S_Parameter 570 × 376
    /// (GainDp natif : `paras+0x8e8 × paras+0x5e0` → 12500).</summary>
    private int DestinyDpCap()
    {
        var p = NativeCombatFormulas.ResolveParameters(
            _services.GameData.Parameters);
        return (int)(p.DpPoolCapMul * p.DpRollCost);
    }

    // ------------------------- wire helpers -------------------------

    /// <summary>589 CNC_CZ_ClientUpdateDestinyRoad — l'équivalent du
    /// NotifyUpdateDestinyRoad natif (type + 2 s16). Les mutations de
    /// l'état sont ensuite persistées en batch par
    /// <see cref="SaveDestinyAsync"/> — la commande DBA texte du natif
    /// n'a pas d'équivalent nécessaire dans notre architecture.</summary>
    private static Task NotifyDestinyAsync(NetSession session,
        short type, short v1, short v2, CancellationToken token)
        => session.SendAsync(new CNC_CZ_ClientUpdateDestinyRoad
        {
            Field1 = type, Field2 = v1, Field3 = v2,
        }, token);

    /// <summary>534 CNC_CZ_ClientPlayerDestinyRoad — sérialisation
    /// `SDestinyRoad` complète (route active ou bloc Reset après une
    /// fin de route — le client affiche le plateau ou le vide).</summary>
    private static Task SendDestinyRoadAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var d = st.Destiny;
        var cmd = new CNC_CZ_ClientPlayerDestinyRoad();
        if (d != null)
        {
            cmd.RoadId = d.RoadId;
            cmd.RoadType = d.RoadType;
            cmd.Step = d.Step;
            cmd.RollsLeft = d.RollsLeft;
            cmd.DpPool = d.DpPool;
            cmd.MovePoints = d.MovePoints;
            cmd.FunctionState = d.FunctionState;
            cmd.RerollFlag = d.RerollFlag;
            cmd.Slots = d.Slots;
        }
        return session.SendAsync(cmd, token);
    }

    /// <summary>590 CNC_CZ_ClientDestinyDiceFailed [s16 op].</summary>
    private static Task SendDestinyFailAsync(NetSession session,
        short op, CancellationToken token)
        => session.SendAsync(new CNC_CZ_ClientDestinyDiceFailed
        {
            Value = op,
        }, token);

    /// <summary>Texte serveur (vcall +0x58 natif →
    /// CNC_CZ_ClientTextOutputIndex — param 0xffffff97/0x88cc8f9 =
    /// le canal système des messages destin).</summary>
    private static Task SendDestinyTextAsync(NetSession session,
        int textId, CancellationToken token)
        => session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = (short)textId, Field2 = "",
        }, token);

    // ------------------------- persistance -------------------------

    /// <summary>Copie l'état runtime vers une ligne de dépôt.</summary>
    private static DestinyRoadRow ToRow(EntryState st)
    {
        var d = st.Destiny;
        var row = new DestinyRoadRow { PlayerId = (int)st.CharId };
        if (d != null)
        {
            row.RoadId = d.RoadId;
            row.RoadType = d.RoadType;
            row.Step = d.Step;
            row.RollsLeft = d.RollsLeft;
            row.DpPool = d.DpPool;
            row.MovePoints = d.MovePoints;
            row.FunctionState = d.FunctionState;
            for (var i = 0; i < 39; i++) row.Slots[i] = d.Slots[i];
        }
        return row;
    }

    /// <summary>UPDATE/INSERT `player_destiny_road` sur la file de
    /// persistance ordonnée du personnage — équivalent du
    /// `UpdateDestinyRoadToDB` natif (une ligne = tout le bloc).</summary>
    private async Task SaveDestinyAsync(EntryState st)
    {
        var row = ToRow(st);
        await _persistence.EnqueueAsync((int)st.CharId,
            ct => _services.DestinyRepo.UpsertAsync(row));
    }

    /// <summary>`Clear DestinyRoad` natif — DELETE de la ligne à la
    /// fin d'une route (TC_ClearDestinyRoad @mission 0x081f7cf0).</summary>
    private async Task ClearDestinyAsync(EntryState st)
        => await _persistence.EnqueueAsync((int)st.CharId,
            ct => _services.DestinyRepo.DeleteAsync((int)st.CharId));

    /// <summary>Persiste `player_characters.destiny_vit` — le champ
    /// exec+0x6b6 du gros UPDATE natif (type 7, « char dirty »).</summary>
    private async Task PersistVitAsync(EntryState st)
    {
        var vit = st.DestinyVit;
        await _persistence.EnqueueAsync((int)st.CharId,
            ct => _services.DestinyRepo.SetVitalityAsync(
                (int)st.CharId, vit));
    }

    // ------------------------- chargement -------------------------

    /// <summary>Charge la route persistée + la vitalité à l'entrée en
    /// zone — équivalent du `LoadDestinyRoad` natif (SELECT des 46
    /// colonnes ; route absente → bloc Reset, pas de ligne).</summary>
    private async Task LoadDestinyAsync(EntryState st,
        CancellationToken token)
    {
        st.DestinyVit = await _services.DestinyRepo
            .GetVitalityAsync((int)st.CharId);
        var row = await _services.DestinyRepo
            .GetAsync((int)st.CharId);
        if (row == null || row.RoadId == 0) return;
        var d = new DestinyState
        {
            RoadId = (short)row.RoadId,
            RoadType = (short)row.RoadType,
            Step = (short)row.Step,
            RollsLeft = (short)row.RollsLeft,
            DpPool = row.DpPool,
            MovePoints = (short)row.MovePoints,
            FunctionState = (short)row.FunctionState,
        };
        for (var i = 0; i < 39; i++) d.Slots[i] = (short)row.Slots[i];
        st.Destiny = d;
    }

    // ------------------------- vit regen -------------------------

    /// <summary>`CEventDestinyVitAdd` natif — créé quand une route est
    /// lancée (op 0, char+0xbec). +1 vitalité toutes les 72 s jusqu'au
    /// cap (100×param569), puis l'événement se termine (char+0xbec=0).</summary>
    private void EnsureDestinyVitRegen(EntryState st)
    {
        if (st.DestinyVitRegen || st.DestinyVit >= DestinyVitCap())
            return;
        st.DestinyVitRegen = true;
        ScheduleOnLoop(TimeSpan.FromSeconds(72),
            ct => DestinyVitTickAsync(st.CharId, ct),
            st.LoopCts?.Token ?? default);
    }

    /// <summary>Tick de régén — retrouve l'état live par charId (la
    /// session peut avoir changé entre deux ticks). Au cap : vit
    /// figée + Notify(7) final et l'événement s'arrête
    /// (DestinyVitRegen = false, comme char+0xbec = 0).</summary>
    private async Task DestinyVitTickAsync(uint charId,
        CancellationToken ct)
    {
        var st = _states.Values.FirstOrDefault(
            s => s.CharId == charId && s.Migrated);
        if (st == null) return;
        if (!st.DestinyVitRegen) return;
        var cap = DestinyVitCap();
        st.DestinyVit++;
        if (st.DestinyVit >= cap)
        {
            st.DestinyVit = cap;
            st.DestinyVitRegen = false;
        }
        if (st.Session != null)
            await NotifyDestinyAsync(st.Session, 7,
                (short)st.DestinyVit, 0, ct);
        await PersistVitAsync(st);
        if (st.DestinyVitRegen)
            ScheduleOnLoop(TimeSpan.FromSeconds(72),
                c => DestinyVitTickAsync(charId, c),
                st.LoopCts?.Token ?? default);
    }

    // ------------------------- handlers -------------------------

    /// <summary>NCID 203 `CNE_CZ_ZoneServerDestinyDice` [s16 op]
    /// [s16 p2][s16 p3] → `DestinyRoadAction` natif.
    /// op 0 créer (vit ≥100), 1 lancer, 2 item (p2=itemId p3=index),
    /// 3 fonction/choix de dé (p2), 4 avancer.</summary>
    private async Task HandleDestinyDiceAsync(NetSession session,
        EntryState st, short op, short p2, short p3,
        CancellationToken token)
    {
        if (!st.Migrated) return;
        // Gate de scène natif : op ≠ 2 et 3 exigent scene_type ∈
        // {1,3,4} (champs/donjons) → texte 0x184e + 590[op] sinon.
        if (op is not (2 or 3) && !DestinySceneAllowed(st))
        {
            await SendDestinyTextAsync(session, 0x184e, token);
            await SendDestinyFailAsync(session, op, token);
            return;
        }
        switch (op)
        {
            case 0:
                await DestinyOpCreateAsync(session, st, token);
                break;
            case 1:
                await DestinyOpRollAsync(session, st, token);
                break;
            case 2:
                await DestinyOpItemAsync(session, st, p2, p3, token);
                break;
            case 3:
                await DestinyOpFunctionAsync(session, st, p2, token);
                break;
            case 4:
                await DestinyOpMoveAsync(session, st, token);
                break;
            default:
                // Le switch natif ignore tout autre op silencieusement.
                break;
        }
    }

    /// <summary>NCID 204 `CNE_CZ_ZoneServerQueryDestinyRoad` — le
    /// natif renvoie le 534 complet UNIQUEMENT si une route existe
    /// (char+0xbc8 != 0, Execute @0x08275db0).</summary>
    private static Task HandleQueryDestinyRoadAsync(NetSession session,
        EntryState st, CancellationToken token)
        => st.Destiny is { RoadId: not 0 }
            ? SendDestinyRoadAsync(session, st, token)
            : Task.CompletedTask;

    /// <summary>Op 0 — créer la route : road==0 requis (0xf0e sinon),
    /// vit &gt; 99 (0x186e sinon). `CreateDestinyRoad()` + vit−100 +
    /// démarre la régén vit + Notify(7,vit) (zone @0x810d810).</summary>
    private async Task DestinyOpCreateAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.Destiny is { RoadId: not 0 })
        {
            await SendDestinyTextAsync(session, 0xf0e, token);
            return;
        }
        if (st.DestinyVit <= 99)
        {
            await SendDestinyTextAsync(session, 0x186e, token);
            return;
        }
        CreateDestinyRoad(st);
        st.DestinyVit -= 100;
        // char+0xbec natif : la régén vit n'est armée qu'ici.
        EnsureDestinyVitRegen(st);
        await NotifyDestinyAsync(session, 7, (short)st.DestinyVit,
            0, token);
        // Le natif envoie la route complète à la création
        // (534 construit à la fin de CreateDestinyRoad).
        await SendDestinyRoadAsync(session, st, token);
        await SaveDestinyAsync(st);
        await PersistVitAsync(st);
    }

    /// <summary>Op 1 — lancer le dé : road requis (0xf0f), dés
    /// restants + DP ≥ coût (0xf12). dice−−, move = 1+rand%6,
    /// Notify(3,move) + Notify(1,dice) (zone @0x810ddcc).</summary>
    private async Task DestinyOpRollAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var d = st.Destiny;
        if (d == null || d.RoadId == 0)
        {
            await SendDestinyTextAsync(session, 0xf0f, token);
            return;
        }
        if (d.RollsLeft <= 0 || d.DpPool < DestinyDiceCost())
        {
            await SendDestinyTextAsync(session, 0xf12, token);
            return;
        }
        d.RollsLeft--;
        d.MovePoints = (short)(1 + Random.Shared.Next(6));
        await NotifyDestinyAsync(session, 3, d.MovePoints, 0, token);
        await NotifyDestinyAsync(session, 1, d.RollsLeft, 0, token);
        await SaveDestinyAsync(st);
    }

    /// <summary>Op 3 — fonction ou choix du dé : si le flag reroll
    /// est armé (item f1), `move = p2+1` et Notify(3)+Notify(6,0)
    /// (zone @0x810db48). Sinon `(func &gt;&gt; p2) &amp; 1` déjà
    /// utilisé → rien ; sinon `UseDestinyFunction(p2)` + bit posé +
    /// Notify(4,func) (zone @0x810d6d0).</summary>
    private async Task DestinyOpFunctionAsync(NetSession session,
        EntryState st, short p2, CancellationToken token)
    {
        var d = st.Destiny;
        if (d == null || d.RoadId == 0) return;
        if (d.RerollFlag != 0)
        {
            d.RerollFlag = 0;
            d.MovePoints = (short)(p2 + 1);
            await NotifyDestinyAsync(session, 3, d.MovePoints, 0,
                token);
            await NotifyDestinyAsync(session, 6, 0, 0, token);
            await SaveDestinyAsync(st);
            return;
        }
        if (p2 is < 0 or > 15) return;
        if (((d.FunctionState >> p2) & 1) != 0) return;
        var old = d.MovePoints;
        d.MovePoints = ApplyDestinyFunction(p2, d.MovePoints);
        if (d.MovePoints != old)
            await NotifyDestinyAsync(session, 3, d.MovePoints, 0,
                token);
        d.FunctionState = (short)(d.FunctionState | (short)(1 << p2));
        await NotifyDestinyAsync(session, 4, d.FunctionState, 0,
            token);
        await SaveDestinyAsync(st);
    }

    /// <summary>`UseDestinyFunction` natif — transforme dice_point :
    /// f0 ±1, f1 ×2, f2 +3, f3 −4, f4 =5, f5 = rand 1-6 ; borné 1-6
    /// (zone @0x08418430). Renvoie la nouvelle valeur.</summary>
    private static short ApplyDestinyFunction(short func, short move)
        => func switch
        {
            0 => ClampDice((short)(move +
                (Random.Shared.Next(2) == 1 ? 1 : -1))),
            1 => ClampDice((short)(move * 2)),
            2 => ClampDice((short)(move + 3)),
            3 => ClampDice((short)(move - 4)),
            4 => (short)5,
            5 => (short)(1 + Random.Shared.Next(6)),
            _ => move,
        };

    private static short ClampDice(short v)
        => v < 1 ? (short)1 : v > 6 ? (short)6 : v;

    /// <summary>Op 4 — avancer : mort (0xf10) ou versus (0xe9a) →
    /// 590[4]. move==0 → log natif seul. Sinon step += move puis
    /// `DestinyDiceEvent` ; fin → Reset + clear DB + 534 du bloc vide ;
    /// sinon move=0, DP −= coût, Notify(0,step)+(3,0)+(2,dp)
    /// (zone @0x810d9bc / @0x810dc98).</summary>
    private async Task DestinyOpMoveAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var d = st.Destiny;
        // Ordre natif : dead → 0xf10, versus → 0xe9a (+590[4] chacun),
        // move==0 → AssertLog seul — PAS de check road_id dans op4
        // (le bloc Reset garantit move==0 quand il n'y a pas de route).
        if (st.Dead)
        {
            await SendDestinyTextAsync(session, 0xf10, token);
            await SendDestinyFailAsync(session, 4, token);
            return;
        }
        if (InVersusScene(st))
        {
            await SendDestinyTextAsync(session, 0xe9a, token);
            await SendDestinyFailAsync(session, 4, token);
            return;
        }
        if (d == null || d.RoadId == 0 || d.MovePoints == 0) return;
        d.Step = (short)(d.Step + d.MovePoints);
        var finished = await DestinyDiceEventAsync(session, st, token);
        if (finished)
        {
            d.Reset();
            st.Destiny = null;
            await ClearDestinyAsync(st);
            // Le natif envoie le bloc Reset complet (534) — le client
            // vide le plateau.
            await SendDestinyRoadAsync(session, st, token);
            return;
        }
        d.MovePoints = 0;
        d.DpPool -= DestinyDiceCost();
        await NotifyDestinyAsync(session, 0, d.Step, 0, token);
        await NotifyDestinyAsync(session, 3, 0, 0, token);
        // Le natif passe la réserve tronquée s16 sur le fil.
        await NotifyDestinyAsync(session, 2, (short)d.DpPool, 0,
            token);
        await SaveDestinyAsync(st);
    }

    // ------------------------- événements de case -------------------------

    /// <summary>`DestinyDiceEvent` natif — envoie 535 [eventId] puis
    /// applique l'effet de la case `slots[min(step,38)]` ; renvoie
    /// true si la route se termine (step ≥ 0x26 ou plus de dés —
    /// step&lt;0x26→0xf14, ==0x26→0xf15, &gt;0x26→0xf13).</summary>
    private async Task<bool> DestinyDiceEventAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var d = st.Destiny;
        if (d == null) return false;
        var idx = Math.Min((int)d.Step, 38);
        var eventId = idx >= 0 && idx < d.Slots.Length
            ? d.Slots[idx] : (short)0;
        await session.SendAsync(new CNC_CZ_ClientPlayerDestinyRoadEvent
        {
            Value = eventId,
        }, token);
        var ev = _services.GameData.GetDestinyEvent(eventId);
        if (ev != null)
            await ApplyDestinyEventAsync(session, st, ev, token);
        if (d.Step < 0x26 && d.RollsLeft > 0)
            return false;
        EdenLog.Write($"[Destiny] char={st.CharId} route finie " +
            $"road={d.RoadId} dice={d.RollsLeft} step={d.Step}");
        if (d.Step < 0x26)
            await SendDestinyTextAsync(session, 0xf14, token);
        else if (d.Step > 0x26)
            await SendDestinyTextAsync(session, 0xf13, token);
        else
            await SendDestinyTextAsync(session, 0xf15, token);
        return true;
    }

    /// <summary>Applique l'effet de la case — types natifs :
    /// 1 or, 2 exp, 3 NP+points de classe, 4/6 monstre hostile,
    /// 5 enchant, 7 dés supplémentaires (DestinyDiceEvent @0x810d190).</summary>
    private async Task ApplyDestinyEventAsync(NetSession session,
        EntryState st,
        Eden.Core.Data.Parsers.DestinyDataParser.DestinyEventDef ev,
        CancellationToken token)
    {
        var d = st.Destiny!;
        var value = (int)Math.Min(ev.Value, int.MaxValue);
        switch (ev.Type)
        {
            case 1: // GainGold(value) — 280 attr 2 + persistance.
                if (value > 0)
                {
                    st.Gold = (int)await _services.Characters
                        .AddGoldAsync((int)st.CharId, value);
                    await SendAttrAsync(session, st.CharId, 0x02,
                        st.Gold, 0, token);
                }
                break;
            case 2: // GainExp(value).
                if (value > 0)
                    await GrantCharExpAsync(session, st, value);
                break;
            case 3: // GainNp + GainClassPoint — notre grant couvre
                    // les deux (exp de classe + skill points).
                if (value > 0)
                    await GrantClassPointAsync(session, st, value);
                break;
            case 4:
            case 6: // DestingBornMonster(value, param2) — mobs
                    // hostiles sur la position du joueur, aggro
                    // immédiate, expiration param 363 s.
                {
                    var mobId = value;
                    var count = Math.Max(1, (int)ev.Param2);
                    if (mobId > 0 &&
                        _services.Live.MonsterDefs.ContainsKey(mobId))
                    {
                        var life = TimeSpan.FromSeconds(
                            Math.Max(1, NativeCombatFormulas
                                .ResolveParameters(
                                    _services.GameData.Parameters)
                                .SummonMobLifespan));
                        for (var i = 0; i < count; i++)
                        {
                            var mob = _services.Live.SpawnHostileMob(
                                st.NodeId, st.InstanceGroup, mobId,
                                (int)st.CharId,
                                st.PosX, 0f, st.PosZ, st.Facing);
                            ScheduleOnLoop(life,
                                ct => DespawnDestinyMobAsync(
                                    mob.Handle, ct),
                                st.LoopCts?.Token ?? default);
                        }
                    }
                    break;
                }
            case 5: // EffectToLife(char, char, effectId, durTicks, …)
                    // — durée en ticks (0.1 s) → secondes.
                if (value > 0 && _services.Enchants.Get(value) != null)
                    await AddPlayerEnchantAsync(session, st,
                        (int)st.CharId, value,
                        (int)(ev.Param2 / 10), 1, token);
                break;
            case 7: // dice_time += value (borné ≥0 natif).
                d.RollsLeft = (short)Math.Max(0,
                    d.RollsLeft + value);
                break;
        }
    }

    /// <summary>Expiration d'un mob d'event (param 363 = 30 s
    /// natif) — le despawn passe par le chemin pet (owner posé).</summary>
    private Task DespawnDestinyMobAsync(uint handle,
        CancellationToken ct)
    {
        _services.Live.DespawnPet(handle);
        return Task.CompletedTask;
    }

    // ------------------------- items destiny (op 2) -------------------------

    /// <summary>Op 2 — utiliser un item destin :
    /// `GetCloneItemFromContainer(char, 0, itemId, index)` →
    /// GetItemKind()==0x18 + quantité &gt; 0 →
    /// `UseDestinyRoadItem(item+0x138, +0x13a, +0x13c)` → succès →
    /// `ItemConsume` (zone @0x810d730/d7a4/dd45).</summary>
    private async Task DestinyOpItemAsync(NetSession session,
        EntryState st, short itemId, short index,
        CancellationToken token)
    {
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        // map conteneur native clé = item_id : l'index sélectionne
        // le n-ième duplicata (param_3 natif).
        var matches = items
            .Where(i => i.ItemId == itemId && !i.IsEquipped &&
                i.Quantity > 0)
            .ToList();
        if (index < 0 || index >= matches.Count) return;
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == itemId);
        if (def == null || def.Kind != 0x18) return;
        if (await UseDestinyRoadItemAsync(session, st,
            (short)def.DestinyFunc, (short)def.DestinyParam2,
            (short)def.DestinyParam3, token))
        {
            // ItemConsume natif — retire 1 de l'emplacement exact
            // (le n-ième duplicata choisi par param_3), puis 275.
            var it = matches[index];
            if (await _services.Inventory.RemoveItemAsync(
                    (int)st.CharId, it.BagId, it.Slot, 1))
            {
                var b275 = new byte[6];
                BitConverter.GetBytes((short)it.BagId).CopyTo(b275, 0);
                BitConverter.GetBytes((short)it.Slot).CopyTo(b275, 2);
                BitConverter.GetBytes((short)1).CopyTo(b275, 4);
                await session.SendPacketAsync(275, b275, token);
                _services.NotifyItemChanged((int)st.CharId, itemId);
            }
            await SaveDestinyAsync(st);
        }
    }

    /// <summary>`UseDestinyRoadItem` natif (zone @0x08422520) —
    /// gate : sans route seuls les types 2/6/7 passent (0x18e2
    /// sinon). Renvoie true = l'item est consommé.</summary>
    private async Task<bool> UseDestinyRoadItemAsync(NetSession session,
        EntryState st, short func, short p2, short p3,
        CancellationToken token)
    {
        var hasRoad = st.Destiny is { RoadId: not 0 };
        if (!hasRoad && func is not (2 or 6 or 7))
        {
            await SendDestinyTextAsync(session, 0x18e2, token);
            return false;
        }
        switch (func)
        {
            case 0: // dice_time += p2 → Notify(1).
                if (st.Destiny == null) return false;
                st.Destiny.RollsLeft += p2;
                await NotifyDestinyAsync(session, 1,
                    st.Destiny.RollsLeft, 0, token);
                return true;
            case 1: // arme le choix du dé — exige un dé en attente.
                if (st.Destiny == null) return false;
                if (st.Destiny.MovePoints == 0)
                {
                    await SendDestinyTextAsync(session, 0x185f, token);
                    return false;
                }
                st.Destiny.RerollFlag = 1;
                await NotifyDestinyAsync(session, 6, 1, 0, token);
                return true;
            case 2: // route imposée — 534 natif émis par
                    // CreateDestinyRoad.
                CreateDestinyRoad(st, p2);
                if (st.Destiny != null)
                    await SendDestinyRoadAsync(session, st, token);
                return true;
            case 3: // route aléatoire du tier.
                CreateDestinyRoad(st);
                if (st.Destiny != null)
                    await SendDestinyRoadAsync(session, st, token);
                return true;
            case 4: // réarme les fonctions — exige func != 0.
                if (st.Destiny == null) return false;
                if (st.Destiny.FunctionState == 0)
                {
                    await SendDestinyTextAsync(session, 0x185e, token);
                    return false;
                }
                st.Destiny.FunctionState = 0;
                await NotifyDestinyAsync(session, 4, 0, 0, token);
                return true;
            case 5: // re-tire p2 cases suivantes — exige step+1&lt;0x27.
                return await RerollDestinyCellsAsync(session, st, p2,
                    token);
            case 6: // enchant p2 pendant p3 minutes (p3×600 ticks,
                    // niveau 0x20 natif).
                if (p2 > 0 && _services.Enchants.Get(p2) != null)
                    await AddPlayerEnchantAsync(session, st,
                        (int)st.CharId, p2, p3 * 60, 0x20, token);
                return true;
            case 7: // vitalité += p2×100, cap 100×param569.
                st.DestinyVit = Math.Min(DestinyVitCap(),
                    st.DestinyVit + p2 * 100);
                await NotifyDestinyAsync(session, 7,
                    (short)st.DestinyVit, 0, token);
                await PersistVitAsync(st);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Item f5 — re-tire les `count` cases suivantes depuis
    /// les groupes de la route (`roadDef.Cells[step+i]`), event pioché
    /// uniformément dans `DestinyEventGroupMap[group]` → Notify(5,
    /// eventId, slotIndex) par case (zone @0x8422a00).</summary>
    private async Task<bool> RerollDestinyCellsAsync(NetSession session,
        EntryState st, short count, CancellationToken token)
    {
        var d = st.Destiny;
        if (d == null || count <= 0 || d.Step + 1 >= 0x27) return false;
        var def = _services.GameData.GetDestinyRoad(d.RoadId);
        if (def == null) return false;
        for (var i = 1; i <= count && d.Step + i < 0x27; i++)
        {
            var cell = d.Step + i;
            if (!_services.GameData.DestinyEventGroupMap
                    .TryGetValue(def.Cells[cell], out var ids)
                || ids.Count == 0)
                continue;
            var pick = (short)ids[Random.Shared.Next(ids.Count)];
            d.Slots[cell] = pick;
            await NotifyDestinyAsync(session, 5, pick, (short)cell,
                token);
        }
        await SaveDestinyAsync(st);
        return true;
    }

    // ------------------------- création de route -------------------------

    /// <summary>`CreateDestinyRoad()` — tier = level/10, route piochée
    /// uniformément dans `DestinyRoadLevelMap[tier]` (lower_bound
    /// natif : plus grand tier ≤ level/10 ; zone @0x084222e0).</summary>
    private void CreateDestinyRoad(EntryState st)
    {
        var map = _services.GameData.DestinyRoadLevelMap;
        if (map.Count == 0) return;
        var tier = st.Level / 10;
        var key = map.Keys.Where(k => k <= tier).DefaultIfEmpty(-1)
            .Max();
        if (key < 0 || !map.TryGetValue(key, out var ids) ||
            ids.Count == 0)
            return;
        CreateDestinyRoad(st, (short)ids[
            Random.Shared.Next(ids.Count)]);
    }

    /// <summary>`CreateDestinyRoad(id)` — Reset complet puis
    /// road_id, road_type = rand%3, step=0, dice=10 ; les 39 cases
    /// sont tirées depuis les groupes de la route
    /// (zone @0x084218b0) ; envoie le 534 complet + persiste.</summary>
    private void CreateDestinyRoad(EntryState st, short roadId)
    {
        var def = _services.GameData.GetDestinyRoad(roadId);
        if (def == null)
        {
            EdenLog.Write($"[Destiny] char={st.CharId} road={roadId} " +
                "inconnu (AssertLog natif)");
            return;
        }
        var d = new DestinyState
        {
            RoadId = roadId,
            RoadType = (short)Random.Shared.Next(3),
            Step = 0,
            RollsLeft = 10,
        };
        for (var i = 0; i < 39; i++)
        {
            if (_services.GameData.DestinyEventGroupMap
                    .TryGetValue(def.Cells[i], out var ids)
                && ids.Count > 0)
                d.Slots[i] = (short)ids[Random.Shared.Next(ids.Count)];
        }
        st.Destiny = d;
    }

    // ------------------------- Destiny Points au kill -------------------------

    /// <summary>`CMonster::GiveDp`/`GiveTeamDp` natifs — DP de kill
    /// sur la route active. Solo : `round(p375 × DesDeclineRate)`.
    /// Team : même distribution que GiveTeamExp — scaled =
    /// `round(p375 × ExpDeclineRate(maxLv,mobLv))`, parts
    /// ExpForTeam/ExpForTeamNoShare ; type 4 → maps 0xc seulement ;
    /// capacité 6-10 → maps 7 seulement (zone @0x8493fxx).</summary>
    private async Task DistributeKillDpAsync(EntryState st,
        ZoneLiveWorld.LiveMob mob)
    {
        var p = NativeCombatFormulas.ResolveParameters(
            _services.GameData.Parameters);
        var mdef = _services.Live.MonsterDefs
            .GetValueOrDefault(mob.MobId);
        // mobdef+0x12 == 7 → pas de DP natif (type non-combat).
        if (mdef != null && mdef.MonsterType == 7) return;
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null || team.TeamType == 3)
        {
            await DestinyGainDpAsync(st, (int)Math.Round(
                p.DpPerKill * NativeCombatFormulas.DesDeclineRate(
                    st.Level, mob.Level, p)));
            return;
        }
        var nodeType = NodeTypeOf(st);
        if (team.TeamType == 4 && nodeType != 0xc) return;
        var locals = LocalTeamMembers(st, team);
        if (locals.Count <= 1)
        {
            await DestinyGainDpAsync(st, (int)Math.Round(
                p.DpPerKill * NativeCombatFormulas.DesDeclineRate(
                    st.Level, mob.Level, p)));
            return;
        }
        var maxLv = 0; var sumLv = 0;
        foreach (var ost in locals)
        {
            sumLv += ost.Level;
            if (ost.Level > maxLv) maxLv = ost.Level;
        }
        var n = locals.Count;
        foreach (var ost in locals)
            if (maxLv - ost.Level > 9) { n--; sumLv -= ost.Level; }
        var scaled = (int)Math.Round(p.DpPerKill
            * NativeCombatFormulas.ExpDeclineRate(maxLv, mob.Level, p));
        foreach (var ost in locals)
        {
            int share;
            if (maxLv - ost.Level < 10 && !team.ExpNoShare)
                share = NativeCombatFormulas.ExpForTeam(scaled, n,
                    ost.Level, sumLv, p);
            else if (ost == st)
                share = NativeCombatFormulas.ExpForTeamNoShare(
                    (int)p.DpPerKill, locals.Count, ost.Level,
                    mob.Level, p);
            else share = 0;
            if (share > 0)
                await DestinyGainDpAsync(ost, share);
        }
    }

    /// <summary>`CCharacter::GainDp` natif (zone @0x0840b0c0) — mort
    /// ou sans route → ignoré ; gain ≥1 → pool += dp borné au cap
    /// (p570×p376) + texte 0xf51 si sous le cap ; Notify(2,pool).</summary>
    private async Task DestinyGainDpAsync(EntryState st, int dp)
    {
        var d = st.Destiny;
        if (st.Dead || d == null || d.RoadId == 0) return;
        if (dp < 1)
        {
            d.DpPool = Math.Max(0, d.DpPool + dp);
        }
        else
        {
            var cap = DestinyDpCap();
            if (d.DpPool + dp <= cap)
            {
                d.DpPool += dp;
                if (st.Session != null)
                    await SendDestinyTextAsync(st.Session, 0xf51,
                        CancellationToken.None);
            }
            else d.DpPool = cap;
        }
        if (st.Session != null)
            await NotifyDestinyAsync(st.Session, 2,
                (short)d.DpPool, 0, CancellationToken.None);
        await SaveDestinyAsync(st);
    }
}
