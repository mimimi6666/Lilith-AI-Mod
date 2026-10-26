using System;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace LilithTextInjector;

internal static class UnityDesktopArtifactPresenter
{
    internal const string EmbeddedIconResource =
        "LilithTextInjector.Assets.lilith-desktop-artifact-icon.png";
    internal static readonly TimeSpan SessionLease = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan SessionRenewalLead = TimeSpan.FromMinutes(2);
    internal const float DoubleClickSeconds = 0.42f;

    private static readonly DesktopArtifactPreviewHost Host = new();
    private static readonly string RuntimeSessionId =
        "unity-desktop-artifact-" + Guid.NewGuid().ToString("N");

    private static GameObject? _root;
    private static RectTransform? _iconRect;
    private static CanvasGroup? _canvasGroup;
    private static Texture2D? _iconTexture;
    private static Sprite? _iconSprite;
    private static DesktopArtifactPreviewSession? _session;
    private static float _lastPrimaryClickAt = -10f;
    private static float _transitionStartedAt = -1f;
    private static long _inputSequence;
    private static bool _creationFailureLogged;
    private static bool _readyLogged;

    internal static DesktopArtifactPreviewHost PreviewHost => Host;

    internal static void Update(bool enabled)
    {
        var now = DateTimeOffset.UtcNow;
        if (!enabled)
        {
            Host.Reconcile(false, null, now);
            DestroySurface();
            _readyLogged = false;
            return;
        }

        try
        {
            EnsureSession(now);
            if (_session == null)
                return;
            if (Host.Session == null
                || !string.Equals(
                    Host.Session.Fingerprint,
                    _session.Fingerprint,
                    StringComparison.Ordinal))
            {
                Host.Reconcile(true, _session, now);
            }
            if (_root == null && !TryCreateSurface())
                return;
            Host.Tick(now);
            HandlePointer(now);
            AnimateTransition();
            if (!_readyLogged)
            {
                _readyLogged = true;
                Plugin.PluginLog.LogInfo(
                    "D-030 Unity fake application icon is ready in "
                    + "Default-Off source preview mode; real desktop "
                    + "file=0, launch=0, provider=0, capability=0.");
            }
        }
        catch (Exception exception)
        {
            Host.Reconcile(false, null, now);
            DestroySurface();
            if (!_creationFailureLogged)
            {
                _creationFailureLogged = true;
                Plugin.PluginLog.LogWarning(
                    "D-030 Unity fake application icon stopped fail-closed: "
                    + exception.GetType().Name
                    + "; real desktop file=0, launch=0, provider=0, "
                    + "capability=0.");
            }
        }
    }

    private static void EnsureSession(DateTimeOffset now)
    {
        if (_session != null
            && now < _session.LeaseExpiresAt - SessionRenewalLead)
        {
            return;
        }
        var runtime = new DesktopArtifactRuntimeIdentity(
            "lilith-unity-process-" + Environment.ProcessId,
            runtimeGeneration: 1,
            RuntimeSessionId);
        _session = new DesktopArtifactPreviewSession(
            "lilith-desktop-artifact-primary",
            runtime,
            now,
            now.Add(SessionLease),
            "lilith-control-center-icon-v1");
    }

