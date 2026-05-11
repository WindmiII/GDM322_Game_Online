using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

/// <summary>
/// Manages connections via Unity Lobby + Relay Service.
/// Bind to the "Lobby Host" and "Lobby Client" buttons on the UI.
/// </summary>
public class LobbyConnectionManager : MonoBehaviour
{
    public static LobbyConnectionManager Instance { get; private set; }

    // ─────────────────────────────────────────────
    //  Inspector Fields
    // ─────────────────────────────────────────────
    [Header("UI References")]
    [SerializeField] private TMP_InputField usernameInput;       // Same UserName Input field as ConnectionManager

    [Tooltip("Input field where the Client types the Lobby Join Code. Assign the existing 'join code' InputField or create a new one.")]
    [SerializeField] private TMP_InputField lobbyJoinCodeInput;  // <- Assign join code input here

    [Tooltip("(Optional) TMP_Text to display the Lobby Code for the Host. Create a new Text in Canvas and assign here.")]
    [SerializeField] private TMP_Text lobbyCodeDisplayText;

    [Tooltip("(Optional) TMP_Text for status messages. If not assigned, falls back to ConnectionManager's ErrorText.")]
    [SerializeField] private TMP_Text statusText;

    [SerializeField] private Button     lobbyHostButton;
    [SerializeField] private Button     lobbyClientButton;
    [SerializeField] private GameObject loginPanel;

    [Header("Lobby Settings")]
    [SerializeField] private string lobbyName  = "MyGameLobby";
    [SerializeField] private int    maxPlayers = 4;

    // ─────────────────────────────────────────────
    //  Private State
    // ─────────────────────────────────────────────
    private Lobby _hostedLobby;         // Lobby created by the Host
    private Lobby _joinedLobby;         // Lobby joined by the Client
    private float _heartbeatTimer;      // Prevents Lobby timeout (every 25 seconds)
    private bool  _isLobbyHost;

    private const string KEY_RELAY_CODE = "RelayCode"; // Key used to store the Relay join code inside Lobby data

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        HandleLobbyHeartbeat();
        PollLobbyForRelayCode();
    }

    // ─────────────────────────────────────────────
    //  Heartbeat — must ping every 25 s or Lobby gets deleted
    // ─────────────────────────────────────────────
    private async void HandleLobbyHeartbeat()
    {
        if (_hostedLobby == null) return;

        _heartbeatTimer += Time.deltaTime;
        if (_heartbeatTimer >= 25f)
        {
            _heartbeatTimer = 0f;
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(_hostedLobby.Id);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyConnectionManager] Heartbeat failed: {e.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Poll — Client waits for Relay code inside Lobby data
    // ─────────────────────────────────────────────
    private float _pollTimer;
    private bool  _relayCodeReceived;

    private async void PollLobbyForRelayCode()
    {
        if (_joinedLobby == null || _isLobbyHost || _relayCodeReceived) return;

        _pollTimer += Time.deltaTime;
        if (_pollTimer < 1.5f) return; // poll every 1.5 s
        _pollTimer = 0f;

        try
        {
            _joinedLobby = await LobbyService.Instance.GetLobbyAsync(_joinedLobby.Id);

            if (_joinedLobby.Data != null &&
                _joinedLobby.Data.TryGetValue(KEY_RELAY_CODE, out DataObject dataObj) &&
                !string.IsNullOrWhiteSpace(dataObj.Value))
            {
                string relayCode = dataObj.Value;
                _relayCodeReceived = true;
                Debug.Log($"[LobbyConnectionManager] Received Relay code from Lobby: {relayCode}");
                await JoinRelayAsClient(relayCode);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyConnectionManager] Poll error: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────
    //  PUBLIC: "Lobby Host" button
    // ─────────────────────────────────────────────
    public async void OnLobbyHostButtonClicked()
    {
        string userName = usernameInput != null ? usernameInput.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(userName))
        {
            ShowStatus("Please enter a username first.", Color.red);
            return;
        }

        SetButtonsInteractable(false);
        ShowStatus("Creating Lobby...", Color.yellow);

        try
        {
            // 1) Initialize Unity Services
            await InitServices();

            // 2) Create Relay Allocation (Host side)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string relayJoinCode  = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[LobbyConnectionManager] Relay code: {relayJoinCode}");

            // 3) Create Lobby and embed the Relay code in its data
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    {
                        KEY_RELAY_CODE,
                        // Public ทำให้ Quick Join หา lobby เจอได้ด้วย
                        new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode)
                    }
                }
            };

            _hostedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            _joinedLobby = _hostedLobby;
            _isLobbyHost = true;

            Debug.Log($"[LobbyConnectionManager] Lobby created! ID: {_hostedLobby.Id}  LobbyCode: {_hostedLobby.LobbyCode}");

            if (lobbyCodeDisplayText != null)
                lobbyCodeDisplayText.text = $"Lobby Code: {_hostedLobby.LobbyCode}";

            // 4) Configure Transport (Host side)
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            // 5) Sync username with ConnectionManager
            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.LocalUsername_Set(userName);

            // 6) Set connection payload (username|charIndex)
            SetConnectionData(userName, 0);

            // 7) Start Host
            NetworkManager.Singleton.StartHost();

            ShowStatus($"Lobby ready! Code: {_hostedLobby.LobbyCode}", Color.green);

            if (loginPanel != null) loginPanel.SetActive(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Lobby Host Error: {ex.Message}", Color.red);
            SetButtonsInteractable(true);
            Debug.LogError($"[LobbyConnectionManager] Host error: {ex}");
        }
    }

    // ─────────────────────────────────────────────
    //  PUBLIC: "Lobby Client" button
    // ─────────────────────────────────────────────
    public async void OnLobbyClientButtonClicked()
    {
        string userName = usernameInput != null ? usernameInput.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(userName))
        {
            ShowStatus("Please enter a username first.", Color.red);
            return;
        }

        // Read Lobby Code from the input field (if empty → Quick Join)
        string lobbyCode = lobbyJoinCodeInput != null ? lobbyJoinCodeInput.text.Trim() : "";

        SetButtonsInteractable(false);
        ShowStatus("Searching for Lobby...", Color.yellow);

        try
        {
            await InitServices();

            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.LocalUsername_Set(userName);

            if (!string.IsNullOrWhiteSpace(lobbyCode))
            {
                // Join by Lobby Code
                Debug.Log($"[LobbyConnectionManager] Joining with Lobby code: {lobbyCode}");
                _joinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
            }
            else
            {
                // Quick Join พร้อม Retry กรณี lobby ยัง index ไม่ทัน
                const int maxRetries = 5;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        Debug.Log($"[LobbyConnectionManager] Quick Join attempt {attempt}/{maxRetries}...");
                        _joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
                        break; // สำเร็จ ออกจาก loop
                    }
                    catch (LobbyServiceException e) when
                        (e.Reason == LobbyExceptionReason.NoOpenLobbies && attempt < maxRetries)
                    {
                        ShowStatus($"Searching... ({attempt}/{maxRetries})", Color.yellow);
                        await Task.Delay(2000); // รอ 2 วินาทีแล้วลองใหม่
                    }
                    // attempt สุดท้าย → ปล่อย exception ขึ้นไปให้ catch หลักจัดการ
                }
            }

            _isLobbyHost       = false;
            _relayCodeReceived = false;
            ShowStatus("Joined Lobby! Waiting for Relay code...", Color.yellow);
            Debug.Log($"[LobbyConnectionManager] Joined Lobby: {_joinedLobby.Id}");

            // Relay code will be received via PollLobbyForRelayCode()
        }
        catch (Exception ex)
        {
            ShowStatus($"Lobby Client Error: {ex.Message}", Color.red);
            SetButtonsInteractable(true);
            Debug.LogError($"[LobbyConnectionManager] Client error: {ex}");
        }
    }

    // ─────────────────────────────────────────────
    //  Connect to Relay as Client after receiving the code
    // ─────────────────────────────────────────────
    private async Task JoinRelayAsClient(string relayCode)
    {
        try
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

            // Set connection payload before StartClient
            string userName = usernameInput != null ? usernameInput.text.Trim() : "Player";
            SetConnectionData(userName, 0);

            NetworkManager.Singleton.StartClient();

            ShowStatus("Connected successfully!", Color.green);
            if (loginPanel != null) loginPanel.SetActive(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Relay Join Error: {ex.Message}", Color.red);
            SetButtonsInteractable(true);
            Debug.LogError($"[LobbyConnectionManager] Relay join error: {ex}");
        }
    }

    // ─────────────────────────────────────────────
    //  Helper Methods
    // ─────────────────────────────────────────────
    private async Task InitServices()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var initOptions = new InitializationOptions();

