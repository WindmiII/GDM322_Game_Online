using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System.Collections; // <-- 1. เพิ่มบรรทัดนี้เพื่อใช้ Coroutines


[RequireComponent(typeof(AudioSource))]
public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public int maxHealth = 100;
    private int currentHealth;

    [Header("Health UI")]
    public Image healthIconImage;
    public List<HealthState> healthStates;

    // --- 💡 NEW: เพิ่มส่วนสำหรับเอฟเฟกต์จอแดง ---
    [Header("Damage Effect")]
    [Tooltip("ลาก UI Image ที่เป็นกรอบแดงเต็มจอมาใส่ช่องนี้")]
    public Image damageVignetteImage;
    [Tooltip("ระยะเวลาที่จอแดงจะจางหายไป (วินาที)")]
    public float damageFlashFadeTime = 0.5f;
    [Tooltip("ความเข้มสูงสุดของจอแดง (0-1)")]
    [Range(0f, 1f)]
    public float maxDamageAlpha = 0.8f;

    [Header("Audio Settings")]
    [Tooltip("เสียงร้องตอนโดนตี (ใส่ได้หลายเสียง ระบบจะสุ่มให้)")]
    public AudioClip[] hurtClips;
    private AudioSource audioSource;
    
    private Coroutine damageFlashCoroutine; // ตัวแปรเก็บ Coroutine ที่กำลังทำงาน
    // --- สิ้นสุดส่วนที่เพิ่ม ---

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        currentHealth = maxHealth;
        if (healthStates != null)
        {
            healthStates = healthStates.OrderByDescending(s => s.minimumHealthPercent).ToList();
        }
        UpdateHealthUI();

        // 💡 NEW: ตั้งค่าให้จอแดงโปร่งใสตอนเริ่ม
        if (damageVignetteImage != null)
        {
            Color color = damageVignetteImage.color;
            color.a = 0;
            damageVignetteImage.color = color;
        }
    }

    public void TakeDamage(int damageAmount)
    {
        if (DebugController.Instance != null && DebugController.Instance.IsGodModeActive)
        {
            Debug.Log("God Mode ON: Damage ignored.");
            return;
        }

        currentHealth -= damageAmount;
        if (currentHealth < 0) currentHealth = 0;
        Debug.Log($"Player took {damageAmount} damage. Current Health: {currentHealth}");

        UpdateHealthUI();
        
        FlashDamageEffect();

        PlayHurtSound();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void PlayHurtSound()
    {
        if (hurtClips.Length > 0 && audioSource != null)
        {
            int index = Random.Range(0, hurtClips.Length);
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(hurtClips[index]);
        }
    }

    void Die()
    {
        Debug.Log("Player has died! Reloading scene...");
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    void UpdateHealthUI()
    {
        // ... (โค้ดอัปเดต Icon รูปหน้าคน เหมือนเดิม) ...
        if (healthIconImage == null || healthStates == null || healthStates.Count == 0) return;
        float healthPercent = (float)currentHealth / maxHealth;
        foreach (var state in healthStates)
        {
            if (healthPercent >= state.minimumHealthPercent)
            {
                healthIconImage.sprite = state.icon;
                return;
            }
        }
        if(healthStates.Count > 0)
        {
            healthIconImage.sprite = healthStates.Last().icon;
        }
    }

    // --- 💡 NEW: ฟังก์ชันใหม่ 2 อันข้างล่างนี้ ---

    // 1. ฟังก์ชันนี้จะ "เริ่ม" การกระพริบ
    void FlashDamageEffect()
    {
        if (damageVignetteImage == null) return; // ออก ถ้าลืมลาก Image มาใส่

        // ถ้ากำลังจางอยู่ ให้หยุดอันเก่าก่อน
        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
        }
        
        // เริ่ม Coroutine อันใหม่
        damageFlashCoroutine = StartCoroutine(FadeDamageVignette());
    }

    // 2. Coroutine นี้คือตัวนับเวลา "จางหาย"
    IEnumerator FadeDamageVignette()
    {
        // ตั้งค่าให้แดงเข้มทันที
        Color color = damageVignetteImage.color;
        color.a = maxDamageAlpha; // ตั้งค่าความเข้มสูงสุด
        damageVignetteImage.color = color;

        float elapsedTime = 0f;

        // วน Loop ค่อยๆ ลด Alpha ลงจนเป็น 0
        while (elapsedTime < damageFlashFadeTime)
        {
            elapsedTime += Time.deltaTime;
            
            // คำนวณ Alpha ใหม่ (จาก maxAlpha ไป 0)
            color.a = Mathf.Lerp(maxDamageAlpha, 0f, elapsedTime / damageFlashFadeTime);
            damageVignetteImage.color = color;
            
            yield return null; // รอเฟรมถัดไป
        }

        // เผื่อไว้: ตั้งค่าเป็น 0 เป๊ะๆ เมื่อจบ
        color.a = 0f;
        damageVignetteImage.color = color;
        damageFlashCoroutine = null; // เคลียร์ Coroutine
    }
}