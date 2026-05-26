using UnityEngine;
using TMPro;

public class HoverValueOverlayTMP : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;                        // ไม่ใส่จะ auto = Camera.main
    public RectTransform overlayRect;         // RectTransform ของ UI (อยู่บน Canvas: Screen Space - Overlay)
    public TMP_Text label;

    [Header("Raycast")]
    public LayerMask interactableLayers;      // Layer ของสิ่งของ (เช่น Interactive)
    public float maxDistance = 50f;
    public bool useSphereCast = true;
    public float hoverRadius = 0.25f;

    [Header("Screen placement")]
    public Vector2 screenOffset = new Vector2(0f, -20f);   // ขยับพิกเซลบนจอ
    public float followLerp = 20f;
    public bool clampToScreen = true;
    public Vector2 screenPadding = new Vector2(12, 12);

    public enum CenterMode { ColliderBounds, RendererBounds, RigidbodyCOM, TransformPivot }
    [Tooltip("วิธีหา 'จุดกลาง' ของวัตถุที่ชี้")]
    public CenterMode centerMode = CenterMode.ColliderBounds;

    [Header("Format")]
    public string currencyPrefix = "$";
    public string numberFormat = "N0";
    public bool hideWhenZero = true;

    [Header("Fade")]
    public float fadeSpeed = 12f;

    CanvasGroup cg;
    Vector3 targetScreenPos;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (overlayRect && !overlayRect.TryGetComponent(out cg))
            cg = overlayRect.gameObject.AddComponent<CanvasGroup>();
        HideImmediate();
    }

    void Update()
    {
        if (!cam || !overlayRect || !label) return;

        if (TryGetHover(out float value, out Vector3 centerWorld))
        {
            if (hideWhenZero && value <= 0f) { Hide(); return; }

            label.text = $"{currencyPrefix}{value.ToString(numberFormat)}";

            Vector3 sp = cam.WorldToScreenPoint(centerWorld);
            if (sp.z <= 0f) { Hide(); return; } // วัตถุอยู่หลังกล้อง

            targetScreenPos = sp + (Vector3)screenOffset;

            if (clampToScreen)
            {
                targetScreenPos.x = Mathf.Clamp(targetScreenPos.x, screenPadding.x, Screen.width  - screenPadding.x);
                targetScreenPos.y = Mathf.Clamp(targetScreenPos.y, screenPadding.y, Screen.height - screenPadding.y);
            }

            overlayRect.position = Vector3.Lerp(overlayRect.position, targetScreenPos, Time.deltaTime * followLerp);
            Show();
        }
        else Hide();
    }

    bool TryGetHover(out float value, out Vector3 centerWorld)
    {
        value = 0f; centerWorld = default;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        bool got = useSphereCast
            ? Physics.SphereCast(ray, hoverRadius, out hit, maxDistance, interactableLayers, QueryTriggerInteraction.Ignore)
            : Physics.Raycast(ray, out hit, maxDistance, interactableLayers, QueryTriggerInteraction.Ignore);
        if (!got) return false;

        // --- หา DragRigidbody/Tracker บน "วัตถุที่ถูกชี้" ---
        var tracker = hit.collider.GetComponentInParent<DragRigidbody.ImpactValueTracker>();
        var dr      = hit.collider.GetComponentInParent<DragRigidbody>();
        if (!tracker && !dr) return false;                 // ไม่ใช่วัตถุแบบเราก็ไม่โชว์

        // มูลค่า: ถ้ามี Tracker ใช้ค่าปัจจุบัน ไม่งั้นใช้ startValue จาก DragRigidbody ของชิ้นนั้น
        value = Mathf.Max(0f, tracker ? tracker.Value : dr.startValue);

        // รากของวัตถุที่ใช้คำนวณ "จุดกลาง" = ตัวที่มี DragRigidbody
        Transform root = (dr != null) ? dr.transform : hit.collider.transform;

        switch (centerMode)
        {
            case CenterMode.RigidbodyCOM:
            {
                var rb = root.GetComponentInParent<Rigidbody>();
                centerWorld = rb ? rb.worldCenterOfMass : root.position;
                break;
            }
            case CenterMode.RendererBounds:
            {
                var rends = root.GetComponentsInChildren<Renderer>(true);
                if (rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    centerWorld = b.center;
                }
                else centerWorld = root.position;
                break;
            }
            case CenterMode.TransformPivot:
                centerWorld = root.position;
                break;

            default: // ColliderBounds
            {
                var cols = root.GetComponentsInChildren<Collider>(true);
                if (cols.Length > 0)
                {
                    Bounds b = cols[0].bounds;
                    for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
                    centerWorld = b.center;
                }
                else centerWorld = root.position;
                break;
            }
        }
        return true;
    }

    void Show()
    {
        if (!overlayRect) return;
        if (cg) cg.alpha = Mathf.MoveTowards(cg.alpha, 1f, Time.deltaTime * fadeSpeed);
        else overlayRect.gameObject.SetActive(true);
    }
    void Hide()
    {
        if (!overlayRect) return;
        if (cg) cg.alpha = Mathf.MoveTowards(cg.alpha, 0f, Time.deltaTime * fadeSpeed);
        else overlayRect.gameObject.SetActive(false);
    }
    void HideImmediate()
    {
        if (!overlayRect) return;
        if (cg) cg.alpha = 0f; else overlayRect.gameObject.SetActive(false);
    }
}
