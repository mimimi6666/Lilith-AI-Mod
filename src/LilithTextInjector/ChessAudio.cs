using System;
using UnityEngine;

namespace LilithTextInjector;

// One bounded voice, two short clips. No writes to native SE/music/dialogue,
// and no sharing of Old Maid's owned sounds or cleanup lifetime.
internal static class ChessAudio
{
    private static GameObject? root;
    private static AudioSource? source;
    private static AudioClip? move, capture;
    private static bool unavailable;

    internal static void Play(GameObject? parent, ChessSound kind)
    {
        if (unavailable || parent == null || !parent.activeInHierarchy || !OldMaidNativeBridge.IsSnackActive
            || !DialogueManagerUpdatePatch.ReadOldMaidForeground() || DialogueManagerUpdatePatch.ReadOldMaidModal()) return;
        try
        {
            var native = AudioManager.instance?.source_SE;
            if (native == null || !native.isActiveAndEnabled || native.mute || native.volume <= 0) return;
            if (root == null)
            {
                root = new GameObject("ChessPieceSounds"); root.transform.SetParent(parent.transform, false);
                source = root.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = false;
                source.spatialBlend = 0; source.priority = 160;
                source.ignoreListenerPause = false; source.ignoreListenerVolume = false;
                move = Clip(ChessSound.Move); capture = Clip(ChessSound.Capture);
            }
            source!.Stop(); source.clip = kind == ChessSound.Capture ? capture : move;
            source.outputAudioMixerGroup = native.outputAudioMixerGroup;
            source.volume = OldMaidCardSound.EffectsVolume(native.volume); source.mute = native.mute; source.Play();
        }
        catch (Exception error)
        {
            Dispose(); unavailable = true;
            Plugin.PluginLog.LogWarning("Chess audio unavailable: " + error.GetType().Name);
        }
    }

    private static AudioClip Clip(ChessSound kind)
    {
        var samples = ChessSoundSamples.Read(kind);
        var clip = AudioClip.Create("Chess" + kind, samples.Length, 1, ChessSoundSamples.SampleRate, false);
        try
        {
            if (clip == null || !clip.SetData(samples, 0)) throw new InvalidOperationException("Chess audio rejected.");
            return clip;
        }
        catch { Destroy(clip); throw; }
    }

    internal static void SyncSettings()
    {
        if (unavailable || source == null || !source.isPlaying) return;
        try
        {
            var native = AudioManager.instance?.source_SE;
            if (native == null || !native.isActiveAndEnabled || native.mute || native.volume <= 0
                || !DialogueManagerUpdatePatch.ReadOldMaidForeground() || DialogueManagerUpdatePatch.ReadOldMaidModal()) { source.Stop(); return; }
            source.volume = OldMaidCardSound.EffectsVolume(native.volume);
        }
        catch (Exception) { try { source.Stop(); } catch (Exception) { } }
    }

    internal static void Dispose()
    {
        try { if (source != null) source.Stop(); } catch (Exception) { }
        Destroy(root); Destroy(move); Destroy(capture);
        root = null; source = null; move = capture = null; unavailable = false;
    }
    private static void Destroy(UnityEngine.Object? value)
    { try { if (value != null) UnityEngine.Object.Destroy(value); } catch (Exception) { } }
}
