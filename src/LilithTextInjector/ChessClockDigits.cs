using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace LilithTextInjector;

// One small owned mesh for both oblique displays. Dynamic seven-segment digits
// follow the shell face rather than facing the player or using rotated labels.
internal sealed class ChessClockDigits : IDisposable
{
    private readonly Mesh mesh = new() { name = "ChessClockDigits" };
    private readonly Material material = new(Graphic.defaultGraphicMaterial);
    private readonly CanvasRenderer renderer;
    private readonly RectTransform rect;
    private string last = "";
    private static readonly int[] Masks = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
    private static readonly Rect[] Segments = {
        new(0.14f,0.86f,0.60f,0.10f), new(0.72f,0.51f,0.10f,0.37f), new(0.72f,0.12f,0.10f,0.37f),
        new(0.14f,0.04f,0.60f,0.10f), new(0.06f,0.12f,0.10f,0.37f), new(0.06f,0.51f,0.10f,0.37f), new(0.14f,0.45f,0.60f,0.10f) };
    internal ChessClockDigits(Transform parent, int layer)
    {
        var obj = new GameObject("ChessClockNumbers", Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>());
        obj.layer = layer; obj.transform.SetParent(parent, false);
        rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        renderer = obj.GetComponent<CanvasRenderer>();
        renderer.materialCount = 1; renderer.SetMaterial(material, 0); renderer.SetTexture(Texture2D.whiteTexture);
        mesh.MarkDynamic();
    }

    internal void Render(Rect area, string near, string far)
    {
        rect.anchoredPosition = area.position; rect.sizeDelta = new Vector2(1254, 1254);
        rect.localScale = new Vector3(area.width / 1254, area.height / 1254, 1);
        var value = near + far;
        if (value == last) { renderer.SetMesh(mesh); return; }
        last = value;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        Digits(near, false); Digits(far, true);
        mesh.Clear();
        mesh.vertices = new Il2CppStructArray<Vector3>(vertices.ToArray());
        var colors = new Color32[vertices.Count]; var uv = new Vector2[vertices.Count];
        for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(25, 36, 69, 255);
        mesh.colors32 = new Il2CppStructArray<Color32>(colors);
        mesh.uv = new Il2CppStructArray<Vector2>(uv);
        mesh.triangles = new Il2CppStructArray<int>(triangles.ToArray());
        mesh.RecalculateBounds(); renderer.SetMesh(mesh);

        void Digits(string text, bool distant)
        {
            for (var i = 0; i < text.Length && i < 5; i++)
            {
                if (text[i] == ':') { Quad(new Rect(0.49f,0.39f,0.02f,0.05f), distant); Quad(new Rect(0.49f,0.57f,0.02f,0.05f), distant); continue; }
                if (text[i] < '0' || text[i] > '9') continue;
                var x = i < 2 ? 0.10f + i * 0.17f : 0.55f + (i - 3) * 0.17f;
                for (var s = 0; s < 7; s++) if ((Masks[text[i] - '0'] & (1 << s)) != 0)
                { var segment = Segments[s]; Quad(new Rect(x + segment.x * 0.16f, 0.25f + segment.y * 0.50f, segment.width * 0.16f, segment.height * 0.50f), distant); }
            }
        }
        void Quad(Rect r, bool distant)
        {
            var start = vertices.Count;
            vertices.Add(Map(r.xMin, r.yMin, distant)); vertices.Add(Map(r.xMin, r.yMax, distant));
            vertices.Add(Map(r.xMax, r.yMax, distant)); vertices.Add(Map(r.xMax, r.yMin, distant));
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
    }
    private static Vector3 Map(float x, float y, bool distant)
    {
        var bottomLeft = distant ? new Vector2(868, 648) : new Vector2(438, 950);
        var bottomRight = distant ? new Vector2(1138, 445) : new Vector2(776, 708);
        var topLeft = distant ? new Vector2(834, 482) : new Vector2(400, 775);
        var topRight = distant ? new Vector2(1101, 291) : new Vector2(738, 538);
        var p = Vector2.Lerp(Vector2.Lerp(bottomLeft, bottomRight, x), Vector2.Lerp(topLeft, topRight, x), y);
        return new Vector3(p.x, 1254 - p.y, 0);
    }
    public void Dispose() { UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(material); if (rect != null) UnityEngine.Object.Destroy(rect.gameObject); }
}
