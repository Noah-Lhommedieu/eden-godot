// Dictionnaire d'items EDENFR : fusionne les tables data/db de corpus/fr-full avec les lecteurs Core
// (IniTable.ParseFile = MyCrypt + UTF-8/Latin-1/Big5, ItemTable = c_item, TextTable = t_item, CraftTable,
// MissionTable.ParseScript). Aucune donnée EN/US. Voir docs/reference/ITEMS.md pour la méthode.
using System.Globalization;
using System.Text;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenTools.ItemDictionary;

public sealed class ItemRow
{
    public int Id;
    public string Name = "", NameSource = "", NameBig5 = "";
    public string Family = "", TypeCode = "", TypeLabel = "", TypeLabelSource = "";
    public int RequiredLevel, MaxLevel, QualityCode, EquipLocCode, StackSize, SystemPrice, MaxDurability;
    public string QualityLabel = "", EquipSlot = "", ClassIds = "", ClassNames = "", Binding = "";
    public string Stats = "", UseEffect = "", Description = "", ItemCode = "", Icon = "", GroupCode = "";
    public string SourceTables = "", ReferencedBy = "", Anomalies = "";
    public ulong ClassMask;
    public int CategoryCol33;
}

public sealed class BuildResult
{
    public List<ItemRow> Rows = new();
    /// <summary>Lignes de rapport (compteurs, écarts, références orphelines).</summary>
    public List<string> Report = new();
    public Dictionary<string, int> Counters = new();
}

public static class ItemDictionaryBuilder
{
    public const string Header =
        "id\tnom\tfamille\ttype_code\ttype_libelle\tniveau_requis\tniveau_max\tqualite_code\tqualite_libelle\t" +
        "emplacement_code\templacement_libelle\tclasses_ids\tclasses_noms\ttaille_pile\tliaison\tprix_systeme\t" +
        "durabilite_max\tstats\teffet_usage\tdescription\tcode_item\ticone\ttables_source\treference_par\t" +
        "nom_source\ttype_libelle_source\tnom_big5_c_item\tanomalies";

    public static string[] ToCells(ItemRow r) => new[]
    {
        r.Id.ToString(CultureInfo.InvariantCulture), r.Name, r.Family, r.TypeCode, r.TypeLabel,
        r.RequiredLevel > 0 ? r.RequiredLevel.ToString() : "", r.MaxLevel > 0 ? r.MaxLevel.ToString() : "",
        r.QualityCode > 0 ? r.QualityCode.ToString() : "", r.QualityLabel,
        r.EquipLocCode > 0 ? r.EquipLocCode.ToString() : "", r.EquipSlot, r.ClassIds, r.ClassNames,
        r.StackSize > 0 ? r.StackSize.ToString() : "", r.Binding,
        r.SystemPrice > 0 ? r.SystemPrice.ToString() : "", r.MaxDurability > 0 ? r.MaxDurability.ToString() : "",
        r.Stats, r.UseEffect, r.Description, r.ItemCode, r.Icon, r.SourceTables, r.ReferencedBy,
        r.NameSource, r.TypeLabelSource, r.NameBig5, r.Anomalies,
    };

