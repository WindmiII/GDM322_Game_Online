using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NavArrowFlexible : MonoBehaviour
{
    public Transform targetTransform;
    public Vector3 targetPosition;
    public bool useTransform = true;

    public Transform cam;              // กล้องของ FPS
    public float distanceInFront = 2f; // ลูกศรอยู่ห่างจากหน้ากล้องเท่าไหร่

    void Update()
    {
        if (cam == null) return;

        // 1) วางลูกศรไว้หน้ากล้องตลอดเวลา (ป้องกันหายเวลาอยู่ชิดกำแพง)
        transform.position = cam.position + cam.forward * distanceInFront;

        // 2) กำหนดเป้าหมาย
        Vector3 finalTarget = (useTransform && targetTransform != null)
            ? targetTransform.position
            : targetPosition;

        // 3) หมุนลูกศรให้ชี้ไปที่เป้าหมาย
        Vector3 dir = finalTarget - transform.position;
        transform.rotation = Quaternion.LookRotation(dir);
    }
}