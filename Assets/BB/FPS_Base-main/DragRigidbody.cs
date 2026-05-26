using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DragRigidbody : MonoBehaviour
{
    // 💡 NEW: The permanent, unique ID for this TYPE of item.
    [Header("Objective System Link")]
    [Tooltip("A unique ID for this item type (e.g., 'Gem_Heart', 'Crystal_8'). This must be the same on the prefab and scene instances.")]
    public string itemID = "Default_ID";
    
    [Tooltip("The specific enemy that will drop a replacement for THIS item if it's an objective.")]
    public EnemyAi backupEnemy;

    // ... The rest of the script is the same ...
    [Header("Joint Settings")]
    public float force = 600;
    public float damping = 6;
    public float distance = 15;

    [Header("Rope (optional)")]
    public LineRenderer lr;
    public Transform lineRenderLocation;

    [Header("Impact → Value (held & post-release)")]
    [Min(0f)] public float startValue = 100f;
    [Tooltip("damage ≈ mass * relativeSpeed * multiplier")]
    public float impactDamageMultiplier = 0.02f;
    [Range(0f, 1f)] public float valueLossPerDamage = 0.25f;
    public float minDamageVelocity = 1.5f;
    [Tooltip("จำกัดการลดมูลค่าต่อการชนหนึ่งครั้ง (0 = ไม่จำกัด)")]
    public float maxValueLossPerHit = 0f;
    
    [Tooltip("Is this item essential for the objective? (Set by Manager)")]
    public bool isEssentialItem = false;

    [Header("Throw arming")]
    public float postReleaseDamageWindow = 1.0f;
    public float minThrowSpeedToArm = 2.0f;

    [Header("Break on zero value")]
    public GameObject brokenPrefab;
    public bool inheritVelocityToPieces = true;
    public float pieceVelocityMultiplier = 0.9f;

    Transform jointTrans;
    float dragDepth;
    
    Rigidbody grabbedRb;
    ImpactValueTracker grabbedTracker;
    Vector3 grabbedLocalAttachPoint;

    void Awake()
    {
        if (lr) lr.useWorldSpace = true;
    }

    void OnMouseDown() => HandleInputBegin(Input.mousePosition);
    void OnMouseUp() => HandleInputEnd(Input.mousePosition);
    void OnMouseDrag() => HandleInput(Input.mousePosition);

    public void HandleInputBegin(Vector3 screenPosition)
    {
        var ray = Camera.main.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out var hit, distance))
        {
            if (hit.transform.gameObject.layer == LayerMask.NameToLayer("Interactive") || hit.transform.gameObject.layer == LayerMask.NameToLayer("Highlight") && hit.rigidbody)
            {
                dragDepth = CameraPlane.CameraToPointDepth(Camera.main, hit.point);
                jointTrans = AttachJoint(hit.rigidbody, hit.point);

                grabbedRb = hit.rigidbody;
                grabbedLocalAttachPoint = grabbedRb.transform.InverseTransformPoint(hit.point);
                
                grabbedTracker = grabbedRb.GetComponent<ImpactValueTracker>();
                if (!grabbedTracker)
                    grabbedTracker = grabbedRb.gameObject.AddComponent<ImpactValueTracker>();
                
                grabbedTracker.Configure(
                    initialValue: startValue,
                    damageMultiplier: impactDamageMultiplier,
                    valueLossPerDamage: valueLossPerDamage,
                    minVelocity: minDamageVelocity,
                    maxLossPerHit: maxValueLossPerHit,
                    postReleaseWindow: postReleaseDamageWindow,
                    minThrowSpeedToArm: minThrowSpeedToArm,
                    brokenPrefab: brokenPrefab,
                    inheritVelocityToPieces: inheritVelocityToPieces,
                    pieceVelocityMultiplier: pieceVelocityMultiplier,
                    isEssential: isEssentialItem
                );

                grabbedTracker.onReplaced = OnGrabbedObjectReplaced;
                grabbedTracker.SetHeld(true);
            }
        }

        if (lr) lr.positionCount = 2;
    }

    public void HandleInput(Vector3 screenPosition)
    {
        if (grabbedRb == null)
        {
            HandleInputEnd(screenPosition);
            return;
        }

        if (!jointTrans) return;
        
        jointTrans.position = CameraPlane.ScreenToWorldPlanePoint(Camera.main, dragDepth, screenPosition);
        DrawRope();
    }

    public void HandleInputEnd(Vector3 screenPosition)
    {
        if (grabbedTracker && grabbedRb)
        {
            float releaseSpeed = grabbedRb.velocity.magnitude;
            grabbedTracker.OnReleased(releaseSpeed);
            grabbedTracker.SetHeld(false);
        }

        grabbedTracker = null;
        grabbedRb = null;

        DestroyRope();
        if (jointTrans) Destroy(jointTrans.gameObject);
        jointTrans = null;
    }
    
    void OnGrabbedObjectReplaced()
    {
        DestroyRope();
        if (jointTrans) { Destroy(jointTrans.gameObject); jointTrans = null; }
        grabbedTracker = null;
        grabbedRb = null;
    }

    Transform AttachJoint(Rigidbody rb, Vector3 attachmentPosition)
    {
        var go = new GameObject("Attachment Point");
        go.hideFlags = HideFlags.HideInHierarchy;
        go.transform.position = attachmentPosition;

        var anchor = go.AddComponent<Rigidbody>();
        anchor.isKinematic = true;

        var joint = go.AddComponent<ConfigurableJoint>();
        joint.connectedBody = rb;
        joint.configuredInWorldSpace = true;

        var drive = NewJointDrive(force, damping);
        joint.xDrive = drive; joint.yDrive = drive; joint.zDrive = drive;
        joint.slerpDrive = drive;
        joint.rotationDriveMode = RotationDriveMode.Slerp;

        return go.transform;
    }

    private JointDrive NewJointDrive(float force, float damping)
    {
        return new JointDrive
        {
            mode = JointDriveMode.Position,
            positionSpring = force,
            positionDamper = damping,
            maximumForce = Mathf.Infinity
        };
    }

    private void DrawRope()
    {
        if (lr == null) return;
        Vector3 start = lineRenderLocation ? lineRenderLocation.position : transform.position;
        Vector3 end = (grabbedRb != null) ? grabbedRb.transform.TransformPoint(grabbedLocalAttachPoint) 
            : (jointTrans != null ? jointTrans.position : start);
        if (lr.positionCount != 2) lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
    }

    private void DestroyRope()
    {
        if (lr) lr.positionCount = 0;
    }
    
    public class ImpactValueTracker : MonoBehaviour
    {
        public static event Action<Transform, float> OnValueLost;
        [HideInInspector] public float Value;
        [HideInInspector] public bool isEssentialItem;

        float damageMult, valueLossRatio, minVelocity, maxLossPerHit;
        float postReleaseWindow, minThrowSpeedToArm, armedUntil;
        bool isHeld;
        Rigidbody rb;
        GameObject brokenPrefab;
        bool inheritVel;
        float pieceVelMul;
        public Action onReplaced;
        int protectionCount = 0;
        public bool IsProtected => protectionCount > 0;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (!rb) enabled = false;
        }

        public void Configure(
            float initialValue, float damageMultiplier, float valueLossPerDamage,
            float minVelocity, float maxLossPerHit, float postReleaseWindow,
            float minThrowSpeedToArm, GameObject brokenPrefab, bool inheritVelocityToPieces,
            float pieceVelocityMultiplier, bool isEssential)
        {
            if (Value <= 0f) Value = Mathf.Max(0f, initialValue);
            damageMult = damageMultiplier;
            valueLossRatio = Mathf.Clamp01(valueLossPerDamage);
            this.minVelocity = Mathf.Max(0f, minVelocity);
            this.maxLossPerHit = Mathf.Max(0f, maxLossPerHit);
            this.postReleaseWindow = Mathf.Max(0f, postReleaseWindow);
            this.minThrowSpeedToArm = Mathf.Max(0f, minThrowSpeedToArm);
            this.brokenPrefab = brokenPrefab;
            this.inheritVel = inheritVelocityToPieces;
            this.pieceVelMul = pieceVelocityMultiplier;
            this.isEssentialItem = isEssential;
        }

        public void AddProtection()    { protectionCount++; }
        public void RemoveProtection() { protectionCount = Mathf.Max(0, protectionCount - 1); }
        public void SetHeld(bool held) => isHeld = held;

        public void OnReleased(float releaseSpeed)
        {
            if (releaseSpeed >= minThrowSpeedToArm)
                armedUntil = Time.time + postReleaseWindow;
        }

        void Update()
        {
            if (armedUntil > 0f && rb != null && rb.IsSleeping())
                armedUntil = 0f;
        }

        void OnCollisionEnter(Collision c)
        {
            if (rb == null || c == null || IsProtected) return;

            bool armed = isHeld || (Time.time < armedUntil);
            if (!armed || c.relativeVelocity.magnitude < minVelocity) return;

            float approxImpulse = rb.mass * c.relativeVelocity.magnitude;
            float damage = approxImpulse * damageMult;
            float valueLoss = damage * valueLossRatio;

            if (maxLossPerHit > 0f) valueLoss = Mathf.Min(valueLoss, maxLossPerHit);
            if (valueLoss <= 0f) return;

            Value = Mathf.Max(0f, Value - valueLoss);
            OnValueLost?.Invoke(transform, valueLoss);

            if (Value <= 0f) ReplaceWithBroken();
        }

        void ReplaceWithBroken()
        {
            if (!brokenPrefab)
            {
                onReplaced?.Invoke();
                Destroy(gameObject);
                return;
            }

            Transform t = transform;
            onReplaced?.Invoke();

            var broken = Instantiate(brokenPrefab, t.position, t.rotation, t.parent);
            broken.transform.localScale = t.localScale;

            if (inheritVel && rb)
            {
                var rbs = broken.GetComponentsInChildren<Rigidbody>(true);
                foreach (var child in rbs)
                {
                    if (child)
                    {
                        child.velocity = rb.velocity * pieceVelMul;
                        child.angularVelocity = rb.angularVelocity;
                    }
                }
            }

            if (broken.GetComponent<Break>() is Break breakGrp)
                breakGrp.BreakAll();

            Destroy(t.gameObject);
        }
        
        public void ApplyExternalDamage(float damage, bool treatAsOneHit = true, float maxLossOverride = -1f)
        {
            if (damage <= 0f || IsProtected) return;

            float valueLoss = damage * valueLossRatio;
            float cap = (maxLossOverride > 0f) ? maxLossOverride : maxLossPerHit;
            if (cap > 0f) valueLoss = Mathf.Min(valueLoss, cap);
            if (valueLoss <= 0f) return;

            Value = Mathf.Max(0f, Value - valueLoss);
            OnValueLost?.Invoke(transform, valueLoss);

            if (Value <= 0f) ReplaceWithBroken();
            if (treatAsOneHit) armedUntil = 0f;
        }
    }
}