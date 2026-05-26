using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("Spawn Points")]
    public List<Transform> spawnPoints = new List<Transform>();  // จุดเกิด

    [Header("Enemy Prefabs")]
    public List<GameObject> enemyPrefabs = new List<GameObject>(); // ตัวเลือกศัตรู

    [Header("Spawn Settings")]
    public float respawnCooldown = 5f;  // เวลารอเกิดใหม่
    private GameObject currentEnemy;    // ตัวที่เกิดอยู่ตอนนี้

    private bool isSpawning = false;

    private void Start()
    {
        SpawnEnemy();
    }

    private void Update()
    {
        // ถ้าไม่มีศัตรูอยู่ในฉาก → เริ่ม Respawn
        if (currentEnemy == null && !isSpawning)
        {
            StartCoroutine(RespawnTimer());
        }
    }

    IEnumerator RespawnTimer()
    {
        isSpawning = true;
        yield return new WaitForSeconds(respawnCooldown);
        SpawnEnemy();
        isSpawning = false;
    }

    void SpawnEnemy()
    {
        if (spawnPoints.Count == 0 || enemyPrefabs.Count == 0)
        {
            Debug.LogError("❌ No spawn points or enemy prefabs assigned!");
            return;
        }

        // สุ่มจุดเกิด
        Transform spawn = spawnPoints[Random.Range(0, spawnPoints.Count)];

        // สุ่ม Prefab ศัตรู
        GameObject enemyPrefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];

        // สร้างศัตรู
        currentEnemy = Instantiate(enemyPrefab, spawn.position, spawn.rotation);

        Debug.Log("✔ Spawned enemy at: " + spawn.name);
        LevelObjectiveManager.Instance.ReassignBackupDrops();
    }
}
