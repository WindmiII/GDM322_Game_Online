using UnityEngine;
using TMPro;

public class ValueLossPopupTMP : MonoBehaviour
{
    public TMP_Text label;          // จะ auto-fill ถ้าไม่ได้ลาก
    public float lifetime = 1.0f;
    public float risePixels = 32f;
    public AnimationCurve alphaByT = AnimationCurve.EaseInOut(0,1,1,0);
    public AnimationCurve riseByT  = AnimationCurve.EaseInOut(0,0,1,1);
    public Vector2 jitter = new Vector2(8f, 4f);

    RectTransform rect;
    Color labelStartColor = Color.white;
    Vector3 startScreenPos;
    float t;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        if (!label) label = GetComponentInChildren<TMP_Text>(true); // ✅ auto-assign
        if (label) labelStartColor = label.color;
    }

    public void Init(string text, Vector3 screenPos, Color color)
    {
        if (!rect) rect = GetComponent<RectTransform>();
        startScreenPos = screenPos + new Vector3(
            Random.Range(-jitter.x, jitter.x),
            Random.Range(-jitter.y, jitter.y), 0f);

        rect.position = startScreenPos;

        if (label)
        {
            label.text  = text;
            label.color = color;
        }
        t = 0f;
    }

    void Update()
    {
        // เดินเวลาเสมอ (ถึงไม่มี label ก็ไม่ค้าง)
        t += Time.deltaTime / Mathf.Max(0.0001f, lifetime);
        float k = Mathf.Clamp01(t);

        // ลอยขึ้น
        if (rect)
        {
            float y = risePixels * riseByT.Evaluate(k);
            rect.position = startScreenPos + new Vector3(0, y, 0);
        }

        // เฟด
        if (label)
        {
            var c = label.color;
            c.a = alphaByT.Evaluate(k);
            label.color = c;
        }

        if (k >= 1f) Destroy(gameObject);
    }
}
