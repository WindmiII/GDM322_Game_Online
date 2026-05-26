using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class MainPlayerScript : NetworkBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 10f;
    public float jumpForce = 5f;
    public float groundCheckDistance = 1.1f;
    public LayerMask groundLayer;
    private Vector2 moveInput;
    private bool isSprinting = false;
    private Rigidbody rb;

    [Header("Look (FPS)")]
    public float mouseSensitivityX = 50f; // Left-right turn speed
    public float mouseSensitivityY = 25f; // Up-down turn speed (adjusted to be lower than X)
    public Transform cameraTransform;
    public Transform cameraTarget; // [New] Target for the camera to follow

    [Header("Camera Zoom Settings")]
    public float zoomSensitivity = 0.02f; // Scroll speed multiplier
    public float minZoomDistance = -1f;   // Closest distance (3rd Person)
    public float maxZoomDistance = -15f;  // Furthest distance (3rd Person)
    public float minFOV = 30f;            // Closest FOV (1st Person)
    public float maxFOV = 75f;            // Furthest FOV (1st Person)
    public float zoomSmoothTime = 10f;    // Smooth speed
    
    private Vector2 lookInput;
    private float verticalLookRotation = 0f;

    [Header("Combat Settings")]
    public float attackRange = 5f; // Attack range
    public int attackDamage = 10; // Damage amount

    [Header("Hide For Local Player")]
    public Renderer[] visualsToHide;

    [Header("Player Tags (floating UI above player)")]
    public GameObject nameTag; // Name text GameObject — toggled with body visibility

    [Header("VFX")]
    public GameObject transformVfxPrefab; // Spawned at player position on prop transform / reset.
    public float transformVfxLifetime = 2f;

    [Header("UI Settings")]
    public GameObject monsterUI; // Slot for Monster UI
    public GameObject survivorUI; // Slot for Survivor UI

    [Header("Prop Hunt Settings")]
    public Transform propVisualContainer; // Container holding the prop models
    public float interactRange = 3f;
    public float interactRadius = 0.5f; // ความกว้างของเป้าเล็ง (รัศมี) ยิ่งเยอะยิ่งเล็งโดนง่าย
    public Vector3 propPositionOffset = Vector3.zero; // [New] Offset to fix prop pivot issues

    // Variables to control Player Input
    private PlayerInput playerInput;
    private PlayerCameraSetup cameraSetup;

    // Variable to check if typing/console is open
    private bool isTyping = false;
    private Transform currentMeshTransform; // Tracks current active visual

    // Zoom tracking variables
    private Transform actualCameraTransform;
    private Camera actualCameraComponent;
    private float targetZoomZ;
    private float targetFOV;
    private bool isThirdPersonCamera = false;

    // NEW: subscription bookkeeping for RoundManager
    private bool _subscribedToRoundManager = false;
    private SpectatorCameraController _spectatorController;
    private PlayerPowerupReceiver _powerupReceiver;
    private Coroutine _monsterRefRetryCoroutine;

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>(); // Get the Component
        _spectatorController = GetComponent<SpectatorCameraController>();
        _powerupReceiver = GetComponent<PlayerPowerupReceiver>();

        // Try to find Camera automatically if not assigned in Inspector
        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        // Prepare Camera for Zooming (Detect if 3rd person or 1st person)
        if (cameraTransform != null)
        {
            actualCameraComponent = cameraTransform.GetComponentInChildren<Camera>();
            if (actualCameraComponent != null)
            {
                if (actualCameraComponent.transform != cameraTransform)
                {
                    // 3rd Person (Camera is a child of the boom)
                    isThirdPersonCamera = true;
                    actualCameraTransform = actualCameraComponent.transform;
                    targetZoomZ = actualCameraTransform.localPosition.z;
                }
                else
                {
                    // 1st Person (Camera is the boom itself)
                    isThirdPersonCamera = false;
                    targetFOV = actualCameraComponent.fieldOfView;
                }
            }
        }

        if (IsOwner)
        {
            // Enable local camera and Input
            if (cameraTransform != null) cameraTransform.gameObject.SetActive(true);
            if (playerInput != null) playerInput.enabled = true;

            currentMeshTransform = playerVisualBody; // Default target

            // 1. Hide character UI on spawn (since Lobby is active)
            if (monsterUI != null) monsterUI.SetActive(false);
            if (survivorUI != null) survivorUI.SetActive(false);

            // 2. Wait for LobbyManager to start the game
            if (LobbyManager.Instance != null)
            {
                // A. Subscribe to value changed event
                LobbyManager.Instance.IsGameStarted.OnValueChanged += OnGameStartedChanged;

                // B. Check current value immediately (fixes late joiner bug)
                if (LobbyManager.Instance.IsGameStarted.Value)
                {
                    OnGameStartedChanged(false, true);
                }
            }

            // NEW (§12.17): subscribe to RoundManager phase changes via coroutine
            // because the singleton may not be set yet when OnNetworkSpawn fires.
            // (Moved out of IsOwner block: every client must run visibility hiding for every
            // survivor visible on the monster's screen during MonsterPreview. Owner-only work
            // is gated inside the handler itself.)
        }
        else
        {
            // Disable camera and input for other players
            if (cameraTransform != null) cameraTransform.gameObject.SetActive(false);
            if (playerInput != null) playerInput.enabled = false;

            // Disable UI for other players
            if (monsterUI != null) monsterUI.SetActive(false);
            if (survivorUI != null) survivorUI.SetActive(false);
        }
        cameraSetup = GetComponent<PlayerCameraSetup>();

        // Subscribe regardless of ownership — phase-driven visibility must run on every client.
        StartCoroutine(SubscribeRoundManagerWhenReady());
    }

    public override void OnNetworkDespawn()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.IsGameStarted.OnValueChanged -= OnGameStartedChanged;
        }

        // NEW: clean up RoundManager subscription
        if (_subscribedToRoundManager && RoundManager.Instance != null)
        {
            RoundManager.Instance.CurrentPhase.OnValueChanged -= OnRoundPhaseChanged;
            _subscribedToRoundManager = false;
        }
        if (_monsterRefRetryCoroutine != null)
        {
            StopCoroutine(_monsterRefRetryCoroutine);
            _monsterRefRetryCoroutine = null;
        }
    }

    // NEW (§12.17): wait until RoundManager.Instance exists, then subscribe and apply the current phase
    // (this fires the late-joiner correctness path automatically too — §6.5).
    private IEnumerator SubscribeRoundManagerWhenReady()
    {
        while (RoundManager.Instance == null) yield return null;
        RoundManager.Instance.CurrentPhase.OnValueChanged += OnRoundPhaseChanged;
        _subscribedToRoundManager = true;
        Debug.Log($"[MainPlayerScript] Subscribed to RoundManager. Current phase = {RoundManager.Instance.CurrentPhase.Value}. _spectatorController null? {_spectatorController == null}");
        OnRoundPhaseChanged(RoundPhase.Lobby, RoundManager.Instance.CurrentPhase.Value);
    }

    private void OnGameStartedChanged(bool previousValue, bool newValue)
    {
        if (newValue == true) // When Game Start becomes true
        {
            if (IsOwner)
            {
                // Force lock mouse cursor immediately
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                // Ensure Game Menu is closed when game starts
                if (GameMenuManager.Instance != null) 
                    GameMenuManager.Instance.ToggleMenu(false);
            }

            // Enable UI based on Role
            PlayerStateSync state = GetComponent<PlayerStateSync>();
            if (state != null)
            {
                if (state.RoleIndex.Value == 0 && survivorUI != null) survivorUI.SetActive(true);
                if (state.RoleIndex.Value == 1 && monsterUI != null) monsterUI.SetActive(true);
            }
        }
    }

    // NEW (§5.3 d, §12.11): react to phase changes.
    // This handler runs on every client for every player object — visibility changes must
    // propagate everywhere (the monster's client must hide the survivor's body, not just the
    // survivor's own client). Owner-only work (spectator camera, retry coroutine) is gated
    // by IsOwner inside the relevant branches.
    private void OnRoundPhaseChanged(RoundPhase prev, RoundPhase next)
    {
        PlayerStateSync state = GetComponent<PlayerStateSync>();
        if (state == null) { Debug.LogWarning("[MainPlayerScript] OnRoundPhaseChanged: no PlayerStateSync."); return; }
        int role = state.RoleIndex.Value;

        Debug.Log($"[MainPlayerScript] OnRoundPhaseChanged: {prev} -> {next}, role={role}, IsOwner={IsOwner}");

        // Survivor logic
        if (role == 0)
        {
            if (next == RoundPhase.MonsterPreview)
            {
                // VISIBILITY (every client): hide this survivor's body and name tag so the
                // monster cannot see survivors during preview.
                if (playerVisualBody != null) playerVisualBody.gameObject.SetActive(false);
                if (nameTag != null) nameTag.SetActive(false);

                // OWNER-ONLY: start spectating the monster.
                if (IsOwner)
                {
                    Debug.Log("[MainPlayerScript] Survivor entering preview (owner): starting spectator.");
                    BeginSpectatingMonster();
                }
            }
            else if (prev == RoundPhase.MonsterPreview && next != RoundPhase.MonsterPreview)
            {
                // VISIBILITY (every client): restore body and name now that preview is over.
                if (playerVisualBody != null) playerVisualBody.gameObject.SetActive(true);
                if (nameTag != null) nameTag.SetActive(true);

                // OWNER-ONLY: stop spectating, restore own camera.
                if (IsOwner)
                {
                    Debug.Log("[MainPlayerScript] Survivor leaving preview (owner): stopping spectator.");
                    if (_spectatorController != null) _spectatorController.StopSpectating();
                    if (_monsterRefRetryCoroutine != null)
                    {
                        StopCoroutine(_monsterRefRetryCoroutine);
                        _monsterRefRetryCoroutine = null;
                    }
                }
            }
        }
    }

    private void BeginSpectatingMonster()
    {
        if (_spectatorController == null)
        {
            Debug.LogWarning("[MainPlayerScript] BeginSpectatingMonster: _spectatorController is NULL on this prefab. " +
                             "Did you add SpectatorCameraController to the Survivors prefab?");
            return;
        }
        if (_monsterRefRetryCoroutine != null) StopCoroutine(_monsterRefRetryCoroutine);
        _monsterRefRetryCoroutine = StartCoroutine(ResolveMonsterAndSpectate());
    }

    private IEnumerator ResolveMonsterAndSpectate()
    {
        float deadline = Time.time + 1.0f;
        int attempts = 0;
        while (Time.time < deadline)
        {
            attempts++;
            if (RoundManager.Instance != null &&
                RoundManager.Instance.MonsterPlayerRef.Value.TryGet(out NetworkObject monsterObj))
            {
                Debug.Log($"[MainPlayerScript] Monster ref resolved after {attempts} attempts. Starting spectator.");
                _spectatorController.StartSpectating(monsterObj.transform);
                _monsterRefRetryCoroutine = null;
                yield break;
            }
            yield return null;
        }
        Debug.LogWarning($"[MainPlayerScript] Could not resolve monster ref within 1s (attempts={attempts}); spectator camera not started.");
        _monsterRefRetryCoroutine = null;
    }

    // NEW (§5.3 a, §12.3): unified gating that combines IsGameStarted + RoundManager phase + RoleIndex.
    // Returns true if the local player is allowed to act (move, jump, fire) right now.
    private bool CanLocalPlayerAct()
    {
        if (LobbyManager.Instance != null && !LobbyManager.Instance.IsGameStarted.Value) return false;
        if (RoundManager.Instance == null) return true; // fallback if RoundManager missing from scene
        var phase = RoundManager.Instance.CurrentPhase.Value;
        if (phase == RoundPhase.Lobby || phase == RoundPhase.Ended) return false;

        var state = GetComponent<PlayerStateSync>();
        int role = state != null ? state.RoleIndex.Value : 0;

        // Phase-vs-role: only the active side can act.
        if (phase == RoundPhase.MonsterPreview) return role == 1;  // only monster moves during preview
        if (phase == RoundPhase.SurvivorHide)   return role == 0;  // only survivors move during hide
        return true; // Active = both sides act
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        if (context.started || context.performed)
            isSprinting = true;
        else if (context.canceled)
            isSprinting = false;
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        
        PlayerStateSync myState = GetComponent<PlayerStateSync>();
        bool isJUnlocked = myState != null && myState.IsCursorUnlocked;

        if (isTyping || (GameMenuManager.Instance != null && GameMenuManager.Instance.isMenuOpen) || isJUnlocked) return;
        // CHANGED (§12.3): phase-aware gate instead of just IsGameStarted.
        if (!CanLocalPlayerAct()) return;

        if (context.performed && IsGrounded())
        {
            rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    private bool IsGrounded()
    {
        // ขยับจุดยิงขึ้นมาเล็กน้อย (0.1f) ป้องกันเส้นจมลงไปใต้พื้น
        Vector3 origin = transform.position + (Vector3.up * 0.1f);
        
        bool isHit = Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundLayer);
        
        // วาดเส้นให้เห็นในหน้าต่าง Scene ตอนทดสอบ (สีเขียว = แตะพื้นโดดได้, สีแดง = ลอยอยู่โดดไม่ได้)
        Debug.DrawRay(origin, Vector3.down * groundCheckDistance, isHit ? Color.green : Color.red, 2f);
        
        return isHit;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        lookInput = context.ReadValue<Vector2>();
    }

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // ถ้าล็อกตัวละครอยู่ ห้ามโจมตี/แปลงร่าง
        PlayerStateSync stateForFire = GetComponent<PlayerStateSync>();
        bool isJUnlocked = stateForFire != null && stateForFire.IsCursorUnlocked;

        if (isTyping || isJUnlocked) return;

        // Don't attack if mouse is unlocked (cursor visible)
        if (Cursor.lockState == CursorLockMode.None) return;

        // Don't attack if menu is open
        if (GameMenuManager.Instance != null && GameMenuManager.Instance.isMenuOpen) return;

        // Layer 1 (§12.6): phase-vs-role gate. Wrong-role player can't fire at all.
        if (!CanLocalPlayerAct()) return;

        // Separate logic by Role on left click
        if (context.started)
        {
            Debug.Log("[OnFire] Left click triggered");
            PlayerStateSync myState = GetComponent<PlayerStateSync>();
            if (myState != null)
            {
                Debug.Log($"[OnFire] Current Role Index: {myState.RoleIndex.Value}");

                // Layer 2 (§12.6): even for the right-role player, narrow further by phase.
                RoundPhase phase = RoundManager.Instance != null
                    ? RoundManager.Instance.CurrentPhase.Value
                    : RoundPhase.Active;

                if (myState.RoleIndex.Value == 1) // Monster
                {
                    // Monster only attacks during Active (not during their own preview phase).
                    if (phase != RoundPhase.Active) return;
                    AttemptAttack();
                }
                else if (myState.RoleIndex.Value == 0) // Survivor
                {
                    // Survivors can transform during Hide AND Active (lets them set up during hide).
                    if (phase != RoundPhase.Active && phase != RoundPhase.SurvivorHide) return;
                    HandlePropTransformation();
                }
            }
            else
            {
                Debug.LogWarning("[OnFire] PlayerStateSync component not found on character!");
            }
        }
    }

    public void OnResetToHuman(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // Trigger only on button down (started)
        if (context.started)
        {
            Debug.Log("[PropHunt] Reset to Human Input Triggered");
            ResetToHumanServerRpc();
        }
    }

    private void AttemptAttack()
    {
        PlayerStateSync myState = GetComponent<PlayerStateSync>();

        if (myState != null && myState.RoleIndex.Value == 1)
        {
            Debug.Log("Monster Attempting Attack!");
            if (cameraTransform != null)
            {
                Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
                RaycastHit[] hits = Physics.RaycastAll(ray, attackRange);
                bool hitSomeone = false;

                foreach (RaycastHit hit in hits)
                {
                    PlayerStateSync targetState = hit.collider.GetComponentInParent<PlayerStateSync>();
                    if (targetState == null)
                    {
                        targetState = hit.collider.transform.root.GetComponentInChildren<PlayerStateSync>();
                    }

                    if (targetState != null && targetState != myState)
                    {
                        if (targetState.RoleIndex.Value == 0)
                        {
                            Debug.Log($"Hit Survivor! Dealing {attackDamage} damage.");
                            targetState.TakeDamageServerRpc(attackDamage);
                            hitSomeone = true;
                            break;
                        }
                    }
                }

                if (!hitSomeone && hits.Length > 0)
                {
                    Debug.Log("Hit nothing actionable (or self/wall).");
                }
            }
        }
    }

    private void HandlePropTransformation()
    {
        Debug.Log("[PropHunt] Entered HandlePropTransformation function");
        if (cameraTransform != null)
        {
            // Shoot Ray from the center of the screen (camera)
            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            
            // (Optional) Draw a red Ray in Scene View to show direction (lasts 2 seconds)
            Debug.DrawRay(ray.origin, ray.direction * interactRange, Color.red, 2f);

            // ใช้ SphereCastAll เพื่อให้ทะลุตัวละครของเราเองไปหา Prop ที่อยู่ด้านหน้าได้
            RaycastHit[] hits = Physics.SphereCastAll(ray, interactRadius, interactRange);
            bool hitProp = false;

            // วนลูปเช็คสิ่งที่ยิงโดนทั้งหมด
            foreach (RaycastHit hit in hits)
            {
                // ถ้าชนโดนตัวละครของเราเอง ให้ข้ามไปเช็คชิ้นต่อไป
                if (hit.collider.transform.root == transform.root) continue;

                Debug.Log($"[PropHunt] SphereCast hit: {hit.collider.gameObject.name} | Tag: {hit.collider.tag}");

                if (hit.collider.CompareTag("Prop"))
                {
                    Debug.Log("[PropHunt] Correct tag! Sending transformation command...");
                    // Send command to Server to change model
                    ChangePropServerRpc(hit.collider.gameObject.name);
                    hitProp = true;
                    break; // หยุดค้นหาเมื่อเจอ Prop ตัวแรกแล้ว
                }
            }

            if (!hitProp)
            {
                Debug.Log("[PropHunt] SphereCast did not hit any valid 'Prop' in range");
            }
        }
        else
        {
            Debug.LogWarning("[PropHunt] Error: cameraTransform is Null, cannot shoot SphereCast!");
        }
    }

    [ServerRpc]
    private void ChangePropServerRpc(string propName)
    {
        // Send data to everyone to change model simultaneously
        ChangePropClientRpc(propName);
    }

    // VFX helper: spawns the transform puff prefab at the player's position.
    // Called from both ChangePropClientRpc and ResetToHumanClientRpc (each runs on every client).
    // Safe to call when prefab is unassigned — just no-ops.
    private void SpawnTransformVfx()
    {
        if (transformVfxPrefab == null) return;
        Vector3 spawnPos = transform.position + Vector3.up * 0.5f;
        GameObject vfx = Instantiate(transformVfxPrefab, spawnPos, Quaternion.identity);
        Destroy(vfx, transformVfxLifetime);
    }

    [ClientRpc]
    private void ChangePropClientRpc(string propName)
    {
        // VFX: spawn transform puff at player position on every client.
        SpawnTransformVfx();

        // 1. Hide original body (SurvivorsCapsule)
        if (playerVisualBody != null) playerVisualBody.gameObject.SetActive(false);

        // Hide name tag too — otherwise it floats above the prop and reveals the survivor.
        if (nameTag != null) nameTag.SetActive(false);

        if (propVisualContainer != null)
        {
            // 2. Disable all old models
            foreach (Transform child in propVisualContainer)
            {
                child.gameObject.SetActive(false);
            }

            // 3. Find and enable new model
            string target = propName.ToLower().Replace("(clone)", "").Trim();

            foreach (Transform child in propVisualContainer)
            {
                if (child.name.ToLower().Contains(target)) 
                {
                    child.gameObject.SetActive(true);
                    
                    // [Important!] Force position to reset to center + offset
                    child.localPosition = propPositionOffset; 
                    currentMeshTransform = child; // Update camera target
                    
                    Debug.Log($"Activated: {child.name} at {child.localPosition}");
                    break;
                }
            }
        }
    }

    [ServerRpc]
    private void ResetToHumanServerRpc()
    {
        ResetToHumanClientRpc();
    }

    [ClientRpc]
    private void ResetToHumanClientRpc()
    {
        // VFX: spawn transform puff at player position on every client.
        SpawnTransformVfx();

        // 1. ปิดโมเดลวัตถุ (Prop) ทุกตัวที่เคยแปลงร่างไว้
        if (propVisualContainer != null)
        {
            foreach (Transform child in propVisualContainer)
            {
                child.gameObject.SetActive(false);
            }
        }

        // 2. เปิดร่างคนปกติกลับมา (ตัวสีน้ำเงิน)
        if (playerVisualBody != null)
        {
            playerVisualBody.gameObject.SetActive(true);
            
            // บังคับให้ MeshRenderer แสดงผล (แก้ปัญหาตัวหายในเครื่องตัวเอง)
            MeshRenderer mr = playerVisualBody.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = true;
            
            currentMeshTransform = playerVisualBody; // Reset camera target to human
        }

        // Restore name tag now that the human body is back.
        if (nameTag != null) nameTag.SetActive(true);
        
        // 3. เปิด UI เฉพาะของ Survivor กลับมา
        if (IsOwner && survivorUI != null)
        {
            survivorUI.SetActive(true);
        }

        Debug.Log("Returned to Human form and UI restored");
    }

    private void Update()
    {
        if (!IsOwner) return;

        // CHANGED (§12.3): phase-aware gate instead of just IsGameStarted.
        if (!CanLocalPlayerAct()) return;

        // Check menu
        if (GameMenuManager.Instance != null && GameMenuManager.Instance.isMenuOpen) return;

        // Toggle typing mode when pressing Slash (/)
        if (Keyboard.current != null && Keyboard.current.slashKey.wasPressedThisFrame)
        {
            // สลับสถานะการล็อกตัวละคร
            isTyping = !isTyping;
        }

        // ออกจากการล็อกเมื่อกด Enter หรือ Escape
        else if (isTyping && Keyboard.current != null && 
                 (Keyboard.current.enterKey.wasPressedThisFrame || 
                  Keyboard.current.numpadEnterKey.wasPressedThisFrame || 
                  Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            isTyping = false;
        }
        
        PlayerStateSync myState = GetComponent<PlayerStateSync>();
        bool isJUnlocked = myState != null && myState.IsCursorUnlocked;

        // [สำคัญ!] บังคับล็อกและซ่อนเมาส์ "ทุกเฟรม" ระหว่างอยู่ในเกม
        // (สู้กับปลั๊กอินอื่นเช่น Quantum Console ที่พยายามจะดึงเมาส์ขึ้นมาตอนเรากดปุ่ม)
        if (!isJUnlocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Smoothly move the CameraTarget to the current active mesh position
        if (cameraTarget != null && currentMeshTransform != null)
        {
            cameraTarget.position = Vector3.Lerp(cameraTarget.position, currentMeshTransform.position, Time.deltaTime * 15f);
        }

        // Handle Camera Zoom via Mouse Scroll Wheel
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (isThirdPersonCamera && actualCameraTransform != null)
                {
                    // Positive scroll = zoom in (closer to 0)
                    targetZoomZ += scroll * zoomSensitivity;
                    targetZoomZ = Mathf.Clamp(targetZoomZ, maxZoomDistance, minZoomDistance);
                }
                else if (!isThirdPersonCamera && actualCameraComponent != null)
                {
                    // Positive scroll = zoom in (lower FOV)
                    targetFOV -= scroll * zoomSensitivity;
                    targetFOV = Mathf.Clamp(targetFOV, minFOV, maxFOV);
                }
            }
        }

        // Apply smooth zoom
        if (isThirdPersonCamera && actualCameraTransform != null)
        {
            Vector3 localPos = actualCameraTransform.localPosition;
            localPos.z = Mathf.Lerp(localPos.z, targetZoomZ, Time.deltaTime * zoomSmoothTime);
            actualCameraTransform.localPosition = localPos;
        }
        else if (!isThirdPersonCamera && actualCameraComponent != null)
        {
            actualCameraComponent.fieldOfView = Mathf.Lerp(actualCameraComponent.fieldOfView, targetFOV, Time.deltaTime * zoomSmoothTime);
        }

        if (!isJUnlocked && !isTyping)
        {
            Look();
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        // CHANGED (§12.3): phase-aware gate instead of just IsGameStarted.
        if (!CanLocalPlayerAct())
        {
            // Skip velocity write if the rigidbody is currently kinematic (e.g. monster locked
            // during SurvivorHide via TeleportAndLockClientRpc). Setting velocity on a kinematic
            // body throws a Unity warning every frame and is a no-op.
            if (rb != null && !rb.isKinematic)
                rb.velocity = new Vector3(0, rb.velocity.y, 0);
            return;
        }

        PlayerStateSync myState = GetComponent<PlayerStateSync>();
        bool isJUnlocked = myState != null && myState.IsCursorUnlocked;

        // Stop moving if menu is open or typing
        if ((GameMenuManager.Instance != null && GameMenuManager.Instance.isMenuOpen) || isTyping || isJUnlocked)
        {
            if (rb != null && !rb.isKinematic)
                rb.velocity = new Vector3(0, rb.velocity.y, 0);
            return;
        }

        Move();
    }

    [Header("Visual Body")]
    public Transform playerVisualBody;
    public float bodyRotationSpeed = 10f;

    private void Look()
    {
        // Use separate sensitivity per axis
        float lookX = lookInput.x * mouseSensitivityX * Time.deltaTime;
        float lookY = lookInput.y * mouseSensitivityY * Time.deltaTime;

        // หมุนทั้งตัวละครเสมอ กล้องจะได้ไปพร้อมกับตัวละคร
        transform.Rotate(Vector3.up * lookX);

        if (cameraTransform != null)
        {
            verticalLookRotation -= lookY;
            verticalLookRotation = Mathf.Clamp(verticalLookRotation, -90f, 90f);

            Vector3 camEuler = cameraTransform.localEulerAngles;
            camEuler.x = verticalLookRotation;
            cameraTransform.localEulerAngles = camEuler;
        }
    }

    private void Move()
    {
        Vector3 forward = cameraTransform != null ? cameraTransform.forward : transform.forward;
        Vector3 right = cameraTransform != null ? cameraTransform.right : transform.right;

        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = (forward * moveInput.y + right * moveInput.x).normalized;
        
        // เช็คว่ากด Shift วิ่งอยู่ และ ต้องเป็นการกดเดินหน้า (W) เท่านั้น
        bool isActuallySprinting = isSprinting && moveInput.y > 0;

        // CHANGED (§5.3 b): apply speed multiplier from PlayerPowerupReceiver (default 1.0,
        // becomes 1.5 for 5s when survivor grabs a speed orb).
        float multiplier = 1f;
        if (_powerupReceiver != null) multiplier = _powerupReceiver.SpeedMultiplier.Value;

        float currentSpeed = (isActuallySprinting ? sprintSpeed : moveSpeed) * multiplier;
        Vector3 targetVelocity = moveDirection * currentSpeed;

        rb.velocity = new Vector3(targetVelocity.x, rb.velocity.y, targetVelocity.z);

        if (playerVisualBody != null && moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            playerVisualBody.rotation = Quaternion.Slerp(playerVisualBody.rotation, targetRotation, Time.deltaTime * bodyRotationSpeed);
        }
    }
}