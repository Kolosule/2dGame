using Fusion;
using Game.Audio.Core;
using Game.Match.Core;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Drives music and mixer snapshots from match state. Owned by AudioManager, not a MonoBehaviour —
/// there is exactly one, its lifetime is the manager's, and it needs no inspector surface.
///
/// It re-resolves its MatchManager reference every frame by comparing against the live singleton
/// rather than subscribing once. MatchManager is a NetworkBehaviour that spawns, despawns, and
/// respawns across scene reloads on rematch; a one-shot subscription would silently go stale the
/// first time a match restarted.
///
/// Crossfades use two ping-ponging AudioSources rather than a mixer snapshot fade: a snapshot can
/// only move one group's volume, so it cannot overlap an outgoing and an incoming track.
/// </summary>
public class MusicDirector
{
    private AudioManager manager;
    private AudioConfig config;

    private AudioSource[] bedSources;      // 2, ping-ponged for crossfades
    private int activeBed;
    private AudioSource ambientSource;

    private MusicTrackId currentBed = MusicTrackId.None;
    private MixerSnapshotId currentSnapshot = (MixerSnapshotId)255;   // force the first apply
    private bool ambientRunning;

    private float fadeTimer;
    private float fadeDuration;
    private bool fading;

    private MatchManager lastMatch;
    private MatchPhase lastPhase;
    private bool hadMatch;

    private int lastCountdownSecond = -1;

    private AudioMixerSnapshot[] snapshotCache;

    public void Initialize(AudioManager owner, AudioConfig cfg)
    {
        manager = owner;
        config = cfg;

        bedSources = new AudioSource[2];
        for (int i = 0; i < 2; i++) bedSources[i] = CreateSource($"MusicBed_{i}", AudioBus.Music);
        ambientSource = CreateSource("AmbientBed", AudioBus.Ambient);

        CacheSnapshots();
        Apply(MusicState.Resolve(hasMatch: false, MatchPhase.Warmup, 0, 0), instant: true);
    }

    private AudioSource CreateSource(string sourceName, AudioBus bus)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(manager.transform, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = 0f;
        src.outputAudioMixerGroup = manager.ResolveGroup(bus);
        return src;
    }

    private void CacheSnapshots()
    {
        snapshotCache = new AudioMixerSnapshot[4];
        if (config.Mixer == null) return;

        foreach (MixerSnapshotId id in System.Enum.GetValues(typeof(MixerSnapshotId)))
        {
            AudioMixerSnapshot snapshot = config.Mixer.FindSnapshot(id.ToString());
            if (snapshot == null)
                Debug.LogError($"[Audio] Mixer has no snapshot named '{id}'. Its ducking will not apply.");
            snapshotCache[(int)id] = snapshot;
        }
    }

    public void Tick(float unscaledDeltaTime)
    {
        PollMatchState();
        AdvanceCrossfade(unscaledDeltaTime);
        AdvanceCountdownTick();
    }

    /// <summary>Re-derives the plan whenever the match object or its phase changes. One reference
    /// compare and one enum compare per frame; nothing is allocated unless the state actually
    /// moved.</summary>
    private void PollMatchState()
    {
        MatchManager live = MatchManager.Instance;
        bool hasMatch = live != null;

        bool changed = hasMatch != hadMatch
                       || live != lastMatch
                       || (hasMatch && live.Phase != lastPhase);
        if (!changed) return;

        lastMatch = live;
        hadMatch = hasMatch;
        lastPhase = hasMatch ? live.Phase : MatchPhase.Warmup;

        int winner = hasMatch ? live.Winner : MusicState.WinnerDraw;
        Apply(MusicState.Resolve(hasMatch, lastPhase, winner, ResolveLocalTeamNumber()), instant: false);
    }

    /// <summary>The local player's team as a plain number, or 0 when it can't be resolved
    /// (spectator, pre-spawn, or a team that hasn't replicated). 0 always yields the neutral
    /// stinger — see MusicState.ResolveStinger.</summary>
    private static int ResolveLocalTeamNumber()
    {
        NetworkRunner runner = MatchManager.Instance != null ? MatchManager.Instance.Runner : null;
        if (runner == null) return MusicState.TeamNone;
        if (!runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject localObject)) return MusicState.TeamNone;
        if (localObject == null) return MusicState.TeamNone;

