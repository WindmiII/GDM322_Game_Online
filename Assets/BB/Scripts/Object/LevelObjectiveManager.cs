using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using System.Text;

public class LevelObjectiveManager : MonoBehaviour
{
    private class Objective { public string ItemID; public string DisplayName; }

    [Header("Item Setup")]
    public List<DragRigidbody> allItemsInLevel;
    public int numberOfEssentialItemsToPick = 2;

    [Header("References")]
    [Tooltip("ลาก ValueSafeZone GameObject มาใส่ตรงนี้")]
    public ValueSafeZone safeZone;

    [Header("UI")]
    public TMP_Text objectiveText;

    [Header("Item Prefabs")]
    public List<GameObject> itemPrefabs;



    // [Header("Required Quest Items")]
    //  public List<ObjectiveItem> requiredItems = new List<ObjectiveItem>();

    private List<Objective> requiredObjectives;
    private HashSet<string> foundItemIDs;
    private float currentSafeZoneValue = 0f;
    private float targetSafeZoneValue = 0f;
    public static LevelObjectiveManager Instance;


    void Awake()
    {
        Instance = this;

        requiredObjectives = new List<Objective>();
        foundItemIDs = new HashSet<string>();

        if (safeZone == null)
        {
            safeZone = FindObjectOfType<ValueSafeZone>();
            if (safeZone == null) Debug.LogError("LevelObjectiveManager cannot find the ValueSafeZone!", gameObject);
        }

        if (safeZone != null)
        {
            targetSafeZoneValue = safeZone.targetValue;
        }

        SelectRandomObjectiveItems();
        UpdateObjectiveUI();
    }

    void SelectRandomObjectiveItems()
    {
        if (allItemsInLevel == null || allItemsInLevel.Count == 0) return; 
            var validItems = allItemsInLevel.Where(item => item != null && !string.IsNullOrEmpty(item.itemID)).ToList(); 
         if (validItems.Count == 0) return; int numToPick = Mathf.Min(numberOfEssentialItemsToPick, validItems.Count); 
        if (numToPick <= 0) return; 
        var chosenItems = validItems.OrderBy(x => Random.value).Take(numToPick).ToList();


        Debug.Log("--- Required Items & Backups ---");
        foreach (var item in chosenItems)
        { requiredObjectives.Add(new Objective { ItemID = item.itemID, DisplayName = item.gameObject.name });
            item.isEssentialItem = true; Debug.Log($"Objective: {item.gameObject.name} (ID: {item.itemID})"); 
            if (item.backupEnemy != null && itemPrefabs != null) 
                { GameObject prefabToDrop = itemPrefabs.FirstOrDefault(p => p != null && p.GetComponent<DragRigidbody>()?.itemID == item.itemID); 
                    if (prefabToDrop != null) 
                { item.backupEnemy.itemToDrop = prefabToDrop; 
                    Debug.Log($" -> Backup for '{item.itemID}' assigned to enemy: {item.backupEnemy.name}"); 
                } 
                else { Debug.LogWarning($"Could not find a matching prefab for itemID '{item.itemID}' in the Item Prefabs list."); }
            } 
            else if (item.backupEnemy != null && itemPrefabs == null) 
            { Debug.LogWarning("Item Prefabs list is not assigned in the LevelObjectiveManager!"); 
            } 
        }
        Debug.Log("--------------------"); // Update UI after selecting objectives UpdateObjectiveUI();
        // ส่ง Required Item IDs ให้ระบบ Spawn
        var spawner = FindObjectOfType<ObjectSpawner>();
        if (spawner != null)
        {
            spawner.requiredItemIDs = requiredObjectives
                .Select(o => o.ItemID)
                .ToList();
            Debug.Log("Worked");
        }
    }

    public void OnItemDelivered(string itemID) 
    { if (requiredObjectives.Any(obj => obj.ItemID == itemID))
        {
            if (requiredObjectives.Any(obj => obj.ItemID == itemID))
            {
                if (foundItemIDs.Add(itemID))
                {
                    Debug.Log($"Objective item with ID '{itemID}' delivered.");
                    UpdateObjectiveUI();
                    ReassignBackupDrops();
                    DisableBackup(itemID);
                }
            }

        }
    }

    public void OnItemRemoved(string itemID)
    {
        if (requiredObjectives.Any(obj => obj.ItemID == itemID))
        {
            if (foundItemIDs.Remove(itemID))
            {
                Debug.Log($"Objective item with ID '{itemID}' removed.");
                UpdateObjectiveUI();
                ReassignBackupDrops();
            }
        }
    }
  
