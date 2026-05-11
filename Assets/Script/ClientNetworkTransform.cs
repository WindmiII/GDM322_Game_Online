using Unity.Netcode.Components;
using UnityEngine;

// This script allows the Client to move their own character and send the position to others
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    // Override to indicate that the Server is not in control, but the Owner is in control
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}