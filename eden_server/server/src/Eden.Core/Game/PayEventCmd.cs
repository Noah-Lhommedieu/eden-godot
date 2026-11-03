namespace Eden.Core.Game;

/// <summary>
/// Commande d'événement pay compilée — port de
/// <c>lapis::CPayEventCmd</c> (0x84 octets) rempli par
/// <c>ConvertCmd</c> @0x080ea500 depuis les deux chaînes « #:… » de
/// <c>S_PaySystem</c> (cond col4 / award col17).
/// Découpage natif : <c>ParsingString(cmd,":")</c> — l'index 0 (« # »)
/// est sauté ; chaque op est ensuite éclatée sur « » ; seules les ops
/// dont le nom ET l'arité correspondent sont retenues.
/// Ops conditions → vecteurs +0x00/+0x0c/+0x18 ; ops récompense →
/// +0x24…+0x78 (détail dans <see cref="PayEventCmdParser"/>).
/// </summary>
public sealed class PayEventCmd
{
    /// <summary>`If_CharClassLevel classId op lvl` — vecteur +0x00,
    /// entrées 6 octets. op encodé : "&gt;="→1, "="→2, "&lt;="→3,
    /// autre→0 (=condition toujours vraie côté CheckCondition).</summary>
    public readonly List<(short ClassId, short Op, short Level)>
        ClassLevelConds = new();

    /// <summary>`If_CharLevel min max` — vecteur +0x0c (paires s16 ;
    /// 0 = non borné, couple 0/0 = échec natif).</summary>
    public readonly List<(short Min, short Max)> LevelConds = new();

    /// <summary>`If_CharReputation repId lo hi` — vecteur +0x18
    /// (12 octets ; prestige exp dans [lo,hi], 0 = non borné).</summary>
    public readonly List<(short RepId, int Lo, int Hi)> RepConds = new();

    /// <summary>`GiveGold n` — vecteur +0x24. ExecAwardCmd :
    /// FamilyLevyTaxes (10 % à la trésorerie, retour ignoré) puis
    /// GainGold(n) complet.</summary>
    public readonly List<int> Gold = new();

    /// <summary>`GiveItem id n` — vecteur +0x30 (pairs int/s16).</summary>
    public readonly List<(int ItemId, short Count)> Items = new();

    /// <summary>`ChooseItem id n` — vecteur +0x3c : récompenses au
    /// choix, index fourni par le client (s16 du payload 253).</summary>
    public readonly List<(int ItemId, int Count)> ChooseItems = new();

    /// <summary>`CharExp n` — vecteur +0x48 → GainExp.</summary>
    public readonly List<int> CharExp = new();

    /// <summary>`ClassPoint n` — vecteur +0x54 → GainClassPoint +
    /// GainNp (même montant pour les deux, natif).</summary>
    public readonly List<int> ClassPoints = new();

    /// <summary>`Reputation id + n` / `Reputation id - n` — vecteur
    /// +0x60 (pairs s16 repId / i32 montant signé) → AddPrestigeExp.</summary>
    public readonly List<(short RepId, int Amount)> Reputations = new();

    /// <summary>`GiveBuff id dur` — vecteur +0x6c. Sémantique durée
    /// (ExecAwardCmd) : 0 → reste -1 flag 0x2000 ; -1 → -2 permanent
    /// flag 0x2020 ; sinon dur*10 s flag 0x2000.</summary>
    public readonly List<(int BuffId, short Duration)> Buffs = new();

    /// <summary>`SpecialCoin type +/- n` — vecteur +0x78 ; le type
    /// n'est retenu que si &lt;12 et ∉ [6,8] (gate ConvertCmd natif).
    /// ExecAwardCmd → DBA « GMGainSpecialCoin ».</summary>
    public readonly List<(ushort Type, int Amount)> SpecialCoins = new();
}

