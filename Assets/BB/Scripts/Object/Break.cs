using System;
using System.Collections;
using UnityEngine;

public class Break : MonoBehaviour
{
    [Header("Timing (set in Inspector)")]
    [SerializeField] private float pieceSleepCheckDelay = 0.1f;  // เช็คหยุดนิ่งถี่แค่ไหน
    [SerializeField] private float destroyDelayAfterSleep = 3f;  // หน่วงก่อนเริ่มเฟด (หลังทุกชิ้นหยุดนิ่ง)
    [SerializeField] private float fadeDuration = 2f;            // ระยะเวลาเฟดหาย

    [Header("Physics")]
    [SerializeField] private bool enableGravityOnBreak = true;

    private bool isBroken = false;
    private bool destroyStarted = false;

    public bool IsBroken => isBroken;

    public void BreakAll()
    {
        if (isBroken) return;
        isBroken = true;

        // ปลดคิเนมาติกทุกชิ้นในกลุ่ม + ปลุกฟิสิกส์ให้แน่ใจว่าจะ "ตก"
        var bodies = GetGroupRigidbodies();
        foreach (var rb in bodies)
        {
            if (!rb) continue;
            rb.isKinematic = false;
            rb.useGravity  = enableGravityOnBreak;
            rb.WakeUp();
        }

        if (!destroyStarted)
        {
            destroyStarted = true;
            StartCoroutine(FadeOutRigidBodies(bodies));
        }
    }

    private IEnumerator FadeOutRigidBodies(Rigidbody[] rigidbodies)
    {
        if (rigidbodies == null || rigidbodies.Length == 0) yield break;

        // 1) รอจนทุกชิ้น sleep
        var wait = new WaitForSeconds(pieceSleepCheckDelay);
        int total = rigidbodies.Length;
        var slept = new bool[total];
        int sleptCount = 0;

        while (sleptCount < total)
        {
            yield return wait;

            for (int i = 0; i < rigidbodies.Length; i++)
            {
                var body = rigidbodies[i];
                if (!body || slept[i]) continue;

                // เช็คเฉพาะบอดี้ที่เป็น dynamic
                if (!body.isKinematic && body.IsSleeping())
                {
                    slept[i] = true;
                    sleptCount++;
                }
            }
        }

        // 2) หน่วงก่อนเฟด
        if (destroyDelayAfterSleep > 0f)
            yield return new WaitForSeconds(destroyDelayAfterSleep);

        // 3) เตรียมเรนเดอร์/ปิดคอลลายเดอร์/หยุดฟิสิกส์
        Renderer[] renderers = Array.ConvertAll(rigidbodies, GetRendererFromRigidbody);
        Collider[]  colliders = Array.ConvertAll(rigidbodies, GetColliderFromRigidbody);

        foreach (var col in colliders) if (col) col.enabled = false;

        foreach (var rb in rigidbodies)
        {
            if (!rb) continue;

            // กันเออเรอร์: เผื่อมีตัวไหนกลับเป็นคิเนมาติกไปแล้ว ให้ปลดชั่วคราวก่อนเซ็ตความเร็ว
            bool wasKinematic = rb.isKinematic;
            if (wasKinematic) rb.isKinematic = false;

            rb.velocity        = Vector3.zero;   // <-- จะไม่ขึ้น error แล้ว
            rb.angularVelocity = Vector3.zero;

            rb.isKinematic = true;
            rb.useGravity  = false;
        }

        foreach (var r in renderers)
        {
            if (!r) continue;

            // ทำ material instance
            r.material = new Material(r.material);
            TrySetMaterialToFade(r.material);
        }

        // 4) เฟดอัลฟา 1 -> 0
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            float alpha = 1f - k;

            foreach (var r in renderers)
            {
                if (!r) continue;
                var mat = r.material;

                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.color; c.a = alpha;
                    mat.color = c;
                }
                else if (mat.HasProperty("_BaseColor")) // URP Lit
                {
                    Color c = mat.GetColor("_BaseColor"); c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                else
                {
                    // ไม่มีพร็อพสีให้เฟด—เลื่อนลงเล็กน้อยแทน
                    r.transform.Translate(Vector3.down * Time.deltaTime * 0.2f, Space.World);
                }
            }
            yield return null;
        }

        // 5) ลบทุกชิ้นในกลุ่ม
        foreach (var rb in rigidbodies)
            if (rb) Destroy(rb.gameObject);
    }

    // ===== Utilities =====
    private Rigidbody[] GetGroupRigidbodies()
        => GetComponentsInChildren<Rigidbody>(true);

    private static Renderer GetRendererFromRigidbody(Rigidbody rb)
        => rb ? (rb.GetComponent<Renderer>() ?? rb.GetComponentInChildren<Renderer>()) : null;

    private static Collider GetColliderFromRigidbody(Rigidbody rb)
        => rb ? (rb.GetComponent<Collider>() ?? rb.GetComponentInChildren<Collider>()) : null;

    private static void TrySetMaterialToFade(Material mat)
    {
        if (!mat) return;

        // ตั้งโหมด Blend สำหรับเฟดอัลฟา
        if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 2f); // Fade (บางเชดเดอร์ของ Built-in)
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }
}
