using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class LoadingScreen : MonoBehaviour
{
    // Singleton เพื่อให้เรียกใช้จากสคริปต์อื่นได้ง่ายๆ (LoadingScreen.Instance.xxx)
    public static LoadingScreen Instance;

    [Header("Settings")]
    [Tooltip("ระยะเวลาที่ใช้ในการจาง (วินาที)")]
    public float fadeDuration = 1.0f;
    [Tooltip("เวลารอก่อนจะเริ่มจางตอนเปิดเกม")]
    public float startDelay = 0.5f;

    [Header("UI References")]
    public CanvasGroup canvasGroup;    // ตัวคุมความโปร่งใส (Alpha) ของ Panel
    public RectTransform spinnerImage; // รูปตัวหมุนโหลด
    public float spinSpeed = 200f;     // ความเร็วในการหมุน

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // === ส่วนที่ 1: เริ่ม Scene (จอดำ -> จอใส) ===
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;           // เริ่มมาดำสนิท
            canvasGroup.blocksRaycasts = true; // กันคนกดทะลุ
        }
        
        // เริ่มทำงาน Fade In (ให้เห็นฉากเกม)
        StartCoroutine(FadeToClear());
    }

    void Update()
    {
        // สั่งให้ตัวหมุน หมุนตลอดเวลา
        if (spinnerImage != null)
        {
            spinnerImage.Rotate(0f, 0f, -spinSpeed * Time.deltaTime);
        }
    }

    // Coroutine: ทำให้จางจนมองเห็นเกม
    IEnumerator FadeToClear()
    {
        yield return new WaitForSeconds(startDelay);

        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            // เปลี่ยนค่า Alpha จาก 1 (ทึบ) ไป 0 (ใส)
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            
            yield return null;
        }

        // จบงาน: ปิดการแสดงผลเพื่อประหยัด Resource
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false; // ปลดล็อกให้ผู้เล่นกดปุ่มในเกมได้
        }
        
        gameObject.SetActive(false); 
    }

    // === ส่วนที่ 2: เปลี่ยน Scene (จอใส -> จอดำ -> โหลด) ===
    // ฟังก์ชันนี้จะถูกเรียกโดย ExitGate
    public void LoadSceneWithFade(string sceneName)
    {
        gameObject.SetActive(true); // ปลุกตัวเองขึ้นมา
        StartCoroutine(FadeToBlackAndLoad(sceneName));
    }

    IEnumerator FadeToBlackAndLoad(string sceneName)
    {
        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = true; // ล็อกไม่ให้ผู้เล่นกดอะไรระหว่างรอ

        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            // เปลี่ยนค่า Alpha จาก 0 (ใส) ไป 1 (ทึบ)
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(0f, 1f, timer / fadeDuration);
            
            yield return null;
        }

        // รออีกนิดนึงให้แน่ใจว่าดำสนิท
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        yield return new WaitForSeconds(0.2f);

        // โหลด Scene ใหม่
        SceneManager.LoadScene(sceneName);
    }
}