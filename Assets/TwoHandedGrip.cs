using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

[RequireComponent(typeof(XRGrabInteractable))]
public class TwoHandedGrip : MonoBehaviour
{
    [Header("Second Grip")]
    [Tooltip("An empty child of where the second hand goes for the club.")]
    public Transform secondGripPoint;
    [Tooltip("How close (meters) the second hand must be to the Second Grip Point to count as a proper grip.")]
    public float maxSecondHandDistance = 0.2f;

    [Header("Two-Handed Control")]
    [Tooltip("How much the second hand (Left) controls the club. 0.5 = both hands equal (most ideal). " +
             "Lower = the first hand leads more.")]
    [Range(0f, 1f)] public float secondHandInfluence = 0.5f;

    [Header("Ball")]
    [Tooltip("Leave empty to find the object named Golf_Ball automatically.")]
    public GameObject ball;
    public string ballName = "Golf_Ball";

    [Header("Hint")]
    [Tooltip("Show a message until second point is gripped.")]
    public bool showHint = true;
    public string hintText = "Use both hands to putt!";
    public Color hintColor = Color.white;
    public float hintFontSize = 0.25f;
    [Tooltip("Where the hint sits relative to the Second Grip Point (meters, world directions).")]
    public Vector3 hintOffset = new Vector3(0f, 0.15f, 0f);

    [Header("Lock Hand Models")]
    [Tooltip("While both hands hold the club, keep each hand model locked onto its grip point.")]
    public bool lockHandModels = true;
    [Tooltip("The first (right) hand's visible model. Leave empty to find it by the name below.")]
    public Transform firstHandModel;
    public string firstHandModelName = "Right Hand Model";
    [Tooltip("The second (left) hand's visible model. Leave empty to find it by the name below.")]
    public Transform secondHandModel;
    public string secondHandModelName = "Left Hand Model";

    [Header("Debug")]
    [Tooltip("Print each step of the grab to the Console: touching, grabbing, letting go, and why a grip doesn't count.")]
    public bool debugLogs = true;

    public bool IsTwoHanded { get; private set; }

    XRGrabInteractable grab;
    Rigidbody rb;
    Collider[] clubColliders;
    Collider[] ballColliders;
    bool collisionsAllowed;
    TextMeshPro hint;

    // Two-handed control
    bool twoHandControl;
    Transform hand0, hand1;
    Vector3 clubOffsetFromHand0, clubOffsetFromHand1;
    Quaternion clubRotFromHand0, clubRotFromHand1;
    Vector3 gripMidOffset; // midpoint between the grip points, in the club's frame (meters)

    // Debug
    bool wasTwoHanded;
    float lastSecondDistance = -1f;
    float lastDistanceLog;

    // Locks a hand model onto a grip point
    class HandModelLock
    {
        public Transform model;
        Vector3 restLocalPos;
        Quaternion restLocalRot;
        Vector3 offsetPos;
        Quaternion offsetRot;
        bool locked;

        public HandModelLock(Transform model)
        {
            this.model = model;
            if (model != null)
            {
                restLocalPos = model.localPosition;
                restLocalRot = model.localRotation;
            }
        }

        public void Lock(Transform handAttach, Transform grip)
        {
            if (model == null || handAttach == null || grip == null) return;

            if (!locked)
            {
                Quaternion inverse = Quaternion.Inverse(handAttach.rotation);
                offsetRot = inverse * model.rotation;
                offsetPos = inverse * (model.position - handAttach.position);
                locked = true;
            }

            model.SetPositionAndRotation(grip.position + grip.rotation * offsetPos, grip.rotation * offsetRot);
        }

        public void Release()
        {
            if (!locked || model == null) return;
            model.localPosition = restLocalPos;
            model.localRotation = restLocalRot;
            locked = false;
        }
    }

    HandModelLock firstLock, secondLock;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        grab.selectMode = InteractableSelectMode.Multiple;

        if (secondGripPoint != null)
            grab.secondaryAttachTransform = secondGripPoint;
        else
            Debug.LogWarning("TwoHandedGrip: no Second Grip Point assigned.", this);

        var transformer = GetComponent<XRGeneralGrabTransformer>();
        if (transformer == null)
            transformer = gameObject.AddComponent<XRGeneralGrabTransformer>();
        transformer.allowTwoHandedRotation = XRGeneralGrabTransformer.TwoHandedRotationMode.FirstHandOnly;

