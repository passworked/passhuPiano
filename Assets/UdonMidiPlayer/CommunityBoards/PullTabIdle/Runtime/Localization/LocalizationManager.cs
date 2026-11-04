using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace MusicMidiPlayer.CommunityBoards
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LocalizationManager : UdonSharpBehaviour
    {
        // Optional scene binding: the player's dictionaries and session language own translations.
        public MusicMidiPlayer.UI.MidiPlayerUI playerUI;
        public string[] keys = new string[0];
        public string[] zh = new string[0];
        public string[] ja = new string[0];
        public string[] en = new string[0];
        public string[] ko = new string[0];
        [Range(0,3)] public int languageIndex;
        public string preferenceKey = "pulltab.ui.language";
        public Renderer[] languageButtons;
        public UnityEngine.UI.Image[] languageButtonImages;
        public Color selectedButtonColor = new Color(.68f,.53f,.31f,1f);
        public Color normalButtonColor = new Color(.12f,.25f,.20f,1f);
        public Material selectedMaterial;
        public Material normalMaterial;
        public Renderer[] posterRenderers = new Renderer[0];
        // Language-major: zh posters, ja posters, en posters, ko posters.
        public Material[] posterMaterials = new Material[0];
        private UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[1024];
        private bool restored;
        private bool explicitlySelected;
        private string[] arguments = new string[8];

        private void Start() { ApplyClientLanguage(VRCPlayerApi.GetCurrentLanguage()); }

        private int BestLanguage(string code)
        {
            if (string.IsNullOrEmpty(code)) return 2;
            string tag = code.Trim().ToLower().Replace("_", "-");
            if (tag == "zh" || tag.StartsWith("zh-")) return 0;
            if (tag == "ja" || tag.StartsWith("ja-")) return 1;
            if (tag == "ko" || tag.StartsWith("ko-")) return 3;
            return 2;
        }

        private void ApplyClientLanguage(string code)
        {
            if (playerUI != null) { RefreshLanguage(); return; }
            if (!explicitlySelected) languageIndex = BestLanguage(code);
            RefreshLanguage();
        }

        public override void OnLanguageChanged(string language) { ApplyClientLanguage(language); }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal && !explicitlySelected)
                ApplyClientLanguage(VRCPlayerApi.GetCurrentLanguage());
        }

        public void Register(UdonSharpBehaviour listener)
        {
            if (listener == null) return;
            for (int i = 0; i < listeners.Length; i++) if (listeners[i] == listener) return;
            for (int i = 0; i < listeners.Length; i++) if (listeners[i] == null) { listeners[i] = listener; return; }
        }

        public void SetLanguage(int index)
        {
            if (playerUI != null) return;
            languageIndex = Mathf.Clamp(index, 0, 3); explicitlySelected = true;
            RefreshLanguage();
            if (restored) PlayerData.SetString(preferenceKey, LanguageCode());
        }

        public string LanguageCode()
        {
            if (languageIndex == 1) return "ja";
            if (languageIndex == 2) return "en";
            if (languageIndex == 3) return "ko";
            return "zh-Hans";
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (playerUI != null) { RefreshLanguage(); return; }
            if (!Utilities.IsValid(player) || !player.isLocal || restored) return;
            restored = true;
            if (explicitlySelected) { PlayerData.SetString(preferenceKey, LanguageCode()); return; }
            string code;
            if (PlayerData.TryGetString(player, preferenceKey, out code) && (code == "zh-Hans" || code == "ja" || code == "en" || code == "ko"))
            {
                languageIndex = BestLanguage(code); explicitlySelected = true;
            }
            else languageIndex = BestLanguage(VRCPlayerApi.GetCurrentLanguage());
            RefreshLanguage();
        }

        public void RefreshLanguage()
        {
            // Two preloaded poster materials switch in the same language/restore event.
            // No per-frame polling, texture downloads or deferred transaction work.
            if(posterRenderers!=null && posterMaterials!=null)
            {
                int count=posterRenderers.Length;
                int language=Mathf.Clamp(languageIndex,0,3);
                for(int i=0;i<count;i++)
                {
                    int at=language*count+i;
                    if(at>=posterMaterials.Length || posterRenderers[i]==null)continue;
                    Material material=posterMaterials[at];
                    int fallback=2*count+i;
                    if(material==null && fallback<posterMaterials.Length)material=posterMaterials[fallback];
                    if(material!=null && posterRenderers[i].sharedMaterial!=material)posterRenderers[i].sharedMaterial=material;
                }
            }
            // Four flat UI graphics update only on language/restore events, never per frame.
            if (languageButtonImages != null)
                for (int i = 0; i < languageButtonImages.Length; i++)
                    if (languageButtonImages[i] != null) languageButtonImages[i].color = i == languageIndex ? selectedButtonColor : normalButtonColor;
            if (languageButtons != null)
                for (int i = 0; i < languageButtons.Length; i++)
                    if (languageButtons[i] != null) languageButtons[i].sharedMaterial = i == languageIndex ? selectedMaterial : normalMaterial;
            for (int i = 0; i < listeners.Length; i++) if (listeners[i] != null) listeners[i].SendCustomEvent("RefreshLanguage");
        }

        public string Get(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key) || key.Trim().Length == 0) return fallback;
            if (playerUI != null)
            {
                string translated = playerUI.Tr(key);
                return translated == key ? fallback : translated;
            }
            for (int i = 0; i < keys.Length; i++) if (!string.IsNullOrEmpty(keys[i]) && keys[i] == key) return At(i, fallback);
            return fallback;
        }

        private string At(int i, string fallback)
        {
            string result = languageIndex == 1 ? ja[i] : languageIndex == 2 ? en[i] : languageIndex == 3 ? ko[i] : zh[i];
            if (!string.IsNullOrEmpty(result)) return result;
            if (!string.IsNullOrEmpty(en[i])) return en[i];
            return fallback;
        }

        public string Format(string key, string fallback, string a, string b, string c)
        {
            return Get(key, fallback).Replace("{0}", a ?? "").Replace("{1}", b ?? "").Replace("{2}", c ?? "");
        }

        public string FormatArgs(string key, string fallback, string[] values)
        {
            string result = Get(key, fallback);
            if (values == null) return result;
            for (int i = 0; i < values.Length; i++) result = result.Replace("{" + i + "}", values[i] ?? "");
            return result;
        }

        // Adapter for existing gameplay messages. Source strings remain outside the economy snapshot.
        public string TranslateMessage(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            for (int i = 0; i < keys.Length; i++) if (!string.IsNullOrEmpty(keys[i]) && zh[i] == source) return At(i, source);
            for (int i = 0; i < keys.Length; i++)
            {
                if (string.IsNullOrEmpty(keys[i]) || !Match(source, zh[i])) continue;
                string[] captured = new string[8];
                for (int n = 0; n < 8; n++) captured[n] = arguments[n];
                string result = At(i, source);
                for (int n = 0; n < 8; n++) result = result.Replace("{" + n + "}", TranslateFragment(captured[n]));
                return result;
            }
            return source;
        }

        private string TranslateFragment(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            for (int i = 0; i < keys.Length; i++) if (!string.IsNullOrEmpty(keys[i]) && zh[i] == source) return At(i, source);
            for (int i = 0; i < keys.Length; i++)
            {
                if (string.IsNullOrEmpty(keys[i]) || !Match(source, zh[i])) continue;
                string result = At(i, source);
                for (int n = 0; n < 8; n++) result = result.Replace("{" + n + "}", TranslateLiteral(arguments[n]));
                return result;
            }
            return source;
        }

        private string TranslateLiteral(string source)
        {
            if (string.IsNullOrEmpty(source)) return source ?? "";
            for (int i = 0; i < keys.Length; i++) if (!string.IsNullOrEmpty(keys[i]) && zh[i] == source) return At(i, source);
            return source;
        }

        private bool Match(string source, string template)
        {
            if (string.IsNullOrEmpty(template)) return false;
            int token = template.IndexOf("{0}");
            if (token < 0 || !source.StartsWith(template.Substring(0, token))) return false;
            string literal = template;
            for (int n = 0; n < 8; n++) { literal = literal.Replace("{" + n + "}", ""); arguments[n] = ""; }
            if (literal.Length == 0) return false;
            int position = token;
            for (int n = 0; n < 8; n++)
            {
                int next = n < 7 ? template.IndexOf("{" + (n + 1) + "}", token + 3) : -1;
                string delimiter = next >= 0 ? template.Substring(token + 3, next - token - 3) : template.Substring(token + 3);
                int end;
                if (next >= 0)
                {
                    if (delimiter.Length == 0) return false;
                    end = source.IndexOf(delimiter, position);
                }
                else
                {
                    if (!source.EndsWith(delimiter)) return false;
                    end = source.Length - delimiter.Length;
                }
                if (end < position) return false;
                string value = source.Substring(position, end - position);
                arguments[n] = value;
                if (next < 0) return true;
                position = end + delimiter.Length; token = next;
            }
            return false;
        }
    }
}
