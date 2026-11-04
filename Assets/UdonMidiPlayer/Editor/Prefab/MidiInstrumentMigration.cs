using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiInstrumentMigration
    {
        public const string MainScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        public const string BankPath = "Assets/UdonMidiPlayer/Editor/Generated/MarimbaBank.asset";
        private const string AudioFolder = "Assets/UdonMidiPlayer/Audio/Marimba";
        private const string ModelPath = "Assets/UdonMidiPlayer/Models/ConcertMarimba.fbx";

        [MenuItem("Tools/Music MIDI Player/Merge Main Scene Instruments")]
        public static void MergeMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MainScene)
            {
                if (scene.isDirty) throw new InvalidOperationException("Save the current scene before migration.");
                scene = EditorSceneManager.OpenScene(MainScene);
            }
            var players = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MidiPlayerFacade>(true)).ToArray();
            if (players.Length == 1 && players[0].instrumentRoots.Length == 3)
                throw new InvalidOperationException("The scene is already merged; edit its existing instrument banks and views.");
            if (players.Length != 2) throw new InvalidOperationException("Expected exactly two existing players.");
            MidiPlayerFacade grand = players.Single(p => p.transform.root.name == "UdonMidiPlayer");
            MidiPlayerFacade digital = players.Single(p => p != grand);
            if (grand.library.catalogHash != digital.library.catalogHash || grand.library.bank.bankHash != digital.library.bank.bankHash)
                throw new InvalidOperationException("The two libraries/banks differ; merge those configurations explicitly first.");
            MidiPlayerBuilder.EnsurePrograms();
            PianoBankAsset marimbaSource = ImportBank();
            GameObject mainRoot = grand.transform.root.gameObject;
            GameObject duplicateRoot = digital.transform.root.gameObject;
            Unpack(mainRoot); Unpack(duplicateRoot);
            Transform presentation = mainRoot.transform.Find("Presentation");
            Transform grandVariant = NewChild(presentation, "WhiteGrandPiano");
            Transform digitalVariant = NewChild(presentation, "DigitalPiano");
            Transform marimbaVariant = NewChild(presentation, "ConcertMarimba");
            Transform grandView = grand.keyboard.transform.parent;
            foreach (string name in new[] { "ViewRoot", "PianoModel", "Pedals" })
            {
                Transform item = presentation.Find(name);
                if (item != null) item.SetParent(grandVariant, false);
                item = duplicateRoot.transform.Find("Presentation/" + name);
                if (item != null) item.SetParent(digitalVariant, false);
            }
            // Keep the existing per-model geometry and animation calibration, share only logic.
            var digitalKeyboard = digital.keyboard;
            var digitalWaterfall = digital.waterfall;
            var digitalPedals = digital.pedals;
            digitalKeyboard.sequencer = grand.sequencer;
            digitalWaterfall.keyboard = digitalKeyboard;
            if (digitalWaterfall.effects != null)
            { digitalWaterfall.effects.waterfall = digitalWaterfall; digitalWaterfall.effects.keyboard = digitalKeyboard; }
            var marimbaView = UnityEngine.Object.Instantiate(grandView.gameObject, marimbaVariant).transform;
            marimbaView.name = "ViewRoot";
            var marimbaKeyboard = marimbaView.GetComponentInChildren<PianoKeyboardView>(true);
            var marimbaWaterfall = marimbaView.GetComponentInChildren<MidiWaterfallView>(true);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), marimbaVariant);
            model.name = "MarimbaModel";
            BindMarimba(model.transform, marimbaView, marimbaKeyboard, marimbaWaterfall, grand);
            var bankHost = NewChild(mainRoot.transform.Find("Data"), "MarimbaBank");
            var bank = bankHost.gameObject.AddUdonSharpComponent<PianoBankData>();
            MidiDataBaker.CopyBankToRuntime(marimbaSource, bank);
            grand.instrumentRoots = new[] { digitalVariant.gameObject, grandVariant.gameObject, marimbaVariant.gameObject };
            grand.instrumentKeyboards = new[] { digitalKeyboard, grand.keyboard, marimbaKeyboard };
            grand.instrumentPedals = new[] { digitalPedals, grand.pedals, null };
            grand.instrumentWaterfalls = new[] { digitalWaterfall, grand.waterfall, marimbaWaterfall };
            grand.instrumentBanks = new[] { grand.library.bank, grand.library.bank, bank };
            grand.selectedInstrument = 1;
            grand.network.instrumentIndex = 1;
            digitalVariant.gameObject.SetActive(false); grandVariant.gameObject.SetActive(true); marimbaVariant.gameObject.SetActive(false);
            // Do not keep an inactive second controller, library, listener or voice pool.
            Undo.DestroyObjectImmediate(duplicateRoot);
            // The selected catalog describes both banks and the stable three-choice ordering.
            grand.library.catalogHash = InstrumentCatalogHash(grand, grand.library.catalogHash);
            MidiPlayerUIBuilder.ConfigureInstruments(grand.playerUI);
            grand.playerUI.transform.localPosition = new Vector3(-2.30f, 0.95f, -0.73f);
            foreach (var behaviour in mainRoot.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All);
                EditorUtility.SetDirty(behaviour);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/instrument-merge-report.json", ValidateScene());
            Selection.activeGameObject = mainRoot;
        }

        public static PianoBankAsset ImportBank()
        {
            return SelectedToneBankImporter.ImportMarimba();
        }

        public static string InstrumentCatalogHash(MidiPlayerFacade player, string pianoCatalog)
        {
            if (player.instrumentRoots == null || player.instrumentRoots.Length == 0) return pianoCatalog;
            string text = "instruments-v1:digital,grand,marimba:" + pianoCatalog + ":" + player.instrumentBanks[2].bankHash;
            if (player.voicePool.strings != null)
            {
                var strings = player.voicePool.strings;
                text += ":strings-v2:solo-violin,viola,cello,contrabass,ensemble:sustain,spiccato,pizzicato:cc11";
                foreach (var bank in strings.banks) text += ":" + bank.bankHash;
                text += ":channels=" + string.Join(",", strings.channelSections) + ":tracks=" + string.Join(",", strings.trackSections);
            }
            if (player.voicePool.drums != null && player.voicePool.drums.bank != null)
                text += ":gm-drums-v1:" + player.voicePool.drums.bank.bankHash;
            if (player.instrumentBanks.Length > 8 && player.instrumentBanks[8] != null) text += ":music-box-v1:" + player.instrumentBanks[8].bankHash;
            if (player.instrumentRouter != null) text += ":" + MidiInstrumentRoutingBuilder.RulesVersion + ":" + player.instrumentRouter.rulesHash;
            return MidiContentHash.Bytes(System.Text.Encoding.UTF8.GetBytes(text));
        }

        [MenuItem("Tools/Music MIDI Player/Apply Instrument Tone Changes")]
        public static void ApplyToneChanges()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var facade = UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
            if (facade == null || facade.instrumentRoots.Length < 3) throw new InvalidOperationException("Open the merged main scene first.");
            ApplyToneChangesTo(facade);
            EditorSceneManager.MarkSceneDirty(facade.gameObject.scene);
            EditorSceneManager.SaveScene(facade.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        public static void ApplyToneChangesTo(MidiPlayerFacade facade)
        {
            var config = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid));
            MidiDataBaker.PrepareBank(config.bank);
            MidiDataBaker.CopyBankToRuntime(config.bank, facade.library.bank);
            var marimba = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(BankPath);
            MidiDataBaker.PrepareBank(marimba);
            ValidateMarimbaBudget(marimba);
            MidiDataBaker.CopyBankToRuntime(marimba, facade.instrumentBanks[2]);
            if (facade.instrumentBanks.Length > 8 && facade.instrumentBanks[8] != null)
            {
                var musicBox = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(MidiMusicBoxBuilder.BankPath);
                MidiDataBaker.PrepareBank(musicBox);
                MidiDataBaker.CopyBankToRuntime(musicBox, facade.instrumentBanks[8]);
                UdonSharpEditorUtility.CopyProxyToUdon(facade.instrumentBanks[8], ProxySerializationPolicy.All);
                EditorUtility.SetDirty(facade.instrumentBanks[8]);
            }
            facade.instrumentBanks[0] = facade.library.bank; facade.instrumentBanks[1] = facade.library.bank;
            if (facade.voicePool.strings != null)
                foreach (var bank in facade.voicePool.strings.banks)
                { AudioEnergyBaker.Bake(bank); bank.bankHash = MidiStringsBuilder.BankHash(bank); UdonSharpEditorUtility.CopyProxyToUdon(bank, ProxySerializationPolicy.All); EditorUtility.SetDirty(bank); }
            if (facade.voicePool.drums != null && facade.voicePool.drums.bank != null)
            {
                var drums = facade.voicePool.drums.bank;
                AudioEnergyBaker.Bake(drums); drums.bankHash = MidiPercussionBuilder.ComputeBankHash(drums);
                UdonSharpEditorUtility.CopyProxyToUdon(drums, ProxySerializationPolicy.All); EditorUtility.SetDirty(drums);
            }
            facade.voicePool.bank = facade.GetInstrumentBank(facade.selectedInstrument);
            facade.library.catalogHash = InstrumentCatalogHash(facade, MidiContentHash.Catalog(config.library, config.bank));
            facade.network.catalogHash = facade.library.catalogHash;
            foreach (var behaviour in new UdonSharpBehaviour[] { facade, facade.library, facade.library.bank, facade.instrumentBanks[2], facade.network, facade.voicePool })
            { UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All); EditorUtility.SetDirty(behaviour); }
        }

        public static void ValidateMarimbaBudget(PianoBankAsset bank)
        {
            MidiDataBaker.ValidateBank(bank);
            var piano = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(SalamanderBankImporter.BankPath);
            long bytes = bank.clips.Distinct().Sum(c => new FileInfo(AssetDatabase.GetAssetPath(c)).Length);
            long pianoBytes = piano.clips.Distinct().Sum(c => new FileInfo(AssetDatabase.GetAssetPath(c)).Length);
            double decoded = bank.clips.Distinct().Sum(c => (double)c.samples * c.channels * 4);
            double pianoDecoded = piano.clips.Distinct().Sum(c => (double)c.samples * c.channels * 4);
            if (bytes > pianoBytes || decoded > pianoDecoded)
                throw new InvalidOperationException("Marimba exceeds the selected piano audio budget.");
        }

        private static void BindMarimba(Transform model, Transform viewRoot, PianoKeyboardView keyboard, MidiWaterfallView waterfall, MidiPlayerFacade facade)
        {
            // FBX's handedness conversion reverses X/Z relative to the authoring preview.
            model.localRotation = Quaternion.Euler(0, 180, 0);
            // Blender export coordinates become Unity x/y/z. Bars have static height and a small tap response.
            viewRoot.localPosition = new Vector3(0, 0.963f, -0.346f);
            viewRoot.localRotation = Quaternion.identity; viewRoot.localScale = Vector3.one;
            keyboard.sequencer = facade.sequencer;
            keyboard.keyTransforms = new Transform[88]; keyboard.keyRenderers = new Renderer[88];
            keyboard.keyRestMaterials = new Material[88]; keyboard.keyPressOffsets = new Vector3[88];
            keyboard.keyPivotPositions = new Vector3[0]; keyboard.keyRotationAxes = new Vector3[0]; keyboard.keyPressAngles = new float[0];
            keyboard.keyIsBlack = new bool[88]; keyboard.pressDurationMs = 20; keyboard.releaseDurationMs = 70;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = MarimbaMaterial(renderer.name);
            foreach (Transform key in model.GetComponentsInChildren<Transform>(true))
            {
                if (!key.name.StartsWith("Key_", StringComparison.Ordinal)) continue;
                int pitch = int.Parse(key.name.Substring(4));
                int index = pitch - 21;
                Renderer renderer = key.GetComponentInChildren<Renderer>();
                keyboard.keyTransforms[index] = key; keyboard.keyRenderers[index] = renderer;
                keyboard.keyRestMaterials[index] = renderer.sharedMaterial;
                keyboard.keyPressOffsets[index] = key.parent.InverseTransformVector(Vector3.down * 0.0015f);
            }
            waterfall.keyboard = keyboard;
            waterfall.keyX = new float[88]; waterfall.keyWidth = new float[88]; waterfall.keyIsBlack = new bool[88];
            // Virtual keys follow the same chromatic arrangement beyond either physical end.
            float[] pitchClass = { 0, .5f, 1, 1.5f, 2, 3, 3.5f, 4, 4.5f, 5, 5.5f, 6 };
            for (int index = 0; index < 88; index++)
            {
                int pitch = index + 21, pc = pitch % 12;
                bool black = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                keyboard.keyIsBlack[index] = black; waterfall.keyIsBlack[index] = black;
                float x = (((pitch / 12) - 3) * 7 + pitchClass[pc]) * .06f - 1.05f;
                waterfall.keyX[index] = x; waterfall.keyWidth[index] = .050f;
                Renderer renderer = keyboard.keyRenderers[index];
                if (renderer != null)
                {
                    Bounds bounds = MidiPianoModelBinder.BoundsIn(renderer, waterfall.transform);
                    waterfall.keyX[index] = bounds.center.x; waterfall.keyWidth[index] = bounds.size.x;
                }
            }
            MidiWaterfallPresentationBuilder.BakeKeyHitPositions(waterfall);
            if (waterfall.effects != null) { waterfall.effects.waterfall = waterfall; waterfall.effects.keyboard = keyboard; }
            // The obsolete generated keyboard/backdrop stays hidden, as on the source piano.
            foreach (Transform item in keyboard.transform) item.gameObject.SetActive(false);
        }

        public static void RebindMarimbaPresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var f = UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
            Transform variant = f.instrumentRoots[2].transform;
            BindMarimba(variant.Find("MarimbaModel"), variant.Find("ViewRoot"), f.instrumentKeyboards[2], f.instrumentWaterfalls[2], f);
            foreach (var b in variant.GetComponentsInChildren<UdonSharpBehaviour>(true))
            { UdonSharpEditorUtility.CopyProxyToUdon(b, ProxySerializationPolicy.All); EditorUtility.SetDirty(b); }
            EditorSceneManager.MarkSceneDirty(f.gameObject.scene);
            EditorSceneManager.SaveScene(f.gameObject.scene);
        }

        private static Material MarimbaMaterial(string objectName)
        {
            string kind = "Wood";
            Color color = new Color(.25f, .085f, .033f);
            float metal = 0, gloss = .35f;
            if (objectName.Contains("Brass")) { kind = "Brass"; color = new Color(.54f,.35f,.11f); metal = .75f; gloss = .65f; }
            else if (objectName.Contains("Graphite")) { kind = "Frame"; color = new Color(.035f,.045f,.055f); metal = .6f; }
            else if (objectName.Contains("Rubber")) { kind = "Rubber"; color = new Color(.015f,.018f,.021f); gloss = .15f; }
            else if (objectName.Contains("Cord")) { kind = "Cord"; color = new Color(.5f,.35f,.13f); }
            else if (objectName.StartsWith("Key_"))
            {
                int pc = int.Parse(objectName.Substring(4)) % 12;
                if (pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10) { kind = "DarkWood"; color = new Color(.14f,.035f,.014f); }
            }
            string path = "Assets/UdonMidiPlayer/Materials/Marimba" + kind + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard")); mat.color = color;
                mat.SetFloat("_Metallic", metal); mat.SetFloat("_Glossiness", gloss);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        public static string ValidateScene()
        {
            var players = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MidiPlayerFacade>(true)).ToArray();
            if (players.Length != 1) throw new InvalidOperationException("Expected one facade.");
            var p = players[0];
            if (p.instrumentRoots.Length < 3 || p.instrumentBanks[0] != p.instrumentBanks[1]) throw new InvalidOperationException("Piano variants must share one piano bank.");
            for (int i = 0; i < 3; i++)
                if (!p.HasInstrument(i) || !p.instrumentBanks[i].IsValid() || p.instrumentKeyboards[i].sequencer != p.sequencer)
                    throw new InvalidOperationException("Invalid instrument binding: " + i);
            if (p.transform.root.GetComponentsInChildren<PianoVoicePool>(true).Length != 1 || p.voicePool.voiceSources.Length != PianoVoicePool.VoiceCapacity)
                throw new InvalidOperationException("Expected one shared 64-voice pool.");
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { scene = MainScene, playerCount = 1,
                variants = p.instrumentRoots.Select(r => r.name).ToArray(), selected = p.selectedInstrument,
                sharedPianoBank = true, voices = p.voicePool.voiceSources.Length,
                physicalKeys = p.instrumentKeyboards.Select(k => k.keyTransforms.Count(t => t != null)).ToArray(),
                banks = p.instrumentBanks.Select(b => b.bankHash).ToArray(), catalogHash = p.library.catalogHash,
                midiListeners = p.transform.root.GetComponentsInChildren<VRC.SDK3.Midi.VRCMidiListener>(true).Length
            }, Newtonsoft.Json.Formatting.Indented);
        }

        private static Transform NewChild(Transform parent, string name)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }
        private static void Unpack(GameObject root)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
    }
}
