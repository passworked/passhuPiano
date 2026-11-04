using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace MusicMidiPlayer.CommunityBoards
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SponsorWallDisplay : UdonSharpBehaviour
    {
        public MembershipAccess membership;
        public LocalizationManager localization;
        public TMP_Text heading;
        public TMP_Text[] nameLabels;
        public TMP_Text pageLabel;
        public GameObject previousButton, nextButton;

        private const int MaxNames = 1000, NamesPerFrame = 32;
        private string[] names, candidateNames;
        private DataList pendingNames, pendingIndices;
        private int count, page, parseIndex;
        private double candidateExpires;
        private float candidateExpiresRealtime;
        private string sourceUrl;
        private bool running, scheduled, registered;

        private double UtcSeconds() { return DateTime.UtcNow.Ticks / 10000000d - 62135596800d; }

        private void OnEnable()
        {
            running = true;
            if (localization != null && !registered) { localization.Register(this); registered = true; }
            RefreshLanguage();
            _Clear();
        }

        private void OnDisable() { running = false; _Clear(); }

        public void RefreshLanguage()
        {
            if (heading == null) return;
            int language = localization != null ? localization.languageIndex : 0;
            heading.text = language == 1 ? "スポンサー" : language == 2 ? "SUPPORTERS" : language == 3 ? "후원자" : "赞助者";
            if (localization != null) heading.text = localization.Get("community.supporters", heading.text);
        }

        // The membership reader dispatches its already validated response directly here.
        // No second downloader, Update or refresh timer. New responses replace pending
        // display work; the single queued callback always uses the latest snapshot.
        public void _SetSnapshot(DataList displayNames, DataList supporterIndices, double expiry, string url)
        {
            if (!running || displayNames == null || supporterIndices == null
                || displayNames.Count > MaxNames || supporterIndices.Count > MaxNames || expiry <= UtcSeconds())
            { _Clear(); return; }
            pendingNames = displayNames;
            pendingIndices = supporterIndices;
            candidateNames = new string[supporterIndices.Count];
            candidateExpires = expiry;
            candidateExpiresRealtime = Time.realtimeSinceStartup + Mathf.Min(300f, (float)(expiry - UtcSeconds()));
            sourceUrl = url;
            parseIndex = 0;
            if (!scheduled)
            {
                scheduled = true;
                SendCustomEventDelayedFrames(nameof(_ProcessSnapshot), 1);
            }
        }

        private bool SourceIsCurrent()
        {
            return running && membership != null && membership.enabled && membership.gameObject.activeInHierarchy
                && !VRCUrl.IsNullOrEmpty(membership.authorizationUrl) && membership.authorizationUrl.Get() == sourceUrl;
        }

        public void _ProcessSnapshot()
        {
            scheduled = false;
            if (pendingIndices == null) return;
            if (!SourceIsCurrent() || candidateExpires <= UtcSeconds() || Time.realtimeSinceStartup >= candidateExpiresRealtime)
            { _Clear(); return; }
            int end = Mathf.Min(parseIndex + NamesPerFrame, pendingIndices.Count);
            for (; parseIndex < end; parseIndex++)
            {
                DataToken at = pendingIndices[parseIndex];
                if (at.TokenType != TokenType.Double || at.Double < 0d || at.Double >= pendingNames.Count
                    || at.Double != Math.Floor(at.Double)) { _Clear(); return; }
                // Sorted unique indices prevent duplicate names and arbitrary ordering.
                int index = (int)at.Double;
                if (parseIndex > 0 && pendingIndices[parseIndex - 1].Double >= at.Double) { _Clear(); return; }
                DataToken entry = pendingNames[index];
                if (entry.TokenType != TokenType.String || string.IsNullOrEmpty(entry.String) || entry.String.Length > 80
                    || entry.String.Contains("\n") || entry.String.Contains("\r")) { _Clear(); return; }
                candidateNames[parseIndex] = entry.String;
            }
            if (parseIndex < pendingIndices.Count)
            {
                scheduled = true;
                SendCustomEventDelayedFrames(nameof(_ProcessSnapshot), 1);
                return;
            }
            // Atomic display commit after all indices are validated. At 30fps, at most
            // 1000 supporters take ~1.1s, concurrently with membership validation.
            names = candidateNames;
            count = names.Length;
            pendingNames = null; pendingIndices = null; candidateNames = null;
            page = Mathf.Min(page, PageCount() - 1);
            RenderPage();
        }

        // Keep the entry snapshot for this world session, like the membership decision.
        // This compatibility entry point only checks the source on page input; response
        // freshness was already checked before the bounded parse/atomic display commit.
        public void _ExpireSnapshot()
        {
            if ((count > 0 || pendingIndices != null) && !SourceIsCurrent()) _Clear();
        }

        public void _Clear()
        {
            pendingNames = null; pendingIndices = null; candidateNames = null; names = null;
            count = 0; page = 0;
            // Keep scheduled until its callback drains, including disable/re-enable.
            // Never enqueue another chain on top of an outstanding callback.
            RenderPage();
        }

        private int PageCount()
        {
            int slots = nameLabels != null ? nameLabels.Length : 0;
            return slots > 0 ? Mathf.Max(1, (count + slots - 1) / slots) : 1;
        }

        public void _PreviousPage() { _ExpireSnapshot(); if (page > 0) { page--; RenderPage(); } }
        public void _NextPage() { _ExpireSnapshot(); if (page + 1 < PageCount()) { page++; RenderPage(); } }

        private void RenderPage()
        {
            // Fixed 16 slots: input updates immediately, independent of list size.
            // Disable rich text in the installer so display names stay literal.
            if (nameLabels != null)
                for (int i = 0; i < nameLabels.Length; i++)
                    if (nameLabels[i] != null)
                    {
                        int at = page * nameLabels.Length + i;
                        string value = names != null && at < count ? names[at] : "";
                        if (nameLabels[i].text != value) nameLabels[i].text = value;
                    }
            int pages = PageCount();
            if (pageLabel != null) pageLabel.text = count > 0 ? (page + 1) + " / " + pages : "";
            if (previousButton != null) previousButton.SetActive(pages > 1 && page > 0);
            if (nextButton != null) nextButton.SetActive(pages > 1 && page + 1 < pages);
        }
    }
}
