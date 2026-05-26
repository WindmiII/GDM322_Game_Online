using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AccessoryInventory))]
public class PlayerRepoLifter : MonoBehaviour
{
    [Header("Refs")]
    public Camera playerCamera;

    [Header("Base Ability")]
    [Tooltip("แรง/กำลังยกพื้นฐาน (ยิ่งมากยกของหนัก/ดึงเร็วขึ้น)")]
    public float baseStrength = 15f;
    [Tooltip("ระยะยกพื้นฐาน (เมตร)")]
    public float baseRange = 4f;

    [Header("Controls")]
    public KeyCode holdKey = KeyCode.Mouse0;   // ซ้ายค้าง = ยก/ถือ
    public KeyCode throwKey = KeyCode.Mouse1;  // ขว้าง
    public float holdDistance = 3f;
    public float minHoldDistance = 1.5f;
    public float maxHoldDistance = 8f;
    public float scrollSensitivity = 1f;

    [Header("Throw")]
    [Tooltip("แรงขว้าง (สเกลด้วย Strength/mass)")]
    public float throwPower = 8f;

    [Header("Detection")]
    public float aimRayLength = 12f;
    public LayerMask liftableMask = ~0;

    [Header("Spring Hold (Weight-aware)")]
    [Tooltip("ฐานสปริง (จะถูกสเกลด้วย Strength/mass)")]
    public float baseSpringK = 80f;
    [Tooltip("สเกล k ตาม (Strength / mass)^alpha")]
    [Range(0.2f, 2.0f)] public float springStrengthExponent = 0.8f;
    [Tooltip("ζ (zeta) สำหรับของเบา")]
    [Range(0.2f, 1.5f)] public float zetaLight = 0.6f;
    [Tooltip("ζ (zeta) สำหรับของหนัก")]
    [Range(0.2f, 2.0f)] public float zetaHeavy = 1.1f;
    [Tooltip("น้ำหนักเทียบ Strength ที่ถือว่า 'หนัก' (m ~ strength * this)")]
    public float heavyThresholdFactor = 0.9f;
    [Tooltip("เพดานความเร่งพื้นฐาน (จะสเกลด้วย Strength/น้ำหนัก)")]
    public float baseMaxAccel = 55f;
    public bool freezeRotationWhileHeld = true;

    AccessoryInventory inv;
    LiftableObject held;
    Rigidbody heldRb;
    Collider[] playerCols;

    void Awake()
    {
        inv = GetComponent<AccessoryInventory>();
        if (!playerCamera) playerCamera = Camera.main;
        playerCols = GetComponentsInChildren<Collider>();
    }

    void Update()
    {
        // ปรับระยะถือด้วยสกอลล์
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            holdDistance = Mathf.Clamp(holdDistance + scroll * scrollSensitivity,
                                       minHoldDistance, maxHoldDistance);
        }

        float effectiveStrength = baseStrength + inv.GetStrengthBonus();
        float effectiveRange = baseRange + inv.GetRangeBonus();

        // เริ่มยกเมื่อ "เริ่มกดซ้าย" และยังไม่ได้ถืออะไร
        if (Input.GetKeyDown(holdKey) && held == null)
            TryPick(effectiveStrength, effectiveRange);

        // ปล่อยเมื่อ "ปล่อยซ้าย"
        if (Input.GetKeyUp(holdKey) && held != null)
            Drop();

