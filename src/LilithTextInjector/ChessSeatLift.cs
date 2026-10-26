using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Spine.Unity;
using UnityEngine;

namespace LilithTextInjector;

// CharacterController and SkeletonAnimation share a native GameObject. Apply
// an affine effect to newly generated vertices, NOT to transforms or bones.
// Spine rebuilds original geometry after removal; no camera/physics changes.
internal sealed class ChessSeatLift : IDisposable
{
    private readonly global::CharacterController character;
    private readonly SkeletonAnimation skeleton;
    private readonly UiOffset[] ui = { new(), new() };
    private ChessLayout? layout;
    private float screenHeight, workX, workY, factor = 1;
    private Vector2 seatedLow, seatedHigh;
    private bool seatedBoundsCaptured;
    private Vector3 localOffset;
    private bool disposed;
    internal bool Failed { get; private set; }
    internal Rect VisualRect { get; private set; }
    internal static ChessSeatLift? Active { get; private set; }
    private static bool available;

    internal ChessSeatLift(global::CharacterController owner)
    {
        character = owner;
        skeleton = owner.SkeletonAnimationInternal;
        if (!available || skeleton == null || skeleton.meshRenderer == null || skeleton.meshFilter == null)
            throw new InvalidOperationException("Chess character display unavailable.");
        Active = this;
    }

    internal static void Install(Harmony harmony)
    {
        try
        {
            // The native event uses a non-blittable value-type argument, which
            // this IL2CPP delegate bridge cannot marshal. Hook the following
            // mesh-upload seam instead, limited to this exact generator.
            harmony.Patch(AccessTools.Method(typeof(MeshGenerator), nameof(MeshGenerator.FillVertexData))
                ?? throw new MissingMethodException("MeshGenerator.FillVertexData"),
                prefix: new HarmonyMethod(typeof(ChessSeatLift), nameof(BeforeUpload)));
            foreach (var type in new[] { typeof(DialogueBubbleUI), typeof(PlayerLineController) })
                harmony.Patch(AccessTools.Method(type, "LateUpdate") ?? throw new MissingMethodException(type.Name, "LateUpdate"),
                    prefix: new HarmonyMethod(typeof(ChessSeatLift), nameof(BeforeUi)),
                    postfix: new HarmonyMethod(typeof(ChessSeatLift), nameof(AfterUi)));
            available = true;
        }
        catch (Exception error) { Plugin.PluginLog.LogWarning("Chess display installation: " + error.GetType().Name); }
    }

    internal void Fit(ChessLayout value, float height, float bottom, float left)
    {
        if (disposed || Failed) throw new InvalidOperationException("Chess display lease unavailable.");
        var changed = layout == null || Math.Abs(layout.Height - value.Height) > 0.5f || Math.Abs(layout.X - value.X) > 0.5f
            || Math.Abs(layout.Y - value.Y) > 0.5f || Math.Abs(screenHeight - height) > 0.5f
            || Math.Abs(workY - bottom) > 0.5f || Math.Abs(workX - left) > 0.5f;
        layout = value; screenHeight = height; workY = bottom; workX = left;
        if (changed) skeleton.LateUpdateMesh(); // geometry only; never advance animation
        if (Failed) throw new InvalidOperationException("Chess display effect failed.");
    }

    private static void BeforeUpload(MeshGenerator __instance)
    {
        var active = Active;
        if (active == null || __instance == null || active.skeleton == null || active.skeleton.meshGenerator == null
            || active.skeleton.meshGenerator.Pointer != __instance.Pointer) return;
        active.Process(__instance);
    }

