using System;
using UnityEngine;

namespace LilithTextInjector;

// Own at most two clips and two short voices. Native SE routing is read only;
// never play/stop/replace the game's music, dialogue or action audio sources.
internal static class OldMaidAudio
{
    private static GameObject? _root;
    private static AudioSource? _draw, _throw;
    private static AudioClip? _drawClip, _throwClip;
    private static bool _unavailable;

    internal static void Play(GameObject? parent, OldMaidSound kind)
    {
        if (_unavailable || parent == null || !parent.activeInHierarchy
            || !OldMaidNativeBridge.IsSnackActive
            || !DialogueManagerUpdatePatch.ReadOldMaidForeground()
            || DialogueManagerUpdatePatch.ReadOldMaidModal()) return;
        try
        {
            var native = AudioManager.instance?.source_SE;
            if (native == null || !native.isActiveAndEnabled || native.mute || native.volume <= 0) return;
            if (_root == null) Create(parent);
            var source = kind == OldMaidSound.Draw ? _draw! : _throw!;
            source.Stop();
            source.outputAudioMixerGroup = native.outputAudioMixerGroup;
            source.volume = OldMaidCardSound.EffectsVolume(native.volume);
            source.mute = native.mute;
            source.Play();
        }
        catch (Exception error)
        {
            Suspend();
            if (!_unavailable) Plugin.PluginLog.LogWarning("Old Maid sound unavailable: " + error.GetType().Name);
            _unavailable = true; // Audio failure must never stop the cards.
        }
    }

    private static void Create(GameObject parent)
    {
        _root = new GameObject("OldMaidCardSounds");
        _root.transform.SetParent(parent.transform, false);
        _drawClip = CreateClip(OldMaidSound.Draw);
        _throwClip = CreateClip(OldMaidSound.Throw);
        _draw = _root.AddComponent<AudioSource>();
        _throw = _root.AddComponent<AudioSource>();
        Configure(_draw, _drawClip);
        Configure(_throw, _throwClip);
    }

    private static AudioClip CreateClip(OldMaidSound kind)
    {
        var samples = OldMaidCardSound.Create(kind);
        var clip = AudioClip.Create("OldMaid" + kind, samples.Length, 1, OldMaidCardSound.SampleRate, false);
        try
        {
            if (clip == null || !clip.SetData(samples, 0)) throw new InvalidOperationException("Card audio samples rejected.");
            return clip;
        }
        catch { if (clip != null) UnityEngine.Object.Destroy(clip); throw; }
    }

    private static void Configure(AudioSource source, AudioClip clip)
    {
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0;
        source.ignoreListenerPause = false;
        source.ignoreListenerVolume = false;
        source.priority = 160;
        source.clip = clip;
    }

    internal static void SyncSettings()
    {
        if (_unavailable || _root == null) return;
        try
        {
            if (!((_draw != null && _draw.isPlaying) || (_throw != null && _throw.isPlaying))) return;
            var native = AudioManager.instance?.source_SE;
            if (native == null || !native.isActiveAndEnabled || native.mute || native.volume <= 0)
            { Suspend(); return; }
            if (_draw != null) _draw.volume = OldMaidCardSound.EffectsVolume(native.volume);
            if (_throw != null) _throw.volume = OldMaidCardSound.EffectsVolume(native.volume);
        }
        catch (Exception) { Suspend(); }
    }

    internal static void Suspend()
    {
        try { if (_draw != null) _draw.Stop(); } catch (Exception) { }
        try { if (_throw != null) _throw.Stop(); } catch (Exception) { }
    }

    internal static void Dispose()
    {
        Suspend();
        DestroyOwned(_root);
        DestroyOwned(_drawClip);
        DestroyOwned(_throwClip);
        _root = null;
        _draw = _throw = null;
        _drawClip = _throwClip = null;
        _unavailable = false;
    }

    private static void DestroyOwned(UnityEngine.Object? owned)
    {
        try { if (owned != null) UnityEngine.Object.Destroy(owned); }
        catch (Exception) { } // Audio cleanup never prevents scale/UI cleanup.
    }
}
