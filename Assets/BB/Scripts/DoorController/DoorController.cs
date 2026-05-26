using UnityEngine;
using UnityEngine.AI;

public class DoorController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("ลาก NavMeshObstacle ของประตูมาใส่ตรงนี้")]
    public NavMeshObstacle obstacle;
    
    [Tooltip("ลาก Mesh หรือ Object ของตัวประตูมาใส่ (เพื่อซ่อน/แสดง)")]
    public GameObject doorVisual;

    private void Start()
    {
        // เริ่มต้น: ประตูเปิดอยู่ (ไม่มีประตูขวาง) -> ปิด Obstacle, ปิดภาพประตู
        SetDoorState(true);
    }

    // true = ล็อก (มีประตู), false = เปิด (ไม่มีประตู)
    public void SetDoorState(bool isLocked)
    {
        if (doorVisual != null) 
            doorVisual.SetActive(isLocked); // แสดง/ซ่อน โมเดลประตู

        if (obstacle != null)
        {
            obstacle.enabled = isLocked; // เปิด/ปิด การขวางทางเดิน
            obstacle.carving = isLocked; // สำคัญ! ตัด NavMesh ให้ศัตรูรู้ว่าเดินผ่านไม่ได้
        }
    }
}