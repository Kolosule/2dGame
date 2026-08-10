using System.Collections.Generic;
using Game.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The whole audio runtime. Creates itself before the first scene loads -- there is no scene
/// object to place and no inspector reference to forget, which is deliberate: unassigned scene
/// references are this project's dominant failure mode, and audio that silently does nothing is
/// exactly the kind of failure nobody notices until playtest.
///
/// CLIENT-ONLY BY CONSTRUCTION. On a build with no graphics device (the dedicated server) this
/// never instantiates, so every Audio.* call is a null-check and a return. Call sites must not
/// add their own server guards.
///
/// Positional cues are spatialized MANUALLY (spatialBlend stays 0; attenuation and pan are
/// computed from the orthographic camera). Unity's 3D panner would need a correctly placed
/// AudioListener at a correct z-depth, which this project has never configured -- and getting
/// that wrong produces silence, not an error.
///
/// See docs/superpowers/specs/2026-07-29-audio-system-design.md.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private AudioConfig config;
    private SoundBank bank;

    private readonly Dictionary<AudioBus, AudioMixerGroup> groups = new Dictionary<AudioBus, AudioMixerGroup>();
    private readonly Dictionary<AudioCueId, int> variantCursor = new Dictionary<AudioCueId, int>();
    private readonly HashSet<AudioCueId> warnedMissing = new HashSet<AudioCueId>();

    private readonly SoundDedupe dedupe = new SoundDedupe();

    private AudioSource[] sfxSources;
    private VoiceBudget sfxBudget;
    private AudioSource[] uiSources;
    private VoiceBudget uiBudget;

    private Camera listenerCamera;

    private MusicDirector music;

    public AudioConfig Config => config;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        // Same client-only gate SettingsService uses. A headless server allocates nothing.
        if (!SettingsService.HasDisplay) return;
        if (Instance != null) return;

        AudioConfig cfg = Resources.Load<AudioConfig>("AudioConfig");
        if (cfg == null)
        {
            Debug.LogError("[Audio] Resources/AudioConfig.asset not found — the game will run silently.");
            return;
        }

        var go = new GameObject("AudioManager");
        DontDestroyOnLoad(go);

        AudioManager manager = go.AddComponent<AudioManager>();
        Instance = manager;
        manager.Initialize(cfg);
    }

    private void Initialize(AudioConfig cfg)
    {
        config = cfg;
        bank = cfg.Bank;

        if (bank == null)
            Debug.LogError("[Audio] AudioConfig has no SoundBank assigned — no sound effect will play.");

        CacheMixerGroups();

        sfxSources = BuildPool("SfxVoice", cfg.SfxVoices);
        sfxBudget = new VoiceBudget(cfg.SfxVoices);
        uiSources = BuildPool("UiVoice", cfg.UiVoices);
        uiBudget = new VoiceBudget(cfg.UiVoices);

        // Hand the mixer to the already-shipped settings layer. The four persisted volume sliders
        // become audible on this line and not before — see SettingsService.Mixer's doc comment.
        SettingsService.Mixer = cfg.Mixer;
        SettingsService.ApplyAudio();

        music = new MusicDirector();
        music.Initialize(this, config);
    }

    private void CacheMixerGroups()
    {
        groups.Clear();
        if (config.Mixer == null)
        {
            Debug.LogError("[Audio] AudioConfig has no AudioMixer assigned — volume sliders will do nothing.");
            return;
        }

        foreach (AudioBus bus in System.Enum.GetValues(typeof(AudioBus)))
        {
            AudioMixerGroup[] matches = config.Mixer.FindMatchingGroups(bus.ToString());
            if (matches != null && matches.Length > 0) groups[bus] = matches[0];
            else Debug.LogError($"[Audio] Mixer has no group named '{bus}'. Cues on that bus will be unrouted.");
        }
    }

    private AudioSource[] BuildPool(string namePrefix, int count)
    {
        var pool = new AudioSource[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{namePrefix}_{i}");
            go.transform.SetParent(transform, false);

            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // manual 2D spatialization; see the class doc comment
            src.loop = false;
            pool[i] = src;
        }
        return pool;
    }

    /// <summary>Returns finished voices to their budgets and advances music. One O(pool) sweep per
    /// frame, no allocation — cheaper and far simpler than a callback per voice.</summary>
    private void Update()
    {
        ReleaseFinished(sfxSources, sfxBudget);
        ReleaseFinished(uiSources, uiBudget);
        music?.Tick(Time.unscaledDeltaTime);
    }

    private static void ReleaseFinished(AudioSource[] pool, VoiceBudget budget)
    {
        for (int i = 0; i < pool.Length; i++)
            if (budget.IsActive(i) && !pool[i].isPlaying) budget.Release(i);
    }

    // ---- Playback ----

    public void PlayAt(AudioCueId id, Vector3 worldPosition)
    {
        if (!Resolve(id, out SoundCue cue)) return;

        float attenuation = 1f;
        float pan = 0f;

        if (cue.positional && !ComputeSpatial(cue, worldPosition, out attenuation, out pan))
            return;   // gate 1: culled by distance, before any voice is acquired

        Play(id, cue, sfxSources, sfxBudget, attenuation, pan);
    }

    public void Play2D(AudioCueId id)
    {
        if (!Resolve(id, out SoundCue cue)) return;
        Play(id, cue, sfxSources, sfxBudget, attenuation: 1f, pan: 0f);
    }

    public void PlayUi(AudioCueId id)
    {
        if (!Resolve(id, out SoundCue cue)) return;
        Play(id, cue, uiSources, uiBudget, attenuation: 1f, pan: 0f);
    }

    private bool Resolve(AudioCueId id, out SoundCue cue)
    {
        cue = null;
        if (bank == null || id == AudioCueId.None) return false;

        if (!bank.TryGet(id, out cue) || !cue.HasClip)
        {
            // Warn once per cue per session: a missing cue in a scrum would otherwise spam the log
            // hard enough to be its own performance problem.
            if (warnedMissing.Add(id))
                Debug.LogWarning($"[Audio] No playable SoundBank entry for {id}.");
            return false;
        }
        return true;
    }

    /// <summary>Gate 1: linear distance attenuation and clamped pan against the local camera.
    /// Returns false when the cue is at or beyond silence, so the caller drops it without touching
    /// the voice pool — an off-screen hit costs one squared-distance compare.</summary>
    private bool ComputeSpatial(SoundCue cue, Vector3 worldPosition, out float attenuation, out float pan)
    {
        attenuation = 1f;
        pan = 0f;

        Camera cam = ResolveCamera();
        if (cam == null) return true;   // no camera yet (boot/menu): play flat rather than swallow

        float max = cue.maxDistance > 0f ? cue.maxDistance : config.DefaultWorldMaxDistance;

        Vector3 camPos = cam.transform.position;
        float dx = worldPosition.x - camPos.x;
        float dy = worldPosition.y - camPos.y;
        if (dx * dx + dy * dy >= max * max) return false;

        attenuation = 1f - Mathf.Sqrt(dx * dx + dy * dy) / max;

        float halfWidth = cam.orthographic ? cam.orthographicSize * cam.aspect : max;
        if (halfWidth > 0.01f)
            pan = Mathf.Clamp(dx / halfWidth, -1f, 1f) * config.MaxPan;

        return true;
    }

    private Camera ResolveCamera()
    {
        if (listenerCamera == null) listenerCamera = Camera.main;
        return listenerCamera;
    }

    private void Play(AudioCueId id, SoundCue cue, AudioSource[] pool, VoiceBudget budget,
                      float attenuation, float pan)
    {
        // Unscaled time: hit-stop and pause must not stretch a dedupe window.
        float now = Time.unscaledTime;

        if (!dedupe.ShouldPlay((int)id, now, cue.dedupeWindow)) return;              // gate 2

        int slot = budget.TryAcquire((int)id, cue.priority, cue.maxConcurrent, now); // gate 3
        if (slot < 0) return;

        AudioSource src = pool[slot];
        src.Stop();
        src.clip = NextVariant(id, cue);
        src.outputAudioMixerGroup = ResolveGroup(cue.bus);
        src.volume = cue.volume * attenuation;
        src.pitch = Random.Range(cue.pitchRange.x, cue.pitchRange.y);
        src.panStereo = pan;
        src.spatialBlend = 0f;
        src.loop = false;
        src.Play();
    }

    /// <summary>Round-robin rather than random: random repeats the same clip back-to-back roughly
    /// 1/n of the time, which is exactly the case the variants exist to prevent.</summary>
    private AudioClip NextVariant(AudioCueId id, SoundCue cue)
    {
        if (cue.variants.Length == 1) return cue.variants[0];

        variantCursor.TryGetValue(id, out int cursor);
        AudioClip clip = cue.variants[cursor % cue.variants.Length];
        variantCursor[id] = (cursor + 1) % cue.variants.Length;
        return clip != null ? clip : cue.variants[0];
    }

    public AudioMixerGroup ResolveGroup(AudioBus bus)
        => groups.TryGetValue(bus, out AudioMixerGroup group) ? group : null;

    private void OnDestroy()
    {
        music?.Shutdown();
        if (Instance == this) Instance = null;
    }
}
