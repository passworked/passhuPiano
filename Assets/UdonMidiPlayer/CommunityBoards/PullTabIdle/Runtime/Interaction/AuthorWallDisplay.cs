using TMPro;
using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.CommunityBoards
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuthorWallDisplay : UdonSharpBehaviour
    {
        public LocalizationManager localization;
        public TMP_Text heading, nameLabel;
        public TMP_Text qqCaption, groupCaption, discordCaption, patreonCaption;
        public string authorName = "-帕休-";
        public string qqGroupNumber = "1081163767";
        public string vrchatGroupCode = "PLUCK.8289";
        private bool registered;

        private void OnEnable()
        {
            if (localization != null && !registered)
            {
                localization.Register(this);
                registered = true;
            }
            RefreshLanguage();
        }

        // Fixed content is committed on enable and language events only. No Update,
        // poller, download, delayed chain or transaction dependency is added. Language
        // input dispatch updates the title and four captions in the same event; the author's name stays
        // exactly as supplied, in every language.
        public void RefreshLanguage()
        {
            int language = localization != null ? localization.languageIndex : 0;
            string title = language == 1 ? "制作者" : language == 2 ? "CREATOR" : language == 3 ? "제작자" : "作者";
            if (localization != null) title = localization.Get("community.creator", title);
            if (heading != null && heading.text != title) heading.text = title;
            if (nameLabel != null && nameLabel.text != authorName) nameLabel.text = authorName;
            string qq = language == 1 ? "QQ グループ" : language == 2 ? "QQ group" : language == 3 ? "QQ 그룹" : "QQ 群";
            string group = language == 1 ? "VRChat グループ" : language == 2 ? "VRChat group" : language == 3 ? "VRChat 그룹" : "VRChat 群组";
            string discord = language == 1 ? "Discord 招待" : language == 2 ? "Discord invite" : language == 3 ? "Discord 초대" : "Discord 邀请";
            if (localization != null)
            {
                qq = localization.Get("community.qq", qq);
                group = localization.Get("community.vrchat", group);
                discord = localization.Get("community.discord", discord);
            }
            SetCaption(qqCaption, qq + "\n" + qqGroupNumber);
            SetCaption(groupCaption, group + "\n" + vrchatGroupCode);
            SetCaption(discordCaption, discord);
            SetCaption(patreonCaption, "Patreon");
        }

        private void SetCaption(TMP_Text target, string value)
        {
            if (target != null && target.text != value) target.text = value;
        }
    }
}
