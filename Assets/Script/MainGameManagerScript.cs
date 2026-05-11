using UnityEngine;
using Unity.Netcode;

public class MainGameManagerScript : MonoBehaviour
{
    private void Start()
    {
        // Register network events to handle connection state
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public void OnServerButtonClick()
    {
        Debug.Log("Starting Server");
        NetworkManager.Singleton.StartServer();
    }

    public void OnHostButtonClick()
    {
        Debug.Log("Starting Host");
        NetworkManager.Singleton.StartHost();
    }

    public void OnClientButtonClick()
    {
        Debug.Log("Starting Client");
        NetworkManager.Singleton.StartClient();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected successfully");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}
