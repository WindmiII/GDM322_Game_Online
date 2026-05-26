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

        // CHANGED (§5.2 / §12.15): main timer UI + tick are now gated on RoundPhase.Active,
        // not LobbyManager.IsGameStarted. This hides the 5:00 timer during preview/hide phases —
        // those phases use PhaseUI's own countdown instead.
        bool isActive = RoundManager.Instance != null &&
                        RoundManager.Instance.CurrentPhase.Value == RoundPhase.Active;

        // เปิด/ปิด UI เวลาตามสถานะของ Round Phase (ซ่อนไว้จนกว่าจะถึง Active phase จริงๆ)
        if (timerTextUI != null)
        {
            if (timerTextUI.gameObject.activeSelf != isActive)
            {
                timerTextUI.gameObject.SetActive(isActive);
                
                if (isActive) UpdateTimerUI(GameTimer.Value); // อัปเดตตัวเลขให้ตรงทันทีที่โชว์
            }
        }

        // เปิด/ปิด ภาพพื้นหลังเวลาด้วย (ถ้ามี)
        if (timerPanel != null && timerPanel.activeSelf != isActive)
        {
            timerPanel.SetActive(isActive);
        }

        if (!IsServer) return;

        // เช็คว่าอยู่ใน Active phase แล้วหรือยัง (preview/hide ผ่านไปแล้ว) ถึงจะเริ่มนับเวลา 5 นาทีหลัก
        if (GameTimer.Value > 0 && isActive)
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
