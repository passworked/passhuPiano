using System;
using System.IO;
using MusicMidiPlayer.Logic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiWaterfallEnvironmentBake
    {
        public const string TexturePath = "Assets/UdonMidiPlayer/Art/Waterfall/WaterfallEnvironment.exr";

        [MenuItem("Tools/Music MIDI Player/Bake Waterfall Environment Reflection")]
        public static void BakeFromMenu() { Debug.Log(BakeCurrentScene()); }

        public static string BakeCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后烘焙环境反射");
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            MidiPlayerFacade player = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<MidiPlayerFacade>(true))
            {
                if (candidate.gameObject.scene != active) continue;
                if (player != null) throw new InvalidOperationException("当前场景有多个播放器，请先只保留一个目标再烘焙共享反射资源");
                player = candidate;
            }
            if (player == null || player.waterfall == null) throw new InvalidOperationException("当前场景找不到播放器");
            var sourceProbe = player.waterfall.GetComponentInChildren<ReflectionProbe>(true);
            if (sourceProbe == null) throw new InvalidOperationException("请先更新现有Prefab的瀑布流表现");
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player.waterfall.gameObject);
            if (string.IsNullOrEmpty(prefabPath)) throw new InvalidOperationException("目标必须连接到现有播放器Prefab");
            Import.MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Art/Waterfall");
            ReflectionProbe temporary = null;
            try
            {
                // Capture the loaded world with a temporary probe only. The user's probe,
                // scene lighting settings, lightmaps and unsaved scene content stay intact.
                temporary = UnityEngine.Object.Instantiate(sourceProbe);
                temporary.name = "MIDI Environment Capture (temporary)";
                temporary.gameObject.hideFlags = HideFlags.HideAndDontSave;
                temporary.transform.position = sourceProbe.transform.position;
                temporary.transform.rotation = sourceProbe.transform.rotation;
                temporary.mode = ReflectionProbeMode.Baked;
                temporary.resolution = 128;
                temporary.enabled = true;
                if (!Lightmapping.BakeReflectionProbe(temporary, TexturePath))
                    throw new InvalidOperationException("当前环境反射烘焙未完成，未修改Prefab反射引用");
            }
            finally { if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary.gameObject); }
            if (!File.Exists(TexturePath)) throw new IOException("环境反射文件未生成");
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(TexturePath);
            if (texture == null || texture.dimension != TextureDimension.Cube)
                throw new InvalidOperationException("烘焙结果不是有效Cubemap，未修改Prefab引用");
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var targetPlayer = contents.GetComponentInChildren<MidiPlayerFacade>(true);
                var probe = targetPlayer.waterfall.GetComponentInChildren<ReflectionProbe>(true);
                if (probe == null) throw new InvalidOperationException("Prefab反射探针缺失");
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = texture;
                probe.boxProjection = true;
                if (PrefabUtility.SaveAsPrefabAsset(contents, prefabPath) == null)
                    throw new InvalidOperationException("烘焙资源已保留，但Prefab保存失败");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return "Captured current-scene reflection: " + TexturePath + "; updated " + prefabPath + ". No scene or world lightmaps were saved/rebuilt.";
        }
    }
}
