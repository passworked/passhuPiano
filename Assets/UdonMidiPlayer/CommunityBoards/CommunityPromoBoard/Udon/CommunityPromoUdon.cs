// 社区宣传栏：激活本地输入框，供用户复制预填的联系方式。
// QQ、Discord 与 Patreon 入口共用预制体中的 VRChat 输入框。
// VRChat 入口仅响应本地点击，打开客户端群组详情页。
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace MusicMidiPlayer.CommunityBoards
{
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CommunityPromoUdon : UdonSharpBehaviour
{
    [Header("直接复制输入")]
    public VRCUrlInputField copyInput;

    [Header("编辑期预填值")]
    public VRCUrl qqValue;
    public VRCUrl discordValue;
    public VRCUrl patreonValue;
    public string vrchatGroupId = "";

    private bool popupPending;
    private VRCUrlInputField pendingInput;
    private string pendingUrl;
    private int popupReadyFrame;

    private void Start()
    {
        if (copyInput != null && qqValue != null) copyInput.SetUrl(qqValue);
    }

    public void OpenQQ()
    {
        ActivateCopyInput(qqValue);
    }

    public void OpenDiscord()
    {
        ActivateCopyInput(discordValue);
    }

    public void OpenPatreon()
    {
        // Commit the URL on click; reuse the bounded two-frame popup, with no polling.
        ActivateCopyInput(patreonValue);
    }

    public void OpenVRChat()
    {
        CancelPendingPopup();
        // Dispatch directly in the click event. No polling, delayed task or network broadcast.
        if(string.IsNullOrEmpty(vrchatGroupId) || !vrchatGroupId.StartsWith("grp_"))return;
        VRC.Economy.Store.OpenGroupPage(vrchatGroupId);
    }

    private void ActivateCopyInput(VRCUrl value)
    {
        if (copyInput == null || value == null) return;
        // The SDK only invokes onSelected from ActivateInputField when isFocused is
        // already true. The first activation merely schedules focus in native LateUpdate.
        copyInput.DeactivateInputField();
        copyInput.SetUrl(value);
        copyInput.ActivateInputField();
        pendingInput = copyInput;
        pendingUrl = value.Get();
        popupReadyFrame = Time.frameCount + 2;
        // One coalesced request; latest click wins. Two frames allow native LateUpdate
        // to run regardless of UI/Udon ordering (~33ms at 60fps), with no idle polling.
        if (popupPending) return;
        popupPending = true;
        SendCustomEventDelayedFrames(nameof(OpenFocusedUrlPopup), 2);
    }

    public void OpenFocusedUrlPopup()
    {
        if (!popupPending) return;
        if (Time.frameCount < popupReadyFrame)
        {
            SendCustomEventDelayedFrames(nameof(OpenFocusedUrlPopup), popupReadyFrame - Time.frameCount);
            return;
        }
        popupPending = false;
        // Never revive a closed/deselected field or apply an older click to another URL.
        if (pendingInput == null || copyInput != pendingInput || !pendingInput.isFocused) return;
        if (pendingInput.GetUrl().Get() != pendingUrl) return;
        pendingInput.ActivateInputField();
    }

    private void CancelPendingPopup()
    {
        popupPending = false;
        if (pendingInput != null) pendingInput.DeactivateInputField();
        pendingInput = null;
    }

    private void OnDisable()
    {
        CancelPendingPopup();
    }
}

}
