using System.Text;
using EdenClient.Data;

// Tables de données -> modèles + textures demandés par le jeu.
static partial class TextureAudit
{
    static IEnumerable<string> Lines(string rel)
    {
        var p = Path.Combine(root, rel);
        if (!File.Exists(p)) return Array.Empty<string>();
        return Encoding.Latin1.GetString(MyCrypt.DecodeFile(p)).Split('\n').Select(l => l.TrimEnd('\r'));
    }

    /// <summary>Lignes « DBn=a,b,c » -> (section, colonnes).</summary>
    static IEnumerable<(string sec, string[] c)> DbRows(string rel)
    {
        var sec = "";
        foreach (var l0 in Lines(rel))
        {
            var l = l0.Trim();
            if (l.StartsWith('[') && l.EndsWith(']')) { sec = l[1..^1]; continue; }
            if (!l.StartsWith("DB")) continue;
            var eq = l.IndexOf('=');
            if (eq > 0) yield return (sec, l[(eq + 1)..].Split(','));
        }
    }

    static string Col(string[] c, int i) => i < c.Length ? c[i].Trim() : "";
    static bool Real(string s) => s.Length > 0 && !s.Equals("NULL", StringComparison.OrdinalIgnoreCase) && s != "0";

    static void ScanTables()
    {
        // ---- 4pequip & co : DBn=code,groupe,mesh,skin,... (CharModelSpec.LoadEquipMap). Section TableX -> lettre de corps.
        var letterOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["TableBoy"] = "b", ["TableGirl"] = "g", ["TableMouse"] = "m", ["TableFrog"] = "f", ["TableKuma"] = "k", ["TableLoli"] = "l", ["TableShota"] = "c" };
        foreach (var tbl in new[] { "4pequip.ini", "Equip.ini", "NewEquip.ini", "SuitEquip.ini", "ElfEquip.ini", "ELFSuit.ini" })
        {
            var rel = "data/db/" + tbl;
            foreach (var (sec, c) in DbRows(rel))
            {
                var code = Col(c, 0); var mesh = Col(c, 2); var skin = Col(c, 3);
                var cat = tbl == "4pequip.ini" ? "table-4pequip" : "table-equip-autres";
                var src = $"{tbl}[{sec}] {code}";
                if (!letterOf.TryGetValue(sec, out var L)) { Mark(cat, src, "section", sec, false, "section inconnue"); continue; }
                if (mesh.Equals("NULL", StringComparison.OrdinalIgnoreCase) || mesh.Length == 0) continue;   // invisible
                if (mesh.StartsWith('#'))
                {
                    // mesh du méga-modèle : doit exister (nom de shape/nœud) dans char/model/<L>.nif
                    var ok = megaNifs.TryGetValue(L, out var nif) &&
                        (nif.Shapes.Any(s => s.Name.Equals(mesh, StringComparison.OrdinalIgnoreCase)) || nif.Nodes.Any(n => n.Name.Equals(mesh, StringComparison.OrdinalIgnoreCase)));
                    Mark(cat, src, "mesh-méga", mesh, ok, $"mesh absent de char/model/{L}.nif");
                }
                else refs.Add(new Ref(cat, src, "mesh-nif", $"char/model/{mesh}.nif", true));
                if (Real(skin))
                {   // CharVisual.DyeMeshSurfaces : masque <skin>B + artwork <skin>A
                    refs.Add(new Ref(cat, src, "skinB", $"char/texture/{skin}B.dds", true));
                    refs.Add(new Ref(cat, src, "skinA", $"char/texture/{skin}A.dds", true));
                }
            }
        }

