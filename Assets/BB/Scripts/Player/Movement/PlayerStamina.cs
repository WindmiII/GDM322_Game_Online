using UnityEngine;
using UnityEngine.UI; // จำเป็นสำหรับ UI

public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float drainRate = 20f;  // วิ่งแล้วลดเร็วแค่ไหน
    public float regenRate = 10f;  // เพิ่มเร็วแค่ไหน
    
    [Tooltip("ถ้าสตามิน่าหมด ต้องรอให้เด้งถึงกี่ % ถึงจะวิ่งได้อีกครั้ง (0.1 = 10%)")]
    public float jumpStartThreshold = 0.1f; 

    [Header("References")]
    [Tooltip("ลากหลอดเหลือง (UI Image) มาใส่ช่องนี้")]
    public Image staminaBarImage;
    
    private PlayerMotor playerMotor;
    private float currentStamina;
    private bool isExhausted = false; // สถานะ "เหนื่อยหอบ" (วิ่งไม่ได้)

    void Start()
    {
        playerMotor = GetComponent<PlayerMotor>();
        currentStamina = maxStamina; // เริ่มต้นเต็มหลอด
        UpdateUI();
    }

    void Update()
    {
        // ถ้ากำลังวิ่ง (เช็คจาก PlayerMotor)
        if (playerMotor.IsSprinting)
        {
            // ลดสตามิน่า
            currentStamina -= drainRate * Time.deltaTime;
            
            if (currentStamina <= 0)
            {
                currentStamina = 0;
                isExhausted = true; // หมดแรง!
                playerMotor.canSprint = false; // สั่งห้ามวิ่งทันที
            }
        }
        else
        {
            // เพิ่มสตามิน่า
            if (currentStamina < maxStamina)
            {
                currentStamina += regenRate * Time.deltaTime;
            }

            // ถ้าสตามิน่าเกินขีดจำกัดที่ตั้งไว้ ให้กลับมาวิ่งได้
            if (isExhausted && currentStamina >= maxStamina * jumpStartThreshold)
            {
                isExhausted = false; // หายเหนื่อยแล้ว
                playerMotor.canSprint = true; // อนุญาตให้วิ่ง
            }
        }

        // จำกัดค่าไม่ให้เกิน Max
        if (currentStamina > maxStamina) currentStamina = maxStamina;

        UpdateUI();
    }

    void UpdateUI()
    {
        if (staminaBarImage != null)
        {
            // คำนวณ Fill Amount (0 ถึง 1)
            staminaBarImage.fillAmount = currentStamina / maxStamina;
        }
    }
}