    public void UpdateCurrentSafeZoneValue(float currentValue)
    {
        if (currentSafeZoneValue != currentValue)
        {
            currentSafeZoneValue = currentValue;
            UpdateObjectiveUI();
        }
    }

    public bool AreAllObjectivesComplete()
    {
        if (requiredObjectives == null || foundItemIDs == null) return false;

        bool itemsComplete = requiredObjectives.Count == 0 || !requiredObjectives.Select(obj => obj.ItemID).Except(foundItemIDs).Any();
        bool valueComplete = currentSafeZoneValue >= targetSafeZoneValue;

        return itemsComplete && valueComplete;
    }

    private void DisableBackup(string itemID)
    {
        // ค้นหา DragRigidbody ทุกตัว (คือของทุกชิ้น)
        var allItems = FindObjectsOfType<DragRigidbody>();

        foreach (var item in allItems)
        {
            if (item.backupEnemy != null)
            {
                // เช็คว่าตรงกับ item ที่ส่งไหม
                if (item.itemID == itemID)
                {
                    item.backupEnemy = null; // ปิด backup
                    Debug.Log($"Backup disabled for item '{itemID}'");
                }
            }
        }
    }
    public void ReassignBackupDrops()
    {
        Debug.Log("Reassigning backup drops...");

        // 1) หา Objective ที่ยังไม่ถูกส่ง (ยังอยู่ใน requiredObjectives แต่ไม่อยู่ใน foundItemIDs)
        var remainingItems = requiredObjectives
            .Where(o => !foundItemIDs.Contains(o.ItemID))
            .Select(o => o.ItemID)
            .ToList();

        Debug.Log("Remaining items count = " + remainingItems.Count);

        if (remainingItems.Count == 0)
        {
            Debug.Log("All items delivered. No need to reassign.");
            return;
        }

        // 2) หา Enemy ทั้งหมด
        var allEnemies = FindObjectsOfType<EnemyAi>();

        // 3) เคลียร์ itemToDrop ของศัตรูที่ไม่มีของแล้ว (กันข้อมูลค้าง)
        foreach (var e in allEnemies)
        {
            if (e.itemToDrop != null)
            {
                var d = e.itemToDrop.GetComponent<DragRigidbody>();
                if (d != null && !remainingItems.Contains(d.itemID))
                {
                    e.itemToDrop = null;
                }
            }
        }

        // 4) Assign ใหม่
        foreach (var itemID in remainingItems)
        {
            // หา Prefab ที่ตรงกับ ItemID
            var prefab = itemPrefabs.FirstOrDefault(p =>
            {
                var drag = p.GetComponent<DragRigidbody>();
                return drag != null && drag.itemID == itemID;
            });

            if (prefab == null)
            {
                Debug.LogWarning($"No prefab found for remaining item '{itemID}'");
                continue;
            }

            // เลือก Enemy ที่ยังไม่มี Drop อยู่
            var freeEnemy = allEnemies.FirstOrDefault(e => e.itemToDrop == null);

            if (freeEnemy != null)
            {
                freeEnemy.itemToDrop = prefab;
                Debug.Log($"Reassigned '{itemID}' to enemy '{freeEnemy.name}'");
            }
            else
            {
                Debug.LogWarning("No free enemy available to assign backup drop.");
            }
        }
    }

    void UpdateObjectiveUI()
    {
        if (!objectiveText || requiredObjectives == null) return;

        var sb = new StringBuilder("Objectives:\n");

        // --- Item Objectives ---
        if (requiredObjectives.Count > 0)
        {
             if (foundItemIDs == null) foundItemIDs = new HashSet<string>();
            foreach (var objective in requiredObjectives)
            {
                if (foundItemIDs.Contains(objective.ItemID))
                {
                    sb.AppendLine($"<color=#77dd77>✅ <s>{objective.DisplayName}</s></color>");
                }
                else
                {
                    sb.AppendLine($"⬜ {objective.DisplayName}");
                }
            }
        }

        // --- Value Objective ---
        bool valueTargetMet = currentSafeZoneValue >= targetSafeZoneValue;

        if (valueTargetMet)
        {
            sb.AppendLine($"<color=#77dd77>✅ <s>Value: {currentSafeZoneValue:N0} / {targetSafeZoneValue:N0}</s></color>");
        }
        else
        {
            sb.AppendLine($"⬜ Value: {currentSafeZoneValue:N0} / {targetSafeZoneValue:N0}");
        }

        objectiveText.text = sb.ToString();
    }
}