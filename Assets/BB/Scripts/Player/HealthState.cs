using UnityEngine;

[System.Serializable] // <-- ทำให้มันแสดงใน Inspector ได้
public struct HealthState
{
    [Tooltip("Sprite ที่จะแสดงผล")]
    public Sprite icon;
    
    [Tooltip("เลือดต้อง *มากกว่า* เปอร์เซ็นต์นี้ เพื่อใช้รูปนี้ (เช่น 0.7 = 70%)")]
    [Range(0f, 1f)]
    public float minimumHealthPercent;
}