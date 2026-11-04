// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MusicMidiPlayer.UI
{
    /// <summary>Local, paged attribution board. Never changes playback or ownership.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/UI/Credits")]
    public class MidiCreditsPanel : UdonSharpBehaviour
    {
        public MidiPlayerUI playerUI;
        public int[] categories = new int[0];
        public string[] names = new string[0], authors = new string[0], licenses = new string[0];
        public string[] detailKeys = new string[0], sources = new string[0], licenseUrls = new string[0];
        public TMP_Text title, subtitle, hint, pageLabel, previousLabel, nextLabel;
        public TMP_Text[] tabLabels = new TMP_Text[0];
        public GameObject[] tabMarks = new GameObject[0], rows = new GameObject[0];
        public TMP_Text[] rowNames = new TMP_Text[0], rowAuthors = new TMP_Text[0];
        public TMP_Text[] rowLicenses = new TMP_Text[0], rowDetails = new TMP_Text[0];
        public TMP_InputField[] rowSources = new TMP_InputField[0];
        public TMP_InputField[] rowLicenseSources = new TMP_InputField[0];
        public RectTransform panelRect, previousRect, nextRect, pageRect;
        public BoxCollider panelCollider;
        public RectTransform[] rowRects = new RectTransform[0], rowLicenseRects = new RectTransform[0];
        public RectTransform[] rowDetailRects = new RectTransform[0], rowSourceRects = new RectTransform[0];
        public RectTransform[] rowNameRects = new RectTransform[0], rowAuthorRects = new RectTransform[0];
        public RectTransform[] rowLicenseSourceRects = new RectTransform[0];
        public Button previous, next;
        [HideInInspector] public int category, page;

        private void Start() { Refresh(); }
        public void _Project() { Select(0); }
        public void _Samples() { Select(1); }
        public void _Fonts() { Select(2); }
        public void _Music() { Select(3); }
        public void _Previous() { page = Mathf.Max(0, page - 1); Refresh(); }
        public void _Next() { page++; Refresh(); }
        private void Select(int value) { category = value; page = 0; Refresh(); }
        private string Tr(string key) { return playerUI == null ? key : playerUI.Tr(key); }
        private string Value(string value) { return value.StartsWith("credits.") ? Tr(value) : value; }

        private float TextHeight(string value, float limit, float singleLine, float twoLines)
        {
            float units = 0;
            for (int i = 0; i < value.Length; i++) units += value[i] > '\u02FF' ? 1f : .55f;
            return units > limit ? twoLines : singleLine;
        }

        public void Refresh()
        {
            if (rows.Length == 0 || playerUI == null || rowRects.Length != rows.Length
                || rowNameRects.Length != rows.Length || rowLicenseSources.Length != rows.Length || panelRect == null) return;
            title.text = Tr("credits.title");
            subtitle.gameObject.SetActive(false);
            hint.gameObject.SetActive(false);
            previousLabel.text = Tr("credits.previous"); nextLabel.text = Tr("credits.next");
            for (int t = 0; t < tabLabels.Length; t++)
            {
                tabLabels[t].text = Tr(t == 0 ? "credits.project" : t == 1 ? "credits.samples" : t == 2 ? "credits.fonts" : "credits.music");
                tabMarks[t].SetActive(t == category);
            }
            int count = 0;
            for (int i = 0; i < categories.Length; i++) if (categories[i] == category) count++;
            int pages = Mathf.Max(1, (count + rows.Length - 1) / rows.Length);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageLabel.text = Tr("credits.page").Replace("{page}", (page + 1).ToString()).Replace("{pages}", pages.ToString());
            previous.interactable = page > 0; next.interactable = page + 1 < pages;
            int match = 0, row = 0;
            for (int i = 0; i < categories.Length && row < rows.Length; i++)
            {
                if (categories[i] != category) continue;
                if (match++ < page * rows.Length) continue;
                rows[row].SetActive(true);
                rowNames[row].text = Value(names[i]);
                rowAuthors[row].text = string.IsNullOrEmpty(authors[i]) ? Tr("credits.unknown_author") : authors[i];
                rowLicenses[row].text = licenses[i] == "credits.unrecorded_license" ? "" : Value(licenses[i]);
                bool hasDetail = !string.IsNullOrEmpty(detailKeys[i]);
                rowDetails[row].text = hasDetail ? Tr(detailKeys[i]) : "";
                rowDetails[row].gameObject.SetActive(hasDetail);
                string links = sources[i];
                bool hasLicenseSource = !string.IsNullOrEmpty(licenseUrls[i]) && licenseUrls[i] != links;
                rowSources[row].SetTextWithoutNotify(links);
                rowSources[row].interactable = !string.IsNullOrEmpty(links);
                rowSources[row].gameObject.SetActive(!string.IsNullOrEmpty(links));
                rowLicenseSources[row].SetTextWithoutNotify(hasLicenseSource ? licenseUrls[i] : "");
                rowLicenseSources[row].gameObject.SetActive(hasLicenseSource);
                float nameHeight = TextHeight(rowNames[row].text, 28f, 24f, 36f);
                float authorHeight = TextHeight(rowAuthors[row].text, 38f, 20f, 28f);
                rowNameRects[row].sizeDelta = new Vector2(526, nameHeight);
                rowAuthorRects[row].anchoredPosition = new Vector2(12, -6f - nameHeight);
                rowAuthorRects[row].sizeDelta = new Vector2(526, authorHeight);
                float cursor = 6f + nameHeight + authorHeight;
                rowLicenseRects[row].anchoredPosition = new Vector2(12, -cursor);
                if (!string.IsNullOrEmpty(rowLicenses[row].text)) cursor += 18f;
                rowDetailRects[row].anchoredPosition = new Vector2(12, -cursor);
                if (hasDetail) cursor += 28f;
                rowSourceRects[row].anchoredPosition = new Vector2(12, -cursor);
                if (!string.IsNullOrEmpty(links)) cursor += 16f;
                rowLicenseSourceRects[row].anchoredPosition = new Vector2(12, -cursor);
                if (hasLicenseSource) cursor += 16f;
                rowRects[row].sizeDelta = new Vector2(550, cursor + 6f);
                row++;
            }
            int displayed = row;
            for (; row < rows.Length; row++) rows[row].SetActive(false);
            float top = -190f;
            for (int i = 0; i < displayed; i += 2)
            {
                float height = rowRects[i].sizeDelta.y;
                if (i + 1 < displayed) height = Mathf.Max(height, rowRects[i + 1].sizeDelta.y);
                rowRects[i].anchoredPosition = new Vector2(-558, top);
                rowRects[i].sizeDelta = new Vector2(550, height);
                if (i + 1 < displayed)
                {
                    rowRects[i + 1].anchoredPosition = new Vector2(8, top);
                    rowRects[i + 1].sizeDelta = new Vector2(550, height);
                }
                top -= height + 6f;
            }
        }
    }
}
