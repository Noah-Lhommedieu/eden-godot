using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages du système de dialogue NPC du ZoneServer (:5201) —
// fenêtre « parler » (285) + la sélection (11 déjà couvert par
// ZoneReplyDialog) et l'ouverture (10 ZoneTalkToNpc).
//
// Sources de preuve :
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs
//     CNC_CZ_ClientDisplayDialogView + Serialize natif @0x0822f1f0
//     cité par ZoneEntryFlow.Dialogue.cs ;
//   - golden server/tests/Eden.Tests/Data/golden/ncid_285.json
//     (corps 23/31B capturés) ;
//   - flux mission : ZoneEntryFlow.Mission.cs ForwardListDialogue /
//     ForwardReplyDialogue — le client ne renvoie qu'un INDEX (s16)
//     dans la liste triée par DisplayId (réponse = NCID 11).

/// <summary>S→C NCID 285 — DisplayDialogView : fenêtre de dialogue NPC.
/// Wire natif (ctor @0x822e450, golden ncid_285.json) :
/// `[i32 ctx][s16 n][n×(i32 displayId, str nom, s16 flag)][u8 type][i32 npcUid]`.
///   ctx      — id T_Dialogue du texte PARLÉ par le NPC (résolution
///              client-side ; -1/0 = pas de texte parlant).
///   displayId— id T_Dialogue de l'option (le client résout le libellé
///              via la table quand le nom embarqué est vide).
///   type     — type de vue (0 gossip, 0xff réponse mission).
///   npcUid   — star id / uid du NPC émetteur.
/// </summary>
public sealed class ZoneDisplayDialogView : NetMessage
{
    public override int Opcode => 285;
    public override string Name => "Zone.ClientDisplayDialogView";

    /// <summary>Une option de dialogue (cliquable → NCID 11 index).</summary>
    public sealed class DialogEntry
    {
        /// <summary>i32 — id T_Dialogue du libellé (ou id de service).</summary>
        public int DisplayId { get; set; }
        /// <summary>str — nom embarqué (souvent vide → résoudre via
        /// DisplayId dans t_dialogue.ini).</summary>
        public string EntryName { get; set; } = "";
        /// <summary>s16 — flag natif (couleur/dispo de l'option).</summary>
        public short Flag { get; set; }
        /// <summary>Libellé à afficher : nom embarqué, sinon le texte
        /// T_Dialogue de <see cref="DisplayId"/>, sinon #id.</summary>
        public string Label =>
            EntryName.Length > 0 ? EntryName
            : Data.TextResolver.Dialogue(DisplayId) ?? $"#{DisplayId}";
    }

    /// <summary>i32 — T_Dialogue id du texte parlé (-1/0 = aucun).</summary>
    public int ContextId { get; set; }
    /// <summary>Options de la fenêtre (ordre = index renvoyé au 11).</summary>
    public List<DialogEntry> Entries { get; } = new();
    /// <summary>u8 — type de vue (0xff = liste mission).</summary>
    public byte ViewType { get; set; }
    /// <summary>i32 — uid/star id du NPC.</summary>
    public int NpcUid { get; set; }

    /// <summary>Texte parlé résolu (t_dialogue.ini) — vide si inconnu.</summary>
    public string ContextText =>
        ContextId > 0 ? Data.TextResolver.Dialogue(ContextId) ?? "" : "";

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(ContextId);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries)
        {
            s.WriteInt(e.DisplayId);
            s.WriteString(e.EntryName);
            s.WriteShort(e.Flag);
        }
        s.WriteByte(ViewType);
        s.WriteInt(NpcUid);
    }

    public override void Deserialize(BinReader s)
    {
        ContextId = s.ReadInt();
        Entries.Clear();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 7; i++)
        {
            Entries.Add(new DialogEntry
            {
                DisplayId = s.ReadInt(),
                EntryName = s.ReadString(),
                Flag = s.ReadShort(),
            });
        }
        if (s.Remaining >= 5)
        {
            ViewType = s.ReadByte();
            NpcUid = s.ReadInt();
        }
    }
}

/// <summary>
/// État du dialogue NPC courant — alimenté par le handler 285 de
/// <c>GameScene</c> (thread principal) ; lu par la couche HUD
/// (C8 fenêtre dialogue) qui appelle <c>GameScene.SendDialogReplyAsync</c>
/// avec l'index de l'option choisie.
/// </summary>
public static class DialogueState
{
    /// <summary>Émis quand un 285 (ré)ouvre/change le dialogue.</summary>
    public static event Action? Changed;
    /// <summary>Émis quand le dialogue est refermé (mort node, sortie…).</summary>
    public static event Action? Closed;

    /// <summary>Texte parlé affiché en haut de fenêtre (déjà résolu).</summary>
    public static string SpokenText { get; private set; } = "";
    /// <summary>Options cliquables (index = s16 renvoyé au serveur).</summary>
    public static IReadOnlyList<ZoneDisplayDialogView.DialogEntry> Options
        => _options;
    /// <summary>Uid du NPC courant (0 = aucun dialogue ouvert).</summary>
    public static int NpcUid { get; private set; }
    /// <summary>True quand un dialogue est ouvert.</summary>
    public static bool IsOpen { get; private set; }

    private static readonly List<ZoneDisplayDialogView.DialogEntry> _options
        = new();

    /// <summary>Mémorise un 285 fraîchement reçu (main thread).</summary>
    public static void OnDialogView(ZoneDisplayDialogView m)
    {
        SpokenText = m.ContextText;
        NpcUid = m.NpcUid;
        _options.Clear();
        _options.AddRange(m.Entries);
        IsOpen = true;
        Changed?.Invoke();
    }

    /// <summary>Réinitialise à la sortie de node / fermeture.</summary>
    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        NpcUid = 0;
        SpokenText = "";
        _options.Clear();
        Closed?.Invoke();
    }
}
