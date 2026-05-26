using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerRayOutline : MonoBehaviour
{
    [Header("Raycast Settings")]
    public float detectDistance = 2f;       // ระยะตรวจจับ
    public LayerMask detectLayer;           // เลเยอร์ที่ตรวจจับ

    [Header("Materials")]
    public Material outlineMaterial;        // แมทริอัล Outline HDRP

    private Renderer currentRender;         // ตัวที่กำลังถูกมองอยู่
    private Material[] originalMaterials;   // แมทริอัลเดิม
    private Material[] outlineMaterials;    // สำหรับเปลี่ยนตอนมอง

    void Update()
    {
        DetectObject();
    }

    void DetectObject()
    {
        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        RaycastHit hit;

        // ------------------------------
        // Raycast เฉพาะ Layer ที่เลือก
        // ------------------------------
        if (Physics.Raycast(ray, out hit, detectDistance, detectLayer))
        {
            Renderer rend = hit.collider.GetComponent<Renderer>();

            if (rend != null)
            {
                if (rend != currentRender)
                {
                    RemoveOutline();
                    AddOutline(rend);
                }
                return; // ออกจากฟังก์ชัน
            }
        }

        // ไม่เจออะไร → ถอด Outline
        RemoveOutline();
    }

    void AddOutline(Renderer rend)
    {
        currentRender = rend;

        // เก็บ material เดิม
        originalMaterials = rend.materials;

        // รวม material เดิม + outline
        outlineMaterials = new Material[originalMaterials.Length + 1];
        for (int i = 0; i < originalMaterials.Length; i++)
            outlineMaterials[i] = originalMaterials[i];

        outlineMaterials[outlineMaterials.Length - 1] = outlineMaterial;

        rend.materials = outlineMaterials;
    }

    void RemoveOutline()
    {
        if (currentRender != null)
        {
            currentRender.materials = originalMaterials;
            currentRender = null;
        }
    }
}