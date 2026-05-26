using System.Collections;
using Unity.Netcode;
using UnityEngine;
using TMPro;

/// <summary>
/// Scene Canvas component that shows the current phase + phase countdown.
///
/// CRITICAL: this script's GameObject must STAY ENABLED in the scene — it hides itself by
/// toggling the TMP_Text component's .enabled property, not by disabling its own GameObject.
/// (Disabling the GameObject would stop its coroutines.)
/// </summary>
public class PhaseUI : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("The text element that shows the phase message.")]
    public TMP_Text phaseText;

    [Tooltip("Optional: a backing panel/image to show/hide along with the text. (This CAN be disabled — it's not this script's host.)")]
    public GameObject panel;

    [Header("Messages")]
    public string previewMessage = "Monster is exploring the map…  {0}s";
    public string hideMonsterMessage = "Locked in the void…  {0}s";
    public string hideSurvivorMessage = "Hide!  {0}s";

    private bool _subscribedRound;
    private int _localRole = -1;

    private void Start()
    {
        SetVisible(false);
        StartCoroutine(BindWhenReady());
    }

    private void OnDestroy()
    {
        if (_subscribedRound && RoundManager.Instance != null)
        {
            RoundManager.Instance.CurrentPhase.OnValueChanged -= OnPhaseChanged;
            RoundManager.Instance.PhaseTimer.OnValueChanged -= OnTimerChanged;
        }
    }

    private IEnumerator BindWhenReady()
    {
        Debug.Log("[PhaseUI] BindWhenReady started.");

        while (RoundManager.Instance == null) yield return null;
        Debug.Log("[PhaseUI] RoundManager.Instance resolved.");

        while (NetworkManager.Singleton == null ||
               NetworkManager.Singleton.LocalClient == null ||
               NetworkManager.Singleton.LocalClient.PlayerObject == null)
        {
            yield return null;
        }

        var stateSync = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerStateSync>();
        if (stateSync != null) _localRole = stateSync.RoleIndex.Value;
        Debug.Log($"[PhaseUI] Local role resolved: {_localRole}");

        RoundManager.Instance.CurrentPhase.OnValueChanged += OnPhaseChanged;
        RoundManager.Instance.PhaseTimer.OnValueChanged += OnTimerChanged;
        _subscribedRound = true;

        Refresh(RoundManager.Instance.CurrentPhase.Value, RoundManager.Instance.PhaseTimer.Value);
        Debug.Log($"[PhaseUI] Subscribed. Initial phase={RoundManager.Instance.CurrentPhase.Value} timer={RoundManager.Instance.PhaseTimer.Value}");
    }

    private void OnPhaseChanged(RoundPhase prev, RoundPhase next)
    {
        Debug.Log($"[PhaseUI] OnPhaseChanged: {prev} -> {next}");
        Refresh(next, RoundManager.Instance.PhaseTimer.Value);
    }

    private void OnTimerChanged(int prev, int next)
    {
        if (RoundManager.Instance == null) return;
        Refresh(RoundManager.Instance.CurrentPhase.Value, next);
    }

    private void Refresh(RoundPhase phase, int timer)
    {
        switch (phase)
        {
            case RoundPhase.MonsterPreview:
                SetText(string.Format(previewMessage, timer));
                SetVisible(true);
                break;

            case RoundPhase.SurvivorHide:
                if (_localRole == 1)
                    SetText(string.Format(hideMonsterMessage, timer));
                else
                    SetText(string.Format(hideSurvivorMessage, timer));
                SetVisible(true);
                break;

            default:
                SetVisible(false);
                break;
        }
    }

    private void SetText(string s)
    {
        if (phaseText != null) phaseText.text = s;
    }

    // FIX: toggle the TMP renderer's .enabled property instead of GameObject.SetActive,
    // so this script's host GameObject stays alive and its coroutines keep running.
    private void SetVisible(bool v)
    {
        if (phaseText != null && phaseText.enabled != v)
            phaseText.enabled = v;
        if (panel != null && panel.activeSelf != v)
            panel.SetActive(v);
    }
}