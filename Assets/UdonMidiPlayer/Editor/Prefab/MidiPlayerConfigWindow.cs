using System;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Logic;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    public sealed class MidiPlayerConfigWindow : global::UnityEditor.EditorWindow
    {
        private GameObject target;
        private MidiPlayerConfigurationAsset configuration;
        private MidiLibraryAsset library;
        private PianoBankAsset bank;
        private GameObject pianoModel;
        private string result;
        private bool busy;

        [MenuItem("Tools/Music MIDI Player/Configure Player")]
        public static void Open()
        {
            var window = GetWindow<MidiPlayerConfigWindow>(MidiEditorI18n.Tr("MIDI Player Configuration"));
            if (Selection.activeGameObject != null)
            {
                MidiPlayerFacade facade = Selection.activeGameObject.GetComponentInChildren<MidiPlayerFacade>(true);
                if (facade != null && facade.transform.parent != null && facade.transform.parent.parent != null) window.target = facade.transform.parent.parent.gameObject;
                else if (Selection.activeGameObject.name == "UdonMidiPlayer") window.target = Selection.activeGameObject;
            }
        }

        private void OnGUI()
        {
            titleContent = MidiEditorI18n.Content("MIDI Player Configuration");
            MidiEditorI18n.DrawLanguageSelector();
            EditorGUILayout.LabelField(MidiEditorI18n.Tr("MIDI Player Configuration"), EditorStyles.boldLabel);
            target = (GameObject)EditorGUILayout.ObjectField(MidiEditorI18n.Tr("Scene player root"), target, typeof(GameObject), true);
            EditorGUI.BeginChangeCheck();
            configuration = (MidiPlayerConfigurationAsset)EditorGUILayout.ObjectField(MidiEditorI18n.Tr("Saved configuration (optional)"), configuration, typeof(MidiPlayerConfigurationAsset), false);
            if (EditorGUI.EndChangeCheck() && configuration != null) { library = configuration.library; bank = configuration.bank; pianoModel = configuration.pianoModel; }
            library = (MidiLibraryAsset)EditorGUILayout.ObjectField(MidiEditorI18n.Tr("Library"), library, typeof(MidiLibraryAsset), false);
            bank = (PianoBankAsset)EditorGUILayout.ObjectField(MidiEditorI18n.Tr("Piano bank"), bank, typeof(PianoBankAsset), false);
            pianoModel = (GameObject)EditorGUILayout.ObjectField(MidiEditorI18n.Tr("Piano model (optional)"), pianoModel, typeof(GameObject), false);
            if (configuration != null)
            {
                var serialized = new SerializedObject(configuration);
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("voiceLimit"), MidiEditorI18n.Content("Voice limit"));
                EditorGUILayout.PropertyField(serialized.FindProperty("visibleNoteLimit"), MidiEditorI18n.Content("Visible note limit"));
                EditorGUILayout.PropertyField(serialized.FindProperty("windowMs"), MidiEditorI18n.Content("View window (ms)"));
                EditorGUILayout.PropertyField(serialized.FindProperty("viewHeight"), MidiEditorI18n.Content("View height"));
                EditorGUILayout.PropertyField(serialized.FindProperty("defaultSongId"), MidiEditorI18n.Content("Default song ID"));
                serialized.ApplyModifiedProperties();
            }
            EditorGUILayout.HelpBox(MidiEditorI18n.Tr("Bakes the library, connects Udon, configures the voice pool and keyboard/waterfall, and creates controls. Apply to a scene instance. Changes are not automatically applied to the shared Prefab."), MessageType.Info);
            using (new EditorGUI.DisabledScope(busy || target == null || library == null || bank == null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button(MidiEditorI18n.Tr("Apply configuration"))) Apply();
            using (new EditorGUI.DisabledScope(busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(MidiEditorI18n.Tr("Build / update complete demo Prefab and scene")))
                {
                    busy = true; result = MidiEditorI18n.Tr("Preparing resources and building the demo…");
                    MidiPlayerBuilder.BuildDemoAsync(
                        () => Complete(MidiEditorI18n.Tr("Demo Prefab and scene saved. Add the player, select a library, and apply the configuration.")), Fail);
                }
            }
            if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.None);
        }

        private void Apply()
        {
            busy = true; result = MidiEditorI18n.Tr("Preparing resources and applying configuration…");
            GameObject requestedTarget = target;
            MidiPlayerConfigurationAsset requestedConfiguration = configuration;
            MidiLibraryAsset requestedLibrary = library;
            PianoBankAsset requestedBank = bank;
            GameObject requestedModel = pianoModel;
            MidiPlayerBuilder.PrepareResources(() =>
            {
                if (requestedTarget == null || requestedLibrary == null || requestedBank == null)
                    throw new InvalidOperationException(MidiEditorI18n.Tr("The target or source assets were removed while waiting for resource import."));
                if (requestedConfiguration != null)
                {
                    Undo.RecordObject(requestedConfiguration, "Configure MIDI Source Assets");
                    requestedConfiguration.library = requestedLibrary; requestedConfiguration.bank = requestedBank;
                    requestedConfiguration.pianoModel = requestedModel;
                    EditorUtility.SetDirty(requestedConfiguration); AssetDatabase.SaveAssetIfDirty(requestedConfiguration);
                    MidiPlayerBuilder.Configure(requestedTarget, requestedConfiguration);
                }
                else MidiPlayerBuilder.Configure(requestedTarget, requestedLibrary, requestedBank, requestedModel);
                Complete(MidiEditorI18n.Tr("Configuration applied. Save the modified scene and verify playback."));
            }, Fail);
        }

        private void Complete(string message)
        { busy = false; result = message; if (this != null) Repaint(); }
        private void Fail(Exception exception)
        { busy = false; result = MidiEditorI18n.Format("Operation failed: {0}", MidiEditorI18n.LocalizeDiagnostic(exception.Message)); Debug.LogException(exception); if (this != null) Repaint(); }
    }
}
