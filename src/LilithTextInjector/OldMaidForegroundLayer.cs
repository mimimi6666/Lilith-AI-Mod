using System;
using UnityEngine;
using UnityEngine.UI;
using Point3 = System.Numerics.Vector3;

namespace LilithTextInjector;

// Cards are projected into the native camera, between the character and UI.
// The sole native write is a reversible sorting lease on the existing shared
// dialogue/options canvas. No reparenting, clone, camera or material mutation.
internal sealed class OldMaidForegroundLayer : IDisposable
{
    private readonly Canvas _cards, _native;
    private readonly RectTransform _rect;
    private readonly DialogueBubbleUI _bubble;
    private readonly PlayerLineController _choices;
    private readonly int _sortingLayer;
    private readonly OldMaidRootUiSortLease _sortLease;

    internal OldMaidForegroundLayer(GameObject root, DialogueBubbleUI bubble)
    {
        _bubble = bubble;
        _choices = UnityEngine.Object.FindObjectOfType<PlayerLineController>(true)
            ?? throw new OldMaidUiBindingException(OldMaidUiBindingFailure.ChoiceSurface);
        _cards = root.GetComponent<Canvas>();
        _rect = root.GetComponent<RectTransform>();
        var bubbleCanvas = bubble.GetComponentInParent<Canvas>();
        var choiceCanvas = _choices.GetComponentInParent<Canvas>();
        if (bubbleCanvas == null || choiceCanvas == null
            || bubbleCanvas.rootCanvas != choiceCanvas.rootCanvas)
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.Hierarchy);
        _native = bubbleCanvas.rootCanvas;
        if (_native.renderMode != RenderMode.WorldSpace)
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.ProjectionMode);
        _sortLease = new OldMaidRootUiSortLease(new RootSortSurface(_native));
        _sortingLayer = _native.sortingLayerID;
        _cards.renderMode = RenderMode.WorldSpace;
        _cards.overrideSorting = true;
        _rect.anchorMin = _rect.anchorMax = _rect.pivot = Vector2.zero;
    }

    internal void Bind(global::CharacterController character, int width, int height)
    {
        var camera = character._mainCamera;
        var renderer = character.SkeletonAnimationInternal?.meshRenderer;
        if (_native == null || _bubble == null || _choices == null || camera == null || renderer == null)
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.SurfaceMissing);
        Require(_native.renderMode == RenderMode.WorldSpace, OldMaidUiBindingFailure.ProjectionMode);
        Require(_bubble.GetComponentInParent<Canvas>()?.rootCanvas == _native
            && _choices.GetComponentInParent<Canvas>()?.rootCanvas == _native,
            OldMaidUiBindingFailure.Hierarchy);
        Require(_native.worldCamera == camera, OldMaidUiBindingFailure.CameraIdentity);
        Require(_native.sortingLayerID == _sortingLayer && renderer.sortingLayerID == _sortingLayer,
            OldMaidUiBindingFailure.SortingLayer);
        Require(camera.orthographic, OldMaidUiBindingFailure.CameraProjection);
        Require(camera.targetTexture == null, OldMaidUiBindingFailure.CameraTarget);
        Require(camera.targetDisplay == 0, OldMaidUiBindingFailure.CameraDisplay);
        Require((camera.cullingMask & (1 << _cards.gameObject.layer)) != 0, OldMaidUiBindingFailure.CullingMask);
        Require(OldMaidDiscardLayerPolicy.FullViewport(width, height,
            camera.pixelRect.x, camera.pixelRect.y, camera.pixelRect.width, camera.pixelRect.height),
            OldMaidUiBindingFailure.Viewport);
        if (!OldMaidUiPresentationPolicy.TryOrders(renderer.sortingOrder, _sortLease.OriginalOrder,
                out var cardOrder, out var uiOrder))
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.SortingRange);
        var depth = camera.nearClipPlane + 0.1f;
        if (!float.IsFinite(depth) || depth >= camera.farClipPlane)
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.Depth);
        var origin = camera.ScreenToWorldPoint(new Vector3(0, 0, depth));
        var right = camera.ScreenToWorldPoint(new Vector3(width, 0, depth));
        var up = camera.ScreenToWorldPoint(new Vector3(0, height, depth));
        var diagonal = camera.ScreenToWorldPoint(new Vector3(width, height, depth));
        if (!OldMaidDiscardLayerPolicy.TryBasis(width, height, Point(origin), Point(right), Point(up),
            Point(diagonal), out var basis)) throw new OldMaidUiBindingException(OldMaidUiBindingFailure.ProjectionBasis);
        if (_cards.worldCamera != camera) _cards.worldCamera = camera;
        if (_cards.sortingLayerID != _sortingLayer) _cards.sortingLayerID = _sortingLayer;
        if (_cards.sortingOrder != cardOrder) _cards.sortingOrder = cardOrder;
        if (_cards.targetDisplay != camera.targetDisplay) _cards.targetDisplay = camera.targetDisplay;
        var size = new Vector2(width, height);
        var rotation = Quaternion.LookRotation(Vector(Point3.Cross(basis.Right, basis.Up)), Vector(basis.Up));
        var scale = new Vector3(basis.ScaleX, basis.ScaleY, 1);
        if (!_rect.sizeDelta.Equals(size)) _rect.sizeDelta = size;
        if (!_rect.position.Equals(origin)) _rect.position = origin;
        if (!_rect.rotation.Equals(rotation)) _rect.rotation = rotation;
        if (!_rect.localScale.Equals(scale)) _rect.localScale = scale;
        if (!RoundTrips(camera, 0, 0, depth) || !RoundTrips(camera, width, height, depth)
            || !RoundTrips(camera, width * 0.5f, height * 0.5f, depth))
            throw new OldMaidUiBindingException(OldMaidUiBindingFailure.PixelGeometry);
        _sortLease.ApplyOrder(uiOrder);
    }

    internal bool NativeUiOwnsPointer()
    {
        if (_choices != null && _choices.isActiveAndEnabled && _choices._isShowing
            && _choices.IsPointerOverAnyOption()) return true;
        if (_bubble == null || !_bubble.isActiveAndEnabled || !_bubble._isShowing) return false;
        var button = _bubble._bubbleButton;
        return button != null && button.isActiveAndEnabled && button.IsInteractable()
            && button.targetGraphic != null && button.targetGraphic.raycastTarget
            && RectTransformUtility.RectangleContainsScreenPoint(
                button.GetComponent<RectTransform>(), Input.mousePosition, _native.worldCamera);
    }

    public void Dispose()
    {
        _sortLease.Dispose();
    }

    private sealed class RootSortSurface : IOldMaidRootUiSortSurface
    {
        private readonly Canvas canvas;
        internal RootSortSurface(Canvas nativeCanvas) => canvas = nativeCanvas;
        public bool IsRootCanvas => canvas != null && canvas.isRootCanvas;
        public int SortingLayer => canvas.sortingLayerID;
        public int SortingOrder { get => canvas.sortingOrder; set => canvas.sortingOrder = value; }
        public bool OverrideSorting => canvas.overrideSorting;
    }

    private bool RoundTrips(Camera camera, float x, float y, float depth) =>
        OldMaidDiscardLayerPolicy.RoundTrips(Point(camera.WorldToScreenPoint(_rect.TransformPoint(new Vector3(x, y, 0)))), x, y, depth);
    private static Point3 Point(Vector3 point) => new(point.x, point.y, point.z);
    private static Vector3 Vector(Point3 point) => new(point.X, point.Y, point.Z);
    private static void Require(bool supported, OldMaidUiBindingFailure reason)
    {
        if (!supported) throw new OldMaidUiBindingException(reason);
    }
}
