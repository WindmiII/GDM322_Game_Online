using UnityEngine;
using UnityEngine.SceneManagement;

public class ExitGate : MonoBehaviour
{
    [Header("Settings")]
    public string sceneToLoadName = "Map2";

    [Header("Components")]
    public Collider gateCollider;

    [Tooltip("ตัว MeshRenderer ของประตู (สำหรับเปลี่ยน Material)")]
    public MeshRenderer gateRenderer;

    [Header("Materials (เปลี่ยนสีตอนเปิด/ปิด)")]
    public Material closedMaterial;
    public Material openMaterial;

    [Header("Effects")]
    public GameObject openEffect;

    private LoadingScreen loadingScreen;

    private bool isOpen = false;

    void Start()
    {
        // เซ็ต Material ตอนเริ่มเกม
        if (gateRenderer != null && closedMaterial != null)
        {
            gateRenderer.material = closedMaterial;
        }

        // Collider
        if (gateCollider != null)
        {
            gateCollider.enabled = true;
            gateCollider.isTrigger = false;
        }

        // Effect ตอนเปิด
        if (openEffect != null) openEffect.SetActive(false);

        // หา LoadingScreen
        loadingScreen = FindObjectOfType<LoadingScreen>();
    }

    // เรียกจาก Unity Event ว่าเปิดประตูแล้ว
    public void OpenGate()
    {
        if (isOpen) return;
        isOpen = true;

        // เปลี่ยนเป็น Trigger เพื่อใช้ OnTriggerEnter
        if (gateCollider != null)
        {
            gateCollider.isTrigger = true;
        }

        // เปลี่ยน Material
        if (gateRenderer != null && openMaterial != null)
        {
            gateRenderer.material = openMaterial;
        }

        // แสดงเอฟเฟกต์ตอนเปิด
        if (openEffect != null)
        {
            openEffect.SetActive(true);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isOpen) return;

        if (other.CompareTag("Player") || other.GetComponent<DragRigidbody>())
        {
            if (!string.IsNullOrEmpty(sceneToLoadName))
            {
                if (loadingScreen != null)
                {
                    loadingScreen.LoadSceneWithFade(sceneToLoadName);
                }
                else
                {
                    SceneManager.LoadScene(sceneToLoadName);
                }
            }
        }
    }
}