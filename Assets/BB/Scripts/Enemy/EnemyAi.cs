using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public struct GizmoColors
{
    public Color patrolRangeColor;
    public Color sightRangeColor;
    public Color playerAttackRangeColor;
    public Color meleeRangeColor;
    public Color objectAttackRangeColor;
    public Color fovColor;
}

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAi : MonoBehaviour
{
    public enum AIState { Patrol, Spotted, Chase, Attack, Wait, ObjAttack, Search }
    [Header("Current State")]
    public AIState currentState;
    private AIState stateAfterWait;

    public enum TargetMode { PlayerOnly, ObjectsOnly, Both_PlayerPriority, Both_ObjectPriority }
    public enum AttackStyle { RangedOnly, MeleeOnly, Adaptive }

    [Header("References")]
    public Animator animator;

    [Header("Animation Settings")]
    public string speedParamName = "Speed";
    public string spottedTriggerName = "Spotted";
    public string meleeTriggerName = "AttackMelee";
    public string rangedTriggerName = "AttackRanged";
    public string objectAttackTriggerName = "ObjAttack";

    [Header("Gizmo Colors")]
    public GizmoColors gizmoColors = new GizmoColors
    {
        patrolRangeColor = Color.grey,
        sightRangeColor = Color.yellow,
        playerAttackRangeColor = Color.red,
        meleeRangeColor = new Color(1, 0.5f, 0),
        objectAttackRangeColor = Color.magenta,
        fovColor = Color.cyan
    };

    [Header("Loot Drop")]
    public GameObject itemToDrop;

    [Header("Possible item prefabs in the whole game")]
    public List<GameObject> allItemPrefabs; // ลิสต์รวมของทุกไอเทมในเกม

    [Header("Core")]
    public NavMeshAgent agent;
    public Transform player;
    public LayerMask whatIsGround, whatIsPlayer;

    [Header("Health")]
    public float health = 50f;

    [Header("Patrol")]
    public float walkPointRange = 10f;
    private Vector3 walkPoint;
    private bool walkPointSet;
    private NavMeshPath path;

    [Header("Patrol Wait")]
    public bool waitAtPoints = true;
    public Vector2 waitTimeRange = new Vector2(5f, 10f);
    private float waitTimer;

    [Header("Spotted Settings")]
    public float spottedDuration = 1.5f;
    private bool hasSpottedPlayer = false;

    [Header("Last Seen System")]
    public Vector3 lastSeenPosition;
    public float searchDuration = 3f;
    private float searchTimer = 0f;

    [Header("Attack Settings")]
    public AttackStyle attackStyle = AttackStyle.Adaptive;
    public float timeBetweenAttacks = 1.25f;
    public float sightRange = 15f;

    [Header("Ranged Attack")]
    public float rangedAttackRange = 10f;
    public float rangedAttackDelay = 0.5f;
    public GameObject projectile;
    public float projectileSpeed = 30f;
    public float projectileForwardOffset = 1.2f;
    public float projectileVerticalOffset = 1.0f;
    public float projectileRightOffset = 0f;

    [Header("Melee Attack")]
    public float meleeAttackRange = 2.5f;
    public int meleeDamage = 15;
    public float meleeAttackDelay = 0.5f;

    [Header("Object Attack")]
    public float objectAttackRange = 2.2f;
    public float objectDamage = 15f;

    [Header("Objects Targeting")]
    public TargetMode targetMode = TargetMode.Both_PlayerPriority;
    public LayerMask whatIsObject;

    [Header("Sight / FOV")]
    public bool useConeSight = true;
    [Range(10f, 180f)] public float fovAngle = 90f;
    public LayerMask obstructionMask; // อย่าลืมตั้งเป็น Default หรือ Layer ของกำแพง

    [Header("Attack Sounds")]
    public AudioSource audioSource;
    public AudioClip meleeAttackSFX;
    public AudioClip rangedAttackSFX;
    public AudioClip hitPlayerSFX;   // เสียงโดนผู้เล่น

    [Header("Idle Sounds")]
    public AudioSource idleSource;
    public AudioClip[] idleClips;
    public float idleMinDelay = 3f;
    public float idleMaxDelay = 8f;

    [Header("Footstep Sounds")]
    public AudioSource footstepSource;
    public AudioClip[] walkFootsteps;
    public AudioClip[] runFootsteps;
    public float footstepIntervalWalk = 0.6f;
    public float footstepIntervalRun = 0.35f;

    private Transform currentTarget;
    private bool targetIsPlayer;
    private bool isAttacking = false;
    private float idleSoundTimer = 0f;
    private float nextIdleSoundTime = 0f;
    private float footstepTimer = 0f;

    public EnemyDeathSound soundManager;

    [Header("Particle")]
    public MonsterDeathParticle monsterDeathParticle;


    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
        if (animator == null) animator = GetComponent<Animator>();

        if (!player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }
    }

    private void Start()
    {
        ChangeState(AIState.Patrol);
        soundManager = FindObjectOfType<EnemyDeathSound>();
    }

    private void Update()
    {
        UpdateIdleSound();
        UpdateFootsteps();

        if (!agent.enabled) return;

        if (animator != null && !string.IsNullOrEmpty(speedParamName))
        {
            animator.SetFloat(speedParamName, agent.velocity.magnitude);
        }

        switch (currentState)
        {
            case AIState.Patrol: PatrolState(); break;
            case AIState.Spotted: SpottedState(); break;
            case AIState.Search: SearchState(); break;
            case AIState.Chase: ChaseState(); break;
            case AIState.Attack: AttackState(); break;
            case AIState.Wait: WaitState(); break;
        }

    }

    private void PlayAnimationTrigger(string triggerName)
    {
        if (animator != null && !string.IsNullOrEmpty(triggerName))
        {
            animator.SetTrigger(triggerName);
        }
    }

    private void ChangeState(AIState newState)
    {
        if (currentState == newState) return;
        if (currentState == AIState.Patrol && newState != AIState.Patrol)
        {
            agent.isStopped = false;
        }
        currentState = newState;
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    // ---------------------------------------------------------
    // 💡 1. แก้ไข IsTargetVisible ให้วาดเส้นชัดเจน และแม่นยำ
    // ---------------------------------------------------------
    private bool IsTargetVisible(Transform target)
    {
        if (target == null) return false;

        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > sightRange) return false;

        if (useConeSight)
        {
            Vector3 directionToTarget = (target.position - transform.position).normalized;
            if (directionToTarget == Vector3.zero) return false;
            if (Vector3.Angle(transform.forward, directionToTarget) > fovAngle / 2)
            {
                return false;
            }
        }

        // จุดเริ่ม: ตา Enemy (สูงจากเท้า 1.5 เมตร)
        Vector3 eyePosition = transform.position + Vector3.up * 1.5f;
        // จุดจบ: กลางอก เป้าหมาย (สูงจากเท้า 1.0 เมตร) --> ช่วยให้มองข้ามสิ่งกีดขวางเตี้ยๆ ได้ แต่ไม่ทะลุกำแพง
        Vector3 targetCenter = target.position + Vector3.up * 1.0f;

        Vector3 dir = targetCenter - eyePosition;
        float distToTarget = dir.magnitude;

        // ยิง Raycast
        if (Physics.Raycast(eyePosition, dir.normalized, out RaycastHit hit, distToTarget, obstructionMask))
        {
            // ถ้าชน "ตัวเอง" หรือ "เป้าหมาย" ถือว่าผ่าน (มองเห็น)
            if (hit.transform == target || hit.transform.IsChildOf(target) || (player != null && hit.transform == player))
            {
                Debug.DrawLine(eyePosition, targetCenter, Color.green); // เส้นเขียว = เห็น
                return true;
            }

            // ถ้าชนอย่างอื่น (เช่น กำแพง)
            Debug.DrawLine(eyePosition, hit.point, Color.red); // เส้นแดง = ติดกำแพง
            return false;
        }

        // ถ้าไม่ชนอะไรเลยใน Layer Mask (มองเห็นโล่งๆ)
        Debug.DrawLine(eyePosition, targetCenter, Color.green);
        return true;
    }

    // ---------------------------------------------------------
    // 💡 แก้ไข SerchState: ถ้าไม่เห็น (เส้นแดง) ต้องเลิกไล่
    // ---------------------------------------------------------
    private void SearchState()
    {
        // ไปยังจุดสุดท้ายที่เห็น player
        agent.SetDestination(lastSeenPosition);

        // ถ้าถึงจุดแล้ว ค้นหาสักพัก
        if (!agent.pathPending && agent.remainingDistance <= 1.2f)
        {
            searchTimer += Time.deltaTime;

            // ถ้าค้นหาเกินเวลาที่กำหนด → กลับ Patrol
            if (searchTimer >= searchDuration)
            {
                searchTimer = 0f;
                ChangeState(AIState.Patrol);
            }

            // ระหว่างค้นหา → หมุนหัวมองหาผู้เล่น
            transform.Rotate(Vector3.up * 60f * Time.deltaTime);

            // ถ้าจู่ๆ เห็น Player → ไล่ต่อทันที
            if (IsTargetVisible(player))
            {
                currentTarget = player;
                targetIsPlayer = true;
                ChangeState(AIState.Chase);
            }
        }
    }
    // ---------------------------------------------------------
    // 💡 2. แก้ไข ChaseState: ถ้าไม่เห็น (เส้นแดง) ต้องเลิกไล่
    // ---------------------------------------------------------
    private void ChaseState()
    {
        // ---------------------------------------------------------
        // 1) ถ้าไม่มีเป้าหมาย → กลับไป Patrol
        // ---------------------------------------------------------
        if (currentTarget == null)
        {
            ChangeState(AIState.Patrol);
            return;
        }

        // ---------------------------------------------------------
        // 2) ถ้าเป้าหมายคือ Player → เช็กว่ามองเห็นหรือไม่
        // ---------------------------------------------------------
        if (targetIsPlayer)
        {
            if (IsTargetVisible(currentTarget))
            {
                // **อัปเดต lastSeenPosition ทุกครั้งที่มองเห็น**
                lastSeenPosition = currentTarget.position;
            }
            else
            {
                // ❌ มองไม่เห็น → เปลี่ยนไป Search State
                ChangeState(AIState.Search);
                return;
            }
        }
        else
        {
            // ถ้าเป้าหมายเป็นสิ่งของ แต่หายไป → ยกเลิกแล้วกลับ Patrol
            if (!IsTargetVisible(currentTarget))
            {
                ChangeState(AIState.Patrol);
                return;
            }
        }

        // ---------------------------------------------------------
        // 3) เดินไปหาเป้าหมาย (Player หรือ Object)
        // ---------------------------------------------------------
        if (targetIsPlayer)
        {
            agent.SetDestination(currentTarget.position);
        }
        else
        {
            // ของบนโต๊ะ → หา NavMesh Hit ตรงพื้น
            if (NavMesh.SamplePosition(currentTarget.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
            else
            {
                agent.SetDestination(currentTarget.position);
            }
        }

        // ---------------------------------------------------------
        // 4) เช็กระยะโจมตี
        // ---------------------------------------------------------
        float stopDistance = targetIsPlayer ? rangedAttackRange : objectAttackRange;

        if (targetIsPlayer && attackStyle == AttackStyle.MeleeOnly)
            stopDistance = meleeAttackRange;

        if (!agent.pathPending && agent.remainingDistance <= stopDistance)
        {
            ChangeState(AIState.Attack);
            return;
        }
    }

    // ---------------------------------------------------------
    // 💡 3. แก้ไข AttackState: ต้องเช็ค Visibility ตลอดเวลา!
    // ---------------------------------------------------------
    private void AttackState()
    {
        agent.SetDestination(transform.position); // หยุดเดิน

        if (currentTarget != null)
        {
            // หันหน้าไปหา
            Vector3 targetPos = new Vector3(currentTarget.position.x, transform.position.y, currentTarget.position.z);
            transform.LookAt(targetPos);

            // --- ส่วนที่เพิ่มเข้ามา (สำคัญมาก) ---
            // ถ้าจู่ๆ มองไม่เห็น (เช่น วิ่งหลบหลังกำแพง) ให้กลับไป Chase (ซึ่ง Chase จะเช็คต่อแล้วดีดกลับ Patrol)
            if (!IsTargetVisible(currentTarget))
            {
                ChangeState(AIState.Chase);
                return;
            }
            // ----------------------------------

            float dist = Vector3.Distance(transform.position, currentTarget.position);
            float maxCombatRange = targetIsPlayer ?
                (attackStyle == AttackStyle.MeleeOnly ? meleeAttackRange : rangedAttackRange) :
                objectAttackRange;

            // ถ้าเป้าหมายวิ่งหนีออกนอกระยะ
            if (dist > maxCombatRange + 1.5f)
            {
                ChangeState(AIState.Chase);
                return;
            }

            if (!isAttacking)
            {
                if (targetIsPlayer) StartCoroutine(AttackPlayerRoutine(dist));
                else AttackObject();
            }
        }
        else
        {
            ChangeState(AIState.Patrol);
        }
    }

    // ... (ส่วนอื่นๆ เหมือนเดิม ไม่มีการเปลี่ยนแปลง logic สำคัญ) ...

    private void PatrolState()
    {
        hasSpottedPlayer = false;
        currentTarget = FindTarget();

        if (currentTarget != null)
        {
            if (targetIsPlayer && !hasSpottedPlayer)
            {
                ChangeState(AIState.Spotted);
            }
            else
            {
                ChangeState(AIState.Chase);
            }
            return;
        }

        if (!walkPointSet) SearchWalkPoint();

        if (walkPointSet)
        {
            agent.SetDestination(walkPoint);
            if (!agent.pathPending && agent.remainingDistance < 1.0f)
            {
                walkPointSet = false;
                if (waitAtPoints)
                {
                    waitTimer = Random.Range(waitTimeRange.x, waitTimeRange.y);
                    stateAfterWait = AIState.Patrol;
                    ChangeState(AIState.Wait);
                }
            }
        }
    }

    private void SpottedState()
    {
        agent.SetDestination(transform.position);
        agent.isStopped = true;
        if (!isAttacking) StartCoroutine(SpottedRoutine());
    }

    IEnumerator SpottedRoutine()
    {
        isAttacking = true;
        hasSpottedPlayer = true;
        PlayAnimationTrigger(spottedTriggerName);

        float timer = 0f;
        while (timer < spottedDuration)
        {
            if (currentTarget != null)
            {
                Vector3 direction = (currentTarget.position - transform.position).normalized;
                direction.y = 0;
                if (direction != Vector3.zero)
                {
                    Quaternion lookRot = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
                }
            }
            timer += Time.deltaTime;
            yield return null;
        }

        isAttacking = false;
        agent.isStopped = false;
        ChangeState(AIState.Chase);
    }

    IEnumerator AttackPlayerRoutine(float dist)
    {
        isAttacking = true;
        bool doMelee = false;

        if (attackStyle == AttackStyle.MeleeOnly) doMelee = true;
        else if (attackStyle == AttackStyle.RangedOnly) doMelee = false;
        else if (attackStyle == AttackStyle.Adaptive)
        {
            if (dist <= meleeAttackRange) doMelee = true;
            else doMelee = false;
        }

        if (doMelee)
        {
            PlayAnimationTrigger(meleeTriggerName);
            PlaySFX(meleeAttackSFX);   // 🔊 เสียงตอนเหวี่ยงโจมตี
            yield return new WaitForSeconds(meleeAttackDelay);
            if (player != null && Vector3.Distance(transform.position, player.position) <= meleeAttackRange + 1.5f)
            {
                PlayerHealth ph = player.GetComponent<PlayerHealth>();
                if (ph != null) ph.TakeDamage(meleeDamage);
                PlaySFX(hitPlayerSFX); // 🔊 เสียงผู้เล่นโดนตี
            }
        }
        else
        {
            PlayAnimationTrigger(rangedTriggerName);
            PlaySFX(rangedAttackSFX); // 🔊 เสียงยิงลูกธนู / ปล่อยพลัง
            yield return new WaitForSeconds(rangedAttackDelay);
            if (projectile != null && player != null)
            {
                Vector3 spawnPos = transform.position
                                   + transform.forward * projectileForwardOffset
                                   + transform.right * projectileRightOffset
                                   + Vector3.up * projectileVerticalOffset;

                // 💡 เล็งที่อก (Chest) เหมือน Raycast
                Vector3 targetHeadPos = player.position + Vector3.up * 1.0f;
                Vector3 direction = (targetHeadPos - spawnPos).normalized;

                Quaternion spawnRot = Quaternion.LookRotation(direction, Vector3.up);
                GameObject proj = Instantiate(projectile, spawnPos, spawnRot);

                Collider myCol = GetComponent<Collider>();
                Collider projCol = proj.GetComponent<Collider>();
                if (myCol && projCol) Physics.IgnoreCollision(myCol, projCol);

                if (proj.GetComponent<Rigidbody>() is Rigidbody prb)
                {
                    prb.velocity = direction * projectileSpeed;
                }
            }
        }

        yield return new WaitForSeconds(timeBetweenAttacks);
        isAttacking = false;
    }

    private void WaitState()
    {
        if (agent.enabled && agent.isOnNavMesh) agent.SetDestination(transform.position);
        waitTimer -= Time.deltaTime;
        if (waitTimer <= 0f) ChangeState(stateAfterWait);
    }

    void AttackObject()
    {
        if (currentTarget == null) return;
        var dragScript = currentTarget.GetComponent<DragRigidbody>();
        if (dragScript == null) return;

        var tracker = dragScript.gameObject.GetComponent<DragRigidbody.ImpactValueTracker>();
        if (tracker == null)
        {
            tracker = dragScript.gameObject.AddComponent<DragRigidbody.ImpactValueTracker>();
            tracker.Configure(
                dragScript.startValue,
                dragScript.impactDamageMultiplier,
                dragScript.valueLossPerDamage,
                dragScript.minDamageVelocity,
                dragScript.maxValueLossPerHit,
                dragScript.postReleaseDamageWindow,
                dragScript.minThrowSpeedToArm,
                dragScript.brokenPrefab,
                dragScript.inheritVelocityToPieces,
                dragScript.pieceVelocityMultiplier,
                dragScript.isEssentialItem
            );
        }
        PlayAnimationTrigger(objectAttackTriggerName);
        tracker.ApplyExternalDamage(objectDamage);
        waitTimer = timeBetweenAttacks;
        stateAfterWait = AIState.Chase;
        ChangeState(AIState.Wait);
    }

    public void TakeDamage(int damage)
    {
        health -= damage;
        Debug.Log("Enemy HP: " + health);
        if (health <= 0)
        {
            DestroyEnemy();
            DropQuest();
            Die();
            soundManager.PlayEnemyDeathSound();
        }
    }
    public void DropQuest()
    {
        if (itemToDrop != null)
        {
            Vector3 dropPosition = transform.position; dropPosition.y += 0.5f;
            Instantiate(itemToDrop, dropPosition, Quaternion.identity);
            Debug.Log("dompi");
        }

    }

    public void DestroyEnemy()
    {
        Destroy(gameObject);
    }
    private Transform FindTarget() { return FindTargetOriginal(); }

    // รวม Logic หาของบนโต๊ะในนี้แล้ว
    private Transform FindNearestVisibleObject()
    {
        if (targetMode == TargetMode.PlayerOnly) return null;
        if (!agent.enabled || !agent.isOnNavMesh) return null;

        var hits = Physics.OverlapSphere(transform.position, sightRange, whatIsObject);
        if (hits.Length == 0) return null;

        Transform best = null;
        float closest = Mathf.Infinity;

        foreach (var hit in hits)
        {
            Transform t = hit.attachedRigidbody ? hit.attachedRigidbody.transform : hit.transform;

            if (IsTargetVisible(t))
            {
                float dist = Vector3.Distance(transform.position, t.position);
                if (dist < closest)
                {
                    if (NavMesh.SamplePosition(t.position, out NavMeshHit navHit, 3.0f, NavMesh.AllAreas))
                    {
                        if (agent.enabled && agent.isOnNavMesh &&
                            agent.CalculatePath(navHit.position, path) &&
                            path.status == NavMeshPathStatus.PathComplete)
                        {
                            closest = dist;
                            best = t;
                        }
                    }
                }
            }
        }
        return best;
    }

    private Transform FindTargetOriginal()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return null;
        Transform playerCand = null;
        bool allowsPlayer = targetMode != TargetMode.ObjectsOnly;
        if (allowsPlayer && IsTargetVisible(player))
        {
            if (agent.enabled && agent.isOnNavMesh && agent.CalculatePath(player.position, path) && path.status == NavMeshPathStatus.PathComplete) playerCand = player;
        }
        Transform objectCand = FindNearestVisibleObject();
        switch (targetMode)
        {
            case TargetMode.PlayerOnly: targetIsPlayer = true; return playerCand;
            case TargetMode.ObjectsOnly: targetIsPlayer = false; return objectCand;
            case TargetMode.Both_PlayerPriority: if (playerCand) { targetIsPlayer = true; return playerCand; } targetIsPlayer = false; return objectCand;
            case TargetMode.Both_ObjectPriority: if (objectCand) { targetIsPlayer = false; return objectCand; } targetIsPlayer = true; return playerCand;
            default: return null;
        }
    }

    private void SearchWalkPoint()
    {
        if (!agent.enabled || !agent.isOnNavMesh) { walkPointSet = false; return; }
        for (int i = 0; i < 20; i++)
        {
            Vector3 rnd = transform.position + Random.insideUnitSphere * walkPointRange;
            if (NavMesh.SamplePosition(rnd, out NavMeshHit hit, walkPointRange, NavMesh.AllAreas))
            {
                if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    walkPoint = hit.position; walkPointSet = true; return;
                }
            }
        }
        walkPointSet = false;
    }


    private void UpdateIdleSound()
    {
        if (currentState != AIState.Wait && currentState != AIState.Patrol)
            return;

        idleSoundTimer += Time.deltaTime;

        if (idleSoundTimer >= nextIdleSoundTime)
        {
            idleSoundTimer = 0;

            if (idleClips.Length > 0)
            {
                idleSource.pitch = Random.Range(0.95f, 1.05f);
                idleSource.PlayOneShot(idleClips[Random.Range(0, idleClips.Length)]);
            }

            nextIdleSoundTime = Random.Range(idleMinDelay, idleMaxDelay);
        }
    }

    private void UpdateFootsteps()
    {

        bool isMoving =
    agent.hasPath &&
    agent.remainingDistance > agent.stoppingDistance;

        if (!isMoving)
            return;

        float interval =
            currentState == AIState.Chase
                ? footstepIntervalRun
                : footstepIntervalWalk;

        AudioClip[] stepClips =
            currentState == AIState.Chase
                ? runFootsteps
                : walkFootsteps;


        footstepTimer += Time.deltaTime;

        if (footstepTimer >= interval)
        {
            footstepTimer = 0f;

            if (stepClips.Length > 0)
            {
                footstepSource.pitch = Random.Range(0.9f, 1.1f);
                footstepSource.PlayOneShot(stepClips[Random.Range(0, stepClips.Length)]);
            }
        }
    }

    public void FootstepEvent()
    {
        AudioClip[] stepClips = (currentState == AIState.Chase) ? runFootsteps : walkFootsteps;

        if (stepClips.Length > 0)
        {
            footstepSource.pitch = Random.Range(0.9f, 1.1f);
            footstepSource.PlayOneShot(stepClips[Random.Range(0, stepClips.Length)]);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColors.patrolRangeColor; DrawWireCircle(Vector3.zero, walkPointRange);
        Gizmos.color = gizmoColors.sightRangeColor; DrawWireCircle(Vector3.zero, sightRange);
        Gizmos.color = gizmoColors.playerAttackRangeColor; DrawWireCircle(Vector3.zero, rangedAttackRange);
        Gizmos.color = gizmoColors.meleeRangeColor; DrawWireCircle(Vector3.zero, meleeAttackRange);
        Gizmos.color = gizmoColors.objectAttackRangeColor; DrawWireCircle(Vector3.zero, objectAttackRange);
        if (useConeSight)
        {
            Gizmos.color = gizmoColors.fovColor;
            Vector3 fwd = Vector3.forward;
            Vector3 l = Quaternion.Euler(0, -fovAngle / 2, 0) * fwd; Vector3 r = Quaternion.Euler(0, fovAngle / 2, 0) * fwd;
            Gizmos.DrawLine(Vector3.zero, l * sightRange); Gizmos.DrawLine(Vector3.zero, r * sightRange);
        }
    }
    private void DrawWireCircle(Vector3 c, float r)
    {
        Vector3 p = c + new Vector3(r, 0, 0);
        for (int i = 1; i <= 36; i++)
        {
            float a = i * 10f * Mathf.Deg2Rad;
            Vector3 cur = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            Gizmos.DrawLine(p, cur); p = cur;
        }
    }

    private void Die()
    {
        if (monsterDeathParticle != null)
        {
            monsterDeathParticle.SpawnDeathEffect(transform.position);
        }

        Destroy(gameObject);
    }

}