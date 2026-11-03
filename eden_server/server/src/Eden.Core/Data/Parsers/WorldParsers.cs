using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

public static class WorldParsers
{
    // ---------- S_Npc.ini (|V.2|21|) ----------
    // Records multi-lignes: "50001|n106010|礦道長" + ligne suivante
    // "布萊德|1|1|0|2|1|11|50|4001###|#:Talk 4620 4621#:::::|...|0|任務|"
    public sealed class NpcDef
    {
        public int Id { get; set; }
        public string ModelCode { get; set; } = string.Empty;   // n106010
        public string Title { get; set; } = string.Empty;       // 礦道長
        public string Name { get; set; } = string.Empty;        // 布萊德
        public List<int> Flags { get; set; } = new();           // cols 1-7 ligne 2
        public int NpcType { get; set; }                        // col 6 ligne 2
        public int Level { get; set; }                          // col 7 ligne 2
        public string ItemRef { get; set; } = string.Empty;     // 4001###
        public string TalkScript { get; set; } = string.Empty;  // #:Talk ...
        public string Role { get; set; } = string.Empty;        // 任務 (col 18)
        /// <summary>Masque de localisation — body[17] (joint idx 19 →
        /// <c>CNpcData+0x38</c>, lu juste après le vecteur de 8 scripts
        /// dans <c>CNpcQuery::Deserialize</c>). La zone native ignore le
        /// template si <c>mask != 0 &amp;&amp; ((mask &gt;&gt; Locate) &amp;
        /// 1) == 0</c> (<c>GetAllNPCTemplate</c> @0x08368260) — marqueur
        /// « NPC régional / event » (festivals, 網咖, olympiques…).
        /// Bit 5 = US (preuve : 51254-6 « 美版專用 NPC » = 160).</summary>
        public int LocateMask { get; set; }
        /// <summary>Vrai si le NPC peut apparaître pour la localisation
        /// <paramref name="locate"/> du serveur (règle native).</summary>
        public bool VisibleAtLocate(int locate) =>
            LocateMask == 0 || ((LocateMask >> (locate & 0x1f)) & 1) != 0;
        /// <summary>Services du PNJ extraits des scripts
        /// "#:Service &lt;dialogId&gt; &lt;type&gt; &lt;refId&gt; [extra]" :
        /// type 3 = boutique (refId = S_Store), 35 = instance_dialog
        /// (refId+extra → 564), 24 = prescription_item (boutique),
        /// 12-17 = métiers EnterWorks, 56 = mission_shop… Table complète
        /// type→verbe désasm TC_Service @0x80ef980 (mission).</summary>
        /// <summary>Une option « #:Service » du NPC — avec sa liste de
        /// conditions (`#:Service …#:If_*:…#`) et le texte de refus
        /// (segment terminal, ex. `:15375` — affiché quand les
        /// conditions échouent, preuve : 5012 = « you didn't bring
        /// enough » derrière `If_InventoryItem >= 10`).</summary>
        public sealed record NpcService(int DialogId, int Type, int RefId,
            int Extra, IReadOnlyList<string> Conds, int FailBody);
        public List<NpcService> Services { get; } = new();
        /// <summary>Premier storeId de type 3 (vendor), 0 si pas vendeur.</summary>
        public int StoreId =>
            Services.FirstOrDefault(s => s.Type == 3)?.RefId ?? 0;
        /// <summary>Fonction directe du NPC — code `#fn#arg#` dans la
        /// colonne itemref/script (ex. 50360 `#8#50#` = transport array).
        /// Ces NPC n'ont aucun #:Talk : un clic droit ouvre DIRECTEMENT
        /// l'UI du service, jamais une fenêtre de dialogue.
        /// fn : 1/2/3 = boutique (arg = S_Store), 8 = transport (arg =
        /// groupe S_Transport), 18 = banque (357), 6 = enchères (430),
        /// 23 = boutique prestige régional, 24 = boutique spéciale,
        /// 55 = event. 0 = pas de fonction directe.</summary>
        public int FuncId { get; set; }
        /// <summary>Argument de la fonction (`#8#50#` → 50) ; -1 si vide
        /// (`#18##`) ou lettre (`#1#M#`).</summary>
        public int FuncArg { get; set; } = -1;
    }

