using System.Collections.Generic;
using MusicMidiPlayer.Editor.Import;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Assets
{
    internal static class MidiAssetPropertyLabels
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "m_Script", "Script" }, { "library", "Library" }, { "bank", "Piano bank" }, { "pianoModel", "Piano model" },
            { "voiceLimit", "Voice limit" }, { "visibleNoteLimit", "Visible note limit" }, { "windowMs", "View window (ms)" },
            { "viewHeight", "View height" }, { "defaultSongId", "Default song ID" }, { "songId", "Song ID" }, { "title", "Title" },
            { "author", "Author (optional)" }, { "contentHash", "Content hash" }, { "sourceGuid", "Source GUID" }, { "sourcePath", "Source path" },
            { "schemaVersion", "Schema version" }, { "durationMs", "Duration (ms)" }, { "startMs", "Note start (ms)" },
            { "keyUpMs", "Key up (ms)" }, { "releaseMs", "Release (ms)" }, { "pitch", "Pitch" }, { "velocity", "Velocity" },
            { "channel", "Channel" }, { "trackIndex", "Track index" }, { "eventMs", "Event time (ms)" }, { "eventKind", "Event kind" },
            { "eventNoteId", "Event note ID" }, { "checkpointEventIndex", "Checkpoint event indices" },
            { "checkpointActiveOffsets", "Checkpoint active offsets" }, { "checkpointActiveNoteIds", "Checkpoint active note IDs" },
            { "pedalMs", "Pedal time (ms)" }, { "pedalController", "Pedal controller" }, { "pedalValue", "Pedal value" },
            { "pedalOffsets", "Pedal offsets" }, { "importSettings", "Import settings" }, { "trackNames", "Track names" },
            { "diagnostics", "Diagnostics" }, { "sourceHash", "Source hash" }, { "importedSettingsHash", "Imported settings hash" },
            { "lastImportError", "Last import error" }, { "importFailed", "Import failed" }, { "tempoChangeCount", "Tempo changes" },
            { "maxKeyPolyphony", "Peak key polyphony" }, { "maxActivePolyphony", "Peak active polyphony" },
            { "estimatedDataBytes", "Estimated data bytes" }, { "packedDataBytes", "Packed data bytes" },
            { "energyStarts", "Energy offsets" }, { "energyCounts", "Energy point counts" }, { "energyValues", "Sample energy (20 ms)" },
            { "bankHash", "Bank hash" }, { "clips", "Audio clips" }, { "rootPitch", "Root pitches" }, { "lowPitch", "Low pitches" },
            { "highPitch", "High pitches" }, { "sourceRate", "Source sample rates" }, { "sourceChannels", "Source channels" },
            { "sourceFrameCount", "Source frame counts" }, { "sourcePeak", "Source peaks" }, { "regionGain", "Region gains" },
            { "tuningCents", "Tuning (cents)" }, { "releaseDurationMs", "Release durations (ms)" }, { "pitchToRegion", "Pitch to region" },
            { "maxReleaseMs", "Maximum release (ms)" }, { "sourceAttribution", "Source attribution" },
            { "redistributionLicense", "Redistribution license" }, { "selectedTracks", "Selected tracks" },
            { "excludePercussion", "Exclude channel 10 percussion" }, { "maxNotes", "Maximum notes" },
            { "maxCheckpointEntries", "Maximum checkpoint entries" }, { "maxDataBytes", "Maximum data bytes" }
        };

        internal static GUIContent Content(SerializedProperty property)
        {
            string english;
            return MidiEditorI18n.Content(Labels.TryGetValue(property.name, out english) ? english : property.displayName, property.tooltip);
        }
    }

    public abstract class MidiLocalizedAssetEditor : global::UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MidiEditorI18n.DrawLanguageSelector();
            serializedObject.Update();
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name == "diagnostics" && property.isArray)
                {
                    property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, MidiAssetPropertyLabels.Content(property), true);
                    if (property.isExpanded)
                    {
                        EditorGUI.indentLevel++;
                        for (int i = 0; i < property.arraySize; i++)
                            EditorGUILayout.HelpBox(MidiEditorI18n.LocalizeDiagnostic(property.GetArrayElementAtIndex(i).stringValue), MessageType.None);
                        EditorGUI.indentLevel--;
                    }
                }
                else if (property.name == "lastImportError")
                {
                    EditorGUILayout.LabelField(MidiAssetPropertyLabels.Content(property));
                    if (!string.IsNullOrEmpty(property.stringValue)) EditorGUILayout.HelpBox(MidiEditorI18n.LocalizeDiagnostic(property.stringValue), MessageType.Error);
                }
                else
                    using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                        EditorGUILayout.PropertyField(property, MidiAssetPropertyLabels.Content(property), true);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(MidiPlayerConfigurationAsset)), CanEditMultipleObjects]
    public sealed class MidiPlayerConfigurationAssetEditor : MidiLocalizedAssetEditor { }

    [CustomEditor(typeof(MidiSongAsset)), CanEditMultipleObjects]
    public sealed class MidiSongAssetEditor : MidiLocalizedAssetEditor { }

    [CustomEditor(typeof(PianoBankAsset)), CanEditMultipleObjects]
    public sealed class PianoBankAssetEditor : MidiLocalizedAssetEditor { }

    [CustomPropertyDrawer(typeof(MidiImportSettings))]
    public sealed class MidiImportSettingsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (property.isExpanded)
            {
                SerializedProperty child = property.Copy();
                SerializedProperty end = child.GetEndProperty();
                if (child.NextVisible(true))
                    do
                    {
                        if (SerializedProperty.EqualContents(child, end)) break;
                        height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
                    } while (child.NextVisible(false));
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(position, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                SerializedProperty child = property.Copy();
                SerializedProperty end = child.GetEndProperty();
                if (child.NextVisible(true))
                    do
                    {
                        if (SerializedProperty.EqualContents(child, end)) break;
                        position.y += position.height + EditorGUIUtility.standardVerticalSpacing;
                        position.height = EditorGUI.GetPropertyHeight(child, true);
                        EditorGUI.PropertyField(position, child, MidiAssetPropertyLabels.Content(child), true);
                    } while (child.NextVisible(false));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