/// <summary>
/// Port de <c>lapis::ConvertCmd(string cond, string award,
/// CPayEventCmd*)</c> @0x080ea500 : remplit un <see cref="PayEventCmd"/>
/// depuis les deux chaînes de commande du record S_PaySystem.
/// </summary>
public static class PayEventCmdParser
{
    /// <summary>Compile cond + award dans <paramref name="cmd"/>.
    /// Les deux chaînes sont traitées par le même dispatcher natif —
    /// ops If_* → conditions, le reste → récompenses.</summary>
    public static void Convert(string? cond, string? award,
        PayEventCmd cmd)
    {
        foreach (var op in SplitOps(cond)) ApplyOp(op, cmd);
        foreach (var op in SplitOps(award)) ApplyOp(op, cmd);
    }

    /// <summary>Construit le cmd complet d'un record (cond col4 +
    /// award col17) — équivalent du new 0x84 + ConvertCmd de
    /// LoadAllPayEventCmd.</summary>
    public static PayEventCmd Build(string? cond, string? award)
    {
        var cmd = new PayEventCmd();
        Convert(cond, award, cmd);
        return cmd;
    }

    /// <summary>Split ':' et suppression du préfixe '#' (index 0
    /// ignoré par la boucle native `uVar20 = 1`).</summary>
    private static IEnumerable<string[]> SplitOps(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var segs = text.Split(':');
        for (var i = 1; i < segs.Length; i++)
        {
            var tokens = segs[i].Split(' ',
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0) yield return tokens;
        }
    }

    /// <summary>Dispatch par nom + arité — reproduit l'ordre des
    /// comparisons std::string::compare de ConvertCmd.</summary>
    private static void ApplyOp(string[] t, PayEventCmd cmd)
    {
        switch (t[0])
        {
            // ---- conditions (vecteurs +0x00 / +0x0c / +0x18) ----
            case "If_CharClassLevel" when t.Length == 4:
                // op : "="→2 ">="→1 "<="→3 autre→0
                var op = t[2] switch { "=" => 2, ">=" => 1,
                    "<=" => 3, _ => 0 };
                cmd.ClassLevelConds.Add(((short)Num(t[1]),
                    (short)op, (short)Num(t[3])));
                break;
            case "If_CharLevel" when t.Length == 3:
                cmd.LevelConds.Add(((short)Num(t[1]),
                    (short)Num(t[2])));
                break;
            case "If_CharReputation" when t.Length == 4:
                cmd.RepConds.Add(((short)Num(t[1]),
                    Num(t[2]), Num(t[3])));
                break;

            // ---- récompenses (vecteurs +0x24…+0x78) ----
            case "GiveGold" when t.Length == 2:
                cmd.Gold.Add(Num(t[1]));
                break;
            case "GiveItem" when t.Length == 3:
                cmd.Items.Add((Num(t[1]), (short)Num(t[2])));
                break;
            case "CharExp" when t.Length == 2:
                cmd.CharExp.Add(Num(t[1]));
                break;
            case "ClassPoint" when t.Length == 2:
                cmd.ClassPoints.Add(Num(t[1]));
                break;
            case "Reputation" when t.Length == 4:
                // "Reputation id + n" / "- n" — le signe est le
                // token central (comparaison "-" native).
                cmd.Reputations.Add(((short)Num(t[1]),
                    t[2] == "-" ? -Num(t[3]) : Num(t[3])));
                break;
            case "GiveBuff" when t.Length == 3:
                cmd.Buffs.Add((Num(t[1]), (short)Num(t[2])));
                break;
            case "SpecialCoin" when t.Length == 4:
                var type = (ushort)Num(t[1]);
                // gate natif : type < 12 et ∉ [6,8]
                if (type < 12 && (ushort)(type - 6) > 2)
                    cmd.SpecialCoins.Add((type,
                        t[2] == "-" ? -Num(t[3]) : Num(t[3])));
                break;
            case "ChooseItem" when t.Length == 3:
                cmd.ChooseItems.Add((Num(t[1]), Num(t[2])));
                break;
        }
    }

    private static int Num(string s) =>
        int.TryParse(s.Trim(), out var n) ? n : 0;
}
