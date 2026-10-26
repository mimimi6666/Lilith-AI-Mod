using System;

namespace LilithTextInjector;

internal readonly struct ConversationHistoryViewportLayout
{
    internal const float MaximumContentWidth = 1120f;
    internal const float MaximumContentFraction = 0.82f;
    internal const float MaximumCloseButtonSize = 44f;
    internal const float CloseButtonCenterY = 0.9325f;
    private const float FallbackCanvasWidth = 1920f;

    internal float ContentLeft { get; }
    internal float ContentRight { get; }
    internal float TitleRight { get; }
    internal float NewerButtonRight { get; }
    internal float OlderButtonLeft { get; }
    internal float CloseButtonLeft { get; }
    internal float CloseButtonSize { get; }
    internal float CloseButtonFontSize => CloseButtonSize * (24f / MaximumCloseButtonSize);
    internal float FooterLeft { get; }
    internal float FooterRight { get; }

    private ConversationHistoryViewportLayout(
        float contentLeft,
        float contentRight,
        float titleRight,
        float newerButtonRight,
        float olderButtonLeft,
        float closeButtonLeft,
        float closeButtonSize,
        float footerLeft,
        float footerRight)
    {
        ContentLeft = contentLeft;
        ContentRight = contentRight;
        TitleRight = titleRight;
        NewerButtonRight = newerButtonRight;
        OlderButtonLeft = olderButtonLeft;
        CloseButtonLeft = closeButtonLeft;
        CloseButtonSize = closeButtonSize;
        FooterLeft = footerLeft;
        FooterRight = footerRight;
    }

    internal static ConversationHistoryViewportLayout Resolve(float canvasWidth)
    {
        var safeCanvasWidth = float.IsFinite(canvasWidth) && canvasWidth >= 1f
            ? canvasWidth
            : FallbackCanvasWidth;
        var contentFraction = Math.Min(
            MaximumContentFraction,
            MaximumContentWidth / safeCanvasWidth);
        var contentLeft = (1f - contentFraction) * 0.5f;
        var contentRight = 1f - contentLeft;
        var gap = Math.Min(0.012f, contentFraction * 0.02f);
        var primaryButtonWidth = Math.Min(0.185f, contentFraction * 0.24f);
        // Size in canvas units, not a screen percentage: the close target must
        // stay square and proportionate on wide monitors. Bound tiny canvases.
        var closeButtonWidth = Math.Min(
            MaximumCloseButtonSize / safeCanvasWidth,
            contentFraction * 0.10f);

        return new ConversationHistoryViewportLayout(
            contentLeft,
            contentRight,
            contentRight - closeButtonWidth - gap,
            contentLeft + primaryButtonWidth,
            contentRight - primaryButtonWidth,
            contentRight - closeButtonWidth,
            closeButtonWidth * safeCanvasWidth,
            contentLeft + primaryButtonWidth + gap,
            contentRight - primaryButtonWidth - gap);
    }
}
