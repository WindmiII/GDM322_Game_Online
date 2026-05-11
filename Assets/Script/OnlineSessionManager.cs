using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OnlineSessionManager : MonoBehaviour
{
    public GameObject loginPanel;
    public TMP_InputField usernameInput;
    // public TMP_Dropdown characterDropdown; // ไม่ใช้ dropdown แล้ว
    public GameObject characterPanel;
    public Button[] characterButtons;
    public Button hostButton;
    public Button clientButton;
    public Button leaveButton;
    public TMP_Text statusText;

    private bool _startAsHost = false;
    private int _pendingCharIndex = 0;

    private void Start()
    {
        leaveButton.gameObject.SetActive(false);
        ShowCharacterSelection(false);
        SetStatus("Not Connected");
        if (hostButton != null) hostButton.onClick.AddListener(OnHostButtonClicked);
        if (clientButton != null) clientButton.onClick.AddListener(OnClientButtonClicked);
        if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveButtonClick);
    }

    public void OnHostButtonClicked()
    {
        string userName = usernameInput.text;
        if (string.IsNullOrWhiteSpace(userName))
        {
            SetStatus("Please enter a name first");
            return;
        }
        loginPanel.SetActive(false);
        ShowCharacterSelection(true);
        _startAsHost = true;
    }

    public void OnClientButtonClicked()
    {
        string userName = usernameInput.text;
        if (string.IsNullOrWhiteSpace(userName))
        {
            SetStatus("Please enter a name first");
            return;
        }
        loginPanel.SetActive(false);
        ShowCharacterSelection(true);
        _startAsHost = false;
    }

    private void ShowCharacterSelection(bool show)
    {
        if (characterPanel != null)
        {
            characterPanel.SetActive(show);
            if (show && characterButtons != null)
            {
                for (int i = 0; i < characterButtons.Length; i++)
                {
                    int idx = i;
                    characterButtons[i].onClick.RemoveAllListeners();
                    characterButtons[i].onClick.AddListener(() => OnCharacterSelected(idx));
                }
            }
        }
    }

    public void OnCharacterSelected(int charIndex)
    {
        _pendingCharIndex = charIndex;
        if (_startAsHost)
        {
            SetStatus($"Host started with character {charIndex}");
            // TODO: เรียก StartHost() ที่ ConnectionManager หรือ NetworkManager
        }
        else
        {
            SetStatus($"Client searching for room... (character {charIndex})");
            // TODO: เรียก StartClient() ที่ ConnectionManager หรือ NetworkManager
        }
        ShowCharacterSelection(false);
    }

    public void OnLeaveButtonClick()
    {
        SetStatus("Left room / connection closed");
        loginPanel.SetActive(true);
        ShowCharacterSelection(false);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log("[OnlineSessionManager] " + message);
    }
}

