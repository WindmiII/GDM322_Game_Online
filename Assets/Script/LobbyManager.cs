using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject lobbyPanel;

    [Header("Lobby List")]
    public Transform contentPanel;
    public GameObject playerNamePrefab;

    [Header("Buttons")]
    public Button readyButton;
    public Button startGameButton;

    public NetworkVariable<bool> IsGameStarted = new NetworkVariable<bool>(false);
    private Dictionary<ulong, GameObject> playerUIList = new Dictionary<ulong, GameObject>();

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        // ซ่อนปุ่ม Start Game ถ้าไม่ใช่ Host
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(IsServer);
        }

        IsGameStarted.OnValueChanged += OnGameStarted;

        // *** จุดสำคัญ: เปิดหน้าจอ Lobby ทันทีที่ผู้เล่นเกิดในเซิร์ฟเวอร์ ***
        if (lobbyPanel != null && !IsGameStarted.Value)
        {
            lobbyPanel.SetActive(true);
        }

        // เปิดเมาส์ให้กดปุ่ม Ready ได้
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public override void OnNetworkDespawn()
    {
        // ปิดหน้า Lobby เมื่อหลุดจากเซิร์ฟเวอร์
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        IsGameStarted.OnValueChanged -= OnGameStarted;
    }

    private void Update()
    {
        if (!IsSpawned || IsGameStarted.Value) return;

        UpdateLobbyUI();
        CheckIfAllReady();
    }

    private void UpdateLobbyUI()
    {
        PlayerStateSync[] allPlayers = FindObjectsOfType<PlayerStateSync>();

        // 1. ลบ UI ของคนที่ออกเกม
        List<ulong> toRemove = new List<ulong>();
        foreach (var clientId in playerUIList.Keys)
        {
            bool found = false;
            foreach (var p in allPlayers)
            {
                if (p.OwnerClientId == clientId) { found = true; break; }
            }
            if (!found) toRemove.Add(clientId);
        }
        foreach (var id in toRemove)
        {
            Destroy(playerUIList[id]);
            playerUIList.Remove(id);
        }

        // 2. อัปเดตรายชื่อคนใน Lobby
        foreach (var player in allPlayers)
        {
            if (!playerUIList.ContainsKey(player.OwnerClientId))
            {
                GameObject newUI = Instantiate(playerNamePrefab, contentPanel);
                playerUIList.Add(player.OwnerClientId, newUI);
            }

            GameObject uiEntry = playerUIList[player.OwnerClientId];
            TMP_Text nameText = uiEntry.GetComponentInChildren<TMP_Text>(); 

            if (nameText != null)
            {
                string status = player.IsReady.Value ? "<color=#4CAF50>Ready</color>" : "<color=orange>Waiting</color>";
                string role = player.RoleIndex.Value == 0 ? "Survivor" : "Monster";
                nameText.text = $"[{status}] {player.PlayerName.Value} ({role})";
            }
        }
    }

    public void OnReadyButtonClicked()
    {
        // 1. เช็คว่าตัวละครของเราโหลดเสร็จมีตัวตนอยู่บนโลกแล้วหรือยัง
        if (NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            // 2. ดึงสคริปต์ของ "ตัวเราเอง" เท่านั้นมาใช้งาน
            if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerStateSync myPlayer))
            {
                myPlayer.ToggleReadyServerRpc();
            }
        }
    }

    // CHANGED: hand off to RoundManager. RoundManager will flip IsGameStarted at the right moment
    // (which keeps the existing OnGameStarted callback working — lobby UI closes, cursor locks).
    public void OnStartGameButtonClicked()
    {
        if (!IsServer) return;
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.BeginRound();
        }
        else
        {
            // Fallback for safety if RoundManager isn't in the scene yet — preserves old behavior.
            Debug.LogWarning("[LobbyManager] RoundManager.Instance is null — falling back to direct IsGameStarted flip.");
            IsGameStarted.Value = true;
        }
    }

    private void CheckIfAllReady()
    {
        if (!IsServer || startGameButton == null) return;

        PlayerStateSync[] allPlayers = FindObjectsOfType<PlayerStateSync>();
        bool isEveryoneReady = true;

        foreach (var p in allPlayers)
        {
            if (!p.IsReady.Value)
            {
                isEveryoneReady = false;
                break;
            }
        }

        // เปิดให้กด Start ได้เมื่อทุกคน Ready
        startGameButton.interactable = (isEveryoneReady && allPlayers.Length > 0);
    }

    private void OnGameStarted(bool oldValue, bool newValue)
    {
        if (newValue == true)
        {
            // ปิดหน้า Lobby UI เมื่อเกมเริ่ม
            if (lobbyPanel != null) lobbyPanel.SetActive(false);

            // ซ่อนเมาส์เพื่อกลับสู่โหมดบังคับตัวละคร
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
    
}
