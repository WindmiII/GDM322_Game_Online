using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class ValueSafeZone : MonoBehaviour
{
    [Header("Objective System")]
    public LevelObjectiveManager objectiveManager;

    [Header("Total Value Display")]
    public TMP_Text totalValueText;
    public string valuePrefix = "Zone Value: ";
    public int valueDecimals = 0;

    [Header("Win Condition")]
    public float targetValue = 500f;
    public UnityEvent onWinConditionMet;

    [Header("Which objects count?")]
    public LayerMask interactiveLayers = 0;

    private readonly HashSet<DragRigidbody> inside = new();
    private Collider zone;
    private bool winConditionAlreadyMet = false;

    void Awake()
    {
        zone = GetComponent<Collider>();
        zone.isTrigger = true;
        if (objectiveManager == null)
        {
            objectiveManager = FindObjectOfType<LevelObjectiveManager>();
            if (objectiveManager == null) Debug.LogWarning("Objective Manager is not assigned/found!", this.gameObject);
        }
        UpdateUi();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!AcceptLayer(other.gameObject.layer)) return;
        var dragScript = other.GetComponent<DragRigidbody>();
        if (dragScript != null && inside.Add(dragScript))
        {
            var tracker = dragScript.GetComponent<DragRigidbody.ImpactValueTracker>();
            if (tracker) tracker.AddProtection();
            if (objectiveManager != null && !string.IsNullOrEmpty(dragScript.itemID))
            {
                objectiveManager.OnItemDelivered(dragScript.itemID);
            }
            UpdateUi();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!AcceptLayer(other.gameObject.layer)) return;
        var dragScript = other.GetComponent<DragRigidbody>();
        if (dragScript != null && inside.Remove(dragScript))
        {
            var tracker = dragScript.GetComponent<DragRigidbody.ImpactValueTracker>();
            if (tracker) tracker.RemoveProtection();
            if (objectiveManager != null && !string.IsNullOrEmpty(dragScript.itemID))
            {
                bool anotherItemOfTypeExists = inside.Any(item => item != null && item.itemID == dragScript.itemID);
                if (!anotherItemOfTypeExists)
                {
                    objectiveManager.OnItemRemoved(dragScript.itemID);
                }
            }
            UpdateUi();
        }
    }

    bool AcceptLayer(int layer)
    {
        if (interactiveLayers.value == 0) return true;
        return (interactiveLayers.value & (1 << layer)) != 0;
    }

    public float SumCurrentValue()
    {
        float sum = 0f;
        inside.RemoveWhere(item => item == null);
        foreach (var item in inside)
        {
            // Ensure item and tracker are valid before accessing Value
            if (item != null)
            {
                var tracker = item.GetComponent<DragRigidbody.ImpactValueTracker>();
                if (tracker != null) sum += tracker.Value;
            }
        }
        return sum;
    }


    void UpdateUi()
    {
        float currentValue = SumCurrentValue();

        if (totalValueText)
        {
            totalValueText.text = valuePrefix + System.Math.Round(currentValue, valueDecimals).ToString();
        }

        if (objectiveManager != null)
        {
            objectiveManager.UpdateCurrentSafeZoneValue(currentValue);
        }

        CheckWinCondition();
    }

    void CheckWinCondition()
    {
        if (winConditionAlreadyMet) return;

        bool allConditionsMet = false;
        if (objectiveManager != null)
        {
            allConditionsMet = objectiveManager.AreAllObjectivesComplete();
        }
        else
        {
             allConditionsMet = SumCurrentValue() >= targetValue;
        }

        if (allConditionsMet)
        {
            Debug.Log("Win Condition Met!");
            winConditionAlreadyMet = true;
            onWinConditionMet?.Invoke();
        }
    }
}