using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyEntryUI : MonoBehaviour
{
    [SerializeField] private TMP_Text lobbyNameText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private Button joinButton;

    private Lobby _lobby;
    private LobbyBrowserUI _browser;

    public void Initialize(Lobby lobby, LobbyBrowserUI browser)
    {
        _lobby = lobby;
        _browser = browser;

        if (lobbyNameText != null) lobbyNameText.text = lobby.Name;
        if (playerCountText != null) playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(OnJoinClicked);
        }
    }

    private void OnJoinClicked()
    {
        if (_browser != null && _lobby != null)
        {
            _browser.JoinLobby(_lobby);
        }
    }
}
