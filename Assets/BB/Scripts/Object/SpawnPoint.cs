using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SpawnType
{
    Small,
    Large,
    Both
}

public class SpawnPoint : MonoBehaviour
{
    public SpawnType spawnType = SpawnType.Small;

    [Header("Spawn Radius (For Gizmos Only)")]
    public float spawnRadius = 0.3f;

    [HideInInspector]
    public bool hasSpawned = false;

    private void OnDrawGizmos()
    {
        // สีตามประเภท
        switch (spawnType)
        {
            case SpawnType.Small:
                Gizmos.color = Color.green;
                break;
            case SpawnType.Large:
                Gizmos.color = Color.red;
                break;
            case SpawnType.Both:
                Gizmos.color = Color.yellow;
                break;
        }

        // วาดวงกลมพื้น
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        // แสดงชื่อบน Scene
#if UNITY_EDITOR
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f,
            spawnType.ToString() + (hasSpawned ? " (Used)" : ""));
#endif
    }
}