using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using QFSW.QC;
using TMPro;
using UnityEngine.UI;

// Connection data payload format: "username|charIndex"
// This script handles both:
//   A) Direct Relay  — Relay Host / Relay Client buttons (enter code manually)
//   B) Lobby + Relay — Lobby Host / Lobby Client buttons (auto Quick Join, no code needed)

public class ConnectionManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Singleton + Public State
    // ─────────────────────────────────────────────
    public static ConnectionManager Instance { get; private set; }
    public string LocalUsername { get; private set; } = "";
    public void LocalUsername_Set(string name) => LocalUsername = name;

    // ─────────────────────────────────────────────
    //  Inspector — Spawn Points
    // ─────────────────────────────────────────────
    [Header("Spawn Points")]
    [SerializeField] private Transform[] survivorSpawnPoints;
    [SerializeField] private Transform[] monsterSpawnPoints;
    [SerializeField] private bool useRandomSpawn = false;
    private int nextSurvivorSpawnIndex = 0;
    private int nextMonsterSpawnIndex  = 0;

    [SerializeField] private List<uint> alternatePlayerPrefabHashes = new List<uint>();

    // ─────────────────────────────────────────────
    //  Inspector — Shared UI
    // ─────────────────────────────────────────────
    [Header("UI - Shared")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private GameObject     loginPanel;
    [SerializeField] private GameObject     leaveButton;
    [SerializeField] public  TMP_Text       errorText;
    [SerializeField] public  Button         clientButton;   // Relay Client button

    // ─────────────────────────────────────────────
    //  Inspector — Direct Relay UI
    // ─────────────────────────────────────────────
    [Header("UI - Direct Relay")]
    [SerializeField] private TMP_Text       relayHostJoinCodeText;
    [SerializeField] private TMP_InputField relayClientJoinCodeInput;
    [SerializeField] private GameObject     characterPanel;
    [SerializeField] private Button[]       characterButtons;

    // ─────────────────────────────────────────────
    //  Inspector — Lobby UI
    // ─────────────────────────────────────────────
    [Header("UI - Lobby")]
    [Tooltip("ช่อง input สำหรับ Client กรอก Lobby Code (ถ้าปล่อยว่าง = Quick Join อัตโนมัติ)")]
    [SerializeField] private TMP_InputField lobbyJoinCodeInput;
    [Tooltip("Text แสดง Lobby Code ให้ Host เห็น")]
    [SerializeField] private TMP_Text       lobbyCodeDisplayText;
    [Tooltip("Text แสดงสถานะ Lobby")]
    [SerializeField] private TMP_Text       lobbyStatusText;
    [SerializeField] private Button         lobbyHostButton;
    [SerializeField] private Button         lobbyClientButton;

    // ─────────────────────────────────────────────
    //  Inspector — Lobby Settings
    // ─────────────────────────────────────────────
    [Header("Lobby Settings")]
    [SerializeField] private string lobbyName  = "MyGameLobby";
    [SerializeField] private int    maxPlayers = 4;

    // ─────────────────────────────────────────────
    //  Private State — Direct Relay
    // ─────────────────────────────────────────────
    private string _relayJoinCode   = "";
    private bool   _startAsHost     = false;
    private int    _pendingCharIndex = 0;
    private int    _hostCharIndex    = 0;
    public void SetHostCharIndex(int index) => _hostCharIndex = index;
    private bool   _isLeaving        = false;

    private readonly Dictionary<ulong, int>    _clientIdToCharIndex = new Dictionary<ulong, int>();
    private readonly HashSet<string>           _connectedNames      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, string> _clientIdToName      = new Dictionary<ulong, string>();

    // ─────────────────────────────────────────────
    //  Private State — Lobby
    // ─────────────────────────────────────────────
    private Lobby _hostedLobby;
    private Lobby _joinedLobby;
    private float _heartbeatTimer;
    private float _pollTimer;
    private bool  _isLobbyHost;
    private bool  _relayCodeReceived;

    private const string KEY_RELAY_CODE = "RelayCode";

    // ═════════════════════════════════════════════
    //  Unity Lifecycle
    // ═════════════════════════════════════════════
    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    private void Update()
    {
        // Keep cursor visible on login / character-select panels
        if ((loginPanel    != null && loginPanel.activeSelf) ||
            (characterPanel != null && characterPanel.activeSelf))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        // Lobby heartbeat + polling run every frame (cheap no-ops when not in a lobby)
        HandleLobbyHeartbeat();
        PollLobbyForRelayCode();
    }

    private void OnApplicationQuit()
    {
        _ = LeaveLobbyOnQuit();
    }

    // ═════════════════════════════════════════════
    //  Shared Services Init  (with ParrelSync support)
    // ═════════════════════════════════════════════
    private async Task InitServices()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var initOptions = new InitializationOptions();

#if UNITY_EDITOR
            // ParrelSync: ให้ Clone ใช้ profile แยก → Player ID ต่างกัน → Quick Join ไม่ติดปัญหา same-account
            if (ParrelSync.ClonesManager.IsClone())
            {
                string cloneArg = ParrelSync.ClonesManager.GetArgument();
                string profile  = string.IsNullOrEmpty(cloneArg) ? "Clone" : cloneArg;
                initOptions.SetProfile(profile);
                Debug.Log($"[ConnectionManager] ParrelSync Clone — using profile: {profile}");
            }
#endif
            await UnityServices.InitializeAsync(initOptions);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    // ═════════════════════════════════════════════
    //  A) Direct Relay Buttons
    // ═════════════════════════════════════════════
    public async void OnHostButtonClicked()
    {
        string userName = usernameInput.text;
        if (string.IsNullOrWhiteSpace(userName)) { SetError("Please enter a name first", Color.red); return; }
        LocalUsername = userName;
        _isLeaving    = false;

        try
        {
            await InitServices();
            await ConfigureRelayHost();
        }
        catch (Exception ex)
        {
            SetError($"Relay host error: {ex.Message}", Color.red);
            return;
        }

        loginPanel.SetActive(false);
        ShowCharacterSelection(true);
        _startAsHost = true;
    }

    public async void OnClientButtonClicked()
    {
        if (clientButton != null) clientButton.interactable = false;

        string userName = usernameInput.text;
        if (string.IsNullOrWhiteSpace(userName))
        {
            SetError("Please enter a name first", Color.red);
            if (clientButton != null) clientButton.interactable = true;
            return;
        }
        LocalUsername = userName;
        _isLeaving    = false;

        try
        {
            await InitServices();
            await ConfigureRelayClient();
        }
        catch (Exception ex)
        {
            SetError($"Relay client error: {ex.Message}", Color.red);
            if (clientButton != null) clientButton.interactable = true;
            return;
        }

        loginPanel.SetActive(false);
        ShowCharacterSelection(true);
        _startAsHost = false;
    }

    private async Task ConfigureRelayHost()
    {
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(4);
        _relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        if (relayHostJoinCodeText != null)
            relayHostJoinCodeText.text = $"Code : {_relayJoinCode}";
        Debug.Log($"[ConnectionManager] Relay host code: {_relayJoinCode}");

        UnityTransport transport = GetUnityTransport();
        if (transport == null) throw new InvalidOperationException("UnityTransport not found on NetworkManager.");

        transport.SetHostRelayData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData
        );
    }

    private async Task ConfigureRelayClient()
    {
        if (relayClientJoinCodeInput == null)
            throw new InvalidOperationException("Relay client join code input is not assigned.");

        string joinCode = relayClientJoinCodeInput.text;
        if (string.IsNullOrWhiteSpace(joinCode))
            throw new InvalidOperationException("Relay join code is required.");

        Debug.Log($"[ConnectionManager] Relay client using code: {joinCode}");
        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        UnityTransport transport = GetUnityTransport();
        if (transport == null) throw new InvalidOperationException("UnityTransport not found on NetworkManager.");

        transport.SetClientRelayData(
            joinAllocation.RelayServer.IpV4,
            (ushort)joinAllocation.RelayServer.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.Key,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData
        );
    }

    // ─── Character Selection (Direct Relay flow) ───
    private void ShowCharacterSelection(bool show)
    {
        if (characterPanel == null) return;
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

    public void OnCharacterSelected(int charIndex)
    {
        _pendingCharIndex = charIndex;
        SetConnectionData(LocalUsername, charIndex);

        if (_startAsHost)
        {
            _hostCharIndex = charIndex;
            NetworkManager.Singleton.StartHost();
        }
        else
        {
            SetError("Searching for room...", Color.yellow);
            var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport != null) transport.MaxConnectAttempts = 2;
            NetworkManager.Singleton.StartClient();
        }

        ShowCharacterSelection(false);
    }

    public int GetSelectedCharIndex() => _pendingCharIndex;

    // ═════════════════════════════════════════════
    //  B) Lobby + Relay Buttons
    // ═════════════════════════════════════════════
    public async void OnLobbyHostButtonClicked()
    {
        string userName = usernameInput != null ? usernameInput.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(userName)) { ShowStatus("Please enter a username first.", Color.red); return; }

        SetLobbyButtonsInteractable(false);
        ShowStatus("Creating Lobby...", Color.yellow);

        try
        {
            await InitServices();

            // 1. Create Relay allocation
            Allocation allocation    = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string     relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[ConnectionManager] Lobby Relay code: {relayJoinCode}");

            // 2. Create Lobby — embed Relay code as Public so Quick Join can find it
            _hostedLobby = await LobbyService.Instance.CreateLobbyAsync(
                lobbyName, maxPlayers,
                new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        { KEY_RELAY_CODE, new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode) }
                    }
                });

            _joinedLobby = _hostedLobby;
            _isLobbyHost = true;
            Debug.Log($"[ConnectionManager] Lobby created! Code: {_hostedLobby.LobbyCode}");

            if (lobbyCodeDisplayText != null)
                lobbyCodeDisplayText.text = $"Lobby Code: {_hostedLobby.LobbyCode}";

            // 3. Configure Transport as Host
            UnityTransport transport = GetUnityTransport();
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            // 4. Start Host
            LocalUsername_Set(userName);
            SetConnectionData(userName, 0);
            NetworkManager.Singleton.StartHost();

            ShowStatus($"Lobby ready! Code: {_hostedLobby.LobbyCode}", Color.green);
            if (loginPanel != null) loginPanel.SetActive(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Lobby Host Error: {ex.Message}", Color.red);
            SetLobbyButtonsInteractable(true);
            Debug.LogError($"[ConnectionManager] Lobby Host error: {ex}");
        }
    }

    public async void OnLobbyClientButtonClicked()
    {
        string userName = usernameInput != null ? usernameInput.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(userName)) { ShowStatus("Please enter a username first.", Color.red); return; }

        // ถ้ากรอก Lobby Code → join ด้วย code, ถ้าว่าง → Quick Join อัตโนมัติ
        string lobbyCode = lobbyJoinCodeInput != null ? lobbyJoinCodeInput.text.Trim() : "";

        SetLobbyButtonsInteractable(false);
        ShowStatus("Searching for Lobby...", Color.yellow);

        try
        {
            await InitServices();
            LocalUsername_Set(userName);

            if (!string.IsNullOrWhiteSpace(lobbyCode))
            {
                // Join by Lobby Code
                Debug.Log($"[ConnectionManager] Joining with Lobby code: {lobbyCode}");
                _joinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
            }
            else
            {
                // Quick Join with retry (รอให้ Host สร้าง Lobby เสร็จก่อน)
                const int maxRetries = 5;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        Debug.Log($"[ConnectionManager] Quick Join attempt {attempt}/{maxRetries}...");
                        _joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
                        break; // สำเร็จ
                    }
                    catch (LobbyServiceException e) when
                        (e.Reason == LobbyExceptionReason.NoOpenLobbies && attempt < maxRetries)
                    {
                        ShowStatus($"Searching... ({attempt}/{maxRetries})", Color.yellow);
                        await Task.Delay(2000); // รอ 2 วิ แล้วลองใหม่
                    }
                    // attempt สุดท้าย → ปล่อย exception ขึ้นไปให้ catch หลักจัดการ
                }
            }

            _isLobbyHost       = false;
            _relayCodeReceived = false;
            _pollTimer         = 0f;
            ShowStatus("Joined Lobby! Waiting for Relay code...", Color.yellow);
            Debug.Log($"[ConnectionManager] Joined Lobby: {_joinedLobby.Id}");
        }
        catch (Exception ex)
        {
            ShowStatus($"Lobby Client Error: {ex.Message}", Color.red);
            SetLobbyButtonsInteractable(true);
            Debug.LogError($"[ConnectionManager] Lobby Client error: {ex}");
        }
    }

    // ─── Lobby Heartbeat (ping ทุก 25 วิ กัน timeout) ───
    private async void HandleLobbyHeartbeat()
    {
        if (_hostedLobby == null) return;

        _heartbeatTimer += Time.deltaTime;
        if (_heartbeatTimer >= 25f)
        {
            _heartbeatTimer = 0f;
            try   { await LobbyService.Instance.SendHeartbeatPingAsync(_hostedLobby.Id); }
            catch (Exception e) { Debug.LogWarning($"[ConnectionManager] Heartbeat failed: {e.Message}"); }
        }
    }

    // ─── Poll Lobby Data — Client รอรับ Relay Code จาก Lobby ───
    private async void PollLobbyForRelayCode()
    {
        if (_joinedLobby == null || _isLobbyHost || _relayCodeReceived) return;

        _pollTimer += Time.deltaTime;
        if (_pollTimer < 1.5f) return;
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
                Debug.Log($"[ConnectionManager] Received Relay code from Lobby: {relayCode}");
                await JoinRelayAsClient(relayCode);
            }
        }
        catch (Exception e) { Debug.LogWarning($"[ConnectionManager] Poll error: {e.Message}"); }
    }

    private async Task JoinRelayAsClient(string relayCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
            UnityTransport transport = GetUnityTransport();
            transport.SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            string userName = usernameInput != null ? usernameInput.text.Trim() : "Player";
            SetConnectionData(userName, 0);
            NetworkManager.Singleton.StartClient();

            ShowStatus("Connected successfully!", Color.green);
            if (loginPanel != null) loginPanel.SetActive(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Relay Join Error: {ex.Message}", Color.red);
            SetLobbyButtonsInteractable(true);
            Debug.LogError($"[ConnectionManager] Relay join error: {ex}");
        }
    }

    // ─── Lobby Cleanup ───
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
                    _joinedLobby.Id, AuthenticationService.Instance.PlayerId);
                _joinedLobby = null;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[ConnectionManager] LeaveLobby error: {e.Message}"); }
    }

    private async Task LeaveLobbyOnQuit()
    {
        await Task.Run(() => LeaveLobby());
    }

    // ═════════════════════════════════════════════
    //  Connection Approval + Spawn
    // ═════════════════════════════════════════════
    private bool isApproveConnection = false;
    [Command("set-approve")]
    public bool SetIsApproveConnection()
    {
        isApproveConnection = !isApproveConnection;
        return isApproveConnection;
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
                               NetworkManager.ConnectionApprovalResponse response)
    {
        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved         = true;
            response.CreatePlayerObject = true;
            response.Pending          = false;
            response.PlayerPrefabHash = GetPlayerPrefabHashFromCharacterId(_hostCharIndex);

            GetSpawnPositionAndRotation(_hostCharIndex, out Vector3 hostPos, out Quaternion hostRot);
            response.Position = hostPos;
            response.Rotation = hostRot;

            TrackNameOnServer(request.ClientNetworkId, LocalUsername, _hostCharIndex);
            return;
        }

        if (!TryParseConnectionPayload(request.Payload, out string incomingName, out int incomingChar))
        {
            response.Approved = false;
            response.Reason   = "Invalid payload";
            response.Pending  = false;
            return;
        }

        if (_connectedNames.Contains(incomingName))
        {
            response.Approved = false;
            response.Reason   = "Name already in use";
            response.Pending  = false;
            return;
        }

        response.Approved         = true;
        response.CreatePlayerObject = true;
        response.PlayerPrefabHash = GetPlayerPrefabHashFromCharacterId(incomingChar);

        GetSpawnPositionAndRotation(incomingChar, out Vector3 pos, out Quaternion rot);
        response.Position = pos;
        response.Rotation = rot;

        TrackNameOnServer(request.ClientNetworkId, incomingName, incomingChar);
        response.Pending = false;
    }

    private uint? GetPlayerPrefabHashFromCharacterId(int characterId)
    {
        if (alternatePlayerPrefabHashes == null || alternatePlayerPrefabHashes.Count == 0)
            return null;
        if (characterId < 0 || characterId >= alternatePlayerPrefabHashes.Count)
            return alternatePlayerPrefabHashes[0];
        return alternatePlayerPrefabHashes[characterId];
    }

    // ═════════════════════════════════════════════
    //  Network Event Handlers
    // ═════════════════════════════════════════════
    private void OnEnable()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted          += HandleServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback  += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.ConnectionApprovalCallback  = null;
        NetworkManager.Singleton.OnServerStarted            -= HandleServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback   -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback  -= HandleClientDisconnected;
    }

    private void HandleServerStarted()
    {
        if (NetworkManager.Singleton.IsHost)
            SetUIConnected(true);
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
            SetUIConnected(true);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId || clientId == 0)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            if (clientButton  != null) clientButton.interactable = true;
            if (characterPanel != null) characterPanel.SetActive(false);

            if (LobbyManager.Instance != null && LobbyManager.Instance.lobbyPanel != null)
                LobbyManager.Instance.lobbyPanel.SetActive(false);

            if (lobbyHostButton != null) lobbyHostButton.interactable = true;
            if (lobbyClientButton != null) lobbyClientButton.interactable = true;
            if (QuickJoinSessionManager.Instance != null && QuickJoinSessionManager.Instance.StartButton != null)
                QuickJoinSessionManager.Instance.StartButton.interactable = true;

            if (loginPanel != null)
            {
                loginPanel.SetActive(true);
                Debug.Log("[ConnectionManager] Forcing LoginPanel to Active");
            }

            string reason = NetworkManager.Singleton.DisconnectReason;
            if (!string.IsNullOrEmpty(reason))
                SetError(reason, Color.red);
            else if (!_isLeaving)
                SetError("Connection failed! Please ensure a Host is running.", Color.red);

            NetworkManager.Singleton.Shutdown();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            UntrackNameOnServer(clientId);
    }

    // ═════════════════════════════════════════════
    //  Name Tracking (Server-side)
    // ═════════════════════════════════════════════
    private void TrackNameOnServer(ulong clientId, string name, int charIndex)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_clientIdToName.TryGetValue(clientId, out string existing))
        {
            if (!string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
            {
                _connectedNames.Remove(existing);
                _clientIdToName[clientId] = name;
                _connectedNames.Add(name);
            }
            _clientIdToCharIndex[clientId] = charIndex;
        }
        else
        {
            _clientIdToName.Add(clientId, name);
            _connectedNames.Add(name);
            _clientIdToCharIndex.Add(clientId, charIndex);
        }
        PrintConnectedClients();
    }

    private void UntrackNameOnServer(ulong clientId)
    {
        if (_clientIdToName.TryGetValue(clientId, out string name))
        {
            _clientIdToName.Remove(clientId);
            _connectedNames.Remove(name);
        }
        if (_clientIdToCharIndex.ContainsKey(clientId))
            _clientIdToCharIndex.Remove(clientId);

        PrintConnectedClients();
    }

    private void PrintConnectedClients()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        Debug.Log("========== SERVER CONNECTED CLIENTS ==========");
        if (_clientIdToName.Count == 0) { Debug.Log("No connected clients."); return; }
        foreach (var kvp in _clientIdToName)
        {
            int charIdx = _clientIdToCharIndex.TryGetValue(kvp.Key, out int idx) ? idx : -1;
            Debug.Log($"ClientID: {kvp.Key} | Username: {kvp.Value} | CharIndex: {charIdx}");
        }
        Debug.Log("===============================================");
    }

    // ═════════════════════════════════════════════
    //  Spawn Helpers
    // ═════════════════════════════════════════════
    private void GetSpawnPositionAndRotation(int charIndex, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        Transform[] pts = charIndex == 0 ? survivorSpawnPoints : monsterSpawnPoints;
        if (pts == null || pts.Length == 0) return;

        if (useRandomSpawn)
        {
            int idx = UnityEngine.Random.Range(0, pts.Length);
            pos = pts[idx].position;
            rot = pts[idx].rotation;
        }
        else
        {
            if (charIndex == 0)
            {
                pos = pts[nextSurvivorSpawnIndex].position;
                rot = pts[nextSurvivorSpawnIndex].rotation;
                nextSurvivorSpawnIndex = (nextSurvivorSpawnIndex + 1) % pts.Length;
            }
            else
            {
                pos = pts[nextMonsterSpawnIndex].position;
                rot = pts[nextMonsterSpawnIndex].rotation;
                nextMonsterSpawnIndex = (nextMonsterSpawnIndex + 1) % pts.Length;
            }
        }
    }

    // ═════════════════════════════════════════════
    //  Payload Helpers
    // ═════════════════════════════════════════════
    private void SetConnectionData(string username, int charIndex)
    {
        string payload = $"{username}|{charIndex}";
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(payload);
    }

    private string DecodePayloadToString(ArraySegment<byte> payload)
    {
        if (payload.Array == null || payload.Count <= 0) return "";
        return Encoding.UTF8.GetString(payload.Array, payload.Offset, payload.Count);
    }

    private bool TryParseConnectionPayload(ArraySegment<byte> payload, out string username, out int characterId)
    {
        username    = "";
        characterId = 0;

        string decoded = DecodePayloadToString(payload);
        if (string.IsNullOrWhiteSpace(decoded)) return false;

        string[] parts = decoded.Split('|');
        if (parts.Length < 2) { username = decoded.Trim(); return true; }

        username = parts[0].Trim();
        if (!int.TryParse(parts[1], out characterId)) characterId = 0;
        return true;
    }

    // ═════════════════════════════════════════════
    //  UI Helpers
    // ═════════════════════════════════════════════
    private UnityTransport GetUnityTransport() =>
        NetworkManager.Singleton.GetComponent<UnityTransport>();

    private void SetLobbyButtonsInteractable(bool interactable)
    {
        if (lobbyHostButton   != null) lobbyHostButton.interactable   = interactable;
        if (lobbyClientButton != null) lobbyClientButton.interactable = interactable;
    }

    /// <summary>แสดงสถานะ Lobby — ใช้ lobbyStatusText ถ้ามี ไม่งั้น fallback ไป errorText</summary>
    private void ShowStatus(string msg, Color color)
    {
        Debug.Log($"[LobbyStatus] {msg}");
        if (lobbyStatusText != null)
        {
            lobbyStatusText.gameObject.SetActive(true);
            lobbyStatusText.text  = msg;
            lobbyStatusText.color = color;
            return;
        }
        SetError(msg, color);
    }

    private void SetUIConnected(bool connected)
    {
        if (loginPanel  != null) loginPanel.SetActive(!connected);
        if (leaveButton != null) leaveButton.SetActive(connected);
        if (connected) ClearError();
    }

    private void SetError(string message, Color color)
    {
        if (errorText != null)
        {
            errorText.gameObject.SetActive(true);
            errorText.text  = message;
            errorText.color = color;
        }
        Debug.LogWarning(message);
    }

    private void ClearError()
    {
        if (errorText != null)
        {
            errorText.text = "";
            errorText.gameObject.SetActive(false);
        }
    }

    public async void OnLeaveButtonClick()
    {
        ClearError();
        if (clientButton != null) clientButton.interactable = true;
        if (lobbyHostButton != null) lobbyHostButton.interactable = true;
        if (lobbyClientButton != null) lobbyClientButton.interactable = true;
        
        if (QuickJoinSessionManager.Instance != null)
        {
            if (QuickJoinSessionManager.Instance.StartButton != null)
                QuickJoinSessionManager.Instance.StartButton.interactable = true;
            await QuickJoinSessionManager.Instance.LeaveAndCleanup();
        }

        // Call the built-in leave lobby for ConnectionManager's own lobbies
        LeaveLobby();

        if (NetworkManager.Singleton == null) return;

        _isLeaving = true;
        NetworkManager.Singleton.Shutdown();
        SetUIConnected(false);
    }
}