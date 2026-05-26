using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryHotbar : MonoBehaviour
{
    [Header("Hotbar Settings")]
    [SerializeField] private Transform handTransform; // จุดถือของที่มือ/กล้อง
    [SerializeField] private float pickupRange = 3f;
    [SerializeField] private LayerMask itemLayer;

    [Header("UI References")]
    // เปลี่ยนจาก Image[] เดียว เป็นแยก 2 ส่วน
    [Tooltip("ลาก GameObject ตัวแม่ที่เป็นรูปกรอบช่องมาใส่ตรงนี้")]
    [SerializeField] private Image[] slotBackgrounds = new Image[3]; 

    [Tooltip("ลาก GameObject ตัวลูก (Image เปล่าๆ) ที่เอาไว้โชว์ไอเทมมาใส่ตรงนี้")]
    [SerializeField] private Image[] itemIcons = new Image[3];       

    [Header("Colors")]
    [SerializeField] private Color selectedColor = Color.yellow; // สีของกรอบตอนเลือก
    [SerializeField] private Color normalColor = Color.white;    // สีของกรอบปกติ

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    private GameObject[] inventory = new GameObject[3]; // เก็บของ 3 ช่อง
    private int currentSlot = 0; // ช่องที่เลือกอยู่ (0-2)



    // เก็บ "ขนาดเดิมก่อนหยิบ" ของแต่ละไอเท็ม เพื่อคืนตอนทิ้ง
    private Dictionary<GameObject, Vector3> originalScales = new Dictionary<GameObject, Vector3>();

    void Start()
    {
        UpdateHotbarUI();

        if (handTransform == null)
            Debug.LogWarning("ไม่ได้กำหนด Hand Transform!");

        if (itemLayer == 0)
            Debug.LogWarning("ไม่ได้กำหนด Item Layer!");
    }

    void Update()
    {
        HandleInput();

        if (showDebug)
            DebugNearbyItems();
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.E)) PickupItem();
        if (Input.GetKeyDown(KeyCode.Q)) DropItem();

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);
    }

    void PickupItem()
    {
        if (showDebug)
        {
            Debug.Log("=== พยายามเก็บของ ===");
            Debug.Log($"ช่องปัจจุบัน: {currentSlot + 1}");
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, pickupRange, itemLayer);

        if (hits.Length == 0)
        {
            if (showDebug) Debug.LogWarning("✗ ไม่พบของในระยะเก็บ!");
            return;
        }

        Collider nearest = null;
        float minDist = Mathf.Infinity;

        // Logic หาของที่ใกล้ที่สุด และเช็คเรื่องกับดัก
        foreach (Collider hit in hits)
        {
            GameObject potentialItem = hit.transform.root.gameObject;
            
            // สมมติถ้ามี Script StunTrap ให้เช็คตรงนี้ (ถ้าไม่มี error ลบส่วนนี้ได้)
            /*
            StunTrap trap = potentialItem.GetComponent<StunTrap>();
            if (trap != null && trap.IsDeactivating)
            {
                 if (showDebug) Debug.Log($"   - {potentialItem.name} ถูกข้ามเพราะกำลังจางหาย");
                continue; 
            }
            */

            float d = Vector3.Distance(transform.position, hit.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = hit;
            }
        }

        if (nearest == null) return;

        GameObject itemToPickup = nearest.transform.root.gameObject;

        if (inventory[currentSlot] != null)
        {
            if (showDebug) Debug.LogWarning($"✗ ช่อง {currentSlot + 1} เต็มแล้ว! (มี {inventory[currentSlot].name})");
            return;
        }

        // บันทึกขนาดเดิม
        if (!originalScales.ContainsKey(itemToPickup))
            originalScales[itemToPickup] = itemToPickup.transform.localScale;

        // ปิด Physics
        foreach (var col in itemToPickup.GetComponentsInChildren<Collider>())
            col.enabled = false;

        Rigidbody rb = itemToPickup.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

        // นำเข้า Inventory
        inventory[currentSlot] = itemToPickup;
        UpdateHandItem();
        UpdateHotbarUI();

        if (showDebug) Debug.Log($"✓ เก็บ {itemToPickup.name} สำเร็จ!");
    }

    void DropItem()
    {
        if (inventory[currentSlot] == null) return;

        GameObject droppedItem = inventory[currentSlot];

        // วางของข้างหน้า
        droppedItem.transform.SetParent(null, true);
        droppedItem.transform.position = transform.position
                                        + transform.forward * 2f
                                        + transform.up * 1f; droppedItem.transform.rotation = Quaternion.identity;

        // เปิด Physics
        foreach (var col in droppedItem.GetComponentsInChildren<Collider>())
            col.enabled = true;

        Rigidbody rb = droppedItem.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }

        // คืนขนาดเดิม
        if (originalScales.TryGetValue(droppedItem, out var original))
            droppedItem.transform.localScale = original;

        inventory[currentSlot] = null;
        UpdateHandItem();
        UpdateHotbarUI();

        if (showDebug) Debug.Log($"✓ ทิ้ง {droppedItem.name}");
    }

    void SelectSlot(int slot)
    {
        if (slot < 0 || slot >= 3) return;

        currentSlot = slot;
        UpdateHandItem();
        UpdateHotbarUI();
    }

    void UpdateHandItem()
    {
        if (handTransform == null) return;

        // ซ่อนของทุกชิ้นก่อน
        for (int i = 0; i < inventory.Length; i++)
            if (inventory[i] != null) inventory[i].SetActive(false);

        // ถ้าช่องปัจจุบันว่าง ก็จบเลย
        if (inventory[currentSlot] == null) return;

        // เอาของช่องปัจจุบันมาแสดง
        GameObject item = inventory[currentSlot];
        ItemData data = item.GetComponent<ItemData>();

        item.transform.SetParent(handTransform, false);

        if (data != null)
        {
            item.transform.localPosition = data.holdLocalPosition;
            item.transform.localRotation = Quaternion.Euler(data.holdLocalEuler);

            if (data.keepOriginalScale)
            {
                if (originalScales.TryGetValue(item, out var original))
                    item.transform.localScale = original;
            }
            else
            {
                item.transform.localScale = data.holdLocalScale;
            }
        }
        else
        {
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            if (originalScales.TryGetValue(item, out var original))
                item.transform.localScale = original;
        }

        item.SetActive(true);
    }

    // --- ส่วนสำคัญที่แก้ไขเพื่อแก้ปัญหาบล็อกขาว ---
    void UpdateHotbarUI()
    {
        // วนลูปเช็คทั้ง 3 ช่อง
        for (int i = 0; i < 3; i++)
        {
            // 1. จัดการ Background (กรอบช่อง) -> เปลี่ยนสีเมื่อเลือก
            if (i < slotBackgrounds.Length && slotBackgrounds[i] != null)
            {
                slotBackgrounds[i].color = (i == currentSlot) ? selectedColor : normalColor;
            }

            // 2. จัดการ Icon (รูปไอเทม) -> แสดงเฉพาะเมื่อมีของ
            if (i < itemIcons.Length && itemIcons[i] != null)
            {
                if (inventory[i] != null)
                {
                    // == มีของ ==
                    ItemData itemData = inventory[i].GetComponent<ItemData>();
                    
                    // เช็คว่ามีข้อมูลไอเทมและมีรูปไหม
                    if (itemData != null && itemData.icon != null)
                    {
                        itemIcons[i].sprite = itemData.icon; // ใส่รูป
                        itemIcons[i].color = Color.white;    // สีรูปปกติ
                        itemIcons[i].enabled = true;         // เปิดการแสดงผล
                    }
                    else
                    {
                         // มีของแต่ไม่มีรูป ให้ปิดไว้ก่อน หรือใส่รูป Default
                         itemIcons[i].enabled = false; 
                    }
                }
                else
                {
                    // == ไม่มีของ (ช่องว่าง) ==
                    itemIcons[i].sprite = null;   // เอารูปออก
                    itemIcons[i].enabled = false; // ปิด Image ทิ้งไปเลย (จะได้เห็นทะลุไปเจอกรอบด้านหลัง)
                }
            }
        }
    }

    void DebugNearbyItems()
    {
        var hits = Physics.OverlapSphere(transform.position, pickupRange, itemLayer);
        Debug.DrawRay(transform.position, transform.forward * pickupRange, Color.green);
        foreach (var h in hits)
            Debug.DrawLine(transform.position, h.transform.position, Color.yellow);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
}