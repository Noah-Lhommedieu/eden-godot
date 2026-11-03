using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages monde reçus « non gérés » en live — lot Vague 4.
// Layouts : Serialize des CNC_CW_* dans
// server/src/Eden.Core/Networking/Messages/Generated/WorldMessages.g.cs.

// 40 WorldResetTime et 60 WorldUpdatePrestigeData existent déjà
// dans WorldMessages.cs ; 48 WorldFamilyInvitation dans
// ZoneSocialMessages.cs — pas de doublon ici.

/// <summary>S→C monde 78 ClientUpdateSpecialCoin — monnaies
/// spéciales : <c>[u16 n][n×(s16 type, i32 total)]</c> (relai
/// serveur <c>ZoneLinkServer.cs</c> ; le n=0 est fréquent à
/// l'entrée — d'où l'ancienne trame « tronquée » du codec fixe
/// 8 octets).</summary>
public sealed class WorldUpdateSpecialCoin : NetMessage
{
    public override int Opcode => 78;
    public override string Name => "World.UpdateSpecialCoin";
    /// <summary>Entrées (type, total) reçues — vide quand n=0.</summary>
    public List<(short Type, int Total)> Entries { get; } = new();
    /// <summary>Première entrée — compat pour les consommateurs qui
    /// n'affichent qu'une monnaie.</summary>
    public short CoinId => Entries.Count > 0 ? Entries[0].Type : (short)0;
    public int Value => Entries.Count > 0 ? Entries[0].Total : 0;

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var n = s.ReadUShort();
        Entries.Clear();
        for (var i = 0; i < n; i++)
            Entries.Add((s.ReadShort(), s.ReadInt()));
    }
}

/// <summary>S→C monde 46 ClientUpdateFamilyMember — maj membre de
/// famille/guilde : i32 + str + 4×s16 + u8 + s16 + u8 + s16 + 2×i32
/// + 2×str + i32 + i32.</summary>
public sealed class WorldUpdateFamilyMember : NetMessage
{
    public override int Opcode => 46;
    public override string Name => "World.UpdateFamilyMember";
    public int CharId { get; private set; }
    public string CharName { get; private set; } = "";
    public string Text1 { get; private set; } = "";
    public string Text2 { get; private set; } = "";
    /// <summary>Champs numériques conservés dans l'ordre wire.</summary>
    public int[] Nums { get; private set; } = Array.Empty<int>();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var nums = new List<int>();
        CharId = s.ReadInt();
        CharName = s.ReadString();
        for (var i = 0; i < 4; i++) nums.Add(s.ReadShort());
        s.ReadByte(); nums.Add(s.ReadShort()); s.ReadByte();
        nums.Add(s.ReadShort());
        nums.Add(s.ReadInt()); nums.Add(s.ReadInt());
        Text1 = s.ReadString(); Text2 = s.ReadString();
        nums.Add(s.ReadInt()); nums.Add(s.ReadInt());
        Nums = nums.ToArray();
    }
}

/// <summary>S→C monde 54 ClientRepeatedLogin — double connexion
/// détectée : u8.</summary>
public sealed class WorldRepeatedLogin : NetMessage
{
    public override int Opcode => 54;
    public override string Name => "World.RepeatedLogin";
    public byte Value { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
        => Value = s.ReadByte();
}
