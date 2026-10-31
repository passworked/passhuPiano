using System;
using System.IO;
using UnityEngine;
using UdonSharp;
using MusicMidiPlayer.Editor.Import;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Explicit entry points for Unity MCP execute_code; no file polling or UI automation.
    public static class MidiEditorJobRunner
    {
        [Serializable] private class Report
        {
            public string operation;
            public bool success;
            public string message;
            public string timestamp;
        }

        public static string BuildDemo()
        {
            MidiPlayerBuilder.BuildDemo();
            return WriteReport("build-demo", "Prefab, synthetic piano bank, example MIDI and demo scene saved.");
        }

        public static string Validate()
        {
            string parser = MidiParserValidation.RunAll();
            MidiPlayerBuilder.EnsurePrograms();
            return WriteReport("validate", parser + " UdonSharp compilation passed.");
        }

        private static string WriteReport(string operation, string message)
        {
            var report = new Report { operation = operation, success = true, message = message, timestamp = DateTime.UtcNow.ToString("o") };
            Directory.CreateDirectory("Logs");
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText("Logs/MusicMidiPlayer-editor-report.json", json);
            return json;
        }
    }
}
