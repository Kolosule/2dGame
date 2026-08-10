using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

public class AudioAssetIntegrityTests
{
    // Duplicated from SettingsService.cs:26-29 as literals: SettingsService lives in
    // Assembly-CSharp, which no asmdef can reference. If these ever diverge, that IS the bug this
    // test exists to catch — the four volume sliders would silently stop working.
    private static readonly string[] RequiredExposedParams =
    {
        "MasterVolume", "MusicVolume", "SfxVolume", "UiVolume"
    };

    private static AudioConfig LoadConfig()
    {
        AudioConfig config = Resources.Load<AudioConfig>("AudioConfig");
        Assert.IsNotNull(config, "Resources/AudioConfig.asset is missing — the game boots silent.");
        return config;
    }

    [Test]
    public void AudioConfig_ExistsWithAMixerAndABank()
    {
        AudioConfig config = LoadConfig();
        Assert.IsNotNull(config.Mixer, "AudioConfig has no AudioMixer assigned.");
        Assert.IsNotNull(config.Bank, "AudioConfig has no SoundBank assigned.");
    }

    [Test]
    public void EveryCueId_HasABankEntryWithAPlayableClip()
    {
        SoundBank bank = LoadConfig().Bank;
        var missing = new List<string>();

        foreach (AudioCueId id in System.Enum.GetValues(typeof(AudioCueId)))
        {
            if (id == AudioCueId.None) continue;

            if (!bank.TryGet(id, out SoundCue cue)) missing.Add($"{id}: no bank entry");
            else if (!cue.HasClip) missing.Add($"{id}: entry has no clip");
        }

        Assert.IsEmpty(missing,
            "Every AudioCueId must resolve to a bank entry with at least one clip:\n"
            + string.Join("\n", missing));
    }

    [Test]
    public void EveryBankEntry_RoutesToAGroupThatExistsInTheMixer()
    {
        AudioConfig config = LoadConfig();
        var missing = new List<string>();

        foreach (SoundCue cue in config.Bank.Cues)
        {
            if (cue == null || cue.id == AudioCueId.None) continue;

            AudioMixerGroup[] groups = config.Mixer.FindMatchingGroups(cue.bus.ToString());
            if (groups == null || groups.Length == 0) missing.Add($"{cue.id} -> '{cue.bus}'");
        }

        Assert.IsEmpty(missing, "Cues routed to mixer groups that do not exist:\n" + string.Join("\n", missing));
    }

    [Test]
    public void Mixer_ExposesExactlyTheFourContractedVolumeParameters()
    {
        AudioMixer mixer = LoadConfig().Mixer;

        foreach (string param in RequiredExposedParams)
            Assert.IsTrue(mixer.GetFloat(param, out _),
                $"Mixer does not expose '{param}'. SettingsService.ApplyAudio would silently no-op for it.");
    }

    [Test]
    public void EverySnapshotNameInTheEnum_ExistsInTheMixer()
    {
        AudioMixer mixer = LoadConfig().Mixer;

        foreach (MixerSnapshotId id in System.Enum.GetValues(typeof(MixerSnapshotId)))
            Assert.IsNotNull(mixer.FindSnapshot(id.ToString()),
                $"Mixer has no snapshot named '{id}'.");
    }

    [Test]
    public void EveryMusicTrack_HasAClip()
    {
        AudioConfig config = LoadConfig();
        var missing = new List<string>();

        foreach (MusicTrackId id in System.Enum.GetValues(typeof(MusicTrackId)))
        {
            if (id == MusicTrackId.None) continue;
            if (config.GetMusicClip(id) == null) missing.Add(id.ToString());
        }

        Assert.IsEmpty(missing, "Music tracks with no clip assigned: " + string.Join(", ", missing));
    }

    // Licensing is a shipping requirement, not a nicety: an unlicensed file in Assets/Sound is a
    // legal problem, and "we'll remember to check" has already failed once in this project.
    [Test]
    public void EveryAudioFile_HasALicenseRow()
    {
        const string soundRoot = "Assets/Sound";
        const string licensePath = "Assets/Sound/LICENSES.md";

        Assert.IsTrue(File.Exists(licensePath), $"{licensePath} is missing.");
        string licenses = File.ReadAllText(licensePath);

        var undocumented = new List<string>();
        foreach (string path in Directory.GetFiles(soundRoot, "*.*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".wav" && extension != ".ogg" && extension != ".mp3") continue;

            string fileName = Path.GetFileName(path);
            if (!licenses.Contains(fileName)) undocumented.Add(fileName);
        }

        Assert.IsEmpty(undocumented,
            "Audio files with no row in LICENSES.md:\n" + string.Join("\n", undocumented));
    }
}
