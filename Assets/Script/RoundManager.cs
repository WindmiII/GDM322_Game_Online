using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum RoundPhase
{
    Lobby = 0,
    MonsterPreview = 1,
    SurvivorHide = 2,
    Active = 3,
    Ended = 4
}

[RequireComponent(typeof(NetworkObject))]
public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }

    [Header("Phase Durations (seconds)")]
    public int previewDurationSec = 30;
    public int hideDurationSec = 30;

    [Header("Monster Lock Position")]
    public Vector3 monsterLockPosition = new Vector3(0f, -10f, 0f);

    public NetworkVariable<RoundPhase> CurrentPhase = new NetworkVariable<RoundPhase>(
        RoundPhase.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> PhaseTimer = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<NetworkObjectReference> MonsterPlayerRef = new NetworkVariable<NetworkObjectReference>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Vector3 _monsterOriginalSpawn = Vector3.zero;
    private Quaternion _monsterOriginalRotation = Quaternion.identity;
    private Coroutine _tickCoroutine;
    private bool _subscribedToGameTimer = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[RoundManager] Awake — Instance set.");
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[RoundManager] OnNetworkSpawn (IsServer={IsServer}).");
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (_tickCoroutine != null) { StopCoroutine(_tickCoroutine); _tickCoroutine = null; }
            UnsubscribeFromGameTimer();
        }
    }

    public void BeginRound()
    {
        Debug.Log($"[RoundManager] BeginRound called (IsServer={IsServer}).");
        if (!IsServer) { Debug.LogWarning("[RoundManager] BeginRound on non-server, ignored."); return; }
        if (CurrentPhase.Value != RoundPhase.Lobby)
        {
            Debug.LogWarning($"[RoundManager] BeginRound but phase already {CurrentPhase.Value}. Ignored.");
            return;
        }

        NetworkObject monsterObj = null;
        int connectedCount = 0;
        foreach (var kv in NetworkManager.Singleton.ConnectedClients)
        {
            connectedCount++;
            var po = kv.Value.PlayerObject;
            if (po == null) { Debug.LogWarning($"[RoundManager] Client {kv.Key} has no PlayerObject."); continue; }
            var state = po.GetComponent<PlayerStateSync>();
            int role = state != null ? state.RoleIndex.Value : -1;
            Debug.Log($"[RoundManager] Scanning client {kv.Key}: role={role}");
            if (state != null && state.RoleIndex.Value == 1)
            {
                monsterObj = po;
                break;
            }
        }
        Debug.Log($"[RoundManager] Connected clients scanned: {connectedCount}. monsterObj={(monsterObj != null ? monsterObj.name : "NULL")}");

        if (LobbyManager.Instance != null)
            LobbyManager.Instance.IsGameStarted.Value = true;

        if (monsterObj == null)
        {
            Debug.LogWarning("[RoundManager] No monster found. Jumping straight to Active.");
            MonsterPlayerRef.Value = default;
            TransitionTo(RoundPhase.Active);
            return;
        }

        _monsterOriginalSpawn = monsterObj.transform.position;
        _monsterOriginalRotation = monsterObj.transform.rotation;
        MonsterPlayerRef.Value = new NetworkObjectReference(monsterObj);

        Debug.Log($"[RoundManager] Monster found: clientId={monsterObj.OwnerClientId} originalSpawn={_monsterOriginalSpawn}");

        TransitionTo(RoundPhase.MonsterPreview);
    }

    private void TransitionTo(RoundPhase next)
    {
        if (!IsServer) return;

        RoundPhase prev = CurrentPhase.Value;
        Debug.Log($"[RoundManager] >>> Transition: {prev} -> {next}");

        if (prev == RoundPhase.Active) UnsubscribeFromGameTimer();

        CurrentPhase.Value = next;

        if (_tickCoroutine != null) { StopCoroutine(_tickCoroutine); _tickCoroutine = null; }

        switch (next)
        {
            case RoundPhase.MonsterPreview:
                PhaseTimer.Value = previewDurationSec;
                Debug.Log($"[RoundManager] Starting MonsterPreview tick: PhaseTimer={PhaseTimer.Value}");
                _tickCoroutine = StartCoroutine(TickPhaseTimer(RoundPhase.SurvivorHide));
                break;

            case RoundPhase.SurvivorHide:
                PhaseTimer.Value = hideDurationSec;
                Debug.Log($"[RoundManager] Starting SurvivorHide tick: PhaseTimer={PhaseTimer.Value}. Teleporting monster to {monsterLockPosition}.");
                TeleportMonster(monsterLockPosition, locked: true);
                _tickCoroutine = StartCoroutine(TickPhaseTimer(RoundPhase.Active));
                break;

            case RoundPhase.Active:
                PhaseTimer.Value = 0;
                Debug.Log($"[RoundManager] Entering Active. Teleporting monster back to {_monsterOriginalSpawn}.");
                TeleportMonster(_monsterOriginalSpawn, locked: false);
                SubscribeToGameTimer();
                break;

            case RoundPhase.Ended:
                PhaseTimer.Value = 0;
                UnsubscribeFromGameTimer();
                Debug.Log("[RoundManager] Round ended.");
                break;
        }
    }

    private IEnumerator TickPhaseTimer(RoundPhase nextPhaseWhenZero)
    {
        Debug.Log($"[RoundManager] TickPhaseTimer coroutine STARTED. PhaseTimer={PhaseTimer.Value} -> goal {nextPhaseWhenZero}");
        while (PhaseTimer.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            PhaseTimer.Value = Mathf.Max(0, PhaseTimer.Value - 1);
            if (PhaseTimer.Value % 5 == 0)
                Debug.Log($"[RoundManager] PhaseTimer tick = {PhaseTimer.Value}");
        }
        Debug.Log($"[RoundManager] TickPhaseTimer reached 0 -> Transition({nextPhaseWhenZero})");
        TransitionTo(nextPhaseWhenZero);
    }

    private void TeleportMonster(Vector3 pos, bool locked)
    {
        if (!MonsterPlayerRef.Value.TryGet(out NetworkObject monsterObj))
        {
            Debug.LogWarning("[RoundManager] TeleportMonster: monster ref invalid.");
            return;
        }
        var receiver = monsterObj.GetComponent<PlayerPowerupReceiver>();
        if (receiver == null)
        {
            Debug.LogWarning("[RoundManager] TeleportMonster: monster has no PlayerPowerupReceiver component.");
            return;
        }
        var rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { monsterObj.OwnerClientId } }
        };
        receiver.TeleportAndLockClientRpc(pos, locked, rpcParams);
    }

    private void SubscribeToGameTimer()
    {
        if (!IsServer || _subscribedToGameTimer) return;
        if (GameTimeManager.Instance == null)
        {
            Debug.LogWarning("[RoundManager] SubscribeToGameTimer: GameTimeManager.Instance is null.");
            return;
        }
        GameTimeManager.Instance.GameTimer.OnValueChanged += OnGameTimerChanged;
        _subscribedToGameTimer = true;
    }

    private void UnsubscribeFromGameTimer()
    {
        if (!_subscribedToGameTimer) return;
        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.GameTimer.OnValueChanged -= OnGameTimerChanged;
        _subscribedToGameTimer = false;
    }

    private void OnGameTimerChanged(int oldValue, int newValue)
    {
        if (!IsServer) return;
        if (newValue <= 0 && CurrentPhase.Value == RoundPhase.Active)
        {
            Debug.Log("[RoundManager] Main timer expired -> survivors win.");
            TransitionTo(RoundPhase.Ended);
        }
    }
}