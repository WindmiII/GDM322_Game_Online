using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody))]
public class LiftableObject : MonoBehaviour
{
    [Header("Stats")]
    [Min(0.1f)] public float Weight = 5f;     // kg (ตั้งเป็น rb.mass อัตโนมัติ)
    [Min(1f)] public float MaxHp = 100f;
    [Min(0f)] public float Value = 100f;
    [Tooltip("HP คงเหลือปัจจุบัน")]
    public float CurrentHp;

    [Header("Damage Settings")]
    [Tooltip("ตัวคูณดาเมจจากแรงปะทะ (ประมาณด้วย mass*relativeSpeed)")]
    public float impactDamageMultiplier = 0.02f;
    [Tooltip("สัดส่วนที่ Value ลดเมื่อโดนดาเมจ (0-1)")]
    [Range(0f, 1f)] public float valueLossPerDamage = 0.25f;
    [Tooltip("ความเร็วชนขั้นต่ำที่จะนับเป็นดาเมจ")]
    public float minDamageVelocity = 1.5f;

    [Header("Damping (while Held)")]
    [Tooltip("Linear drag per kg while held")]
    public float dragPerKg = 0.35f;
    [Tooltip("Angular drag per kg while held")]
    public float angularDragPerKg = 0.45f;
    [Tooltip("Material damping multiplier (per object)")]
    public float dampingMultiplier = 1.0f;

    [Header("Events")]
    public UnityEvent onBroken;
    public UnityEvent onDamaged;

    Rigidbody rb;
    Collider[] myCols;
    float defaultDrag, defaultAngularDrag;
    bool isHeld;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = Mathf.Max(0.1f, Weight);
        CurrentHp = MaxHp;

        myCols = GetComponentsInChildren<Collider>();
        defaultDrag = rb.drag;
        defaultAngularDrag = rb.angularDrag;
    }

    public void SetHeld(bool held, Collider[] playerCols = null)
    {
        isHeld = held;

        // ฟิสิกส์ช่วยให้ลอยนิ่ง
        rb.interpolation = held ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        rb.collisionDetectionMode = held ? CollisionDetectionMode.ContinuousSpeculative
                                         : CollisionDetectionMode.Discrete;
        rb.useGravity = !held;

        if (held)
        {
            float m = Mathf.Max(0.1f, rb.mass);
            rb.drag = Mathf.Clamp(dragPerKg * m * dampingMultiplier, 2f, 25f);
            rb.angularDrag = Mathf.Clamp(angularDragPerKg * m * dampingMultiplier, 2f, 25f);
        }
        else
        {
            rb.drag = defaultDrag;
            rb.angularDrag = defaultAngularDrag;
        }

        // ตัด/คืนการชนกับคอลลายเดอร์ผู้เล่นตอนถือ/ปล่อย
        if (playerCols != null && myCols != null)
        {
            foreach (var pc in playerCols)
            {
                if (!pc) continue;
                foreach (var mc in myCols)
                {
                    if (!mc) continue;
                    Physics.IgnoreCollision(mc, pc, held);
                }
            }
        }
    }

    public void ApplyDamage(float dmg)
    {
        if (dmg <= 0f) return;
        CurrentHp = Mathf.Max(0f, CurrentHp - dmg);
        Value = Mathf.Max(0f, Value - (dmg * valueLossPerDamage));
        onDamaged?.Invoke();

        if (CurrentHp <= 0f)
        {
            onBroken?.Invoke();
            // ถ้าต้องการทำลาย:
            // Destroy(gameObject);
        }
    }

    void OnCollisionEnter(Collision c)
    {
        if (c == null) return;
        float relSpeed = c.relativeVelocity.magnitude;
        if (relSpeed < minDamageVelocity) return;

        float approxImpulse = rb.mass * relSpeed;
        float damage = approxImpulse * impactDamageMultiplier;
        if (damage > 0f) ApplyDamage(damage);
    }
}
