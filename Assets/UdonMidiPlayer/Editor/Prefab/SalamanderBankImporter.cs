using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>
    /// Imports the selected, already-encoded single v9 layer without changing its source bytes.
    /// Unity is explicitly configured for PCM, not another lossy Vorbis encoding pass.
    /// The imported/build representation and decoded memory are NOT the 9.72 MiB source payload.
    /// </summary>
    public static class SalamanderBankImporter
    {
        public const string DestinationDirectory = "Assets/UdonMidiPlayer/Audio/SalamanderV9";
        public const string BankPath = "Assets/UdonMidiPlayer/Editor/Generated/SalamanderV9Bank.asset";
        private const string OfficialSource = "https://freepats.zenvoid.org/Piano/acoustic-grand-piano.html";
        private const string LicenseUrl = "https://creativecommons.org/licenses/by/3.0/";
        private const string ImportPolicy = "PC validation: DecompressOnLoad + PCM, PreserveSampleRate, stereo, preload. Original Vorbis q8 files copied byte-for-byte. Unity PCM import creates an internal decoded/platform representation; no second Vorbis encoding is requested. No claim of identical source/build size, exact internal bit depth, or original-recording losslessness.";

        private sealed class SourceSample
        {
            public string path;
            public string name;
            public int pitch;
            public int channels;
            public int frequency;
            public long frames;
            public long bytes;
            public string sha256;
        }

        [Serializable]
        private sealed class RootMapping
        {
            public string filename;
            public int rootPitch;
            public int keyLow;
            public int keyHigh;
            public int leftSampleId;
            public int rightSampleId;
            public int sourceVelocityLow;
            public int sourceVelocityHigh;
            public int sf2FineTuneCents;
            public bool sf2FineTuneDefaulted;
            public float tuningCents;
            public int sf2ReleaseTimecents;
            public bool sf2ReleaseDefaulted;
            public int releaseDurationMs;
        }

        [Serializable]
        private sealed class BankMapping
        {
            public string sourceAnalysisFilename;
            public string sourceAnalysisSha256;
            public string sourceUrl;
            public string note;
            public RootMapping[] roots;
        }

        [Serializable]
        private sealed class ClipReport
        {
            public string filename;
            public string assetPath;
            public string sourceSha256;
            public bool originalBytesPreserved;
            public long encodedSourceBytes;
            public int rootPitch;
            public int regionLow;
            public int regionHigh;
            public float tuningCents;
            public int releaseDurationMs;
            public int sourceChannels;
            public int sourceFrequency;
            public long sourceFrames;
            public int importedChannels;
            public int importedFrequency;
            public int importedFrames;
            public long decodedFloat32EstimateBytes;
            public long observedEditorClipMemoryBytes;
            public string importCompression;
            public string loadType;
        }

        [Serializable]
        private sealed class ImportReport
        {
            public string generatedAtUtc;
            public string encodedSourceAssetDirectory;
            public string originalReadmeAssetPath;
            public string bankMappingAssetPath;
            public string officialSource;
            public string author;
            public string license;
            public string sourceEncoding;
            public string importPolicy;
            public string memoryMeasurementScope;
            public string bankPath;
            public string bankHash;
            public int clipCount;
            public long encodedSourceBytes;
            public long totalFrames;
            public long decodedFloat32EstimateBytes;
            public long observedEditorClipMemoryBytes;
            public ClipReport[] clips;
        }

        public static PianoBankAsset Import()
        {
            PianoBankAsset existing = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(BankPath);
            if (existing == null)
                throw new InvalidOperationException("No imported Salamander bank exists. Call Import(string sourceDirectory, string licenseSource) with explicit source and original-readme paths; this tool does not search the local computer.");
            MidiDataBaker.ValidateBank(existing);
            return existing;
        }

        public static PianoBankAsset Import(string sourceDirectory, string licenseSource)
        {
            return Import(sourceDirectory, licenseSource, null);
        }

        public static PianoBankAsset Import(string sourceDirectory, string licenseSource, string analysisPath)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || string.IsNullOrWhiteSpace(licenseSource))
                throw new ArgumentException("Explicit sample sourceDirectory and original-readme licenseSource paths are required.");
            sourceDirectory = Path.GetFullPath(sourceDirectory);
            licenseSource = Path.GetFullPath(licenseSource);
            // Complete source validation before touching existing imported files or bank metadata.
            List<SourceSample> samples = ReadSources(sourceDirectory);
            if (!File.Exists(licenseSource)) throw new FileNotFoundException("Salamander original attribution/readme is required.", licenseSource);
            if (string.IsNullOrWhiteSpace(analysisPath))
            {
                DirectoryInfo directory = new DirectoryInfo(sourceDirectory);
                if (directory.Parent == null || directory.Parent.Parent == null)
                    throw new InvalidOperationException("Cannot infer analysis.json: supply the third analysisPath argument explicitly.");
                analysisPath = Path.Combine(directory.Parent.Parent.FullName, "analysis.json");
            }
            BankMapping mapping = ReadMapping(samples, Path.GetFullPath(analysisPath));
            Directory.CreateDirectory(DestinationDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(BankPath));
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SourceSample sample in samples) expected.Add(sample.name);
            foreach (string existing in Directory.GetFiles(DestinationDirectory, "*.ogg"))
                if (!expected.Contains(Path.GetFileName(existing)))
                    throw new InvalidOperationException("Unexpected audio in the dedicated Salamander v9 folder; preserve and resolve it explicitly: " + existing);

            var checksums = new StringBuilder();
            foreach (SourceSample sample in samples)
            {
                string target = DestinationDirectory + "/" + sample.name;
                if (!File.Exists(target) || MidiContentHash.Bytes(File.ReadAllBytes(target)) != sample.sha256)
                    File.Copy(sample.path, target, true);
                if (MidiContentHash.Bytes(File.ReadAllBytes(target)) != sample.sha256)
                    throw new InvalidOperationException("Source/copy checksum mismatch: " + sample.name);
                checksums.Append(sample.sha256).Append("  ").Append(sample.name).Append('\n');
            }
            File.Copy(licenseSource, DestinationDirectory + "/ORIGINAL-README.txt", true);
            File.WriteAllText(DestinationDirectory + "/SOURCE-AND-ADAPTATION.txt", AttributionText(), new UTF8Encoding(false));
            File.WriteAllText(DestinationDirectory + "/SHA256SUMS.txt", checksums.ToString(), new UTF8Encoding(false));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var staged = ScriptableObject.CreateInstance<PianoBankAsset>();
            try
            {
                int count = samples.Count;
                staged.clips = new AudioClip[count];
                staged.rootPitch = new int[count];
                staged.lowPitch = new int[count];
                staged.highPitch = new int[count];
                staged.tuningCents = new float[count];
                staged.regionGain = new float[count];
                staged.releaseDurationMs = new int[count];
                staged.sourceAttribution = "Salamander Grand Piano — Alexander Holm; FreePats SF2 v3+20200602 assembled by roberto@zenvoid.org. Adapted for Music MIDI Player: one v9 layer, 30 stereo roots, full natural tails; SF2 fine tuning, key ranges and release retained; pre-encoded Vorbis q8 source copied unchanged. " + OfficialSource;
                staged.redistributionLicense = "CC BY 3.0 Unported — " + LicenseUrl + " . Retain ORIGINAL-README.txt and SOURCE-AND-ADAPTATION.txt with redistribution; original credits and changes are recorded there.";
                var report = new ImportReport
                {
                    generatedAtUtc = DateTime.UtcNow.ToString("o"),
                    encodedSourceAssetDirectory = DestinationDirectory,
                    originalReadmeAssetPath = DestinationDirectory + "/ORIGINAL-README.txt",
                    bankMappingAssetPath = DestinationDirectory + "/BANK-MAPPING.json",
                    officialSource = OfficialSource,
                    author = "Alexander Holm; FreePats SF2 assembly by roberto@zenvoid.org",
                    license = "CC BY 3.0 Unported: " + LicenseUrl,
                    sourceEncoding = "Existing Ogg Vorbis q8, one v9 layer; importer performs no source re-encoding",
                    importPolicy = ImportPolicy,
                    memoryMeasurementScope = "Profiler.GetRuntimeMemorySizeLong(AudioClip) observed in this Editor after LoadAudioData; not a VRChat player measurement. Float32 estimate is frames * channels * 4 only; excludes voices, metadata and platform overhead. Built/download size requires a separate build measurement.",
                    bankPath = BankPath,
                    clipCount = count,
                    clips = new ClipReport[count]
                };

                for (int i = 0; i < count; i++)
                {
                    SourceSample sample = samples[i];
                    string path = DestinationDirectory + "/" + sample.name;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null) throw new InvalidOperationException("Unity did not create an AudioImporter: " + path);
                    // Preserve source stereo/rate/tails. PCM avoids asking Unity to encode Vorbis again.
                    // Quality=1 is not a no-recompression guarantee; it is irrelevant to the PCM choice.
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    settings.preloadAudioData = true;
                    importer.defaultSampleSettings = settings;
                    importer.forceToMono = false;
                    importer.ambisonic = false;
                    importer.loadInBackground = false;
                    importer.ClearSampleSettingOverride("Standalone");
                    importer.SaveAndReimport();
                    settings = importer.defaultSampleSettings;
                    if (settings.compressionFormat != AudioCompressionFormat.PCM || settings.loadType != AudioClipLoadType.DecompressOnLoad
                        || settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate || !settings.preloadAudioData
                        || importer.forceToMono || importer.ContainsSampleSettingsOverride("Standalone"))
                        throw new InvalidOperationException("Unity import settings did not retain the selected PCM/stereo/48 kHz policy: " + path);
                    AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null || clip.channels != 2 || clip.frequency != 48000 || clip.samples != sample.frames)
                        throw new InvalidOperationException("Imported clip differs in stereo/rate/natural-tail frame count: " + path);
                    if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
                        throw new InvalidOperationException("Cannot preload decoded sample: " + path);
                    staged.clips[i] = clip;
                    staged.rootPitch[i] = sample.pitch;
                    staged.lowPitch[i] = mapping.roots[i].keyLow;
                    staged.highPitch[i] = mapping.roots[i].keyHigh;
                    staged.tuningCents[i] = mapping.roots[i].tuningCents;
                    // All MIDI velocities use this one recorded layer, with runtime velocity gain.
                    staged.regionGain[i] = 1f;
                    staged.releaseDurationMs[i] = mapping.roots[i].releaseDurationMs;
                    long decodedBytes = checked((long)clip.samples * clip.channels * sizeof(float));
                    long observedBytes = Profiler.GetRuntimeMemorySizeLong(clip);
                    report.clips[i] = new ClipReport
                    {
                        filename = sample.name, assetPath = path, sourceSha256 = sample.sha256,
                        originalBytesPreserved = MidiContentHash.Bytes(File.ReadAllBytes(path)) == sample.sha256,
                        encodedSourceBytes = sample.bytes, rootPitch = sample.pitch,
                        tuningCents = staged.tuningCents[i], releaseDurationMs = staged.releaseDurationMs[i],
                        sourceChannels = sample.channels, sourceFrequency = sample.frequency, sourceFrames = sample.frames,
                        importedChannels = clip.channels, importedFrequency = clip.frequency, importedFrames = clip.samples,
                        decodedFloat32EstimateBytes = decodedBytes, observedEditorClipMemoryBytes = observedBytes,
                        importCompression = settings.compressionFormat.ToString(), loadType = settings.loadType.ToString()
                    };
                    report.encodedSourceBytes += sample.bytes;
                    report.totalFrames += clip.samples;
                    report.decodedFloat32EstimateBytes += decodedBytes;
                    report.observedEditorClipMemoryBytes += observedBytes;
                }

                MidiDataBaker.PrepareBank(staged);
                for (int i = 0; i < count; i++)
                {
                    report.clips[i].regionLow = staged.lowPitch[i];
                    report.clips[i].regionHigh = staged.highPitch[i];
                }
                PianoBankAsset bank = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(BankPath);
                if (bank == null)
                {
                    bank = ScriptableObject.CreateInstance<PianoBankAsset>();
                    AssetDatabase.CreateAsset(bank, BankPath);
                }
                EditorUtility.CopySerialized(staged, bank);
                bank.name = "SalamanderV9Bank";
                EditorUtility.SetDirty(bank);
                report.bankHash = bank.bankHash;
                File.WriteAllText(DestinationDirectory + "/BANK-MAPPING.json", JsonUtility.ToJson(mapping, true), new UTF8Encoding(false));
                File.WriteAllText(DestinationDirectory + "/IMPORT-REPORT.json", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                AssetDatabase.SaveAssetIfDirty(bank);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[MusicMidiPlayer] Salamander v9: " + count + " stereo/48 kHz clips; original source "
                    + (report.encodedSourceBytes / 1048576.0).ToString("F2") + " MiB; float32 decoded estimate "
                    + (report.decodedFloat32EstimateBytes / 1048576.0).ToString("F2") + " MiB; observed Editor AudioClip memory "
                    + (report.observedEditorClipMemoryBytes / 1048576.0).ToString("F2") + " MiB. PCM build size is not the Ogg source size. See "
                    + DestinationDirectory + "/IMPORT-REPORT.json");
                return bank;
            }
            finally { UnityEngine.Object.DestroyImmediate(staged); }
        }

        private static List<SourceSample> ReadSources(string sourceDirectory)
        {
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
            var namePattern = new Regex(@"^([A-G])(#?)(-?\d+)v9\.ogg$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var result = new List<SourceSample>();
            foreach (string path in Directory.GetFiles(sourceDirectory, "*v9.ogg", SearchOption.TopDirectoryOnly))
            {
                Match match = namePattern.Match(Path.GetFileName(path));
                if (!match.Success) throw new InvalidOperationException("Unexpected v9 sample name: " + path);
                char letter = char.ToUpperInvariant(match.Groups[1].Value[0]);
                int pitchClass = letter == 'C' ? 0 : letter == 'D' ? 2 : letter == 'E' ? 4 : letter == 'F' ? 5 : letter == 'G' ? 7 : letter == 'A' ? 9 : 11;
                if (match.Groups[2].Value == "#") pitchClass++;
                int pitch = checked((int.Parse(match.Groups[3].Value) + 1) * 12 + pitchClass);
                byte[] bytes = File.ReadAllBytes(path);
                SourceSample sample = ReadVorbis(bytes);
                sample.path = path;
                sample.name = Path.GetFileName(path);
                sample.pitch = pitch;
                sample.bytes = bytes.LongLength;
                sample.sha256 = MidiContentHash.Bytes(bytes);
                if (sample.channels != 2 || sample.frequency != 48000 || sample.frames <= 0 || sample.frames > int.MaxValue)
                    throw new InvalidOperationException("Expected a complete stereo/48 kHz natural-tail Vorbis clip: " + path);
                result.Add(sample);
            }
            result.Sort((a, b) => a.pitch.CompareTo(b.pitch));
            if (result.Count != 30) throw new InvalidOperationException("Exactly 30 v9 roots are required; found " + result.Count);
            for (int i = 0; i < result.Count; i++)
                if (result[i].pitch != 21 + i * 3) throw new InvalidOperationException("v9 roots must cover A0–C8 in minor thirds; unexpected " + result[i].name);
            return result;
        }

        private static BankMapping ReadMapping(List<SourceSample> sources, string analysisPath)
        {
            if (!File.Exists(analysisPath))
                throw new FileNotFoundException("Original SF2 analysis.json is required; fine tuning and release must not be guessed. Pass analysisPath explicitly as the third Import argument.", analysisPath);
            byte[] analysisBytes = File.ReadAllBytes(analysisPath);
            JObject analysis = JObject.Parse(Encoding.UTF8.GetString(analysisBytes).TrimStart('\ufeff'));
            var sampleArray = analysis["samples"] as JArray;
            var zoneArray = analysis["zones"] as JArray;
            if (sampleArray == null || zoneArray == null) throw new InvalidDataException("analysis.json requires samples[] and zones[].");
            var samples = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var zones = new Dictionary<int, List<JObject>>();
            foreach (JToken token in sampleArray)
            {
                var sample = token as JObject;
                if (sample == null || sample["name"] == null) throw new InvalidDataException("Invalid SF2 sample record.");
                string name = sample.Value<string>("name");
                if (samples.ContainsKey(name)) throw new InvalidDataException("Duplicate SF2 sample name: " + name);
                samples.Add(name, sample);
            }
            foreach (JToken token in zoneArray)
            {
                var zone = token as JObject;
                if (zone == null) throw new InvalidDataException("Invalid SF2 zone record.");
                int id = RequiredInt(zone, "sample_id");
                if (!zones.ContainsKey(id)) zones.Add(id, new List<JObject>());
                zones[id].Add(zone);
            }
            var result = new BankMapping
            {
                sourceAnalysisFilename = Path.GetFileName(analysisPath),
                sourceAnalysisSha256 = MidiContentHash.Bytes(analysisBytes),
                sourceUrl = OfficialSource,
                note = "Selected v9 stereo sample pairs. Signed SF2 generator 52 is tuningCents; generator 38 timecents converts as releaseSeconds=2^(timecents/1200). Standard omitted-generator defaults: fineTune 52=0 cents, coarseTune 51=0 semitones, release 38=-12000 timecents. Original v9 velocity ranges are documented but this single-layer adaptation responds to all MIDI velocities through runtime gain. No external absolute paths are exported.",
                roots = new RootMapping[sources.Count]
            };
            int expectedLow = 21;
            for (int i = 0; i < sources.Count; i++)
            {
                SourceSample source = sources[i];
                string stem = Path.GetFileNameWithoutExtension(source.name);
                JObject left;
                JObject right;
                if (!samples.TryGetValue(stem + "_L", out left) || !samples.TryGetValue(stem + "_R", out right))
                    throw new InvalidDataException("Missing v9 SF2 stereo metadata: " + stem);
                int leftId = RequiredInt(left, "id"), rightId = RequiredInt(right, "id");
                ValidateSampleMetadata(left, rightId, source);
                ValidateSampleMetadata(right, leftId, source);
                if (!zones.ContainsKey(leftId) || zones[leftId].Count != 1 || !zones.ContainsKey(rightId) || zones[rightId].Count != 1)
                    throw new InvalidDataException("Expected exactly one original v9 zone per stereo side: " + stem);
                JObject leftZone = zones[leftId][0], rightZone = zones[rightId][0];
                if (!JToken.DeepEquals(leftZone["key_range"], rightZone["key_range"])
                    || !JToken.DeepEquals(leftZone["velocity_range"], rightZone["velocity_range"]))
                    throw new InvalidDataException("SF2 left/right key or velocity ranges differ: " + stem);
                int fine = SignedGenerator(leftZone, "52");
                int release = SignedGenerator(leftZone, "38");
                if (fine != SignedGenerator(rightZone, "52") || release != SignedGenerator(rightZone, "38"))
                    throw new InvalidDataException("SF2 left/right fine tuning or release differs: " + stem);
                var keyRange = leftZone["key_range"] as JArray;
                var velocityRange = leftZone["velocity_range"] as JArray;
                if (keyRange == null || keyRange.Count != 2 || velocityRange == null || velocityRange.Count != 2)
                    throw new InvalidDataException("Missing original SF2 zone ranges: " + stem);
                int low = keyRange[0].Value<int>(), high = keyRange[1].Value<int>();
                if (low != expectedLow || high < low || high > 108 || source.pitch < low || source.pitch > high)
                    throw new InvalidDataException("SF2 selected key ranges must continuously cover A0–C8: " + stem);
                expectedLow = high + 1;
                double durationMs = Math.Pow(2.0, release / 1200.0) * 1000.0;
                if (double.IsNaN(durationMs) || double.IsInfinity(durationMs) || durationMs < 0.5 || durationMs > int.MaxValue - 0.5)
                    throw new InvalidDataException("SF2 release cannot be represented by positive runtime milliseconds: " + stem);
                result.roots[i] = new RootMapping
                {
                    filename = source.name, rootPitch = source.pitch, keyLow = low, keyHigh = high,
                    leftSampleId = leftId, rightSampleId = rightId,
                    sourceVelocityLow = velocityRange[0].Value<int>(), sourceVelocityHigh = velocityRange[1].Value<int>(),
                    sf2FineTuneCents = fine, tuningCents = fine, sf2ReleaseTimecents = release,
                    sf2FineTuneDefaulted = leftZone["generators"]["52"] == null || rightZone["generators"]["52"] == null,
                    sf2ReleaseDefaulted = leftZone["generators"]["38"] == null || rightZone["generators"]["38"] == null,
                    releaseDurationMs = (int)Math.Round(durationMs, MidpointRounding.AwayFromZero)
                };
            }
            if (expectedLow != 109) throw new InvalidDataException("SF2 selected key ranges do not reach C8.");
            return result;
        }

        private static void ValidateSampleMetadata(JObject sample, int expectedLink, SourceSample source)
        {
            if (RequiredInt(sample, "root") != source.pitch || RequiredInt(sample, "sample_rate") != source.frequency
                || RequiredInt(sample, "frames") != source.frames || RequiredInt(sample, "sample_link") != expectedLink
                || RequiredInt(sample, "pitch_correction") != 0)
                throw new InvalidDataException("SF2 sample root/rate/frames/stereo link/header correction mismatch: " + source.name);
        }

        private static int RequiredInt(JObject record, string key)
        {
            JToken value = record[key];
            if (value == null || value.Type != JTokenType.Integer) throw new InvalidDataException("Required integer SF2 metadata is missing: " + key);
            return value.Value<int>();
        }

        private static int SignedGenerator(JObject zone, string number)
        {
            var generators = zone["generators"] as JObject;
            if (generators == null) throw new InvalidDataException("Missing SF2 generators.");
            int value = SignedGeneratorValue(generators, number);
            if (RequiredInt(zone, "sample_mode") != 0) throw new InvalidDataException("This bank requires non-looping natural-tail SF2 zones.");
            JToken rootOverride = zone["root_override"];
            if (rootOverride != null && rootOverride.Type != JTokenType.Null)
                throw new InvalidDataException("Unexpected SF2 overriding root key; review the mapping explicitly.");
            if (SignedGeneratorValue(generators, "51") != 0) throw new InvalidDataException("Unexpected SF2 coarse tuning; review it explicitly.");
            return value;
        }

        private static int SignedGeneratorValue(JObject generators, string number)
        {
            if (generators[number] == null)
            {
                if (number == "52" || number == "51") return 0;
                if (number == "38") return -12000;
                throw new InvalidDataException("Required SF2 generator is missing: " + number);
            }
            int value = RequiredInt(generators, number);
            if (value < short.MinValue || value > ushort.MaxValue) throw new InvalidDataException("Invalid 16-bit SF2 generator: " + number);
            return value > short.MaxValue ? value - 65536 : value;
        }

        private static SourceSample ReadVorbis(byte[] data)
        {
            var result = new SourceSample();
            int cursor = 0;
            uint serial = 0;
            bool identified = false;
            bool ended = false;
            long lastGranule = -1;
            while (cursor < data.Length)
            {
                if (data.Length - cursor < 27 || data[cursor] != 'O' || data[cursor + 1] != 'g' || data[cursor + 2] != 'g' || data[cursor + 3] != 'S' || data[cursor + 4] != 0)
                    throw new InvalidDataException("Invalid/truncated Ogg page.");
                int segments = data[cursor + 26];
                int payload = checked(cursor + 27 + segments);
                if (payload > data.Length) throw new InvalidDataException("Truncated Ogg segment table.");
                int bodyLength = 0;
                for (int i = 0; i < segments; i++) bodyLength += data[cursor + 27 + i];
                if (bodyLength > data.Length - payload) throw new InvalidDataException("Truncated Ogg payload.");
                uint pageSerial = ReadUInt32(data, cursor + 14);
                if (!identified)
                {
                    if (bodyLength < 30 || data[payload] != 1 || Encoding.ASCII.GetString(data, payload + 1, 6) != "vorbis")
                        throw new InvalidDataException("Expected a Vorbis identification packet.");
                    serial = pageSerial;
                    result.channels = data[payload + 11];
                    result.frequency = checked((int)ReadUInt32(data, payload + 12));
                    identified = true;
                }
                else if (serial != pageSerial) throw new InvalidDataException("Chained/multiplexed Ogg is not supported by this bank importer.");
                long granule = unchecked((long)((ulong)ReadUInt32(data, cursor + 6) | ((ulong)ReadUInt32(data, cursor + 10) << 32)));
                if (granule >= 0) lastGranule = granule;
                if ((data[cursor + 5] & 4) != 0) ended = true;
                cursor = payload + bodyLength;
            }
            if (!identified || !ended || lastGranule <= 0) throw new InvalidDataException("Vorbis stream lacks a valid final granule/end marker.");
            result.frames = lastGranule;
            return result;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)data[offset] | ((uint)data[offset + 1] << 8) | ((uint)data[offset + 2] << 16) | ((uint)data[offset + 3] << 24);
        }

        private static string AttributionText()
        {
            return "Salamander Grand Piano — v9 single-layer adaptation for Music MIDI Player\n\n"
                + "Original piano recordings: Alexander Holm (Yamaha C5).\n"
                + "Source distribution: Salamander Grand Piano (FreePats), SF2 v3+20200602, assembled by roberto@zenvoid.org for the FreePats project.\n"
                + "Official source: " + OfficialSource + "\n"
                + "License: Creative Commons Attribution 3.0 Unported (CC BY 3.0) — " + LicenseUrl + "\n"
                + "The unmodified upstream readme, including its additional credits and changelog, is preserved in ORIGINAL-README.txt. No endorsement by the original creators is implied.\n\n"
                + "Changes: selected only recorded velocity layer v9 (one of the original 16), retaining all 30 minor-third roots A0–C8, 48,000 Hz, stereo and each selected sample's full natural tail; no loops. Original SF2 per-root signed fine tuning (generator 52), key ranges and release timecents (generator 38) are retained in BANK-MAPPING.json and baked into the player bank; left/right metadata must agree. Existing extracted/encoded Vorbis q8 files from the user's 2026-10-24 Salamander analysis are copied byte-for-byte, verified in SHA256SUMS.txt. The importer performs no Ogg encoding. All MIDI velocities use this one timbre with gain response, so this adaptation does not reproduce the original 16-layer timbral dynamics, pedal/mechanical noises or all SFZ/SF2 behavior.\n\n"
                + "Unity import policy: " + ImportPolicy + "\n"
                + "The 10,188,801-byte (about 9.72 MiB) selection is the encoded-source payload, not a Unity build/download or runtime-memory guarantee. Unity decodes/imports the Ogg sources into its PCM/platform representation; this may involve internal sample representation/quantization. A Quality=1 Vorbis setting would not establish bitstream pass-through, so it is not used as a no-re-encoding claim. Actual imported frame counts and observed Editor AudioClip memory are recorded in IMPORT-REPORT.json; target-player memory and final build/download size require separate measurement.\n";
        }
    }
}
