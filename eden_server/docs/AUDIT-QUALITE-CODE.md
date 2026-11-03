# Audit qualité du code C# — 2026-10-20

Audit demandé sur 5 axes : anti-patterns/architecture, performance
réseau & GC, thread-safety, cycle de vie TCP, TODOs oubliés.
Chaque constat liste l'impact production réel puis le correctif appliqué.

---

## P0 — Crash / corruption potentiels

### 1. `MonsterService._instances` : `Dictionary` muté depuis le thread timer + threads sessions

**Impact réel** : `_instances` (Dictionary non concurrent) est itéré par
`TickRespawns()` sur le thread `System.Timers.Timer` (Program.cs, tick 1 s)
pendant que `Spawn`/`Kill`/`Despawn`/`ApplyDamage` le mutent depuis les
threads de session (combat joueur). `Dictionary` n'est PAS thread-safe en
lecture concurrente à une écriture : `InvalidOperationException` levée dans
le callback `Elapsed` → **exception non gérée sur un thread pool → arrêt
immédiat du process zone** (comportement .NET Core par défaut). Un mob qui
spawn pendant un tick suffit à tuer le serveur entier.
`_nextInstanceId++` sur `long` est en plus non atomique (deux spawns
simultanés → id dupliqué).

**Correctif** : `ConcurrentDictionary<long,MonsterEntity>` +
`Interlocked.Increment` pour l'id + `try/catch` dans le callback du timer
(Program.cs) pour qu'aucune exception ne s'échappe d'un `Elapsed`.

### 2. `NetSession.DisposeAsync` dispose `_sendLock` pendant des envois en vol

**Impact réel** : `DisposeAsync` pose `_isDisposed` puis appelle
`_sendLock.Dispose()`. Si une tâche de fond (cast temporisé, boucle de
combat, broadcast) est en `WaitAsync` ou tient le sémaphore à cet instant,
`SemaphoreSlim.Dispose` lève `ObjectDisposedException` sur le thread de la
tâche → exception non gérée dans un fire-and-forget. Sur un serveur réel,
chaque déconnexion pendant un combat expose la race (dizaines de sendloops
concurrents). `_isDisposed` en `bool` simple ajoute une race de visibilité.

**Correctif** : `_isDisposed` passé en `volatile` ; `_sendLock` n'est plus
disposé (un `SemaphoreSlim` ne requiert `Dispose` que si
`AvailableWaitHandle` est utilisé — sinon l'objet est GC-safe). Les sends
tardifs sortent sur le garde `_isDisposed` au lieu de crasher.

### 3. `st.CastCts` non annulé à la déconnexion

**Impact réel** : un cast temporisé en vol quand le joueur déconnecte
poursuit son `Task.Delay` puis émet des paquets vers une session morte et
applique des effets (dégâts, heal) sur un état orphelin. Aujourd'hui les
envois sont gardés par `_isDisposed` (pas de crash), mais la boucle peut
encore écrire en DB après sauvegarde finale → **état persisté incohérent**
(ex. spell résolu après le SaveCharacterAsync → mana débité/hit point
modifié en DB post-logout).

**Correctif** : `OnDisconnect` annule `st.CastCts` comme les autres CTS.

---

## P1 — Performance réseau & GC

### 4. Buffer de réception `List<byte>` — copie double + memmove O(n) par paquet

`NetSession.ReadPacketsAsync` et `MissionSession.RunAsync` accumulent dans
un `List<byte>` : `AddRange(span.ToArray())` (1 alloc), puis par paquet
`GetRange().ToArray()` (List intermédiaire + array = 2 allocs) et
`RemoveRange(0, n)` = memmove de tout le reste du buffer à CHAQUE paquet.

**Impact réel** : lors du burst d'entrée (centaines de paquets 298/280/…
en une fenêtre TCP), `RemoveRange` rend l'extraction O(n²) en octets copiés
— le coût du burst d'entrée monte en flèche quand le client coalesce ses
envois. Sous charge multi-joueurs ça devient du lag perceptible et du
bruit GC permanent (3+ allocations par paquet reçu).

