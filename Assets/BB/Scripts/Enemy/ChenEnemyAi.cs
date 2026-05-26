using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static EnemyAi;

public class ChenEnemyAi : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Animator animator;
    private NavMeshAgent agent;

    [Header("Patrol Settings")]
    public Transform[] patrolPoints;
    public float patrolSpeed = 1.5f;  // speed เดิน
    public float patrolAnimSpeed = 0.5f; // animation Walk = 0.5

    [Header("Detection Settings")]
    [Range(0, 180)]
    public float FOV = 90f;
    public float viewDistance = 10f;

    [Header("Spotted/Detect Settings")]
    public float spottedDelay = 1.5f;
    private bool isSpotted = false;

    [Header("Chase Settings")]
    public float chaseSpeed = 4f;     // speed วิ่ง
    public float chaseAnimSpeed = 2f; // animation Run = 2
    public float stopToAttackDistance = 2f;

    [Header("Attack Settings")]
    public float attackRange = 1.8f;
    public float attackCooldown = 1.5f;
    private float attackTimer;

    [Header("Attack Sounds")]
    public AudioSource audioSource;
    public AudioClip meleeAttackSFX;
    public AudioClip rangedAttackSFX;
    public AudioClip hitPlayerSFX;   // เสียงโดนผู้เล่น
    public AudioClip missSFX;        // เสียงฟาดพลาด (เผื่อทำ)

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

    private int patrolIndex = 0;
    private bool isChasing = false;
    private AIState currentState;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        GoToNextPatrol();
    }

    void Update()
    {

        attackTimer -= Time.deltaTime;

        if (!isChasing)
        {
            Patrol();
            DetectPlayer();
        }
        else
        {
            ChasePlayer();
        }
    }

    // ---------------------------------------------
    // PATROL
    // ---------------------------------------------
    void Patrol()
    {
        agent.speed = patrolSpeed;
        animator.SetFloat("Speed", patrolAnimSpeed);

        if (!agent.pathPending && agent.remainingDistance < 0.4f)
            GoToNextPatrol();
    }

    void GoToNextPatrol()
    {
        if (patrolPoints.Length == 0) return;

        agent.SetDestination(patrolPoints[patrolIndex].position);
        patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
    }

    // ---------------------------------------------
    // DETECT PLAYER (FOV + Distance)
    // ---------------------------------------------
    void DetectPlayer()
    {
        Vector3 dir = (player.position - transform.position).normalized;

        float angle = Vector3.Angle(transform.forward, dir);
        float dist = Vector3.Distance(transform.position, player.position);

        if (angle < FOV && dist < viewDistance)
        {
            StartCoroutine(PlayerSpottedRoutine());
        }
    }

    // ---------------------------------------------
    // PLAYER SPOTTED DELAY
    // ---------------------------------------------
    System.Collections.IEnumerator PlayerSpottedRoutine()
    {
        if (isSpotted) yield break;

        isSpotted = true;
        agent.isStopped = true;

        animator.SetTrigger("PlayerDetect");

        yield return new WaitForSeconds(spottedDelay);

        isChasing = true;
        agent.isStopped = false;
    }

    // ---------------------------------------------
    // CHASE PLAYER
    // ---------------------------------------------
    void ChasePlayer()
    {
        agent.speed = chaseSpeed;
        animator.SetFloat("Speed", chaseAnimSpeed);

        agent.SetDestination(player.position);

        float dist = Vector3.Distance(transform.position, player.position);

        if (dist <= stopToAttackDistance)
        {
            agent.isStopped = true;
            TryAttack();
        }
        else
        {
            agent.isStopped = false;
        }
    }

    // ---------------------------------------------
    // ATTACK
    // ---------------------------------------------
    void TryAttack()
    {
        float dist = Vector3.Distance(transform.position, player.position);

        if (dist <= attackRange && attackTimer <= 0f)
        {
            animator.SetTrigger("Attack");
            attackTimer = attackCooldown;
        }
    }
    
}