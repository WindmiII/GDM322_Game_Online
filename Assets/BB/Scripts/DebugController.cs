using UnityEngine;
using UnityEngine.SceneManagement; // จำเป็นสำหรับจัดการ Scene
using UnityEngine.AI;            // จำเป็นสำหรับ NavMeshAgent ของศัตรู

public class DebugController : MonoBehaviour
{
    [Header("Key Bindings")]
    [Tooltip("ปุ่มสำหรับ Reload Scene ปัจจุบัน")]
    public KeyCode reloadSceneKey = KeyCode.F5;

    [Tooltip("ปุ่มสำหรับเปิด/ปิด God Mode (ต้องเขียนโค้ดใน PlayerHealth เพื่อใช้)")]
    public KeyCode godModeKey = KeyCode.F6;

    [Tooltip("ปุ่มสำหรับหยุด/เริ่มการทำงานของ AI ศัตรูทั้งหมด")]
    public KeyCode toggleAIKey = KeyCode.F7;

    [Tooltip("ปุ่มสำหรับทำลายศัตรูทั้งหมด (ที่มี Tag 'Enemy')")]
    public KeyCode killAllEnemiesKey = KeyCode.F8;

    [Tooltip("ปุ่มสำหรับข้ามไป Scene ถัดไป (วนลูปกลับ Scene แรกถ้าถึงด่านสุดท้าย)")]
    public KeyCode skipSceneKey = KeyCode.F9; // <--- เพิ่มปุ่มนี้เข้ามา

    [Header("Debug Status (Read Only)")]
    [SerializeField, Tooltip("สถานะ God Mode ปัจจุบัน")]
    private bool isGodModeActive = false;
    [SerializeField, Tooltip("สถานะ AI ศัตรู ปัจจุบัน (True = ทำงาน, False = หยุด)")]
    private bool isAIActive = true;

    // --- Singleton Pattern ---
    public static DebugController Instance { get; private set; }
    public bool IsGodModeActive => isGodModeActive; 

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // แนะนำให้เปิดอันนี้ไว้ เพื่อให้กดข้ามได้ทุกด่านโดยไม่ต้องวาง Script ใหม่
        }
    }

    void Update()
    {
        // --- Reload Scene ---
        if (Input.GetKeyDown(reloadSceneKey)) ReloadCurrentScene();

        // --- Toggle God Mode ---
        if (Input.GetKeyDown(godModeKey)) ToggleGodMode();

        // --- Toggle Enemy AI ---
        if (Input.GetKeyDown(toggleAIKey)) ToggleAllEnemyAI();

        // --- Kill All Enemies ---
        if (Input.GetKeyDown(killAllEnemiesKey)) KillAllEnemies();

        // --- Skip Scene (New) ---
        if (Input.GetKeyDown(skipSceneKey)) SkipToNextScene();
    }

    // --- ฟังก์ชันการทำงาน ---

    public void ReloadCurrentScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
        Debug.Log($"[Debug] Reloaded Scene: {currentScene.name}");
    }

    public void SkipToNextScene()
    {
        // 1. หา Index ของ Scene ปัจจุบัน
        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        
        // 2. บวกเพิ่มไป 1
        int nextIndex = currentIndex + 1;

        // 3. เช็คว่าเกินจำนวน Scene ทั้งหมดที่มีหรือยัง?
        // (SceneManager.sceneCountInBuildSettings คือจำนวน Scene ทั้งหมดที่ใส่ไว้ใน Build Settings)
        if (nextIndex >= SceneManager.sceneCountInBuildSettings)
        {
            nextIndex = 0; // ถ้าเกิน ให้วนกลับไป Scene แรก (Index 0)
            Debug.Log("[Debug] Reached last scene. Looping back to start.");
        }

        // 4. โหลด Scene ใหม่
        SceneManager.LoadScene(nextIndex);
        Debug.Log($"[Debug] Skipped to Scene Index: {nextIndex}");
    }

    public void ToggleGodMode()
    {
        isGodModeActive = !isGodModeActive;
        Debug.Log($"[Debug] God Mode {(isGodModeActive ? "ENABLED" : "DISABLED")}.");
    }

    public void ToggleAllEnemyAI()
    {
        isAIActive = !isAIActive; 
        EnemyAi[] allEnemies = FindObjectsOfType<EnemyAi>();
        int count = 0;

        foreach (EnemyAi enemy in allEnemies)
        {
            if (enemy != null)
            {
                enemy.enabled = isAIActive;
                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                if (agent != null)
                {
                    agent.isStopped = !isAIActive;
                    if(isAIActive && !agent.isStopped) agent.ResetPath();
                }
                count++;
            }
        }
        Debug.Log($"[Debug] Toggled AI for {count} enemies. AI is now {(isAIActive ? "ACTIVE" : "INACTIVE")}.");
    }

     public void KillAllEnemies()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        int count = enemies.Length;
        foreach (GameObject enemy in enemies)
        {
            Destroy(enemy);
        }
        Debug.Log($"[Debug] Destroyed {count} enemies.");
    }
}