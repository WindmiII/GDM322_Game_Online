using UnityEngine;

public class HoverOutline : MonoBehaviour
{
    [SerializeField] private Material outlineMaterial;

    private Renderer render;
    private Material[] originalMats;
    private Material[] hoverMats;

    private bool isOutlined = false; // ใช้กันซ้ำเวลาโดน Raycast หลายรอบ

    void Start()
    {
        render = GetComponent<Renderer>();

        // เก็บ Materials เดิม
        originalMats = render.materials;

        // เตรียมชุด Materials ที่มี Outline
        hoverMats = new Material[originalMats.Length + 1];

        for (int i = 0; i < originalMats.Length; i++)
        {
            hoverMats[i] = originalMats[i];
        }

        hoverMats[hoverMats.Length - 1] = outlineMaterial;
    }

    // -----------------------------
    //  ฟังก์ชันใหม่: สำหรับ Raycast เรียกใช้ได้
    // -----------------------------
    public void SetOutline(bool active)
    {
        if (active && !isOutlined)
        {
            render.materials = hoverMats;
            isOutlined = true;
        }
        else if (!active && isOutlined)
        {
            render.materials = originalMats;
            isOutlined = false;
        }
    }

    // -----------------------------
    //  ฟังก์ชัน Hover เมาส์ (ยังใช้ได้ตามปกติ)
    // -----------------------------
    void OnMouseEnter()
    {
        SetOutline(true);
    }

    void OnMouseExit()
    {
        SetOutline(false);
    }
}