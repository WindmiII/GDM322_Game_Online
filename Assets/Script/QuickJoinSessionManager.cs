using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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
using TMPro;

public class QuickJoinSessionManager : MonoBehaviour
{
    public static QuickJoinSessionManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private Button startButton;
    public Button StartButton => startButton;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_Text statusText;
    
    [Header("Character Selection")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject characterPanel;
    [SerializeField] private Button[] characterButtons;
    
    [Header("Lobby Settings")]
    [SerializeField] private string lobbyName = "QuickJoinLobby";
    [SerializeField] private int maxPlayers = 4;

    private Lobby _currentLobby;
    private float _heartbeatTimer;
    private bool _isLobbyHost;
    private int _selectedCharIndex = 0; // Default or fetched from ConnectionManager

    private const string KEY_RELAY_CODE = "RelayCode";

    private void Awake()
    {
        Instance = this;
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);
        }
    }

    private void Update()
    {
        HandleLobbyHeartbeat();
    }

    private async Task InitServices()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var initOptions = new InitializationOptions();
#if UNITY_EDITOR
            if (ParrelSync.ClonesManager.IsClone())
            {
                string cloneArg = ParrelSync.ClonesManager.GetArgument();
                string profile = string.IsNullOrEmpty(cloneArg) ? "Clone" : cloneArg;
                initOptions.SetProfile(profile);
            }
#endif
            await UnityServices.InitializeAsync(initOptions);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch (AuthenticationException ex)
            {
                // If it's already signing in, we just need to wait until it finishes
                Debug.LogWarning($"[QuickJoin] SignIn Warning: {ex.Message}");
                // Wait until signed in
                while (!AuthenticationService.Instance.IsSignedIn)
                {
                    await Task.Delay(100);
                }
            }
        }
    }

    public void OnStartButtonClicked()
    {
        if (startButton != null) startButton.interactable = false;
        
        string userName = usernameInput != null ? usernameInput.text.Trim() : "Player";
        if (string.IsNullOrWhiteSpace(userName))
        {
            ShowStatus("Please enter a username.", Color.red);
            if (startButton != null) startButton.interactable = true;
            return;
        }

        if (ConnectionManager.Instance != null)
        {
            ConnectionManager.Instance.LocalUsername_Set(userName);
        }

        // ถ้ามี Panel เลือกตัวละคร ให้เปิดขึ้นมาก่อน
        if (characterPanel != null && characterButtons != null && characterButtons.Length > 0)
        {
            if (loginPanel != null) loginPanel.SetActive(false);
            characterPanel.SetActive(true);

            for (int i = 0; i < characterButtons.Length; i++)
            {
                int index = i;
                characterButtons[i].onClick.RemoveAllListeners();
                characterButtons[i].onClick.AddListener(() => OnCharacterSelected(userName, index));
            }
        }
        else
        {
            // ถ้าไม่ได้ตั้งค่าหน้าเลือกตัวละครไว้ ให้เข้าเกมเลยด้วยตัวละคร 0
            _selectedCharIndex = 0;
            StartQuickJoinProcess(userName, _selectedCharIndex);
        }
    }

    private void OnCharacterSelected(string userName, int charIndex)
    {
        if (characterPanel != null) characterPanel.SetActive(false);
        _selectedCharIndex = charIndex;
        
        StartQuickJoinProcess(userName, charIndex);
    }

    private async void StartQuickJoinProcess(string userName, int charIndex)
    {
        ShowStatus("Initializing Services...", Color.yellow);

        try
        {
            await InitServices();
            ShowStatus("Searching for available games...", Color.yellow);

            bool joined = false;
            const int maxRetries = 5;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    ShowStatus($"Searching for games... ({attempt}/{maxRetries})", Color.yellow);
                    // Try to Quick Join
                    _currentLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
                    _isLobbyHost = false;
                    
                    string relayCode = _currentLobby.Data[KEY_RELAY_CODE].Value;
                    await JoinRelayAsClient(relayCode, userName, _selectedCharIndex);
                    joined = true;
                    break;
                }
                catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.NoOpenLobbies)
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(3000); // Wait 3 seconds before retrying
                    }
                }
            }

            if (!joined)
            {
                // No lobbies found after retries, create one
                ShowStatus("No game found. Creating a new game...", Color.yellow);
                await CreateLobbyAndRelayAsHost(userName, charIndex);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", Color.red);
            if (startButton != null) startButton.interactable = true;
            if (loginPanel != null) loginPanel.SetActive(true);
            Debug.LogError($"[QuickJoin] Error: {ex}");
        }
    }

    private async Task CreateLobbyAndRelayAsHost(string userName, int charIndex)
    {
        // 1. Create Relay allocation
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
        string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        // 2. Create Lobby
        _currentLobby = await LobbyService.Instance.CreateLobbyAsync(
            lobbyName, maxPlayers,
            new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_RELAY_CODE, new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode) }
                }
            });

        _isLobbyHost = true;

        // 3. Configure Transport as Host
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetHostRelayData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData
        );

        // 4. Start Host
        if (ConnectionManager.Instance != null)
        {
            ConnectionManager.Instance.SetHostCharIndex(charIndex);
        }
        SetConnectionData(userName, charIndex);
        
        // Safety: Shutdown if already running
        if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
            await Task.Delay(100); // Small delay to let it shutdown
        }

        NetworkManager.Singleton.StartHost();

        ShowStatus("Game Started as Host!", Color.green);
        HideLoginUI();
    }

    private async Task JoinRelayAsClient(string relayCode, string userName, int charIndex)
    {
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

        SetConnectionData(userName, charIndex);

        // Safety: Shutdown if already running
        if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
            await Task.Delay(100); // Small delay to let it shutdown
        }

        NetworkManager.Singleton.StartClient();

        ShowStatus("Joined Game as Client!", Color.green);
        HideLoginUI();
    }

    private void SetConnectionData(string username, int charIndex)
    {
        string payload = $"{username}|{charIndex}";
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(payload);
    }

    private async void HandleLobbyHeartbeat()
    {
        if (_currentLobby == null || !_isLobbyHost) return;

        _heartbeatTimer += Time.deltaTime;
        if (_heartbeatTimer >= 15f) // Ping every 15 seconds
        {
            _heartbeatTimer = 0f;
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QuickJoin] Heartbeat failed: {e.Message}");
            }
        }
    }

    public async Task LeaveAndCleanup()
    {
        try
        {
            if (_currentLobby != null)
            {
                if (_isLobbyHost)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, AuthenticationService.Instance.PlayerId);
                }
                _currentLobby = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[QuickJoin] Leave lobby error: {e.Message}");
        }
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Reset UI state
        if (startButton != null) startButton.interactable = true;
    }

    private void ShowStatus(string msg, Color color)
    {
        if (statusText != null)
        {
            statusText.gameObject.SetActive(true);
            statusText.text = msg;
            statusText.color = color;
        }
        Debug.Log($"[QuickJoin] {msg}");
    }

    private void HideLoginUI()
    {
        if (loginPanel != null) loginPanel.SetActive(false);
        if (characterPanel != null) characterPanel.SetActive(false);
        
        // Ensure status text is hidden after success
        if (statusText != null)
        {
            statusText.text = "Connected!";
            statusText.color = Color.green;
            // Optionally hide it after a delay
            Invoke(nameof(HideStatusText), 3f);
        }
    }

    private void HideStatusText()
    {
        if (statusText != null) statusText.gameObject.SetActive(false);
    }
}
