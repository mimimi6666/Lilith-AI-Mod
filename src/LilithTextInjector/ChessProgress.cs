using System;
using System.IO;
using System.Text.Json;

namespace LilithTextInjector;

internal sealed class ChessProgress
{
    public int Version { get; set; } = 1;
    public int Strength { get; set; } = 800;
    public bool NextPlayerWhite { get; set; } = true;
    public int Minutes { get; set; } = 10;
    public int CompletedGames { get; set; }

    internal static ChessProgress Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Chess progress too large.");
        var data = JsonSerializer.Deserialize<ChessProgress>(File.ReadAllText(path)) ?? throw new InvalidDataException("Chess progress unavailable.");
        if (data.Version != 1 || data.Strength is < 400 or > 2800 || data.Minutes is < 1 or > 60 || data.CompletedGames < 0)
            throw new InvalidDataException("Chess progress version or range unavailable.");
        return data;
    }

    internal void Complete(int outcome)
    {
        Strength = Math.Clamp(Strength + Math.Sign(outcome) * 25, 400, 2800);
        CompletedGames = Math.Min(int.MaxValue - 1, CompletedGames) + 1;
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, this); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
