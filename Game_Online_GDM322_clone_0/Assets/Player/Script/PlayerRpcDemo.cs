using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class PlayerRpcDemo : NetworkBehaviour
{
    // 1. NetworkVariable to track interaction counts across the network
    private NetworkVariable<int> interactCount = new NetworkVariable<int>(0);

    // Subscribe to the value changed event when enabled
    private void OnEnable()
    {
        interactCount.OnValueChanged += OnInteractCountChanged;
    }

    // Unsubscribe to prevent memory leaks when disabled
    private void OnDisable()
    {
        interactCount.OnValueChanged -= OnInteractCountChanged;
    }

    // --- NEW ADDITION ---
    // Called when the NetworkObject associated with this NetworkBehaviour is spawned
    public override void OnNetworkSpawn()
    {
        Debug.Log(
            $"[Spawn State] Local machine clientId={NetworkManager.Singleton.LocalClientId}, " +
            $"object owner={OwnerClientId}, current interactCount={interactCount.Value}, object={gameObject.name}"
        );
    }

    // Callback that fires locally whenever the NetworkVariable changes on the server
    private void OnInteractCountChanged(int oldValue, int newValue)
    {
        Debug.Log(
            $"[State] Local machine clientId={NetworkManager.Singleton.LocalClientId}, " +
            $"object owner={OwnerClientId}, interact count changed: {oldValue} -> {newValue}"
        );
    }

    public void PingServer(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!context.performed) return;
        Debug.Log($"[Local] Interact pressed by client: {NetworkManager.Singleton.LocalClientId}");
        SendPingServerRpc();
    }

    [ServerRpc]
    private void SendPingServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        
        Debug.Log(
            $"[Server] Received request from clientId: {senderId}, " +
            $"this object owner={OwnerClientId}, object={gameObject.name}"
        );

        // Increment the NetworkVariable (this syncs to clients and triggers OnInteractCountChanged)
        interactCount.Value++;
        Debug.Log(
            $"[Server] interactCount for owner {OwnerClientId} is now: {interactCount.Value}"
        );

        // Configure the parameters to only target the sender
        ClientRpcParams targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { senderId }
            }
        };
        
        // Execute the targeted ClientRpc
        ShowTargetedResponseClientRpc(targetParams);
    }

    [ClientRpc]
    private void ShowTargetedResponseClientRpc(ClientRpcParams clientRpcParams = default)
    {
        Debug.Log(
            $"[Client] Local machine clientId={NetworkManager.Singleton.LocalClientId}, " +
            $"this object owner={OwnerClientId}, object name={gameObject.name}"
        );
    }
}