using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Liste des villes de guilde (VDG) — lot B6. Layout natif
/// <c>familytownlistwnd.layout</c> (« Root/TownList ») : liste des
/// villes, 6 cases de filtre bâtiment, bornes de niveau, recherche
/// nom, infos ville (chef/exp/membres/taxe), liste MCL des membres.
///
/// Wire (preuves : <c>ZoneEntryFlow.FamilyInstance.cs</c>,
/// <c>FamilyPackets.FamilyTownQuery</c>, désasm
/// <c>CPlayerFactory::FamilyTownQuery @0x8110830</c>) :
///   - Ouverture : dialogue NPC service 25 → <b>514</b>
///     <c>ClientEnterFamilyTownList</c> → la fenêtre s'ouvre et le
///     client envoie <b>174</b>
///     <c>[s16 lvlMin][s16 lvlMax][i32 buildingMask][str nameFilter]</c>
///     (masque = types S_Buliding 4-9 → bits 0-5, cases 1-6).
///   - <b>502</b> S→C : résultats en vecteurs parallèles
///     (<see cref="ZoneClientFamilyTownQuery"/>) — chunks de 10,
///     dernier paquet toujours émis (même vide = fin).
///   - <b>175</b> C→S <c>[i32 famId]</c> → <b>503</b> membres
///     (vecteurs charId/nom/niveau/rang/node).
///   - <b>176</b> C→S <c>[i32 famId]</c> : entrer dans la ville
///     (famId=0 = quitter la sienne).
/// </summary>
public partial class TownListWindow : Control
{
    private Control? _window;
    private ItemList? _list;
    private MclList? _members;
    private readonly List<ZoneClientFamilyTownQuery.Entry> _towns =
        new();
    private int _sel = -1;
    private bool _seenTrailer;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        MiscPackets.Received += OnPacket;
        TownDetailUi.Received += OnTownDetail;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        TownDetailUi.Received -= OnTownDetail;
        base._ExitTree();
    }

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientEnterFamilyTownList:
                Open();
                break;
            case ZoneClientFamilyTownQuery q:
                if (!_seenTrailer && q.Entries.Count == 0
                    && _towns.Count > 0)
                { _seenTrailer = true; break; }   // chunk final vide
                if (q.Entries.Count == 0 && _towns.Count == 0)
                {
                    HudExtensions.FindHud()?.Chat?.AddSystem(
                        "Aucune ville de guilde ne correspond à la recherche.");
                    GD.Print("[vdg-panel] 502 vide : aucune ville");
                    break;
                }
                _towns.AddRange(q.Entries);
                FillTowns();
                break;
            case ZoneClientFamilyTownMemberQuery mq:
                FillMembers(mq);
                break;
        }
    }

    // ------------------------- réseau -------------------------

    private void Send(NetMessage msg)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true)
        {
            GD.PushWarning($"[townlist] pas de session — {msg.Name}");
            return;
        }
        _ = s.SendZoneAsync(msg, CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[townlist] {msg.Name} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Ouverture (514 NPC ou bouton « villes » de la
    /// fenêtre guilde) — affiche + envoie la requête 174.</summary>
    public void Open()
    {
        if (_window == null) BuildWindow();
        _towns.Clear();
        _sel = -1;
        _seenTrailer = false;
        _window!.Visible = true;
        Visible = true;
        // Au premier plan (devant la fenêtre guilde qui l'a ouverte).
        if (GetParent() is { } par) par.MoveChild(this, par.GetChildCount() - 1);
        _window.MoveToFront();
        HudWindows.Front(_window);
        Query();
        GD.Print("[verify] townlist : 514 reçu — ouverture + 174");
    }

    private void Query()
    {
        if (_window == null) return;
        var mask = 0;
        for (var i = 0; i < 6; i++)
        {
            var cb = FindLeaf<BaseButton>(_window, $"Checkbox{i + 1}");
            if (cb is { ButtonPressed: true }) mask |= 1 << i;
        }
        // Le serveur filtre lo ≤ niveau ≤ hi (inclusif) : un champ vide
        // doit donc valoir « sans borne », pas 0 (sinon aucune ville).
        var lo = Math.Max(0, GetInt("LevelLowerLimit", 0));
        var hi = GetInt("LevelUpLimit", 0);
        if (hi <= 0) hi = 999;
        var name = FindLeaf<LineEdit>(_window, "SearchNpc")?.Text
            ?? "";
        _towns.Clear();
        _sel = -1;
        _seenTrailer = false;
        Send(new ZoneServerFamilyTownQuery
        {
            Field1 = (short)lo, Field2 = (short)hi,
            Field3 = mask, Field4 = name,
        });
    }

    private void Enter()
    {
        if (_sel < 0 || _sel >= _towns.Count)
        {
            HudExtensions.FindHud()?.Chat?.AddSystem(
                "Sélectionnez d'abord une ville dans la liste.");
            return;
        }
        var t = _towns[_sel];
        GD.Print($"[vdg-panel] TownList Enter → 176 fam={t.FamilyId} " +
            $"<{t.Name}> houseOpen={t.HouseOpen}");
        Send(new ZoneServerFamilyTownEnter { Value = t.FamilyId });
        // La visite migre vers le node 501/502 : on ferme la liste.
        if (_window != null) _window.Visible = false;
    }

    private void Members()
    {
        if (_sel < 0 || _sel >= _towns.Count) return;
        Send(new ZoneServerFamilyTownMemberQuery
            { Value = _towns[_sel].FamilyId });
    }

    // ------------------------- fenêtre -------------------------

    private void BuildWindow()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("familytownlistwnd.layout");
        if (_window == null) { BuildFallback(); return; }
        AddChild(_window);
        // Racine à ancres d'échelle : Center lirait une taille nulle
        // (fenêtre repliée, cases qui se chevauchent).
        HudExtensions.CenterNative(_window, new Vector2(852, 524));
        DragHelper.EnsureTitleBar(_window,
            UiTextEn.Translate("Guild towns"));
        DragHelper.Persist(_window, "townlist");
        ClearDetails();
        // Le layout pose « 999 » (gabarit) dans la borne basse : on
        // vide les deux bornes pour que la 1re requête liste tout.
        if (FindLeaf<LineEdit>(_window, "LevelLowerLimit") is { } llo)
            llo.Text = "";
        if (FindLeaf<LineEdit>(_window, "LevelUpLimit") is { } lhi)
            lhi.Text = "";
        // « xNPC » (gabarit) : champ de recherche vide + indice.
        if (FindLeaf<LineEdit>(_window, "SearchNpc") is { } sn)
        {
            sn.Text = "";
            sn.PlaceholderText = UiTextEn.Translate("NPC name");
        }
        _list = FindLeaf<ItemList>(_window, "TownList");
        _members = FindLeaf<MclList>(_window, "NPCList");
        if (_list != null)
            _list.ItemSelected += i => SelectTown((int)i);
        if (FindLeaf<BaseButton>(_window, "TownSerachBtn")
            is { } q) q.Pressed += Query;
        if (FindLeaf<BaseButton>(_window, "EnterTownBtn")
            is { } e) e.Pressed += Enter;
        if (FindLeaf<BaseButton>(_window, "MemberSerachBtn")
            is { } mb) mb.Pressed += Members;
        if (FindLeaf<BaseButton>(_window, "CloseBtn")
            is { } c) c.Pressed += () => _window.Visible = false;
    }

    private void SelectTown(int i)
    {
        if (i < 0 || i >= _towns.Count) return;
        _sel = i;
        var t = _towns[i];
        // Les labels du layout (« 會長： », « 聲望： »…) sont écrasés :
        // on réécrit l'intitulé + la valeur en français.
        SetText("FamilyNameText", $"L{t.Level:D2}.{t.Name}");
        SetText("LeaderText", $"Chef : {t.Leader}");
        SetText("ExpText", $"Réputation : {t.Exp}");
        SetText("MemberCountText", $"Membres : {t.Members}");
        SetText("BusinessTaxText", $"Taxe : {t.BusinessTax} %");
        // house_open = node du sanctuaire : 501 forêt, 502 montagne.
        var kind = t.HouseOpen switch
        {
            501 => UiTextEn.Translate("Forest sanctuary"),
            502 => UiTextEn.Translate("Mountain sanctuary"),
            _ => "—",
        };
        SetText("InstanceType", "Type de ville : " + kind);
        FillBuildings(t);
        TownDetailUi.Request(t.FamilyId);
    }

    /// <summary>Panneau de détails vide (aucune ville choisie) : les
    /// gabarits du layout (« L01.… », « 12345678 ») sont écrasés.</summary>
    private void ClearDetails()
    {
        SetText("FamilyNameText", "");
        SetText("LeaderText", "Chef :");
        SetText("ExpText", "Réputation :");
        SetText("MemberCountText", "Membres :");
        SetText("BusinessTaxText", "Taxe :");
        SetText("InstanceType", "Type de ville :");
        foreach (var leaf in new[] { "LeaderText", "ExpText",
                     "MemberCountText", "BusinessTaxText", "InstanceType" })
        {
            if (_window != null &&
                FindLeaf<Control>(_window, leaf) is { } c)
                c.ClipContents = true;
        }
    }

    /// <summary>BuildingListBorder/BuildingPane : bâtiments de la ville
    /// sélectionnée (vecteurs 9-16 du 502).</summary>
    private void FillBuildings(ZoneClientFamilyTownQuery.Entry t)
    {
        if (_window == null ||
            FindLeaf<ScrollContainer>(_window, "BuildingPane")
                is not { } pane) return;
        _buildingBox ??= new VBoxContainer
        {
            Name = "__buildings",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        if (_buildingBox.GetParent() == null) pane.AddChild(_buildingBox);
        foreach (var c in _buildingBox.GetChildren()) c.QueueFree();
        var buildings = t.Buildings.Where(b => b.ItemType == 0x29).ToList();   // 0x2a/0x52 = PNJ (liste de droite)
        _buildingBox.AddChild(Dark($"Bâtiments actifs : {buildings.Count} — niveau de ville {t.Level}"));
        foreach (var b in buildings.OrderBy(x => x.Loc))
            _buildingBox.AddChild(Dark(DescribeBuilding(b)));
        FillNpcs(t);
    }

    /// <summary>Label lisible : la zone des bâtiments est claire (le thème met du texte blanc).</summary>
    private static Label Dark(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", new Color(0.1f, 0.1f, 0.12f));
        l.AddThemeFontSizeOverride("font_size", 12);
        return l;
    }

    /// <summary>Grande liste du panneau (« NPCList » natif) : les PNJ réellement posés de la ville.</summary>
    private void FillNpcs(ZoneClientFamilyTownQuery.Entry t)
    {
        if (_members == null) return;
        _members.ClearRows();
                if (!_npcs.TryGetValue(t.FamilyId, out var npcs))
        {
            _members.AddRow(new[] { "Chargement…", "", "" });
            return;
        }
        foreach (var n in npcs.OrderBy(n => n.Role == TownDetailUi.Role.ToolVendor).ThenBy(n => n.Loc))
        {
            var row = _members.AddRow(NpcCells(n, t));
            _members.SetCellColor(row, 0, new Color(0.1f, 0.1f, 0.12f));   // fond clair du layout : texte sombre
        }
    }

    /// <summary>Type de bâtiment (c_buliding col 3) → libellé FR.</summary>
    private static string BuildingTypeName(int type) => type switch
    {
        1 => "Manoir", 2 => "Hôtel", 4 => "Ferme", 5 => "Mine", 6 => "Autel",
        7 => "Alchimie", 8 => "Fonderie", 9 => "Mare magique", 10 => "Marché",
        _ => "Bâtiment",
    };

    /// <summary>« Mine — niv. 12 — durabilité 100 » : nom du TYPE de bâtiment (et
    /// non son identifiant), niveau et état.</summary>
    private static string DescribeBuilding(ZoneClientFamilyTownQuery.Building b)
    {
        var def = Data.Tables.HouseTable.GetBuilding(b.Tid);
        var name = def != null ? BuildingTypeName(def.Type)
            : TextResolver.ItemName(b.ItemTid) ?? $"#{b.ItemTid}";
        var due = b.DueDate > 0 ? " — contrat limité" : "";
        return $"{name} — niv. {b.Level} — durabilité {b.Durability}{due}";
    }
    private VBoxContainer? _buildingBox;
    private readonly Dictionary<int, IReadOnlyList<TownDetailUi.Npc>> _npcs = new();

    /// <summary>Réponse 9064 : PNJ posés de la ville → section « PNJ actifs » du panneau.</summary>
    private void OnTownDetail(int familyId, IReadOnlyList<TownDetailUi.Npc> npcs)
    {
        _npcs[familyId] = npcs;
        if (_sel >= 0 && _sel < _towns.Count && _towns[_sel].FamilyId == familyId)
            FillBuildings(_towns[_sel]);
    }

    private static string[] NpcCells(TownDetailUi.Npc n, ZoneClientFamilyTownQuery.Entry t)
    {
        var name = TextResolver.NpcName(n.Tid) ?? $"PNJ #{n.Tid}";
        var title = TextResolver.NpcTitle(n.Tid);
        var label = string.IsNullOrEmpty(title) ? name : $"{title} {name}";
        var role = n.Role switch
        {
            TownDetailUi.Role.Housekeeper => "responsable",
            TownDetailUi.Role.ToolVendor => "vendeur d'outils",
            _ => "service",
        };
        var home = t.Buildings.FirstOrDefault(b => b.Loc == n.Loc / 10 * 10);
        var where = n.Role == TownDetailUi.Role.ToolVendor || home.Tid == 0 && home.ItemTid == 0
            ? "zones de récolte" : BuildingTypeName(Data.Tables.HouseTable.GetBuilding(home.Tid)?.Type ?? 0);
        return new[] { $"{label} — {role} — {where}" };   // le layout natif n'a qu'une colonne
    }

    private void FillTowns()
    {
        if (_list == null) return;
        _list.Clear();
        foreach (var t in _towns)
            _list.AddItem($"Niv.{t.Level} {t.Name}");
    }

    private void FillMembers(ZoneClientFamilyTownMemberQuery mq)
    {
        if (_members == null) return;
        _members.ClearRows();
        foreach (var e in mq.Entries)
            _members.AddRow(new[]
            {
                e.Name, $"{e.Level}",
                e.Rank == 1 ? UiTextEn.Translate("Leader") : "",
            });
    }

    private int GetInt(string leaf, int def)
    {
        var t = FindLeaf<LineEdit>(_window!, leaf)?.Text;
        return int.TryParse(t, out var v) ? v : def;
    }

    /// <summary>Premier descendant de type <typeparamref name="T"/> dont le nom finit par
    /// <paramref name="leaf"/>. Filtre par type C# (<c>is T</c>) : <c>FindChildren</c> ne reconnaît
    /// pas les classes de script (<c>MclList</c>…) par leur nom — la liste des PNJ/membres restait
    /// donc nulle et vide.</summary>
    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        foreach (var n in root.FindChildren("*", "", true, false))
            if (n is T t && n.Name.ToString().EndsWith(leaf))
                return t;
        return null;
    }

    private void SetText(string leaf, string text)
    {
        if (_window == null) return;
        if (FindLeaf<Label>(_window, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_window, leaf) is { } r)
            r.Text = text;
    }

    /// <summary>Repli sans uidata : liste + bouton entrée.</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "TownListWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-200, -220);
        p.Size = new Vector2(400, 440);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
        DragHelper.EnsureTitleBar(p,
            UiTextEn.Translate("Guild towns"));
        _list = new ItemList { Name = "__list" };
        _list.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _list.OffsetTop = 30;
        _list.OffsetBottom = -40;
        _list.ItemSelected += i => SelectTown((int)i);
        p.AddChild(_list);
        var enter = new Button
        {
            Text = UiTextEn.Translate("Enter"),
            Position = new Vector2(10, 400),
        };
        enter.Pressed += Enter;
        p.AddChild(enter);
    }
}
