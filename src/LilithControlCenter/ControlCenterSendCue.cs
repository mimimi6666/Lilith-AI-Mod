using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace LilithControlCenter;

// This player owns only a short send cue, never the speech playback stream.
internal sealed class ControlCenterSendCue : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _limit = new() { Interval = TimeSpan.FromSeconds(8) };
    internal ControlCenterSendCue()
    {
        _player.Volume = .18;
        _player.MediaEnded += (_, _) => Stop();
        _player.MediaFailed += (_, _) => Stop();
        _limit.Tick += (_, _) => Stop();
    }
    internal void Play(bool nativeVoiceOwnsCue)
    {
        Stop();
        if (nativeVoiceOwnsCue || !App.UiPreferences.ChatSendSoundEnabled) return;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LilithAiMod", "Runtime", "hermes", "lilith-core-3c5fd918", "audio", "thinking-cue.wav");
            for (var cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 44 or > 4194304) return;
            _player.Open(new Uri(path));
            _player.Play();
            _limit.Start();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { Stop(); }
    }
    internal void Stop() { _limit.Stop(); _player.Close(); }
    public void Dispose() => Stop();
}
