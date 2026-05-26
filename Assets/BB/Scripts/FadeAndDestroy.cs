using UnityEngine;

public class FadeAndDestroy : MonoBehaviour
{
    [Header("Fade Settings")]
    [SerializeField][Range(0,10)]public float lifeTime = 2f;          // เวลารวมก่อน Destroy
    [SerializeField][Range(0,10)]public float fadeDuration = 1f;      // ระยะเวลาที่ใช้ค่อยๆจางหาย

    private float timer;
    private Material material;
    private Color originalColor;

    void Start()
    {
        // clone material so only this object fades
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            material = renderer.material;
            originalColor = material.color;
        }
        timer = lifeTime;
    }

    void Update()
    {
        timer -= Time.deltaTime;

        if (timer <= fadeDuration && material != null)
        {
            float alpha = Mathf.Clamp01(timer / fadeDuration);
            Color newColor = originalColor;
            newColor.a = alpha;
            material.color = newColor;
        }

        if (timer <= 0f)
        {
            Destroy(gameObject);
        }
    }
}