        // ขว้าง: คลิกขวาขณะถือ
        if (Input.GetKeyDown(throwKey) && held != null)
            Throw(effectiveStrength);
    }

    void FixedUpdate()
    {
        if (!held) return;

        Vector3 target = playerCamera.transform.position + playerCamera.transform.forward * holdDistance;

        Vector3 pos = heldRb.worldCenterOfMass;
        Vector3 toTarget = target - pos;

        float m = Mathf.Max(heldRb.mass, 0.1f);
        float effectiveStrength = baseStrength + inv.GetStrengthBonus();

        // คำนวณ k, ζ, c แบบ Weight-aware
        float k = baseSpringK * (effectiveStrength / m);
        float zeta = ComputeZeta(m, effectiveStrength);
        float c = ComputeDamperC(k, m, zeta);

        // a = (k/m)*dx - (c/m)*v   (อยากหยุดนิ่งที่เป้า → desiredVel = 0)
        Vector3 accel = (k / m) * toTarget - (c / m) * heldRb.velocity;

        // จำกัดความเร่งตาม Strength/น้ำหนัก กันสะบัด
        float maxA = ComputeMaxAccel(m, effectiveStrength);
        if (accel.sqrMagnitude > maxA * maxA)
            accel = accel.normalized * maxA;

        heldRb.AddForce(accel, ForceMode.Acceleration);

        if (freezeRotationWhileHeld)
            heldRb.angularVelocity = Vector3.Lerp(heldRb.angularVelocity, Vector3.zero, 0.6f);
    }

    void TryPick(float effectiveStrength, float effectiveRange)
    {
        if (!playerCamera) return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(aimRayLength, effectiveRange), liftableMask, QueryTriggerInteraction.Ignore))
            return;

        var obj = hit.collider.GetComponentInParent<LiftableObject>();
        if (!obj) return;

        float dist = Vector3.Distance(playerCamera.transform.position, obj.transform.position);
        if (dist > effectiveRange) return;

        var rb = obj.GetComponent<Rigidbody>();
        if (!rb) return;

        // ยกได้ยาก/ง่ายตาม Strength/Weight (ต่ำกว่า 0.3 = ไม่ยก)
        float ratio = effectiveStrength / Mathf.Max(0.1f, rb.mass);
        if (ratio < 0.3f) return;

        held = obj;
        heldRb = rb;
        held.SetHeld(true, playerCols);

        holdDistance = Mathf.Clamp(dist, minHoldDistance, maxHoldDistance);
    }

    void Drop()
    {
        if (!held) return;
        held.SetHeld(false, playerCols);
        held = null;
        heldRb = null;
    }

    void Throw(float effectiveStrength)
    {
        if (!held) return;

        // ปล่อยก่อนแล้วขว้างไปตามทิศกล้อง
        held.SetHeld(false, playerCols);
        Vector3 dir = playerCamera.transform.forward;
        float power = throwPower * Mathf.Clamp01(effectiveStrength / Mathf.Max(0.1f, heldRb.mass));
        heldRb.AddForce(dir * power, ForceMode.VelocityChange);

        held = null;
        heldRb = null;
    }

    // ---------- Weight-aware spring helpers ----------

    float ComputeSpringK(float mass, float effectiveStrength)
    {
        // k = baseK * (Strength / mass)^alpha (จำกัดช่วง)
        float ratio = Mathf.Max(0.05f, effectiveStrength / Mathf.Max(0.1f, mass));
        float k = baseSpringK * Mathf.Pow(ratio, springStrengthExponent);
        return Mathf.Clamp(k, 20f, 300f);
    }

    float ComputeZeta(float mass, float effectiveStrength)
    {
        // หนักกว่า strength*factor → ใช้ zeta ใกล้ zetaHeavy
        float heavyPoint = Mathf.Max(0.1f, effectiveStrength) * Mathf.Max(0.1f, heavyThresholdFactor);
        float t = Mathf.Clamp01(mass / (mass + heavyPoint)); // mass >> heavyPoint ⇒ t→1
        return Mathf.Lerp(zetaLight, zetaHeavy, t);
    }

    float ComputeDamperC(float k, float mass, float zeta)
    {
        // c = 2 * zeta * sqrt(k*m)
        return 2f * zeta * Mathf.Sqrt(Mathf.Max(1e-3f, k * Mathf.Max(0.1f, mass)));
    }

    float ComputeMaxAccel(float mass, float effectiveStrength)
    {
        // เพดานความเร่งสเกลด้วย (Strength / mass)
        float ratio = Mathf.Clamp(effectiveStrength / Mathf.Max(0.1f, mass), 0.2f, 3f);
        return baseMaxAccel * ratio;
    }
}