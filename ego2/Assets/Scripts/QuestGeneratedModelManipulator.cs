using UnityEngine;

public class QuestGeneratedModelManipulator : MonoBehaviour
{
    [Header("Grab")]
    [Range(0.1f, 1f)] public float grabThreshold = 0.65f;
    [Range(0.05f, 1f)] public float releaseThreshold = 0.4f;
    public float maxGrabRayDistance = 6f;
    public float nearGrabDistance = 0.14f;
    public float grabAimTolerance = 0.12f;

    [Header("Scale")]
    public float minimumScaleMultiplier = 0.2f;
    public float maximumScaleMultiplier = 4f;

    private enum ManipulationState
    {
        Idle,
        LeftHand,
        RightHand,
        TwoHands
    }

    private Transform leftController;
    private Transform rightController;
    private BoxCollider interactionCollider;
    private ManipulationState state;
    private bool interactable;
    private bool previousLeftHeld;
    private bool previousRightHeld;

    private Vector3 oneHandPositionOffset;
    private Quaternion oneHandRotationOffset;

    private Vector3 initialTwoHandVector;
    private Vector3 initialTwoHandMidpoint;
    private Vector3 initialTwoHandModelPosition;
    private Quaternion initialTwoHandModelRotation;
    private Vector3 initialTwoHandModelScale;
    private float initialTwoHandDistance;

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 homeScale;
    private Vector3 minimumScale;
    private Vector3 maximumScale;

    public bool IsManipulating => state != ManipulationState.Idle;

    public void Configure(Transform leftControllerAnchor, Transform rightControllerAnchor)
    {
        leftController = leftControllerAnchor;
        rightController = rightControllerAnchor;
    }

    public void SetModelBounds(Bounds worldBounds)
    {
        if (interactionCollider == null)
        {
            interactionCollider = GetComponent<BoxCollider>();
            if (interactionCollider == null)
            {
                interactionCollider = gameObject.AddComponent<BoxCollider>();
            }
        }

        Vector3 lossyScale = transform.lossyScale;
        interactionCollider.center = transform.InverseTransformPoint(worldBounds.center);
        interactionCollider.size = new Vector3(
            DivideByScale(worldBounds.size.x, lossyScale.x),
            DivideByScale(worldBounds.size.y, lossyScale.y),
            DivideByScale(worldBounds.size.z, lossyScale.z));
        interactionCollider.enabled = true;

        homePosition = transform.position;
        homeRotation = transform.rotation;
        homeScale = transform.localScale;
        minimumScale = homeScale * Mathf.Max(0.01f, minimumScaleMultiplier);
        maximumScale = homeScale * Mathf.Max(minimumScaleMultiplier, maximumScaleMultiplier);
        interactable = true;
        state = ManipulationState.Idle;
    }

    public void SetInteractable(bool value)
    {
        interactable = value;
        state = ManipulationState.Idle;

        if (interactionCollider != null)
        {
            interactionCollider.enabled = value;
        }
    }

    public void ResetToHome()
    {
        if (!interactable)
        {
            return;
        }

        state = ManipulationState.Idle;
        transform.SetPositionAndRotation(homePosition, homeRotation);
        transform.localScale = homeScale;
    }

    private void Update()
    {
        if (!interactable)
        {
            previousLeftHeld = false;
            previousRightHeld = false;
            return;
        }

        ResolveControllerAnchors();
        if (leftController == null || rightController == null)
        {
            return;
        }

        float leftGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        float rightGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        bool leftHeld = leftGrip >= (previousLeftHeld ? releaseThreshold : grabThreshold);
        bool rightHeld = rightGrip >= (previousRightHeld ? releaseThreshold : grabThreshold);
        bool leftPressed = leftHeld && !previousLeftHeld;
        bool rightPressed = rightHeld && !previousRightHeld;

        switch (state)
        {
            case ManipulationState.Idle:
                TryBeginManipulation(leftHeld, rightHeld, leftPressed, rightPressed);
                break;

            case ManipulationState.LeftHand:
                if (!leftHeld)
                {
                    if (rightHeld)
                    {
                        BeginOneHand(rightController, ManipulationState.RightHand);
                    }
                    else
                    {
                        state = ManipulationState.Idle;
                    }
                }
                else if (rightPressed)
                {
                    BeginTwoHands();
                }
                else
                {
                    UpdateOneHand(leftController);
                }
                break;

            case ManipulationState.RightHand:
                if (!rightHeld)
                {
                    if (leftHeld)
                    {
                        BeginOneHand(leftController, ManipulationState.LeftHand);
                    }
                    else
                    {
                        state = ManipulationState.Idle;
                    }
                }
                else if (leftPressed)
                {
                    BeginTwoHands();
                }
                else
                {
                    UpdateOneHand(rightController);
                }
                break;

            case ManipulationState.TwoHands:
                if (leftHeld && rightHeld)
                {
                    UpdateTwoHands();
                }
                else if (leftHeld)
                {
                    BeginOneHand(leftController, ManipulationState.LeftHand);
                }
                else if (rightHeld)
                {
                    BeginOneHand(rightController, ManipulationState.RightHand);
                }
                else
                {
                    state = ManipulationState.Idle;
                }
                break;
        }

        previousLeftHeld = leftHeld;
        previousRightHeld = rightHeld;
    }

