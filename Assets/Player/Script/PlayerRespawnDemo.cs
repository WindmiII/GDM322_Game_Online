using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class PlayerRespawnDemo : NetworkBehaviour
{
    private Transform respawnPoint;
    
    private void Start()
    {
        GameObject point = GameObject.Find("ReSpawnPoint");
        if (point != null) { respawnPoint = point.transform; }
        else { Debug.LogError("ReSpawnPoint not found in scene."); }
    }

    public void RequestRespawn(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!context.performed) return;
        Debug.Log($"[Local] Respawn requested by client: {NetworkManager.Singleton.LocalClientId}");
        RequestRespawnServerRpc();
    }

    [ServerRpc]
    private void RequestRespawnServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (respawnPoint == null)
        {
            Debug.LogError("[Server] Respawn point is null.");
            return;
        }

        Debug.Log(
            $"[Server] Respawn request from clientId: {senderId}, " +
            $"object owner={OwnerClientId}, object={gameObject.name}"
        );
        
        ClientRpcParams targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { senderId }
            }
        };

        ApplyRespawnClientRpc(respawnPoint.position, targetParams);
    }

    private void StopMovementBeforeRespawn()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        MainPlayerScript mainPlayerScript = GetComponent<MainPlayerScript>();
        if (mainPlayerScript != null) mainPlayerScript.enabled = false;
    }
    
    [ClientRpc]
    private void ApplyRespawnClientRpc(Vector3 targetPosition, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        Debug.Log(
            $"[Client] Applying respawn on local client {NetworkManager.Singleton.LocalClientId}, " +
            $"owner={OwnerClientId}, target={targetPosition}"
        );
        
        StopMovementBeforeRespawn();
        transform.position = targetPosition;
        
        MainPlayerScript mainPlayerScript = GetComponent<MainPlayerScript>();
        if (mainPlayerScript != null) mainPlayerScript.enabled = true;
    }
}
