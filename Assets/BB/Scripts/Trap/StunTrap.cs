using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class StunTrap : MonoBehaviour
{
    [Header("Trap Settings")]
    public int damageAmount = 20;
    public float stunDuration = 3.0f;
    public string playerTag = "Player";
    public string enemyTag = "Enemy";

    [Header("Visuals & Cooldown")]
    public ParticleSystem triggerEffect;
    [Tooltip("ระยะเวลา Cooldown (0 = ทำงานครั้งเดียวแล้วจางหาย)")]
    public float cooldownTime = 0f; // <-- ทำให้ค่าเริ่มต้นเป็น 0 เพื่อเน้นการใช้ครั้งเดียว
    [Tooltip("ระยะเวลาที่ใช้ในการจางหายก่อนถูกลบ (วินาที)")]
    public float fadeDuration = 2.0f;
    [Tooltip("ชื่อ Layer ที่จะเปลี่ยนไปเมื่อเริ่มจาง (ต้องสร้าง Layer นี้เอง และเอาออกจาก LayerMask ใน InventoryHotbar)")]
    public string deactivatedLayerName = "DeactivatedItems"; // <-- ตั้งชื่อ Layer ที่จะใช้

    [Header("Prefab Swap")]
    public GameObject prefabAfterTrigger;   

    private Collider trapCollider;
    private bool isOnCooldown = false;
    private bool isDeactivating = false; // <-- สถานะใหม่: กำลังจางหรือไม่
    private Renderer itemRenderer; // <-- สำหรับการปรับสี/ความโปร่งใส
    private Material originalMaterial; // <-- เก็บ Material เดิม (ถ้าต้องการคืนค่า)
    private Color originalColor; // <-- เก็บสีเดิม

    // เพิ่ม Property สำหรับให้ Inventory เช็คได้
    public bool IsDeactivating => isDeactivating;

    void Awake()
    {
        trapCollider = GetComponent<Collider>();
        if (trapCollider != null)
        {
            trapCollider.isTrigger = true;
        }
        else
        {
            Debug.LogError("StunTrap needs a Collider component!", this.gameObject);
            this.enabled = false;
            return; // Exit Awake if no collider
        }

        // หา Renderer และเก็บสีเดิม (รองรับ MeshRenderer เป็นหลัก)
        itemRenderer = GetComponentInChildren<Renderer>(); // หาใน children เผื่อ model ซับซ้อน
         if (itemRenderer != null && itemRenderer.material != null)
         {
             // Important: Accessing .material creates an instance if not already unique.
             // This is usually desired for fading individual traps.
             originalMaterial = itemRenderer.material; // Keep reference to the potentially instanced material
             if(originalMaterial.HasProperty("_Color")) // Check if the standard color property exists
             {
                 originalColor = originalMaterial.color;
             }
             else if (originalMaterial.HasProperty("_BaseColor")) // Check for URP/HDRP base color
             {
                  originalColor = originalMaterial.GetColor("_BaseColor");
             }
             else
             {
                 Debug.LogWarning("Material on trap does not have a standard '_Color' or '_BaseColor' property for fading.", gameObject);
                 itemRenderer = null; // Cannot fade if color property is unknown
             }
         }
         else
         {
              Debug.LogWarning("Could not find Renderer or Material on trap for fading.", gameObject);
              itemRenderer = null; // Mark as null if not found
         }
    }

    void OnTriggerEnter(Collider other)
    {
        // ถ้ากำลังจาง หรือ Cooldown อยู่ ก็ไม่ต้องทำอะไร
        if (isDeactivating || isOnCooldown)
        {
            return;
        }

        // (โค้ดตรวจสอบ Player/Enemy เหมือนเดิม)
        if (other.CompareTag(playerTag))
        {
            PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                ApplyTrapEffects(other.gameObject, playerHealth, null, null); // Pass null for enemy components
            }
        }
        else if (other.CompareTag(enemyTag))
        {
            EnemyAi enemyAi = other.GetComponent<EnemyAi>();
            NavMeshAgent enemyAgent = other.GetComponent<NavMeshAgent>();
            if (enemyAi != null && enemyAgent != null)
            {
                ApplyTrapEffects(other.gameObject, null, enemyAgent, enemyAi); // Pass null for playerHealth
            }
        }
    }

    void ApplyTrapEffects(GameObject targetObject, PlayerHealth playerHealth, NavMeshAgent enemyAgent, EnemyAi enemyAi)
    {
        Debug.Log($"{targetObject.name} triggered the trap!");

        if (playerHealth != null) playerHealth.TakeDamage(damageAmount);
        if (enemyAi != null) enemyAi.TakeDamage(damageAmount);

        // เริ่ม stun (ยังต้องปล่อยให้ Coroutine รันก่อน)
        StartCoroutine(StunTarget(enemyAgent, targetObject.GetComponent<Rigidbody>()));

        // เล่น effect
        if (triggerEffect != null) triggerEffect.Play();

        // สร้าง prefab หลังจากโดน
        if (prefabAfterTrigger != null)
        {
            Instantiate(prefabAfterTrigger, transform.position, transform.rotation);
        }

        // ❗❗ ปิดการทำงานก่อน (อย่าเพิ่ง Destroy)
        DisableTrapVisual();

        // ถ้ามี Fade ใช้ Fade (Destroy หลังจบ)
        if (cooldownTime <= 0)
        {
            StartCoroutine(FadeAndDestroy(fadeDuration));
        }

        // ถ้าเป็น trap ใช้ซ้ำ มี cooldown
        else
        {
            StartCoroutine(StartCooldown());
        }
    }
    void DisableTrapVisual()
    {
        if (trapCollider) trapCollider.enabled = false;

        // ปิด Renderer เพื่อไม่ให้กับดักเก่าค้างบนพื้น
        if (itemRenderer) itemRenderer.enabled = false;
    }

    // ใน StunTrap.cs
    IEnumerator StunTarget(NavMeshAgent agentToStop, Rigidbody playerRb)
    {
        GameObject targetObject = null; // เก็บ GameObject เป้าหมาย
        bool originallyEnabledAgent = false; // เก็บสถานะเดิมของ Agent

        // --- Stun Logic ---
        if (agentToStop != null) // Stun Enemy
        {
            targetObject = agentToStop.gameObject; // เก็บ GameObject ของ Enemy
            if (agentToStop.enabled)
            {
                originallyEnabledAgent = true;
                try
                {
                    agentToStop.enabled = false; // Disable NavMeshAgent
                    Debug.Log($"Enemy {targetObject.name} stunned.");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error disabling NavMeshAgent on {targetObject.name}: {e.Message}");
                    yield break; // หยุด Coroutine ถ้า disable ไม่ได้
                }
            }
        }
        else if (playerRb != null) // Stun Player
        {
            targetObject = playerRb.gameObject; // เก็บ GameObject ของ Player
            playerRb.constraints = RigidbodyConstraints.FreezeAll;
            Debug.Log($"Player {targetObject.name} stunned.");
        }
        else
        {
            yield break; // ไม่มีเป้าหมายที่ถูกต้อง
        }

        // --- Wait for Stun Duration ---
        yield return new WaitForSeconds(stunDuration);

        // --- Un-stun Logic ---
        // 💡 FIX: ตรวจสอบว่า GameObject เป้าหมายยังคงอยู่หรือไม่ ก่อนพยายามยกเลิก Stun
        if (targetObject == null)
        {
            Debug.Log("Target was destroyed during stun.");
            yield break; // ออกจาก Coroutine ถ้าเป้าหมายถูกทำลายไปแล้ว
        }

        if (agentToStop != null && originallyEnabledAgent) // Un-stun Enemy
        {
            // ตรวจสอบอีกครั้งเผื่อ Component หายไป
            NavMeshAgent currentAgent = targetObject.GetComponent<NavMeshAgent>();
            if (currentAgent != null)
            {
                 try
                 {
                    currentAgent.enabled = true; // Re-enable NavMeshAgent
                    Debug.Log($"Enemy {targetObject.name} recovered from stun.");
                 }
                 catch (System.Exception e)
                 {
                     Debug.LogError($"Error re-enabling NavMeshAgent on {targetObject.name}: {e.Message}");
                 }
            }
             else {
                 Debug.LogWarning($"NavMeshAgent component lost on {targetObject.name} during stun.");
             }
        }
        else if (playerRb != null) // Un-stun Player
        {
             // ตรวจสอบอีกครั้งเผื่อ Component หายไป
             Rigidbody currentPlayerRb = targetObject.GetComponent<Rigidbody>();
             if(currentPlayerRb != null)
             {
                 // คืนค่า Constraints (สำคัญ: ต้องตั้งค่าให้เหมือนเดิมก่อนโดน Stun)
                 // ตัวอย่าง: ถ้าปกติ Freeze Rotation ไว้ ก็ต้อง Freeze กลับไป
                 currentPlayerRb.constraints = RigidbodyConstraints.FreezeRotation;
                 Debug.Log($"Player {targetObject.name} recovered from stun.");
             }
              else {
                 Debug.LogWarning($"Rigidbody component lost on {targetObject.name} during stun.");
             }
        }
    }

    IEnumerator StartCooldown()
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(cooldownTime);
        isOnCooldown = false;
    }

    // 💡 NEW: Coroutine สำหรับจางหายและทำลายตัวเอง
    IEnumerator FadeAndDestroy(float duration)
    {
        isDeactivating = true; // ตั้งสถานะว่ากำลังจาง
        trapCollider.enabled = false; // ปิด Trigger ทันที ป้องกันการทำงานซ้ำ
        Debug.Log($"Trap {gameObject.name} activated and starting to fade.");

        // เปลี่ยน Layer เพื่อป้องกันการเก็บ
        int targetLayer = LayerMask.NameToLayer(deactivatedLayerName);
        if (targetLayer != -1) // Layer exists
        {
            gameObject.layer = targetLayer;
            // Optional: Change layer of children too if needed
            // foreach (Transform child in transform) { child.gameObject.layer = targetLayer; }
        }
        else
        {
            Debug.LogWarning($"Layer '{deactivatedLayerName}' not found. Please create it in Project Settings -> Tags and Layers.", gameObject);
        }


        // กระบวนการจาง (ถ้ามี Renderer และ Material)
        if (itemRenderer != null && originalMaterial != null)
        {
            float elapsedTime = 0f;
            Color currentColor = originalColor; // Start with original color

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                // คำนวณ alpha ใหม่ (ลดลงตามเวลา)
                currentColor.a = Mathf.Lerp(originalColor.a, 0f, elapsedTime / duration);

                 // ตั้งค่าสีกลับไปที่ Material
                 if(originalMaterial.HasProperty("_Color"))
                 {
                     originalMaterial.color = currentColor;
                 }
                 else if (originalMaterial.HasProperty("_BaseColor")) // For URP/HDRP Lit shaders
                 {
                      originalMaterial.SetColor("_BaseColor", currentColor);
                 }


                yield return null; // รอเฟรมถัดไป
            }
        }
        else
        {
            // ถ้าไม่มี Renderer/Material ก็รอเวลาตามปกติ
            yield return new WaitForSeconds(duration);
        }

        // ทำลาย GameObject ทิ้ง
        Debug.Log($"Trap {gameObject.name} faded out and destroyed.");
        Destroy(gameObject);
    }
}