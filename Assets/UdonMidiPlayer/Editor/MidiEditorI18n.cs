using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor
{
    /// <summary>Editor-only localization. English strings are stable keys and the fallback text.</summary>
    public static class MidiEditorI18n
    {
        private const string PreferenceKey = "MusicMidiPlayer.Editor.Language";
        private static readonly string[] LanguageCodes = { "en", "zh", "ja" };
        private static readonly Dictionary<string, string[]> Translations = MidiEditorTranslations.Create();
        private static readonly List<DiagnosticPattern> DiagnosticPatterns = new List<DiagnosticPattern>();
        private static readonly Dictionary<string, string> DiagnosticCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string diagnosticLanguage;
        private static string language = EditorPrefs.GetString(PreferenceKey, "auto");
        private static string automaticLanguage;

        static MidiEditorI18n()
        {
            foreach (string[] entry in MidiLibraryTranslations.Entries)
                Translations[entry[0]] = new[] { entry[1], entry[2], entry[3] };
            string normalizedLanguage = NormalizeSelection(language);
            if (language != normalizedLanguage)
            {
                language = normalizedLanguage;
                EditorPrefs.SetString(PreferenceKey, language);
            }
            foreach (string[] entry in MidiEditorDiagnosticTranslations.Entries)
            {
                Translations[entry[0]] = new[] { entry[1], entry[2], entry[3] };
                DiagnosticPatterns.Add(new DiagnosticPattern(entry[0], entry[1]));
            }
        }

        public static string Language
        {
            get { return language; }
            set
            {
                language = NormalizeSelection(value);
                automaticLanguage = null;
                EditorPrefs.SetString(PreferenceKey, language);
                foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>()) window.Repaint();
            }
        }

        public static string ResolvedLanguage
        {
            get
            {
                if (language == "zh")
                    return (automaticLanguage ?? (automaticLanguage = DetectLanguage())) == "zh-Hant" ? "zh-Hant" : "zh-Hans";
                if (language != "auto" && Array.IndexOf(LanguageCodes, language) >= 0) return language;
                return automaticLanguage ?? (automaticLanguage = DetectLanguage());
            }
        }

        public static string Tr(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            string[] values;
            if (!Translations.TryGetValue(english, out values)) return english;
            int index;
            switch (ResolvedLanguage)
            {
                case "zh-Hans": index = 0; break;
                case "zh-Hant": index = 1; break;
                case "ja": index = 2; break;
                default: index = -1; break;
            }
            return index >= 0 && index < values.Length && !string.IsNullOrEmpty(values[index]) ? values[index] : english;
        }

        public static string Format(string english, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, Tr(english), arguments);
        }

        public static GUIContent Content(string english, string tooltip = null)
        {
            return new GUIContent(Tr(english), Tr(tooltip));
        }

        /// <summary>Localizes legacy parser/baker diagnostics at display time without changing stored song data.</summary>
        public static string LocalizeDiagnostic(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            if (diagnosticLanguage != ResolvedLanguage)
            {
                DiagnosticCache.Clear();
                diagnosticLanguage = ResolvedLanguage;
            }
            string localized;
            if (DiagnosticCache.TryGetValue(message, out localized)) return localized;
            int context = message.IndexOf(" (track=", StringComparison.Ordinal);
            if (context >= 0) localized = LocalizeDiagnostic(message.Substring(0, context)) + message.Substring(context);
            else
            {
                localized = message;
                foreach (DiagnosticPattern pattern in DiagnosticPatterns)
                {
                    Match match = pattern.pattern.Match(message);
                    if (!match.Success) continue;
                    object[] arguments = new object[pattern.argumentCount];
                    for (int i = 0; i < arguments.Length; i++) arguments[i] = LocalizeDiagnostic(match.Groups["a" + i].Value);
                    localized = Format(pattern.english, arguments);
                    break;
                }
            }
            DiagnosticCache[message] = localized;
            return localized;
        }

        public static string[] LocalizeDiagnostics(string[] messages)
        {
            if (messages == null) return Array.Empty<string>();
            var localized = new string[messages.Length];
            for (int i = 0; i < messages.Length; i++) localized[i] = LocalizeDiagnostic(messages[i]);
            return localized;
        }

        private sealed class DiagnosticPattern
        {
            internal readonly string english;
            internal readonly Regex pattern;
            internal readonly int argumentCount;

            internal DiagnosticPattern(string english, string source)
            {
                this.english = english;
                var expression = new StringBuilder("\\A");
                int previous = 0;
                foreach (Match token in Regex.Matches(source, @"\{(\d+)\}"))
                {
                    int index = int.Parse(token.Groups[1].Value, CultureInfo.InvariantCulture);
                    argumentCount = Math.Max(argumentCount, index + 1);
                    expression.Append(Regex.Escape(source.Substring(previous, token.Index - previous)));
                    expression.Append("(?<a").Append(index).Append(">.*?)");
                    previous = token.Index + token.Length;
                }
                expression.Append(Regex.Escape(source.Substring(previous))).Append("\\z");
                pattern = new Regex(expression.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
            }
        }

        public static void DrawLanguageSelector()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect button = GUILayoutUtility.GetRect(32f, 28f, GUILayout.Width(32f), GUILayout.Height(28f));
            if (GUI.Button(button, new GUIContent("", Tr("Editor language")), EditorStyles.miniButton))
                PopupWindow.Show(button, new LanguagePopup());
            DrawLanguageIcon(new Rect(button.x + 4f, button.y + 2f, button.width - 8f, button.height - 4f), "Language");
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawLanguageIcon(Rect rect, string code)
        {
            if (Event.current.type != EventType.Repaint) return;
            Sprite sprite = Prefab.MidiLanguageIconAssets.Icon(code == "zh" ? "zh-Hans" : code);
            if (sprite == null) return;
            Color previous = GUI.color;
            GUI.color = code == "Language" || code == "auto" ? new Color(0.22f, 0.82f, 0.94f, 1f) : Color.white;
            GUI.DrawTexture(rect, sprite.texture, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        private sealed class LanguagePopup : PopupWindowContent
        {
            private const int Columns = 3;
            private const float Padding = 8f, Gap = 6f, CellWidth = 64f, CellHeight = 48f;
            private static readonly Color SelectionColor = new Color(0.22f, 0.82f, 0.94f, 1f);

            public override Vector2 GetWindowSize()
            {
                return new Vector2(Padding * 2f + Columns * CellWidth + (Columns - 1) * Gap,
                    Padding * 2f + CellHeight);
            }

            public override void OnGUI(Rect rect)
            {
                string[] names = { "English", "中文", "日本語" };
                string selectedLanguage = language == "auto" ? NormalizeSelection(ResolvedLanguage) : language;
                int current = Math.Max(0, Array.IndexOf(LanguageCodes, selectedLanguage));
                for (int i = 0; i < LanguageCodes.Length; i++)
                {
                    Rect cell = new Rect(Padding + i * (CellWidth + Gap), Padding, CellWidth, CellHeight);
                    if (GUI.Button(cell, new GUIContent("", names[i]), EditorStyles.miniButton))
                    {
                        Language = LanguageCodes[i];
                        editorWindow.Close();
                        return;
                    }
                    DrawLanguageIcon(new Rect(cell.x + 10f, cell.y + 8f, cell.width - 20f, cell.height - 16f), LanguageCodes[i]);
                    if (i == current && Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(new Rect(cell.x, cell.y, cell.width, 2f), SelectionColor);
                        EditorGUI.DrawRect(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), SelectionColor);
                        EditorGUI.DrawRect(new Rect(cell.x, cell.y, 2f, cell.height), SelectionColor);
                        EditorGUI.DrawRect(new Rect(cell.xMax - 2f, cell.y, 2f, cell.height), SelectionColor);
                        EditorGUI.DrawRect(new Rect(cell.xMax - 8f, cell.y + 4f, 4f, 4f), SelectionColor);
                    }
                }
            }
        }

        private static string NormalizeSelection(string value)
        {
            if (value == "auto") return value;
            if (value == "zh-Hans" || value == "zh-Hant") return "zh";
            return Array.IndexOf(LanguageCodes, value) >= 0 ? value : "auto";
        }

        private static string DetectLanguage()
        {
            // Unity does not expose the editor locale as a public API in every supported version.
            // Read it when available, then fall back to the operating system's language/culture.
            try
            {
                Type database = typeof(EditorWindow).Assembly.GetType("UnityEditor.LocalizationDatabase");
                PropertyInfo property = database == null ? null : database.GetProperty("currentEditorLanguage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object value = property == null ? null : property.GetValue(null, null);
                if (value is SystemLanguage) return FromSystemLanguage((SystemLanguage)value);
                if (value != null) return NormalizeLanguage(value.ToString());
            }
            catch (Exception) { /* Editor localization APIs differ between Unity versions. */ }
            return FromSystemLanguage(Application.systemLanguage);
        }

        private static string FromSystemLanguage(SystemLanguage value)
        {
            switch (value)
            {
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.ChineseTraditional: return "zh-Hant";
                case SystemLanguage.ChineseSimplified: return "zh-Hans";
                case SystemLanguage.Chinese: return NormalizeLanguage(CultureInfo.CurrentUICulture.Name);
                default: return "en";
            }
        }

        private static string NormalizeLanguage(string value)
        {
            value = (value ?? "").Replace('_', '-').ToLowerInvariant();
            if (value.StartsWith("ja") || value == "japanese") return "ja";
            if (value.StartsWith("zh") || value.StartsWith("chinese"))
                return value.Contains("hant") || value.Contains("-tw") || value.Contains("-hk") || value.Contains("-mo") || value.Contains("traditional") ? "zh-Hant" : "zh-Hans";
            return "en";
        }
    }
}
