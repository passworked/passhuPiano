using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Editor.Import;
using Newtonsoft.Json.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Main-scene-only incremental installation. Re-running preserves existing model/bank GUIDs.
    public static class MidiMusicBoxBuilder
    {
        public const string BankPath = "Assets/UdonMidiPlayer/Editor/Generated/MusicBoxBank.asset";
        const string Folder = "Assets/UdonMidiPlayer/Audio/MusicBox";
        const string MeshFolder = "Assets/UdonMidiPlayer/Meshes/MusicBox";
        const float TabletopScale = .14f;
        const float TableSurface = .82f;
        const float DrumRadius = .215f;

        [MenuItem("Tools/Music MIDI Player/Add or Update Music Box")]
        public static void Install()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MidiInstrumentMigration.MainScene)
                throw new InvalidOperationException("Open the main scene outside Play mode.");
            var f = UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
            if (f == null || f.instrumentRouter == null || f.instrumentRoots.Length < 8) throw new InvalidOperationException("Existing multi-instrument scene required.");
            Directory.CreateDirectory("RecoveryBackups/MusicBox-20261104");
            const string backup = "RecoveryBackups/MusicBox-20261104/Before-Install.unity";
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Scene backup failed.");
            var source = ImportBank();
            var host = f.transform.root.Find("Data/MusicBoxBank");
            if (host == null) host = Child(f.transform.root.Find("Data"), "MusicBoxBank");
            var bank = host.GetComponent<PianoBankData>() ?? host.gameObject.AddUdonSharpComponent<PianoBankData>();
            MidiDataBaker.CopyBankToRuntime(source, bank);
            var root = f.instrumentRoots.Length > 8 ? f.instrumentRoots[8] : null;
            PianoKeyboardView keys; MidiWaterfallView fall;
            if (root == null)
            {
                root = Child(f.instrumentRoots[2].transform.parent, "MusicBox").gameObject;
                root.SetActive(false);
                var view = UnityEngine.Object.Instantiate(f.instrumentKeyboards[2].transform.parent.gameObject, root.transform).transform;
                view.name = "ViewRoot"; view.localPosition = new Vector3(0, 1.075f, .08f);
                view.localRotation = Quaternion.identity; view.localScale = Vector3.one;
                keys = view.GetComponentInChildren<PianoKeyboardView>(true);
                fall = view.GetComponentInChildren<MidiWaterfallView>(true);
                // Generated piano-key placeholders are inactive legacy geometry, not comb tines.
                foreach (Transform t in keys.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(t.gameObject);
                TrimWaterfall(fall);
                BuildModel(root.transform, keys, fall);
                f.instrumentRoots = Extend(f.instrumentRoots, root);
                f.instrumentKeyboards = Extend(f.instrumentKeyboards, keys);
                f.instrumentWaterfalls = Extend(f.instrumentWaterfalls, fall);
                f.instrumentBanks = Extend(f.instrumentBanks, bank);
                f.instrumentPedals = Extend(f.instrumentPedals, (PianoPedalView)null);
                f.instrumentStrings = Extend(f.instrumentStrings, (StringsView)null);
                var router = f.instrumentRouter;
                router.viewRestPositions = Extend(router.viewRestPositions, root.transform.localPosition);
                router.stageOffsets = ExtendTo(router.stageOffsets, 8);
                router.stageOffsets[7] = new Vector3(3.7f, 0, 0);
                f.voicePool.ensemble.soundOrigins = ExtendTo(f.voicePool.ensemble.soundOrigins, 8);
                f.voicePool.ensemble.soundOrigins[7] = keys.rotatingMechanism;
            }
            else
            {
                keys = f.instrumentKeyboards[8]; fall = f.instrumentWaterfalls[8];
            }
            f.instrumentBanks[8] = bank;
            ConfigureTabletop(root.transform, fall);
            ConfigurePerformance(f);
            ConfigurePlacement(f);
            keys.sequencer = f.sequencer; keys.instrumentRouter = f.instrumentRouter; keys.instrumentTarget = 7;
            fall.instrumentRouter = f.instrumentRouter; fall.instrumentTarget = 7; fall.keyboard = keys;
            MidiInstrumentRoutingBuilder.Bake(f.instrumentRouter); f.instrumentRouter.ResolveSelection();
            MidiPlayerUIBuilder.ConfigureInstruments(f.playerUI);
            MidiHardwareMappingUIBuilder.Configure(f.playerUI);
            MidiPlayerLocalizationBuilder.Configure(f.playerUI);
            RegisterCredit(f);
            var config = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(AssetDatabase.GUIDToAssetPath(f.library.configurationAssetGuid));
            f.library.catalogHash = MidiInstrumentMigration.InstrumentCatalogHash(f, MidiContentHash.Catalog(config.library, config.bank));
            f.network.catalogHash = f.library.catalogHash;
            f.playerUI.ApplyLanguage();
            foreach (var b in f.transform.root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            { UdonSharpEditorUtility.CopyProxyToUdon(b, ProxySerializationPolicy.All); EditorUtility.SetDirty(b); }
            var report = Measure(f);
            File.WriteAllText("Logs/MusicBox-Budget.json", report.ToString());
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("Music box installed in main scene. " + MidiInstrumentRoutingBuilder.ValidateRules(f.instrumentRouter) + " GM/selection checks. No distribution prefab updated.");
        }

        static T[] Extend<T>(T[] old, T item) { var a = ExtendTo(old, 9); a[8] = item; return a; }
        static T[] ExtendTo<T>(T[] old, int count) { var a = new T[count]; Array.Copy(old, a, Math.Min(old.Length, count)); return a; }
        static Transform Child(Transform parent, string name)
        { var g = new GameObject(name); g.transform.SetParent(parent, false); return g.transform; }
        static Transform FindOrChild(Transform parent, string name) => parent.Find(name) ?? Child(parent, name);

        [MenuItem("Tools/Music MIDI Player/Apply Tabletop Music Box")]
        public static void ApplyTabletop()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MidiInstrumentMigration.MainScene)
                throw new InvalidOperationException("Open the main scene outside Play mode.");
            var f = UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
            if (f == null || f.instrumentRoots.Length <= 8 || f.instrumentRoots[8] == null)
                throw new InvalidOperationException("Install the music box first.");
            const string backup = "RecoveryBackups/MusicBox-20261104/Before-DrumFit.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Scene backup failed.");
            var fall = f.instrumentWaterfalls[8];
            ConfigureTabletop(f.instrumentRoots[8].transform, fall);
            ConfigurePerformance(f);
            ConfigurePlacement(f);
            foreach (var b in new UdonSharpBehaviour[] { f, fall, fall.effects, fall.keyboard, f.performanceInput,
                f.instrumentRoots[8].GetComponentInChildren<MidiPerformanceSeat>(true) })
                if (b != null) { UdonSharpEditorUtility.CopyProxyToUdon(b, ProxySerializationPolicy.RootOnly); EditorUtility.SetDirty(b); }
            File.WriteAllText("Logs/MusicBox-Budget.json", Measure(f).ToString());
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save main scene.");
            AssetDatabase.SaveAssets();
        }

        public static void ConfigurePlacement(MidiPlayerFacade f)
        {
            var root = f.instrumentRoots[8].transform;
            f.musicBoxTable = root.Find("SideTable").gameObject;
            f.musicBoxSeat = root.Find("PerformanceSeat").gameObject;
        }

        static void RegisterCredit(MidiPlayerFacade f)
        {
            var board=f.playerUI.creditsPanel;
            if(board==null||board.detailKeys.Contains("credits.music_box_detail"))return;
            board.categories=board.categories.Concat(new[]{1}).ToArray();
            board.names=board.names.Concat(new[]{"MusicalBoxSingleNotes — Music box"}).ToArray();
            board.authors=board.authors.Concat(new[]{"rbnuser"}).ToArray();
            board.licenses=board.licenses.Concat(new[]{"CC0 1.0"}).ToArray();
            board.detailKeys=board.detailKeys.Concat(new[]{"credits.music_box_detail"}).ToArray();
            board.sources=board.sources.Concat(new[]{"https://freesound.org/people/rbnuser/packs/19724/"}).ToArray();
            board.licenseUrls=board.licenseUrls.Concat(new[]{"https://creativecommons.org/publicdomain/zero/1.0/"}).ToArray();
            board.Refresh();EditorUtility.SetDirty(board);
        }

        static void ConfigurePerformance(MidiPlayerFacade f)
        {
            var input = f.performanceInput;
            if (input == null) return;
            input.instrumentFrames = ExtendTo(input.instrumentFrames, 9);
            input.instrumentMinimum = ExtendTo(input.instrumentMinimum, 9); input.instrumentMaximum = ExtendTo(input.instrumentMaximum, 9);
            input.keyMinimum = ExtendTo(input.keyMinimum, 9 * 88); input.keyMaximum = ExtendTo(input.keyMaximum, 9 * 88);
            var root = f.instrumentRoots[8].transform; input.instrumentFrames[8] = root;
            Bounds all = new Bounds();
            for (int i = 0; i < 88; i++)
            {
                var bounds = MidiPianoModelBinder.BoundsIn(f.instrumentKeyboards[8].keyRenderers[i], root);
                input.keyMinimum[8 * 88 + i] = bounds.min; input.keyMaximum[8 * 88 + i] = bounds.max;
                if (i == 0) all = bounds; else all.Encapsulate(bounds);
            }
            input.instrumentMinimum[8] = all.min; input.instrumentMaximum[8] = all.max;
            var chair = root.Find("PerformanceSeat");
            if (chair == null)
            {
                var original = f.instrumentRoots[2].transform.Find("PerformanceSeat");
                if (original == null) return;
                chair = UnityEngine.Object.Instantiate(original.gameObject, root).transform; chair.name = "PerformanceSeat";
                foreach (var mesh in chair.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.transform != chair) UnityEngine.Object.DestroyImmediate(mesh.gameObject);
            }
            chair.localPosition = new Vector3(0,0,-.65f);
            var seat = chair.GetComponent<MidiPerformanceSeat>(); seat.input = input; seat.instrumentIndex = 8;
            seat.station.stationEnterPlayerLocation.localPosition = new Vector3(0,.48f,0);
            var hit = chair.GetComponent<BoxCollider>(); hit.center = new Vector3(0,.445f,0); hit.size = new Vector3(.34f,.13f,.34f);
            var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/UdonMidiPlayer/Materials/MusicBoxWalnut.mat");
            Part(chair,"RoundSeat",PrimitiveType.Cylinder,new Vector3(0,.455f,0),new Vector3(.32f,.025f,.32f),wood);
            Part(chair,"Stem",PrimitiveType.Cylinder,new Vector3(0,.23f,0),new Vector3(.075f,.205f,.075f),wood);
            Part(chair,"Foot",PrimitiveType.Cylinder,new Vector3(0,.023f,0),new Vector3(.30f,.023f,.30f),wood);
            Merge(chair, "MusicBoxSeat");
        }

        public static PianoBankAsset ImportBank()
        {
            var rows = (JArray)JObject.Parse(File.ReadAllText(Folder + "/BANK-MAPPING.json"))["rows"];
            var paths = rows.Select(r => (string)r["file"]).ToArray();
            foreach (var r in rows)
            {
                string p = (string)r["file"];
                if (MidiContentHash.Bytes(File.ReadAllBytes(p)) != (string)r["sha256"]) throw new IOException("Music box checksum mismatch: " + p);
                var importer = (AudioImporter)AssetImporter.GetAtPath(p);
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate; settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false;
                foreach (string platform in new[] { "Standalone", "Android" })
                    if (importer.ContainsSampleSettingsOverride(platform)) importer.ClearSampleSettingOverride(platform);
                importer.SaveAndReimport();
            }
            var bank = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(BankPath);
            if (bank == null) { bank = ScriptableObject.CreateInstance<PianoBankAsset>(); AssetDatabase.CreateAsset(bank, BankPath); }
            bank.velocityLayerCount = 1; bank.clips = paths.Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).ToArray();
            bank.rootPitch = rows.Select(r => (int)r["pitch"]).ToArray();
            bank.tuningCents = new float[paths.Length]; bank.regionGain = Enumerable.Repeat(.7f, paths.Length).ToArray();
            bank.releaseDurationMs = Enumerable.Repeat(1800, paths.Length).ToArray();
            bank.lowPitch = new int[0]; bank.highPitch = new int[0];
            bank.sourceAttribution = File.ReadAllText(Folder + "/ATTRIBUTION.txt"); bank.redistributionLicense = File.ReadAllText(Folder + "/LICENSE.txt");
            MidiDataBaker.PrepareBank(bank); return bank;
        }

        static void TrimWaterfall(MidiWaterfallView fall)
        {
            // The new instrument's complete presentation stays below 128 Renderers.
            for (int i = 28; i < fall.barTransforms.Length; i++) if (fall.barTransforms[i] != null) UnityEngine.Object.DestroyImmediate(fall.barTransforms[i].gameObject);
            fall.barTransforms = fall.barTransforms.Take(28).ToArray(); fall.barRenderers = fall.barRenderers.Take(28).ToArray();
            var fx = fall.effects;
            foreach (var p in fx.hitParticles.Skip(2).Concat(fx.symbolParticles.Skip(2)).Concat(fx.ringParticles))
                if (p != null) UnityEngine.Object.DestroyImmediate(p.gameObject);
            foreach (var t in fx.glowTransforms) if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
            foreach (var l in fx.hitLights) if (l != null) UnityEngine.Object.DestroyImmediate(l.gameObject);
            fx.hitParticles = fx.hitParticles.Take(2).ToArray(); fx.symbolParticles = fx.symbolParticles.Take(2).ToArray();
            fx.ringParticles = new ParticleSystem[0]; fx.glowTransforms = new Transform[0]; fx.glowRenderers = new Renderer[0]; fx.hitLights = new Light[0];
            fx.totalParticleBudget = 128; fx.totalSymbolBudget = 32; fx.maxParticlesPerFrame = 16; fx.maxHitsPerFrame = 8; fx.maxActiveLights = 0;
        }

        static Material Material(string name, Color color, float metallic, float smoothness)
        {
            string path = "Assets/UdonMidiPlayer/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = color; m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", smoothness); m.enableInstancing = true;
            EditorUtility.SetDirty(m); return m;
        }
        static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = position; g.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>()); g.GetComponent<Renderer>().sharedMaterial = material; return g;
        }
        static void Merge(Transform host, string name)
        {
            var filters = host.GetComponentsInChildren<MeshFilter>(true).Where(f => f.transform != host).ToArray();
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(filters.Select(f => new CombineInstance { mesh = f.sharedMesh, transform = host.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray());
            var mat = filters[0].GetComponent<Renderer>().sharedMaterial;
            foreach (var f in filters) UnityEngine.Object.DestroyImmediate(f.gameObject);
            SetMesh(host, mesh, mat);
        }

        static MeshRenderer SetMesh(Transform host, Mesh mesh, Material mat)
        {
            string path = MeshFolder + "/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else
            {
                // Update native mesh buffers as well as the asset, retaining its GUID.
                existing.Clear(); existing.indexFormat = mesh.indexFormat;
                existing.vertices = mesh.vertices; existing.normals = mesh.normals;
                existing.tangents = mesh.tangents; existing.uv = mesh.uv;
                existing.triangles = mesh.triangles; existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(mesh); mesh = existing;
            }
            var filter = host.GetComponent<MeshFilter>();
            if (filter == null) filter = host.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = host.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            return renderer;
        }

        static void BuildModel(Transform root, PianoKeyboardView keys, MidiWaterfallView fall)
        {
            MidiImportService.EnsureFolder(MeshFolder);
            var brass = Material("MusicBoxBrass", new Color(.68f,.43f,.13f), .83f, .63f);
            var steel = Material("MusicBoxSteel", new Color(.56f,.64f,.68f), .88f, .72f);
            var model = Child(root, "MusicBoxModel");
            var drum=Child(model,"RotatingPinBelt"); drum.localPosition=new Vector3(0,1.23f,.25f);
            keys.rotatingMechanism=drum;keys.mechanismDegreesPerSecond=24;
            keys.whiteMaterial=keys.blackMaterial=steel;
            // Reuse the current player's pressed-material shader and deterministic palette.
            fall.viewHeight=1.85f;fall.barDepth=.008f;fall.windowMs=3000;
            foreach(var p in fall.effects.hitParticles.Concat(fall.effects.symbolParticles))
            { var shape=p.shape;shape.scale=new Vector3(1.58f,.01f,.01f); }
        }

        static void ConfigureTabletop(Transform root, MidiWaterfallView fall)
        {
            var model = root.Find("MusicBoxModel");
            var wood = Material("MusicBoxWalnut", new Color(.19f,.065f,.028f), .05f, .38f);
            var brass = Material("MusicBoxBrass", new Color(.68f,.43f,.13f), .83f, .63f);
            var w = FindOrChild(model, "WalnutCase");
            var b = FindOrChild(model, "BrassFrame");
            // The original shaft keeps its transform. Rebuild the case as a
            // self-contained wooden box with a separate transparent dust cover.
            Part(w,"Base",PrimitiveType.Cube,new Vector3(0,.88f,0),new Vector3(3.54f,.20f,1.55f),wood);
            foreach (float x in new[]{-1.72f,1.72f})
            {
                Part(w,"Side",PrimitiveType.Cube,new Vector3(x,1.006f,0),new Vector3(.13f,.25f,1.55f),wood);
                Part(b,"Rim",PrimitiveType.Cube,new Vector3(x,1.14f,0),new Vector3(.145f,.025f,1.56f),brass);
                Part(b,"Bearing",PrimitiveType.Cylinder,new Vector3(x,1.23f,.25f),new Vector3(.14f,.04f,.14f),brass).transform.localRotation=Quaternion.Euler(0,0,90);
                foreach (float z in new[]{-.59f,.59f})
                    Part(b,"BoxFoot",PrimitiveType.Cylinder,new Vector3(x*.85f,.74f,z),new Vector3(.17f,.04f,.17f),brass);
            }
            foreach(float z in new[]{-.73f,.73f})
            {
                Part(w,"Rail",PrimitiveType.Cube,new Vector3(0,.99f,z),new Vector3(3.4f,.25f,.11f),wood);
                Part(b,"Inlay",PrimitiveType.Cube,new Vector3(0,1.12f,z),new Vector3(3.38f,.015f,.115f),brass);
            }
            Part(b,"Clasp",PrimitiveType.Cube,new Vector3(0,1.064f,-.793f),new Vector3(.28f,.18f,.035f),brass);
            BuildSmoothDrum(fall.keyboard.rotatingMechanism, brass, fall.keyboard.whiteMaterial);
            BuildComb(model, b, fall.keyboard, brass, fall.keyboard.whiteMaterial);
            Merge(w,"WalnutCase"); Merge(b,"BrassFrame");
            BuildCover(model);
            model.localScale = Vector3.one * TabletopScale;
            model.localPosition = Vector3.up * (TableSurface - .70f * TabletopScale);

            // A small side table is separate furniture, not part of the instrument case.
            var table = FindOrChild(root,"SideTable");
            Part(table,"Top",PrimitiveType.Cylinder,new Vector3(0,TableSurface-.025f,0),new Vector3(.77f,.025f,.77f),wood);
            Part(table,"Column",PrimitiveType.Cylinder,new Vector3(0,.402f,0),new Vector3(.12f,.37f,.12f),wood);
            Part(table,"Base",PrimitiveType.Cylinder,new Vector3(0,.025f,0),new Vector3(.55f,.025f,.55f),wood);
            Merge(table,"MusicBoxSideTable");
            var top = table.GetComponent<BoxCollider>();
            if (top == null) top = table.gameObject.AddComponent<BoxCollider>();
            top.center = new Vector3(0,TableSurface-.025f,0); top.size = new Vector3(.68f,.05f,.68f);
            top.enabled = false;

            var view = root.Find("ViewRoot");
            var backdrop = view.Find("WaterfallBackdrop");
            if (backdrop != null && !backdrop.gameObject.activeSelf)
            {
                var renderer = backdrop.GetComponent<MeshRenderer>();
                var filter = backdrop.GetComponent<MeshFilter>();
                if (renderer != null) UnityEngine.Object.DestroyImmediate(renderer);
                if (filter != null) UnityEngine.Object.DestroyImmediate(filter);
            }
            view.localScale = Vector3.one * TabletopScale;
            view.localPosition = model.localPosition + new Vector3(0,1.23f,.25f)*TabletopScale;
            ConfigureDrumNotes(fall);
            ConfigureContactEffects(fall.effects);
        }

        public static void ConfigureContactEffects(MidiWaterfallEffects effects)
        {
            effects.particleLift = .002f;
            effects.particleDepthOffset = -.005f;
            foreach(var p in effects.hitParticles.Concat(effects.symbolParticles))
            {
                bool symbol = effects.symbolParticles.Contains(p);
                var main=p.main; main.scalingMode=ParticleSystemScalingMode.Hierarchy;
                main.startSize = symbol ? new ParticleSystem.MinMaxCurve(.105f,.155f) : new ParticleSystem.MinMaxCurve(.026f,.052f);
                main.startLifetime = symbol ? new ParticleSystem.MinMaxCurve(.65f,1.05f) : new ParticleSystem.MinMaxCurve(.28f,.58f);
                // Send the burst forward as it rises, clear of the cylinder silhouette.
                main.startSpeed = symbol ? new ParticleSystem.MinMaxCurve(.32f,.46f) : new ParticleSystem.MinMaxCurve(.48f,.68f);
                p.transform.localRotation = Quaternion.Euler(-135,0,0);
                var shape=p.shape; shape.shapeType=ParticleSystemShapeType.Cone;
                shape.angle=symbol ? 8f : 21f; shape.scale=Vector3.one*.52f;
                var velocity = p.velocityOverLifetime; velocity.zMultiplier = symbol ? .65f : .70f;
                EditorUtility.SetDirty(p); EditorUtility.SetDirty(p.transform);
            }
        }

        static void BuildSmoothDrum(Transform drum, Material brass, Material steel)
        {
            var db = FindOrChild(drum,"BrassCylinder");
            var axle = drum.Find("SteelAxle");
            if (axle == null) axle = FindOrChild(drum,"SteelPins");
            axle.name = "SteelAxle";
            var shell = DrumShell();
            var body = Part(db,"Drum",PrimitiveType.Cylinder,Vector3.zero,new Vector3(2*DrumRadius,1.622f,2*DrumRadius),brass);
            body.GetComponent<MeshFilter>().sharedMesh = shell;
            body.transform.localRotation=Quaternion.Euler(0,0,90);
            foreach (float x in new[]{-1.635f,1.635f})
                Part(db,"EndRing",PrimitiveType.Cylinder,new Vector3(x,0,0),new Vector3(.458f,.016f,.458f),brass).transform.localRotation=Quaternion.Euler(0,0,90);
            Part(axle,"Axle",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.055f,1.82f,.055f),steel).transform.localRotation=Quaternion.Euler(0,0,90);
            Part(db,"CrankArm",PrimitiveType.Cube,new Vector3(1.82f,0,.12f),new Vector3(.035f,.035f,.25f),brass);
            Part(db,"CrankHandle",PrimitiveType.Cylinder,new Vector3(1.91f,0,.24f),new Vector3(.055f,.09f,.055f),brass).transform.localRotation=Quaternion.Euler(0,0,90);
            Merge(db,"BrassCylinder"); Merge(axle,"SteelPins"); // Retain the existing mesh asset GUID.
            UnityEngine.Object.DestroyImmediate(shell);
        }

        static Mesh DrumShell()
        {
            // The end rings close the shell. A 64-sided section keeps the rolling arc
            // within 0.04 mm of the visible metal surface at the tabletop scale.
            const int sides = 64;
            var vertices = new Vector3[sides*2]; var normals = new Vector3[vertices.Length];
            var triangles = new int[sides*6];
            for (int i=0;i<sides;i++)
            {
                float a = i*2*Mathf.PI/sides;
                var n = new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                int lo=i*2,hi=lo+1,next=((i+1)%sides)*2,t=i*6;
                vertices[lo]=n*.5f-Vector3.up; vertices[hi]=n*.5f+Vector3.up;
                normals[lo]=normals[hi]=n;
                triangles[t]=lo; triangles[t+1]=hi; triangles[t+2]=next+1;
                triangles[t+3]=lo; triangles[t+4]=next+1; triangles[t+5]=next;
            }
            var mesh = new Mesh { name = "DrumShellSource" };
            mesh.vertices=vertices; mesh.normals=normals; mesh.triangles=triangles; mesh.RecalculateBounds();
            return mesh;
        }

        static float DrumPitchX(int pitch) => Mathf.Lerp(-1.55f,1.55f,pitch/127f);

        static void BuildComb(Transform model, Transform brassHost, PianoKeyboardView keys, Material brass, Material steel)
        {
            var screws = FindOrChild(model,"SteelMechanism");
            var legacyMarker = model.Find("CombSupport");
            if (legacyMarker != null) Undo.DestroyObjectImmediate(legacyMarker.gameObject);
            keys.pluckedTines = true;
            keys.keyTransforms = new Transform[88]; keys.keyRenderers = new Renderer[88];
            keys.keyRestMaterials = new Material[88]; keys.keyPressOffsets = new Vector3[88];
            keys.keyPivotPositions = new Vector3[88]; keys.keyRotationAxes = new Vector3[88]; keys.keyPressAngles = new float[88];
            keys.keyIsBlack = new bool[88];
            // Move the tine tips onto the same cylinder used by the note's inner face.
            var tip = keys.rotatingMechanism.localPosition + new Vector3(0,-.155f,-.17f).normalized*DrumRadius;
            float rootZ = tip.z-.64f;
            Part(brassHost,"CombBridge",PrimitiveType.Cube,new Vector3((DrumPitchX(21)+DrumPitchX(108))*.5f,tip.y-.010f,rootZ),new Vector3(2.20f,.055f,.10f),brass);
            for(int i=0;i<88;i++)
            {
                int pitch = i+21;
                float x = DrumPitchX(pitch), length = Mathf.Lerp(.64f,.25f,i/87f);
                var tine = model.Find("Tine_"+pitch);
                var pos = new Vector3(x,tip.y,tip.z-length*.5f);
                var scale = new Vector3(.019f,.009f,length);
                if (tine == null) tine = Part(model,"Tine_"+pitch,PrimitiveType.Cube,pos,scale,steel).transform;
                tine.localPosition = pos; tine.localScale = scale;
                keys.keyTransforms[i] = tine; keys.keyRenderers[i] = tine.GetComponent<Renderer>(); keys.keyRestMaterials[i] = steel;
                keys.keyPivotPositions[i] = new Vector3(x,tip.y,tip.z-length); keys.keyRotationAxes[i] = Vector3.right; keys.keyPressAngles[i] = 3.2f;
                float depth = .64f-length;
                if(depth>.001f) Part(brassHost,"CombSupport",PrimitiveType.Cube,new Vector3(x,tip.y-.010f,rootZ+depth*.5f),new Vector3(.021f,.020f,depth+.012f),brass);
                Part(screws,"Screw",PrimitiveType.Cylinder,new Vector3(x,tip.y+.028f,rootZ),new Vector3(.016f,.006f,.016f),steel);
            }
            Merge(screws,"SteelMechanism");
        }

        static void ConfigureDrumNotes(MidiWaterfallView fall)
        {
            var keys = fall.keyboard;
            // Calibrate from the actual tine tip, in the coordinate frame centered on the shaft.
            var contact = fall.transform.InverseTransformPoint(keys.keyTransforms[0].TransformPoint(new Vector3(0,0,.5f)));
            fall.rollOverDrum = true;
            fall.drumNoteRadius = new Vector2(contact.y,contact.z).magnitude;
            fall.drumContactAngle = Mathf.Atan2(contact.z,contact.y);
            fall.drumArcSweep = 1.02f - fall.drumContactAngle;
            fall.barDepth = .010f; // 1.4 mm outward thickness at the tabletop scale.
            keys.mechanismDegreesPerSecond = -fall.drumArcSweep * Mathf.Rad2Deg / (Mathf.Max(1, fall.windowMs) * .001f);
            fall.keyX = new float[88]; fall.keyWidth = new float[88]; fall.keyIsBlack = new bool[88];
            fall.keyHitPositions = new Vector3[88]; fall.keyRootHitPositions = new Vector3[88];
            fall.landAtKeyRoot = false;
            for (int i=0;i<88;i++)
            {
                float x = DrumPitchX(i+21);
                fall.keyX[i] = x; fall.keyWidth[i] = .023f;
                fall.keyHitPositions[i] = fall.keyRootHitPositions[i] =
                    fall.transform.InverseTransformPoint(keys.keyTransforms[i].TransformPoint(new Vector3(0,0,.5f)));
            }
            var crystal = AssetDatabase.LoadAssetAtPath<Mesh>(MidiWaterfallPresentationBuilder.NoteMeshPath);
            const string path = MeshFolder + "/DrumNoteCrystal.asset";
            var arcMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (arcMesh == null)
            {
                arcMesh = UnityEngine.Object.Instantiate(crystal); arcMesh.name = "DrumNoteCrystal";
                AssetDatabase.CreateAsset(arcMesh,path);
            }
            // Include the lower front contact point as well as the upper arc.
            arcMesh.bounds = new Bounds(Vector3.zero, new Vector3(1.35f,2.2f,2.2f));
            EditorUtility.SetDirty(arcMesh);
            foreach (var bar in fall.barTransforms) bar.GetComponent<MeshFilter>().sharedMesh = arcMesh;
        }

        static void BuildCover(Transform model)
        {
            var cover = FindOrChild(model,"ClearDustCover");
            const string materialPath = "Assets/UdonMidiPlayer/Materials/MusicBoxClearCover.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("MusicMidiPlayer/MusicBoxClearCover");
                if (shader == null) throw new InvalidOperationException("Music box cover shader must be imported first.");
                material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath);
            }
            var vertices = new List<Vector3>(); var indices = new List<int>();
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
            {
                int i = vertices.Count;
                if (Vector3.Dot(Vector3.Cross(b-a,c-a),outward) < 0) { var t=b;b=d;d=t; }
                vertices.AddRange(new[]{a,b,c,d}); indices.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            }
            const float x=1.77f,z=.75f,bottom=1.13f,shoulder=1.58f,top=1.65f,bevel=.07f;
            Vector3[] Ring(float xx,float zz,float yy) => new[] {new Vector3(-xx,yy,-zz),new Vector3(xx,yy,-zz),new Vector3(xx,yy,zz),new Vector3(-xx,yy,zz)};
            var low=Ring(x,z,bottom);var mid=Ring(x,z,shoulder);var high=Ring(x-bevel,z-bevel,top);
            var normals=new[]{Vector3.back,Vector3.right,Vector3.forward,Vector3.left};
            for(int i=0;i<4;i++)
            {
                int j=(i+1)%4;
                if (i != 1) Quad(low[i],low[j],mid[j],mid[i],normals[i]);
                Quad(mid[i],mid[j],high[j],high[i],normals[i]+Vector3.up);
            }
            void RightPanel(float za,float zb,float ya,float yb) => Quad(new Vector3(x,ya,za),new Vector3(x,ya,zb),new Vector3(x,yb,zb),new Vector3(x,yb,za),Vector3.right);
            // The right-side shaft and crank stay outside the cover with a real notch.
            RightPanel(-z,.16f,bottom,shoulder); RightPanel(.34f,z,bottom,shoulder); RightPanel(.16f,.34f,1.31f,shoulder);
            Quad(high[0],high[1],high[2],high[3],Vector3.up);
            var mesh=new Mesh { name="MusicBoxClearCover" }; mesh.SetVertices(vertices); mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var renderer=SetMesh(cover,mesh,material);
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }

        public static JObject Measure(MidiPlayerFacade f)
        {
            var root=f.instrumentRoots[8];
            var meshes=root.GetComponentsInChildren<MeshFilter>(true);
            long triangles=meshes.Sum(m=>(long)m.sharedMesh.triangles.Length/3);
            var renderers=root.GetComponentsInChildren<Renderer>(true);
            var materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
            long particleTriangles=0;
            foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer=ps.GetComponent<ParticleSystemRenderer>();int perParticle=2;
                if(renderer!=null&&renderer.renderMode==ParticleSystemRenderMode.Mesh)
                {var particleMeshes=new Mesh[renderer.meshCount];renderer.GetMeshes(particleMeshes);perParticle=particleMeshes.Where(m=>m!=null).Select(m=>m.triangles.Length/3).DefaultIfEmpty(0).Max();}
                particleTriangles+=(long)ps.main.maxParticles*perParticle;
            }
            var textures=materials.SelectMany(m=>m.GetTexturePropertyNames().Select(p=>m.GetTexture(p)))
                .Concat(root.GetComponentsInChildren<ReflectionProbe>(true).Select(p=>p.customBakedTexture)).Where(t=>t!=null).Distinct().ToArray();
            long textureBytes=0;
            foreach(var t in textures)
            {
                if(t.width>2048||t.height>2048)throw new InvalidOperationException("Music box texture exceeds 2048: "+t.name);
                int width=t.width,height=t.height;long mipBytes=0;
                while(true){mipBytes+=(long)width*height*4;if(width==1&&height==1)break;width=Mathf.Max(1,width/2);height=Mathf.Max(1,height/2);}
                textureBytes+=mipBytes*(t is Cubemap?6:1);
            }
            var bank=f.instrumentBanks[8];
            long sourceBytes=bank.clips.Distinct().Sum(c=>new FileInfo(AssetDatabase.GetAssetPath(c)).Length);
            long decodedBytes=bank.clips.Distinct().Sum(c=>(long)c.samples*c.channels*4);
            if(triangles+particleTriangles>50000||renderers.Length>128||materials.Length>8||textureBytes>67108864||sourceBytes>33554432||decodedBytes>671088640)
                throw new InvalidOperationException("Music box exceeds instrument budget.");
            return JObject.FromObject(new {date="2026-11-04",scene=MidiInstrumentMigration.MainScene,target=7,view=8,
                triangles,particleTriangles,worstVisibleTriangles=triangles+particleTriangles,renderers=renderers.Length,staticMaterials=materials.Length,declaredRuntimeMaterialMaximum=8,
                textureWidthMax=textures.Select(t=>t.width).DefaultIfEmpty(0).Max(),textureHeightMax=textures.Select(t=>t.height).DefaultIfEmpty(0).Max(),textureRgba32FullMipBytes=textureBytes,
                textures=textures.Select(t=>new {path=AssetDatabase.GetAssetPath(t),t.width,t.height,type=t.GetType().Name}).ToArray(),
                sourceBytes,decodedFloat32Bytes=decodedBytes,physicalTines=88,physicalRange="21-108",audioRange="0-127",
                physicalSourcesPerNote=1,sharedSources=f.voicePool.voiceSources.Length,waterfallSlots=f.instrumentWaterfalls[8].barTransforms.Length,
                pcmNotMeasuredClientMemory=true,PCVRChat="not tested",mobile="not tested",hardwareMidi="not tested"});
        }
    }
}
