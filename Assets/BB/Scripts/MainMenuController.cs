using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using System.Collections;

public class MainMenuController : MonoBehaviour
{
    [Header("Timeline Settings")]
    public PlayableDirector director;
    public PlayableAsset introTimeline;
    public PlayableAsset outroTimeline;

    [Header("UI Panels")]
    public GameObject mainPanel;
    public GameObject levelSelectPanel;

    private bool isTransitioning = false;

    private LoadingScreen loadingScreen;

    void Start()
    {
        mainPanel.SetActive(true);
        levelSelectPanel.SetActive(false);

        loadingScreen = FindObjectOfType<LoadingScreen>();

        if (introTimeline != null)
        {
            director.playableAsset = introTimeline;
            director.Play();
        }
    }

    public void OpenLevelSelection()
    {
        mainPanel.SetActive(false);
        levelSelectPanel.SetActive(true);

    }

    public void BackToMain()
    {
        mainPanel.SetActive(true);
        levelSelectPanel.SetActive(false);
    }

    public void SelectMapAndStart(string sceneName)
    {
        if (isTransitioning) return;

        StartCoroutine(PlayOutroAndLoad(sceneName));
    }

    IEnumerator PlayOutroAndLoad(string sceneName)
    {
        isTransitioning = true;

        levelSelectPanel.SetActive(false);

        if (outroTimeline != null)
        {
            director.Stop();
            director.playableAsset = outroTimeline;
            director.Play();

            yield return new WaitForSeconds((float)outroTimeline.duration);
        }

        if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.LoadSceneWithFade(sceneName);
        }
        else if (loadingScreen != null)
        {
            loadingScreen.LoadSceneWithFade(sceneName);
        }
        else
        {
            // กันเหนียว: ถ้าหาตัวโหลดไม่เจอจริงๆ ให้โหลดสดเลย
            Debug.LogWarning("ไม่เจอ LoadingScreen ใน Scene เมนู! โหลดแบบปกติแทน");
            SceneManager.LoadScene(sceneName);
        }
    }
}