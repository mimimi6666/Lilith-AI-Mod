using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Point = System.Numerics.Vector2;

namespace LilithTextInjector;

// Discard-only adapter for the exact projected quads used by the standalone
// preview. No camera, native physics, input component, stencil or custom
// IL2CPP component is involved. Whole-card ordering remains the presenter's
// far-to-near order, not a claim of per-pixel intersecting-card occlusion.
internal sealed class OldMaidSpatialCardVisual : IDisposable
{
    private const float CardWidth = 100f, CardHeight = 150f;
    private const int MaximumTextVertices = 1024, MaximumTextMaterials = 8;
    private static readonly Color32 Navy = new(17, 25, 46, 255);
    private static readonly Color32 Keyline = new(250, 247, 242, 255);
    private static readonly Color32 Plate = new(255, 251, 245, 224);

    private readonly Transform _shadowParent, _cardParent;
    private readonly TextMeshProUGUI _font;
    private int _layer;
    private int _renderQueue;
    private Material? _graphicMaterial;
    private readonly List<Mesh> _ownedMeshes = new();
    private readonly Dictionary<int, Material> _fontMaterials = new();
    private readonly List<MeshLayer> _faceLayers = new();
    private readonly List<MeshLayer> _indexLayers = new();
    private readonly List<MeshLayer> _frontFallback = new();
    private readonly List<MeshLayer> _backFallback = new();
    private readonly MeshLayer[] _shadows = new MeshLayer[3];
    private readonly Point[] _lastTexture = new Point[4], _lastShadow = new Point[4];
    private RectTransform _shadowRect = null!;
    private MeshLayer _art = null!;
    private bool _disposed, _hasFrame, _lastFront, _faceVisible, _shadowVisible;
    private bool _artMissing;
    private int _cardId = -1;
    private float _lastX, _lastY, _lastWidth, _lastHeight, _lastOpacity, _lastBlur;

    internal RectTransform Rect { get; private set; } = null!;

    private OldMaidSpatialCardVisual(Transform shadowParent, Transform cardParent,
        int layer, TextMeshProUGUI font)
    {
        _shadowParent = shadowParent; _cardParent = cardParent;
        _layer = layer; _font = font;
    }

    internal static OldMaidSpatialCardVisual Create(Transform shadowParent, Transform cardParent,
        int layer, TextMeshProUGUI font)
    {
        var visual = new OldMaidSpatialCardVisual(shadowParent, cardParent, layer, font);
        try { visual.Initialize(); return visual; }
        catch { visual.Dispose(); throw; }
    }

    private void Initialize()
    {
        // Both roots occupy the owned canvas's full work extent; narrow
        // pre-projection rectangles must not cull a warped mesh's corners.
        Rect = NewRoot("SpatialDiscardCard", _cardParent);
        _shadowRect = NewRoot("SpatialDiscardShadow", _shadowParent);
        _graphicMaterial = new Material(Graphic.defaultGraphicMaterial) { name = "OldMaidSpatialGraphic" };
        var outer = Topology(2, 3, 0);
        _faceLayers.Add(NewLayer("NavyOutline", Rect, outer, Navy));
        _faceLayers.Add(NewLayer("SoftWhiteKeyline", Rect, Topology(2, 3, 0.00625f), Keyline));
        _art = NewLayer("PerspectiveArtwork", Rect, Topology(12, 18, 0.015625f), White);
        _faceLayers.Add(_art);
        for (var index = 0; index < _shadows.Length; index++)
            _shadows[index] = NewLayer("FloorShadow" + index, _shadowRect, outer, Navy);
        Hide();
    }

