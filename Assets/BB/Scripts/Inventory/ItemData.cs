using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemData : MonoBehaviour
{
    [Header("Info")]
    public string itemName;
    public Sprite icon;

    [Header("Hold Settings")]
    public Vector3 holdLocalPosition = new Vector3(0.1f, -0.1f, 0.4f);
    public Vector3 holdLocalEuler = Vector3.zero;

    // ถ้า true จะคง "ขนาดเดิมก่อนหยิบ" ไว้ตอนถือ (แนะนำให้เปิดไว้)
    public bool keepOriginalScale = true;

    // ถ้าอยากกำหนดสเกลพิเศษตอนถือ ให้ปิด keepOriginalScale แล้วใช้ค่านี้แทน
    public Vector3 holdLocalScale = Vector3.one;
}