**Correctif** : `PacketReceiveBuffer` (Eden.Core/Networking) — `byte[]`
possédé + `_start`/`_length` : extraction d'une trame = une seule copie du
payload (nécessaire, le déchiffrement RC4 mute en place et le handler
garde l'array), compaction amortie (memmove seulement quand l'espace de
tête est épuisé). Remplacé dans `NetSession` et `MissionSession`.

### 5. Double copie `payload[2..]` + `payload.AsSpan(2).ToArray()` par paquet (zone & monde)

Boucle de lecture : `TryHandleAsync(session, opcode, payload[2..], …)`
(copie n°1) puis, si non intercepté, `new InBinStream(payload.AsSpan(2).ToArray())`
(copie n°2). Chaque paquet dispatché est donc copié deux fois.

**Impact réel** : 100 % des paquets payent une allocation inutile ; les
gros (buyback list, vendor view, dumps multi-entrées ~ Ko) doublent leur
coût mémoire. Latent mais constant.

**Correctif** : une seule `body = payload.AsSpan(2).ToArray()` réutilisée
pour le flow ET le dispatcher (InBinStream ctor `(byte[])` inchangé).
Idem côté World (`payload.AsSpan(2).ToArray()` gardé une fois).

### 6. `SendPacketAsync`/`MissionSession.SendAsync` : buffer intermédiaire superflu

Chaque envoi alloue `body` (ncid+payload) puis `frame` (len+body) = 2
arrays + 2 copies pour un seul paquet.

**Impact réel** : à ~10-30 paquets/s/joueur en combat, ~2×le débit
d'allocation d'envoi — pression GC évitable sur le chemin le plus chaud.

**Correctif** : un seul `frame[2+2+len]` rempli directement
(len@0, ncid@2, payload@4) — moitié moins d'allocations par paquet émis.

### 7. `OutBinStream` sur `List<byte>` — `WriteBytes(span)` copie deux fois

`WriteBytes(ReadOnlySpan)` faisait `span.ToArray()` puis `AddRange` →
allocation intermédiaire + double copie pour chaque blob (apparence 32o,
records vendor, dumps item). `WriteLong/Float/Double` allouaient aussi un
`BitConverter.GetBytes` puis recopiaient.

**Impact réel** : les sérialiseurs de paquets (NCID 298, 274, vendor
view…) sont appelés à chaque spawn/achat — allocations parasites au fil
de l'eau, pas de danger mais du GC évitable sur le chemin chaud.

**Correctif** : buffer interne remplacé par `ArrayBufferWriter<byte>`
(API `GetSpan`/`Advance` zéro-alloc) — `WriteBytes` = copie directe dans
le buffer, `WriteLong` = `BitConverter.TryWriteBytes` dans un span.
`ToArray()` final inchangé.

---

## P2 — Architecture / maintenabilité

### 8. `ZoneEntryFlow.TryHandleAsync` — switch ~60 cas avec logique métier inline

Le switch de dispatch mélangeait routage et traitement (MigrationDone
~60 lignes inline, PlayerAction, CCDataSave, TurnSelf…). AGENTS.md exige
« dispatch via tables/handlers, pas un switch géant mêlé à la logique ».

**Impact réel** : méthode de ~700 lignes illisible → chaque ajout de NCID
risque une régression silencieuse (cas inséré au mauvais endroit, retour
manquant). Pas d'impact runtime, impact bug-introduction élevé.

**Correctif** : chaque cas avec de la logique extrait en méthode dédiée
(`HandleFirstEchoAsync`, `HandleMigrationDoneAsync`, `HandleTurnSelfAsync`,
`HandleLeaveShop`, `HandlePlayerCCDataSave`, `HandlePlayerActionEntryAsync`,
`HandleItemMallReceivableEnter`, `HandleVendorNoOp`…) — le switch ne fait
plus que router, un cas = une ligne.

