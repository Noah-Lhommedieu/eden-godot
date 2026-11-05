using System;

namespace EdenClient.Game;

/// <summary>
/// Locomotion directionnelle du client natif (sans GodotSharp,
/// testable dans NetTests). Preuves et adresses :
/// <c>docs/ANIMS-DIRECTIONNELLES.md</c>.
///
/// Natif : 9 « records de direction » <c>DAT_00fe49a8</c> (index =
/// LR + FB*3, LR 0=gauche 1=centre 2=droite, FB 0=arrière 1=aucun
/// 2=avant) + 2 variantes monture. Chaque record porte : un offset de
/// direction de déplacement, un offset de FACING (le corps ne se
/// tourne que de ±45° aux diagonales ; 0 pour A/D/S purs → il garde
/// son cap = caméra), un callback d'anim (avant 101 / gauche 102 /
/// droite 103 / arrière 104 → <c>move01..move04</c>) et l'id d'action
/// (= le <c>MoveType</c> s16 du MoveSelf, relayé tel quel en octet
/// <c>Flag</c> du 326 aux autres clients).
/// </summary>
public static class DirectionalLocomotion
{
    /// <summary>Id d'action des records natifs (= MoveType/Flag).
    /// </summary>
    public const int BackLeft = 0, Back = 1, BackRight = 2,
        Left = 3, None = 4, Right = 5,
        FwdLeft = 6, Fwd = 7, FwdRight = 8;

    /// <summary>Seuil d'axe (analogique) sous lequel une direction
    /// compte comme neutre.</summary>
    public const float Deadzone = 0.1f;

    /// <summary>Si vrai, A/D montés suivent les records monture
    /// (anim gauche/droite 102/103 + corps pivoté de 90°) ; sinon la
    /// monture joue move01 avec le même pivot.</summary>
    public static bool MountSideAnims = true;

    /// <summary>Record natif depuis les axes clavier/manette dans le
    /// repère du perso : <paramref name="lr"/> −1 gauche … +1 droite,
    /// <paramref name="fwd"/> +1 avant … −1 arrière.</summary>
    public static int Record(float lr, float fwd)
    {
        var l = lr < -Deadzone ? 0 : lr > Deadzone ? 2 : 1;
        var f = fwd < -Deadzone ? 0 : fwd > Deadzone ? 2 : 1;
        return l + f * 3;
    }

    /// <summary>Id d'action natif (KFM) joué : 101 avant, 102 gauche,
    /// 103 droite, 104 arrière (stance neutre / hors combat :
    /// <c>FUN_007738f0/970/7738d0</c>, repli 101).</summary>
    public static int KfmAction(int record) => record switch
    {
        Left => 102,
        Right => 103,
        BackLeft or Back or BackRight => 104,
        _ => 101,
    };

    /// <summary>Nom d'anim motion.ini (move01..move04).</summary>
    public static string AnimFor(int record) =>
        $"move{KfmAction(record) - 100:D2}";

    /// <summary>Record valide d'un octet Flag de 326 (0..8) ; toute
    /// autre valeur (0x6a ruée de mob…) = avant.</summary>
    public static int RecordFromFlag(int flag) =>
        flag is >= 0 and <= 8 ? flag : Fwd;

    /// <summary>Anim de déplacement depuis le Flag d'un 326.</summary>
    public static string AnimForFlag(int flag) =>
        AnimFor(RecordFromFlag(flag));

    /// <summary>Offset de facing à ajouter au cap, en rotation Y
    /// Godot (positif = tourne à gauche). Natif (record+0x0c, négatif
    /// = gauche) : diagonales ±45°, A/D purs 0 à pied, ±90° monté
    /// (records 9/10).</summary>
    public static float FaceOffset(int record, bool mounted)
    {
        const float q = MathF.PI / 4f;
        return record switch
        {
            FwdLeft => q,
            BackRight => q,
            FwdRight => -q,
            BackLeft => -q,
            Left when mounted => 2f * q,
            Right when mounted => -2f * q,
            _ => 0f,
        };
    }

    /// <summary>Anim du corps/monture : monté, A/D gardent move02/03
    /// (records 9/10 → cb gauche/droite) si
    /// <see cref="MountSideAnims"/>.</summary>
    public static string AnimFor(int record, bool mounted) =>
        mounted && !MountSideAnims && record is Left or Right
            ? "move01" : AnimFor(record);

    /// <summary>Anim jouée par la MONTURE pour ce record — seul
    /// l'EntityAnimator de la monture doit l'utiliser (le cavalier
    /// dérive sa ride* de l'anim courante de la monture via
    /// <c>EntityAnimator.RiderAnimFor</c> ; à pied c'est
    /// <see cref="AnimFor(int)"/> qui s'applique).
    ///
    /// SPÉCIFICATION NATIF (records monture de
    /// <c>DirectionControl.cpp</c>, init @0x…2470067, game_.exe) :
    /// record 9 (strafe gauche monté, <c>FUN_00781640(_,0,1)</c>) =
    /// copie du record Left sauf +0x0c facing = −90° D3D (= yaw 90°
    /// gauche Godot) et callback <c>FUN_00741d70</c> = le MÊME que le
    /// Left à pied (vtable+0x38 → action 102 → move02) ; record 10 =
    /// copie du Right avec facing +90° D3D et callback
    /// <c>FUN_00741db0</c> (vtable+0x3c → action 103 → move03). Le
    /// natif ne croise PAS : A → move02, D → move03 — le corps pivote
    /// de 90° vers le côté du strafe (<see cref="FaceOffset"/>) et
    /// l'anim de flanc est celle du record à pied correspondant. Le
    /// ressenti « inversé » venait du croisement local alors que le
    /// chemin remote (flag 326 → <see cref="AnimForFlag"/>) restait
    /// natif — deux visions différentes du même strafe.
    /// Sans <see cref="MountSideAnims"/> : move01 (inchangé).</summary>
    public static string MountAnimFor(int record) => record switch
    {
        Left => MountSideAnims ? "move02" : "move01",
        Right => MountSideAnims ? "move03" : "move01",
        _ => AnimFor(record),
    };
}
