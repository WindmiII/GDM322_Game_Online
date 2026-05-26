using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BreakP : MonoBehaviour
{
    public Break group; // อ้างถึงพาเรนต์กลุ่ม

    private Rigidbody rb;

    void Reset()
    {
        rb = GetComponent<Rigidbody>();
        if (group == null) group = GetComponentInParent<Break>();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (group == null) group = GetComponentInParent<Break>();
    }

    void Start()
    {
        // เริ่มต้นเป็นคิเนมาติก/ไม่ใช้แรงโน้มถ่วงจนกว่าจะถูกสั่งแตก
        // rb.isKinematic = true;
        // rb.useGravity = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Breaker"))
        {
            // แตะชิ้นเดียว -> สั่งแตกทั้งกลุ่มนี้เท่านั้น
            Debug.Log("BreakAll called");
            if (group != null) group.BreakAll();
        }
    }
}