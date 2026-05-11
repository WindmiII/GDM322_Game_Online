using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections; // เพิ่มเพื่อให้ใช้ Coroutine ได้

public class GameMenuManager : MonoBehaviour
{
    public static GameMenuManager Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject menuPanel;

    public bool isMenuOpen = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this.gameObject);
        else Instance = this;
    }

    private void Start()
    {
        if (menuPanel != null) menuPanel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) ToggleMenu();
    }

    public void ToggleMenu()
    {
        ToggleMenu(!isMenuOpen);
    }

    public void ToggleMenu(bool open)
    {
        if (menuPanel == null) return;

        isMenuOpen = open;
        menuPanel.SetActive(isMenuOpen);

        if (isMenuOpen)
        {
            Transform leaveBtn = menuPanel.transform.Find("Leave Button");
            if (leaveBtn != null) leaveBtn.gameObject.SetActive(true);
        }

        // จัดการเรื่องเมาส์
        Cursor.lockState = isMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isMenuOpen;
        
        // ถ้าปิดเมนู (open เป็น false) ตัวละครจะหลุดจากเงื่อนไข return ใน Update/FixedUpdate ทันที
    }

    public void ResumeGame()
    {
        if (isMenuOpen) ToggleMenu();
    }

    public void LeaveGame()
    {
        StartCoroutine(LeaveGameRoutine());
    }

    private IEnumerator LeaveGameRoutine()
    {
        // 1. ปลดล็อคเมาส์
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 2. ปิดการเชื่อมต่อ (Shutdown จะทำการส่งข้อมูล Disconnect ไปแจ้ง Host อัตโนมัติ)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            
            // หน่วงเวลา 0.2 วินาทีเพื่อให้ระบบทำการส่งข้อมูล Disconnect ไปหา Host ได้สำเร็จก่อนถูกลบ
            yield return new WaitForSeconds(0.2f);
            
            // ทำลาย NetworkManager เก่าทิ้ง เพื่อให้ฉากใหม่โหลดขึ้นมาได้อย่างสะอาด ไม่มีของเก่าตกค้าง (แก้บัค MissingReference)
            if (NetworkManager.Singleton != null) Destroy(NetworkManager.Singleton.gameObject);
        }

        // 3. โหลด Scene ปัจจุบันใหม่
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}