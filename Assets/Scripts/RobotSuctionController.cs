using UnityEngine;

/// <summary>Attaches one contacted target to the robot when all eight suction cups are enabled and touching it.</summary>
[DisallowMultipleComponent]
public sealed class RobotSuctionController : MonoBehaviour
{
    private const int RequiredCupCount = 8;
    private const float ContactTolerance = 0.003f;

    [SerializeField, Tooltip("Turn on to pick up one configured target when every suction cup touches that same target.")]
    private bool suctionEnabled;
    [SerializeField, Tooltip("Assign Suction 1–8 transforms in order. Both same-named Suction 1 objects are separate cups.")]
    private Transform[] suctionCups = new Transform[RequiredCupCount];
    [SerializeField, Tooltip("Any one of these target objects can be picked up, one at a time.")]
    private Transform[] targetObjects;
    [SerializeField, Tooltip("Runtime suction state.")]
    private string suctionStatus = "Vacuum off";
    [SerializeField, Tooltip("Target currently held by suction, if any.")]
    private Transform attachedTarget;

    private Transform originalTargetParent;
    private Rigidbody attachedRigidbody;
    private bool originalIsKinematic;
    private bool originalUseGravity;
    private Vector3 attachedTargetStartPosition;
    private Quaternion attachedTargetStartRotation = Quaternion.identity;
    private Transform[] cachedTargets;
    private Collider[][] cachedTargetColliders;
    private Renderer[][] cachedTargetRenderers;

    /// <summary>Gets whether the Inspector or runtime UI has enabled suction.</summary>
    public bool IsSuctionEnabled => suctionEnabled;

    /// <summary>Gets the latest suction pickup status for runtime UI.</summary>
    public string SuctionStatus => suctionStatus;

    /// <summary>Gets the target currently held by the suction cups.</summary>
    public Transform AttachedTarget => attachedTarget;

    /// <summary>Gets the world position of the target when it was first attached.</summary>
    public Vector3 AttachedTargetStartPosition => attachedTargetStartPosition;

    /// <summary>Gets the world rotation of the target when it was first attached.</summary>
    public Quaternion AttachedTargetStartRotation => attachedTargetStartRotation;


    private void Awake()
    {
        CacheTargetGeometry();
    }

    private void Update()
    {
        if (!suctionEnabled)
        {
            ReleaseTarget();
            suctionStatus = "Vacuum off";
            return;
        }

        if (attachedTarget != null)
            return;

        if (!HasCompleteCupSetup())
        {
            suctionStatus = "Assign all eight suction cups in the Inspector";
            return;
        }

        if (!TryAttachContactedTarget())
            suctionStatus = "On · waiting for all eight cups to touch one target";
    }

    private void OnDisable()
    {
        ReleaseTarget();
    }

    /// <summary>Enables or disables suction pickup.</summary>
    public void SetSuctionEnabled(bool isEnabled)
    {
        suctionEnabled = isEnabled;
        if (!isEnabled)
            ReleaseTarget();
    }

    /// <summary>Toggles suction from a runtime control button.</summary>
    public void ToggleSuction()
    {
        SetSuctionEnabled(!suctionEnabled);
    }

    private bool TryAttachContactedTarget()
    {
        if (!IsTargetCacheCurrent())
            CacheTargetGeometry();
        if (cachedTargets == null)
            return false;

        for (int targetIndex = 0; targetIndex < cachedTargets.Length; targetIndex++)
        {
            Transform candidateTarget = cachedTargets[targetIndex];
            if (!candidateTarget || !AreAllCupsTouching(targetIndex))
                continue;

            Transform attachmentFrame = FindCommonAncestor(suctionCups);
            if (!attachmentFrame)
            {
                suctionStatus = "Could not find a shared robot frame for the cups";
                return false;
            }

            attachedTarget = candidateTarget;
            attachedTargetStartPosition = attachedTarget.position;
            attachedTargetStartRotation = attachedTarget.rotation;
            originalTargetParent = attachedTarget.parent;
            attachedRigidbody = attachedTarget.GetComponent<Rigidbody>();
            if (attachedRigidbody)
            {
                originalIsKinematic = attachedRigidbody.isKinematic;
                originalUseGravity = attachedRigidbody.useGravity;
                attachedRigidbody.isKinematic = true;
                attachedRigidbody.useGravity = false;
            }

            attachedTarget.SetParent(attachmentFrame, true);
            suctionStatus = "Holding " + attachedTarget.name;
            return true;
        }

        return false;
    }