        // The club's solid colliders (the head and shaft)
        var solid = new List<Collider>();
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (!c.isTrigger) solid.Add(c);
        clubColliders = solid.ToArray();

        if (ball == null)
            ball = GameObject.Find(ballName);
        if (ball != null)
            ballColliders = ball.GetComponentsInChildren<Collider>(true);
        else
        {
            ballColliders = new Collider[0];
            Debug.LogWarning($"TwoHandedGrip: no object named '{ballName}' found. Assign the ball in the Inspector.", this);
        }

        if (lockHandModels)
        {
            if (firstHandModel == null) firstHandModel = FindByName(firstHandModelName);
            if (secondHandModel == null) secondHandModel = FindByName(secondHandModelName);
        }
        firstLock = new HandModelLock(firstHandModel);
        secondLock = new HandModelLock(secondHandModel);
    }

    Transform FindByName(string objectName)
    {
        var found = GameObject.Find(objectName);
        if (found == null)
            Debug.LogWarning($"TwoHandedGrip: no hand model named '{objectName}' found, so it won't be locked to the club. " +
                             "Assign it in the Inspector.", this);
        return found != null ? found.transform : null;
    }

    void OnEnable()
    {
        Application.onBeforeRender += UpdateHandLocks;

        grab.hoverEntered.AddListener(OnHoverEntered);
        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        Application.onBeforeRender -= UpdateHandLocks;
        EndTwoHandControl();

        grab.hoverEntered.RemoveListener(OnHoverEntered);
        grab.selectEntered.RemoveListener(OnSelectEntered);
        grab.selectExited.RemoveListener(OnSelectExited);
    }

    void FixedUpdate()
    {
        UpdateTwoHandControl();

        IsTwoHanded = CheckTwoHanded();
        ApplyBallCollisions(IsTwoHanded);

        if (debugLogs)
        {
            if (IsTwoHanded != wasTwoHanded)
                Debug.Log(IsTwoHanded
                    ? "TwoHandedGrip: both hands are on. The club can hit the ball."
                    : "TwoHandedGrip: no longer two-handed. The club passes through the ball.", this);

            if (!IsTwoHanded && lastSecondDistance >= 0f && Time.time - lastDistanceLog > 1f)
            {
                lastDistanceLog = Time.time;
                Debug.Log($"TwoHandedGrip: the second hand is holding the club but is {lastSecondDistance:F2} m " +
                          $"from the Second Grip Point (max {maxSecondHandDistance:F2} m), so it doesn't count. " +
                          "Raise Max Second Hand Distance or move GripPoint2.", this);
            }
        }
        wasTwoHanded = IsTwoHanded;
    }

    void UpdateTwoHandControl()
    {
        var hands = grab.interactorsSelecting;
        bool bothHolding = hands.Count >= 2;

        if (bothHolding && !twoHandControl)
            BeginTwoHandControl(hands[0].GetAttachTransform(grab), hands[1].GetAttachTransform(grab));
        else if (!bothHolding && twoHandControl)
            EndTwoHandControl();

        if (twoHandControl)
            DriveClub();
    }

    void BeginTwoHandControl(Transform first, Transform second)
    {
        hand0 = first;
        hand1 = second;

        Vector3 clubPos = rb.position;
        Quaternion clubRot = rb.rotation;

        Quaternion inv0 = Quaternion.Inverse(hand0.rotation);
        clubOffsetFromHand0 = inv0 * (clubPos - hand0.position);
        clubRotFromHand0 = inv0 * clubRot;

        Quaternion inv1 = Quaternion.Inverse(hand1.rotation);
        clubOffsetFromHand1 = inv1 * (clubPos - hand1.position);
        clubRotFromHand1 = inv1 * clubRot;

        // The club turns around the point between the two grips
        Vector3 firstGrip = grab.attachTransform != null ? grab.attachTransform.position : hand0.position;
        Vector3 secondGrip = secondGripPoint != null ? secondGripPoint.position : hand1.position;
        gripMidOffset = Quaternion.Inverse(clubRot) * ((firstGrip + secondGrip) * 0.5f - clubPos);

        // Take over from XRI while both hands hold the club
        grab.trackPosition = false;
        grab.trackRotation = false;
        twoHandControl = true;
    }

    void EndTwoHandControl()
    {
        if (!twoHandControl) return;
        twoHandControl = false;

        // Hand control back to XRI (the first hand)
        grab.trackPosition = true;
        grab.trackRotation = true;

        firstLock.Release();
        secondLock.Release();
    }

    void DriveClub()
    {
        if (hand0 == null || hand1 == null) return;

        // Where each hand on its own would put the club
        Quaternion rot0 = hand0.rotation * clubRotFromHand0;
        Quaternion rot1 = hand1.rotation * clubRotFromHand1;
        Vector3 pos0 = hand0.position + hand0.rotation * clubOffsetFromHand0;
        Vector3 pos1 = hand1.position + hand1.rotation * clubOffsetFromHand1;

        Quaternion rot = Quaternion.Slerp(rot0, rot1, secondHandInfluence);
        Vector3 mid0 = pos0 + rot0 * gripMidOffset;
        Vector3 mid1 = pos1 + rot1 * gripMidOffset;
        Vector3 mid = Vector3.Lerp(mid0, mid1, secondHandInfluence);
        Vector3 pos = mid - rot * gripMidOffset;

        // MovePosition gives the club real velocity, so hits on the ball use the stroke's speed
        rb.MovePosition(pos);
        rb.MoveRotation(rot);
    }

    // HIT CHECK

    bool CheckTwoHanded()
    {
        lastSecondDistance = -1f;
        var hands = grab.interactorsSelecting;
        if (hands.Count < 2) return false;
        if (secondGripPoint == null) return true;

        Transform secondHand = hands[1].GetAttachTransform(grab);
        lastSecondDistance = Vector3.Distance(secondHand.position, secondGripPoint.position);
        return lastSecondDistance <= maxSecondHandDistance;
    }

    void ApplyBallCollisions(bool allow)
    {
        if (allow && !collisionsAllowed && ClubOverlapsBall())
            allow = false;

        foreach (var c in clubColliders)
            foreach (var b in ballColliders)
                if (Usable(c) && Usable(b))
                    Physics.IgnoreCollision(c, b, !allow);

        collisionsAllowed = allow;
    }

    bool ClubOverlapsBall()
    {
        foreach (var c in clubColliders)
            foreach (var b in ballColliders)
            {
                if (!Usable(c) || !Usable(b) || !c.bounds.Intersects(b.bounds)) continue;
                if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                                               b, b.transform.position, b.transform.rotation, out _, out _))
                    return true;
            }
        return false;
    }
    static bool Usable(Collider c) => c != null && c.enabled && c.gameObject.activeInHierarchy;

    void UpdateHandLocks()
    {
        if (!lockHandModels || !twoHandControl || hand0 == null || hand1 == null) return;
        firstLock.Lock(hand0, grab.attachTransform);
        secondLock.Lock(hand1, secondGripPoint);
    }

    void LateUpdate()
    {
        UpdateHandLocks();

        bool show = showHint && secondGripPoint != null && grab.isSelected && !IsTwoHanded;

        if (!show)
        {
            if (hint != null) hint.gameObject.SetActive(false);
            return;
        }

        if (hint == null)
        {
            var go = new GameObject("TwoHandHint");
            hint = go.AddComponent<TextMeshPro>();
            hint.alignment = TextAlignmentOptions.Center;
            hint.rectTransform.sizeDelta = new Vector2(1.5f, 0.4f);
        }

        hint.text = hintText;
        hint.fontSize = hintFontSize;
        hint.color = hintColor;
        hint.gameObject.SetActive(true);
        hint.transform.position = secondGripPoint.position + hintOffset;

        Camera cam = Camera.main;
        if (cam != null)
            hint.transform.rotation = Quaternion.LookRotation(hint.transform.position - cam.transform.position);
    }

    void OnDestroy()
    {
        if (hint != null) Destroy(hint.gameObject);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"TwoHandedGrip: {NameOf(args.interactorObject)} is touching the club.", this);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"TwoHandedGrip: {NameOf(args.interactorObject)} grabbed the club " +
                      $"({grab.interactorsSelecting.Count} hand(s) holding it).", this);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"TwoHandedGrip: {NameOf(args.interactorObject)} let go of the club.", this);
    }

    static string NameOf(object interactor) => interactor is Component c ? c.name : interactor?.ToString();

    void OnDrawGizmosSelected()
    {
        if (secondGripPoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(secondGripPoint.position, maxSecondHandDistance);
    }
}