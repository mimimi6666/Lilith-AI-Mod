using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace LilithTextInjector;

// A format adapter, not a second music player. Native code still owns playlists,
// announcements, play modes, AudioSource/AudioClip lifecycle, seek and restart.
internal static class FlacMusicPlayerPatches
{
    private const string HarmonyId = Plugin.PluginGuid + ".music-flac";
    private static FlacMusicPreparation _preparation = null!;
    private static ManualLogSource _log = null!;
    private static FlacMusicLoadRoutine? _activeRoutine;
    private static long _requestEpoch;
    [ThreadStatic] private static int _nativeLoadDepth;
    [ThreadStatic] private static FlacMusicTicket? _nativeTicket;
    [ThreadStatic] private static string? _nativeWavePath;

    internal static void Install(ManualLogSource log)
    {
        var harmony = new Harmony(HarmonyId);
        _log = log;
        Il2CppStringArray? originalExtensions = null;
        try
        {
            _preparation = new FlacMusicPreparation(
                Path.Combine(Paths.CachePath, "LilithMusicFlac-v1"),
                (source, target, cancellation) => FlacWaveDecoder.Decode(
                    source, target, FlacMetadataReader.Read(FlacWaveDecoder.ValidateSourcePath(source)), cancellation));
            Patch(harmony, typeof(AudioManager), "PlayBGMFromFile", nameof(CancelOutsideNativeLoad));
            Patch(harmony, typeof(AudioManager), "PlayBGM", nameof(CancelOutsideNativeLoad));
            Patch(harmony, typeof(AudioManager), "StopBGM", nameof(CancelOutsideNativeLoad));
            Patch(harmony, typeof(AudioManager), "StopUserMusic", nameof(CancelOutsideNativeLoad));
            Patch(harmony, typeof(AudioManager), "SilenceForProcessExit", nameof(CancelAll));
            Patch(harmony, typeof(AudioManager), "OnDestroy", nameof(CancelAll));
            Patch(harmony, typeof(AudioManager), "TickUserMusicPlayback", nameof(WaitForPreparedTrack));
            Patch(harmony, typeof(AudioManager), "LoadAndPlayBGMRoutine", nameof(WrapNativeLoad), postfix: true);
            Patch(harmony, typeof(AudioManager), "BuildLocalFileUrl", nameof(RedirectPreparedFile));
            Patch(harmony, typeof(AudioManager), "GetAudioTypeByExtension", nameof(PreparedAudioType));
            Patch(harmony, typeof(MusicFileMetadata), "TryRead", nameof(ReadTags));
            Patch(harmony, typeof(MusicFileMetadata), "TryReadDurationSeconds", nameof(ReadDuration));

            // Publish discovery last: a partially patched build must not show
            // tracks that the installed adapter cannot handle.
            originalExtensions = MusicLibrary.SupportedExtensions;
            if (originalExtensions is null) throw new InvalidOperationException("Native music extensions unavailable.");
            MusicLibrary.SupportedExtensions = new Il2CppStringArray(
                FlacMusicPreparation.WithFlac(originalExtensions));
            log.LogInfo("Music FLAC adapter initialized; native playback controls preserved.");
        }
        catch (Exception error)
        {
            try
            {
                _preparation?.Cancel();
                if (originalExtensions is not null) MusicLibrary.SupportedExtensions = originalExtensions;
                harmony.UnpatchSelf();
            }
            catch (Exception rollbackError)
            {
                // Match the plugin's existing partial-patch hard-stop contract;
                // never announce safe fallback when rollback itself failed.
                log.LogError("Music FLAC patch rollback failed. kind=" + rollbackError.GetType().Name);
                throw new InvalidOperationException("Music FLAC partial-patch rollback failed.");
            }
            log.LogWarning("Music FLAC adapter unavailable; existing formats unchanged. kind=" + error.GetType().Name);
        }
    }

    private static void Patch(Harmony harmony, Type targetType, string targetName, string patchName, bool postfix = false)
    {
        var target = AccessTools.Method(targetType, targetName)
            ?? throw new MissingMethodException(targetType.Name, targetName);
        var patch = new HarmonyMethod(typeof(FlacMusicPlayerPatches), patchName);
        if (postfix) harmony.Patch(target, postfix: patch);
        else harmony.Patch(target, prefix: patch);
    }

    private static void CancelOutsideNativeLoad()
    {
        // Native PlayBGM/StopBGM calls within a successful load are not a new
        // user request. External stop, replacement or destruction is.
        if (_nativeLoadDepth == 0) CancelAll();
    }

    // The native auto-advance loop must not mistake background preparation for
    // an ended track. Explicit next/previous/stop commands remain available.
    private static bool WaitForPreparedTrack() => !_preparation.HasCurrent;

    private static void CancelAll()
    {
        unchecked { _requestEpoch++; }
        RetireActive();
    }

