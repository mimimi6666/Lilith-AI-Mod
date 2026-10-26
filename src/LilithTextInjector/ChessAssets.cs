using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LilithTextInjector;

internal sealed class ChessAssets : IDisposable
{
    internal static string DirectoryPath => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "LilithChess");
    internal const string EngineHash = "5568B400DF3425718D310050B5B77FCC81D30B82FDF231A90F40B520C74DE3BF";
    private readonly List<Texture2D> textures = new();
    private readonly List<Sprite> sprites = new();
    internal Sprite Board { get; }
    internal Sprite Clock { get; }
    internal Sprite Shadow { get; }
    internal Sprite Dot { get; }
    private readonly Sprite[] pieces = new Sprite[12];
    private readonly Sprite[] halos = new Sprite[12];
    private readonly ChessPieceAnchor[] anchors = new ChessPieceAnchor[12];
    private byte[] pieceAlpha = Array.Empty<byte>();
    internal ChessAssets()
    {
        try
        {
            var board = Load(ChessArtwork.Board);
            Board = Sprite(board, new Rect(0, ChessLayout.BoardTransparentBottom, 1536, 1024 - ChessLayout.BoardTransparentBottom), Vector2.zero);
            var clock = Load(ChessArtwork.Clock);
            Clock = Sprite(clock, new Rect(0, 0, 1254, 1254), Vector2.zero);
            var sheet = Load(ChessArtwork.Pieces, false);
            var pixels = sheet.GetPixels32().ToArray();
            pieceAlpha = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) pieceAlpha[i] = pixels[i].a;
            sheet.Apply(false, false); // native alpha-aware UI picking uses this one bounded CPU texture
            // Sprite extraction only: accepted PNG bytes stay untouched.
            for (var side = 0; side < 2; side++)
            for (var kind = 0; kind < 6; kind++)
            {
                var index = side * 6 + kind;
                var tile = new byte[256 * 512];
                for (var y = 0; y < 512; y++)
                    Array.Copy(pieceAlpha, ((side == 0 ? 512 : 0) + y) * 1536 + kind * 256, tile, y * 256, 256);
                anchors[index] = ChessPieceGeometry.Measure(tile);
                var pivot = anchors[index].Pivot;
                pieces[index] = Sprite(sheet, new Rect(kind * 256, side == 0 ? 512 : 0, 256, 512), new Vector2(pivot.X, pivot.Y));
                var halo = new Texture2D(ChessPieceGeometry.HaloWidth, ChessPieceGeometry.HaloHeight, TextureFormat.RGBA32, false);
                textures.Add(halo);
                var values = ChessPieceGeometry.Halo(tile);
                var colors = new Color32[values.Length];
                for (var i = 0; i < values.Length; i++) colors[i] = new Color32(255, 255, 255, values[i]);
                halo.SetPixels32(colors); halo.wrapMode = TextureWrapMode.Clamp; halo.filterMode = FilterMode.Bilinear;
                halo.Apply(false, true);
                halos[index] = Sprite(halo, new Rect(0, 0, halo.width, halo.height), new Vector2(0.5f, 0.5f));
            }
            var shadow = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            textures.Add(shadow);
            for (var y = 0; y < 32; y++) for (var x = 0; x < 64; x++)
            {
                var d = MathF.Pow((x - 31.5f) / 32, 2) + MathF.Pow((y - 15.5f) / 16, 2);
                shadow.SetPixel(x, y, new Color(0.1f, 0.17f, 0.32f, Math.Max(0, 1 - d) * 0.25f));
            }
            shadow.Apply(false, true);
            Shadow = Sprite(shadow, new Rect(0, 0, 64, 32), new Vector2(0.5f, 0.5f));
            var dot = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            textures.Add(dot);
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                dot.SetPixel(x, y, new Color(1, 1, 1, Math.Clamp((15 - MathF.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f))) / 1.5f, 0, 1)));
            dot.Apply(false, true); Dot = Sprite(dot, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
        }
        catch { Dispose(); throw; }
    }

    internal Sprite Piece(char fen)
    {
        var kind = Kind(fen);
        return pieces[(char.IsUpper(fen) ? 0 : 6) + kind];
    }
    private static int Kind(char fen) => char.ToLowerInvariant(fen) switch { 'k' => 0, 'q' => 1, 'b' => 2, 'n' => 3, 'r' => 4, _ => 5 };
    internal ChessPieceAnchor Anchor(char fen) => anchors[(char.IsUpper(fen) ? 0 : 6) + Kind(fen)];
    internal Sprite Halo(char fen) => halos[(char.IsUpper(fen) ? 0 : 6) + Kind(fen)];
    internal bool HitPiece(char fen, float u, float v)
    {
        if (!float.IsFinite(u) || !float.IsFinite(v) || u < 0 || u >= 1 || v < 0 || v >= 1) return false;
        var x = Kind(fen) * 256 + (int)(u * 256);
        var y = (char.IsUpper(fen) ? 512 : 0) + (int)(v * 512);
        return pieceAlpha[y * 1536 + x] >= 64;
    }
    private Texture2D Load(ChessArtwork artwork, bool nonReadable = true)
    {
        var spec = ChessArtPolicy.Describe(artwork);
        var bytes = ChessArtPolicy.ReadFile(DirectoryPath, artwork);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        textures.Add(texture);
        if (!ImageConversion.LoadImage(texture, bytes, nonReadable))
            throw new ChessArtworkException(artwork, ChessArtFailure.Decode);
        if (texture.width != spec.Width || texture.height != spec.Height)
            throw new ChessArtworkException(artwork, ChessArtFailure.DecodedDimensions);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }
    private Sprite Sprite(Texture2D texture, Rect rect, Vector2 pivot)
    {
        var sprite = UnityEngine.Sprite.Create(texture, rect, pivot, 100, 0, SpriteMeshType.FullRect);
        sprites.Add(sprite);
        return sprite;
    }
    public void Dispose()
    {
        foreach (var sprite in sprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
        foreach (var texture in textures) if (texture != null) UnityEngine.Object.Destroy(texture);
        sprites.Clear(); textures.Clear();
        pieceAlpha = Array.Empty<byte>();
    }
}
