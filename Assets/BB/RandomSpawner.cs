using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomSpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public string name;
        public GameObject prefab;
        [Tooltip("ใส่ค่าน้ำหนักเท่าไหร่ก็ได้ (ยิ่งเยอะยิ่งออกง่าย เมื่อเทียบกับชิ้นอื่น)")]
        public float weight;
    }

    [Header("Empty Chance Settings")]
    [Range(0, 100)]
    [Tooltip("โอกาสที่จะเป็น 'ช่องว่าง' (ไม่เกิดอะไรเลย) คิดเป็น %")]
    public float emptyChancePercentage = 20f; // ตั้งค่าเริ่มต้นไว้ 20%

    [Header("Loot Table")]
    [Tooltip("รายการของที่จะสุ่ม (น้ำหนักเทียบกันเองในกลุ่ม)")]
    public List<SpawnEntry> lootTable;

    [Header("Spawn Points")]
    public List<Transform> spawnPoints;

    [Header("Settings")]
    public bool spawnOnStart = true;
    public bool randomRotationY = true;

    void Start()
    {
        if (spawnOnStart) SpawnAll();
    }

    public void SpawnAll()
    {
        foreach (Transform point in spawnPoints)
        {
            SpawnItemAt(point);
        }
    }

    void SpawnItemAt(Transform point)
    {
        // --- ขั้นตอนที่ 1: เช็คโอกาสว่าง (Empty Check) ---
        // สุ่ม 0-100 ถ้าค่าน้อยกว่าที่ตั้งไว้ = ว่าง
        if (Random.Range(0f, 100f) < emptyChancePercentage)
        {
            return; // จบงาน ไม่เกิดอะไรขึ้น
        }

        // --- ขั้นตอนที่ 2: สุ่มของตามน้ำหนัก (Weighted Random) ---

        // 2.1 หาผลรวมน้ำหนักของของทั้งหมด
        float totalWeight = 0f;
        foreach (var entry in lootTable)
        {
            if (entry.prefab != null) totalWeight += entry.weight;
        }

        // ถ้าไม่มีของใน List เลย ก็จบ
        if (totalWeight <= 0) return;

        // 2.2 สุ่มตัวเลขจาก 0 ถึง ผลรวมน้ำหนัก
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        foreach (SpawnEntry entry in lootTable)
        {
            if (entry.prefab == null) continue;

            currentWeight += entry.weight;

            // ถ้าตกในช่วงของชิ้นนี้
            if (randomValue <= currentWeight)
            {
                // กำหนด Rotation
                Quaternion rotation = point.rotation;
                if (randomRotationY)
                {
                    rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                }

                // เสกของ
                Instantiate(entry.prefab, point.position, rotation);
                return; // ได้ของแล้ว จบ
            }
        }
    }

    void OnDrawGizmos()
    {
        if (spawnPoints == null) return;
        Gizmos.color = Color.yellow;
        foreach (Transform point in spawnPoints)
        {
            if (point != null) Gizmos.DrawSphere(point.position, 0.3f);
        }
    }
}