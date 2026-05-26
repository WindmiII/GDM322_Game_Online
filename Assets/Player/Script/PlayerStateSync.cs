using Unity.Collections;
using Unity.Netcode;
using TMPro;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement; // เพิ่มเพื่อใช้คำสั่งเกี่ยวกับโหลด Scene

public class PlayerStateSync : NetworkBehaviour
{
    [Header("Name UI")]
    [SerializeField] private TMP_Text nameLabel; 

    [Header("Status Visual")]
    [SerializeField] private Renderer statusRenderer;

    [Header("Health UI")]
    [SerializeField] private TMP_Text hpTextUI; 

    [Header("Hit VFX (Survivor Only)")]
    public GameObject hitVfxPrefab;       // Spawned at survivor position when they take damage.
    public float hitVfxLifetime = 2f;     // Auto-destroy after this many seconds.
    public Vector3 hitVfxOffset = new Vector3(0f, 1f, 0f); // Local-space offset above feet.

    [Header("Death Settings (Survivor Only)")]
    public GameObject deadUI; // UI to show when dead
    public GameObject[] modelsToHide; // Models and character parts to hide
    public MonoBehaviour[] scriptsToDisable; // Scripts to disable (e.g., movement)
    
    private Transform spectateTarget; 
    private int spectateIndex = 0;
    private bool canSpectate = false; // Check if 5-second wait is over
    private Coroutine spectateCoroutine; // Stores the coroutine to cancel it upon respawn

    [Header("Game Over Settings")]
    public GameObject winUI; // Win UI screen (for Hunter/Monster)
    public GameObject loseUI; // Lose UI screen (for Hider/Survivor)
    
    private bool isGameOverSequenceRunning = false; // Prevents multiple Game Over sequence calls

    private bool isCursorUnlocked = false; // เก็บสถานะการกด J ปลดล็อกเมาส์
    public bool IsCursorUnlocked => isCursorUnlocked; // ดึงสถานะเมาส์ไปใช้บล็อกการเดิน/กล้อง

    private int lastTimeValue = -1; // เก็บค่าเวลาล่าสุดเพื่ออัปเดต UI เฉพาะตอนเวลาเปลี่ยน

    // ==========================================
    // Modification: Changed Permissions to Server to prevent errors during spawn
    // ==========================================
    public NetworkVariable<FixedString64Bytes> PlayerName =
        new NetworkVariable<FixedString64Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Health =
        new NetworkVariable<int>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> RoleIndex =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsSpecialStatus =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsReady =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ==========================================

    public override void OnNetworkSpawn()
    {
        // ---------------------------------------------------
        // Added code: Let the Server set HP to 100 after spawning
        if (IsServer)
        {
            Health.Value = 100;
        }
        // ---------------------------------------------------

        PlayerName.OnValueChanged += OnPlayerNameChanged;
        RoleIndex.OnValueChanged += OnRoleIndexChanged;
        IsSpecialStatus.OnValueChanged += OnStatusChanged;
        Health.OnValueChanged += OnHealthChanged;

        SceneManager.sceneLoaded += OnSceneLoaded; // ผูก Event เพื่อทำงานทันทีที่ฉากโหลดเสร็จ

        if (IsOwner)
        {
            if (ConnectionManager.Instance != null)
            {
                string localName = ConnectionManager.Instance.LocalUsername;
                int roleIdx = ConnectionManager.Instance.GetSelectedCharIndex();
                
                // Request the Server to assign name and role (Fixes Error)
                InitializePlayerServerRpc(localName, roleIdx);
            }

            // Show 100/100 HP correctly based on role
            if (RoleIndex.Value == 0) UpdateHPUI(Health.Value);
        }
        
        UpdateNameUI(PlayerName.Value.ToString(), RoleIndex.Value);
        UpdateStatusVisual(IsSpecialStatus.Value);
    }

