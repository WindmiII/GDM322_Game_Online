using UnityEngine;


public class MonsterDeathParticle : MonoBehaviour
{
    [Header("Death Particle")]
    public GameObject deathParticle;   // ลาก prefab ใส่ใน Inspector

    public void SpawnDeathEffect(Vector3 pos)
    {
        if (deathParticle != null)
            Instantiate(deathParticle, pos, Quaternion.identity);
    }
}