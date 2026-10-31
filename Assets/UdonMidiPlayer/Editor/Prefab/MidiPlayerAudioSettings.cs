using System;
using MusicMidiPlayer.Audio;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Project-local opt-in: importing the player in another world must not
    // change that world's global audio configuration without authorization.
    [FilePath("ProjectSettings/MusicMidiPlayerAudioSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    [InitializeOnLoad]
    public sealed class MidiPlayerAudioSettings : ScriptableSingleton<MidiPlayerAudioSettings>
    {
        [SerializeField] private bool keepVoiceCapacity;
        private static double nextCheck;
        private static bool reportedFailure;

        static MidiPlayerAudioSettings()
        {
            EditorApplication.update += CheckAfterSdk;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static bool EnabledForProject { get { return instance.keepVoiceCapacity; } }

        public static void SetEnabledForProject(bool enabled)
        {
            instance.keepVoiceCapacity = enabled;
            instance.Save(true);
            if (enabled) ApplyNow();
        }

        private static void CheckAfterSdk()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 0.5;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try { ApplyNow(); reportedFailure = false; }
            catch (Exception exception)
            {
                if (!reportedFailure) Debug.LogError("[Music MIDI Player] Audio configuration: " + exception.Message);
                reportedFailure = true;
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) ApplyNow();
            if (state == PlayModeStateChange.EnteredEditMode) nextCheck = 0;
        }

        // The SDK configures defaults on reload and target changes. Its callback
        // order is not an audio budget contract; check after it settles, write
        // only when different, and also enforce before Play Mode / world builds.
        public static void ApplyNow()
        {
            if (!EnabledForProject || EditorApplication.isPlaying) return;
            const int capacity = PianoVoicePool.VoiceCapacity;
            UnityEngine.Object audioManager = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/AudioManager.asset");
            if (audioManager == null) throw new InvalidOperationException("AudioManager.asset is unavailable.");
            var settings = new SerializedObject(audioManager);
            SerializedProperty real = settings.FindProperty("m_RealVoiceCount");
            SerializedProperty managed = settings.FindProperty("m_VirtualVoiceCount");
            if (real.intValue != capacity || managed.intValue != capacity)
            {
                real.intValue = capacity;
                managed.intValue = capacity;
                settings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(audioManager);
            }
            // Serialized settings and the running Editor audio device can differ.
            // This Editor API is intentionally never called by an Udon program.
            AudioConfiguration configuration = AudioSettings.GetConfiguration();
            if (configuration.numRealVoices == capacity && configuration.numVirtualVoices == capacity) return;
            configuration.numRealVoices = capacity;
            configuration.numVirtualVoices = capacity;
            if (!AudioSettings.Reset(configuration)) throw new InvalidOperationException("Unable to apply 64-voice Editor audio configuration.");
            configuration = AudioSettings.GetConfiguration();
            if (configuration.numRealVoices != capacity || configuration.numVirtualVoices != capacity)
                throw new InvalidOperationException("The Editor audio device did not retain the requested voice capacity.");
        }
    }
}
