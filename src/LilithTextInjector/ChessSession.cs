using System;

namespace LilithTextInjector;

internal enum ChessMode { Casual, Challenge }
internal enum ChessPhase { Setup, Playing, Finished }

internal sealed class ChessSession
{
    internal ChessRules Rules { get; }
    internal ChessMode Mode { get; }
    internal bool PlayerWhite { get; }
    internal int Strength { get; }
    internal int Minutes { get; private set; }
    internal ChessPhase Phase { get; private set; } = ChessPhase.Setup;
    internal double WhiteSeconds { get; private set; }
    internal double PinkSeconds { get; private set; }
    internal bool PlayerTurn => Rules.WhiteTurn == PlayerWhite;
    private double? lastTime;

    internal ChessSession(ChessMode mode, bool playerWhite, int strength, int minutes = 10, string? fen = null)
    {
        Mode = mode;
        PlayerWhite = playerWhite;
        Strength = Math.Clamp(strength + (mode == ChessMode.Challenge ? 100 : 0), 400, 2800);
        Minutes = Math.Clamp(minutes, 1, 60);
        WhiteSeconds = PinkSeconds = Minutes * 60;
        Rules = new ChessRules(fen);
    }

    internal void AdjustMinutes(int delta)
    {
        if (Phase != ChessPhase.Setup || Mode != ChessMode.Challenge) return;
        Minutes = Math.Clamp(Minutes + Math.Sign(delta), 1, 60);
        WhiteSeconds = PinkSeconds = Minutes * 60;
    }

    internal void Start(double now)
    {
        if (Phase != ChessPhase.Setup || !double.IsFinite(now)) return;
        Phase = Rules.Result == ChessResult.None ? ChessPhase.Playing : ChessPhase.Finished;
        lastTime = now;
    }

    internal void Tick(double now)
    {
        if (!double.IsFinite(now)) return;
        // Never move the monotonic baseline backwards or create a hidden pause.
        if (lastTime.HasValue && now < lastTime.Value) return;
        var elapsed = lastTime.HasValue ? now - lastTime.Value : 0;
        lastTime = now;
        if (Phase != ChessPhase.Playing || Mode != ChessMode.Challenge) return;
        if (Rules.WhiteTurn) WhiteSeconds = Math.Max(0, WhiteSeconds - elapsed);
        else PinkSeconds = Math.Max(0, PinkSeconds - elapsed);
        if ((Rules.WhiteTurn ? WhiteSeconds : PinkSeconds) <= 0) Rules.Timeout(Rules.WhiteTurn);
        ObserveEnd();
    }

    internal bool TryMove(string move, int revision, bool fromPlayer)
    {
        if (Phase != ChessPhase.Playing || PlayerTurn != fromPlayer || !Rules.TryMove(move, revision)) return false;
        ObserveEnd();
        return true;
    }

    internal void ObserveEnd() { if (Rules.Result != ChessResult.None) Phase = ChessPhase.Finished; }
    internal int PlayerOutcome => Rules.Result switch
    {
        ChessResult.WhiteMate or ChessResult.PinkTimeout or ChessResult.PinkResigned => PlayerWhite ? 1 : -1,
        ChessResult.PinkMate or ChessResult.WhiteTimeout or ChessResult.WhiteResigned => PlayerWhite ? -1 : 1,
        _ => 0
    };
    internal static string ClockText(double seconds) => $"{(int)Math.Ceiling(Math.Max(0, seconds)) / 60:00}:{(int)Math.Ceiling(Math.Max(0, seconds)) % 60:00}";
}
