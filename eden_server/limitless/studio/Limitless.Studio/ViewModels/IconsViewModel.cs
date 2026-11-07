using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>Page Icon finder — deux usages concrets : 1) trouver
/// l'icône d'un item/mob par son NOM (recherche entité → code →
/// aperçu + localisation dans la grille) ; 2) comprendre à quoi sert
/// un fichier icône (clic tuile → entités qui l'utilisent via
/// EntityIconIndex) ; copie du code au clic. La grille parcourt les
/// icônes réelles extraites du client (`UI/<set>/*.dds|png|tga`),
/// décodage en tâche de fond.</summary>
public partial class IconsViewModel : PageViewModel
{
    /// <summary>Une tuile de la grille — nom de fichier + bitmap.</summary>
    public partial class IconTile : ObservableObject
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        [ObservableProperty] private Bitmap? _image;
    }

    /// <summary>Un résultat de recherche entité (item/mob) — nom EN,
    /// code icône, bitmap décodée en fond.</summary>
    public partial class EntityHit : ObservableObject
    {
        public IconEntity Entity { get; }
        public EntityHit(IconEntity e) => Entity = e;
        public int Id => Entity.Id;
        public string Name => Entity.Name;
        public string IconCode => Entity.IconCode;
        public string Display =>
            $"{Entity.Id}  {Entity.Name} — {Entity.Kind}";
        [ObservableProperty] private Bitmap? _image;
    }

    private const int PageSize = 96;
    private const int MaxEntityHits = 60;
    private string[] _allFiles = Array.Empty<string>();
    private CancellationTokenSource? _decode;
    private CancellationTokenSource? _entityLoad;
    private readonly EntityIconIndex _entities;

    public IconsViewModel(StudioContext ctx) : base(ctx)
    {
        _entities = new EntityIconIndex(ctx);
    }

    public override string Title => "Icon finder";
    public override string Subtitle =>
        "Find an icon by item/mob name — or see which entities " +
        "use an icon file";

    /// <summary>Injecté par la vue — copie presse-papier.</summary>
    public Func<string, Task>? CopyText { get; set; }

    public ObservableCollection<string> Sets { get; } = new();
    public ObservableCollection<IconTile> Tiles { get; } = new();
    public ObservableCollection<EntityHit> EntityResults { get; } =
        new();
    public ObservableCollection<EntityHit> UsedByList { get; } = new();

    [ObservableProperty] private string _selectedSet = "itemicon";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _entityFilter = "";
    [ObservableProperty] private int _page;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string _pageLabel = "page 1/1";
    [ObservableProperty] private int _matchCount;
    [ObservableProperty] private IconTile? _selectedTile;
    [ObservableProperty] private EntityHit? _selectedEntity;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isLoading;

    // --- Fiche détail (tuile OU entité) ---
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private Bitmap? _detailImage;
    [ObservableProperty] private string _detailTitle = "";
    [ObservableProperty] private string _detailCode = "";
    [ObservableProperty] private string _detailMeta = "";
    [ObservableProperty] private string _usedByLabel = "";
    [ObservableProperty] private bool _canLocate;

    protected override void OnFirstActivate()
    {
        foreach (var d in IconSetDirs()) Sets.Add(d);
        if (Sets.Count > 0 && !Sets.Contains(SelectedSet))
            SelectedSet = Sets[0];
        LoadSet();
        _ = EnsureIndexAsync();
    }

    /// <summary>Construit l'index entité↔icône en fond (S_Item +
    /// S_Monster + libellés EN) — nécessaire à la recherche par nom
    /// et au « used by ».</summary>
    private async Task EnsureIndexAsync()
    {
        var idx = _entities;
        await Task.Run(() => idx.EnsureLoaded());
        Status($"Entity index ready — {idx.Count} items/monsters " +
               "searchable by name");
    }

    /// <summary>Sous-dossiers de UI/ existants sur disque.</summary>
    private string[] IconSetDirs() =>
        !Ctx.UiDirExists ? Array.Empty<string>()
        : Directory.GetDirectories(Ctx.UiDir)
            .Select(Path.GetFileName).Where(d => d != null)
            .Select(d => d!).OrderBy(d => d).ToArray();

    partial void OnSelectedSetChanged(string value)
    {
        if (Activated) LoadSet();
    }

    private void LoadSet()
    {
        _decode?.Cancel();
        Tiles.Clear();
        SelectedTile = null;
        Page = 0;
        var dir = Path.Combine(Ctx.UiDir, SelectedSet);
        if (!Directory.Exists(dir))
        {
            _allFiles = Array.Empty<string>();
            IsEmpty = true;
            EmptyMessage =
                $"Folder UI/{SelectedSet} not found — extract the " +
                "client pkg or check Reforged path.";
            Status(EmptyMessage);
            return;
        }
        _allFiles = Directory.GetFiles(dir)
            .Where(f => f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToArray();
        ShowPage();
    }

    [RelayCommand]
    private void Search()
    { Page = 0; ShowPage(); }

    [RelayCommand]
    private void Refresh() => LoadSet();

    [RelayCommand]
    private void NextPage()
    { if (Page < PageCount - 1) { Page++; ShowPage(); } }

    [RelayCommand]
    private void PrevPage()
    { if (Page > 0) { Page--; ShowPage(); } }

    /// <summary>Recherche « trouve-moi l'icône de X » — items et
    /// monstres par nom EN/Big5, id ou code exact.</summary>
    [RelayCommand]
    private void SearchEntities()
    {
        EntityResults.Clear();
        if (string.IsNullOrWhiteSpace(EntityFilter)) return;
        if (!_entities.Loaded)
        {
            Status("Entity index still loading — retry in a second");
            return;
        }
        var hits = _entities.Search(EntityFilter, MaxEntityHits);
        foreach (var e in hits) EntityResults.Add(new EntityHit(e));
        Status(EntityResults.Count == 0
            ? $"No item or monster named \"{EntityFilter}\""
            : $"{EntityResults.Count} entities match " +
              $"\"{EntityFilter}\"");
        DecodeEntityIcons();
    }

    private void DecodeEntityIcons()
    {
        _entityLoad?.Cancel();
        var cts = _entityLoad = new CancellationTokenSource();
        var hits = EntityResults.ToArray();
        var icons = Ctx.Icons;
        Task.Run(() =>
        {
            foreach (var h in hits)
            {
                if (cts.IsCancellationRequested) return;
                var bmp = icons.Get(h.IconCode);
                if (bmp != null) h.Image = bmp;
            }
        }, cts.Token);
    }

    /// <summary>Clic sur une entité → fiche avec son icône, le
    /// fichier réel, et la possibilité de la localiser dans la
    /// grille ou de copier le code.</summary>
    partial void OnSelectedEntityChanged(EntityHit? value)
    {
        if (value == null) return;
        UsedByList.Clear();
        UsedByLabel = "";
        DetailTitle = $"{value.Name}  ({value.Entity.Kind} #{value.Id})";
        DetailCode = value.IconCode;
        DetailImage = value.Image ?? Ctx.Icons.Get(value.IconCode);
        DetailMeta = ResolveMeta(value.IconCode);
        CanLocate = Ctx.Icons.TryResolve(
            value.IconCode, out _, out _);
        HasDetail = true;
    }

    /// <summary>Clic sur une tuile → fiche + les entités qui
    /// utilisent ce fichier icône.</summary>
    partial void OnSelectedTileChanged(IconTile? value)
    {
        UsedByList.Clear();
        if (value == null)
        { if (SelectedEntity == null) HasDetail = false; return; }
        var baseName = Path.GetFileNameWithoutExtension(value.Name);
        DetailTitle = value.Name;
        DetailCode = baseName;
        DetailImage = value.Image;
        DetailMeta = $"{SelectedSet}/{value.Name}";
        CanLocate = false;
        HasDetail = true;
        if (DetailImage == null)
            _ = LoadTilePreviewAsync(value);
        if (!_entities.Loaded)
        {
            UsedByLabel =
                "Entity index still loading — used-by unavailable.";
            return;
        }
        var used = _entities.UsedBy(baseName, 20, out var total);
        foreach (var e in used) UsedByList.Add(new EntityHit(e));
        UsedByLabel = total == 0
            ? "No item or monster uses this icon."
            : $"Used by {total} entit{(total > 1 ? "ies" : "y")}" +
              (total > used.Count ? $" (first {used.Count})" : "") +
              ":";
        DecodeUsedByIcons();
    }

    /// <summary>Décode l'aperçu détail si la tuile n'est pas encore
    /// décodée (la grille décode page par page).</summary>
    private async Task LoadTilePreviewAsync(IconTile tile)
    {
        var bmp = await Task.Run(() =>
        {
            try
            { return ImageCodec.Decode(File.ReadAllBytes(tile.Path)); }
            catch { return null; }
        });
        if (bmp != null && SelectedTile == tile) DetailImage = bmp;
    }

    private void DecodeUsedByIcons()
    {
        var hits = UsedByList.ToArray();
        var icons = Ctx.Icons;
        Task.Run(() =>
        {
            foreach (var h in hits)
            {
                var bmp = icons.Get(h.IconCode);
                if (bmp != null) h.Image = bmp;
            }
        });
    }

    /// <summary>Métadonnées du fichier icône réel derrière un code —
    /// dossier + nom trouvés (variante digit-strip) ou pkg.</summary>
    private string ResolveMeta(string code)
    {
        if (Ctx.Icons.TryResolve(code, out var dir, out var file))
            return $"icon {dir}/{file}";
        return Ctx.Icons.TryLocate(code, out var f)
            ? $"icon inside a pkg ({f}) — not extracted as a file"
            : $"no file found for code {code}";
    }

    /// <summary>Saute la grille sur le fichier icône de l'entité
    /// sélectionnée (dossier + filtre sur le nom résolu).</summary>
    [RelayCommand]
    private void LocateIcon()
    {
        var code = DetailCode;
        if (!Ctx.Icons.TryResolve(code, out var dir, out var file))
        {
            Status($"No loose icon file for {code} — it lives " +
                   "inside a pkg archive");
            return;
        }
        SelectedSet = dir;
        Filter = Path.GetFileNameWithoutExtension(file);
        Page = 0;
        ShowPage();
        SelectedTile = Tiles.FirstOrDefault(
            t => t.Name.Equals(file,
                StringComparison.OrdinalIgnoreCase));
        Status($"{code} → {dir}/{file}");
    }

    /// <summary>Copie le code icône courant (entité ou tuile).</summary>
    [RelayCommand]
    private async Task CopyCode()
    {
        if (DetailCode.Length == 0)
        { Status("Nothing selected to copy"); return; }
        if (CopyText != null) await CopyText(DetailCode);
        Status($"Icon code {DetailCode} copied");
    }

    private void ShowPage()
    {
        _decode?.Cancel();
        Tiles.Clear();
        SelectedTile = null;
        var match = _allFiles
            .Where(f => string.IsNullOrWhiteSpace(Filter)
                || Path.GetFileName(f).Contains(Filter,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        MatchCount = match.Length;
        PageCount = Math.Max(1, (match.Length + PageSize - 1) / PageSize);
        if (Page >= PageCount) Page = PageCount - 1;
        PageLabel = $"page {Page + 1}/{PageCount}";
        IsEmpty = match.Length == 0;
        EmptyMessage = match.Length == 0
            ? $"No icons match \"{Filter}\" in {SelectedSet} — " +
              "clear the filter, or search an item/monster name " +
              "in the left panel."
            : "";
        foreach (var f in match.Skip(Page * PageSize).Take(PageSize))
            Tiles.Add(new IconTile
            { Name = Path.GetFileName(f), Path = f });
        Status($"{match.Length} icons in {SelectedSet} — " +
               $"page {Page + 1}/{PageCount}");
        DecodePage();
    }

    /// <summary>Décode les bitmaps de la page courante en tâche de
    /// fond — la grille se remplit au fil de l'eau.</summary>
    private void DecodePage()
    {
        var cts = _decode = new CancellationTokenSource();
        var tiles = Tiles.ToArray();
        Task.Run(() =>
        {
            foreach (var t in tiles)
            {
                if (cts.IsCancellationRequested) return;
                try
                {
                    var bmp = ImageCodec.Decode(
                        File.ReadAllBytes(t.Path));
                    if (bmp != null) t.Image = bmp;
                }
                catch { /* format non décodable — tuile reste vide */ }
            }
        }, cts.Token);
    }
}
