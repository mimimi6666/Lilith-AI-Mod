using System.ComponentModel;
using LilithRuntimeContracts;

namespace LilithControlCenter;

// Display grouping only. Capability membership comes exclusively from the native catalog.
internal sealed class NativeToolGroup(string id, IReadOnlyList<D046HermesNativeToolsetViewModel> rows,
    bool isExpanded) : INotifyPropertyChanged
{
    private bool _isExpanded = isExpanded;
    public string Id { get; } = id;
    public IReadOnlyList<D046HermesNativeToolsetViewModel> Rows { get; } = rows;
    public string Label => ControlCenterLocalization.Current["tools.group." + Id];
    public string Summary => ControlCenterLocalization.Current.Format("tools.group.count", Rows.Count(r => r.DesiredEnabled), Rows.Count);
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (value == _isExpanded) return; _isExpanded = value; Refresh(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

internal static class NativeToolsPresentation
{
    internal static readonly string[] GroupOrder = ["lilith", "web", "media", "development", "computer", "integrations"];
    internal static string GroupFor(string name) => name switch
    {
        "lilith_local_scene" or "memory" or "session_search" or "lilith_notes" or "lilith_leave_note" => "lilith",
        "web" or "x_search" or "clarify" => "web",
        "vision" or "video" or "image_gen" or "video_gen" or "bfl" or "tts" or "stt" or "spotify" => "media",
        "file" or "terminal" or "code_execution" or "skills" or "todo" or "delegation" or "context_engine" => "development",
        "browser" or "computer_use" => "computer",
        _ => "integrations"
    };
    internal static int RowOrder(string name) => name switch
    {
        "lilith_local_scene" => 0, "memory" => 1, "session_search" => 2, "lilith_notes" => 3, "lilith_leave_note" => 4,
        _ => 10
    };

    internal static string Classify(IEnumerable<string> selected, IReadOnlyList<HermesNativeToolPreset>? presets)
    {
        var selection = selected.ToHashSet(StringComparer.Ordinal);
        return presets?.FirstOrDefault(p => selection.Where(p.ManagedToolsetIds.Contains)
            .ToHashSet(StringComparer.Ordinal).SetEquals(p.ToolsetIds))?.Id ?? "custom";
    }
}