    public override void OnNetworkDespawn()
    {
        PlayerName.OnValueChanged -= OnPlayerNameChanged;
        RoleIndex.OnValueChanged -= OnRoleIndexChanged;
        IsSpecialStatus.OnValueChanged -= OnStatusChanged;
        Health.OnValueChanged -= OnHealthChanged;

        SceneManager.sceneLoaded -= OnSceneLoaded; // ยกเลิก Event เพื่อป้องกันบัค

        // Check if the disconnecting player is the last surviving Survivor (delay half a second so the system removes the character first)
        Invoke(nameof(CheckGameOver), 0.5f);
    }

    // ==========================================
    // ServerRpc Section
    // ==========================================
    [ServerRpc(RequireOwnership = true)]
    private void InitializePlayerServerRpc(string name, int role)
    {
        PlayerName.Value = name;
        RoleIndex.Value = role;
    }

    [ServerRpc(RequireOwnership = true)]
    public void ToggleReadyServerRpc()
    {
        IsReady.Value = !IsReady.Value;
        
        if (IsReady.Value)
        {
            Health.Value = 100; // คืนชีพ (เกิดใหม่) ทันทีที่ผู้เล่นกด Ready (Server เป็นคนสั่ง)
        }
    }

    [ServerRpc(RequireOwnership = true)]
    public void ToggleAttackStatusServerRpc()
    {
        IsSpecialStatus.Value = !IsSpecialStatus.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(int damage)
    {
        if (RoleIndex.Value == 1) return;

        if (Health.Value > 0)
        {
            Health.Value -= damage;
            if (Health.Value < 0) Health.Value = 0;
            Debug.Log($"[Server] Survivor took {damage} damage. Current HP: {Health.Value}");
        }
    }
    // ==========================================

    private void Update()
    {
        if (nameLabel != null && Camera.main != null)
        {
            nameLabel.transform.rotation = Camera.main.transform.rotation;

            bool shouldShowName = false; // ค่าเริ่มต้นคือซ่อนชื่อ

            // ถ้ายังมีชีวิตอยู่ และ ไม่ใช่ตัวเราเอง ถึงจะเช็คว่าควรโชว์ชื่อให้เห็นไหม
            if (Health.Value > 0 && !IsOwner)
            {
                if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
                {
                    var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerStateSync>();
                    if (localPlayer != null)
                    {
                        // จะโชว์ชื่อก็ต่อเมื่ออยู่ฝ่ายเดียวกันเท่านั้น
                        shouldShowName = (localPlayer.RoleIndex.Value == RoleIndex.Value);
                    }
                }
            }
            
            // ใช้ SetActive เพื่อซ่อนทั้งข้อความและพื้นหลัง (ถ้ามี) อย่างเด็ดขาด
            if (nameLabel.gameObject.activeSelf != shouldShowName)
            {
                nameLabel.gameObject.SetActive(shouldShowName);
            }
        }

        // ปลดล็อก/ล็อก เมาส์ด้วยการกดปุ่ม J
        if (IsOwner)
        {
            if (Input.GetKeyDown(KeyCode.J))
            {
                isCursorUnlocked = !isCursorUnlocked; // สลับสถานะเมาส์
            }
        }

        // Spectator mode for dead Survivors:
        // Handled event-driven via BeginMonsterSpectate() in WaitBeforeSpectate, which engages
        // SpectatorCameraController to smoothly follow the monster. No per-frame work needed here.

        // ดึงเวลาจาก GameTimeManager มาแสดงผลและเช็คเวลาหมด
        if (GameTimeManager.Instance != null && IsOwner)
        {
            int currentTime = GameTimeManager.Instance.GameTimer.Value;
            if (currentTime != lastTimeValue)
            {
                if (currentTime <= 0 && lastTimeValue > 0)
                {
                    TriggerTimeUpGameOver();
                }
                lastTimeValue = currentTime;
            }
        }
    }


    private void LateUpdate()
    {
        if (!IsOwner) return;

        // ใช้ LateUpdate เพื่อบังคับทับคำสั่งล็อกเมาส์ของสคริปต์เดิน/กล้อง
        // จะโชว์เมาส์ก็ต่อเมื่อ: ผู้เล่นกด J, เกมยังไม่เริ่ม (อยู่ใน Lobby), หรือเปิดหน้าเมนู ESC
        bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted.Value;
        bool isMenuOpen = GameMenuManager.Instance != null && GameMenuManager.Instance.isMenuOpen;

        if (isCursorUnlocked || !isGameStarted || isMenuOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void OnAttack()
    {
        if (!IsOwner) return;
        ToggleAttackStatusServerRpc(); // Send command to Server instead
    }

    private void OnPlayerNameChanged(FixedString64Bytes oldValue, FixedString64Bytes newValue)
    {
        UpdateNameUI(newValue.ToString(), RoleIndex.Value);
    }

    private void OnRoleIndexChanged(int oldValue, int newValue)
    {
        UpdateNameUI(PlayerName.Value.ToString(), newValue);
        if (IsOwner && newValue == 0) UpdateHPUI(Health.Value);
    }

    private void OnStatusChanged(bool oldValue, bool newValue)
    {
        UpdateStatusVisual(newValue);
    }

    private void OnHealthChanged(int oldValue, int newValue)
    {
        if (RoleIndex.Value == 0 && IsOwner)
        {
            UpdateHPUI(newValue);
        }

        // VFX: spawn hit effect when survivor takes damage (HP went down).
        // Runs on every client because OnValueChanged fires everywhere.
        if (RoleIndex.Value == 0 && newValue < oldValue && hitVfxPrefab != null)
        {
            Vector3 spawnPos = transform.position + hitVfxOffset;
            GameObject vfx = Instantiate(hitVfxPrefab, spawnPos, Quaternion.identity);
            Destroy(vfx, hitVfxLifetime);
        }

        // Check if out of health or respawned (Survivor only)
        if (RoleIndex.Value == 0)
        {
            if (newValue <= 0 && oldValue > 0)
                HandleDeath();
            else if (newValue > 0 && oldValue <= 0) 
                HandleRespawn();
        }

        // Check game over status whenever health changes (someone dies)
        CheckGameOver();
    }

    private void TriggerTimeUpGameOver()
    {
        if (isGameOverSequenceRunning) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerStateSync>();
            if (localPlayer != null && !localPlayer.isGameOverSequenceRunning)
            {
                // ส่งค่า true เพราะ Hiders (ฝ่ายแอบ) เป็นฝ่ายชนะเมื่อเวลาหมด
                localPlayer.StartCoroutine(localPlayer.HandleGameOverSequence(true)); 
            }
        }
    }

    private void UpdateHPUI(int currentHP)
    {
        if (hpTextUI != null)
        {
            hpTextUI.text = $"{currentHP}/100HP";
        }
    }

    private void UpdateNameUI(string newName, int roleIndex)
    {
        if (nameLabel != null)
        {
            string roleColor = roleIndex == 0 ? "#00A2FF" : "#FF0000";
            string roleName = roleIndex == 0 ? "Survivor" : "Monster";
            nameLabel.text = $"<color={roleColor}>{roleName}</color>\n{newName}";
        }
    }

    private void UpdateStatusVisual(bool active)
    {
        if (statusRenderer == null) return;
        statusRenderer.material.color = active ? Color.red : Color.white;
    }

    private void HandleDeath()
    {
        // Hide models and disable Collider so everyone on the server sees it disappear
        foreach (var obj in modelsToHide)
            if (obj != null) obj.SetActive(false);

        if (nameLabel != null) nameLabel.gameObject.SetActive(false); // Hide dead player's name

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        if (IsOwner)
        {
            if (deadUI != null) deadUI.SetActive(true);
            foreach (var script in scriptsToDisable)
                if (script != null) script.enabled = false;
            
            // Start 5-second countdown before spectating
            if (spectateCoroutine != null) StopCoroutine(spectateCoroutine);
            spectateCoroutine = StartCoroutine(WaitBeforeSpectate(5f));
        }
    }

    private IEnumerator WaitBeforeSpectate(float delay)
    {
        yield return new WaitForSeconds(delay);
        canSpectate = true;
        if (deadUI != null) deadUI.SetActive(false); // Close Die screen when spectating starts
        BeginMonsterSpectate(); // NEW: hand off to SpectatorCameraController to follow the monster
    }

    private void HandleRespawn()
    {
        foreach (var obj in modelsToHide)
            if (obj != null) obj.SetActive(true);

        if (nameLabel != null) nameLabel.gameObject.SetActive(true); // Show name again when respawned

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        if (IsOwner)
        {
            canSpectate = false; // Reset Spectate state
            if (spectateCoroutine != null) 
            {
                StopCoroutine(spectateCoroutine); // Cancel only the spectate countdown
                spectateCoroutine = null;
            }

            // NEW: stop the death-spectator camera (restores own camera under its original parent).
            var spectator = GetComponent<SpectatorCameraController>();
            if (spectator != null) spectator.StopSpectating();

            if (deadUI != null) deadUI.SetActive(false);
            foreach (var script in scriptsToDisable)
                if (script != null) script.enabled = true;
                
            spectateTarget = null;
        }
    }

    // NEW: kick off the spectator camera following the monster, after the 5s "you died" wait.
    // Mirrors the MonsterPreview phase behavior — same SpectatorCameraController, same smoothing,
    // same 3rd-person offsets.
    private void BeginMonsterSpectate()
    {
        if (!IsOwner) return;

        Transform monsterTransform = ResolveMonsterTransform();
        if (monsterTransform == null)
        {
            Debug.LogWarning("[PlayerStateSync] BeginMonsterSpectate: no monster found — cannot spectate.");
            return;
        }

        var spectator = GetComponent<SpectatorCameraController>();
        if (spectator == null)
        {
            Debug.LogWarning("[PlayerStateSync] BeginMonsterSpectate: no SpectatorCameraController on this player.");
            return;
        }

        spectator.StartSpectating(monsterTransform);
        Debug.Log("[PlayerStateSync] Dead survivor now spectating monster.");
    }

    // Try RoundManager.MonsterPlayerRef first (authoritative — set when round started).
    // Fall back to searching by RoleIndex in case the round started weirdly (debug / edge cases).
    private Transform ResolveMonsterTransform()
    {
        if (RoundManager.Instance != null)
        {
            if (RoundManager.Instance.MonsterPlayerRef.Value.TryGet(out NetworkObject mo) && mo != null)
                return mo.transform;
        }

        PlayerStateSync[] all = FindObjectsOfType<PlayerStateSync>();
        foreach (var p in all)
        {
            if (p != null && p.RoleIndex.Value == 1) return p.transform;
        }
        return null;
    }

    private void CheckGameOver()
    {
        if (isGameOverSequenceRunning) return;

        PlayerStateSync[] allPlayers = FindObjectsOfType<PlayerStateSync>();
        bool hasSurvivor = false;
        bool allSurvivorsDead = true;

        // Check if all Survivors on the map are dead
        foreach (var p in allPlayers)
        {
            // Count only Survivors still connected (not disconnected)
            if (p.IsSpawned && p.RoleIndex.Value == 0) 
            {
                hasSurvivor = true;
                if (p.Health.Value > 0)
                {
                    allSurvivorsDead = false;
                    break;
                }
            }
        }

        // If there are Survivors and all are dead = Game Over
        if (hasSurvivor && allSurvivorsDead)
        {
            // สั่งรันหน้าจอจบเกมที่ "ตัวละครของตัวเองเท่านั้น (Local Player)" ป้องกันบัค IsOwner ทำให้ฝ่ายหาไม่เห็น UI
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerStateSync>();
                if (localPlayer != null && !localPlayer.isGameOverSequenceRunning)
                {
                    localPlayer.StartCoroutine(localPlayer.HandleGameOverSequence(false)); // ส่งค่า false = ฝ่ายแอบแพ้ ฝ่ายหาชนะ
                }
            }
        }
    }

    // ฟังก์ชันนี้จะทำงานอัตโนมัติทันทีที่ Scene โหลดเสร็จ 100%
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsOwner) return;

