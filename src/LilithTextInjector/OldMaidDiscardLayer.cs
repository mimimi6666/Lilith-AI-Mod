using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Point3 = System.Numerics.Vector3;

namespace LilithTextInjector;

// Session-owned scene canvas. All native objects below are READ ONLY. The
// bounded supported topology matches the inspected original scene, not a
// camera/layer name guess. Unsupported topology hides discards, never moves
// Lilith or falls back to the foreground hand overlay.
internal sealed class OldMaidDiscardLayer : IDisposable
{
    private const int MaximumCameras = 8, MaximumRenderers = 16;
    private readonly Il2CppReferenceArray<Camera> _cameras = new(MaximumCameras);
    private GameObject _root = null!;
    private RectTransform _rect = null!;
    private Canvas _canvas = null!;
    private bool _disposed;
    private int _materialCount = -1, _materialSlot = -1, _materialQueue = -1;
    private int _shaderClass, _tagClass;

    internal RectTransform ShadowParent { get; private set; } = null!;
    internal RectTransform CardParent { get; private set; } = null!;
    internal int Layer { get; private set; }
    internal int RenderQueue { get; private set; }
    internal string MaterialDiagnostic => OldMaidDiscardLayerPolicy.MaterialDiagnostic(
        _materialCount, _materialSlot, _materialQueue, _shaderClass, _tagClass);

    internal static OldMaidDiscardLayer Create()
    {
        var layer = new OldMaidDiscardLayer();
        try
        {
            layer._root = new GameObject("LilithOldMaidSceneDiscards", Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>());
            layer._root.SetActive(false);
            layer._rect = layer._root.GetComponent<RectTransform>();
            layer._rect.SetParent(null, false);
            layer._rect.anchorMin = layer._rect.anchorMax = layer._rect.pivot = Vector2.zero;
            layer._canvas = layer._root.GetComponent<Canvas>();
            layer._canvas.renderMode = RenderMode.WorldSpace;
            layer._canvas.overrideSorting = true;
            layer._canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            layer.ShadowParent = layer.Child("SpatialGroundShadows");
            layer.CardParent = layer.Child("SpatialDiscardCards");
            return layer;
        }
        catch { layer.Dispose(); throw; }
    }

    internal bool TryBind(global::CharacterController character, int screenWidth, int screenHeight, out string reason)
    {
        reason = "disposed";
        if (_disposed) return false;
        _materialCount = _materialSlot = _materialQueue = -1;
        _shaderClass = _tagClass = 0;
        try
        {
            if (!TryReadBinding(character, screenWidth, screenHeight, out var camera, out var renderer,
                out var depth, out var order, out var queue, out reason)) { Hide(); return false; }
            var origin = camera!.ScreenToWorldPoint(new Vector3(0, 0, depth));
            var right = camera.ScreenToWorldPoint(new Vector3(screenWidth, 0, depth));
            var up = camera.ScreenToWorldPoint(new Vector3(0, screenHeight, depth));
            var diagonal = camera.ScreenToWorldPoint(new Vector3(screenWidth, screenHeight, depth));
            if (!OldMaidDiscardLayerPolicy.TryBasis(screenWidth, screenHeight,
                Point(origin), Point(right), Point(up), Point(diagonal), out var basis))
            { reason = "projection_basis"; Hide(); return false; }

            // Only owned canvas/transform/layer values are changed. Pixel
            // coordinates remain independent of Lilith's screen position.
            Layer = renderer!.gameObject.layer;
            RenderQueue = queue;
            // Revalidate native authority each call, but do not dirty the
            // whole owned canvas repeatedly when its actual values match.
            if (_root.layer != Layer) _root.layer = Layer;
            if (ShadowParent.gameObject.layer != Layer) ShadowParent.gameObject.layer = Layer;
            if (CardParent.gameObject.layer != Layer) CardParent.gameObject.layer = Layer;
            if (_canvas.worldCamera != camera) _canvas.worldCamera = camera;
            if (_canvas.targetDisplay != camera.targetDisplay) _canvas.targetDisplay = camera.targetDisplay;
            if (_canvas.sortingLayerID != renderer.sortingLayerID) _canvas.sortingLayerID = renderer.sortingLayerID;
            if (_canvas.sortingOrder != order) _canvas.sortingOrder = order;
            var size = new Vector2(screenWidth, screenHeight);
            var position = Vector(basis.Origin);
            var rotation = Quaternion.LookRotation(Vector(Point3.Cross(basis.Right, basis.Up)), Vector(basis.Up));
            var scale = new Vector3(basis.ScaleX, basis.ScaleY, 1);
            if (!_rect.sizeDelta.Equals(size)) _rect.sizeDelta = size;
            if (!_rect.position.Equals(position)) _rect.position = position;
            if (!_rect.rotation.Equals(rotation)) _rect.rotation = rotation;
            if (!_rect.localScale.Equals(scale)) _rect.localScale = scale;

            // Check the actual native transform, not merely algebraic inputs.
            // This catches unexpected projection or hierarchy assumptions.
            if (!RoundTrips(camera, 0, 0, depth) || !RoundTrips(camera, screenWidth, 0, depth) ||
                !RoundTrips(camera, 0, screenHeight, depth) || !RoundTrips(camera, screenWidth, screenHeight, depth) ||
                !RoundTrips(camera, screenWidth * 0.5f, screenHeight * 0.5f, depth))
            { reason = "projection_roundtrip"; Hide(); return false; }
            if (!_root.activeSelf) _root.SetActive(true);
            reason = "ready";
            return true;
        }
        catch
        {
            // No raw hierarchy, asset names or exception text in diagnostics.
            reason = "native_binding";
            Hide();
            return false;
        }
    }

