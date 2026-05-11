using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;
using System.Text;

public class LobbyBrowserUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject browserPanel;
    [SerializeField] private Transform lobbyListContent;
    [SerializeField] private GameObject lobbyEntryPrefab;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text statusText;

    private void Awake()
    {
        if (refreshButton != null) refreshButton.onClick.AddListener(RefreshLobbyList);
        if (closeButton != null) closeButton.onClick.AddListener(CloseBrowser);
    }

    public void OpenBrowser()
    {
        if (browserPanel != null) browserPanel.SetActive(true);
        RefreshLobbyList();
    }

    public void CloseBrowser()
    {
        if (browserPanel != null) browserPanel.SetActive(false);
    }

    public async void RefreshLobbyList()
    {
        ShowStatus("Refreshing lobbies...", Color.yellow);
        
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(false, QueryOrder.FieldOptions.Created)
                }
            };

            QueryResponse lobbies = await LobbyService.Instance.QueryLobbiesAsync(options);

            // Clear old UI
            foreach (Transform child in lobbyListContent)
            {
                Destroy(child.gameObject);
            }

            if (lobbies.Results.Count == 0)
            {
                ShowStatus("No lobbies found.", Color.white);
                return;
            }

            // Populate UI
            foreach (Lobby lobby in lobbies.Results)
            {
                GameObject entryGo = Instantiate(lobbyEntryPrefab, lobbyListContent);
                LobbyEntryUI entryUI = entryGo.GetComponent<LobbyEntryUI>();
                if (entryUI != null)
                {
                    entryUI.Initialize(lobby, this);
                }
            }
            
            ShowStatus($"Found {lobbies.Results.Count} lobbies.", Color.green);
        }
        catch (Exception e)
        {
            ShowStatus($"Error refreshing lobbies: {e.Message}", Color.red);
            Debug.LogError($"[LobbyBrowser] Query failed: {e}");
        }
    }

    public async void JoinLobby(Lobby lobby)
    {
        ShowStatus($"Joining {lobby.Name}...", Color.yellow);
        try
        {
            Lobby joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id);
            
            // Wait for Relay Code if it's not immediately available
            string relayCode = "";
            if (joinedLobby.Data != null && joinedLobby.Data.ContainsKey("RelayCode"))
            {
                relayCode = joinedLobby.Data["RelayCode"].Value;
            }

            if (string.IsNullOrEmpty(relayCode))
            {
                ShowStatus("Waiting for Relay Code...", Color.yellow);
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(1000);
                    joinedLobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
                    if (joinedLobby.Data != null && joinedLobby.Data.ContainsKey("RelayCode"))
                    {
                        relayCode = joinedLobby.Data["RelayCode"].Value;
                        if (!string.IsNullOrEmpty(relayCode)) break;
                    }
                }
            }

            if (string.IsNullOrEmpty(relayCode))
            {
                throw new Exception("Relay code not found in Lobby data.");
            }

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            // Get username and character id from ConnectionManager if possible
            string userName = "Player";
            int charIndex = 0;
            if (ConnectionManager.Instance != null)
            {
                userName = string.IsNullOrEmpty(ConnectionManager.Instance.LocalUsername) ? "Player" : ConnectionManager.Instance.LocalUsername;
                charIndex = ConnectionManager.Instance.GetSelectedCharIndex();
            }

            string payload = $"{userName}|{charIndex}";
            NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(payload);
            
            NetworkManager.Singleton.StartClient();
            
            ShowStatus("Joined successfully!", Color.green);
            CloseBrowser();
            
            // Also might want to hide the login panel if handled here, but ConnectionManager should handle the server started callbacks
        }
        catch (Exception e)
        {
            ShowStatus($"Error joining lobby: {e.Message}", Color.red);
            Debug.LogError($"[LobbyBrowser] Join failed: {e}");
        }
    }

    private void ShowStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
    }
}