    private bool IsTargetCacheCurrent()
    {
        if (targetObjects == null)
            return cachedTargets != null && cachedTargets.Length == 0;
        if (cachedTargets == null || cachedTargets.Length != targetObjects.Length)
            return false;

        for (int targetIndex = 0; targetIndex < targetObjects.Length; targetIndex++)
        {
            if (cachedTargets[targetIndex] != targetObjects[targetIndex])
                return false;
        }

        return true;
    }

    private void CacheTargetGeometry()
    {
        int targetCount = targetObjects == null ? 0 : targetObjects.Length;
        cachedTargets = new Transform[targetCount];
        cachedTargetColliders = new Collider[targetCount][];
        cachedTargetRenderers = new Renderer[targetCount][];

        for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
        {
            Transform target = targetObjects[targetIndex];
            cachedTargets[targetIndex] = target;
            cachedTargetColliders[targetIndex] = target
                ? target.GetComponentsInChildren<Collider>(true)
                : new Collider[0];
            cachedTargetRenderers[targetIndex] = target
                ? target.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
        }
    }


    private bool AreAllCupsTouching(int targetIndex)
    {
        Collider[] targetColliders = cachedTargetColliders[targetIndex];
        Renderer[] targetRenderers = cachedTargetRenderers[targetIndex];
        if (targetColliders.Length == 0 && targetRenderers.Length == 0)
            return false;

        for (int cupIndex = 0; cupIndex < RequiredCupCount; cupIndex++)
        {
            Transform cup = suctionCups[cupIndex];
            if (!cup || !cup.gameObject.activeInHierarchy)
                return false;

            Renderer cupRenderer = cup.GetComponent<Renderer>();
            if (!cupRenderer || !cupRenderer.enabled)
                return false;

            Bounds cupBounds = cupRenderer.bounds;
            cupBounds.Expand(ContactTolerance * 2f);
            if (!CupBoundsTouchTarget(cupBounds, targetColliders, targetRenderers))
                return false;
        }

        return true;
    }

    private static bool CupBoundsTouchTarget(Bounds cupBounds, Collider[] targetColliders,
        Renderer[] targetRenderers)
    {
        for (int colliderIndex = 0; colliderIndex < targetColliders.Length; colliderIndex++)
        {
            Collider targetCollider = targetColliders[colliderIndex];
            if (!targetCollider || !targetCollider.enabled || !targetCollider.gameObject.activeInHierarchy)
                continue;

            Vector3 targetPoint = targetCollider.ClosestPoint(cupBounds.center);
            if (cupBounds.Contains(targetPoint))
                return true;
        }

        for (int rendererIndex = 0; rendererIndex < targetRenderers.Length; rendererIndex++)
        {
            Renderer targetRenderer = targetRenderers[rendererIndex];
            if (!targetRenderer || !targetRenderer.enabled)
                continue;

            Bounds targetBounds = targetRenderer.bounds;
            if (cupBounds.Intersects(targetBounds))
                return true;
        }

        return false;
    }



    private bool HasCompleteCupSetup()
    {
        if (suctionCups == null || suctionCups.Length < RequiredCupCount)
            return false;

        for (int cupIndex = 0; cupIndex < RequiredCupCount; cupIndex++)
        {
            if (!suctionCups[cupIndex])
                return false;
        }

        return true;
    }

    private static Transform FindCommonAncestor(Transform[] cups)
    {
        Transform candidate = cups[0].parent;
        while (candidate)
        {
            bool containsEveryCup = true;
            for (int cupIndex = 1; cupIndex < RequiredCupCount; cupIndex++)
            {
                Transform cup = cups[cupIndex];
                if (cup != candidate && !cup.IsChildOf(candidate))
                {
                    containsEveryCup = false;
                    break;
                }
            }

            if (containsEveryCup)
                return candidate;
            candidate = candidate.parent;
        }

        return null;
    }

    private void ReleaseTarget()
    {
        if (attachedTarget)
            attachedTarget.SetParent(originalTargetParent, true);

        if (attachedRigidbody)
        {
            attachedRigidbody.useGravity = originalUseGravity;
            attachedRigidbody.isKinematic = originalIsKinematic;
        }

        attachedTarget = null;
        originalTargetParent = null;
        attachedRigidbody = null;
    }
}
