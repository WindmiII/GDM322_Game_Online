using UnityEngine;
using TMPro; // ถ้าใช้ TextMeshPro
// using UnityEngine.UI; // ถ้าใช้ Text ธรรมดา ให้เปิดบรรทัดนี้แทน

public class DoorButton : MonoBehaviour
{
    [Header("References")]
    public DoorController targetDoor;
    public TextMeshPro textDisplay; // ลาก Text 3D หรือ UI มาใส่เพื่อโชว์เลข

    [Header("Settings")]
    public int maxUses = 5; // จำนวนครั้งที่ล็อกได้
    public Color hoverColor = Color.green; // สีตอนเอาเมาส์ชี้

    private int currentUses;
    private bool isRoomLocked = false; // สถานะปัจจุบัน
    private Renderer buttonRenderer;
    private Color originalColor;

    private void Start()
    {
        currentUses = maxUses;
        buttonRenderer = GetComponent<Renderer>();
        originalColor = buttonRenderer.material.color;
        UpdateText();
    }

    // ทำงานเมื่อเอาเมาส์ชี้ (ต้องมี Collider ที่ปุ่ม)
    private void OnMouseEnter()
    {
        buttonRenderer.material.color = hoverColor;
    }

    // ทำงานเมื่อเอาเมาส์ออก
    private void OnMouseExit()
    {
        buttonRenderer.material.color = originalColor;
    }

    // ทำงานเมื่อคลิกซ้าย
    private void OnMouseDown()
    {
        if (isRoomLocked)
        {
            // ถ้าล็อกอยู่ -> ให้เปิด (ไม่เสียจำนวนครั้ง)
            UnlockRoom();
        }
        else
        {
            // ถ้าเปิดอยู่ -> จะล็อก (เช็คว่าโควต้าเหลือไหม)
            if (currentUses > 0)
            {
                LockRoom();
            }
            else
            {
                Debug.Log("โควต้าล็อกหมดแล้ว!");
                // อาจจะเพิ่มเสียง Error หรือเปลี่ยนสีเป็นสีแดงบอกผู้เล่นตรงนี้
            }
        }
    }

    void LockRoom()
    {
        isRoomLocked = true;
        currentUses--; // ลดจำนวนครั้งเฉพาะตอนล็อก
        targetDoor.SetDoorState(true); // สั่งประตูปิด
        UpdateText();
    }

    void UnlockRoom()
    {
        isRoomLocked = false;
        targetDoor.SetDoorState(false); // สั่งประตูเปิด
        // ไม่ลดจำนวนครั้ง
        UpdateText();
    }

    void UpdateText()
    {
        if (textDisplay != null)
        {
            textDisplay.text = currentUses.ToString();
        }
    }
}