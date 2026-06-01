using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.App.ViewModels;

// One node in the sidebar folder tree (Lightroom-style).
//
// The tree models the *catalog* — drives and registered root folders are
// always represented; deeper nodes are derived from the distinct
// VideoItem.FolderPath values produced by scans. Counts roll up
// recursively: a parent node shows OwnCount + the sum of all
// descendants' VideoCount, matching how Lightroom and Bridge surface
// folder counts in their tree panels.
//
// IsExpanded / IsSelected are TwoWay-bound from the TreeViewItem via the
// ItemContainerStyle in MainWindow.xaml, so node-side state mutations
// (e.g. restoring expansion after a rebuild) reach the visual tree
// without any code-behind plumbing.
public partial class FolderNode : ObservableObject
{
    // Display name. For drive nodes this is a friendly label like
    // "MediaExtension I (E:)" when the volume is mounted, falling back
    // to the bare drive path for offline drives. For registered root
    // nodes it's RootFolder.Name (or the last path segment). For all
    // other nodes it's just the segment (e.g. "20070101 - Brasil").
    public string Name { get; init; } = string.Empty;

    // Canonical, absolute path. Used as the dictionary key during tree
    // construction and as the prefix fed into SearchQuery.RootFolderPath
    // when the node is selected. No trailing separator unless the path
    // IS a drive root (e.g. "E:\").
    public string FullPath { get; init; } = string.Empty;

    // True for the synthetic top-level node that represents a physical
    // drive. Drives auto-expand on first render so the user sees their
    // registered roots without having to click.
    public bool IsDriveRoot { get; init; }

    // True when this node corresponds to a RootFolder entity the user
    // explicitly added via "Add folder…". Drives the visibility / enabled
    // state of the "Remove folder" context-menu action — only registered
    // roots can be removed; intermediate / drive nodes can't.
    public bool IsRegisteredRoot { get; set; }

    // Back-reference to the RootFolder entity for registered-root nodes.
    // Null for drive nodes and derived subfolder nodes. Passed to
    // RemoveRootFolderCommand verbatim so the existing flow keeps working.
    public RootFolder? RootFolder { get; set; }

    // Count of videos whose FolderPath equals this node's FullPath
    // exactly. Populated during tree construction before counts are
    // rolled up. Not displayed directly — VideoCount (recursive total)
    // is what the UI shows.
    public int OwnCount { get; set; }

    // Recursive total: OwnCount plus every descendant's VideoCount.
    // Bound by the TreeView ItemTemplate. Observable so future
    // partial-refresh paths (e.g. incremental scan updates) can patch
    // a single subtree without a full rebuild.
    [ObservableProperty]
    private int _videoCount;

    [ObservableProperty]
    private bool _isExpanded;

    // TwoWay-bound to TreeViewItem.IsSelected. Mirrored into
    // MainViewModel.SelectedFolderNode via TreeView_SelectedItemChanged
    // in the code-behind, which is the trigger for re-running the
    // catalog search.
    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<FolderNode> Children { get; } = new();
}
