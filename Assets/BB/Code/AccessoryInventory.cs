using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AccessoryInventory : MonoBehaviour
{
    public List<Accessory> equipped = new List<Accessory>();

    public float GetStrengthBonus()
    {
        float sum = 0f;
        foreach (var a in equipped) if (a) sum += a.strengthBonus;
        return sum;
    }

    public float GetRangeBonus()
    {
        float sum = 0f;
        foreach (var a in equipped) if (a) sum += a.rangeBonus;
        return sum;
    }
}