    private static void RetireActive()
    {
        _preparation.Cancel();
        var previous = _activeRoutine;
        _activeRoutine = null;
        // Do not dispose a native iterator re-entrantly from its own MoveNext
        // (for example when native failure recovery selects the next track).
        // Its fenced wrapper disposes it as soon as that MoveNext returns.
        if (_nativeLoadDepth == 0)
        {
            try { previous?.Dispose(); }
            catch (Exception error)
            {
                // The ticket is already fenced. A disposed/destroyed native
                // request must not prevent the user's next Stop or MP3 play.
                _log.LogWarning("Music FLAC native cleanup failed. kind=" + error.GetType().Name);
            }
        }
    }

    private static void WrapNativeLoad(string __0, ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result is null) return;
        var isFlac = FlacMusicPreparation.IsFlac(__0);
        if (!isFlac && _nativeLoadDepth == 0)
        {
            CancelAll();
            return;
        }
        var source = __0;
        var native = __result;
        var epoch = _requestEpoch;
        // A native failure handler may yield a child LoadAndPlayBGMRoutine.
        // Constructing that child must not invalidate its parent before Unity
        // receives the yielded iterator. Admit/start only on first MoveNext.
        __result = new FlacDeferredMusicLoad(() =>
        {
            RetireActive();
            if (!isFlac) return new NativeEnumerator(native, passthroughScope: true);
            var ticket = _preparation.Begin(source);
            _activeRoutine = new FlacMusicLoadRoutine(_preparation, ticket,
                new NativeEnumerator(native), path => new NativeLoadScope(ticket, path),
                reason => _log.LogWarning("Music FLAC decode unavailable; native load-failure recovery retained. kind=" + reason));
            return _activeRoutine;
        }, () => epoch == _requestEpoch).WrapToIl2Cpp();
    }

    private sealed class NativeEnumerator : IEnumerator, IDisposable
    {
        private readonly Il2CppSystem.Collections.IEnumerator _native;
        private readonly bool _passthroughScope;
        internal NativeEnumerator(Il2CppSystem.Collections.IEnumerator native, bool passthroughScope = false)
        { _native = native; _passthroughScope = passthroughScope; }
        public object? Current => _native.Current;
        public bool MoveNext()
        {
            if (!_passthroughScope) return _native.MoveNext();
            // Native recovery into MP3/OGG/WAV still calls native Stop/Play.
            // Those internal calls must not invalidate the child's epoch.
            using var scope = new NativeLoadScope(null, null);
            return _native.MoveNext();
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => _native.TryCast<Il2CppSystem.IDisposable>()?.Dispose();
    }

    private sealed class NativeLoadScope : IDisposable
    {
        private readonly FlacMusicTicket? _previousTicket = _nativeTicket;
        private readonly string? _previousPath = _nativeWavePath;
        private readonly int _previousDepth = _nativeLoadDepth;
        internal NativeLoadScope(FlacMusicTicket? ticket, string? path)
        {
            _nativeLoadDepth++;
            _nativeTicket = ticket;
            _nativeWavePath = path;
        }
        public void Dispose()
        {
            _nativeLoadDepth = _previousDepth;
            _nativeTicket = _previousTicket;
            _nativeWavePath = _previousPath;
        }
    }

    private static bool IsPreparedRequest(string path) => _nativeTicket is not null &&
        _nativeWavePath is not null && string.Equals(path, _nativeTicket.SourcePath, StringComparison.OrdinalIgnoreCase);

    private static bool RedirectPreparedFile(string __0, ref string __result)
    {
        if (!IsPreparedRequest(__0)) return true;
        __result = new Uri(_nativeWavePath!, UriKind.Absolute).AbsoluteUri;
        return false;
    }

    private static bool PreparedAudioType(string __0, ref AudioType __result)
    {
        if (!IsPreparedRequest(__0)) return true;
        __result = AudioType.WAV;
        return false;
    }

    private static bool ReadTags(string __0, ref MusicFileMetadata.Tags __result)
    {
        if (!FlacMusicPreparation.IsFlac(__0)) return true;
        try
        {
            var metadata = FlacMetadataReader.Read(FlacWaveDecoder.ValidateSourcePath(__0));
            __result = new MusicFileMetadata.Tags(metadata.Title ?? string.Empty, metadata.Artist ?? string.Empty);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            __result = new MusicFileMetadata.Tags(string.Empty, string.Empty);
        }
        return false;
    }

    private static bool ReadDuration(string __0, ref float __result)
    {
        if (!FlacMusicPreparation.IsFlac(__0)) return true;
        try { __result = (float)FlacMetadataReader.Read(FlacWaveDecoder.ValidateSourcePath(__0)).DurationSeconds; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { __result = 0; }
        return false;
    }
}
