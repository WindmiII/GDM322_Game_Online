using UnityEngine;

public class PlayerLook : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private float mouseSensitivity = 100f;
    [Tooltip("ลาก GameObject ของ Player (ตัวหลัก) มาใส่")]
    [SerializeField] private Transform playerBody;
    [SerializeField] private float maxLookAngle = 80f;

    private float xRotation = 0f;

    void Start()
    {
        if (playerBody == null)
        {
            Debug.LogError("PlayerLook: Player Body is not assigned!");
        }
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (playerBody == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // หันซ้าย/ขวา: หมุนตัว Player (Rigidbody) ทั้งตัว
        playerBody.Rotate(Vector3.up * mouseX);

        // หันขึ้น/ลง: หมุนเฉพาะกล้อง
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle); // จำกัดมุมก้ม/เงย
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}