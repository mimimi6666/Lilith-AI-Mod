using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LilithTextInjector;

// One reusable face per logical card, including flight, pair reveal and floor.
// This class has no input, rules, native character or dialogue authority.
// Every graphic here is non-interactive except the explicitly separate factory
// for a stationary hit surface, which the presenter positions independently.
internal sealed class OldMaidCardVisuals
{
    private static readonly Dictionary<string, Sprite?> Art = new(StringComparer.Ordinal);
    private static readonly List<Sprite> OwnedSprites = new();
    private static readonly List<Texture2D> OwnedTextures = new();
    private static readonly float[] HaloPadding = { 0.08125f, 0.05625f, 0.03125f, 0.015625f };
    private static Sprite? _rounded;
    private static bool _warningLogged;

    internal RectTransform Rect { get; }
    private readonly Image _outer, _keyline, _mask, _art, _shadow;
    private readonly GameObject _glow;
    private readonly RectTransform[] _halos;
    private readonly TextMeshProUGUI _fallback, _topLabel, _bottomLabel;
    private readonly RectTransform _topIndex, _bottomIndex;
    private int _cardId = -1;
    private bool _back, _joker;
    private float _width, _height;
    private float _tilt;
    private Color _artBaseColor = Color.white;

    private OldMaidCardVisuals(Transform parent, int layer, TextMeshProUGUI font)
    {
        Rect = NewRect("CardVisual", parent, layer);
        Rect.anchorMin = Rect.anchorMax = Vector2.zero;
        Rect.pivot = new Vector2(0.5f, 0.5f);

        _shadow = RoundedImage("CardShadow", Rect, layer, new Color(0.04f, 0.06f, 0.12f, 0.19f));
        _glow = NewRect("SelectionGlow", Rect, layer).gameObject;
        Stretch(_glow.GetComponent<RectTransform>(), 0);
        _halos = new RectTransform[4];
        var alpha = new[] { 0.07f, 0.13f, 0.25f, 0.62f };
        for (var i = 0; i < _halos.Length; i++)
            _halos[i] = RoundedImage("Halo" + i, _glow.transform, layer,
                new Color(0.80f, 0.94f, 1f, alpha[i])).rectTransform;
        _glow.SetActive(false);

        _outer = RoundedImage("NavyOutline", Rect, layer, new Color(0.067f, 0.098f, 0.18f, 1));
        _keyline = RoundedImage("SoftWhiteKeyline", Rect, layer, new Color(0.98f, 0.97f, 0.95f, 1));
        _mask = RoundedImage("RoundedFaceMask", Rect, layer, Color.white);
        var stencil = _mask.gameObject.AddComponent<Mask>();
        stencil.showMaskGraphic = false;
        _art = NewImage("CardArtwork", _mask.transform, layer);
        _art.color = Color.white;
        _fallback = NewLabel("ProceduralFallback", _mask.transform, layer, font);
        Stretch(_fallback.rectTransform, 0);
        _fallback.color = new Color(0.22f, 0.07f, 0.12f, 1);
        _topIndex = NewIndex("TopLeftIndex", _mask.transform, layer, font, out _topLabel);
        _bottomIndex = NewIndex("BottomRightIndex", _mask.transform, layer, font, out _bottomLabel);
        SetSize(160, 240);
    }

    internal static OldMaidCardVisuals Create(Transform parent, int layer, TextMeshProUGUI font) =>
        new(parent, layer, font);

    internal static Image CreateHitSurface(Transform parent, int layer)
    {
        var hit = RoundedImage("StableCardHitSurface", parent, layer, new Color(1, 1, 1, 0.001f));
        hit.alphaHitTestMinimumThreshold = 0.1f;
        hit.raycastTarget = true;
        return hit;
    }

