using Godot;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// Liste multi-colonnes native (<c>DejaVu/MultiColumnList</c>) —
/// instanciée par <c>CeguiToGodot</c> avec les colonnes parsées des
/// props <c>ColumnHeader</c>. Adossée à <see cref="Tree"/> (seul
/// contrôle Godot à en-têtes) : titres cliquables = tri, largeurs
/// proportionnelles aux <c>width:{scale,offset}</c> natifs, icône en
/// colonne 0, double-clic = activation.
///
/// API minimale orientée « remplissage » : <see cref="ClearRows"/>,
/// <see cref="AddRow"/>, <see cref="SelectedRow"/>. Les cellules
/// restent du texte — les icônes de prix/couleurs se posent via
/// <see cref="SetCellColor"/>/<see cref="SetRowIcon"/> au besoin.
/// </summary>
public sealed partial class MclList : Tree
{
    private sealed class Row
    {
        public required string[] Cells;
        public Texture2D? Icon;
        public Color? IconTint;
        public readonly Dictionary<int, Color> CellColors = new();
        public object? Tag;
    }

    private readonly List<Row> _rows = new();
    private readonly List<MclColumn> _cols = new();
    private int _sortCol = -1;
    private bool _sortAsc = true;

    /// <summary>Activation d'une ligne (double-clic / Entrée).</summary>
    public event Action<int>? RowActivated;
    /// <summary>Changement de sélection.</summary>
    public event Action<int>? RowSelected;

    /// <summary>Index de la ligne sélectionnée (−1 = aucune).</summary>
    public int SelectedRow { get; private set; } = -1;

    public MclList(IReadOnlyList<MclColumn>? cols = null)
    {
        HideRoot = true;
        AllowRmbSelect = true;
        ColumnTitlesVisible = true;
        FocusMode = FocusModeEnum.None;
        if (cols != null) SetColumns(cols);
        ItemSelected += OnItemSelected;
        ItemActivated += OnItemActivated;
        ColumnTitleClicked += (col, _idx) => SortBy((int)col);
    }

    /// <summary>Colonnes natives — ordre = ordre du layout ; le tri se
    /// fait sur l'index de colonne affichée.</summary>
    public void SetColumns(IReadOnlyList<MclColumn> cols)
    {
        _cols.Clear();
        _cols.AddRange(cols);
        Columns = Math.Max(_cols.Count, 1);
        for (var i = 0; i < _cols.Count; i++)
        {
            SetColumnTitle(i, EdenClient.Data.UiTextEn.Translate(_cols[i].Text));
            SetColumnExpand(i, true);
            SetColumnClipContent(i, true);
        }
        ReflowWidths();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            ReflowWidths();
    }

    /// <summary>Distribue la largeur disponible selon les parts
    /// <c>width:{scale,offset}</c> natives (min 24 px/colonne).</summary>
    private void ReflowWidths()
    {
        if (_cols.Count == 0) return;
        var w = Size.X;
        for (var i = 0; i < _cols.Count; i++)
        {
            var px = _cols[i].WidthScale * w + _cols[i].WidthOffset;
            SetColumnCustomMinimumWidth(i, Math.Max(24, (int)px));
        }
    }

    /// <summary>Nombre de lignes.</summary>
    public int RowCount => _rows.Count;

    /// <summary>Vide toutes les lignes.</summary>
    public void ClearRows()
    {
        _rows.Clear();
        SelectedRow = -1;
        Clear();
    }

    /// <summary>Ajoute une ligne ; <paramref name="cells"/> est
    /// tronqué/complété au nombre de colonnes.</summary>
    public int AddRow(IReadOnlyList<string> cells, Texture2D? icon = null,
        object? tag = null)
    {
        var r = new Row { Cells = new string[Math.Max(_cols.Count, 1)] };
        for (var i = 0; i < r.Cells.Length; i++)
            r.Cells[i] = i < cells.Count ? cells[i] : "";
        r.Icon = icon;
        r.Tag = tag;
        _rows.Add(r);
        Rebuild();
        return _rows.Count - 1;
    }

    /// <summary>Donnée attachée à une ligne (id recette, entrée…).</summary>
    public object? RowTag(int row) =>
        row >= 0 && row < _rows.Count ? _rows[row].Tag : null;

    /// <summary>Texte d'une cellule.</summary>
    public string CellText(int row, int col) =>
        row >= 0 && row < _rows.Count && col >= 0 &&
        col < _rows[row].Cells.Length ? _rows[row].Cells[col] : "";

    /// <summary>Couleur d'une cellule (appliquée au prochain rebuild).</summary>
    public void SetCellColor(int row, int col, Color c)
    {
        if (row >= 0 && row < _rows.Count)
        {
            _rows[row].CellColors[col] = c;
            Rebuild();
        }
    }

    /// <summary>Icône de la colonne 0.</summary>
    public void SetRowIcon(int row, Texture2D? icon, Color? tint = null)
    {
        if (row >= 0 && row < _rows.Count)
        {
            _rows[row].Icon = icon;
            _rows[row].IconTint = tint;
            Rebuild();
        }
    }

    /// <summary>Tri sur une colonne (clic en-tête : alterne
    /// asc/desc) — comparaison numérique quand les deux cellules se
    /// parsent, texte insensible à la casse sinon.</summary>
    public void SortBy(int col)
    {
        if (_sortCol == col) _sortAsc = !_sortAsc;
        else { _sortCol = col; _sortAsc = true; }
        var dir = _sortAsc ? 1 : -1;
        _rows.Sort((a, b) =>
        {
            var sa = col < a.Cells.Length ? a.Cells[col] : "";
            var sb = col < b.Cells.Length ? b.Cells[col] : "";
            if (double.TryParse(sa, out var na) &&
                double.TryParse(sb, out var nb))
                return na.CompareTo(nb) * dir;
            return string.Compare(sa, sb,
                StringComparison.OrdinalIgnoreCase) * dir;
        });
        Rebuild();
    }

    /// <summary>Réécrit les TreeItems dans l'ordre courant de
    /// <see cref="_rows"/> (tri inclus) en préservant la sélection.</summary>
    private void Rebuild()
    {
        var selTag = SelectedRow >= 0 && SelectedRow < _rows.Count
            ? _rows[SelectedRow].Tag : null;
        Clear();
        var root = CreateItem();
        for (var i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            var it = CreateItem(root);
            for (var c = 0; c < r.Cells.Length && c < Columns; c++)
            {
                it.SetText(c, r.Cells[c]);
                if (r.CellColors.TryGetValue(c, out var col))
                    it.SetCustomColor(c, col);
            }
            if (r.Icon != null)
            {
                it.SetIcon(0, r.Icon);
                it.SetIconMaxWidth(0, 20);
                if (r.IconTint is { } tint)
                    it.SetIconModulate(0, tint);
            }
            it.SetMetadata(0, i);
            if (selTag != null && Equals(r.Tag, selTag))
            {
                it.Select(0);
                SelectedRow = i;
            }
        }
        if (selTag == null) SelectedRow = -1;
    }

    private void OnItemSelected()
    {
        var it = GetSelected();
        SelectedRow = it?.GetMetadata(0).AsInt32() ?? -1;
        RowSelected?.Invoke(SelectedRow);
    }

    private void OnItemActivated()
    {
        var it = GetSelected();
        if (it != null)
            RowActivated?.Invoke(it.GetMetadata(0).AsInt32());
    }
}
