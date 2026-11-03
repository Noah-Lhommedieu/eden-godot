# Phase 1 — `PacketReceiveBuffer` sur les liens inter-serveurs de la zone

Date : 2026-10-26. Item « `PacketReceiveBuffer` sur les liens » du plan de
refonte (`eden_godot/docs/plan-refonte_architecture-complet.md`
§Phase 1, hors-scope du lot `2026-10-26-phase1-envois-non-bloquants.md`).

## Objectif

Remplacer le `List<byte>` + `RemoveRange(0, 2+len)` — O(n²) pendant les
bursts — par `Eden.Core.Networking.PacketReceiveBuffer` (fenêtre glissante,
compaction amortie) dans les deux boucles de réception des liens internes :

- `server/src/Eden.Zone/Links/ZoneWorldLink.cs` (~l.124-140, lien RC4)
- `server/src/Eden.Zone/Links/ZoneMissionLink.cs` (~l.109-153, lien EN
  CLAIR — pas de `_recvCipher` sur ce lien)

## Sémantique de framing à conserver

Trames `[u16 len en clair][payload]` où len = taille du payload (u16 ncid
inclus). `TryTakePacket(out payload)` rend exactement `len` octets sans
l'en-tête → `payload.Length == len`, identique à l'ancien
`GetRange(2, len)`.

## Points d'édition

1. `ZoneWorldLink` : `buffer.Append(tmp[0..read])` puis
   `while (buffer.TryTakePacket(out var packet)) { _recvCipher.Process(packet);
   await HandlePacketAsync(packet, token); }` — RC4 in-place et dispatch
   inchangés.
2. `ZoneMissionLink` : même structure, dispatch ncid/body + logs inchangés.

## Différence de sémantique (acceptée, documentée)

`TryTakePacket` lève `InvalidDataException` sur `len<2` **avant de
consommer** — impossible de reproduire l'ancien « trame sautée » du lien
mission. Traduction : log `[Zone-M]` conservé puis rethrow →
`RunLoopAsync` abandonne le lien et retente après 3 s (comportement déjà
en place pour toute erreur). Côté ZW, l'ancien code laissait
`HandlePacketAsync` ignorer les payloads `<2` ; désormais `len<2` →
reconnexion (cohérent avec `NetSession`, anti-flood/désync RC4).

## Vérification

- [ ] `dotnet build src/Eden.Zone/Eden.Zone.csproj` → 0 erreur, 0 warning.
- [ ] `PacketFramingTests` (tests du buffer lui-même) verts.
- [ ] Aucun test n'existe sur les liens (grep `ZoneWorldLink|ZoneMissionLink`
      dans `server/tests/` → 0 résultat).
