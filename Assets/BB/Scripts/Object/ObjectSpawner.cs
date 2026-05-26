using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    public List<GameObject> smallPrefabs;
    public List<GameObject> largePrefabs;

    [Header("Spawn Points")]
    public List<SpawnPoint> spawnPoints;

    [Header("Spawn Settings")]
    public float spawnInterval = 3f;

    [Header("Value Control")]
    public float maxTotalSpawnValue = 500f;
    private float currentSpawnValue = 0f;

    [Header("Required Quest Items")]
    public List<string> requiredItemIDs = new List<string>(); // Set จาก ObjectiveManager

    private float timer;

    // 🔥 ตัวนี้เอาไว้หยุด Spawn ทั้งหมด เมื่อเกิด value limit หรือ spawn ครบ
    private bool stopSpawning = false;


    private void Update()
    {
        if (stopSpawning) return;  // ❌ หยุดระบบ Spawn ไปเลย

        timer += Time.deltaTime;

        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnOneObject();
        }
    }

    void SpawnOneObject()
    {
        // หา SpawnPoint ที่ยังไม่ spawn
        List<SpawnPoint> availablePoints = spawnPoints.FindAll(p => !p.hasSpawned);

        if (availablePoints.Count == 0)
        {
            Debug.Log("✅ All SpawnPoints finished spawning.");
            stopSpawning = true;   // ไม่มีจุดเหลือ → หยุดระบบ
            return;
        }

        SpawnPoint point = availablePoints[Random.Range(0, availablePoints.Count)];

        GameObject prefabToSpawn = SelectPrefabConsideringQuest(point);
        if (prefabToSpawn == null)
        {
            Debug.Log("⚠ ไม่มี prefab ให้ spawn หรือถูก value limit บล็อก");
            return;
        }

        float prefabValue = GetPrefabValue(prefabToSpawn);

        // ถ้าเกิน limit → หยุด system
        if (currentSpawnValue + prefabValue > maxTotalSpawnValue)
        {
            Debug.Log("❌ Cancel Spawn: Value limit exceeded");
            stopSpawning = true;   // 🔥 หยุดระบบ Spawn ถาวรในรอบนี้
            return;
        }

        // Spawn
        Instantiate(prefabToSpawn, point.transform.position, point.transform.rotation);

        // Update value
        currentSpawnValue += prefabValue;

        // Mark ว่าจุดนี้ spawn ไปแล้ว
        point.hasSpawned = true;

        Debug.Log($"Spawned {prefabToSpawn.name} | Value: {prefabValue} | Total: {currentSpawnValue}");
    }

    // เลือก prefab โดยคำนึงถึง quest ก่อน
    GameObject SelectPrefabConsideringQuest(SpawnPoint point)
    {
        // 1) บังคับ spawn quest item ถ้าจำเป็น
        for (int i = 0; i < requiredItemIDs.Count; i++)
        {
            string id = requiredItemIDs[i];

            GameObject questPrefab = FindPrefabByItemID(id);
            if (questPrefab != null)
            {
                float v = GetPrefabValue(questPrefab);

                if (currentSpawnValue + v <= maxTotalSpawnValue)
                {
                    requiredItemIDs.RemoveAt(i);
                    return questPrefab;
                }
            }
        }

        // 2) ถ้าไม่มี quest item → spawn ตาม spawnType
        return GetPrefabFromSpawnType(point.spawnType);
    }

    float GetPrefabValue(GameObject prefab)
    {
        var drag = prefab.GetComponent<DragRigidbody>();
        return drag != null ? drag.startValue : 0f;
    }

    GameObject FindPrefabByItemID(string id)
    {
        var list = smallPrefabs.Concat(largePrefabs).ToList();
        foreach (var p in list)
        {
            var drag = p.GetComponent<DragRigidbody>();
            if (drag != null && drag.itemID == id)
                return p;
        }
        return null;
    }

    GameObject GetPrefabFromSpawnType(SpawnType type)
    {
        switch (type)
        {
            case SpawnType.Small: return GetRandomPrefab(smallPrefabs);
            case SpawnType.Large: return GetRandomPrefab(largePrefabs);
            case SpawnType.Both:
                bool chooseLarge = Random.Range(0, 2) == 0;
                return chooseLarge ? GetRandomPrefab(largePrefabs) : GetRandomPrefab(smallPrefabs);
        }
        return null;
    }

    GameObject GetRandomPrefab(List<GameObject> list)
    {
        if (list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }
}