        // ---- 4pequipmap.ini : « B1001H01A.dds G1001H01A.dds » (substitution entre lettres de corps) : les deux existent.
        foreach (var l in Lines("data/db/4pequipmap.ini"))
        {
            if (l.TrimStart().StartsWith("//")) continue;       // ligne commentée dans la table
            var p = l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2) continue;
            foreach (var n in p) refs.Add(new Ref("table-4pequipmap", "4pequipmap.ini " + p[0], "paire", "char/texture/" + n, true));
        }

        // ---- weapon.ini : DBn=W10601,w106,w10601.dds,nom -> item/model/w106.nif + item/texture/w10601.dds
        foreach (var (_, c) in DbRows("data/db/weapon.ini"))
        {
            var src = "weapon.ini " + Col(c, 0);
            refs.Add(new Ref("table-armes", src, "modèle", $"item/model/{Col(c, 1)}.nif", true));
            if (Real(Col(c, 2))) refs.Add(new Ref("table-armes", src, "texture", $"item/texture/{Col(c, 2)}", true));
        }
        // Armes citées ailleurs (monstres/PNJ/items) : W + 3 chiffres = modèle, W + 5 chiffres = variante de skin (CharModelSpec.WeaponSkinCode).
        var weaponCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wrx = new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z0-9])W\d{5}(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var t in new[] { "c_item.ini", "monsterlist.ini", "npclist.ini" })
            foreach (var l in Lines("data/db/" + t))
                foreach (System.Text.RegularExpressions.Match m in wrx.Matches(l)) weaponCodes.Add(m.Value.ToUpperInvariant());
        foreach (var w in weaponCodes)
        {
            refs.Add(new Ref("table-armes", "code arme " + w, "modèle", $"item/model/{w[..4]}.nif", true));
            refs.Add(new Ref("table-armes", "code arme " + w, "skin-variante", $"item/texture/{w}.dds", true));
        }

        // ---- monsterlist / npclist : DBn=code,kfm,scale,..,arme1,arme2,texture,...
        void Creatures(string tbl, string cat, string dir)
        {
            foreach (var (_, c) in DbRows("data/db/" + tbl))
            {
                var src = $"{tbl} {Col(c, 0)}";
                var kfm = Col(c, 1);
                if (kfm.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) refs.Add(new Ref(cat, src, "modèle", $"{dir}/model/{kfm[..^4]}.nif", true));
                var tex = Col(c, 8);
                if (tex.Length > 0) refs.Add(new Ref(cat, src, "texture-variante", $"{dir}/texture/{tex}", true));
            }
        }
        Creatures("monsterlist.ini", "table-monstres", "monster");
        Creatures("npclist.ini", "table-pnj", "monster");

        // ---- ridelist.ini : DBn=code,kfm,s1,s2,texture,...
        foreach (var (_, c) in DbRows("data/db/ridelist.ini"))
        {
            var src = "ridelist.ini " + Col(c, 0);
            var kfm = Col(c, 1);
            if (kfm.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) refs.Add(new Ref("table-montures", src, "modèle", $"ride/model/{kfm[..^4]}.nif", true));
            if (Col(c, 4).Length > 0) refs.Add(new Ref("table-montures", src, "texture-variante", "ride/texture/" + Col(c, 4), true));
        }

        // ---- decorate.ini / 4PHouse.ini : DBn=code,modèle,texture,...
        foreach (var (_, c) in DbRows("data/db/decorate.ini"))
        {
            refs.Add(new Ref("table-decor", "decorate.ini " + Col(c, 0), "modèle", $"decorate/model/{Col(c, 1)}.nif", true));
            refs.Add(new Ref("table-decor", "decorate.ini " + Col(c, 0), "texture", $"decorate/texture/{Col(c, 2)}.dds", true));
        }
        foreach (var (_, c) in DbRows("data/db/4PHouse.ini"))
        {
            refs.Add(new Ref("table-maisons", "4PHouse.ini " + Col(c, 0), "modèle", $"house/model/{Col(c, 1)}.nif", true));
            refs.Add(new Ref("table-maisons", "4PHouse.ini " + Col(c, 0), "texture", $"house/texture/{Col(c, 2)}.dds", true));
        }

        // ---- codes modèle des tables de jeu -> lignes monsterlist/npclist (monstres, PNJ, partenaires)
        var ncodes = DbRows("data/db/npclist.ini").Select(r => Col(r.c, 0)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Le runtime déduit le modèle du code : stem = code sans ses 3 derniers caractères (« M025010 » -> monster/model/m025.nif,
        // GameScene.ResolveMonster / PartnerTable). Les PNJ passent par npclist (NpcModelTable).
        void CodeRefs(string tbl, int col, string cat, HashSet<string>? list)
        {
            foreach (var l in Lines("data/db/" + tbl))
            {
                var p = l.Split('|');
                if (p.Length <= col || p[0].Length == 0 || !char.IsDigit(p[0][0])) continue;
                var code = p[col].Trim();
                if (code.Length < 5) continue;
                var src = $"{tbl} #{p[0]}";
                if (list != null) Mark(cat, src, "code→npclist", code, list.Contains(code), "code absent de npclist.ini");
                else refs.Add(new Ref(cat, src, "modèle", $"monster/model/{code[..^3]}.nif", true));
            }
        }
        CodeRefs("c_monster.ini", 1, "table-monstres(codes)", null);
        CodeRefs("c_npc.ini", 1, "table-pnj(codes)", ncodes);
        CodeRefs("c_partner.ini", 2, "table-partenaires(codes)", null);
    }
}