    private static bool TryCreateSurface()
    {
        try
        {
            _root = new GameObject(
                "LilithDesktopArtifactSourcePreview",
                Il2CppType.Of<RectTransform>(),
                Il2CppType.Of<Canvas>(),
                Il2CppType.Of<CanvasScaler>(),
                Il2CppType.Of<GraphicRaycaster>(),
                Il2CppType.Of<CanvasGroup>());
            _root.transform.SetParent(null, false);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 900;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGroup = _root.GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var iconObject = new GameObject(
                "LilithFakeApplicationIcon",
                Il2CppType.Of<RectTransform>(),
                Il2CppType.Of<CanvasRenderer>(),
                Il2CppType.Of<Image>());
            iconObject.transform.SetParent(_root.transform, false);
            _iconRect = iconObject.GetComponent<RectTransform>();
            _iconRect.anchorMin = Vector2.one;
            _iconRect.anchorMax = Vector2.one;
            _iconRect.pivot = Vector2.one;
            _iconRect.sizeDelta = new Vector2(112f, 112f);
            _iconRect.anchoredPosition = new Vector2(-46f, -82f);
            var image = iconObject.GetComponent<Image>();
            image.sprite = LoadEmbeddedIcon();
            image.preserveAspect = true;
            image.raycastTarget = true;
            image.color = Color.white;
            return true;
        }
        catch
        {
            DestroySurface();
            throw;
        }
    }

    private static Sprite LoadEmbeddedIcon()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(EmbeddedIconResource)
            ?? throw new FileNotFoundException(EmbeddedIconResource);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        _iconTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(
                _iconTexture,
                buffer.ToArray(),
                markNonReadable: true))
        {
            throw new InvalidDataException("Embedded desktop icon is invalid.");
        }
        _iconTexture.name = "LilithDesktopArtifactIconTexture";
        _iconSprite = Sprite.Create(
            _iconTexture,
            new Rect(0f, 0f, _iconTexture.width, _iconTexture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        _iconSprite.name = "LilithDesktopArtifactIconSprite";
        return _iconSprite;
    }

    private static void HandlePointer(DateTimeOffset now)
    {
        if (_iconRect == null || !Input.GetMouseButtonDown(0))
            return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(
                _iconRect,
                Input.mousePosition,
                null))
        {
            return;
        }
        checked
        {
            _inputSequence++;
        }
        var current = Time.unscaledTime;
        var doubleClick = current - _lastPrimaryClickAt <= DoubleClickSeconds;
        _lastPrimaryClickAt = current;
        var session = _session!;
        var request = new DesktopArtifactPreviewRequest(
            session.SurfaceInstanceId,
            session.RuntimeIdentity,
            doubleClick
                ? DesktopArtifactInputKind.PrimaryDoubleClick
                : DesktopArtifactInputKind.PrimarySingleClick,
            _inputSequence,
            now,
            now.AddMilliseconds(700));
        var receipt = Host.ObserveInput(request, now);
        if (receipt.AllowsTransitionPreview)
        {
            _transitionStartedAt = current;
            Plugin.PluginLog.LogInfo(
                "D-030 desktop artifact double-click accepted for a "
                + "presentation-only outward transition; launch=0, "
                + "capability=0.");
        }
    }

    private static void AnimateTransition()
    {
        if (_iconRect == null || _canvasGroup == null)
            return;
        if (Host.Status != DesktopArtifactPreviewStatus.TransitionPreview
            || _transitionStartedAt < 0f)
        {
            _iconRect.localScale = Vector3.one;
            _canvasGroup.alpha = 1f;
            _transitionStartedAt = -1f;
            return;
        }
        var progress = Math.Clamp(
            (Time.unscaledTime - _transitionStartedAt)
            / (float)DesktopArtifactPreviewHost.TransitionDuration.TotalSeconds,
            0f,
            1f);
        var eased = 1f - (float)Math.Pow(1f - progress, 3f);
        var scale = 1f + eased * 8f;
        _iconRect.localScale = new Vector3(scale, scale, 1f);
        _canvasGroup.alpha = 1f - eased;
    }

    private static void DestroySurface()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        if (_iconSprite != null)
            UnityEngine.Object.Destroy(_iconSprite);
        if (_iconTexture != null)
            UnityEngine.Object.Destroy(_iconTexture);
        _root = null;
        _iconRect = null;
        _canvasGroup = null;
        _iconSprite = null;
        _iconTexture = null;
        _session = null;
        _lastPrimaryClickAt = -10f;
        _transitionStartedAt = -1f;
        _inputSequence = 0;
    }
}