    // ---- nettoyage de texte : pas de tab/retour ligne dans un champ TSV ----
    public static string CleanText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = TextResolver.Clean(s);            // retire les marqueurs $N$ (couleur/format)
        var sb = new StringBuilder(s.Length);
        foreach (var ch0 in s)
        {
            var ch = ch0;
            if (ch == '\r') continue;
            // Les t_* sont lues en Latin-1 alors que le texte est en CP1252 : 0x80-0x9F = œ, Œ, ’, … etc.
            if (ch >= '\u0080' && ch <= '\u009F' && Cp1252C1[ch - 0x80] != '\0') ch = Cp1252C1[ch - 0x80];
            if (ch == '\n') { sb.Append(" ¶ "); continue; }
            if (char.IsControl(ch) || ch == '�') { sb.Append(' '); continue; }
            sb.Append(ch);
        }
        var t = sb.ToString();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        t = t.Replace("¶ ¶", "¶");
        while (t.Contains("¶ ¶")) t = t.Replace("¶ ¶", "¶");
        return t.Trim(' ', '¶');
    }

    // CP1252 0x80..0x9F (0x81, 0x8D, 0x8F, 0x90, 0x9D non définis)
    static readonly char[] Cp1252C1 = ("\u20AC\0\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\0\u017D\0" +
                                       "\0\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\0\u017E\u0178").ToCharArray();

    static string P(string db, string f) => Path.Combine(db, f);

    // kind (c_item col 8) -> id t_textindex du libellé natif (ItemTipFormat.KindLabel, désasm @0x00897fe0)
    static readonly Dictionary<int, int> KindTextIndex = BuildKindMap();
    static Dictionary<int, int> BuildKindMap()
    {
        var m = new Dictionary<int, int>();
        foreach (var k in new[] { 1, 4, 7, 10, 13 }) m[k] = 1158;   // Armure Lourde
        foreach (var k in new[] { 2, 5, 8, 11, 14 }) m[k] = 1159;   // Armure Légère
        foreach (var k in new[] { 3, 6, 9, 12, 15 }) m[k] = 1160;   // Robe de Protection
        m[16] = 1161;                                                // « Retour » (Back)
        for (var k = 17; k <= 22; k++) m[k] = 1162 + (k - 17);       // Poignard..Hache
        m[23] = 5465;                                                // Lances
        for (var k = 24; k <= 29; k++) m[k] = 1168 + (k - 24);       // Masse..Pièce d'Artillerie
        m[31] = 1174; m[32] = 1175; m[33] = 1176; m[34] = 1177;
        m[37] = 1026; m[38] = 1027; m[39] = 1028;                    // Collier, Anneau, Trophée
        m[40] = 1029; m[41] = 1030; m[42] = 1031; m[43] = 1032; m[44] = 1033; m[45] = 1034; m[46] = 1035;
        m[47] = 1036; m[48] = 1037;
        for (var k = 49; k <= 57; k++) m[k] = 1038 + (k - 49);       // matériaux/formules
        m[60] = 1107;                                                // Familier
        for (var k = 61; k <= 68; k++) m[k] = 1178;                  // Costume
        m[69] = 3837; m[70] = 3838; m[71] = 3839; m[72] = 3840; m[73] = 3841; m[74] = 3842;
        m[78] = 4158; m[82] = 7046;
        return m;
    }

    // libellés sans entrée t_textindex : dérivés des noms d'items observés (marqués « derive »)
    static readonly Dictionary<int, string> DerivedKind = new()
    {
        [0] = "Sans type (kind 0)", [30] = "Faucille-fusil", [35] = "Type 35 (non documenté)",
        [58] = "Parchemin de compétence (type 58, dérivé des noms)",
        [59] = "Teinture (mall)", [75] = "Sol de salon", [76] = "Sol de chambre",
        [77] = "Invitation de banquet", [79] = "Enchantement de costume",
        [80] = "Expertise de classe", [81] = "Équipement de familier", [83] = "Invitation de mariage",
        [84] = "Parchemin de costume (mall)",
    };

    static string FamilyOf(int kind) => kind switch
    {
        >= 1 and <= 16 => "Armure",
        >= 17 and <= 33 => "Arme",
        34 => "Bouclier",
        >= 37 and <= 39 => "Accessoire",
        40 => "Certificat",
        41 or 42 or 43 => "Bâtiment/PNJ/Sac",
        44 => "Objet",
        45 => "Gemme",
        46 => "Parchemin",
        47 or 48 => "Coffre/Panoplie",
        >= 49 and <= 58 => "Matériau/Formule",
        59 => "Teinture",
        60 or 81 => "Familier",
        >= 61 and <= 68 or 79 or 84 => "Costume",
        >= 69 and <= 76 => "Mobilier",
        78 or 80 => "Classe",
        82 => "Esprit gardien",
        77 or 83 => "Invitation",
        _ => "Autre",
    };

    // ---- construction ----
    public static BuildResult Build(string frFull)
    {
        var db = Path.Combine(frFull, "data", "db");
        var res = new BuildResult();
        void Rep(string s) => res.Report.Add(s);
        void Cnt(string k, int v) { res.Counters[k] = v; }

        // libellés natifs FR (t_textindex)
        var ti = TextTable.Load(P(db, "t_textindex.ini"));
        string TI(int id) => ti.TryGetValue(id, out var r) ? CleanText(r.Field(1)) : "";

        var cItem = ItemTable.Load(P(db, "c_item.ini"));
        var cMall = ItemTable.Load(P(db, "c_itemmall.ini"));
        // ItemTable.Load ne remplit pas ItemDef.Raw : on relit les lignes brutes pour les colonnes non portées (33, 77)
        var rawRows = new Dictionary<int, string[]>();
        foreach (var f in new[] { "c_item.ini", "c_itemmall.ini" })
            foreach (var row in IniTable.ParseFile(P(db, f)).Rows)
                if (row.Length > 10 && int.TryParse(row[0], out var rid)) rawRows.TryAdd(rid, row);
        var tItem = TextTable.Load(P(db, "t_item.ini"));
        var tMall = TextTable.Load(P(db, "t_itemmall.ini"));
        // ids dupliqués dans chaque table (dictionnaire : dernière occurrence gagne)
        foreach (var f in new[] { "c_item.ini", "t_item.ini", "c_itemmall.ini", "t_itemmall.ini" })
        {
            var rows = IniTable.ParseFile(P(db, f)).Rows;
            var ids = rows.Select(r => r.Length > 0 && int.TryParse(r[0], out var x) ? x : int.MinValue).Where(x => x != int.MinValue).ToList();
            Cnt($"rows:{f}", ids.Count);
            Cnt($"dupids:{f}", ids.Count - ids.Distinct().Count());
        }
        // enchants d'usage (effet des consommables) : t_enchant puis t_itemmallenchant (additif)
        var enchText = TextTable.Load(P(db, "t_enchant.ini"));
        foreach (var kv in TextTable.Load(P(db, "t_itemmallenchant.ini"))) enchText.TryAdd(kv.Key, kv.Value);
        var classNames = new Dictionary<int, string>();
        foreach (var kv in TextTable.Load(P(db, "t_class.ini"))) classNames[kv.Key] = CleanText(kv.Value.Field(1));

        // partenaires/familiers : c_partner/t_partner sont indexés par id d'item
        var partnerIds = new HashSet<int>(TextTable.Load(P(db, "t_partner.ini")).Keys);
        foreach (var r in IniTable.ParseFile(P(db, "c_partner.ini")).Rows)
            if (r.Length > 0 && int.TryParse(r[0], out var pid)) partnerIds.Add(pid);

        var all = new SortedSet<int>(cItem.Keys);
        all.UnionWith(tItem.Keys); all.UnionWith(cMall.Keys); all.UnionWith(tMall.Keys);

        // ---- références croisées (boutiques, butin, quêtes, recettes) ----
        var refs = new Dictionary<int, SortedSet<string>>();
        var refAll = new Dictionary<string, HashSet<int>>();
        void Ref(string table, int item)
        {
            if (item <= 0) return;
            if (!refs.TryGetValue(item, out var s)) refs[item] = s = new();
            s.Add(table);
            if (!refAll.TryGetValue(table, out var h)) refAll[table] = h = new();
            h.Add(item);
        }
        // C_Store : cols 3.. par triplets (itemId, nom, n) — grammaire de StoreTable
        foreach (var row in IniTable.ParseFile(P(db, "c_store.ini")).Rows)
            for (var i = 3; i + 2 < row.Length; i += 3)
            {
                var id = IniTable.GetInt(row, i);
                if (id <= 0) break;
                Ref("boutique", id);
            }
        // c_mission col 20 : récompenses (RewardItem / RewardChooseItem), grammaire de ItemSources
        foreach (var row in IniTable.ParseFile(P(db, "c_mission.ini")).Rows)
        {
            if (row.Length <= 20) continue;
            foreach (var c in MissionTable.ParseScript(row[20]))
                if (c.Name is "RewardItem" or "RewardChooseItem" && c.Args.Count > 0 && int.TryParse(c.Args[0], out var id))
                    Ref("quete", id);
        }
        // recettes
        foreach (var r in CraftTable.LoadCombine(P(db, "c_familycombine.ini")).Values)
        {
            Ref("recette_produit", r.ProductItemId);
            foreach (var m in r.Materials) Ref("recette_materiau", m.ItemId);
        }
        foreach (var r in CraftTable.LoadCollect(P(db, "c_familycollect.ini")).Values)
        {
            Ref("collecte_catalyseur", r.CatalystItemId);
            foreach (var p in r.Products) Ref("collecte_produit", p.ItemId);
        }
        // c_dropitem : grammaire de ItemSources.Drops (paires (taux,item,…) ou (item,?,?,?))
        var inv = CultureInfo.InvariantCulture;
        foreach (var row in IniTable.ParseFile(P(db, "c_dropitem.ini")).Rows)
        {
            if (row.Length < 14 || !int.TryParse(row[0], out _)) continue;
            var i = 10;
            while (i + 3 < row.Length)
            {
                var bNum = int.TryParse(row[i + 1], out var itemB);
                var aNum = double.TryParse(row[i], NumberStyles.Float, inv, out var fa);
                var cNum = double.TryParse(row[i + 2], NumberStyles.Float, inv, out _);
                var dNum = double.TryParse(row[i + 3], NumberStyles.Float, inv, out _);
                if (aNum && bNum && itemB > 0 && !cNum) { Ref("butin", itemB); i += 4; }
                else if (aNum && !bNum && cNum && dNum) { Ref("butin", (int)fa); i += 4; }
                else i++;
            }
        }
        foreach (var id in partnerIds) Ref("partenaire", id);

        // ---- lignes ----
        foreach (var id in all)
        {
            var r = new ItemRow { Id = id };
            var tabs = new List<string>();
            var hasC = cItem.TryGetValue(id, out var ci);
            var hasCM = cMall.TryGetValue(id, out var cmi);
            var def = hasC ? ci : hasCM ? cmi : null;
            if (hasC) tabs.Add("c_item");
            if (tItem.ContainsKey(id)) tabs.Add("t_item");
            if (hasCM) tabs.Add("c_itemmall");
            if (tMall.ContainsKey(id)) tabs.Add("t_itemmall");
            if (partnerIds.Contains(id)) tabs.Add("c_partner/t_partner");
            r.SourceTables = string.Join("+", tabs);

            // nom/description : mall prioritaire (règle de TextResolver.ItemName)
            var nm = "";
            if (tMall.TryGetValue(id, out var tm) && CleanText(tm.Field(1)).Length > 0) { nm = CleanText(tm.Field(1)); r.NameSource = "t_itemmall"; r.Description = CleanText(tm.Field(2)); }
            if (nm.Length == 0 && tItem.TryGetValue(id, out var ti2) && CleanText(ti2.Field(1)).Length > 0) { nm = CleanText(ti2.Field(1)); r.NameSource = "t_item"; }
            if (r.Description.Length == 0)
            {
                if (tMall.TryGetValue(id, out var tm2)) r.Description = CleanText(tm2.Field(2));
                if (r.Description.Length == 0 && tItem.TryGetValue(id, out var ti3)) r.Description = CleanText(ti3.Field(2));
            }
            var anomalies = new List<string>();
            if (nm.Length == 0)
            {
                nm = "(sans nom)"; r.NameSource = "absent";
                anomalies.Add("nom_absent");
            }
            r.Name = nm;
            if (nm.Contains("Testing ID", StringComparison.OrdinalIgnoreCase) || nm.Contains("Placeholder", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(nm, @"^test\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) anomalies.Add("nom_test");
            if (def == null) anomalies.Add("sans_c_item");
            if (r.Description.Length == 0) anomalies.Add("sans_description");

            if (def != null)
            {
                var kind = def.Kind;
                r.TypeCode = kind.ToString(CultureInfo.InvariantCulture);
                if (KindTextIndex.TryGetValue(kind, out var tid) && TI(tid).Length > 0)
                { r.TypeLabel = TI(tid); r.TypeLabelSource = "t_textindex:" + tid; }
                else if (DerivedKind.TryGetValue(kind, out var dl)) { r.TypeLabel = dl; r.TypeLabelSource = "derive"; }
                else { r.TypeLabel = $"Type {kind} (non documenté)"; r.TypeLabelSource = "derive"; }
                if (kind == 75) r.TypeLabel = TI(3843).Length > 0 ? "Sol : " + TI(3843) : r.TypeLabel;
                if (kind == 76) r.TypeLabel = TI(3844).Length > 0 ? "Sol : " + TI(3844) : r.TypeLabel;
                if (kind is 75 or 76) r.TypeLabelSource = "t_textindex:" + (kind == 75 ? 3843 : 3844);
                r.Family = FamilyOf(kind);
                r.RequiredLevel = def.RequiredLevel; r.MaxLevel = def.MaxLevel;
                r.QualityCode = def.Quality;
                r.QualityLabel = def.Quality <= 0 ? "Aucune (blanc)" : def.Quality <= 6 ? TI(1151 + def.Quality) : "Qualité " + def.Quality;
                r.EquipLocCode = def.EquipLoc;
                r.EquipSlot = SlotLabel(def.EquipLoc, TI);
                r.StackSize = def.MaxStack; r.SystemPrice = def.SellPrice; r.MaxDurability = def.MaxDurability;
                r.ItemCode = def.Code; r.Icon = def.IconCode;
                r.NameBig5 = CleanText(def.Name);
                var bind = new List<string>();
                if (ItemTipFormat.CantTrade(def.Flags)) bind.Add("echange_interdit");
                if (ItemTipFormat.BindsOnEquip(def.Flags)) bind.Add("lie_a_l_equipement");
                r.Binding = string.Join(";", bind);
                // restriction de classe : c_item col 77, masque u64 bit(classId-1) (preuve : arcs -> classes Chasseur)
                rawRows.TryGetValue(id, out var raw);
                var maskStr = raw == null ? "" : IniTable.GetStr(raw, 77);
                ulong.TryParse(maskStr, out var mask);
                if (mask == 0 && long.TryParse(maskStr, out var sm) && sm < 0) mask = unchecked((ulong)sm);
                r.ClassMask = mask;
                var cids = new List<int>();
                for (var b = 0; b < 64; b++) if ((mask >> b & 1UL) != 0) cids.Add(b + 1);
                r.ClassIds = string.Join(",", cids);
                r.ClassNames = string.Join(", ", cids.Select(c => classNames.TryGetValue(c, out var n) && n.Length > 0 ? n : "classe#" + c));
                r.CategoryCol33 = raw == null ? 0 : IniTable.GetInt(raw, 33);
                r.Stats = StatsOf(def, TI);
                if (def.UseEnchantId > 0)
                {
                    if (enchText.TryGetValue(def.UseEnchantId, out var et))
                    {
                        var d = CleanText(et.Field(3)); if (d.Length == 0) d = CleanText(et.Field(2));
                        var n = CleanText(et.Field(1));
                        r.UseEffect = d.Length > 0 ? (n.Length > 0 ? n + " : " + d : d) : n;
                    }
                    else anomalies.Add("enchant_usage_sans_texte");
                }
            }
            else
            {
                r.TypeCode = ""; r.TypeLabel = "Type inconnu (sans c_item)"; r.TypeLabelSource = "derive"; r.Family = "Autre";
            }
            if (refs.TryGetValue(id, out var rs)) r.ReferencedBy = string.Join(",", rs);
            r.Anomalies = string.Join(";", anomalies);
            res.Rows.Add(r);
        }

        // ---- rapport : écarts entre tables ----
        Cnt("items", res.Rows.Count);
        Cnt("c_item", cItem.Count); Cnt("t_item", tItem.Count); Cnt("c_itemmall", cMall.Count); Cnt("t_itemmall", tMall.Count);
        Cnt("c_item_sans_t_item", cItem.Keys.Count(k => !tItem.ContainsKey(k) && !tMall.ContainsKey(k)));
        Cnt("t_item_sans_c_item", tItem.Keys.Count(k => !cItem.ContainsKey(k) && !cMall.ContainsKey(k)));
        Cnt("c_itemmall_sans_t_itemmall", cMall.Keys.Count(k => !tMall.ContainsKey(k) && !tItem.ContainsKey(k)));
        Cnt("t_itemmall_sans_c_itemmall", tMall.Keys.Count(k => !cMall.ContainsKey(k) && !cItem.ContainsKey(k)));
        Cnt("ids_dans_base_et_mall_texte", tItem.Keys.Count(tMall.ContainsKey));
        Cnt("ids_dans_base_et_mall_c", cItem.Keys.Count(cMall.ContainsKey));
        Cnt("sans_nom", res.Rows.Count(x => x.NameSource == "absent"));
        Cnt("sans_nom_aucune_ligne_texte", res.Rows.Count(x => x.NameSource == "absent" && !tItem.ContainsKey(x.Id) && !tMall.ContainsKey(x.Id)));
        Cnt("sans_nom_ligne_texte_nom_vide", res.Rows.Count(x => x.NameSource == "absent" && (tItem.ContainsKey(x.Id) || tMall.ContainsKey(x.Id))));
        Cnt("sans_nom_mais_nom_big5_c_item", res.Rows.Count(x => x.NameSource == "absent" && x.NameBig5.Length > 0));
        Cnt("sans_description_avec_effet_usage", res.Rows.Count(x => x.Description.Length == 0 && x.UseEffect.Length > 0));
        Cnt("sans_niveau_requis_equipement", res.Rows.Count(x => x.RequiredLevel == 0 && x.Family is "Armure" or "Arme" or "Bouclier" or "Accessoire"));
        Cnt("niveau_requis_cote_equipement", res.Rows.Count(x => x.RequiredLevel > 0 && x.Family is "Armure" or "Arme" or "Bouclier" or "Accessoire"));
        Cnt("sans_description", res.Rows.Count(x => x.Description.Length == 0));
        Cnt("sans_niveau_requis", res.Rows.Count(x => x.RequiredLevel == 0));
        Cnt("sans_c_item", res.Rows.Count(x => x.TypeCode == ""));
        Cnt("noms_test", res.Rows.Count(x => x.Anomalies.Contains("nom_test")));
        var allSet = new HashSet<int>(all);
        foreach (var kv in refAll.OrderBy(k => k.Key))
        {
            var miss = kv.Value.Where(x => !allSet.Contains(x)).OrderBy(x => x).ToList();
            Cnt($"ref:{kv.Key}:ids", kv.Value.Count);
            Cnt($"ref:{kv.Key}:absents", miss.Count);
            Rep($"ref:{kv.Key}\tdistincts={kv.Value.Count}\tabsents_des_tables_items={miss.Count}\texemples={string.Join(",", miss.Take(12))}");
        }
        // items nommés mais sans description
        return res;
    }

    static string SlotLabel(int loc, Func<int, string> ti)
    {
        if (loc <= 0) return "";
        // c_item col 9 -> textindex 1047.. (ItemTipFormat.LocLabel : ancres ref-shots, loc 1..20)
        if (loc is >= 1 and <= 20)
        {
            var s = ti(1046 + loc);
            if (s.Length > 0) return s.Trim('(', ')');
        }
        return loc switch
        {
            22 => "Esprit gardien", 23 or 30 => "Bâtiment", 32 => "Outil de récolte", 33 => "Expertise de classe",
            34 => "Équipement de familier", 35 => "2 mains (projectile)", _ => $"Emplacement {loc} (non documenté)",
        };
    }

    static string StatsOf(ItemTable.ItemDef d, Func<int, string> ti)
    {
        var parts = new List<string>();
        void A(string label, int v) { if (v != 0) parts.Add($"{label.Trim()} {v}"); }
        A(ti(1180), d.PhysicalAttack); A(ti(1181), d.MagicAttack); A(ti(1182), d.Defence);
        A(ti(1183), d.BonusStr); A(ti(1184), d.BonusAgi); A(ti(1185), d.BonusInt); A(ti(1186), d.BonusWis); A(ti(1187), d.BonusLck);
        A("PV", d.BonusHp); A("PM", d.BonusMp);
        A(ti(1190), d.Hit); A(ti(1191), d.Dodge); A(ti(1192), d.Critical); A(ti(1193), d.CriticalDamage);
        A(ti(1194), d.MagicCritical); A(ti(1195), d.MagicCriticalDamage);
        A(ti(1196), d.AtkSpeedBonus); A(ti(1197), d.CastSpeed); A(ti(1198), d.GHealing); A(ti(1199), d.PHealing);
        A(ti(1200), d.Parry); A(ti(1201), d.Block);
        for (var i = 0; i < d.Resistances.Length; i++) A(ti(1202 + i), d.Resistances[i]);
        if (d.MaxSocket > 0) parts.Add($"sertissages max {d.MaxSocket}");
        return string.Join("; ", parts);
    }
}
