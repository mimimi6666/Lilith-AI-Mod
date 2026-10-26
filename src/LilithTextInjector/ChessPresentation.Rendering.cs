using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LilithTextInjector;

internal static partial class ChessPresentation
{
    private static readonly Image?[] pieceImages = new Image?[64], shadows = new Image?[64], targets = new Image?[64];
    private static Image boardImage = null!, clockImage = null!, selectedGlow = null!, checkedGlow = null!;
    private static Image movingImage = null!, capturedImage = null!, dragImage = null!, characterHit = null!;
    private static ChessClockDigits? clockDigits;
    private static Rect boardRect, clockRect, characterRect;
    private static Vector2 workOffset;
    private static int movingFrom = -1, movingTo = -1, capturedSquare = -1;
    private static char movingPiece, capturedPiece;
    private static double animationStarted;
    private static int targetsRevision = -1, targetsSelection = -2;
    private static bool[] legalTargets = new bool[64];

    private static void CreateSurface()
    {
        var native = UnityEngine.Object.FindObjectOfType<DialogueBubbleUI>(true);
        var font = native?.GetComponentInChildren<TextMeshProUGUI>(true);
        var window = TransparentWindowNew.instance;
        if (native == null || font == null || window == null) throw new InvalidOperationException("Chess native UI unavailable.");
        var layer = -1;
        foreach (var graphic in native.GetComponentsInChildren<Graphic>(true))
            if (graphic != null && graphic.raycastTarget && (window.hitTestLayerMask.value & (1 << graphic.gameObject.layer)) != 0)
            { layer = graphic.gameObject.layer; break; }
        if (layer < 0) throw new InvalidOperationException("Chess hit layer unavailable.");
        root = new GameObject("LilithChess", Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>(), Il2CppType.Of<GraphicRaycaster>());
        root.layer = layer;
        foregroundLayer = new OldMaidForegroundLayer(root, native);
        // Only an owned hit proxy follows the lifted visual. Never move the
        // original character collider/transform or touch global click-through.
        characterHit = NewImage("ChessCharacterPointer", root.transform, layer, null, true);
        characterHit.color = new Color(1, 1, 1, 0.001f);
        boardImage = NewImage("Board", root.transform, layer, assets!.Board, true);
        var shadowGroup = NewGroup("ContactShadows", root.transform, layer);
        for (var i = 0; i < 64; i++) shadows[i] = NewImage("Shadow" + i, shadowGroup, layer, assets.Shadow, false);
        // Board decorations stay below every piece, including tall silhouettes
        // extending into another square. Piece-local reordering cannot lift dots.
        var targetGroup = NewGroup("LegalTargets", root.transform, layer);
        for (var i = 0; i < 64; i++)
        {
            targets[i] = NewImage("LegalTarget" + i, targetGroup, layer, assets.Dot, false);
            targets[i]!.color = new Color(0.36f, 0.38f, 0.42f, 0.70f);
        }
        var pieceGroup = NewGroup("Pieces", root.transform, layer);
        selectedGlow = NewImage("SelectedPieceGlow", pieceGroup, layer, null, false);
        checkedGlow = NewImage("CheckedKingGlow", pieceGroup, layer, null, false);
        for (var i = 0; i < 64; i++)
        {
            pieceImages[i] = NewImage("Piece" + i, pieceGroup, layer, null, true);
            pieceImages[i]!.alphaHitTestMinimumThreshold = 64f / 255;
        }
        capturedImage = NewImage("CapturedPiece", pieceGroup, layer, null, false);
        movingImage = NewImage("MovingPiece", pieceGroup, layer, null, false);
        // Drag stays above the other pieces, without a second copy at origin.
        dragImage = NewImage("DraggedPiece", root.transform, layer, null, false);
        clockImage = NewImage("ClockShell", root.transform, layer, assets.Clock, true);
        clockDigits = new ChessClockDigits(root.transform, layer);
        targetsRevision = -1; targetsSelection = -2;
    }

