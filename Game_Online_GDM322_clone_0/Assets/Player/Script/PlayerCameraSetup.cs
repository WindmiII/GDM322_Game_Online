using Unity.Netcode;
using UnityEngine;

public class PlayerCameraSetup : NetworkBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener playerAudioListener;
    [SerializeField] private Transform cameraBoom;

    [Header("TPS/FPS Offsets")]
    [SerializeField] private Vector3 tpsOffset = new Vector3(0.7f, 0.5f, -2.5f);
    [SerializeField] private Vector3 fpsOffset = new Vector3(0f, 0.6f, 0.2f);

    [Header("Rotation Settings")]
    public float sensitivity = 2f;
    private float rotationX = 0f;
    private float rotationY = 0f;
    private bool isShiftLock = true;
    private bool isMenuOpen = false;

    [Header("Zoom Settings")]
    [SerializeField] private float minZoom = 1.5f;
    [SerializeField] private float maxZoom = 10f;
    [SerializeField] private float zoomSpeed = 5f;
    private float currentZoom = 3f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            if (playerCamera != null) playerCamera.gameObject.SetActive(false);
            if (playerAudioListener != null) playerAudioListener.enabled = false;
        }
        else
        {
            SetupInitialCamera();

            // แก้ตรงนี้: เริ่มต้นมาให้เปิดเมนูไว้ก่อน (isMenuOpen = true) 
            // เพื่อให้ขยับตัวไม่ได้ขณะอยู่หน้า Lobby/UI
            ToggleMenu(true);
        }
    }

    void SetupInitialCamera()
    {
        if (gameObject.name.Contains("Monster"))
        {
            playerCamera.transform.localPosition = fpsOffset;
        }
        else
        {
            currentZoom = Mathf.Abs(tpsOffset.z);
            playerCamera.transform.localPosition = tpsOffset;
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        PlayerStateSync myState = GetComponent<PlayerStateSync>();
        bool isJUnlocked = myState != null && myState.IsCursorUnlocked;
        bool isGameMenuOpen = GameMenuManager.Instance != null && GameMenuManager.Instance.isMenuOpen;

        // 2. ถ้าเมนูเปิดอยู่ หรือกด J ปลดเมาส์ ให้ "ล็อคทุกอย่าง" (ทั้งกล้องและการขยับ)
        if (isMenuOpen || isJUnlocked || isGameMenuOpen) return;

        // 3. รันระบบกล้องปกติ
        HandleZoom();
        HandleCameraRotation();

        // 4. สลับ Shift Lock (เฉพาะ Survivor)
        if (Input.GetKeyDown(KeyCode.LeftShift) && !gameObject.name.Contains("Monster"))
        {
            isShiftLock = !isShiftLock;
        }
    }

    // ฟังก์ชันสำหรับเช็คสถานะจากสคริปต์อื่น (เช่น สคริปต์เดิน)
    public bool IsMenuOpen()
    {
        return isMenuOpen;
    }

    void HandleZoom()
    {
        if (gameObject.name.Contains("Monster")) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            currentZoom -= scroll * zoomSpeed;
            currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);

            Vector3 targetPos = playerCamera.transform.localPosition;
            targetPos.z = -currentZoom;
            playerCamera.transform.localPosition = targetPos;
        }
    }

    void HandleCameraRotation()
    {
        // ถ้าเป็น Monster หรือ Shift Lock หรือ คลิกขวาค้าง ถึงจะหมุนได้
        bool canRotate = gameObject.name.Contains("Monster") || isShiftLock || Input.GetMouseButton(1);

        if (canRotate)
        {
            float mouseX = Input.GetAxis("Mouse X") * sensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

            rotationY += mouseX;
            rotationX -= mouseY;
            rotationX = Mathf.Clamp(rotationX, -60f, 80f);

            if (cameraBoom != null)
                cameraBoom.localRotation = Quaternion.Euler(rotationX, 0, 0);
            else
                playerCamera.transform.parent.localRotation = Quaternion.Euler(rotationX, 0, 0);

            transform.rotation = Quaternion.Euler(0, rotationY, 0);
        }
    }

    public void ToggleMenu(bool open)
    {
        isMenuOpen = open;
        Cursor.lockState = isMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isMenuOpen;
    }
    public void StartGame()
    {
        if (IsOwner) ToggleMenu(false);
    }
}