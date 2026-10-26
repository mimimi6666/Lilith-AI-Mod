using System;

namespace LilithTextInjector;

// Source-owned enums only: diagnostics never expose native names, assets,
// arbitrary exception messages, paths or dialogue text.
internal enum OldMaidUiBindingFailure
{
    ChoiceSurface, Hierarchy, ProjectionMode, SurfaceMissing, CameraIdentity,
    SortingLayer, CameraProjection, CameraTarget, CameraDisplay, CullingMask,
    Viewport, SortingRange, SortingOwnership, Depth, ProjectionBasis, PixelGeometry
}

internal sealed class OldMaidUiBindingException : InvalidOperationException
{
    internal OldMaidUiBindingException(OldMaidUiBindingFailure failure) : base("Old Maid UI binding unavailable.") => Failure = failure;
    internal OldMaidUiBindingFailure Failure { get; }
}

internal static class OldMaidUiPresentationPolicy
{
    // The inspected native UI and Spine use the same sorting layer. Reserve
    // two orders, never wrap Unity's signed 16-bit sorting-order range.
    internal static bool TryOrders(int character, int nativeUi, out int cards, out int ui)
    {
        cards = ui = 0;
        if (character < short.MinValue || nativeUi < short.MinValue
            || character > short.MaxValue - 2 || nativeUi > short.MaxValue - 2) return false;
        cards = Math.Max(character, nativeUi) + 1;
        ui = cards + 1;
        return true;
    }
}

// overrideSorting is a nested-canvas switch. The inspected engine ignores its
// setter on root canvases. Keep it read-only, including during restoration.
internal interface IOldMaidRootUiSortSurface
{
    bool IsRootCanvas { get; }
    int SortingLayer { get; }
    int SortingOrder { get; set; }
    bool OverrideSorting { get; }
}

internal sealed class OldMaidRootUiSortLease : IDisposable
{
    private readonly IOldMaidRootUiSortSurface _surface;
    private readonly int _originalOrder, _originalLayer;
    private readonly bool _originalOverride;
    private int _expectedOrder;
    private bool _leased, _disposed;

    internal OldMaidRootUiSortLease(IOldMaidRootUiSortSurface surface)
    {
        _surface = surface;
        if (!surface.IsRootCanvas)
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.Hierarchy);
        _originalOrder = _expectedOrder = surface.SortingOrder;
        _originalLayer = surface.SortingLayer;
        _originalOverride = surface.OverrideSorting;
    }

    internal int OriginalOrder => _originalOrder;

    internal void ApplyOrder(int order)
    {
        if (_disposed || !OwnsCurrentState())
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.SortingOwnership);
        if (order < short.MinValue || order > short.MaxValue)
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.SortingRange);
        if (_expectedOrder == order) return;
        // Record the intended write before the setter: if it writes then throws,
        // cleanup can still restore exactly our value, never an unrelated value.
        _expectedOrder = order;
        _leased = true;
        _surface.SortingOrder = order;
        if (!OwnsCurrentState())
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.SortingOwnership);
    }

    private bool OwnsCurrentState() => _surface.IsRootCanvas
        && _surface.SortingLayer == _originalLayer
        && _surface.SortingOrder == _expectedOrder
        && _surface.OverrideSorting == _originalOverride;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_leased && OwnsCurrentState()) _surface.SortingOrder = _originalOrder;
        _leased = false;
    }
}

// Native head drop is an offset, not the character's world position. The UI
// converts it to local space itself. Hold it within a seated geometry epoch;
// moving the character still moves
// its native parent normally. No card animation clock is involved.
internal sealed class OldMaidStableUiLift
{
    private int _owner, _width, _height;
    private float _scaleX, _scaleY, _zoom, _value;
    private bool _captured;

    internal bool IsCaptured => _captured;

    internal float Apply(bool seated, int owner, int width, int height,
        float scaleX, float scaleY, float zoom, float nativeValue)
    {
        if (!seated || owner == 0 || width <= 0 || height <= 0
            || !float.IsFinite(scaleX) || scaleX <= 0 || !float.IsFinite(scaleY) || scaleY <= 0
            || !float.IsFinite(zoom) || zoom <= 0 || !float.IsFinite(nativeValue))
        { Reset(); return nativeValue; }
        if (!_captured || _owner != owner || _width != width || _height != height
            || _scaleX != scaleX || _scaleY != scaleY || _zoom != zoom)
        {
            _captured = true;
            _owner = owner; _width = width; _height = height;
            _scaleX = scaleX; _scaleY = scaleY; _zoom = zoom; _value = nativeValue;
        }
        return _value;
    }

    internal void Reset() => _captured = false;
}