    /// <summary>
    /// S_Npc.ini — chaque NPC = ligne head "id|code|titre" (3 champs)
    /// suivie de la ligne body "nom|flags|...|script|...|role|".
    /// </summary>
    public static List<NpcDef> ParseNpcs(string filePath)
    {
        var raw = System.IO.File.ReadAllBytes(filePath);
        var text = EdenEncoding.Big5.GetString(
            MyCrypt.DecodeFile(raw));
        var lines = text.Split(new[] { "\r\n", "\n" },
            System.StringSplitOptions.RemoveEmptyEntries);

        var npcs = new List<NpcDef>();
        for (var i = 1; i < lines.Length; i++)
        {
            var head = lines[i].Split('|');
            // Deux formats coexistent dans S_Npc.ini :
            //  - 2 lignes : head "id|modelcode|title" (3 champs) + body
            //    "nom|flags|...|script|...|role|" (19 champs) ;
            //  - 1 ligne fusionnée (21 champs) : id|model|title|cols1..18
            //    sans le nom (le titre tient lieu de nom) — les NPC de
            //    service purs (transport 50360…) sont dans ce format.
            string[] body;
            var merged = false;
            if (head.Length >= 15 && int.TryParse(head[0], out _))
                merged = true;                 // record fusionné
            else if (head.Length == 3 && int.TryParse(head[0], out _))
                body = null!;                  // body sur la ligne suivante
            else
                continue;
            if (merged)
            {
                // body[0]=nom absent ; body[k] = head[k+2] pour k>=1.
                var b = new string[19];
                b[0] = head.Length > 2 ? head[2] : string.Empty;
                Array.Copy(head, 3, b, 1, Math.Min(head.Length - 3, 18));
                body = b;
            }
            else
            {
                body = i + 1 < lines.Length
                    ? lines[i + 1].Split('|') : Array.Empty<string>();
            }
            int.TryParse(head[0], out var id);

            var npc = new NpcDef
            {
                Id = id,
                ModelCode = head.Length > 1 ? head[1] : string.Empty,
                Title = head.Length > 2 ? head[2] : string.Empty,
                Name = body.Length > 0 ? body[0] : string.Empty,
                NpcType = body.Length > 6 && int.TryParse(body[6], out var t) ? t : 0,
                Level = body.Length > 7 && int.TryParse(body[7], out var l) ? l : 0,
                ItemRef = body.Length > 8 ? body[8] : string.Empty,
                TalkScript = body.Length > 9 ? body[9] : string.Empty,
                Role = body.Length > 18 ? body[18] : string.Empty,
                // body[17] = masque Locate (CNpcData+0x38) — voir
                // GetAllNPCTemplate @0x08368260 ; merged : b[17]=head[19].
                LocateMask = body.Length > 17 &&
                    int.TryParse(body[17], out var lm) ? lm : 0,
            };
            // Options « #:Service <dlg> <type> [ref] [extra] #:<conds>#
            // :<actions>#:<failBody> » dans les colonnes 9+ — le refId
            // peut être VIDE (banquier 50347 : `#:Service 5218 6  #` →
            // banque sans référence) : l'ancien motif à 3 groupes
            // obligatoires faisait passer le NPC à côté (constaté live).
            // Format natif décodé de InsertNPCServiceDB (mission
            // @0x8066730) : chaque option = segments séparés par '#'.
            foreach (var col in body.Skip(9))
                foreach (var seg in col.Split('|'))
                {
                    var opt = ParseDialogOption(seg);
                    if (opt is { Head.Length: >= 3 } o &&
                        o.Head[0] == "Service" &&
                        int.TryParse(o.Head[1], out var dlg) &&
                        int.TryParse(o.Head[2], out var type))
                        npc.Services.Add(new NpcDef.NpcService(dlg, type,
                            o.Head.Length > 3 &&
                                int.TryParse(o.Head[3], out var r) ? r : 0,
                            o.Head.Length > 4 &&
                                int.TryParse(o.Head[4], out var x) ? x : 0,
                            o.Conds, o.FailBody));
                }
            // Fonction directe `#fn#arg#` (colonne itemref/script) —
            // `#1#1#` shop, `#8#50#` transport, `#18##` banque…
            // Le motif exige # + chiffres + # + chiffres|M + # :
            // `4001###` et `#:Talk …#` ne matchent pas.
            foreach (System.Text.RegularExpressions.Match fm in
                System.Text.RegularExpressions.Regex.Matches(
                    string.Join('|', head) + "|" + string.Join('|', body),
                    @"#(\d+)#([Mm\d]*)#"))
            {
                npc.FuncId = int.Parse(fm.Groups[1].Value);
                npc.FuncArg = fm.Groups[2].Value.Length > 0 &&
                    int.TryParse(fm.Groups[2].Value, out var fa) ? fa : -1;
                break;
            }
            npcs.Add(npc);
            if (!merged) i++; // body consommee (record 2 lignes)
        }
        return npcs;
    }

    // ---------- S_Dialogue.ini (|V.1|2|) ----------
    public static Dictionary<int, string> ParseDialogues(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var map = new Dictionary<int, string>();
        foreach (var row in table.Rows)
            if (row.Length >= 2 && int.TryParse(row[0], out var id))
                map[id] = row[1];
        return map;
    }

    // ---------- S_Transport.ini (|V.10|59|) ----------

