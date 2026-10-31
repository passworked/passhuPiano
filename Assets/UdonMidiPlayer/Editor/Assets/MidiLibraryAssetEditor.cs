using System;
using System.Collections.Generic;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Editor.Prefab;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Assets
{
    [CustomEditor(typeof(MidiLibraryAsset))]
    public sealed class MidiLibraryAssetEditor : global::UnityEditor.Editor
    {
        private ReorderableList folderList;
        private string result;
        private MessageType resultType;

        private void OnEnable()
        {
            SerializedProperty folders = serializedObject.FindProperty("folders");
            folderList = new ReorderableList(serializedObject, folders, true, true, true, true);
            folderList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, MidiEditorI18n.Tr("Favorite folders (drag handles to reorder)"));
            folderList.drawElementCallback = (rect, index, active, focused) =>
            {
                SerializedProperty folder = folders.GetArrayElementAtIndex(index);
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                Rect count = rect;
                count.xMin = Mathf.Max(rect.xMin, rect.xMax - 64);
                rect.xMax = count.xMin - 4;
                EditorGUI.PropertyField(rect, folder.FindPropertyRelative("path"), GUIContent.none);
                EditorGUI.LabelField(count, MidiEditorI18n.Format("{0} songs", folder.FindPropertyRelative("songs").arraySize), EditorStyles.miniLabel);
            };
            folderList.onAddCallback = list =>
            {
                int index = folders.arraySize;
                folders.arraySize++;
                SerializedProperty folder = folders.GetArrayElementAtIndex(index);
                string path = MidiEditorI18n.Tr("New folder");
                int suffix = 2;
                var existing = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < index; i++) existing.Add(folders.GetArrayElementAtIndex(i).FindPropertyRelative("path").stringValue);
                while (existing.Contains(path)) path = MidiEditorI18n.Tr("New folder") + " " + suffix++;
                folder.FindPropertyRelative("path").stringValue = path;
                folder.FindPropertyRelative("songs").arraySize = 0;
                list.index = index;
            };
        }

        public override void OnInspectorGUI()
        {
            MidiEditorI18n.DrawLanguageSelector();
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(MidiEditorI18n.Tr("Script"), MonoScript.FromScriptableObject((MidiLibraryAsset)target), typeof(MonoScript), false);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("title"), MidiEditorI18n.Content("Library title"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("songs"), MidiEditorI18n.Content("All songs"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultSongId"), MidiEditorI18n.Content("Default song ID"));
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(MidiEditorI18n.Tr("Add songs to the full library before assigning favorites. Use / for nested folders, e.g. Game Music/Arcaea; parent folders appear automatically. Songs may belong to multiple folders, and empty folders are retained. Browsing folders does not interrupt playback."), MessageType.Info);
            folderList.DoLayoutList();
            SerializedProperty folders = serializedObject.FindProperty("folders");
            if (folderList.index >= 0 && folderList.index < folders.arraySize)
            {
                SerializedProperty folder = folders.GetArrayElementAtIndex(folderList.index);
                EditorGUILayout.LabelField(MidiEditorI18n.Tr("Selected folder"), EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(folder.FindPropertyRelative("path"), MidiEditorI18n.Content("Folder path"));
                SerializedProperty songs = folder.FindPropertyRelative("songs");
                songs.isExpanded = true;
                EditorGUILayout.PropertyField(songs, MidiEditorI18n.Content("Folder songs (display order)"), true);
            }
            serializedObject.ApplyModifiedProperties();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(MidiEditorI18n.Tr("Check folders"))) CheckFolders();
                if (GUILayout.Button(MidiEditorI18n.Tr("Save and sync scene folders"))) SaveAndSynchronize();
            }
            EditorGUILayout.HelpBox(MidiEditorI18n.Tr("Synchronization updates menus on players using this library in open scenes. Save the scenes afterward. Use Update Player UI to update menus in a shared Prefab."), MessageType.None);
            if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, resultType);
        }

        private void CheckFolders()
        {
            try
            {
                MidiDataBaker.ValidateFolders((MidiLibraryAsset)target);
                result = MidiEditorI18n.Tr("Folder validation passed.");
                resultType = MessageType.Info;
            }
            catch (Exception exception) { ShowError(exception); }
        }

        private void SaveAndSynchronize()
        {
            try
            {
                var library = (MidiLibraryAsset)target;
                MidiDataBaker.ValidateLibrary(library);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                var messages = new List<string>();
                MidiPlayerBuilder.SynchronizeLibraryPlayers(library, message => messages.Add(MidiEditorI18n.LocalizeDiagnostic(message)));
                result = string.Join("\n", messages);
                resultType = MessageType.Info;
            }
            catch (Exception exception) { ShowError(exception); }
        }

        private void ShowError(Exception exception)
        {
            result = MidiEditorI18n.LocalizeDiagnostic(exception.Message);
            resultType = MessageType.Error;
        }
    }
}