    private void Process(MeshGenerator generator)
    {
        if (disposed || layout == null || Failed) return;
        try
        {
            if (character == null || character.SkeletonAnimationInternal != skeleton || !OldMaidNativeBridge.IsSnackActive)
                throw new InvalidOperationException("Chess display owner changed.");
            var count = generator.VertexCount; var vertices = generator.vertexBuffer.Items;
            if (count == 0) return;
            if (count < 0 || count > 65536 || vertices == null || vertices.Length < count || generator == null)
                throw new InvalidOperationException("Chess geometry unsupported.");
            var camera = character._mainCamera;
            if (camera == null || !camera.orthographic) throw new InvalidOperationException("Chess projection unavailable.");
            var low = generator.meshBoundsMin; var high = generator.meshBoundsMax;
            if (!seatedBoundsCaptured) { seatedLow = low; seatedHigh = high; seatedBoundsCaptured = true; }
            var tr = skeleton.transform;
            // Freeze the seated reference bounds, so gestures/breathing do not
            // move the clock, board or native dialogue/options anchors.
            var p0 = camera.WorldToScreenPoint(tr.TransformPoint(new Vector3(seatedLow.x, seatedLow.y, 0)));
            var p1 = camera.WorldToScreenPoint(tr.TransformPoint(new Vector3(seatedHigh.x, seatedHigh.y, 0)));
            var height = Math.Abs(p1.y - p0.y);
            var target = workY + layout.SeatBottom;
            factor = ChessDisplayMath.Scale(height, layout.SeatAvailableHeight);
            var scaledBottom = camera.WorldToScreenPoint(tr.TransformPoint(new Vector3((seatedLow.x + seatedHigh.x) * 0.5f * factor, seatedLow.y * factor, 0)));
            var delta = camera.ScreenToWorldPoint(new Vector3(workX + layout.X + layout.Width * 0.5f, target, scaledBottom.z)) - camera.ScreenToWorldPoint(scaledBottom);
            localOffset = tr.InverseTransformVector(delta);
            if (!float.IsFinite(localOffset.y) || !float.IsFinite(localOffset.x)) throw new InvalidOperationException("Chess offset invalid.");
            for (var i = 0; i < count; i++)
            {
                var v = vertices[i];
                vertices[i] = new Vector3(v.x * factor + localOffset.x, v.y * factor + localOffset.y, v.z);
            }
            generator.meshBoundsMin = low * factor + new Vector2(localOffset.x, localOffset.y);
            generator.meshBoundsMax = high * factor + new Vector2(localOffset.x, localOffset.y);
            var visibleLow = camera.WorldToScreenPoint(tr.TransformPoint(new Vector3(generator.meshBoundsMin.x, generator.meshBoundsMin.y, 0)));
            var visibleHigh = camera.WorldToScreenPoint(tr.TransformPoint(new Vector3(generator.meshBoundsMax.x, generator.meshBoundsMax.y, 0)));
            VisualRect = Rect.MinMaxRect(Math.Min(visibleLow.x, visibleHigh.x), Math.Min(visibleLow.y, visibleHigh.y),
                Math.Max(visibleLow.x, visibleHigh.x), Math.Max(visibleLow.y, visibleHigh.y));
        }
        catch (Exception) { Failed = true; }
    }

    private static void BeforeUi(Component __instance)
    {
        var active = Active;
        if (active == null) return;
        try { active.Surface(__instance)?.Restore(); } catch (Exception) { active.Failed = true; }
    }
    private static void AfterUi(Component __instance)
    {
        var active = Active;
        if (active == null || active.disposed || active.Failed || active.layout == null) return;
        try
        {
            var surface = active.Surface(__instance);
            if (surface == null || surface.Rect == null) return;
            var tr = active.skeleton.transform; var camera = active.character._mainCamera;
            var old = surface.Rect.position; var local = tr.InverseTransformPoint(old);
            var moved = tr.TransformPoint(new Vector3(local.x * active.factor + active.localOffset.x, local.y * active.factor + active.localOffset.y, local.z));
            var pixel = camera.WorldToScreenPoint(moved);
            pixel.y = Math.Clamp(pixel.y, active.workY + 40, active.workY + active.screenHeight - 40);
            surface.Apply(camera.ScreenToWorldPoint(pixel));
        }
        catch (Exception) { active.Failed = true; }
    }
    private UiOffset? Surface(Component component)
    {
        if (character == null || component == null || !component.transform.IsChildOf(character.transform)) return null;
        var index = component.TryCast<DialogueBubbleUI>() != null ? 0 : component.TryCast<PlayerLineController>() != null ? 1 : -1;
        if (index < 0) return null;
        var rect = component.GetComponent<RectTransform>();
        if (ui[index].Rect != rect) { ui[index].Restore(); ui[index].Rect = rect; }
        return ui[index];
    }
    private sealed class UiOffset
    {
        internal RectTransform? Rect;
        private Vector3 original, expected;
        private bool owned;
        internal void Apply(Vector3 position)
        {
            original = Rect!.position; expected = position; owned = true; Rect.position = position;
        }
        internal void Restore()
        {
            if (owned && Rect != null && (Rect.position - expected).sqrMagnitude < 0.000001f) Rect.position = original;
            owned = false;
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(Active, this)) Active = null;
        foreach (var surface in ui) surface.Restore();
        if (skeleton != null && skeleton.isActiveAndEnabled) skeleton.LateUpdateMesh();
    }
}
