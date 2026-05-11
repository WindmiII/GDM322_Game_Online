using UnityEngine;
using UnityEngine.InputSystem;

public class CursorController : MonoBehaviour
{
    void Start()
    {
        // เริ่มเกมมา ปลดล็อคเมาส์ไว้ก่อนเลย จะได้เอาไปคลิกปุ่ม Host / Client หน้า UI ได้
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        // เช็คการกดปุ่ม / (Slash) เพื่อสลับการเปิด/ปิดเมาส์
        if (Keyboard.current != null && Keyboard.current.slashKey.wasPressedThisFrame)
        {
            ToggleMouseCursor();
        }
    }

    private void ToggleMouseCursor()
    {
        // เช็คสถานะปัจจุบัน ถ้าล็อคอยู่ให้ปลด ถ้าปลดอยู่ให้ล็อค
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}