    private static void Render()
    {
        var view = layout!; var game = session!;
        var x = view.X + workOffset.x; var y = view.Y + workOffset.y;
        boardRect = new Rect(x, y + view.BoardBottomInset, view.Width, view.Height - view.BoardBottomInset);
        Place(boardImage.rectTransform, boardRect, Vector2.zero);
        characterRect = lift?.VisualRect ?? default;
        Place(characterHit.rectTransform, characterRect, Vector2.zero);
        var side = view.Width * 0.31f;
        clockRect = new Rect(Math.Max(workOffset.x, x - side * 0.70f), y + view.Height * 0.43f, side, side);
        clockImage.gameObject.SetActive(game.Mode == ChessMode.Challenge);
        Place(clockImage.rectTransform, clockRect, Vector2.zero);
        clockDigits!.Render(game.Mode == ChessMode.Challenge ? clockRect : new Rect(-10000, -10000, 1, 1),
            ChessSession.ClockText(game.PlayerWhite ? game.WhiteSeconds : game.PinkSeconds),
            ChessSession.ClockText(game.PlayerWhite ? game.PinkSeconds : game.WhiteSeconds));
        var activeAnimation = Now < animationUntil && movingFrom >= 0;
        var dragging = input.Dragging && selected >= 0;
        var showTargets = game.Phase == ChessPhase.Playing && game.PlayerTurn && !activeAnimation && selected >= 0;
        if (targetsRevision != game.Rules.Revision || targetsSelection != selected)
        {
            legalTargets = ChessInteraction.Targets(selected, game.Rules.LegalMoves);
            targetsRevision = game.Rules.Revision; targetsSelection = selected;
        }
        selectedGlow.gameObject.SetActive(false); checkedGlow.gameObject.SetActive(false);
        for (var rank = 7; rank >= 0; rank--)
        for (var file = 0; file < 8; file++)
        {
            var display = rank * 8 + file; var square = game.PlayerWhite ? display : 63 - display;
            var piece = game.Rules.PieceAt(square); var image = pieceImages[square]!; var shadow = shadows[square]!;
            var at = view.SquareCenter(square, game.PlayerWhite); var width = view.CellWidth(square, game.PlayerWhite);
            var p = ScreenPoint(at);
            var target = targets[square]!;
            target.gameObject.SetActive(showTargets && legalTargets[square]);
            Place(target.rectTransform, new Rect(p.x, p.y, width * 0.19f, width * 0.115f), new Vector2(0.5f, 0.5f));
            var visible = piece != '.' && !(activeAnimation && square == movingTo) && !(dragging && square == selected);
            image.gameObject.SetActive(visible); shadow.gameObject.SetActive(visible);
            if (!visible) continue;
            PlacePiece(image, piece, at, width, 1);
            var baseWidth = width * 0.62f * assets!.Anchor(piece).BaseWidth;
            Place(shadow.rectTransform, new Rect(p.x, p.y - width * 0.006f, baseWidth * 1.06f, width * 0.105f), new Vector2(0.5f, 0.5f));
            var checkedKing = game.Rules.InCheck && piece == (game.Rules.WhiteTurn ? 'K' : 'k');
            if (checkedKing) PlaceGlow(checkedGlow, piece, at, width, new Color(1, 0.12f, 0.20f, 1));
            else if (square == selected && showTargets) PlaceGlow(selectedGlow, piece, at, width, new Color(0.80f, 0.94f, 1, 1));
            image.rectTransform.SetAsLastSibling();
        }
        movingImage.gameObject.SetActive(activeAnimation);
        capturedImage.gameObject.SetActive(activeAnimation && capturedPiece != '.');
        if (activeAnimation)
        {
            var t = Mathf.Clamp01((float)((Now - animationStarted) / 0.38)); var ease = t * t * (3 - 2 * t);
            var a = view.SquareCenter(movingFrom, game.PlayerWhite); var b = view.SquareCenter(movingTo, game.PlayerWhite);
            var width = Mathf.Lerp(view.CellWidth(movingFrom, game.PlayerWhite), view.CellWidth(movingTo, game.PlayerWhite), ease);
            var at = System.Numerics.Vector2.Lerp(a, b, ease); at.Y += MathF.Sin(t * MathF.PI) * width * 0.12f;
            if (capturedPiece != '.')
            {
                PlacePiece(capturedImage, capturedPiece, view.SquareCenter(capturedSquare, game.PlayerWhite), width, 1 - t);
                capturedImage.rectTransform.SetAsLastSibling();
            }
            PlacePiece(movingImage, movingPiece, at, width, 1); movingImage.rectTransform.SetAsLastSibling();
        }
        dragImage.gameObject.SetActive(dragging);
        if (dragging)
        {
            var piece = game.Rules.PieceAt(selected);
            var at = input.DragFoot - new System.Numerics.Vector2(workOffset.x, workOffset.y);
            var width = view.CellWidth(selected, game.PlayerWhite);
            PlaceGlow(selectedGlow, piece, at, width, new Color(0.80f, 0.94f, 1, 1));
            PlacePiece(dragImage, piece, at, width, 1);
        }
        renderRevision = game.Rules.Revision;
    }

    private static void PlaceGlow(Image image, char piece, System.Numerics.Vector2 at, float width, Color color)
    {
        image.gameObject.SetActive(true); image.sprite = assets!.Halo(piece); image.color = color;
        var anchor = assets.Anchor(piece).Pivot; var p = ScreenPoint(at);
        var padding = ChessPieceGeometry.HaloPad;
        var pivot = new Vector2((anchor.X * 128 + padding) / ChessPieceGeometry.HaloWidth,
            (anchor.Y * 256 + padding) / ChessPieceGeometry.HaloHeight);
        Place(image.rectTransform, new Rect(p.x, p.y, width * 0.62f * ChessPieceGeometry.HaloWidth / 128,
            width * 1.24f * ChessPieceGeometry.HaloHeight / 256), pivot);
        image.rectTransform.SetAsLastSibling();
    }
    private static Vector2 ScreenPoint(System.Numerics.Vector2 p) => new(p.X + workOffset.x, p.Y + workOffset.y);
    private static void PlacePiece(Image image, char fen, System.Numerics.Vector2 position, float cell, float alpha)
    {
        var p = ScreenPoint(position); image.sprite = assets!.Piece(fen); image.color = new Color(1, 1, 1, alpha);
        var anchor = assets.Anchor(fen).Pivot;
        Place(image.rectTransform, new Rect(p.x, p.y, cell * 0.62f, cell * 1.24f), new Vector2(anchor.X, anchor.Y));
    }
    private static void Place(RectTransform rect, Rect area, Vector2 pivot)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = pivot;
        rect.anchoredPosition = area.position; rect.sizeDelta = area.size;
    }
    private static Transform NewGroup(string name, Transform parent, int layer)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>()); obj.layer = layer; obj.transform.SetParent(parent, false);
        var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero; return rect;
    }
    private static Image NewImage(string name, Transform parent, int layer, Sprite? sprite, bool hit)
    {
        var obj = new GameObject(name, Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>(), Il2CppType.Of<Image>());
        obj.layer = layer; obj.transform.SetParent(parent, false); var image = obj.GetComponent<Image>();
        image.sprite = sprite; image.useSpriteMesh = false; image.raycastTarget = hit; return image;
    }
}