    public sealed class TransportRoute
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;   // Big5
        public int[] Costs { get; set; } = new int[13];    // cols 4-16 par palier
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    /// <summary>S_Transport.ini — couts de transport par palier de niveau.</summary>
    public static List<TransportRoute> ParseTransport(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<TransportRoute>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 17 || !int.TryParse(row[0], out var id)) continue;
            var r = new TransportRoute
            {
                Id = id, Name = IniTable.GetStr(row, 1), Raw = row,
            };
            for (var i = 0; i < 13; i++)
                r.Costs[i] = IniTable.GetInt(row, 4 + i);
            list.Add(r);
        }
        return list;
    }

    // ---------- S_LifeSkill.ini (|V.1|36|) ----------

    public sealed class LifeSkillNode
    {
        public int Id { get; set; }
        public int Type { get; set; }                 // 1=gather?
        public int SourceItemId { get; set; }         // 36792 (matiere premiere)
        public string SourceName { get; set; } = string.Empty;
        public int RequiredLevel { get; set; }        // col 5
        public int TimeSeconds { get; set; }          // col 8 (200)
        public List<(int ItemId, string Name, int Value)> Products { get; } = new();
    }

    /// <summary>S_LifeSkill.ini — nodes de recolte → produits.</summary>
    public static List<LifeSkillNode> ParseLifeSkills(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<LifeSkillNode>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 12 || !int.TryParse(row[0], out var id)) continue;
            var n = new LifeSkillNode
            {
                Id = id,
                Type = IniTable.GetInt(row, 1),
                SourceItemId = IniTable.GetInt(row, 2),
                SourceName = IniTable.GetStr(row, 3),
                RequiredLevel = IniTable.GetInt(row, 5),
                TimeSeconds = IniTable.GetInt(row, 8),
            };
            // triplets produit: [itemId|nom|valeur] a partir de col 21
            for (var i = 21; i + 2 < row.Length; i += 3)
            {
                var itemId = IniTable.GetInt(row, i);
                if (itemId <= 0) break;
                n.Products.Add((itemId, IniTable.GetStr(row, i + 1),
                    IniTable.GetInt(row, i + 2)));
            }
            list.Add(n);
        }
        return list;
    }

    // ---------- S_Partner.ini (|V.2|30|) ----------

    public sealed class PartnerDef
    {
        public int Id { get; set; }
        public string ItemCode { get; set; } = string.Empty;  // I00557
        public string ModelCode { get; set; } = string.Empty; // M144010
        public string Name { get; set; } = string.Empty;      // 小博美狗
        public int Type { get; set; }
        public int Level { get; set; }
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    public static List<PartnerDef> ParsePartners(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<PartnerDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id)) continue;
            list.Add(new PartnerDef
            {
                Id = id,
                ItemCode = IniTable.GetStr(row, 1),
                ModelCode = IniTable.GetStr(row, 2),
                Name = IniTable.GetStr(row, 3),
                Type = IniTable.GetInt(row, 4),
                Level = IniTable.GetInt(row, 12),
                Raw = row,
            });
        }
        return list;
    }

    /// <summary>Une option de dialogue NPC (segment `|…|`) décomposée :
    /// `#:<cmd args…>#:<conds>:…:#:<actions>:…:#:<failBody>` —
    /// Head = tokens de la commande (Talk/Service…), Conds = If_*,
    /// Actions = verbes exécutés à la sélection, FailBody = texte de
    /// refus affiché quand les conditions échouent.</summary>
    public sealed record DialogOption(string[] Head,
        IReadOnlyList<string> Conds, IReadOnlyList<string> Actions,
        int FailBody);

    /// <summary>Découpe une option de dialogue NPC
    /// (`#:Talk L B#:conds:#:actions:#:fail`) en ses segments — même
    /// grammaire pour les `#:Service` (InsertNPCServiceDB /
    /// InsertNPCTalkDB, mission @0x8066-7). Chaque segment entre '#'
    /// commence par ':' ; ses éléments internes sont séparés par ':'.
    /// Le dernier segment non vide sans verbe connu = texte de refus.</summary>
    public static DialogOption ParseDialogOption(string segment)
    {
        var pieces = segment.Split('#',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var groups = new List<List<string>>();
        foreach (var p in pieces)
        {
            var items = p.TrimStart(':').Split(':',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            groups.Add(items.ToList());
        }
        // La tête (`Talk …`, `Service …`) est le 1er token du groupe 0 —
        // les tokens suivants sont ses arguments.
        var headToks = groups.Count > 0 && groups[0].Count > 0
            ? groups[0][0].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        var failBody = 0;
        var conds = new List<string>();
        var actions = new List<string>();
        for (var g = 1; g < groups.Count; g++)
        {
            // Segment terminal « :<num> » (texte de refus) — pas de verbe.
            if (groups[g].Count == 1 &&
                int.TryParse(groups[g][0], out var tail) &&
                !groups[g][0].Contains(' '))
            {
                failBody = tail;
                continue;
            }
            foreach (var item in groups[g])
                if (item.StartsWith("If_", StringComparison.Ordinal))
                    conds.Add(item);
                else
                    actions.Add(item);
        }
        return new DialogOption(headToks, conds, actions, failBody);
    }
}