    internal void SetCard(OldMaidCard card, bool back)
    {
        if (_cardId == card.Id && _back == back) return;
        _cardId = card.Id;
        _back = back;
        _joker = card.IsJoker;
        var sprite = LoadArt(card, back);
        _art.sprite = sprite;
        _artBaseColor = sprite != null ? Color.white : back
            ? new Color(0.94f, 0.36f, 0.44f, 1) : new Color(1, 0.97f, 0.94f, 1);
        ApplyTilt();
        _fallback.gameObject.SetActive(sprite == null);
        _fallback.text = back ? "◇\nL\n◇" : card.IsJoker ? "★\nJOKER\n★" : OldMaidCardArtPolicy.Rank(card);
        _topIndex.gameObject.SetActive(!back);
        _bottomIndex.gameObject.SetActive(!back);
        var label = OldMaidCardArtPolicy.Index(card);
        _topLabel.text = _bottomLabel.text = label;
        var ink = card.IsJoker || card.Id % 2 == 0
            ? new Color(0.714f, 0.22f, 0.314f, 1) : new Color(0.067f, 0.098f, 0.18f, 1);
        _topLabel.color = _bottomLabel.color = ink;
        LayoutIndices();
    }

    internal void SetGlow(bool visible) => _glow.SetActive(visible);

    // Overlay canvases are orthographic: +pitch and -pitch have the same
    // silhouette. A restrained cool tint and signed shadow offset distinguish
    // the two directions without editing the artwork or hiding its face.
    internal void SetTilt(float pitchDegrees)
    {
        if (float.IsNaN(pitchDegrees) || float.IsInfinity(pitchDegrees)) return;
        var tilt = Math.Clamp(pitchDegrees, -70f, 70f);
        if (_tilt == tilt) return;
        _tilt = tilt;
        ApplyTilt();
    }

    private void ApplyTilt()
    {
        var amount = Math.Abs(_tilt) / 70f;
        var shade = amount * (_tilt < 0 ? 0.075f : 0.025f);
        _art.color = _artBaseColor * new Color(1 - shade, 1 - shade * 0.55f, 1, 1);
        _shadow.rectTransform.anchoredPosition = new Vector2(_width * 0.0125f,
            -_height * (0.0125f + _tilt / 70f * 0.018f));
        _shadow.color = new Color(0.04f, 0.06f, 0.12f, 0.19f + amount * 0.05f);
    }

