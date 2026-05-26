using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Repo/Accessory", fileName = "Accessory_New")]

public class Accessory : ScriptableObject
{
    [Header("Bonuses")]
    public float strengthBonus = 0f;
    public float rangeBonus = 0f;
}