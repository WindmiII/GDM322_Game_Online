using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Scene NetworkObject singleton. Server-side only logic — no NetworkVariables of its own.
///
/// Subscribes lazily (only while RoundPhase.Active) to GameTimer.OnValueChanged.
/// During the last 60 seconds, spawns one orb every 15s (60, 45, 30, 15) at a random
/// pre-placed spawn point. On exit from Active, despawns any remaining orbs.
///
/// Single neutral orb prefab — effect is decided when grabbed, by the grabber's RoleIndex
/// (see PowerupOrb.cs).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PowerupSpawner : NetworkBehaviour
{
    public static PowerupSpawner Instance { get; private set; }

    [Header("Spawn Settings")]
    [Tooltip("Pre-placed empty Transforms in the map. The orb spawns at a random one of these.")]
    public Transform[] spawnPoints;

    [Tooltip("Orb prefab. Must have NetworkObject + PowerupOrb + Rigidbody(kinematic) + SphereCollider(isTrigger). " +
             "Register it in NetworkManager > NetworkPrefabsList.")]
    public GameObject orbPrefab;

    [Tooltip("Seconds between orb spawns during the last minute (15 = 60/45/30/15).")]
    public int spawnIntervalSec = 15;

    [Tooltip("Start spawning when GameTimer falls to this value (60 = last minute).")]
    public int spawnWindowStartSec = 60;

    private bool _subscribedRound = false;
    private bool _subscribedTimer = false;
    private readonly List<NetworkObject> _activeOrbs = new List<NetworkObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        // §12.8: don't subscribe to GameTimer here (scene NetworkObject spawn order isn't deterministic).
        // Instead, wait for RoundManager.CurrentPhase to enter Active.
        StartCoroutine(SubscribeRoundWhenReady());
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        UnsubscribeRound();
        UnsubscribeTimer();
        DespawnAllOrbs();
    }

    private System.Collections.IEnumerator SubscribeRoundWhenReady()
    {
        while (RoundManager.Instance == null) yield return null;
        RoundManager.Instance.CurrentPhase.OnValueChanged += OnPhaseChanged;
        _subscribedRound = true;
        // Apply current phase once in case Active is already running (e.g. host restarted scene mid-round).
        OnPhaseChanged(RoundPhase.Lobby, RoundManager.Instance.CurrentPhase.Value);
    }

    private void UnsubscribeRound()
    {
        if (!_subscribedRound) return;
        if (RoundManager.Instance != null)
            RoundManager.Instance.CurrentPhase.OnValueChanged -= OnPhaseChanged;
        _subscribedRound = false;
    }

    private void SubscribeTimer()
    {
        if (_subscribedTimer || GameTimeManager.Instance == null) return;
        GameTimeManager.Instance.GameTimer.OnValueChanged += OnGameTimerChanged;
        _subscribedTimer = true;
    }

    private void UnsubscribeTimer()
    {
        if (!_subscribedTimer) return;
        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.GameTimer.OnValueChanged -= OnGameTimerChanged;
        _subscribedTimer = false;
    }

    private void OnPhaseChanged(RoundPhase prev, RoundPhase next)
    {
        if (!IsServer) return;
        if (next == RoundPhase.Active)
        {
            SubscribeTimer();
        }
        else if (prev == RoundPhase.Active)
        {
            UnsubscribeTimer();
            DespawnAllOrbs();
        }
    }

    private void OnGameTimerChanged(int prev, int next)
    {
        if (!IsServer) return;

        // Spawn at 60, 45, 30, 15 (or generalized: every spawnIntervalSec down from spawnWindowStartSec).
        if (next > 0 && next <= spawnWindowStartSec && (spawnWindowStartSec - next) % spawnIntervalSec == 0)
        {
            SpawnOneOrb();
        }
    }

    private void SpawnOneOrb()
    {
        if (orbPrefab == null)
        {
            Debug.LogWarning("[PowerupSpawner] orbPrefab not assigned.");
            return;
        }
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[PowerupSpawner] No spawn points assigned.");
            return;
        }

        // Pick a random spawn point. (Could add "no-stack" filtering in v2 if orbs cluster.)
        int idx = Random.Range(0, spawnPoints.Length);
        Transform sp = spawnPoints[idx];
        if (sp == null) return;

        GameObject go = Instantiate(orbPrefab, sp.position, sp.rotation);
        var no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("[PowerupSpawner] Orb prefab missing NetworkObject component.");
            Destroy(go);
            return;
        }
        no.Spawn(true); // destroyWithScene = true
        _activeOrbs.Add(no);

        Debug.Log($"[PowerupSpawner] Spawned orb at {sp.name} (timer={GameTimeManager.Instance?.GameTimer.Value}s left).");
    }

    private void DespawnAllOrbs()
    {
        foreach (var no in _activeOrbs)
        {
            if (no != null && no.IsSpawned) no.Despawn(true);
        }
        _activeOrbs.Clear();
    }

    /// <summary>Called by PowerupOrb when it gets grabbed, so the spawner doesn't try to despawn it later.</summary>
    public void NotifyOrbConsumed(NetworkObject orb)
    {
        if (orb != null) _activeOrbs.Remove(orb);
    }
}