    internal void SetSize(float width, float height)
    {
        if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0
            || float.IsNaN(height) || float.IsInfinity(height) || height <= 0) return;
        if (_width == width && _height == height) return;
        _width = width;
        _height = height;
        Rect.sizeDelta = new Vector2(width, height);
        Stretch(_shadow.rectTransform, width * 0.009f);
        ApplyTilt();
        Stretch(_outer.rectTransform, 0);
        Stretch(_keyline.rectTransform, -width * 0.00625f);
        Stretch(_mask.rectTransform, -width * 0.015625f);
        for (var i = 0; i < _halos.Length; i++) Stretch(_halos[i], width * HaloPadding[i]);
        _fallback.fontSizeMin = Math.Max(5, width * 0.07f);
        _fallback.fontSizeMax = width * 0.25f;
        LayoutIndices();
    }

    private void LayoutIndices()
    {
        var width = _width * (_joker ? 0.11f : 0.185f);
        var height = _height * (_joker ? 0.35f : 0.17f);
        PlaceIndex(_topIndex, false, width, height);
        PlaceIndex(_bottomIndex, true, width, height);
        var fontSize = _width * (_joker ? 0.068f : 0.135f);
        _topLabel.fontSizeMin = _bottomLabel.fontSizeMin = fontSize * 0.75f;
        _topLabel.fontSizeMax = _bottomLabel.fontSizeMax = fontSize;
    }

    private void PlaceIndex(RectTransform rect, bool bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = bottom ? new Vector2(1, 0) : new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = bottom ? new Vector2(-_width * 0.036f, _height * 0.024f)
            : new Vector2(_width * 0.036f, -_height * 0.024f);
        // Rotate the entire same rank+suit group. Never reverse the glyphs.
        rect.localRotation = Quaternion.Euler(0, 0, bottom ? 180 : 0);
    }

    // The presenter destroys its visual/hit roots first, then releases this
    // session's shared textures and sprites. Reopening obtains a fresh cache.
    internal static void DisposeSharedAssets()
    {
        foreach (var sprite in OwnedSprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
        foreach (var texture in OwnedTextures) if (texture != null) UnityEngine.Object.Destroy(texture);
        OwnedSprites.Clear();
        OwnedTextures.Clear();
        Art.Clear();
        _rounded = null;
        _warningLogged = false;
    }

    internal static Sprite? LoadArt(OldMaidCard card, bool back)
    {
        var name = OldMaidCardArtPolicy.FileName(card, back);
        if (Art.TryGetValue(name, out var cached)) return cached;
        Sprite? sprite = null;
        Texture2D? texture = null;
        try
        {
            var baseDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)
                ?? throw new InvalidDataException();
            var directory = Path.Combine(baseDirectory, "OldMaidCards");
            var group = Path.Combine(directory, back ? "back" : "faces");
            var path = Path.Combine(group, name);
            // Reject redirected package components; this is a fixed read-only
            // art folder, not a general image/file loader or user manifest.
            foreach (var component in new[] { directory, group, path })
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 33 || stream.Length > OldMaidCardArtPolicy.MaximumEncodedBytes)
                throw new InvalidDataException();
            var header = new byte[33];
            ReadExactly(stream, header, 0, header.Length);
            if (!OldMaidCardArtPolicy.TryReadPngDimensions(header, out var width, out var height))
                throw new InvalidDataException();
            var bytes = new byte[(int)stream.Length];
            Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
            ReadExactly(stream, bytes, header.Length, bytes.Length - header.Length);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: true)
                || texture.width != width || texture.height != height)
                throw new InvalidDataException();
            texture.name = "OldMaidArtTexture-" + name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "OldMaidArtSprite-" + name;
            OwnedTextures.Add(texture);
            OwnedSprites.Add(sprite);
        }
        catch (Exception error)
        {
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            sprite = null;
            if (!_warningLogged)
            {
                Plugin.PluginLog.LogWarning("Old Maid optional card art unavailable; using local fallback: " + error.GetType().Name);
                _warningLogged = true;
            }
        }
        Art[name] = sprite;
        return sprite;
    }

    private static void ReadExactly(Stream stream, byte[] bytes, int offset, int count)
    {
        while (count > 0)
        {
            var read = stream.Read(bytes, offset, count);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
            count -= read;
        }
    }

    private static Sprite RoundedSprite()
    {
        if (_rounded != null) return _rounded;
        const int width = 192, height = 288;
        var radius = width * OldMaidCardArtPolicy.CornerRadiusFraction;
        var pixels = new Color32[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = Math.Max(0, radius - Math.Min(x + 0.5f, width - x - 0.5f));
            var dy = Math.Max(0, radius - Math.Min(y + 0.5f, height - y - 0.5f));
            var alpha = Math.Clamp(radius + 0.5f - (float)Math.Sqrt(dx * dx + dy * dy), 0, 1);
            pixels[y * width + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        OwnedTextures.Add(texture);
        texture.name = "OldMaidRoundedSilhouette";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(pixels);
        // Readability is needed only for the separate alpha-tested hit surface.
        texture.Apply(false, false);
        _rounded = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        OwnedSprites.Add(_rounded);
        return _rounded;
    }

    private static RectTransform NewIndex(string name, Transform parent, int layer,
        TextMeshProUGUI font, out TextMeshProUGUI label)
    {
        var plate = RoundedImage(name, parent, layer, new Color(1f, 0.985f, 0.96f, 0.88f));
        label = NewLabel("RankAndSuit", plate.transform, layer, font);
        Stretch(label.rectTransform, 0);
        return plate.rectTransform;
    }

    private static TextMeshProUGUI NewLabel(string name, Transform parent, int layer, TextMeshProUGUI font)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>(),
            Il2CppType.Of<TextMeshProUGUI>());
        obj.layer = layer;
        obj.transform.SetParent(parent, false);
        var label = obj.GetComponent<TextMeshProUGUI>();
        label.font = font.font;
        label.fontSharedMaterial = font.fontSharedMaterial;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.enableWordWrapping = false;
        label.richText = false;
        label.raycastTarget = false;
        return label;
    }

    private static Image RoundedImage(string name, Transform parent, int layer, Color color)
    {
        var image = NewImage(name, parent, layer);
        image.sprite = RoundedSprite();
        image.color = color;
        return image;
    }

    private static Image NewImage(string name, Transform parent, int layer)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>(),
            Il2CppType.Of<Image>());
        obj.layer = layer;
        obj.transform.SetParent(parent, false);
        var image = obj.GetComponent<Image>();
        image.raycastTarget = false;
        Stretch(image.rectTransform, 0);
        return image;
    }

    private static RectTransform NewRect(string name, Transform parent, int layer)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>());
        obj.layer = layer;
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-padding, -padding);
        rect.offsetMax = new Vector2(padding, padding);
    }
}