    private void TryBeginManipulation(bool leftHeld, bool rightHeld, bool leftPressed, bool rightPressed)
    {
        bool leftCanGrab = leftPressed && CanGrab(leftController);
        bool rightCanGrab = rightPressed && CanGrab(rightController);

        if (leftHeld && rightHeld && (leftCanGrab || rightCanGrab))
        {
            BeginTwoHands();
        }
        else if (leftCanGrab)
        {
            BeginOneHand(leftController, ManipulationState.LeftHand);
        }
        else if (rightCanGrab)
        {
            BeginOneHand(rightController, ManipulationState.RightHand);
        }
    }

    private bool CanGrab(Transform controller)
    {
        if (controller == null || interactionCollider == null || !interactionCollider.enabled)
        {
            return false;
        }

        Vector3 closestPoint = interactionCollider.ClosestPoint(controller.position);
        if (Vector3.Distance(closestPoint, controller.position) <= nearGrabDistance)
        {
            return true;
        }

        Ray ray = new Ray(controller.position, controller.forward);
        if (interactionCollider.Raycast(ray, out _, maxGrabRayDistance))
        {
            return true;
        }

        Bounds forgivingBounds = interactionCollider.bounds;
        forgivingBounds.Expand(Mathf.Max(0f, grabAimTolerance) * 2f);
        return forgivingBounds.IntersectRay(ray, out float distance) && distance <= maxGrabRayDistance;
    }

    private void BeginOneHand(Transform controller, ManipulationState nextState)
    {
        state = nextState;
        oneHandPositionOffset = Quaternion.Inverse(controller.rotation) * (transform.position - controller.position);
        oneHandRotationOffset = Quaternion.Inverse(controller.rotation) * transform.rotation;
    }

    private void UpdateOneHand(Transform controller)
    {
        transform.position = controller.position + controller.rotation * oneHandPositionOffset;
        transform.rotation = controller.rotation * oneHandRotationOffset;
    }

    private void BeginTwoHands()
    {
        initialTwoHandVector = rightController.position - leftController.position;
        initialTwoHandDistance = Mathf.Max(0.001f, initialTwoHandVector.magnitude);
        initialTwoHandMidpoint = (leftController.position + rightController.position) * 0.5f;
        initialTwoHandModelPosition = transform.position;
        initialTwoHandModelRotation = transform.rotation;
        initialTwoHandModelScale = transform.localScale;
        state = ManipulationState.TwoHands;
    }

    private void UpdateTwoHands()
    {
        Vector3 currentVector = rightController.position - leftController.position;
        float currentDistance = Mathf.Max(0.001f, currentVector.magnitude);
        Vector3 currentMidpoint = (leftController.position + rightController.position) * 0.5f;
        Quaternion rotationDelta = Quaternion.FromToRotation(initialTwoHandVector, currentVector);
        float scaleFactor = currentDistance / initialTwoHandDistance;

        Vector3 desiredScale = initialTwoHandModelScale * scaleFactor;
        desiredScale = ClampScale(desiredScale);
        float appliedScaleFactor = desiredScale.x / Mathf.Max(0.0001f, initialTwoHandModelScale.x);

        transform.rotation = rotationDelta * initialTwoHandModelRotation;
        transform.localScale = desiredScale;
        transform.position = currentMidpoint +
                             rotationDelta * (initialTwoHandModelPosition - initialTwoHandMidpoint) * appliedScaleFactor;
    }

    private Vector3 ClampScale(Vector3 scale)
    {
        float factor = scale.x / Mathf.Max(0.0001f, homeScale.x);
        float minFactor = minimumScale.x / Mathf.Max(0.0001f, homeScale.x);
        float maxFactor = maximumScale.x / Mathf.Max(0.0001f, homeScale.x);
        return homeScale * Mathf.Clamp(factor, minFactor, maxFactor);
    }

    private void ResolveControllerAnchors()
    {
        if (leftController != null && rightController != null)
        {
            return;
        }

        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig == null)
        {
            return;
        }

        leftController = cameraRig.leftControllerAnchor != null
            ? cameraRig.leftControllerAnchor
            : cameraRig.leftHandAnchor;
        rightController = cameraRig.rightControllerAnchor != null
            ? cameraRig.rightControllerAnchor
            : cameraRig.rightHandAnchor;
    }

    private static float DivideByScale(float value, float scale)
    {
        return value / Mathf.Max(0.0001f, Mathf.Abs(scale));
    }
}
