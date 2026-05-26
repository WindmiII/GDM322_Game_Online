using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(AudioSource))]
public class PlayerMotor : MonoBehaviour
{
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private AudioSource audioSource;

    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float crouchSpeed = 2.5f;
    public float maxVelocityChange = 10f;
    private float currentSpeed;

    [Header("Jumping")]
    public float jumpForce = 5f;
    public KeyCode jumpKey = KeyCode.Space;
    public AudioClip jumpClip;

    [Header("Crouching")]
    public KeyCode crouchKey = KeyCode.LeftControl;
    public float crouchHeight = 1f;
    private float standingHeight;
    private Vector3 standingCenter;
    private Vector3 crouchingCenter;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundDistance = 0.4f;
    public LayerMask groundMask;

    [Header("Audio Settings (Distance Based)")]
    [Tooltip("ใส่เสียงเดินหลายๆ แบบ")]
    public AudioClip[] footstepClips;
    
    [Tooltip("ระยะทางที่ต้องเดินถึงจะเกิดเสียง 1 ครั้ง (หน่วยเมตร)")]
    public float stepLength = 1.2f; // ปรับค่านี้: ถ้าน้อยไปเสียงจะรัว, ถ้ามากไปเสียงจะช้า
    
    private float accumulatedDistance; // ตัวนับระยะทางสะสม

    // States
    public bool IsGrounded { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsMoving { get; private set; } // เปลี่ยนความหมายเป็น "มีการเคลื่อนที่จริง"

    [HideInInspector] public bool canSprint = true;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        audioSource = GetComponent<AudioSource>();

        rb.freezeRotation = true;
        standingHeight = capsule.height;
        standingCenter = capsule.center;
        crouchingCenter = new Vector3(standingCenter.x, (crouchHeight / 2f) - (standingHeight / 2f) + standingCenter.y, standingCenter.z);
    }

    void Update()
    {
        IsGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

        // เช็ค IsMoving จากความเร็วเครื่องจริง (แก้ปัญหาเดินชนกำแพงแล้วเสียงดัง)
        // เช็คเฉพาะแกนราบ (Horizontal Velocity) ไม่นับการตกจากที่สูง
        Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        IsMoving = flatVel.magnitude > 0.1f; 

        // Input
        float verticalInput = Input.GetAxis("Vertical");
        
        HandleCrouch();

        // Sprint logic
        IsSprinting = Input.GetKey(KeyCode.LeftShift) && IsGrounded && !IsCrouching && verticalInput > 0 && canSprint && IsMoving;

        // Jump
        if (Input.GetKeyDown(jumpKey) && IsGrounded && !IsCrouching)
        {
            Jump();
        }

        // Set Speed
        if (IsCrouching) currentSpeed = crouchSpeed;
        else if (IsSprinting) currentSpeed = sprintSpeed;
        else currentSpeed = walkSpeed;

        // Handle Footsteps
        HandleFootsteps(flatVel.magnitude);
    }

    void FixedUpdate()
    {
        if (IsGrounded)
        {
            Vector3 targetVelocity = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            targetVelocity = transform.TransformDirection(targetVelocity) * currentSpeed;

            Vector3 velocity = rb.velocity;
            Vector3 velocityChange = (targetVelocity - velocity);
            velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
            velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
            velocityChange.y = 0;

            rb.AddForce(velocityChange, ForceMode.VelocityChange);
        }
    }

    private void Jump()
    {
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        if (jumpClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(jumpClip);
        }
    }

    private void HandleCrouch()
    {
        if (Input.GetKeyDown(crouchKey)) IsCrouching = !IsCrouching;

        if (IsCrouching)
        {
            capsule.height = crouchHeight;
            capsule.center = crouchingCenter;
        }
        else
        {
            capsule.height = standingHeight;
            capsule.center = standingCenter;
        }
    }

    // 💡 NEW: ระบบเสียงเดินแบบนับระยะทาง
    private void HandleFootsteps(float currentSpeedMagnitude)
    {
        if (!IsGrounded || !IsMoving) return; 

        // บวกระยะทางสะสม (ความเร็ว * เวลา = ระยะทาง)
        accumulatedDistance += currentSpeedMagnitude * Time.deltaTime;

        // ถ้าสะสมครบระยะก้าว (stepLength) ให้เล่นเสียง
        if (accumulatedDistance >= stepLength)
        {
            PlayRandomFootstep();
            accumulatedDistance = 0f; // รีเซ็ตตัวนับ
        }
    }

    void PlayRandomFootstep()
    {
        if (footstepClips.Length == 0 || audioSource == null) return;

        // สุ่มเสียงมา 1 อัน
        int index = Random.Range(0, footstepClips.Length);
        
        // ปรับ Pitch นิดหน่อยให้เสียงไม่ซ้ำซาก (Optional)
        audioSource.pitch = Random.Range(0.9f, 1.1f); 
        audioSource.PlayOneShot(footstepClips[index]);
    }
} 