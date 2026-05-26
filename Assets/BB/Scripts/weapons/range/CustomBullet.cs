using UnityEngine;
using System.Collections.Generic;

public class CustomBullet : MonoBehaviour
{
    // Assignables
    public Rigidbody rb;
    public GameObject explosion;
    public LayerMask whatIsEnemies;

    // Stats
    [Range(0f,1f)] public float bounciness;
    public bool useGravity;

    // Damage
    public int explosionDamage = 3;
    public float explosionRange = 1.0f;   // make sure > 0 in Inspector
    public float explosionForce;

    // Lifetime
    public int maxCollisions = 1;
    public float maxLifetime = 3f;
    public bool explodeOnTouch = true;

    int collisions;
    PhysicMaterial physics_mat;

    // prevent double hits (direct + AOE, or multi-collider enemies)
    private readonly HashSet<EnemyAi> damagedThisShot = new HashSet<EnemyAi>();
    private bool hasExploded = false;

    void Start() => Setup();

    void Update()
    {
        if (hasExploded) return;  // stop logic after explode

        if (collisions > maxCollisions) Explode();

        maxLifetime -= Time.deltaTime;
        if (maxLifetime <= 0f) Explode();
    }

    // --- Helper: find EnemyAi on self, parent, or children ---
    private static EnemyAi FindEnemyOnHierarchy(Component c)
    {
        // Self
        var e = c.GetComponent<EnemyAi>();
        if (e) return e;
        // Parent (covers child hitboxes)
        e = c.GetComponentInParent<EnemyAi>();
        if (e) return e;
        // Children (covers cases where collider is parent, script on child)
        return c.GetComponentInChildren<EnemyAi>();
    }

    private void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        // disable bullet physics immediately so no repeat collisions
        var col = GetComponent<Collider>();
        if (col) col.enabled = false;
        if (rb)
        {
            rb.velocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (explosion) Instantiate(explosion, transform.position, Quaternion.identity);

        // --- AOE ---
        var cols = Physics.OverlapSphere(transform.position, explosionRange, whatIsEnemies);

        var unique = new HashSet<EnemyAi>();
        foreach (var c in cols)
        {
            var enemy = FindEnemyOnHierarchy(c);
            if (enemy) unique.Add(enemy);
        }

        foreach (var enemy in unique)
        {
            if (damagedThisShot.Contains(enemy)) continue; // already hit by direct
            enemy.TakeDamage(explosionDamage);
            // Debug.Log($"AOE hit {enemy.name} for {explosionDamage}");
            damagedThisShot.Add(enemy);

            if (explosionForce > 0f)
            {
                var rbEnemy = enemy.GetComponentInChildren<Rigidbody>() ?? enemy.GetComponent<Rigidbody>();
                if (rbEnemy) rbEnemy.AddExplosionForce(explosionForce, transform.position, explosionRange);
            }
        }

        Destroy(gameObject, 0.05f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasExploded) return;
        if (collision.collider.CompareTag("Bullet")) return;

        collisions++;

        // --- Direct-hit damage (covers 0-radius or tight explosions) ---
        var enemy = FindEnemyOnHierarchy(collision.collider);
        if (enemy && !damagedThisShot.Contains(enemy))
        {
            enemy.TakeDamage(explosionDamage);
            // Debug.Log($"Direct hit {enemy.name} for {explosionDamage}");
            damagedThisShot.Add(enemy);
        }

        if (explodeOnTouch) Explode();
    }

    private void Setup()
    {
        if (!rb) rb = GetComponent<Rigidbody>();

        physics_mat = new PhysicMaterial
        {
            bounciness = bounciness,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounceCombine = PhysicMaterialCombine.Maximum
        };

        var sphere = GetComponent<SphereCollider>();
        if (sphere) sphere.material = physics_mat;

        if (rb) rb.useGravity = useGravity;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRange);
    }
}
