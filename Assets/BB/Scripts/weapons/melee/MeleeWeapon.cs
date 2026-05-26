using UnityEngine;
using System.Collections;

public class MeleeWeapon : MonoBehaviour
{
    [Header("Stats")]
    public int damage = 20;
    public float cooldownTime = 2f;
    public float attackRange = 1.5f;

    [Header("Setup")]
    public Transform attackPoint;
    public LayerMask enemyLayers;

    [Header("Animation")]
    public Animator animator;

    [Header("Sound")]
    public AudioSource audioSource;      // <--- เพิ่ม AudioSource
    public AudioClip swingSound;         // <--- เสียงตอนฟัน (ไม่โดนก็เล่น)
    public AudioClip hitSound;           // <--- เสียงตอนโดนศัตรู

    private float nextAttackTime = 0f;

    void Update()
    {
        if (Time.time >= nextAttackTime)
        {
            if (Input.GetKeyDown(KeyCode.Mouse0) && transform.parent != null && transform.parent.gameObject.layer == LayerMask.NameToLayer("Player")) 
            {
                Attack();
                nextAttackTime = Time.time + cooldownTime;
            }
        }
    }

    void Attack()
    {
        // ⭐ เล่น Animation ⭐
        if (animator != null)
            animator.SetTrigger("Attack");

        // ⭐ เล่นเสียงฟันลม ⭐
        if (audioSource != null && swingSound != null)
            audioSource.PlayOneShot(swingSound);


        Vector3 pos = attackPoint != null ? attackPoint.position : transform.position;

        Collider[] hitEnemies = Physics.OverlapSphere(pos, attackRange, enemyLayers);

        foreach (Collider hit in hitEnemies)
        {
            EnemyAi enemyAi = hit.GetComponent<EnemyAi>();

            if (enemyAi != null)
            {
                enemyAi.TakeDamage(damage);

                // ⭐ เล่นเสียงโดนตี ⭐
                if (audioSource != null && hitSound != null)
                    audioSource.PlayOneShot(hitSound);

                Debug.Log("Hit Enemy!");

                // ถ้าอยากให้ตีโดนแค่ตัวเดียว:
                // break;
            }
        }
    }



    void OnDrawGizmosSelected()
    {
        if (attackPoint == null)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}