    // textureCorners already has the core's back-side correction and any
    // short, visual-only launch bridge. Never mirror it a second time.
    internal void Render(OldMaidCard card, OldMaidSpatialProjection projection,
        Point[] textureCorners, float workX, float workY, float workWidth, float workHeight)
    {
        if (_disposed) return;
        if (!OldMaidDiscardLayerPolicy.ValidOwnedQueue(_renderQueue))
            throw new InvalidOperationException("Discard material binding unavailable.");
        if (!ValidViewport(workX, workY, workWidth, workHeight)
            || projection.CardId != card.Id || !ValidCorners(textureCorners)
            || !ValidCorners(projection.ShadowCorners) || !Finite(projection.ShadowOpacity)
            || !Finite(projection.ShadowBlur)) { Hide(); return; }
        try
        {
            if (_cardId < 0)
            {
                // One renderer belongs to one logical card for its lifetime.
                _cardId = card.Id;
                Rect.gameObject.SetActive(true);
                BuildIndicesAndFallbacks(card);
            }
            if (_cardId != card.Id) { Hide(); return; }
            var changed = !_hasFrame || _lastFront != projection.FrontFacing
                || _lastX != workX || _lastY != workY || _lastWidth != workWidth || _lastHeight != workHeight
                || _lastOpacity != projection.ShadowOpacity || _lastBlur != projection.ShadowBlur
                || !SameCorners(_lastTexture, textureCorners) || !SameCorners(_lastShadow, projection.ShadowCorners);
            if (changed)
            {
                SizeRoots(workX, workY, workWidth, workHeight);
                if (!_hasFrame || _lastFront != projection.FrontFacing) SetArtwork(card, !projection.FrontFacing);
                _faceVisible = OldMaidSpatialMeshGeometry.TryCreateMap(textureCorners, out var faceMap);
                if (_faceVisible)
                {
                    foreach (var mesh in _faceLayers) mesh.Project(faceMap, workX, workY, workHeight);
                    if (projection.FrontFacing)
                        foreach (var mesh in _indexLayers) mesh.Project(faceMap, workX, workY, workHeight);
                    if (_artMissing)
                        foreach (var mesh in projection.FrontFacing ? _frontFallback : _backFallback)
                            mesh.Project(faceMap, workX, workY, workHeight);
                }
                _shadowVisible = OldMaidSpatialMeshGeometry.TryCreateMap(projection.ShadowCorners, out var shadowMap);
                if (_shadowVisible)
                {
                    var opacity = Math.Clamp(projection.ShadowOpacity, 0f, 0.5f);
                    var blur = Math.Clamp(projection.ShadowBlur, 0f, 10f);
                    for (var index = 0; index < _shadows.Length; index++)
                    {
                        // Three bounded translucent silhouettes approximate a
                        // soft floor shadow, independent of face orientation.
                        var weight = index == 0 ? 0.16f : index == 1 ? 0.28f : 0.56f;
                        _shadows[index].Tint(new Color32(10, 15, 31, (byte)(opacity * weight * 255)));
                        _shadows[index].Project(shadowMap, workX, workY, workHeight,
                            (2 - index) * blur * 0.5f);
                    }
                }
                CacheFrame(projection, textureCorners, workX, workY, workWidth, workHeight);
            }
            Rect.gameObject.SetActive(_faceVisible);
            _shadowRect.gameObject.SetActive(_shadowVisible);
            foreach (var mesh in _indexLayers) mesh.Visible(projection.FrontFacing);
            foreach (var mesh in _frontFallback) mesh.Visible(_artMissing && projection.FrontFacing);
            foreach (var mesh in _backFallback) mesh.Visible(_artMissing && !projection.FrontFacing);
            // All shadows have a different parent below *every* card. This
            // changes order only inside that separate, non-interactive group.
            if (_shadowVisible) _shadowRect.SetAsLastSibling();
        }
        catch
        {
            // Missing optional art is a readable fallback. An unexpected
            // native renderer failure is NOT a silently missing discarded
            // card: let the presenter perform its existing session cleanup.
            try { Hide(); } catch { }
            throw;
        }
    }

    internal void Hide()
    {
        if ((Rect != null && Rect.gameObject.activeSelf)
            || (_shadowRect != null && _shadowRect.gameObject.activeSelf)) Invalidate();
        if (Rect != null) Rect.gameObject.SetActive(false);
        if (_shadowRect != null) _shadowRect.gameObject.SetActive(false);
    }

    // The presenter calls this across an ancestor canvas hide/resume. Native
    // UI disable can discard CanvasRenderer state even if corners are equal.
    internal void Invalidate() => _hasFrame = false;

