namespace EdenClient.Game;

/// <summary>Noms d'animations (stems .kf) préchargés pour le joueur et les entités.</summary>
public static class AnimNames
{
    /// <summary>Anims du joueur préchargées (sous-ensemble du méga-fichier
    /// char — idle/course/attaques/dégâts/mort/saut/assise). Le 272 porte
    /// le kfId exact de l'arme (motion.ini → attackNN jusqu'à attack27
    /// pour 2H/dual/tir) — tout le pool attack01-27 est chargé pour que
    /// la résolution <see cref="Combat.MotionTable"/> joue vraiment.</summary>
    public static readonly string[] CharWanted = new string[]
    {
        "stand01", "stand02", "move01", "move02", "move03", "move04",
        "hurt01", "death01",
        "jump01", "sit01", "collect01",
    }.Concat(Enumerable.Range(1, 27).Select(i => $"attack{i:D2}"))
     // Casts/actions de sorts à pied : kf → shootNN/magicNN/battleNN
     // (motion.ini, stem b) — absents → PlayOnce sautait en silence.
     .Concat(Enumerable.Range(1, 79).Select(i => $"shoot{i:D2}"))
     .Concat(Enumerable.Range(1, 20).Select(i => $"magic{i:D2}"))
     .Concat(Enumerable.Range(1, 13).Select(i => $"battle{i:D2}"))
     // Cavalier : ride01-08 (4001-4008) + variantes montées des
     // actions de combat.
     .Concat(Enumerable.Range(1, 8).Select(i => $"ride{i:D2}"))
     .Concat(Enumerable.Range(1, 27).Select(i => $"rattack{i:D2}"))
     .Concat(Enumerable.Range(1, 20).Select(i => $"rmagic{i:D2}"))
     .Concat(Enumerable.Range(1, 13).Select(i => $"rbattle{i:D2}"))
     .Concat(Enumerable.Range(1, 79).Select(i => $"rshoot{i:D2}"))
     .ToArray();

    /// <summary>Anims entités préchargées (monster/NPC stems —
    /// die01/death02 couvrent les modèles sans « death01 »).</summary>
    public static readonly string[] EntityWanted =
    [
        "stand01", "stand02", "move01", "move02", "move03", "move04",
        "attack01", "attack02", "attack03", "attack04",
        "battle01", "hurt01", "hurt02",
        "death01", "death02", "die01", "magic01",
    ];
}
