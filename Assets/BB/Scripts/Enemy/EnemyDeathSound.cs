using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyDeathSound : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip enemyDeathSFX;

    public void PlayEnemyDeathSound()
    {
        if (audioSource != null && enemyDeathSFX != null)
        {
            audioSource.PlayOneShot(enemyDeathSFX);
        }
    }
}