        PlayerTeamData teamData = localObject.GetComponent<PlayerTeamData>();
        return teamData != null ? TeamUtil.ToNumber(teamData.Team) : MusicState.TeamNone;
    }

    private void Apply(MusicPlan plan, bool instant)
    {
        ApplySnapshot(plan.Snapshot, instant);
        ApplyBed(plan.Bed, instant);
        ApplyAmbient(plan.Ambient);

        if (plan.Stinger != AudioCueId.None) Audio.PlayUi(plan.Stinger);
    }

    private void ApplySnapshot(MixerSnapshotId id, bool instant)
    {
        if (id == currentSnapshot) return;
        currentSnapshot = id;

        AudioMixerSnapshot snapshot = snapshotCache != null ? snapshotCache[(int)id] : null;
        if (snapshot == null) return;

        snapshot.TransitionTo(instant ? 0f : SnapshotTransitionSeconds(id));
    }

    private static float SnapshotTransitionSeconds(MixerSnapshotId id)
    {
        switch (id)
        {
            case MixerSnapshotId.Stinger: return 0.2f;
            case MixerSnapshotId.SuddenDeath: return 1.5f;
            default: return 0.5f;
        }
    }

    private void ApplyBed(MusicTrackId bed, bool instant)
    {
        if (bed == currentBed) return;
        currentBed = bed;

        AudioSource outgoing = bedSources[activeBed];
        activeBed = 1 - activeBed;
        AudioSource incoming = bedSources[activeBed];

        AudioClip clip = config.GetMusicClip(bed);
        incoming.clip = clip;
        incoming.volume = 0f;

        if (clip != null) incoming.Play();
        else incoming.Stop();

        fadeDuration = instant ? 0f : config.MusicCrossfadeSeconds;
        fadeTimer = 0f;
        fading = true;

        if (fadeDuration <= 0f)
        {
            outgoing.Stop();
            outgoing.volume = 0f;
            incoming.volume = clip != null ? 1f : 0f;
            fading = false;
        }
    }

    /// <summary>Equal-power crossfade: linear volume ramps would dip audibly through the middle of
    /// the transition, because two uncorrelated tracks sum in power, not amplitude.</summary>
    private void AdvanceCrossfade(float unscaledDeltaTime)
    {
        if (!fading) return;

        fadeTimer += unscaledDeltaTime;
        float t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeTimer / fadeDuration);

        AudioSource incoming = bedSources[activeBed];
        AudioSource outgoing = bedSources[1 - activeBed];

        incoming.volume = Mathf.Sin(t * Mathf.PI * 0.5f);
        outgoing.volume = Mathf.Cos(t * Mathf.PI * 0.5f);

        if (t < 1f) return;

        outgoing.Stop();
        outgoing.volume = 0f;
        incoming.volume = incoming.clip != null ? 1f : 0f;
        fading = false;
    }

    /// <summary>
    /// One tick per whole second remaining during Countdown. Derived from the networked phase
    /// timer, so every peer counts down together without a per-second RPC. The second counter
    /// resets whenever the phase is not Countdown, so re-entering it starts clean.
    /// </summary>
    private void AdvanceCountdownTick()
    {
        MatchManager match = MatchManager.Instance;
        if (match == null || match.Phase != MatchPhase.Countdown)
        {
            lastCountdownSecond = -1;
            return;
        }

        float? remaining = match.PhaseTimeRemaining;
        if (!remaining.HasValue) return;

        int second = Mathf.CeilToInt(remaining.Value);
        if (second <= 0 || second == lastCountdownSecond) return;

        lastCountdownSecond = second;
        Audio.PlayUi(AudioCueId.CountdownTick);
    }

    private void ApplyAmbient(bool shouldRun)
    {
        if (shouldRun == ambientRunning) return;
        ambientRunning = shouldRun;

        if (!shouldRun)
        {
            ambientSource.Stop();
            return;
        }

        AudioClip clip = config.GetMusicClip(MusicTrackId.ArenaAmbientBed);
        if (clip == null) return;

        ambientSource.clip = clip;
        ambientSource.volume = 1f;
        ambientSource.Play();
    }

    public void Shutdown()
    {
        if (bedSources != null)
            foreach (AudioSource src in bedSources) if (src != null) src.Stop();
        if (ambientSource != null) ambientSource.Stop();
    }
}