#if UNITY_EDITOR
            // ParrelSync: ให้ Clone ใช้ profile แยก เพื่อได้ Player ID ต่างกัน
            if (ParrelSync.ClonesManager.IsClone())
            {
                string cloneArg = ParrelSync.ClonesManager.GetArgument();
                string profile  = string.IsNullOrEmpty(cloneArg) ? "Clone" : cloneArg;
                initOptions.SetProfile(profile);
                Debug.Log($"[LobbyConnectionManager] ParrelSync Clone detected, using profile: {profile}");
            }
#endif
            await UnityServices.InitializeAsync(initOptions);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void SetConnectionData(string username, int charIndex)
    {
        string payload = $"{username}|{charIndex}";
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(payload);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (lobbyHostButton   != null) lobbyHostButton.interactable   = interactable;
        if (lobbyClientButton != null) lobbyClientButton.interactable = interactable;
    }

    private void ShowStatus(string msg, Color color)
    {
        Debug.Log($"[LobbyStatus] {msg}");

        // Use own statusText first
        if (statusText != null)
        {
            statusText.gameObject.SetActive(true);
            statusText.text  = msg;
            statusText.color = color;
            return;
        }

        // Fallback: use ConnectionManager's errorText
        if (ConnectionManager.Instance != null && ConnectionManager.Instance.errorText != null)
        {
            ConnectionManager.Instance.errorText.gameObject.SetActive(true);
            ConnectionManager.Instance.errorText.text  = msg;
            ConnectionManager.Instance.errorText.color = color;
        }
    }

    // ─────────────────────────────────────────────
    //  Cleanup on Leave / Quit
    // ─────────────────────────────────────────────
    public async void LeaveLobby()
    {
        try
        {
            if (_hostedLobby != null)
            {
                await LobbyService.Instance.DeleteLobbyAsync(_hostedLobby.Id);
                _hostedLobby = null;
            }
            else if (_joinedLobby != null)
            {
                await LobbyService.Instance.RemovePlayerAsync(
                    _joinedLobby.Id,
                    AuthenticationService.Instance.PlayerId
                );
                _joinedLobby = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyConnectionManager] LeaveLobby error: {e.Message}");
        }
    }

    private void OnApplicationQuit()
    {
        _ = LeaveLobbyOnQuit();
    }

    private async Task LeaveLobbyOnQuit()
    {
        await Task.Run(() => LeaveLobby());
    }
}
