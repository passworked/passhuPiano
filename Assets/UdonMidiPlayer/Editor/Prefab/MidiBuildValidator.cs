using System;
using System.Linq;
using System.Collections.Generic;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.UI;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace MusicMidiPlayer.Editor.Prefab
{
    public sealed class MidiBuildValidator : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder { get { return 0; } }

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType, BuildTarget buildTarget)
        {
            if (requestedBuildType == VRCSDKRequestedBuildType.Avatar) return true;
            try { MidiPlayerAudioSettings.ApplyNow(); Debug.Log(ValidateOpenPlayers()); return true; }
            catch (Exception exception) { Debug.LogError("[Music MIDI Player] 构建已阻止：" + exception.Message); return false; }
        }

        public static string ValidateOpenPlayers()
        {
            int count = 0;
            var errors = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                    foreach (MidiPlayerFacade facade in sceneRoot.GetComponentsInChildren<MidiPlayerFacade>(true))
                    {
                        count++;
                        try { ValidateRoot(FindPlayerRoot(facade).gameObject); }
                        catch (Exception exception) { errors.Add(scene.path + " / " + Hierarchy(facade.transform) + ": " + exception.Message); }
                    }
            }
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            return "Music MIDI Player validation passed: " + count + " open scene player(s). No world resources were modified.";
        }

        public static string ValidateRoot(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            MidiPlayerFacade[] facades = root.GetComponentsInChildren<MidiPlayerFacade>(true);
            Need(facades.Length == 1, "根对象须恰有一个 MidiPlayerFacade");
            MidiPlayerFacade facade = facades[0];
            int modelVariant = facade.GetStringsView(facade.selectedInstrument) != null ? 1 : facade.selectedInstrument;
            var validationKeyboard = facade.keyboard != null ? facade.keyboard : facade.instrumentKeyboards[modelVariant];
            var validationWaterfall = facade.waterfall != null ? facade.waterfall : facade.instrumentWaterfalls[modelVariant];
            var validationPedals = facade.pedals != null ? facade.pedals : facade.instrumentPedals[modelVariant];
            Need(FindPlayerRoot(facade) == root.transform, "必须提供完整播放器根对象");
            var midiListener = facade.GetComponent<VRC.SDK3.Midi.VRCMidiListener>();
            var midiEvents = VRC.SDK3.Midi.VRCMidiListener.MidiEvents.NoteOn
                | VRC.SDK3.Midi.VRCMidiListener.MidiEvents.NoteOff | VRC.SDK3.Midi.VRCMidiListener.MidiEvents.CC;
            Need(midiListener != null && midiListener.enabled && (midiListener.activeEvents & midiEvents) == midiEvents,
                "标准 MIDI 监听器须启用 NoteOn、NoteOff 和 CC");
            Need(new SerializedObject(midiListener).FindProperty("behaviour").objectReferenceValue
                == UdonSharpEditorUtility.GetBackingUdonBehaviour(facade), "MIDI 监听器须连接当前 Facade 的 UdonBehaviour");
            Need(facade.hardwareMidiChannelTargets != null && facade.hardwareMidiChannelTargets.Length == 16
                && facade.hardwareMidiChannelTargets.All(t => t >= -1 && t <= 6),
                "硬件 MIDI 通道映射须为16项：-1自动，0至6为乐器目标；第10通道固定鼓组");
            Bound(root, facade.library, "Facade.library"); Bound(root, facade.gate, "Facade.gate");
            Bound(root, facade.network, "Facade.network"); Bound(root, facade.clock, "Facade.clock");
            Bound(root, facade.sequencer, "Facade.sequencer"); Bound(root, facade.voicePool, "Facade.voicePool");
            if(facade.voicePool.drums!=null)
            {
                var drums=facade.voicePool.drums;
                Bound(root,drums,"Percussion.voices");Bound(root,drums.bank,"Percussion.bank");Bound(root,facade.drumView,"Percussion.view");
                Need(drums.output==facade.voicePool && drums.clock==facade.clock && facade.clock.drums==drums,"鼓组声音池/时钟接线不一致");
                Need(drums.bank.IsValid() && drums.bank.bankHash==MidiPercussionBuilder.ComputeBankHash(),"鼓组音源或导入设置已变化，请重新应用鼓组");
                Need(facade.drumView.bank==drums.bank && facade.drumView.lanes!=null
                    && facade.drumView.lanes.bank==drums.bank && facade.drumView.lanes.bars.Length==64
                    && facade.drumView.hitPoints.Length==9 && drums.hitPoints.Length==9,"鼓组模型/轨道接线不完整");
                Need(root.GetComponentsInChildren<AudioSource>(true).Length==PianoVoicePool.VoiceCapacity,"鼓组必须共用64个声音源");
                for(int lane=0;lane<9;lane++)
                {Bound(root,drums.hitPoints[lane],"Percussion.hit");Need(drums.hitPoints[lane]==facade.drumView.hitPoints[lane],"鼓组发声/画面落点串线");}
                Need(facade.playerUI.drumVisibilityLabel!=null && facade.playerUI.drumVisibilitySelectionMark!=null,"鼓组本地显示设置缺失");
            }
            Bound(root, validationKeyboard, "Facade.keyboard"); Bound(root, validationWaterfall, "Facade.waterfall"); Bound(root, facade.playerUI, "Facade.playerUI");
            Bound(root, validationPedals, "Facade.pedals");
            Need(validationWaterfall.keyboard == validationKeyboard, "瀑布流须复用对应键盘的哈希色调映射");
            Bound(root, validationWaterfall.effects, "Waterfall.effects");
            Need(validationWaterfall.effects.waterfall == validationWaterfall && validationWaterfall.effects.keyboard == validationKeyboard,
                "瀑布流效果引用串线");
            Need(validationWaterfall.volumeNoteMaterial != null && validationWaterfall.volumeNoteMaterial.shader != null
                && validationWaterfall.volumeNoteMaterial.shader.name == "MusicMidiPlayer/WaterfallVolume", "体积音符材质缺失");
            var effects = validationWaterfall.effects;
            Need(effects.hitParticles != null && effects.hitParticles.Length == 12 && effects.hitLights != null
                && effects.hitLights.Length == 8 && effects.maxActiveLights <= 4 && effects.totalParticleBudget <= 768,
                "粒子池或实时灯光预算未配置");
            Need(effects.symbolParticles != null && effects.symbolParticles.Length == 12
                && effects.symbolLayersEnabled && effects.totalSymbolBudget <= 192, "音乐符号粒子层未配置");
            foreach (var pool in effects.hitParticles)
            {
                Bound(root, pool, "Waterfall.hitParticles");
                Need(!pool.main.playOnAwake && pool.main.simulationSpace == ParticleSystemSimulationSpace.World
                    && pool.collision.enabled && !pool.emission.enabled, "粒子须按音乐手动推进并启用环境碰撞");
                Need(pool.main.gravityModifier.constant == 0 && pool.velocityOverLifetime.enabled
                    && pool.limitVelocityOverLifetime.enabled && pool.velocityOverLifetime.z.Evaluate(0) > 0
                    && pool.velocityOverLifetime.z.Evaluate(1) > 0
                    && pool.velocityOverLifetime.x.mode == pool.velocityOverLifetime.y.mode
                    && pool.velocityOverLifetime.y.mode == pool.velocityOverLifetime.z.mode,
                    "光尘须使用模式一致且受限的向上速度曲线");
            }
            var glyphMeshes = new HashSet<Mesh>();
            foreach (var pool in effects.symbolParticles)
            {
                Bound(root, pool, "Waterfall.symbolParticles");
                var renderer = pool.GetComponent<ParticleSystemRenderer>();
                Need(pool.main.maxParticles == 16 && !pool.main.playOnAwake && !pool.emission.enabled
                    && pool.velocityOverLifetime.enabled && pool.limitVelocityOverLifetime.enabled
                    && pool.velocityOverLifetime.x.mode == pool.velocityOverLifetime.y.mode
                    && pool.velocityOverLifetime.y.mode == pool.velocityOverLifetime.z.mode
                    && renderer != null && renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.meshCount == 4,
                    "符号粒子须有分组网格、上浮曲线及独立池容量");
                var meshes = new Mesh[renderer.meshCount]; renderer.GetMeshes(meshes);
                foreach (var mesh in meshes) { Need(mesh != null, "符号网格缺失"); glyphMeshes.Add(mesh); }
            }
            Need(glyphMeshes.Count == 6, "符号粒子池合计须覆盖全部6种音乐符号");
            foreach (var light in effects.hitLights)
            {
                Bound(root, light, "Waterfall.hitLights");
                Need(light.type == LightType.Point && light.shadows == LightShadows.None, "命中环境光须为无阴影点光源");
            }
            Need(root.GetComponentsInChildren<MusicMidiPlayer.Networking.MidiNetworkTransport>(true).Length == 1, "根对象须恰有一个网络Transport");
            Need(facade.gate.facade == facade && facade.network.facade == facade && facade.network.clock == facade.clock
                && facade.network.library == facade.library && facade.network.gate == facade.gate, "Network/Gate内部引用与Facade不一致");
            Need(facade.sequencer.voicePool == facade.voicePool && validationKeyboard.sequencer == facade.sequencer
                && facade.voicePool.clock == facade.clock && facade.voicePool.bank == facade.GetInstrumentBank(facade.selectedInstrument) && facade.playerUI.facade == facade, "音序器/音频/视图/UI引用串线");
            foreach (UdonSharpBehaviour behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
                Need(backing != null && backing.programSource != null, "缺少Udon backing/program: " + Hierarchy(behaviour.transform));
                var program = backing.programSource as UdonSharpProgramAsset;
                Need(program != null && program.CompiledVersion == UdonSharpProgramVersion.CurrentVersion && program.SerializedProgramAsset != null
                    && program.SerializedProgramAsset.RetrieveProgram() != null, "Udon程序未完成编译: " + behaviour.GetType().Name);
                var expected = behaviour == facade.network ? VRC.SDKBase.Networking.SyncType.Manual : VRC.SDKBase.Networking.SyncType.None;
                Need(backing.SyncMethod == expected, "同步模式错误: " + Hierarchy(behaviour.transform));
                if (behaviour != facade.network) Need(behaviour.gameObject != facade.network.gameObject, "None行为不能与网络组件共对象");
            }
            MidiLibraryData library = facade.library;
            Need(!string.IsNullOrEmpty(library.configurationAssetGuid), "Library.configurationAssetGuid为空，请应用配置");
            string configPath = AssetDatabase.GUIDToAssetPath(library.configurationAssetGuid);
            var config = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(configPath);
            Need(config != null, "配置源资产已缺失: " + library.configurationAssetGuid);
            MidiDataBaker.ValidateLibrary(config.library); MidiDataBaker.ValidateBank(config.bank);
            Need(library.catalogHash == MidiInstrumentMigration.InstrumentCatalogHash(facade, MidiContentHash.Catalog(config.library, config.bank)), "烘焙catalogHash已过期: " + configPath);
            bool multiInstrument = facade.instrumentRoots != null && facade.instrumentRoots.Length > 0;
            if (multiInstrument)
            {
                int variants = facade.voicePool.strings == null ? 3 : 8;
                Need(facade.instrumentRoots.Length == variants && facade.instrumentBanks.Length == variants
                    && facade.instrumentKeyboards.Length == variants && facade.instrumentWaterfalls.Length == variants
                    && facade.instrumentPedals.Length == variants, "乐器选项接线不完整");
                Need(facade.instrumentBanks[0] == library.bank && facade.instrumentBanks[1] == library.bank, "两种钢琴外观必须共用同一音源");
                var marimbaSource = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(MidiInstrumentMigration.BankPath);
                MidiInstrumentMigration.ValidateMarimbaBudget(marimbaSource); CompareBank(marimbaSource, facade.instrumentBanks[2]);
                string[] events = { "_SelectDigitalPiano", "_SelectGrandPiano", "_SelectMarimba" };
                Need(facade.playerUI.instrumentOptionButtons.Length == variants, "乐器菜单缺失");
                for (int option = 0; option < 3; option++)
                {
                    Need(facade.HasInstrument(option), "无效乐器选项: " + option);
                    Bound(root, facade.instrumentRoots[option].transform, "Instrument.root");
                    Bound(root, facade.instrumentBanks[option], "Instrument.bank");
                    var view = facade.instrumentKeyboards[option];
                    var fall = facade.instrumentWaterfalls[option];
                    Bound(root, view, "Instrument.keyboard"); Bound(root, fall, "Instrument.waterfall");
                    Need(view.sequencer == facade.sequencer && fall.keyboard == view, "乐器输入/视图串线");
                    int physical = 0;
                    for (int key = 0; key < 88; key++)
                    {
                        bool expected = option != 2 || (key + 21 >= 36 && key + 21 <= 96);
                        Need((view.keyTransforms[key] != null) == expected && (view.keyRenderers[key] != null) == expected, "实体音域映射错误");
                        if (expected) { Bound(root, view.keyTransforms[key], "Instrument.key"); physical++; }
                        Need(fall.keyWidth[key] > 0 && (key == 0 || fall.keyX[key] > fall.keyX[key - 1]), "域外音符布局错误");
                    }
                    Need(physical == (option == 2 ? 61 : 88), "实体琴键/琴条数量错误");
                    CheckEvent(facade.playerUI.instrumentOptionButtons[option], "m_OnClick", facade.playerUI.instrumentOptionButtons[option].onClick, facade.playerUI, events[option]);
                }
                if (facade.voicePool.strings != null) ValidateStrings(root, facade);
                if (facade.instrumentRouter != null)
                {
                    var router = facade.instrumentRouter;
                    var pool = facade.voicePool.ensemble;
                    Bound(root, router, "Instrument.router"); Bound(root, pool, "Instrument.ensemblePool");
                    Need(router.facade == facade && pool.output == facade.voicePool && pool.router == router
                        && facade.clock.instrumentRouter == router, "多音色路由接线错误");
                    Need(router.rulesHash == MidiInstrumentRoutingBuilder.Fingerprint()
                        && router.programGroup.SequenceEqual(MidiInstrumentRoutingBuilder.ProgramGroups()), "音色分组或例外已过期");
                    MidiInstrumentRoutingBuilder.ValidateRules(router);
                    Need(!facade.instrumentRoots[7].activeSelf && !facade.playerUI.instrumentOptionButtons[7].gameObject.activeSelf,
                        "弦乐组已停用，不应再显示模型或菜单入口");
                    var mapping = facade.playerUI;
                    Bound(root, mapping.fileMappingEntry, "UI.fileMappingEntry");
                    Bound(root, mapping.fileMappingPanel.transform, "UI.fileMappingPanel");
                    Need(mapping.fileMappingEntry.transform.parent == mapping.transform && mapping.fileMappingPanel.transform.parent == mapping.transform
                        && mapping.fileProgramButtons.Length == 16 && mapping.fileTargetButtons.Length == 7,
                        "播放音色映射必须直接接入播放器，并包含16行与自动/六种乐器");
                    CheckEvent(mapping.fileMappingEntry, "m_OnClick", mapping.fileMappingEntry.onClick, mapping, "_OpenFileMapping");
                    for (int row = 0; row < 16; row++)
                        CheckEvent(mapping.fileProgramButtons[row], "m_OnClick", mapping.fileProgramButtons[row].onClick, mapping, "_FileProgram" + row);
                    for (int target = 0; target < 7; target++)
                        CheckEvent(mapping.fileTargetButtons[target], "m_OnClick", mapping.fileTargetButtons[target].onClick, mapping, "_MidiTarget" + target);
                    Need(router.enabledMask >= 0 && router.enabledMask <= 63 && router.pianoAppearance >= 0
                        && router.pianoAppearance <= 1, "乐器复选框状态无效");
                    Need(facade.playerUI.instrumentToggles.Length == variants, "乐器复选框缺失");
                    for (int i = 0; i < variants; i++)
                    {
                        var toggle = facade.playerUI.instrumentToggles[i];
                        Bound(root, toggle, "Instrument.toggle");
                        Need(toggle.group == null && toggle.graphic != null && !facade.playerUI.instrumentOptionButtons[i].enabled,
                            "乐器多选必须使用独立复选框");
                        Need(toggle.onValueChanged.GetPersistentEventCount() == 1, "乐器复选框回调缺失或重复");
                    }
                    for (int i = 0; i < 3; i++)
                        Need(facade.instrumentKeyboards[i].instrumentRouter == router && facade.instrumentWaterfalls[i].instrumentRouter == router
                            && facade.instrumentKeyboards[i].instrumentTarget == router.TargetForView(i)
                            && facade.instrumentWaterfalls[i].instrumentTarget == router.TargetForView(i), "乐器表现路由串线");
                }
            }
            Bound(root, library.bank, "Library.bank");
            CompareBank(config.bank, library.bank);
            Need(library.songs != null && library.songs.Length == config.library.songs.Length, "烘焙曲目数量与配置不一致");
            var ids = new HashSet<string>();
            for (int i = 0; i < library.songs.Length; i++)
            {
                Bound(root, library.songs[i], "Library.songs[" + i + "]");
                Need(ids.Add(library.songs[i].songId), "重复songId: " + library.songs[i].songId);
                CompareSong(config.library.songs[i], library.songs[i]);
                Need((long)library.songs[i].durationMs + library.bank.maxReleaseMs <= int.MaxValue, "有效曲终时间溢出");
            }
            string expectedDefault = string.IsNullOrEmpty(config.defaultSongId) ? config.library.defaultSongId : config.defaultSongId;
            if (string.IsNullOrEmpty(expectedDefault)) expectedDefault = config.library.songs[0].songId;
            Need(library.defaultSongId == expectedDefault && ids.Contains(expectedDefault), "默认曲目未正确烘焙");
            var voices = facade.voicePool;
            Need(voices.voiceSources != null && voices.voiceSources.Length == PianoVoicePool.VoiceCapacity && voices.voiceLimit == config.voiceLimit
                && voices.voiceLimit >= 1 && voices.voiceLimit <= PianoVoicePool.VoiceCapacity, "声音池应为64个独立AudioSource且voiceLimit匹配配置");
            Need(voices.GetComponentsInChildren<AudioSource>(true).Length == PianoVoicePool.VoiceCapacity, "声音池存在额外AudioSource残留");
            var voiceObjects = new HashSet<AudioSource>();
            foreach (AudioSource source in voices.voiceSources)
            {
                Bound(root, source, "VoicePool.voiceSources");
                Need(voiceObjects.Add(source) && !source.playOnAwake && !source.loop && (Mathf.Approximately(source.spatialBlend, 0) || Mathf.Approximately(source.spatialBlend, 1)), "声音槽重复或AudioSource设置错误: " + source.name);
            }
            var keys = validationKeyboard; var waterfall = validationWaterfall;
            Need(keys.keyTransforms != null && keys.keyTransforms.Length == 88 && keys.keyRenderers != null && keys.keyRenderers.Length == 88
                && keys.keyIsBlack != null && keys.keyIsBlack.Length == 88, "键盘需要完整88键数组");
            Need(keys.whiteMaterial != null && keys.blackMaterial != null && keys.whitePressedMaterial != null && keys.blackPressedMaterial != null, "琴键基础/高亮材质缺失");
            Need(keys.keyRestMaterials != null && keys.keyRestMaterials.Length == 88 && keys.keyPressOffsets != null && keys.keyPressOffsets.Length == 88, "每键原材质/下压偏移未烘焙");
            Need(waterfall.keyX != null && waterfall.keyX.Length == 88 && waterfall.keyWidth != null && waterfall.keyWidth.Length == 88
                && waterfall.keyIsBlack != null && waterfall.keyIsBlack.Length == 88, "瀑布流音高映射须为88项");
            Need(waterfall.keyHitPositions != null && waterfall.keyHitPositions.Length == 88, "瀑布流88键落点未烘焙");
            var uniqueKeys = new HashSet<Transform>();
            for (int i = 0; i < 88; i++)
            {
                if (multiInstrument && modelVariant == 2 && (i + 21 < 36 || i + 21 > 96)) continue;
                Bound(root, keys.keyTransforms[i], "Keyboard.keyTransforms[" + i + "]"); Bound(root, keys.keyRenderers[i], "Keyboard.keyRenderers[" + i + "]");
                Need(uniqueKeys.Add(keys.keyTransforms[i]) && keys.keyRestMaterials[i] != null, "琴键引用重复或原材质缺失: " + (i + 21));
                Need(waterfall.keyWidth[i] > 0 && !float.IsNaN(waterfall.keyX[i]) && !float.IsInfinity(waterfall.keyX[i])
                    && (i == 0 || waterfall.keyX[i] > waterfall.keyX[i - 1]) && keys.keyIsBlack[i] == waterfall.keyIsBlack[i], "音高映射/宽度无效: " + (i + 21));
            }
            Need(waterfall.windowMs == config.windowMs && Mathf.Approximately(waterfall.viewHeight, config.viewHeight), "瀑布窗口参数未应用");
            Need(waterfall.barTransforms != null && waterfall.barRenderers != null && waterfall.barTransforms.Length == config.visibleNoteLimit
                && waterfall.barRenderers.Length == config.visibleNoteLimit && config.visibleNoteLimit >= 1 && config.visibleNoteLimit <= 512, "可见音符池数量与配置不一致");
            var uniqueBars = new HashSet<Transform>();
            for (int i = 0; i < waterfall.barTransforms.Length; i++)
            { Bound(root, waterfall.barTransforms[i], "Waterfall.barTransforms"); Bound(root, waterfall.barRenderers[i], "Waterfall.barRenderers"); Need(uniqueBars.Add(waterfall.barTransforms[i]), "可见条带槽重复"); }
            Need(waterfall.whiteNoteMaterial != null && waterfall.blackNoteMaterial != null && waterfall.whiteHitMaterial != null && waterfall.blackHitMaterial != null, "音符材质缺失");
            if (config.pianoModel != null && (!multiInstrument || modelVariant != 2))
            {
                Transform modelHost = multiInstrument ? facade.instrumentRoots[modelVariant].transform.Find("PianoModel") : root.transform.Find("Presentation/PianoModel");
                Need(modelHost != null && modelHost.gameObject.activeSelf && modelHost.childCount == 1, "已配置钢琴模型但实例缺失/不唯一");
                var modelSource = config.pianoModel;
                if (multiInstrument && modelVariant == 0)
                    modelSource = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(keys.keyTransforms[0].GetComponentInChildren<MeshFilter>().sharedMesh));
                string modelSourcePath = AssetDatabase.GetAssetPath(modelSource);
                bool sourceMatches = MidiPianoModelBinder.HasModelSource(modelHost.GetChild(0).gameObject, modelSourcePath);
                if (!sourceMatches && multiInstrument && modelSource != null)
                {
                    var expectedMeshes = new HashSet<Mesh>();
                    foreach (var filter in modelSource.GetComponentsInChildren<MeshFilter>(true)) expectedMeshes.Add(filter.sharedMesh);
                    sourceMatches = true;
                    foreach (var filter in modelHost.GetComponentsInChildren<MeshFilter>(true))
                        if (!expectedMeshes.Contains(filter.sharedMesh)) sourceMatches = false;
                }
                Need(!string.IsNullOrEmpty(modelSourcePath) && sourceMatches,
                    "钢琴模型源引用已改变，请重新应用配置");
                Need(keys.keyPivotPositions != null && keys.keyPivotPositions.Length == 88
                    && keys.keyRotationAxes != null && keys.keyRotationAxes.Length == 88
                    && keys.keyPressAngles != null && keys.keyPressAngles.Length == 88, "88键铰链动画数据未烘焙");
                for (int i = 0; i < 88; i++)
                    Need(keys.keyRotationAxes[i].sqrMagnitude > 0.99f && keys.keyRotationAxes[i].sqrMagnitude < 1.01f
                        && keys.keyPressAngles[i] < 0 && keys.keyPressAngles[i] > -30, "琴键铰链轴或按压角无效: " + (i + 21));
                string[] pedalNames = { "Pedal_UnaCorda", "Pedal_Sostenuto", "Pedal_Sustain" };
                int sourcePedalCount = 0;
                var sourcePedalNames = new HashSet<string>();
                foreach (Transform item in modelSource.GetComponentsInChildren<Transform>(true))
                    if (Array.IndexOf(pedalNames, item.name) >= 0)
                    { sourcePedalCount++; sourcePedalNames.Add(item.name); }
                Need(sourcePedalCount == 0 || (sourcePedalCount == 3 && sourcePedalNames.Count == 3),
                    "钢琴源模型踏板须为空，或含三个不重复的标准命名踏板");
                var pedals = validationPedals;
                Need(pedals.pedalTransforms != null && pedals.pedalTransforms.Length == sourcePedalCount
                    && pedals.pedalPivotPositions != null && pedals.pedalPivotPositions.Length == sourcePedalCount
                    && pedals.pedalRotationAxes != null && pedals.pedalRotationAxes.Length == sourcePedalCount
                    && pedals.pedalPressAngles != null && pedals.pedalPressAngles.Length == sourcePedalCount,
                    "踏板铰链动画绑定数量须与源模型一致: " + sourcePedalCount);
                for (int i = 0; i < sourcePedalCount; i++)
                {
                    Bound(root, pedals.pedalTransforms[i], "Pedals[" + i + "]");
                    Need(pedals.pedalTransforms[i].name == pedalNames[i] && pedals.pedalRotationAxes[i].sqrMagnitude > 0.99f
                        && pedals.pedalRotationAxes[i].sqrMagnitude < 1.01f && pedals.pedalPressAngles[i] < 0
                        && pedals.pedalPressAngles[i] > -30, "踏板顺序或铰链数据无效: " + i);
                }
                Transform backdrop = root.transform.Find("Presentation/ViewRoot/WaterfallBackdrop");
                Need(backdrop == null || !backdrop.gameObject.activeSelf, "真实钢琴模式不得启用实体屏幕Backdrop");
            }
            var ui = facade.playerUI;
            Bound(root, ui.titleLabel, "UI.titleLabel"); Bound(root, ui.stateLabel, "UI.stateLabel"); Bound(root, ui.timeLabel, "UI.timeLabel");
            Need(ui.titleLabel.font != null && ui.stateLabel.font != null && ui.timeLabel.font != null, "UI字体缺失");
            MidiPlayerFontAssets.ValidateUI(ui.transform, config.library);
            MidiPlayerLocalizationBuilder.Validate(ui);
            CheckEvent(ui.languageButton, "m_OnClick", ui.languageButton.onClick, ui, "_ToggleLanguagePanel");
            var languageDismiss = ui.languagePanel.GetComponent<UnityEngine.UI.Button>();
            Bound(root, languageDismiss, "UI.languagePanel.Button");
            CheckEvent(languageDismiss, "m_OnClick", languageDismiss.onClick, ui, "_CloseLanguagePanel");
            for (int i = 0; i < ui.languageOptionButtons.Length; i++)
                CheckEvent(ui.languageOptionButtons[i], "m_OnClick", ui.languageOptionButtons[i].onClick, ui, MidiPlayerLocalizationBuilder.SelectionEvents[i]);
            Bound(root, ui.playButton, "UI.playButton"); Bound(root, ui.pauseButton, "UI.pauseButton"); Bound(root, ui.stopButton, "UI.stopButton");
            CheckEvent(ui.playButton, "m_OnClick", ui.playButton.onClick, ui, "_OnPlayPressed");
            CheckEvent(ui.pauseButton, "m_OnClick", ui.pauseButton.onClick, ui, "_OnPausePressed");
            CheckEvent(ui.stopButton, "m_OnClick", ui.stopButton.onClick, ui, "_OnStopPressed");
            Bound(root, ui.progressSlider, "UI.progressSlider");
            Need(ui.progressSlider.handleRect != null && ui.progressSlider.GetComponent<UnityEngine.UI.Image>().raycastTarget,
                "进度条需要拖动手柄和射线交互");
            CheckEvent(ui.progressSlider, "m_OnValueChanged", ui.progressSlider.onValueChanged, ui, "_OnProgressChanged");
            var progressTrigger = ui.progressSlider.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            Need(progressTrigger != null, "进度条拖动事件缺失");
            string[] progressEvents = { "_OnProgressPointerDown", "_OnProgressPointerUp", "_OnProgressPointerUp" };
            var progressTypes = new[] { UnityEngine.EventSystems.EventTriggerType.PointerDown,
                UnityEngine.EventSystems.EventTriggerType.PointerUp, UnityEngine.EventSystems.EventTriggerType.EndDrag };
            for (int i = 0; i < progressTypes.Length; i++)
            {
                int index = progressTrigger.triggers.FindIndex(entry => entry.eventID == progressTypes[i]);
                Need(index >= 0, "进度条指针事件缺失: " + progressTypes[i]);
                CheckEvent(progressTrigger, "m_Delegates.Array.data[" + index + "].callback", progressTrigger.triggers[index].callback, ui, progressEvents[i]);
            }
            Bound(root, ui.volumeSlider, "UI.volumeSlider"); Bound(root, ui.muteToggle, "UI.muteToggle");
            CheckEvent(ui.volumeSlider, "m_OnValueChanged", ui.volumeSlider.onValueChanged, ui, "_OnVolumeChanged");
            CheckEvent(ui.muteToggle, "onValueChanged", ui.muteToggle.onValueChanged, ui, "_OnMuteChanged");
            Need(ui.songButtons != null && ui.songButtons.Length == library.songs.Length, "曲目按钮数与曲库不一致");
            for (int i = 0; i < ui.songButtons.Length; i++)
            {
                var button = ui.songButtons[i]; Bound(root, button, "UI.songButtons[" + i + "]");
                Need(button.facade == facade && button.songId == library.songs[i].songId, "曲目按钮引用/顺序错误: " + i);
                Bound(root, button.button, "SongButton.button"); Bound(root, button.selectionMark, "SongButton.selectionMark");
                Bound(root, button.numberLabel, "SongButton.numberLabel");
                CheckEvent(button.button, "m_OnClick", button.button.onClick, button, "_OnClick");
            }
            ValidateLibraryUI(root, ui, config.library);
            Canvas canvas = ui.GetComponent<Canvas>();
            Need(canvas != null && canvas.renderMode == RenderMode.WorldSpace && ui.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null
                && ui.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null, "UI需要WorldSpace Canvas、Raycaster与VRCUiShape");
            return "Validated " + root.name + " (" + library.songs.Length + " songs, 64 audio slots, 88 keys).";
        }

        private static void ValidateStrings(GameObject root, MidiPlayerFacade facade)
        {
            var voices = facade.voicePool.strings;
            Bound(root, voices, "Strings.voicePool");
            Need(voices.output == facade.voicePool && voices.clock == facade.clock && voices.banks.Length == 5, "弦乐音频接线错误");
            foreach (var bank in voices.banks)
            {
                Bound(root, bank, "Strings.bank");
                Need(bank.IsValid() && bank.bankHash == MidiStringsBuilder.BankHash(bank), "弦乐音源无效或哈希过期");
            }
            string[] events = { "_SelectViolin", "_SelectViola", "_SelectCello", "_SelectContrabass", "_SelectStrings" };
            for (int i = 3; i < (facade.instrumentRouter == null ? 8 : 7); i++)
            {
                Need(facade.HasInstrument(i), "弦乐选项缺失");
                var view = facade.GetStringsView(i); Bound(root, view, "Strings.view");
                int sections = i == 7 ? 5 : 1;
                Need(view.facade == facade && view.voices == voices && view.bodies.Length == sections
                    && view.bows.Length == sections && view.contacts.Length == sections && view.strings.Length == sections * 4
                    && view.openNotes.Length == sections * 4 && view.stringParticles.Length == sections * 4, "弦乐模型与表现接线不完整");
                foreach(var particle in view.stringParticles)
                { Bound(root,particle,"Strings.stringParticles"); Need(!particle.main.playOnAwake && particle.main.maxParticles==12,"弦乐粒子配置不正确"); }
                foreach(Transform child in view.transform) Need(!System.Text.RegularExpressions.Regex.IsMatch(child.name,@"^Note(?:Window|Hit)?\d+$"),"弦乐废弃钢琴窗对象尚未移除");
                foreach (var bow in view.bows) Bound(root, bow, "Strings.bow");
                foreach (var renderer in view.strings) Need(renderer != null && renderer.sharedMaterial.shader.name == "MusicMidiPlayer/StringVibration", "弦振动材质缺失");
                CheckEvent(facade.playerUI.instrumentOptionButtons[i], "m_OnClick", facade.playerUI.instrumentOptionButtons[i].onClick, facade.playerUI, events[i-3]);
            }
            var controls=facade.playerUI;
            bool independent = facade.instrumentRouter != null;
            int styleCount = independent ? 4 : 3;
            Need(controls.stringsStyleButtons != null && controls.stringsStyleButtons.Length == styleCount
                && controls.stringsStyleLabels != null && controls.stringsStyleLabels.Length == styleCount
                && controls.stringsStyleMarks != null && controls.stringsStyleMarks.Length == styleCount, "弦乐奏法菜单缺失");
            Bound(root,controls.instrumentDropdownScroll,"UI.instrumentDropdownScroll");
            Bound(root,controls.instrumentDropdownPanel,"UI.instrumentDropdownPanel");
            Bound(root,controls.instrumentDropdownViewport,"UI.instrumentDropdownViewport");
            Need(controls.instrumentDropdownScroll.content!=null && controls.instrumentDropdownScroll.viewport==controls.instrumentDropdownViewport,"乐器下拉列表接线错误");
            for(int i=0;i<controls.instrumentOptionButtons.Length;i++)
            {
                var rect=(RectTransform)controls.instrumentOptionButtons[i].transform;
                Need(rect.parent==controls.instrumentDropdownScroll.content && Mathf.Approximately(rect.anchoredPosition.x,0)
                    && Mathf.Approximately(rect.anchoredPosition.y,-20-i*40),"乐器须使用竖向下拉列表");
            }
            Bound(root,controls.stringsStyleDropdownButton,"UI.stringsStyleDropdownButton");
            Bound(root,controls.stringsStyleEntryLabel,"UI.stringsStyleEntryLabel");
            Bound(root,controls.stringsStylePanel != null ? controls.stringsStylePanel.transform : null,"UI.stringsStylePanel");
            Bound(root,controls.stringsStyleOptions != null ? controls.stringsStyleOptions.transform : null,"UI.stringsStyleOptions");
            Need(controls.stringsStylePanel.transform.GetSiblingIndex()>controls.instrumentDropdownViewport.GetSiblingIndex(),"奏法下拉须绘制在乐器列表上方");
            CheckEvent(controls.stringsStyleDropdownButton,"m_OnClick",controls.stringsStyleDropdownButton.onClick,controls,"_ToggleStringsStyleDropdown");
            string[] styles = { "_StringsSustain", "_StringsSpiccato", "_StringsPizzicato", "_StringsAutomatic" };
            for (int i = 0; i < styleCount; i++)
            {
                Bound(root, controls.stringsStyleButtons[i], "UI.stringsStyleButtons[" + i + "]");
                Bound(root, controls.stringsStyleLabels[i], "UI.stringsStyleLabels[" + i + "]");
                Bound(root, controls.stringsStyleMarks[i] != null ? controls.stringsStyleMarks[i].transform : null, "UI.stringsStyleMarks[" + i + "]");
                CheckEvent(controls.stringsStyleButtons[i], "m_OnClick", controls.stringsStyleButtons[i].onClick, controls, styles[i]);
            }
            if (independent)
            {
                Need(controls.stringStyleEntries != null && controls.stringStyleEntries.Length == 4
                    && controls.stringStyleEntryLabels != null && controls.stringStyleEntryLabels.Length == 4
                    && controls.stringStyleArrows != null && controls.stringStyleArrows.Length == 4, "四把提琴的独立奏法入口缺失");
                string[] entryEvents = { "_ToggleStringsStyleDropdown", "_ViolaStyle", "_CelloStyle", "_ContrabassStyle" };
                for (int i = 0; i < 4; i++)
                {
                    Bound(root, controls.stringStyleEntries[i], "UI.stringStyleEntries[" + i + "]");
                    Bound(root, controls.stringStyleEntryLabels[i], "UI.stringStyleEntryLabels[" + i + "]");
                    Bound(root, controls.stringStyleArrows[i], "UI.stringStyleArrows[" + i + "]");
                    Need(controls.stringStyleEntries[i].transform.parent == controls.instrumentOptionButtons[i + 3].transform,
                        "奏法入口须位于对应提琴行内: " + i);
                    CheckEvent(controls.stringStyleEntries[i], "m_OnClick", controls.stringStyleEntries[i].onClick, controls, entryEvents[i]);
                }
            }
        }

        private static void ValidateLibraryUI(GameObject root, MidiPlayerUI ui, MidiLibraryAsset library)
        {
            Bound(root, ui.librarySearchInput, "UI.librarySearchInput");
            Bound(root, ui.librarySearchPlaceholder, "UI.librarySearchPlaceholder");
            Bound(root, ui.librarySearchClearButton, "UI.librarySearchClearButton");
            Need(ui.songSearchKeys != null && ui.songSearchKeys.Length == library.songs.Length, "歌曲搜索索引与曲库不一致");
            RequireSerializedArray(ui, "songSearchKeys", ui.songSearchKeys);
            CheckEvent(ui.librarySearchInput, "m_OnValueChanged", ui.librarySearchInput.onValueChanged, ui, "_OnLibrarySearchChanged");
            CheckEvent(ui.librarySearchClearButton, "m_OnClick", ui.librarySearchClearButton.onClick, ui, "_ClearLibrarySearch");
            Bound(root, ui.libraryScroll, "UI.libraryScroll");
            Bound(root, ui.libraryScroll.viewport, "UI.libraryScroll.viewport");
            Bound(root, ui.libraryScroll.content, "UI.libraryScroll.content");
            Need(ui.libraryScroll.transform.IsChildOf(ui.transform)
                && ui.libraryScroll.viewport.IsChildOf(ui.libraryScroll.transform)
                && ui.libraryScroll.content.IsChildOf(ui.libraryScroll.viewport), "曲库滚动区域层级错误");
            Need(ui.libraryScroll.vertical && !ui.libraryScroll.horizontal, "曲库菜单须启用纵向滚动");
            Need(!float.IsNaN(ui.libraryRowHeight) && !float.IsInfinity(ui.libraryRowHeight) && ui.libraryRowHeight > 0
                && !float.IsNaN(ui.libraryRowSpacing) && !float.IsInfinity(ui.libraryRowSpacing)
                && ui.libraryRowSpacing >= ui.libraryRowHeight, "曲库行高或间距无效");
            Bound(root, ui.libraryBackButton, "UI.libraryBackButton");
            CheckEvent(ui.libraryBackButton, "m_OnClick", ui.libraryBackButton.onClick, ui, "_OnLibraryBackPressed");
            Bound(root, ui.libraryPathLabel, "UI.libraryPathLabel");
            Bound(root, ui.libraryCountLabel, "UI.libraryCountLabel");
            Bound(root, ui.libraryEmptyLabel, "UI.libraryEmptyLabel");
            Bound(root, ui.allSongsRow, "UI.allSongsRow");
            Need(ui.allSongsRow.parent == ui.libraryScroll.content, "全部曲目入口须位于曲库滚动内容内");
            var allSongsButton = ui.allSongsRow.GetComponent<UnityEngine.UI.Button>();
            Bound(root, allSongsButton, "UI.allSongsRow.Button");
            CheckEvent(allSongsButton, "m_OnClick", allSongsButton.onClick, ui, "_OnAllSongsPressed");
            foreach (MidiSongButton songButton in ui.songButtons)
                Need(songButton.transform.parent == ui.libraryScroll.content, "曲目行须位于曲库滚动内容内：" + songButton.songId);

            var paths = new List<string>();
            var pathIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            var explicitFolders = new Dictionary<string, MidiLibraryFolder>(StringComparer.Ordinal);
            foreach (MidiLibraryFolder folder in library.folders ?? Array.Empty<MidiLibraryFolder>())
            {
                explicitFolders.Add(folder.path, folder);
                string path = "";
                foreach (string segment in folder.path.Split('/'))
                {
                    path = path.Length == 0 ? segment : path + "/" + segment;
                    if (pathIndices.ContainsKey(path)) continue;
                    pathIndices.Add(path, paths.Count);
                    paths.Add(path);
                }
            }
            Need(ui.folderButtons != null && ui.folderButtons.Length == paths.Count, "收藏夹目录数量与配置不一致，请更新播放器菜单");
            Need(root.GetComponentsInChildren<MidiLibraryFolderButton>(true).Length == paths.Count, "存在过期或未接线的收藏夹目录行");
            for (int i = 0; i < paths.Count; i++)
            {
                MidiLibraryFolderButton folder = ui.folderButtons[i];
                string context = "UI.folderButtons[" + i + "]";
                Bound(root, folder, context);
                Need(folder.playerUI == ui && folder.folderIndex == i && folder.folderPath == paths[i], "收藏夹目录引用/路径/顺序错误：" + paths[i]);
                Need(folder.transform.parent == ui.libraryScroll.content, "收藏夹目录行须位于曲库滚动内容内：" + paths[i]);
                int slash = paths[i].LastIndexOf('/');
                int parentIndex = slash < 0 ? -1 : pathIndices[paths[i].Substring(0, slash)];
                Need(folder.parentFolderIndex == parentIndex, "收藏夹父目录接线错误：" + paths[i]);
                MidiLibraryFolder source;
                MidiSongAsset[] songs = explicitFolders.TryGetValue(paths[i], out source)
                    ? source.songs ?? Array.Empty<MidiSongAsset>() : Array.Empty<MidiSongAsset>();
                var indices = new int[songs.Length];
                for (int song = 0; song < songs.Length; song++) indices[song] = Array.IndexOf(library.songs, songs[song]);
                Need(Same(folder.songIndices, indices), "收藏夹曲目或显示顺序与源配置不一致：" + paths[i]);
                RequireSerializedArray(folder, "songIndices", indices);
                var button = folder.GetComponent<UnityEngine.UI.Button>();
                Bound(root, button, context + ".Button");
                CheckEvent(button, "m_OnClick", button.onClick, folder, "_OnClick");
            }
        }

        private static void CompareSong(MidiSongAsset source, MidiSongData target)
        {
            Need(source.songId == target.songId && source.title == target.title && source.contentHash == target.contentHash
                && source.sourceHash == target.sourceHash
                && source.schemaVersion == target.schemaVersion && source.durationMs == target.durationMs, "曲目身份/元信息烘焙已过期: " + source.title);
            Need(target.HasPackedData() && target.packedFormatVersion == 1 && target.noteCount == source.startMs.Length,
                "曲目压缩描述与源资产不一致: " + source.title);
            byte[] packed = target.packedData.bytes;
            Need(target.packedDataBytes == packed.Length && Same(MidiSongCodec.Encode(source), packed),
                "曲目压缩数据与源资产不一致，请重新烘焙: " + source.title);
            // Decode into an independent Editor object: validation must never refill the serialized
            // runtime descriptor with the large playback arrays it is meant to replace.
            MidiSongCodec.ValidateRoundTrip(source, packed);
            RequireSerializedValue(target, "packedData", target.packedData);
            RequireSerializedValue(target, "packedFormatVersion", target.packedFormatVersion);
            RequireSerializedValue(target, "noteCount", target.noteCount);
            RequireSerializedValue(target, "packedDataBytes", target.packedDataBytes);
            RequireEmptySongArray(target, "startMs", target.startMs);
            RequireEmptySongArray(target, "keyUpMs", target.keyUpMs);
            RequireEmptySongArray(target, "releaseMs", target.releaseMs);
            RequireEmptySongArray(target, "pitch", target.pitch);
            RequireEmptySongArray(target, "velocity", target.velocity);
            RequireEmptySongArray(target, "channel", target.channel);
            RequireEmptySongArray(target, "trackIndex", target.trackIndex);
            RequireEmptySongArray(target, "eventMs", target.eventMs);
            RequireEmptySongArray(target, "eventKind", target.eventKind);
            RequireEmptySongArray(target, "eventNoteId", target.eventNoteId);
            RequireEmptySongArray(target, "checkpointEventIndex", target.checkpointEventIndex);
            RequireEmptySongArray(target, "checkpointActiveOffsets", target.checkpointActiveOffsets);
            RequireEmptySongArray(target, "checkpointActiveNoteIds", target.checkpointActiveNoteIds);
            RequireEmptySongArray(target, "pedalMs", target.pedalMs);
            RequireEmptySongArray(target, "pedalController", target.pedalController);
            RequireEmptySongArray(target, "pedalValue", target.pedalValue);
            RequireEmptySongArray(target, "pedalOffsets", target.pedalOffsets);
        }

        private static void RequireEmptySongArray(MidiSongData target, string field, int[] value)
        {
            Need(value == null || value.Length == 0, "曲目描述中仍存在解码数组: " + target.title + "." + field);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            Need(backing != null, "曲目缺少Udon backing: " + target.title);
            int[] persisted;
            if (backing.publicVariables.TryGetVariableValue<int[]>(field, out persisted))
                Need(persisted == null || persisted.Length == 0,
                    "曲目Udon backing中仍持久化了解码数组: " + target.title + "." + field + "，请重新烘焙");
        }

        private static void RequireSerializedValue<T>(UdonSharpBehaviour proxy, string field, T expected)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            Need(backing != null, "缺少Udon backing: " + Hierarchy(proxy.transform));
            T persisted;
            Need(backing.publicVariables.TryGetVariableValue<T>(field, out persisted)
                && EqualityComparer<T>.Default.Equals(expected, persisted),
                "Udon backing.publicVariables中的压缩描述缺失或与proxy不一致: " + field + "，请重新烘焙");
        }
        private static void CompareBank(PianoBankAsset source, PianoBankData target)
        {
            Need(source.bankHash == target.bankHash && source.velocityLayerCount == target.velocityLayerCount && source.maxReleaseMs == target.maxReleaseMs && Same(source.clips, target.clips)
                && Same(source.rootPitch, target.rootPitch) && Same(source.lowPitch, target.lowPitch) && Same(source.highPitch, target.highPitch)
                && Same(source.sourceRate, target.sourceRate) && Same(source.sourceChannels, target.sourceChannels) && Same(source.sourceFrameCount, target.sourceFrameCount)
                && Same(source.sourcePeak, target.sourcePeak) && Same(source.regionGain, target.regionGain) && Same(source.tuningCents, target.tuningCents) && Same(source.releaseDurationMs, target.releaseDurationMs)
                && Same(source.pitchToRegion, target.pitchToRegion), "运行音源数组/Hash与源配置不一致");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            Need(backing != null, "音源缺少Udon backing，无法校验显式序列化变量");
            int[] serializedMap;
            Need(backing.publicVariables.TryGetVariableValue<int[]>("pitchToRegion", out serializedMap)
                && Same(target.pitchToRegion, serializedMap),
                "PianoBankData.pitchToRegion 未显式持久化到 backing.publicVariables 或与烘焙数据不一致；" +
                "不能用proxy或默认程序heap回退值证明正确。请移除正长度烘焙数组字段初始化并重新应用配置");
            RequireSerializedArray(target, "clips", target.clips);
            RequireSerializedArray(target, "rootPitch", target.rootPitch);
            RequireSerializedArray(target, "lowPitch", target.lowPitch);
            RequireSerializedArray(target, "highPitch", target.highPitch);
            RequireSerializedArray(target, "sourceRate", target.sourceRate);
            RequireSerializedArray(target, "sourceChannels", target.sourceChannels);
            RequireSerializedArray(target, "sourceFrameCount", target.sourceFrameCount);
            RequireSerializedArray(target, "sourcePeak", target.sourcePeak);
            RequireSerializedArray(target, "regionGain", target.regionGain);
            RequireSerializedArray(target, "tuningCents", target.tuningCents);
            RequireSerializedArray(target, "releaseDurationMs", target.releaseDurationMs);
        }

        private static void RequireSerializedArray<T>(UdonSharpBehaviour proxy, string field, T[] expected)
        {
            Need(expected != null, proxy.GetType().Name + "." + field + " 的烘焙数组为空引用");
            // Empty arrays can legitimately be omitted when their compiled default is also empty,
            // e.g. a song with no notes spanning a checkpoint has an empty CSR active-note list.
            if (expected.Length == 0) return;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            Need(backing != null, "缺少Udon backing: " + Hierarchy(proxy.transform));
            T[] serialized;
            Need(backing.publicVariables.TryGetVariableValue<T[]>(field, out serialized) && Same(expected, serialized),
                "Udon backing.publicVariables 中的非空数组未显式持久化或与proxy/source不一致: " +
                proxy.GetType().Name + "." + field + " (期望 " + expected.Length + " 项)。请重新烘焙；默认heap回退不算通过");
        }
        private static void CheckEvent(Component owner, string field, UnityEventBase events, UdonSharpBehaviour receiver, string name)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(receiver);
            var serialized = new SerializedObject(owner);
            SerializedProperty calls = serialized.FindProperty(field + ".m_PersistentCalls.m_Calls");
            for (int i = 0; calls != null && i < events.GetPersistentEventCount(); i++)
                if (events.GetPersistentTarget(i) == backing && events.GetPersistentMethodName(i) == "SendCustomEvent"
                    && calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == name) return;
            throw new InvalidOperationException("UI回调缺失/串线: " + Hierarchy(owner.transform) + " → " + name);
        }
        private static bool Same<T>(T[] a, T[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
            return true;
        }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Bound(GameObject root, Component value, string field)
        { Need(value != null, field + "缺失"); Need(value.transform == root.transform || value.transform.IsChildOf(root.transform), field + "引用其他播放器/场景对象"); }
        private static Transform FindPlayerRoot(MidiPlayerFacade facade)
        {
            for (Transform node = facade.transform; node != null; node = node.parent)
                if (node.Find("Runtime/Facade") == facade.transform) return node;
            throw new InvalidOperationException("播放器层级缺少Runtime/Facade，请重新配置");
        }
        private static string Hierarchy(Transform node)
        { string path = node.name; while (node.parent != null) { node = node.parent; path = node.name + "/" + path; } return path; }
    }
}
