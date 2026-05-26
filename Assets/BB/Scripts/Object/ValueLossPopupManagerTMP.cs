using UnityEngine;

public class ValueLossPopupManagerTMP : MonoBehaviour
{
    public Camera cam;                         // กล้องหลัก (ว่างไว้จะ auto = Camera.main)
    public RectTransform overlayRoot;          // พาเรนต์ใน Canvas (Screen Space – Overlay)
    public ValueLossPopupTMP popupPrefab;      // พรีแฟบ TMP ที่มี ValueLossPopupTMP

    [Header("Format")]
    public string currencyPrefix = "$";
    public string numberFormat = "N0";         // N0=1,234  | N2=1,234.56
    public Color lossColor = new Color(1f, 0.3f, 0.3f, 1f);

    public enum CenterMode { ColliderBounds, RendererBounds, RigidbodyCOM, TransformPivot }
    public CenterMode centerMode = CenterMode.ColliderBounds;

    void Awake()
    {
        if (!cam) cam = Camera.main;
    }

    void OnEnable()
    {
        DragRigidbody.ImpactValueTracker.OnValueLost += HandleValueLost;
    }

    void OnDisable()
    {
        DragRigidbody.ImpactValueTracker.OnValueLost -= HandleValueLost;
    }

    void HandleValueLost(Transform objRoot, float loss)
    {
        if (!overlayRoot || !popupPrefab || !objRoot || loss <= 0f) return;

        Vector3 centerWorld = ComputeCenter(objRoot, centerMode);
        Vector3 sp = cam.WorldToScreenPoint(centerWorld);
        if (sp.z <= 0f) return; // อยู่หลังกล้อง

        var popup = Instantiate(popupPrefab, overlayRoot);
        popup.Init($"-{currencyPrefix}{loss.ToString(numberFormat)}", sp, lossColor);
    }

    static Vector3 ComputeCenter(Transform root, CenterMode mode)
    {
        if (!root) return Vector3.zero;

        switch (mode)
        {
            case CenterMode.RigidbodyCOM:
                {
                    var rb = root.GetComponentInParent<Rigidbody>();
                    return rb ? rb.worldCenterOfMass : root.position;
                }
            case CenterMode.RendererBounds:
                {
                    var rends = root.GetComponentsInChildren<Renderer>(true);
                    if (rends.Length > 0)
                    {
                        Bounds b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        return b.center;
                    }
                    return root.position;
                }
            case CenterMode.TransformPivot:
                return root.position;

            default: // ColliderBounds
                {
                    var cols = root.GetComponentsInChildren<Collider>(true);
                    if (cols.Length > 0)
                    {
                        Bounds b = cols[0].bounds;
                        for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
                        return b.center;
                    }
                    return root.position;
                }
        }
    }
}
