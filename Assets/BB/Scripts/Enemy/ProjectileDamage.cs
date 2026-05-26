using UnityEngine;

public class ProjectileDamage : MonoBehaviour
{
    [Tooltip("จำนวนดาเมจที่กระสุนนี้ทำได้")]
    public int damageAmount = 10;

    [Tooltip("แท็กของ GameObject ที่เป็นผู้เล่น")]
    public string playerTag = "Player"; // ตรวจสอบให้แน่ใจว่า Player ของคุณมี Tag นี้

    [Tooltip("Particle Effect หรือ Prefab ที่จะแสดงเมื่อชน (ถ้ามี)")]
    public GameObject hitEffectPrefab;

    [Tooltip("ระยะเวลาที่กระสุนจะคงอยู่ก่อนถูกทำลาย (ถ้าไม่ชนอะไรเลย)")]
    public float lifetime = 5.0f;

    void Start()
    {
        // ทำลายตัวเองหลังจากผ่านไปตามเวลา lifetime
        Destroy(gameObject, lifetime);
    }

    void OnCollisionEnter(Collision collision)
    {
        // 1. ตรวจสอบว่าชนกับผู้เล่นหรือไม่ (เช็คจาก Tag)
        if (collision.gameObject.CompareTag(playerTag))
        {
            // 2. พยายามหา Component จัดการเลือดของผู้เล่น (เราจะสร้างสคริปต์นี้ในขั้นตอนถัดไป)
            PlayerHealth playerHealth = collision.gameObject.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                // 3. ถ้าเจอ ให้สั่งให้ผู้เล่นรับดาเมจ
                playerHealth.TakeDamage(damageAmount);
                Debug.Log($"Projectile hit player for {damageAmount} damage.");
            }
            else
            {
                Debug.LogWarning("Projectile hit object with 'Player' tag, but couldn't find PlayerHealth component.");
            }
        }

        // แสดงเอฟเฟกต์ (ถ้ามี) ณ จุดที่ชน
        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, collision.contacts[0].point, Quaternion.LookRotation(collision.contacts[0].normal));
        }

        // 4. ทำลายกระสุนทิ้งหลังจากชน
        Destroy(gameObject);
    }
}