### 9. `EntryState` (≈80 champs) déplacé dans `ZoneEntryFlow.State.cs`

**Impact réel** : le fichier principal faisait 1200+ lignes avec le sac
d'état noyé dedans — navigation pénible, conflits de merge fréquents.
Le `EntryState` reflète le `CCharacter` natif (aussi un agrégat géant) :
le split en sous-états par domaine (`st.Combat.*`, `st.Vendor.*`)
imposerait ~300 réécritures de références pour un bénéfice nul — le
risque de régression dépasse le gain. Dette documentée, non corrigée.

**Correctif** : extraction pure (déplacement) — même sémantique, fichier
dédié avec régions par domaine.

### 10. `CombatHandlers` = mort-vivant partiel

Les opcodes 5/9/13/16/17/20/59 sont interceptés par le flow AVANT le
dispatcher → les handlers enregistrés pour ces opcodes sont du code mort
invisible (les TODOs « vraies stats » qu'ils contiennent ne s'exécutent
jamais). Seuls les echos enchant 85/127/128 restent vivants.

**Impact réel** : confusion de maintenance — un dev peut « corriger » un
handler qui ne tourne jamais.

**Correctif** : commentaire d'en-tête explicite dans `CombatHandlers.cs`
indiquant les opcodes interceptés par `ZoneEntryFlow.Combat.cs`. (Pas de
suppression : les echos 85/127/128 transitent encore par lui.)

---

## P2 — Cycle de vie TCP

### 11. Log par paquet sur le chemin chaud

`[Zone-In] NCID=…` (+ `Convert.ToHexString`) à CHAQUE paquet non-mouvement,
`[Mission] --> NCID=…` idem côté mission — console synchronisée par un
lock global : tous les threads de session se serialisent dessus.

**Impact réel** : sous charge (multi-joueurs + bursts), chaque thread
attend le lock console → latence ajoutée à tous les paquets et logs qui
défilent illisibles.

**Correctif** : trace gated par `EDEN_ZONE_TRACE` / `EDEN_MISSION_TRACE`
(env `= "1"` requis ; défaut = silencieux) — le diagnostic live reste
disponible, la prod ne paie plus le lock console.

### 12. Session Gateway : pas de framing (lecture brut par read)

`Gateway/Program.cs` lit `ReceiveAsync` puis parse chaque read comme un
paquet entier (pas d'accumulation [u16 len][payload]). TCP ne garantit
pas qu'un `read` = un paquet → trames fragmentées lues comme des paquets
tronqués (heartbeat répondu à tort, paquets coalescés cassés).

**Impact réel** : serveur gateway de démo — pas sur le chemin client réel
(le client parle Login/World/Zone). Risque limité aux outils internes.

**Correctif** : documenté seulement — le gateway natif doit être réécrit
de toute façon (il sert l'auth launcher, pas ce chemin). Marqué
`// NOTE:` dans le code.

### 13. `BroadcastAllAsync` séquentielle

Les broadcasts attendent chaque `SendAsync` en série — un client lent
(retard socket) retarde tous les suivants.

**Impact réel** : head-of-line blocking réel mais borné (N petit en dev).
Pas corrigé : le RC4 étant par-flux et `_sendLock` par session, paralléliser
est sûr mais la sérialisation `NetCommand` par session (shift NCID
variable) empêche le serialize-once — le gain ne vaut pas la complexité.
Documenté.

---

## TODOs — inventaire

Les `TODO(phase reverse)` restants sont tous des stubs déclarés dans le
master plan (loyer housing, annonces recrutement team, fastdress, rank
info…) ou des handlers morts (CombatHandlers). Ils sont listés ci-dessus
et le master plan les couvre — aucun TODO « oublié » hors roadmap : chacun
a été vérifié contre le natif dans les lots précédents.

## Vérif

`dotnet build` : 0 erreur / 0 warning. `dotnet test` : suite complète —
9 échecs = baseline pré-existante identique (dispatcher, parsers,
loop/login serialization), zéro régression introduite.
