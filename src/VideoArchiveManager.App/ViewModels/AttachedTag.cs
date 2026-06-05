using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.App.ViewModels;

// Display wrapper for a Tag attached to a specific clip or moment, carrying the
// per-attachment prominence (IsBackground) that the bare Tag can't express.
// Name / Id / Type are passed through so existing chip bindings ({Binding Name})
// and the attach/detach/toggle logic keep working unchanged.
public partial class AttachedTag : ObservableObject
{
    public Tag Tag { get; }

    // true ⇒ this tag is an incidental / background subject on the clip or
    // moment (e.g. distant islands behind a beach), false ⇒ primary subject.
    // Drives the dimmed chip styling and the context-menu toggle.
    [ObservableProperty]
    private bool _isBackground;

    public AttachedTag(Tag tag, bool isBackground)
    {
        Tag = tag;
        _isBackground = isBackground;
    }

    public int Id => Tag.Id;
    public string Name => Tag.Name;
    public TagType Type => Tag.Type;
}