        // วาร์ปตัวละครของตัวเองกลับไปจุดเกิดในฉากใหม่
        GameObject respawnPoint = GameObject.Find("ReSpawnPoint");
        if (respawnPoint != null)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            transform.position = respawnPoint.transform.position;
        }

        // เปิดหน้า Lobby กลับมา
        LobbyManager lobby = FindObjectOfType<LobbyManager>(true);
        if (lobby != null) lobby.gameObject.SetActive(true);
    }

    private IEnumerator HandleGameOverSequence(bool hidersWin)
    {
        isGameOverSequenceRunning = true;

        // 1. Show Game Over UI based on role and win condition
        if (deadUI != null) deadUI.SetActive(false); // Close 'You Died' screen so it doesn't block
        if (RoleIndex.Value == 0) // Hider (ฝ่ายซ่อน)
        {
            if (hidersWin) { if (winUI != null) winUI.SetActive(true); } // เวลาหมด แอบชนะ
            else { if (loseUI != null) loseUI.SetActive(true); } // ตายหมด แอบแพ้
        }
        else if (RoleIndex.Value == 1) // Seeker (ฝ่ายหา)
        {
            if (hidersWin) { if (loseUI != null) loseUI.SetActive(true); } // เวลาหมด หาแพ้
            else { if (winUI != null) winUI.SetActive(true); } // ฆ่าหมด หาชนะ
        }

        // ทำลาย (ซ่อน) โมเดลและชื่อของตัวละครทุกคนในฉากทันที เพื่อเคลียร์หน้าจอตอนจบเกม
        PlayerStateSync[] allPlayersInScene = FindObjectsOfType<PlayerStateSync>();
        foreach (var p in allPlayersInScene)
        {
            foreach (var obj in p.modelsToHide)
                if (obj != null) obj.SetActive(false); // ซ่อนโมเดล
            
            if (p.nameLabel != null) p.nameLabel.gameObject.SetActive(false); // ซ่อนป้ายชื่อ
            
            Collider col = p.GetComponent<Collider>();
            if (col != null) col.enabled = false; // ปิดการชน
        }

        // 2. Wait for 5 seconds
        yield return new WaitForSeconds(5f);

        // 3. Close Game Over UI
        if (loseUI != null) loseUI.SetActive(false);
        if (winUI != null) winUI.SetActive(false);

        // 4. ตัดการเชื่อมต่อและกลับไปหน้าเริ่มเกม (Host/Client)
        if (IsServer)
        {
            EndGameAndDisconnectClientRpc();
        }

        isGameOverSequenceRunning = false;
    }

    [ClientRpc]
    private void EndGameAndDisconnectClientRpc()
    {
        StartCoroutine(DisconnectRoutine());
    }

    private IEnumerator DisconnectRoutine()
    {
        // ให้ Server รอ 0.5 วินาที เพื่อให้คำสั่งนี้ส่งไปถึง Client ทุกเครื่องก่อนที่จะตัดเน็ต
        if (IsServer) yield return new WaitForSeconds(0.5f);

        if (GameMenuManager.Instance != null)
        {
            GameMenuManager.Instance.LeaveGame(); // ใช้ฟังก์ชันจากที่มีอยู่แล้วเพื่อตัดการเชื่อมต่อ
        }
        else
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}