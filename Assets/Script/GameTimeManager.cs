using Unity.Netcode;
using UnityEngine;
using TMPro; // เพิ่มเพื่อให้ใช้ TextMeshPro ได้

[RequireComponent(typeof(NetworkObject))] // บังคับให้ Unity ใส่ NetworkObject ให้อัตโนมัติเพื่อป้องกันบัค
public class GameTimeManager : NetworkBehaviour
{
    public static GameTimeManager Instance { get; private set; }

    [Header("Timer Settings")]
    [Tooltip("ปรับเวลาในการเล่นตรงนี้ (หน่วยเป็นวินาที) เช่น 300 = 5 นาที, 600 = 10 นาที")]
    public int roundTimeSeconds = 300;

    [Header("UI Settings")]
    public TMP_Text timerTextUI; // ลาก UI Text เวลาจากใน Scene มาใส่ช่องนี้ได้เลย
    public GameObject timerPanel; // ลากออบเจกต์ที่เป็นพื้นหลัง (Image) หรือ Panel ของเวลามาใส่ช่องนี้

    // ตัวแปรเวลาของเกม ซิงค์กันทั้งเซิร์ฟเวอร์ (มีแค่ตัวเดียวทั้งฉาก)
    public NetworkVariable<int> GameTimer = new NetworkVariable<int>(
        300, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    private float timerTick = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this) 
        {
            Destroy(gameObject);
        }
        else 
        {
            Instance = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            GameTimer.Value = roundTimeSeconds;
        }
        
        GameTimer.OnValueChanged += OnTimerChanged;
        UpdateTimerUI(GameTimer.Value); // อัปเดต UI ทันทีตอนเริ่ม
    }

    public override void OnNetworkDespawn()
    {
        GameTimer.OnValueChanged -= OnTimerChanged;
    }

    private void OnTimerChanged(int oldValue, int newValue)
    {
        UpdateTimerUI(newValue);
    }

    private void UpdateTimerUI(int seconds)
    {
        if (timerTextUI == null) return;
        int min = seconds / 60;
        int sec = seconds % 60;
        timerTextUI.text = $"{min:00}:{sec:00}"; // แสดงผลเป็น 05:00
    }

    private void Update()
    {
        // แจ้งเตือนเตือนเผื่อว่าตัว GameTimeManager ไม่ได้ถูก Spawn เข้าสู่ระบบออนไลน์
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !IsSpawned)
        {
            Debug.LogWarning("GameTimeManager ยังไม่ถูก Spawn ในระบบออนไลน์! กรุณาเช็คว่ามันมี NetworkObject หรือไม่");
            return;
        }

        bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted.Value;

        // เปิด/ปิด UI เวลาตามสถานะการเริ่มเกม (ซ่อนไว้ก่อนจนกว่า Host จะกด Start)
        if (timerTextUI != null)
        {
            if (timerTextUI.gameObject.activeSelf != isGameStarted)
            {
                timerTextUI.gameObject.SetActive(isGameStarted);
                
                if (isGameStarted) UpdateTimerUI(GameTimer.Value); // อัปเดตตัวเลขให้ตรงทันทีที่โชว์
            }
        }

        // เปิด/ปิด ภาพพื้นหลังเวลาด้วย (ถ้ามี)
        if (timerPanel != null && timerPanel.activeSelf != isGameStarted)
        {
            timerPanel.SetActive(isGameStarted);
        }

        if (!IsServer) return;

        // เช็คว่าเกมเริ่มแล้วหรือยัง (Host กดปุ่ม Start แล้ว) ถึงจะเริ่มนับเวลา
        if (GameTimer.Value > 0 && isGameStarted)
        {
            timerTick += Time.deltaTime;
            if (timerTick >= 1f)
            {
                timerTick -= 1f;
                GameTimer.Value--;
            }
        }
    }

    public void ResetTimer()
    {
        if (IsServer)
        {
            GameTimer.Value = roundTimeSeconds;
            timerTick = 0f;
        }
    }
}