    private bool TryReadBinding(global::CharacterController character, int width, int height,
        out Camera? camera, out MeshRenderer? renderer, out float depth, out int order, out int queue, out string reason)
    {
        camera = null; renderer = null; depth = 0; order = queue = 0;
        reason = "character";
        if (character == null || character != global::CharacterController.s_activeInstance || !character.isActiveAndEnabled) return false;
        var window = TransparentWindowNew.instance;
        camera = character._mainCamera;
        reason = "camera_identity";
        if (window == null || !window.isActiveAndEnabled || camera == null || window.mainCamera == null || camera != window.mainCamera ||
            !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game) return false;
        reason = "camera_count";
        if (Camera.allCamerasCount != 1 || Camera.GetAllCameras(_cameras) != 1 || _cameras[0] != camera) return false;
        reason = "camera_output";
        var pixels = camera.pixelRect;
        var viewport = camera.rect;
        if (!camera.orthographic || camera.stereoEnabled || camera.targetTexture != null || camera.targetDisplay != 0 ||
            !OldMaidDiscardLayerPolicy.FullViewport(width, height, pixels.x, pixels.y, pixels.width, pixels.height) ||
            !OldMaidDiscardLayerPolicy.FullNormalizedViewport(viewport.x, viewport.y, viewport.width, viewport.height)) return false;
        reason = "camera_pipeline";
        var additional = camera.GetComponent<UniversalAdditionalCameraData>();
        if (additional == null || additional.renderType != CameraRenderType.Base) return false;
        var stack = additional.cameraStack;
        if (stack != null && stack.Count != 0) return false;

        reason = "spine_renderer";
        var skeleton = character.SkeletonAnimationInternal;
        if (skeleton == null || !skeleton.isActiveAndEnabled || skeleton.meshRenderer == null) return false;
        renderer = skeleton.meshRenderer;
        if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
            (character._cachedSpineRenderer != null && character._cachedSpineRenderer != renderer) ||
            (character._cachedSpineRendererHost != null && character._cachedSpineRendererHost != skeleton)) return false;
        var animations = character.GetComponentsInChildren<SkeletonAnimation>(false);
        if (animations.Length != 1 || animations[0] != skeleton) return false;
        var renderers = character.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length < 1 || renderers.Length > MaximumRenderers) return false;
        var activeRenderers = 0;
        foreach (var item in renderers)
        {
            if (item == null || !item.enabled || item.forceRenderingOff || !item.gameObject.activeInHierarchy) continue;
            if (item != renderer) return false;
            activeRenderers++;
        }
        if (activeRenderers != 1) return false;
        reason = "sorting_group";
        var groups = renderer.GetComponentsInParent<SortingGroup>(true);
        if (groups.Length > MaximumRenderers) return false;
        foreach (var group in groups) if (group != null && group.isActiveAndEnabled) return false;
        if (renderer.sortingGroupID != SortingGroup.invalidSortingGroupID) return false;
        reason = "spine_separator";
        var separators = character.GetComponentsInChildren<SkeletonRenderSeparator>(false);
        if (separators.Length > MaximumRenderers) return false;
        foreach (var separator in separators) if (separator != null && separator.isActiveAndEnabled) return false;
        reason = "culling_layer";
        var layer = renderer.gameObject.layer;
        if (layer < 0 || layer > 31 || (camera.cullingMask & (1 << layer)) == 0 ||
            !SortingLayer.IsValid(renderer.sortingLayerID)) return false;

        // Spine rebuilds this array during LateUpdate from the active
        // attachments. The presenter binds at the validated late render seam;
        // neither an old success nor an unused-slot guess permits this frame.
        reason = "material_count_empty";
        var materials = renderer.sharedMaterials;
        _materialCount = materials?.Length ?? 0;
        if (materials == null) return false;
        if (!OldMaidDiscardLayerPolicy.TryBeginOrdering(layer, renderer.sortingOrder, _materialCount,
            out order, out var minimumQueue, out reason)) return false;
        for (var index = 0; index < materials.Length; index++)
        {
            _materialSlot = index; _materialQueue = -1;
            _shaderClass = _tagClass = 0;
            var material = materials[index];
            reason = "material_slot_null";
            if (material == null) return false;
            _materialQueue = material.renderQueue;
            var shader = material.shader;
            reason = "material_shader_null";
            if (shader == null) return false;
            _shaderClass = OldMaidDiscardLayerPolicy.KnownCharacterShaderName(shader.name) ? 1 : 2;
            reason = "material_shader_unknown";
            if (_shaderClass != 1) return false;
            _tagClass = OldMaidDiscardLayerPolicy.KnownCharacterRenderType(
                material.GetTag("RenderType", false, string.Empty)) ? 1 : 2;
            reason = "material_tag";
            if (_tagClass != 1) return false;
            reason = "material_queue";
            if (!OldMaidDiscardLayerPolicy.TryIncludeMaterialQueue(_materialQueue, ref minimumQueue)) return false;
        }
        if (!OldMaidDiscardLayerPolicy.TryFinishOrdering(minimumQueue, out queue, out reason)) return false;
        reason = "character_depth";
        var bounds = renderer.bounds;
        if (!OldMaidDiscardLayerPolicy.Finite(Point(bounds.min)) || !OldMaidDiscardLayerPolicy.Finite(Point(bounds.max)) ||
            bounds.size.sqrMagnitude < 0.000001f) return false;
        var nearest = float.PositiveInfinity;
        var farthest = float.NegativeInfinity;
        for (var index = 0; index < 8; index++)
        {
            var corner = new Vector3((index & 1) == 0 ? bounds.min.x : bounds.max.x,
                (index & 2) == 0 ? bounds.min.y : bounds.max.y, (index & 4) == 0 ? bounds.min.z : bounds.max.z);
            var projected = camera.WorldToScreenPoint(corner);
            if (!OldMaidDiscardLayerPolicy.Finite(Point(projected))) return false;
            nearest = Math.Min(nearest, projected.z); farthest = Math.Max(farthest, projected.z);
        }
        return OldMaidDiscardLayerPolicy.TryPlaneDepth(camera.nearClipPlane, camera.farClipPlane, nearest, farthest, out depth);
    }

    private bool RoundTrips(Camera camera, float x, float y, float depth) =>
        OldMaidDiscardLayerPolicy.RoundTrips(Point(camera.WorldToScreenPoint(_rect.TransformPoint(new Vector3(x, y, 0)))), x, y, depth);

    private RectTransform Child(string name)
    {
        var child = new GameObject(name, Il2CppType.Of<RectTransform>());
        child.transform.SetParent(_root.transform, false);
        var rect = child.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    internal void Hide()
    {
        try { if (_root != null && _root.activeSelf) _root.SetActive(false); }
        catch { /* Native teardown must not abandon other owned resources. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hide();
        try { if (_root != null) UnityEngine.Object.Destroy(_root); }
        catch { /* Only this session-owned root is eligible for destruction. */ }
    }

    private static Point3 Point(Vector3 point) => new(point.x, point.y, point.z);
    private static Vector3 Vector(Point3 point) => new(point.X, point.Y, point.Z);
}
