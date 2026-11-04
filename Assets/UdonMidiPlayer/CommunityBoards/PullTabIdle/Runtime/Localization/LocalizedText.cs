using UdonSharp;
using UnityEngine;
using TMPro;

namespace MusicMidiPlayer.CommunityBoards
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LocalizedText : UdonSharpBehaviour
    {
        public LocalizationManager localization;
        public TMP_Text target;
        [Tooltip("Empty: keep source text. @dynamic: localize changing gameplay messages using source templates.")]
        public string key = "";
        [TextArea] public string fallbackText = "";
        [Tooltip("Bind the shared four-language font at editor import; Udon cannot assign TMP font assets at runtime.")]
        public bool applyLanguageFont = true;
        private string sourceText;
        private string renderedText;
        private bool initialized;
        private bool registered;
        private string runtimeSuffix="";
        private string runtimeOverride="";

        // Temporary button feedback owns the caption while its normal suffix keeps ticking.
        // Language changes must not replace an active error with the normal caption.
        public void SetRuntimeOverride(string message)
        {
            if(runtimeOverride==message)return;
            runtimeOverride=message;RefreshLanguage();
        }

        public void SetRuntimeSuffix(string suffix)
        {
            if(runtimeSuffix==suffix)return;
            runtimeSuffix=suffix;RefreshLanguage();
        }

        private void Start() { RefreshLanguage(); }
        private void OnEnable() { RefreshLanguage(); }

        public void RefreshLanguage()
        {
            if (target == null) target = GetComponent<TMP_Text>();
            if (target == null) return;
            if (localization == null)
            {
                GameObject root = GameObject.Find("PullTab Localization");
                if (root != null) localization = root.GetComponent<LocalizationManager>();
            }
            if (localization != null && !registered) { localization.Register(this); registered = true; }
            if (!initialized) { initialized = true; sourceText = target.text; renderedText = target.text; }
            if (target.text != renderedText) sourceText = target.text;
            if (string.IsNullOrEmpty(key) || key.Trim().Length == 0) return;
            string fallback=string.IsNullOrEmpty(fallbackText)?sourceText:fallbackText;
            renderedText=localization==null?(key=="@dynamic"?sourceText:fallback)
                :key=="@dynamic"?localization.TranslateMessage(sourceText):localization.Get(key,fallback);
            renderedText=string.IsNullOrEmpty(runtimeOverride)?renderedText+runtimeSuffix
                :localization==null?runtimeOverride:localization.TranslateMessage(runtimeOverride);
            target.text = renderedText;
        }

        private void LateUpdate()
        {
            if (key == "@dynamic" && target != null && target.text != renderedText) RefreshLanguage();
        }
    }
}