    // The binding adapter computes this from actual native material queues.
    // Every material here is an owned clone, never the native font/template.
    internal void SetRenderQueue(int queue)
    {
        if (_disposed) return;
        if (!OldMaidDiscardLayerPolicy.ValidOwnedQueue(queue))
            throw new ArgumentOutOfRangeException(nameof(queue));
        if (_renderQueue == queue) return;
        _renderQueue = queue;
        if (_graphicMaterial != null) _graphicMaterial.renderQueue = queue;
        foreach (var material in _fontMaterials.Values) material.renderQueue = queue;
        Invalidate();
    }

    internal void SetRenderLayer(int layer)
    {
        if (_disposed) return;
        if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer));
        if (_layer == layer) return;
        // Roots and descendants belong exclusively to this logical card.
        // There is no native character/font hierarchy beneath either root.
        var faceNodes = Rect.GetComponentsInChildren<Transform>(true);
        var shadowNodes = _shadowRect.GetComponentsInChildren<Transform>(true);
        if (faceNodes.Length > 512 || shadowNodes.Length > 512)
            throw new InvalidOperationException("Unbounded owned card hierarchy.");
        foreach (var node in faceNodes) node.gameObject.layer = layer;
        foreach (var node in shadowNodes) node.gameObject.layer = layer;
        _layer = layer;
        Invalidate();
    }

    private void SetArtwork(OldMaidCard card, bool back)
    {
        // Shared loader owns these sprites/textures; this adapter must never
        // destroy or rewrite the approved artwork, nor load a second copy.
        var sprite = OldMaidCardVisuals.LoadArt(card, back);
        _artMissing = sprite == null;
        _art.Texture(sprite != null ? sprite.texture : Texture2D.whiteTexture);
        _art.Tint(sprite != null ? White : back ? new Color32(240, 92, 112, 255) : new Color32(255, 247, 240, 255));
    }

    private void BuildIndicesAndFallbacks(OldMaidCard card)
    {
        var width = CardWidth * (card.IsJoker ? 0.11f : 0.185f);
        var height = CardHeight * (card.IsJoker ? 0.35f : 0.17f);
        const float left = 0.051625f, top = 0.034416667f;
        if (!OldMaidSpatialMeshGeometry.TryCreateRoundedTopology(2, 3,
            OldMaidCardArtPolicy.CornerRadiusFraction, height / width, 0, out var plate))
            throw new InvalidOperationException("Invalid index topology.");
        _indexLayers.Add(NewLayer("TopLeftPlate", Rect, plate, Plate,
            point => new Point(left + point.X * width / CardWidth, top + point.Y * height / CardHeight)));
        _indexLayers.Add(NewLayer("BottomRightPlate", Rect, plate, Plate,
            point => new Point(1 - left - point.X * width / CardWidth, 1 - top - point.Y * height / CardHeight)));
        var ink = card.IsJoker || card.Id % 2 == 0 ? new Color32(182, 56, 80, 255) : Navy;
        var label = BakeLabel("IndexStamp", OldMaidCardArtPolicy.Index(card), width, height,
            CardWidth * (card.IsJoker ? 0.068f : 0.135f), ink);
        CopyLabelMeshes(label, _indexLayers, "TopLeftGlyphs",
            vertex => new Point(left + (vertex.x + width * 0.5f) / CardWidth,
                top + (height * 0.5f - vertex.y) / CardHeight));
        // Same glyphs and UVs; rotate the entire rank+suit group by 180deg.
        CopyLabelMeshes(label, _indexLayers, "BottomRightGlyphs",
            vertex => new Point(1 - left - (vertex.x + width * 0.5f) / CardWidth,
                1 - top - (height * 0.5f - vertex.y) / CardHeight));
        label.gameObject.SetActive(false);
        BuildFallback("FrontFallbackStamp", card.IsJoker ? "★\nJOKER\n★" : OldMaidCardArtPolicy.Rank(card), _frontFallback);
        BuildFallback("BackFallbackStamp", "◇\nL\n◇", _backFallback);
    }

    private void BuildFallback(string name, string text, List<MeshLayer> target)
    {
        var label = BakeLabel(name, text, 60, 90, 24, Navy);
        CopyLabelMeshes(label, target, name + "Glyphs",
            vertex => new Point(0.5f + vertex.x / CardWidth, 0.5f - vertex.y / CardHeight));
        label.gameObject.SetActive(false);
    }

    private TextMeshProUGUI BakeLabel(string name, string text, float width, float height, float size, Color32 color)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>(),
            Il2CppType.Of<TextMeshProUGUI>());
        obj.layer = _layer;
        obj.transform.SetParent(Rect, false);
        var label = obj.GetComponent<TextMeshProUGUI>();
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        label.font = _font.font;
        label.fontSharedMaterial = _font.fontSharedMaterial;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSize = label.fontSizeMax = size;
        label.fontSizeMin = size * 0.75f;
        label.enableWordWrapping = false;
        label.richText = false;
        label.raycastTarget = false;
        label.color = color;
        label.text = text;
        // Parent and generator are active here. Keep this small stamp hidden
        // afterwards (not destroyed) so TMP retains any fallback-font material
        // references. Never destroy the native shared font material ourselves.
        obj.SetActive(true);
        label.ForceMeshUpdate(true, true);
        return label;
    }

    private void CopyLabelMeshes(TextMeshProUGUI label, List<MeshLayer> target, string name,
        Func<Vector3, Point> placement)
    {
        var parts = label.textInfo.meshInfo;
        if (parts == null || parts.Length > MaximumTextMaterials)
            throw new InvalidOperationException("Unbounded font material count.");
        var total = 0;
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = parts[partIndex];
            var count = part.vertexCount;
            if (count == 0) continue;
            total += count;
            if (count < 0 || count % 4 != 0 || total > MaximumTextVertices)
                throw new InvalidOperationException("Unbounded font mesh.");
            var points = new Point[count];
            var uv = new Vector2[count];
            var uv2 = new Vector2[count];
            var colors = new Color32[count];
            for (var index = 0; index < count; index++)
            {
                var point = placement(part.vertices[index]);
                // Auto-fit keeps the stamp within its plate. Reject rather
                // than distort a glyph if a foreign font exceeds card bounds.
                if (!Finite(point.X) || !Finite(point.Y) || point.X < 0 || point.X > 1 || point.Y < 0 || point.Y > 1)
                    throw new InvalidOperationException("Font mesh outside card.");
                points[index] = point;
                uv[index] = part.uvs0[index];
                uv2[index] = part.uvs2[index];
                colors[index] = part.colors32[index];
            }
            var indices = new int[count / 4 * 6];
            for (var index = 0; index < indices.Length; index++)
            {
                indices[index] = part.triangles[index];
                if (indices[index] < 0 || indices[index] >= count)
                    throw new InvalidOperationException("Invalid font triangle.");
            }
            var material = IsolatedFontMaterial(part.material != null ? part.material : label.fontSharedMaterial);
            target.Add(NewLayer(name + partIndex, Rect, points, uv, indices, colors, uv2,
                material, material.mainTexture));
        }
    }

    private Material IsolatedFontMaterial(Material original)
    {
        var id = original.GetInstanceID();
        if (_fontMaterials.TryGetValue(id, out var cached)) return cached;
        // A native TMP template may come from a masked panel. Retain its
        // font atlas/preset but clear mask state on an OWNED clone only.
        // At most three stamps, each with <=8 materials, exist per card.
        if (_fontMaterials.Count >= MaximumTextMaterials * 3)
            throw new InvalidOperationException("Unbounded font material cache.");
        var material = new Material(original) { name = "OldMaidSpatialFont" };
        _fontMaterials.Add(id, material);
        material.renderQueue = _renderQueue;
        foreach (var keyword in new[] { "UNITY_UI_CLIP_RECT", "UNITY_UI_ALPHACLIP", "MASK_SOFT", "MASK_HARD" })
            material.DisableKeyword(keyword);
        SetMaterialFloat(material, "_Stencil", 0);
        SetMaterialFloat(material, "_StencilComp", 8); // Always
        SetMaterialFloat(material, "_StencilOp", 0); // Keep
        SetMaterialFloat(material, "_StencilReadMask", 255);
        SetMaterialFloat(material, "_StencilWriteMask", 255);
        SetMaterialFloat(material, "_ColorMask", 15);
        SetMaterialFloat(material, "_UseUIAlphaClip", 0);
        SetMaterialFloat(material, "_CullMode", 0); // Both windings are already UV-corrected.
        if (material.HasProperty("_ClipRect"))
            material.SetVector("_ClipRect", new Vector4(-1000000, -1000000, 1000000, 1000000));
        return material;
    }

    private static void SetMaterialFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    private MeshLayer NewLayer(string name, Transform parent, OldMaidSpatialMeshTopology topology,
        Color32 color, Func<Point, Point>? placement = null)
    {
        var points = new Point[topology.Points.Length];
        var uv = new Vector2[points.Length];
        var colors = new Color32[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = placement == null ? topology.Points[index] : placement(topology.Points[index]);
            uv[index] = new Vector2(topology.Uvs[index].X, topology.Uvs[index].Y);
            colors[index] = color;
        }
        return NewLayer(name, parent, points, uv, topology.Triangles, colors, null,
            _graphicMaterial!, Texture2D.whiteTexture);
    }

    private MeshLayer NewLayer(string name, Transform parent, Point[] points, Vector2[] uv,
        int[] triangles, Color32[] colors, Vector2[]? uv2, Material material, Texture texture)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>());
        obj.layer = _layer;
        obj.transform.SetParent(parent, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.zero;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var mesh = new Mesh { name = "OldMaidSpatial-" + name };
        _ownedMeshes.Add(mesh);
        return new MeshLayer(obj.GetComponent<CanvasRenderer>(), mesh, points, uv, triangles, colors,
            uv2, material, texture);
    }

    private RectTransform NewRoot(string name, Transform parent)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>());
        obj.layer = _layer; obj.transform.SetParent(parent, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }

    private void SizeRoots(float x, float y, float width, float height)
    {
        var size = new Vector2(Math.Max(1, x + width), Math.Max(1, y + height));
        Rect.sizeDelta = _shadowRect.sizeDelta = size;
    }

    private void CacheFrame(OldMaidSpatialProjection projection, Point[] texture, float x, float y, float width, float height)
    {
        Array.Copy(texture, _lastTexture, 4); Array.Copy(projection.ShadowCorners, _lastShadow, 4);
        _lastX = x; _lastY = y; _lastWidth = width; _lastHeight = height;
        _lastFront = projection.FrontFacing; _lastOpacity = projection.ShadowOpacity; _lastBlur = projection.ShadowBlur;
        _hasFrame = true;
    }

    private static OldMaidSpatialMeshTopology Topology(int columns, int rows, float inset)
    {
        if (!OldMaidSpatialMeshGeometry.TryCreateRoundedTopology(columns, rows,
            OldMaidCardArtPolicy.CornerRadiusFraction, OldMaidCardArtPolicy.HeightToWidth, inset, out var result))
            throw new InvalidOperationException("Invalid rounded card topology.");
        return result;
    }

    private static readonly Color32 White = new(255, 255, 255, 255);
    private static bool Finite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);
    private static bool ValidViewport(float x, float y, float width, float height) =>
        Finite(x) && Finite(y) && Math.Abs(x) <= 65536 && Math.Abs(y) <= 65536
        && Finite(width) && Finite(height) && width >= 1 && height >= 1 && width <= 65536 && height <= 65536;
    private static bool ValidCorners(Point[] corners)
    {
        if (corners == null || corners.Length != 4) return false;
        foreach (var point in corners) if (!Finite(point.X) || !Finite(point.Y)) return false;
        return true;
    }
    private static bool SameCorners(Point[] left, Point[] right)
    {
        for (var index = 0; index < 4; index++) if (left[index] != right[index]) return false;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Own meshes are released before the presenter releases shared art.
        // Canvas/TMP objects and their internal resources follow root lifetime.
        try { Hide(); } catch { /* Native scene may already have torn down. */ }
        try { if (Rect != null) DestroyOwned(Rect.gameObject); } catch { }
        try { if (_shadowRect != null) DestroyOwned(_shadowRect.gameObject); } catch { }
        foreach (var mesh in _ownedMeshes) DestroyOwned(mesh);
        foreach (var material in _fontMaterials.Values) DestroyOwned(material);
        if (_graphicMaterial != null) DestroyOwned(_graphicMaterial);
        _graphicMaterial = null;
        _ownedMeshes.Clear();
        _fontMaterials.Clear();
        _faceLayers.Clear(); _indexLayers.Clear(); _frontFallback.Clear(); _backFallback.Clear();
    }

    private static void DestroyOwned(UnityEngine.Object item)
    {
        try { if (item != null) UnityEngine.Object.Destroy(item); }
        catch { /* Best-effort independent cleanup; never abandon later items. */ }
    }

    private sealed class MeshLayer
    {
        private readonly CanvasRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly Point[] _points;
        private readonly Il2CppStructArray<Vector3> _vertices;
        private readonly Il2CppStructArray<Color32> _colors;
        private readonly Vector2[]? _originalUv2;
        private readonly Il2CppStructArray<Vector2>? _uv2;
        private Color32 _tint;

        internal MeshLayer(CanvasRenderer renderer, Mesh mesh, Point[] points, Vector2[] uv,
            int[] triangles, Color32[] colors, Vector2[]? uv2, Material material, Texture texture)
        {
            _renderer = renderer; _mesh = mesh; _points = points; _originalUv2 = uv2;
            _vertices = new Il2CppStructArray<Vector3>(points.Length);
            _colors = new Il2CppStructArray<Color32>(colors);
            _tint = colors[0];
            mesh.MarkDynamic();
            mesh.vertices = _vertices;
            mesh.uv = new Il2CppStructArray<Vector2>(uv);
            mesh.colors32 = _colors;
            mesh.triangles = new Il2CppStructArray<int>(triangles);
            if (uv2 != null) { _uv2 = new Il2CppStructArray<Vector2>(uv2); mesh.uv2 = _uv2; }
            renderer.materialCount = 1;
            renderer.SetMaterial(material, 0);
            renderer.SetTexture(texture);
            renderer.SetMesh(mesh);
        }

        internal void Texture(Texture texture) => _renderer.SetTexture(texture);
        internal void Visible(bool value) => _renderer.gameObject.SetActive(value);
        internal void Tint(Color32 color)
        {
            if (_tint.r == color.r && _tint.g == color.g && _tint.b == color.b && _tint.a == color.a) return;
            _tint = color;
            for (var index = 0; index < _colors.Length; index++) _colors[index] = color;
            _mesh.colors32 = _colors;
        }

        internal void Project(OldMaidProjectiveMap map, float workX, float workY, float workHeight, float spread = 0)
        {
            if (!map.TryMap(0.5f, 0.5f, out var center)) throw new InvalidOperationException("Invalid projected center.");
            var textScale = 1f;
            if (_uv2 != null && map.TryMap(0f, 0.5f, out var left) && map.TryMap(1f, 0.5f, out var right))
                textScale = Math.Clamp(Point.Distance(left, right) / CardWidth, 0.001f, 64f);
            for (var index = 0; index < _points.Length; index++)
            {
                var point = _points[index];
                if (!map.TryMap(point.X, point.Y, out var screen)) throw new InvalidOperationException("Invalid projected vertex.");
                if (spread > 0)
                {
                    var delta = screen - center;
                    var length = delta.Length();
                    if (length > 0.001f) screen += delta * (spread / length);
                }
                _vertices[index] = new Vector3(workX + screen.X, workY + workHeight - screen.Y, 0);
                if (_uv2 != null && _originalUv2 != null)
                {
                    var source = _originalUv2[index];
                    _uv2[index] = new Vector2(source.x, source.y * textScale);
                }
            }
            _mesh.vertices = _vertices;
            if (_uv2 != null) _mesh.uv2 = _uv2;
            _mesh.RecalculateBounds();
            _renderer.SetMesh(_mesh);
        }
    }
}
