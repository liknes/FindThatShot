using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VideoArchiveManager.App.Views;
using VideoArchiveManager.Core.Configuration;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services;
using VideoArchiveManager.Data;

namespace VideoArchiveManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;
    private readonly ISearchService _searchService;
    private readonly ITagService _tagService;
    private readonly IVideoScannerService _scannerService;
    private readonly IVideoLibraryService _libraryService;
    private readonly IFfprobeService _ffprobeService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IVideoLocationService _locationService;
    private readonly ISettingsStore _settingsStore;
    private readonly IUpdateService _updateService;
    private readonly IServiceProvider _services;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _geocodeCts;
    private DispatcherTimer? _scanHeartbeatTimer;
    private DateTime _scanStartedAt;
    private DateTime _currentFileStartedAt;
    private string? _currentFilePath;

    public MainViewModel(
        IDbContextFactory<VideoArchiveDbContext> contextFactory,
        ISearchService searchService,
        ITagService tagService,
        IVideoScannerService scannerService,
        IVideoLibraryService libraryService,
        IFfprobeService ffprobeService,
        IThumbnailService thumbnailService,
        IVideoLocationService locationService,
        ISettingsStore settingsStore,
        IUpdateService updateService,
        IServiceProvider services,
        VideoDetailViewModel detail)
    {
        _contextFactory = contextFactory;
        _searchService = searchService;
        _tagService = tagService;
        _scannerService = scannerService;
        _libraryService = libraryService;
        _ffprobeService = ffprobeService;
        _thumbnailService = thumbnailService;
        _locationService = locationService;
        _settingsStore = settingsStore;
        _updateService = updateService;
        _services = services;
        Detail = detail;

        // Mirror VideoDetailViewModel's LastSaveStatus into the main status
        // bar so users get immediate confirmation of what just happened
        // (saved · sidecar written / disabled / failed). This is the
        // primary visibility fix for "I clicked Save and nothing seemed
        // to happen with the sidecar".
        Detail.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(VideoDetailViewModel.LastSaveStatus)
                && !string.IsNullOrEmpty(Detail.LastSaveStatus))
            {
                StatusMessage = Detail.LastSaveStatus!;
            }
        };

        // Any change to the user's chip selection re-runs the catalog
        // search AND refreshes the picker list (so just-added tags no
        // longer appear in the list, just-removed tags reappear).
        SelectedTagFilters.CollectionChanged += (_, _) =>
        {
            RebuildFilteredTags();
            ClearTagFiltersCommand.NotifyCanExecuteChanged();
            OnFilterChanged();
        };

        // Keep the sidebar tag picker in sync when the editor (or any other
        // path) attaches a tag. Previously a brand-new tag would only show
        // up after an app restart or a re-scan because AllTags was only
        // populated by ReloadFiltersAsync.
        Detail.TagCatalogChanged += Detail_TagCatalogChanged;
    }

    // Live indicator for the bottom status bar; reflects the current
    // WriteSidecarFiles setting. Re-evaluated after the Settings dialog
    // closes (see OpenSettings).
    public string SidecarStatusText =>
        _settingsStore.Current.WriteSidecarFiles ? "Sidecars: ON" : "Sidecars: OFF";

    public ObservableCollection<RootFolder> RootFolders { get; } = new();

    // Hierarchical folder view shown in the sidebar (Lightroom-style).
    // Top-level nodes are synthetic drive roots ("E:\", "C:\"); registered
    // root folders sit directly beneath their drive; every distinct
    // VideoItem.FolderPath under a registered root is mirrored into the
    // tree with counts rolled up. Rebuilt by RebuildFolderTreeAsync,
    // which is called from ReloadFiltersAsync so every existing
    // refresh path (initial load, post-scan, remove-folder, bulk-edit,
    // remove-offline) keeps the tree in sync without new wiring.
    public ObservableCollection<FolderNode> FolderTree { get; } = new();

    public ObservableCollection<Tag> AllTags { get; } = new();
    public ObservableCollection<string> Cameras { get; } = new();

    // Tag filters the user has picked. Search applies them as AND
    // (a video must carry every selected tag). Backed by the Tag picker
    // list + chip buttons in the sidebar.
    public ObservableCollection<Tag> SelectedTagFilters { get; } = new();

    // Subset of AllTags filtered by TagFilterSearchText and excluding any
    // tag that is already a chip. Rebuilt by RebuildFilteredTags(). The
    // sidebar list binds to this so typing narrows what's shown.
    public ObservableCollection<Tag> FilteredTags { get; } = new();
    public ObservableCollection<VideoStatus> AvailableStatuses { get; } = new(Enum.GetValues<VideoStatus>());
    public ObservableCollection<VideoItemViewModel> Videos { get; } = new();
    public ObservableCollection<VideoItemViewModel> SelectedVideos { get; } = new();

    public VideoDetailViewModel Detail { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private VideoStatus? _statusFilter;

    [ObservableProperty]
    private int _minRatingFilter;

    [ObservableProperty]
    private string? _cameraFilter;

    // Live filter for the tag picker list. Doesn't itself filter the
    // catalog — only narrows which tags the user sees while choosing.
    [ObservableProperty]
    private string _tagFilterSearchText = string.Empty;

    [ObservableProperty]
    private RootFolder? _selectedRootFolder;

    // The user's current pick in the folder tree. Drives the catalog
    // filter via SearchQuery.RootFolderPath (a path-prefix match in
    // SearchService). Mirrored into SelectedRootFolder when the node is
    // a registered root so the existing RemoveRootFolderCommand
    // CanExecute / CommandParameter wiring keeps working unchanged.
    [ObservableProperty]
    private FolderNode? _selectedFolderNode;

    // Gates the tree's "Remove folder…" context-menu item: only nodes
    // that map to a RootFolder entity can be removed (drives and
    // intermediate folders are derived state, not catalog records).
    // Notified whenever SelectedFolderNode changes.
    public bool CanRemoveSelectedFolder => SelectedFolderNode?.IsRegisteredRoot == true;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private bool _showOnlyAvailable = true;

    // Backs the sidebar "Show only unreviewed" checkbox AND the toolbar
    // "Start review session" button. Treated as a union filter server-side
    // (Status == Unreviewed OR no tags) — see SearchService.
    [ObservableProperty]
    private bool _onlyUnreviewed;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 100;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanElapsedText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private VideoItemViewModel? _selectedVideo;

    partial void OnSelectedVideoChanged(VideoItemViewModel? value)
    {
        _ = Detail.LoadAsync(value);
    }

    // When true, filter setters that would normally trigger a re-search stay
    // quiet. Used by ClearFiltersAsync so 7+ property updates result in one
    // search rather than 7 overlapping ones.
    private bool _suppressFilterSearch;

    private void OnFilterChanged()
    {
        if (_suppressFilterSearch) return;
        _ = SearchAsync();
    }

    // Auto-search whenever any filter changes via UI (combo boxes, date
    // pickers, sidebar lists, checkboxes). Without these, the filter values
    // only get applied when the user explicitly clicks Search.
    partial void OnStatusFilterChanged(VideoStatus? value) => OnFilterChanged();
    partial void OnMinRatingFilterChanged(int value) => OnFilterChanged();
    partial void OnCameraFilterChanged(string? value)
    {
        ClearCameraFilterCommand.NotifyCanExecuteChanged();
        OnFilterChanged();
    }
    partial void OnTagFilterSearchTextChanged(string value) => RebuildFilteredTags();
    // SelectedRootFolder is now derived from SelectedFolderNode (set
    // only when the picked tree node is a registered root). The search
    // is triggered by the SelectedFolderNode change handler below; we
    // intentionally don't fire it from here to avoid two searches per
    // selection click.
    partial void OnSelectedFolderNodeChanged(FolderNode? value)
    {
        SelectedRootFolder = value?.RootFolder;
        OnPropertyChanged(nameof(CanRemoveSelectedFolder));
        OnFilterChanged();
    }
    partial void OnDateFromChanged(DateTime? value) => OnFilterChanged();
    partial void OnDateToChanged(DateTime? value) => OnFilterChanged();
    partial void OnShowOnlyAvailableChanged(bool value) => OnFilterChanged();
    partial void OnOnlyUnreviewedChanged(bool value) => OnFilterChanged();

    public async Task InitializeAsync()
    {
        if (!_ffprobeService.IsAvailable() || !_thumbnailService.IsAvailable())
        {
            StatusMessage = "FFmpeg/FFprobe not found. Configure paths in Settings.";
        }

        await ReloadFiltersAsync();
        await SearchAsync();

        // Best-effort startup update check. Fire-and-forget so it never
        // blocks the catalog load, delayed a few seconds so it doesn't
        // race the initial search/thumbnail work for network bandwidth on
        // slow connections. Failures are swallowed silently — the only
        // user-visible effect is the status-bar update pill appearing
        // when a newer release is published on GitHub.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await CheckForUpdatesSilentlyAsync();
        });
    }

    private async Task ReloadFiltersAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var roots = await ctx.RootFolders.OrderBy(r => r.Path).ToListAsync();
        RootFolders.Clear();
        foreach (var r in roots) RootFolders.Add(r);

        // Rebuild the sidebar folder tree from the same context so the
        // registered-root snapshot and the distinct-folder-paths query
        // see a consistent view of the catalog.
        await RebuildFolderTreeAsync(ctx);

        var tags = await _tagService.GetAllAsync();
        AllTags.Clear();
        foreach (var t in tags) AllTags.Add(t);

        // Drop chips whose tag no longer exists (e.g. cleaned up via bulk
        // edit or admin tools) and refresh the picker against the new set.
        var validIds = AllTags.Select(t => t.Id).ToHashSet();
        for (var i = SelectedTagFilters.Count - 1; i >= 0; i--)
        {
            if (!validIds.Contains(SelectedTagFilters[i].Id))
            {
                SelectedTagFilters.RemoveAt(i);
            }
        }
        RebuildFilteredTags();

        var cams = await _searchService.GetDistinctCamerasAsync();
        Cameras.Clear();
        foreach (var c in cams) Cameras.Add(c);
    }

    // Rebuilds the sidebar folder tree from the catalog.
    //
    // Shape (Lightroom-style):
    //   * Top-level nodes are synthetic drives — one per distinct
    //     drive that hosts a registered root or has video data. We
    //     try to read the volume label so the rail shows
    //     "MediaExtension I (E:)" instead of a bare "E:\".
    //   * Direct children of a drive are the registered RootFolder
    //     entries that live on it.
    //   * Beneath each registered root we materialise the real
    //     subfolder structure from the distinct VideoItem.FolderPath
    //     values that fall under it. Empty intermediate folders
    //     (no own count, but ancestors of leaves) are kept so the
    //     hierarchy is navigable.
    //   * Any video folder that doesn't fall under a registered root
    //     (e.g. orphaned after the user removed the root but kept the
    //     records — currently impossible since RemoveRootFolderAsync
    //     also clears the rows, but cheap defensive coverage) gets
    //     attached straight under its drive.
    //
    // Counts are rolled up post-build so each node displays the
    // recursive total (its own clips + every descendant's). Expansion
    // state and the current selection are snapshotted before the
    // rebuild and restored against the new tree by FullPath so a
    // post-scan refresh doesn't collapse what the user just opened.
    private async Task RebuildFolderTreeAsync(VideoArchiveDbContext ctx)
    {
        // 1. Snapshot UI state that survives the rebuild.
        var expandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpandedPaths(FolderTree, expandedPaths);
        var previousSelection = SelectedFolderNode?.FullPath;

        // 2. Pull the data we need in two cheap queries.
        var folderCounts = await ctx.VideoItems
            .Where(v => v.FolderPath != null && v.FolderPath != string.Empty)
            .GroupBy(v => v.FolderPath)
            .Select(g => new { Path = g.Key, Count = g.Count() })
            .ToListAsync();

        var registeredRoots = RootFolders.ToList();

        // 3. Build the tree in-memory keyed by canonical path so we
        //    can dedupe shared ancestors across siblings.
        var nodeByPath = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        var driveNodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        var registeredByPath = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);

        // First-build default: auto-expand drives so registered roots are
        // visible without an extra click. Subsequent rebuilds honour
        // whatever the user had open.
        var isFirstBuild = expandedPaths.Count == 0;

        FolderNode EnsureDriveNode(string drivePath)
        {
            var normalized = drivePath; // GetPathRoot already includes the trailing separator on Windows.
            if (driveNodes.TryGetValue(normalized, out var existing)) return existing;
            var node = new FolderNode
            {
                Name = FormatDriveLabel(normalized),
                FullPath = normalized,
                IsDriveRoot = true,
                IsExpanded = isFirstBuild || expandedPaths.Contains(normalized)
            };
            driveNodes[normalized] = node;
            nodeByPath[normalized] = node;
            return node;
        }

        // Register each root folder under its drive first so subsequent
        // video paths can attach to the right anchor (longest matching
        // prefix wins below).
        foreach (var rf in registeredRoots.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = TrimTrailingSeparator(rf.Path);
            var drive = Path.GetPathRoot(canonical);
            if (string.IsNullOrEmpty(drive)) continue;

            var driveNode = EnsureDriveNode(drive);

            // If the registered root IS the drive itself (e.g. user added "E:\"),
            // promote the drive node to a registered root rather than nesting it.
            if (string.Equals(canonical, TrimTrailingSeparator(drive), StringComparison.OrdinalIgnoreCase))
            {
                driveNode.IsRegisteredRoot = true;
                driveNode.RootFolder = rf;
                registeredByPath[TrimTrailingSeparator(drive)] = driveNode;
                continue;
            }

            if (nodeByPath.ContainsKey(canonical)) continue;

            var rootNode = new FolderNode
            {
                Name = !string.IsNullOrWhiteSpace(rf.Name)
                    ? rf.Name!
                    : Path.GetFileName(canonical),
                FullPath = canonical,
                IsRegisteredRoot = true,
                RootFolder = rf,
                IsExpanded = expandedPaths.Contains(canonical)
            };
            driveNode.Children.Add(rootNode);
            nodeByPath[canonical] = rootNode;
            registeredByPath[canonical] = rootNode;
        }

        // Precompute root prefixes ordered longest-first so a video under
        // "E:\Footage\Personal" attaches to that root rather than "E:\Footage".
        var rootPrefixes = registeredByPath
            .Select(kv => new { Canonical = kv.Key, Prefix = EnsureTrailingSeparator(kv.Key) })
            .OrderByDescending(r => r.Prefix.Length)
            .ToList();

        // Step 2: materialise every distinct video folder, walking
        // segment-by-segment from its anchor (root node or drive node)
        // and creating intermediate nodes lazily.
        foreach (var folder in folderCounts)
        {
            if (string.IsNullOrEmpty(folder.Path)) continue;
            var canonicalFolder = TrimTrailingSeparator(folder.Path);

            FolderNode anchor;
            string anchorPath;

            // Try to attach to the longest registered root prefix.
            var match = rootPrefixes.FirstOrDefault(r =>
                canonicalFolder.Equals(r.Canonical, StringComparison.OrdinalIgnoreCase) ||
                canonicalFolder.StartsWith(r.Prefix, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                anchor = registeredByPath[match.Canonical];
                anchorPath = match.Canonical;
            }
            else
            {
                // No registered root contains this folder — attach under its drive.
                var drive = Path.GetPathRoot(canonicalFolder);
                if (string.IsNullOrEmpty(drive)) continue;
                anchor = EnsureDriveNode(drive);
                anchorPath = TrimTrailingSeparator(drive);
            }

            // Folder == anchor: just credit its videos to the anchor node.
            if (canonicalFolder.Equals(anchorPath, StringComparison.OrdinalIgnoreCase))
            {
                anchor.OwnCount += folder.Count;
                continue;
            }

            // Compute the relative path below the anchor and walk segments.
            var relative = canonicalFolder.Substring(anchorPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            var current = anchor;
            var currentPath = anchorPath;
            foreach (var segment in segments)
            {
                currentPath = string.IsNullOrEmpty(currentPath)
                    ? segment
                    : currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar + segment;
                if (!nodeByPath.TryGetValue(currentPath, out var child))
                {
                    child = new FolderNode
                    {
                        Name = segment,
                        FullPath = currentPath,
                        IsExpanded = expandedPaths.Contains(currentPath)
                    };
                    current.Children.Add(child);
                    nodeByPath[currentPath] = child;
                }
                current = child;
            }
            current.OwnCount += folder.Count;
        }

        // 4. Roll up counts (post-order) so parents show recursive totals.
        foreach (var drive in driveNodes.Values)
        {
            RollUpCounts(drive);
        }

        // 5. Alphabetise children at every level. OrdinalIgnoreCase keeps
        //    things deterministic regardless of locale.
        foreach (var node in nodeByPath.Values)
        {
            SortChildren(node);
        }

        // 6. Replace the live collection. Done as a single Clear/Add pass
        //    so the TreeView only re-binds once.
        FolderTree.Clear();
        foreach (var drive in driveNodes.Values.OrderBy(d => d.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            FolderTree.Add(drive);
        }

        // 7. Restore selection by path if the previous node still exists.
        if (!string.IsNullOrEmpty(previousSelection)
            && nodeByPath.TryGetValue(previousSelection, out var restored))
        {
            // Expand the chain up to the selected node so the TreeView
            // realises the container — otherwise IsSelected wouldn't
            // visibly highlight a node inside a collapsed branch.
            ExpandAncestors(restored, nodeByPath);
            SelectedFolderNode = restored;
            restored.IsSelected = true;
        }
        else if (previousSelection is not null)
        {
            // Previously selected folder vanished (e.g. its only video was
            // removed). Clear the filter so the catalog isn't trapped on
            // an empty result set.
            SelectedFolderNode = null;
        }
    }

    private static int RollUpCounts(FolderNode node)
    {
        var total = node.OwnCount;
        foreach (var c in node.Children)
        {
            total += RollUpCounts(c);
        }
        node.VideoCount = total;
        return total;
    }

    private static void SortChildren(FolderNode node)
    {
        if (node.Children.Count <= 1) return;
        var sorted = node.Children
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        node.Children.Clear();
        foreach (var c in sorted) node.Children.Add(c);
    }

    private static void CollectExpandedPaths(IEnumerable<FolderNode> nodes, HashSet<string> sink)
    {
        foreach (var n in nodes)
        {
            if (n.IsExpanded) sink.Add(n.FullPath);
            CollectExpandedPaths(n.Children, sink);
        }
    }

    private static void ExpandAncestors(FolderNode node, IReadOnlyDictionary<string, FolderNode> nodeByPath)
    {
        var path = node.FullPath;
        while (true)
        {
            var parentPath = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parentPath) || string.Equals(parentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            if (nodeByPath.TryGetValue(parentPath, out var parent))
            {
                parent.IsExpanded = true;
                path = parent.FullPath;
            }
            else
            {
                path = parentPath;
            }
        }
    }

    private static string TrimTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var root = Path.GetPathRoot(path);
        // Don't trim the separator off a path that IS a drive root
        // ("E:\") — the trailing separator is part of its canonical
        // form and Path.GetPathRoot("E:") returns "E:" (no slash),
        // which is a different beast we don't want to manufacture.
        if (!string.IsNullOrEmpty(root)
            && path.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path[path.Length - 1] == Path.DirectorySeparatorChar
            || path[path.Length - 1] == Path.AltDirectorySeparatorChar)
        {
            return path;
        }
        return path + Path.DirectorySeparatorChar;
    }

    private static string? NormalizeFolderPrefix(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return null;
        return EnsureTrailingSeparator(fullPath);
    }

    private static string FormatDriveLabel(string drivePath)
    {
        // Mirror Windows Explorer's drive label format ("Windows (C:)",
        // "MediaExtension I (E:)", "Local Disk (D:)"):
        //   1. Use the volume label when set.
        //   2. Fall back to a DriveType-based default ("Local Disk",
        //      "Removable Disk", "CD/DVD Drive", "Network Drive", "RAM Disk")
        //      so drives without a custom label still get a friendly name.
        //   3. Only fall back to the bare path for things DriveInfo can't
        //      reason about at all (UNC shares it can't resolve, etc.).
        // Drive letter is always shown in parens for consistency, regardless
        // of which branch produced the prefix.
        var trimmed = drivePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string? label = null;
        DriveType driveType = DriveType.Unknown;
        try
        {
            // DriveInfo only works for fixed / removable / network-mapped
            // drives — UNC paths (\\server\share) make the ctor throw.
            // Catch generously so unresolvable shares don't break the tree.
            var info = new DriveInfo(drivePath);
            driveType = info.DriveType;
            if (info.IsReady)
            {
                label = info.VolumeLabel;
            }
        }
        catch
        {
            // Offline / unmapped / network share without a drive letter —
            // fall through to the bare drive path at the bottom.
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            return $"{label} ({trimmed})";
        }

        var defaultName = driveType switch
        {
            DriveType.Fixed => "Local Disk",
            DriveType.Removable => "Removable Disk",
            DriveType.CDRom => "CD/DVD Drive",
            DriveType.Network => "Network Drive",
            DriveType.Ram => "RAM Disk",
            _ => null
        };

        return defaultName is not null
            ? $"{defaultName} ({trimmed})"
            : drivePath;
    }

    // Called when the editor attaches a tag. Inserts the tag into AllTags
    // at the position it would occupy if reloaded from scratch (same
    // Type → Name ordering as TagService.GetAllAsync), so the picker
    // stays alphabetised. Deduplicates by Id so adding an existing tag
    // is a silent no-op for the sidebar.
    private void Detail_TagCatalogChanged(object? sender, Tag tag)
    {
        if (AllTags.Any(t => t.Id == tag.Id))
        {
            return;
        }

        var insertIndex = 0;
        while (insertIndex < AllTags.Count)
        {
            var existing = AllTags[insertIndex];
            var typeCmp = ((int)tag.Type).CompareTo((int)existing.Type);
            if (typeCmp < 0) break;
            if (typeCmp == 0 &&
                string.Compare(tag.Name, existing.Name, StringComparison.OrdinalIgnoreCase) < 0)
            {
                break;
            }
            insertIndex++;
        }
        AllTags.Insert(insertIndex, tag);
        RebuildFilteredTags();
    }

    // Repopulates FilteredTags from AllTags, excluding anything the user
    // already picked as a chip and applying the live text filter.
    // Case-insensitive substring match keeps "birds" / "Birds" equivalent.
    private void RebuildFilteredTags()
    {
        var search = TagFilterSearchText?.Trim() ?? string.Empty;
        var selectedIds = SelectedTagFilters.Select(t => t.Id).ToHashSet();

        FilteredTags.Clear();
        foreach (var t in AllTags)
        {
            if (selectedIds.Contains(t.Id)) continue;
            if (search.Length > 0
                && t.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            FilteredTags.Add(t);
        }
    }

    [RelayCommand]
    private void AddTagFilter(Tag? tag)
    {
        if (tag is null) return;
        if (SelectedTagFilters.Any(t => t.Id == tag.Id)) return;
        SelectedTagFilters.Add(tag);
    }

    [RelayCommand]
    private void RemoveTagFilter(Tag? tag)
    {
        if (tag is null) return;
        var existing = SelectedTagFilters.FirstOrDefault(t => t.Id == tag.Id);
        if (existing is not null)
        {
            SelectedTagFilters.Remove(existing);
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearTagFilters))]
    private void ClearTagFilters() => SelectedTagFilters.Clear();

    private bool CanClearTagFilters() => SelectedTagFilters.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearCameraFilter))]
    private void ClearCameraFilter() => CameraFilter = null;

    private bool CanClearCameraFilter() => !string.IsNullOrEmpty(CameraFilter);

    // Tracks the currently-running search so that fast successive filter
    // changes don't race — a newer call cancels any older one before its
    // results can clobber the grid.
    private CancellationTokenSource? _searchCts;

    [RelayCommand]
    private async Task SearchAsync()
    {
        // Supersede any in-flight search. Cancelling makes the EF query in
        // SearchService throw OperationCanceledException, which we catch
        // below. Disposing the old CTS here is safe because it's idempotent
        // and the awaiting code never touches the CTS itself again.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        var query = new SearchQuery
        {
            Text = SearchText,
            Status = StatusFilter,
            MinRating = MinRatingFilter > 0 ? MinRatingFilter : null,
            Camera = string.IsNullOrWhiteSpace(CameraFilter) ? null : CameraFilter,
            TagIds = SelectedTagFilters.Count == 0
                ? null
                : SelectedTagFilters.Select(t => t.Id).ToArray(),
            DateFrom = DateFrom,
            DateTo = DateTo,
            // The folder tree feeds the path prefix now. EnsureTrailingSeparator
            // turns "E:\20070101 - Brasil" into "E:\20070101 - Brasil\" so
            // SearchService's StartsWith match doesn't accidentally pick up
            // sibling folders like "E:\20070101 - Brasilia" — a latent bug
            // when the old SelectedRootFolder path was passed in raw.
            RootFolderPath = NormalizeFolderPrefix(SelectedFolderNode?.FullPath),
            FileExists = ShowOnlyAvailable ? true : null,
            OnlyUnreviewed = OnlyUnreviewed ? true : null,
            Take = 500
        };

        try
        {
            var result = await _searchService.SearchAsync(query, token);

            // Belt-and-suspenders: if we were superseded after the await
            // resumed but before we got here, bail out so we don't overwrite
            // a fresher result.
            if (token.IsCancellationRequested) return;

            Videos.Clear();
            foreach (var v in result.Items)
            {
                Videos.Add(new VideoItemViewModel(v));
            }
            TotalCount = result.TotalCount;
            StatusMessage = $"Showing {Videos.Count} of {TotalCount} videos";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query — drop the stale result silently.
        }
        finally
        {
            // Only dispose if we're still the active CTS. If a newer call has
            // already replaced (and disposed) us, it's not ours to clean up.
            if (ReferenceEquals(_searchCts, cts))
            {
                cts.Dispose();
                _searchCts = null;
            }
        }
    }

    [RelayCommand]
    private async Task AddRootFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a root folder to scan"
        };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        if (await ctx.RootFolders.AnyAsync(r => r.Path == path))
        {
            StatusMessage = "Root folder already exists";
            return;
        }

        var rf = new RootFolder
        {
            Path = path,
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        };
        ctx.RootFolders.Add(rf);
        await ctx.SaveChangesAsync();
        RootFolders.Add(rf);
        StatusMessage = $"Added root folder: {rf.Path}";
    }

    [RelayCommand]
    private async Task RemoveRootFolderAsync(RootFolder? folder)
    {
        if (folder is null) return;

        var videoCount = await _libraryService.CountUnderRootAsync(folder.Path);

        var message = videoCount > 0
            ? $"Remove the root folder \"{folder.Path}\" and {videoCount} video record(s) imported from it?\n\n" +
              "This affects the catalog database and the app's thumbnail cache only. " +
              "The source video files on disk will NOT be touched."
            : $"Remove the root folder \"{folder.Path}\"?\n\n" +
              "No video records were imported from this folder. The folder on disk will NOT be touched.";

        var result = MessageBox.Show(
            message,
            "Remove root folder",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;

        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            var entity = await ctx.RootFolders.FirstOrDefaultAsync(r => r.Id == folder.Id);
            if (entity is null)
            {
                RootFolders.Remove(folder);
                return;
            }
            ctx.RootFolders.Remove(entity);
            await ctx.SaveChangesAsync();
        }

        var removedVideos = videoCount > 0
            ? await _libraryService.RemoveUnderRootAsync(folder.Path)
            : 0;

        RootFolders.Remove(folder);
        if (SelectedRootFolder?.Id == folder.Id) SelectedRootFolder = null;

        StatusMessage = removedVideos > 0
            ? $"Removed root folder and {removedVideos} video record(s) from the catalog. Source video files untouched."
            : "Removed root folder. Source video files untouched.";

        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private async Task RemoveSelectedVideosAsync()
    {
        var ids = SelectedVideos.Select(v => v.Id).ToList();
        if (ids.Count == 0)
        {
            StatusMessage = "Select one or more videos to remove";
            return;
        }

        var preview = string.Join(
            Environment.NewLine,
            SelectedVideos.Take(5).Select(v => "  • " + v.FileName));
        if (SelectedVideos.Count > 5)
        {
            preview += Environment.NewLine + $"  …and {SelectedVideos.Count - 5} more";
        }

        var message =
            $"Remove {ids.Count} video record(s) from the database?\n\n" +
            preview + "\n\n" +
            "This removes the catalog entries (tags, ratings, notes, etc.) and their " +
            "cached thumbnails. The source video files on disk will NOT be touched.";

        var result = MessageBox.Show(
            message,
            "Remove videos from database",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;

        var removed = await _libraryService.RemoveByIdsAsync(ids);

        if (SelectedVideo is not null && ids.Contains(SelectedVideo.Id))
        {
            SelectedVideo = null;
        }
        SelectedVideos.Clear();

        StatusMessage = $"Removed {removed} video record(s) from the catalog. Source video files untouched.";
        await SearchAsync();
    }

    [RelayCommand]
    private async Task RemoveOfflineVideosAsync()
    {
        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            var offlineCount = await ctx.VideoItems.CountAsync(v => !v.FileExists);
            if (offlineCount == 0)
            {
                MessageBox.Show(
                    "There are no offline video records to remove.",
                    "Remove offline videos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Remove all {offlineCount} offline video record(s) from the database?\n\n" +
                "These are records whose source file is no longer found on disk " +
                "(based on the latest Refresh / scan).\n\n" +
                "This cleans up the catalog and the app's thumbnail cache. " +
                "No source video files will be touched.",
                "Remove offline videos",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (result != MessageBoxResult.OK) return;
        }

        var removed = await _libraryService.RemoveOfflineAsync();
        StatusMessage = $"Removed {removed} offline video record(s) from the catalog. Source video files untouched.";
        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        if (RootFolders.Count == 0)
        {
            StatusMessage = "Add at least one root folder before scanning";
            return;
        }

        if (!_ffprobeService.IsAvailable())
        {
            MessageBox.Show(
                "ffprobe.exe was not found. Open Settings to configure the FFmpeg paths.",
                "Find That Shot",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ProgressValue = 0;
        ProgressMaximum = 100;

        _scanStartedAt = DateTime.UtcNow;
        _currentFileStartedAt = _scanStartedAt;
        _currentFilePath = null;
        ScanElapsedText = "Elapsed 00:00";
        StartHeartbeat();

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.TotalFound > 0) ProgressMaximum = p.TotalFound;
            ProgressValue = p.Processed;
            if (!string.Equals(p.CurrentFile, _currentFilePath, StringComparison.Ordinal))
            {
                _currentFilePath = p.CurrentFile;
                _currentFileStartedAt = DateTime.UtcNow;
            }
            StatusMessage = p.Message ?? p.CurrentFile ?? StatusMessage;
        });

        try
        {
            var roots = RootFolders.ToList();
            await _scannerService.ScanAsync(roots, progress, _scanCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            StopHeartbeat();
            ScanElapsedText = string.Empty;
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }

        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _scannerService.UpdateFileAvailabilityAsync();
        await ReloadFiltersAsync();
        await SearchAsync();
    }

    [ObservableProperty]
    private bool _isGeocodingLocations;

    [RelayCommand]
    private async Task FillMissingLocationsAsync()
    {
        if (IsGeocodingLocations) return;

        _geocodeCts = new CancellationTokenSource();
        IsGeocodingLocations = true;
        ProgressValue = 0;
        ProgressMaximum = 1;
        StatusMessage = "Looking up missing locations…";

        var progress = new Progress<GeocodeProgress>(p =>
        {
            if (p.TotalCandidates > 0) ProgressMaximum = p.TotalCandidates;
            ProgressValue = p.Processed;
            if (!string.IsNullOrWhiteSpace(p.Message)) StatusMessage = p.Message;
        });

        try
        {
            var filled = await _locationService.FillMissingLocationsAsync(progress, _geocodeCts.Token);
            StatusMessage = filled > 0
                ? $"Filled {filled} location(s) from GPS."
                : "No locations needed updating.";
            if (filled > 0)
            {
                await SearchAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Location lookup cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Location lookup failed: {ex.Message}";
        }
        finally
        {
            IsGeocodingLocations = false;
            _geocodeCts?.Dispose();
            _geocodeCts = null;
        }
    }

    [RelayCommand]
    private void CancelFillMissingLocations()
    {
        _geocodeCts?.Cancel();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow(_services.GetRequiredService<SettingsViewModel>())
        {
            Owner = App.Current.MainWindow
        };
        window.ShowDialog();

        // Settings may have flipped WriteSidecarFiles; refresh the status
        // bar indicator regardless of how the window was closed (Save / X
        // / Cancel all funnel through this point).
        OnPropertyChanged(nameof(SidecarStatusText));
    }

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    // Set by the silent background check kicked off in InitializeAsync.
    // The status bar binds a "Update available · vX.Y.Z" pill to these so
    // the user has a calm, dismissible indicator instead of a popup on
    // every launch. Clicking the pill invokes CheckForUpdatesCommand (the
    // existing explicit flow), which re-validates and opens the install
    // confirmation dialog.
    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _availableUpdateVersion;

    // Tracks whether the silent startup check has already run so we never
    // hit GitHub more than once per session from this path. The explicit
    // user-initiated CheckForUpdatesCommand is unaffected and may be
    // re-run at will.
    private bool _silentUpdateCheckDone;

    private async Task CheckForUpdatesSilentlyAsync()
    {
        if (_silentUpdateCheckDone) return;
        _silentUpdateCheckDone = true;

        try
        {
            var result = await _updateService.CheckAsync().ConfigureAwait(false);

            // Surface failures here ONLY through the silent log path; do
            // NOT touch StatusMessage or show any dialog. The user can
            // still hit Help -> Check for updates to get the explicit
            // error dialog if they care to investigate.
            if (!result.Success) return;
            if (result.NotInstalledMode) return;
            if (!result.UpdateAvailable) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AvailableUpdateVersion = result.AvailableVersion;
                IsUpdateAvailable = true;
            });
        }
        catch
        {
            // Network down, GitHub rate-limited, etc. The pill simply
            // stays hidden — this path is best-effort by design.
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates) return;
        IsCheckingForUpdates = true;
        try
        {
            StatusMessage = "Checking for updates…";

            var result = await _updateService.CheckAsync();

            if (!result.Success)
            {
                StatusMessage = $"Update check failed: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Could not check for updates.\n\n{result.ErrorMessage}",
                    "Check for updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (result.NotInstalledMode)
            {
                var msg = "This build is not running from an installer, so updates can't be applied.\n\n" +
                          "To test the update flow:\n" +
                          "  1. Run scripts/publish.ps1\n" +
                          "  2. Install the produced VideoArchiveManager-win-Setup.exe\n" +
                          "  3. Open the installed app and try Check for updates again.\n\n" +
                          (result.CurrentVersion is null
                              ? string.Empty
                              : $"Current build version: {result.CurrentVersion}");
                StatusMessage = "Update check skipped (not an installed build)";
                MessageBox.Show(msg, "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!result.UpdateAvailable)
            {
                // If the silent check had previously raised the pill but
                // the user has since installed via another path (or the
                // release was yanked), clear it so we don't keep nagging.
                IsUpdateAvailable = false;
                AvailableUpdateVersion = null;

                var v = result.CurrentVersion is null ? string.Empty : $" (v{result.CurrentVersion})";
                StatusMessage = $"Up to date{v}";
                MessageBox.Show(
                    $"You're on the latest version{v}.",
                    "Check for updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Keep the pill in sync with whatever the explicit check just
            // observed — particularly important when the user hits "Check
            // for updates" before the silent path has run, or when a
            // newer release dropped since the silent check.
            IsUpdateAvailable = true;
            AvailableUpdateVersion = result.AvailableVersion;

            StatusMessage = $"Update available: v{result.AvailableVersion}";
            var confirm = MessageBox.Show(
                $"An update is available.\n\n" +
                $"Current: v{result.CurrentVersion}\n" +
                $"New:     v{result.AvailableVersion}\n\n" +
                "Download and install it now? The app will close and restart on the new version when ready.\n\n" +
                "Your catalog database, settings, and sidecar files will NOT be affected.",
                "Update available",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.OK);
            if (confirm != MessageBoxResult.OK)
            {
                StatusMessage = $"Update deferred (v{result.AvailableVersion} available)";
                return;
            }

            StatusMessage = $"Downloading v{result.AvailableVersion}…";
            var apply = await _updateService.DownloadAndApplyAsync(onProgress: p =>
            {
                // Velopack reports 0..100 ints. Throttle the UI updates a bit
                // so we don't spam the property change pipeline.
                if (p % 5 == 0)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        StatusMessage = $"Downloading v{result.AvailableVersion}… {p}%";
                    });
                }
            });

            // We only reach this point if apply did NOT exit the process
            // (typical case is it never returns because ApplyUpdatesAndRestart
            // tears the process down). So this path == failure.
            if (!apply.Success)
            {
                StatusMessage = $"Update failed: {apply.ErrorMessage}";
                MessageBox.Show(
                    $"The update could not be installed.\n\n{apply.ErrorMessage}\n\n" +
                    "Your current version is unaffected.",
                    "Update failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private async Task OpenBulkEditAsync()
    {
        var ids = SelectedVideos.Select(v => v.Id).ToList();
        if (ids.Count == 0) return;

        var vm = _services.GetRequiredService<BulkEditViewModel>();
        vm.Initialize(ids);
        var window = new BulkEditDialog(vm)
        {
            Owner = App.Current.MainWindow
        };
        window.ShowDialog();
        // Bulk edit can create a brand-new tag via "Add tag"; pick it up
        // in the sidebar picker without requiring a restart. Cheap and
        // keeps cameras / root folders in sync too.
        await ReloadFiltersAsync();
        await SearchAsync();
        // Also refresh the detail editor's AutoSuggestBox cache so a tag
        // minted in bulk edit shows up immediately when the user starts
        // typing in the per-clip tag field.
        await Detail.RefreshTagCatalogAsync();
    }

    // One-click "I want to do a review batch now" affordance.
    // Strips every other filter so candidate clips aren't hidden by stale
    // state (e.g. an old tag chip still in play), flips OnlyUnreviewed on,
    // runs the search once at the end, and selects the first result so the
    // user can start reviewing immediately. Keeps ShowOnlyAvailable in
    // whatever state the user prefers — offline drives shouldn't appear in
    // a review queue unless they've explicitly opted in.
    [RelayCommand]
    private async Task StartReviewSessionAsync()
    {
        _suppressFilterSearch = true;
        try
        {
            SearchText = string.Empty;
            StatusFilter = null;
            MinRatingFilter = 0;
            CameraFilter = null;
            SelectedTagFilters.Clear();
            TagFilterSearchText = string.Empty;
            SelectedFolderNode = null;
            DateFrom = null;
            DateTo = null;
            OnlyUnreviewed = true;
        }
        finally
        {
            _suppressFilterSearch = false;
        }

        await SearchAsync();

        if (Videos.Count > 0)
        {
            SelectedVideo = Videos[0];
            StatusMessage = $"Review session: {Videos.Count} clip(s) waiting";
        }
        else
        {
            StatusMessage = "Review session: nothing to review — you're caught up.";
        }
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        // Batch: avoid 7+ overlapping searches as each property setter fires.
        _suppressFilterSearch = true;
        try
        {
            SearchText = string.Empty;
            StatusFilter = null;
            MinRatingFilter = 0;
            CameraFilter = null;
            SelectedTagFilters.Clear();
            TagFilterSearchText = string.Empty;
            SelectedFolderNode = null;
            DateFrom = null;
            DateTo = null;
            OnlyUnreviewed = false;
        }
        finally
        {
            _suppressFilterSearch = false;
        }
        await SearchAsync();
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _scanHeartbeatTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _scanHeartbeatTimer.Tick += ScanHeartbeat_Tick;
        _scanHeartbeatTimer.Start();
    }

    private void StopHeartbeat()
    {
        if (_scanHeartbeatTimer is null) return;
        _scanHeartbeatTimer.Stop();
        _scanHeartbeatTimer.Tick -= ScanHeartbeat_Tick;
        _scanHeartbeatTimer = null;
    }

    private void ScanHeartbeat_Tick(object? sender, EventArgs e)
    {
        if (!IsScanning) return;
        var now = DateTime.UtcNow;
        var total = now - _scanStartedAt;
        var onFile = now - _currentFileStartedAt;
        ScanElapsedText = _currentFilePath is null
            ? $"Elapsed {FormatDuration(total)}"
            : $"Elapsed {FormatDuration(total)} · current file {FormatDuration(onFile)}";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        }
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }
}
