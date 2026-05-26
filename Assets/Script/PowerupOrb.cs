using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sits on the orb prefab. The prefab also needs: NetworkObject, Rigidbody (kinematic),
/// SphereCollider (isTrigger = true), and some visual (MeshRenderer with an emissive material).
///
/// Server-only detection: although OnTriggerEnter fires on all clients (the orb is replicated),
/// only the server's instance acts on it. Effect is routed by the grabber's RoleIndex:
///   - Monster (1) grabs: every survivor's PlayerPowerupReceiver runs RevealClientRpc, targeted
///                        at the monster's client only -> only the monster sees survivors glow.
///   - Survivor (0) grabs: the grabber's own PlayerPowerupReceiver.ApplySpeedBoost runs server-side
///                         (SpeedMultiplier replicates to everyone, but only the owner applies it
///                          in Move() because Move() is IsOwner-gated).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(SphereCollider))]
public class PowerupOrb : NetworkBehaviour
{
    [Header("Monster Effect (when monster grabs)")]
    [Tooltip("Seconds the survivors glow on the monster's screen.")]
    public float monsterRevealDuration = 5f;

    [Header("Survivor Effect (when survivor grabs)")]
    [Tooltip("Movement speed multiplier (1.5 = +50%).")]
    public float survivorSpeedMultiplier = 1.5f;
    [Tooltip("Seconds the speed boost lasts.")]
    public float survivorSpeedDuration = 5f;

    [Header("Visual")]
    [Tooltip("Optional rotation speed (degrees/sec) for visual flair. Set 0 to disable.")]
    public float spinDegreesPerSecond = 90f;

    private bool _grabbed = false;

    private void Update()
    {
        if (spinDegreesPerSecond != 0f)
        {
            transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Server-only resolution. Other clients also fire OnTriggerEnter on their local replica
        // but their early-return here means they don't act on it.
        if (!IsServer) return;
        if (_grabbed) return;

        // Resolve the player that walked into us.
        var state = other.GetComponentInParent<PlayerStateSync>();
        if (state == null) state = other.transform.root.GetComponentInChildren<PlayerStateSync>();
        if (state == null) return;

        var receiver = state.GetComponent<PlayerPowerupReceiver>();
        if (receiver == null)
        {
            Debug.LogWarning("[PowerupOrb] Grabber has no PlayerPowerupReceiver component.");
            return;
        }

        _grabbed = true;
        ResolveGrab(state, receiver);
    }

    private void ResolveGrab(PlayerStateSync grabberState, PlayerPowerupReceiver grabberReceiver)
    {
        int role = grabberState.RoleIndex.Value;

        if (role == 1) // Monster grabbed -> reveal survivors (visible only to monster)
        {
            ulong monsterClientId = grabberState.OwnerClientId;
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { monsterClientId } }
            };

            // Iterate ALL players, apply reveal to every survivor's receiver, targeted to monster.
            foreach (var kv in NetworkManager.Singleton.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                if (po == null) continue;
                var s = po.GetComponent<PlayerStateSync>();
                if (s == null || s.RoleIndex.Value != 0) continue; // survivors only
                var r = po.GetComponent<PlayerPowerupReceiver>();
                if (r == null) continue;
                r.RevealClientRpc(monsterRevealDuration, rpcParams);
            }
            Debug.Log($"[PowerupOrb] Monster grabbed: reveal triggered on monster client {monsterClientId} for {monsterRevealDuration}s.");
        }
        else if (role == 0) // Survivor grabbed -> speed boost
        {
            grabberReceiver.ApplySpeedBoost(survivorSpeedMultiplier, survivorSpeedDuration);
            Debug.Log($"[PowerupOrb] Survivor grabbed: speed x{survivorSpeedMultiplier} for {survivorSpeedDuration}s.");
        }

        // Tell the spawner this orb is gone, then despawn.
        if (PowerupSpawner.Instance != null)
            PowerupSpawner.Instance.NotifyOrbConsumed(NetworkObject);

        NetworkObject.Despawn(true);
    }
}
