using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// Tracks the player's head and both hands every frame: position, rotation,
/// velocity, angular velocity, speed, and a short history of recent samples.
public class PlayerTracker : MonoBehaviour
{
    [Header("Targets")]
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Tooltip("Name of the left hand object under the XR Origin.")]
    public string leftHandName = "Left Controller";
    [Tooltip("Name of the right hand object under the XR Origin.")]
    public string rightHandName = "Right Controller";

    [Header("Settings")]
    [Tooltip("0 = raw values (jittery), closer to 1 = smoother but slower to react.")]
    [Range(0f, 0.95f)] public float velocitySmoothing = 0.5f;
    [Tooltip("How many seconds of history to keep for each tracked part.")]
    public float historySeconds = 2f;

    [Header("Debug")]
    public bool drawGizmos = true;
    public bool logSpeeds = false;

    [Header("Live Readout (updates during Play mode)")]
    public bool showReadout = true;
    public PartReadout headReadout = new PartReadout();
    public PartReadout leftHandReadout = new PartReadout();
    public PartReadout rightHandReadout = new PartReadout();

    public TrackedPart Head { get; private set; }
    public TrackedPart LeftHand { get; private set; }
    public TrackedPart RightHand { get; private set; }

    XROrigin origin;

    void Start()
    {
        origin = FindAnyObjectByType<XROrigin>();
        if (origin == null)
        {
            Debug.LogWarning("PlayerTracker: no XR Origin found in the scene.", this);
            return;
        }

        if (head == null && origin.Camera != null) head = origin.Camera.transform;
        if (leftHand == null) leftHand = FindChild(leftHandName);
        if (rightHand == null) rightHand = FindChild(rightHandName);

        Head = new TrackedPart("Head", head);
        LeftHand = new TrackedPart("Left Hand", leftHand);
        RightHand = new TrackedPart("Right Hand", rightHand);

        foreach (var part in new[] { Head, LeftHand, RightHand })
        {
            if (part.Target == null)
                Debug.LogWarning($"PlayerTracker: could not find {part.Name}. Assign it in the Inspector.", this);
        }
    }

    void LateUpdate()
    {
        if (origin == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Head.Sample(origin.transform, dt, velocitySmoothing, historySeconds);
        LeftHand.Sample(origin.transform, dt, velocitySmoothing, historySeconds);
        RightHand.Sample(origin.transform, dt, velocitySmoothing, historySeconds);

        if (showReadout)
        {
            headReadout.CopyFrom(Head);
            leftHandReadout.CopyFrom(LeftHand);
            rightHandReadout.CopyFrom(RightHand);
        }

        if (logSpeeds)
            Debug.Log($"Head {Head.Speed:F2} m/s | Left {LeftHand.Speed:F2} m/s | Right {RightHand.Speed:F2} m/s");
    }

    [ContextMenu("Reset Peak Speeds")]
    public void ResetPeaks()
    {
        Head?.ResetPeak();
        LeftHand?.ResetPeak();
        RightHand?.ResetPeak();
    }

    Transform FindChild(string childName)
    {
        foreach (var t in origin.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName)
                return t;
        }
        return null;
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying) return;
        DrawPart(Head, Color.yellow);
        DrawPart(LeftHand, Color.cyan);
        DrawPart(RightHand, Color.magenta);
    }

    static void DrawPart(TrackedPart part, Color color)
    {
        if (part == null || part.Target == null) return;

        // Velocity line; longer = faster
        Gizmos.color = color;
        Gizmos.DrawLine(part.Position, part.Position + part.Velocity * 0.25f);
        Gizmos.DrawWireSphere(part.Position, 0.03f);

        // Recent path
        var history = part.History;
        for (int i = 1; i < history.Count; i++)
            Gizmos.DrawLine(history[i - 1].position, history[i].position);
    }
}

public struct TrackedSample
{
    public float time;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
}
public class TrackedPart
{
    public string Name { get; }
    public Transform Target { get; }

    public Vector3 Position { get; private set; }
    public Quaternion Rotation { get; private set; }

    public Vector3 LocalPosition { get; private set; }
    public Quaternion LocalRotation { get; private set; }

    /// Velocity in m/s (world directions), Measured relative to the XR Origin.
    public Vector3 Velocity { get; private set; }

    /// Angular velocity in radians per second (world axes).
    public Vector3 AngularVelocity { get; private set; }

    public float Speed => Velocity.magnitude;
    public float PeakSpeed { get; private set; }

    /// Recent samples, oldest first.
    public IReadOnlyList<TrackedSample> History => history;

    readonly List<TrackedSample> history = new List<TrackedSample>();
    bool hasPrevious;

    public TrackedPart(string name, Transform target)
    {
        Name = name;
        Target = target;
    }

    public void Sample(Transform space, float dt, float smoothing, float historySeconds)
    {
        if (Target == null) return;

        Vector3 newLocalPos = space.InverseTransformPoint(Target.position);
        Quaternion newLocalRot = Quaternion.Inverse(space.rotation) * Target.rotation;

        if (hasPrevious)
        {
            // Linear velocity
            Vector3 localVel = (newLocalPos - LocalPosition) / dt;
            Vector3 worldVel = space.TransformDirection(localVel);
            Velocity = Vector3.Lerp(worldVel, Velocity, smoothing);

            // Angular velocity
            Quaternion delta = newLocalRot * Quaternion.Inverse(LocalRotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 localAngVel = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
            AngularVelocity = Vector3.Lerp(space.TransformDirection(localAngVel), AngularVelocity, smoothing);

            PeakSpeed = Mathf.Max(PeakSpeed, Speed);
        }

        LocalPosition = newLocalPos;
        LocalRotation = newLocalRot;
        Position = Target.position;
        Rotation = Target.rotation;
        hasPrevious = true;

        float now = Time.time;
        history.Add(new TrackedSample { time = now, position = Position, rotation = Rotation, velocity = Velocity });
        int removeCount = 0;
        while (removeCount < history.Count && now - history[removeCount].time > historySeconds)
            removeCount++;
        if (removeCount > 0)
            history.RemoveRange(0, removeCount);
    }

    public void ResetPeak() => PeakSpeed = 0f;
}

[System.Serializable]
public class PartReadout
{
    [ReadOnlyInInspector] public Vector3 position;
    [ReadOnlyInInspector] public Vector3 rotation;
    [ReadOnlyInInspector] public Vector3 velocity;
    [ReadOnlyInInspector, Tooltip("Meters per second")] public float speed;
    [ReadOnlyInInspector, Tooltip("Highest speed since the last reset (m/s)")] public float peakSpeed;
    [ReadOnlyInInspector, Tooltip("Degrees per second")] public float spinSpeed;

    public void CopyFrom(TrackedPart part)
    {
        if (part == null || part.Target == null) return;
        position = part.Position;
        rotation = part.Rotation.eulerAngles;
        velocity = part.Velocity;
        speed = part.Speed;
        peakSpeed = part.PeakSpeed;
        spinSpeed = part.AngularVelocity.magnitude * Mathf.Rad2Deg;
    }
}

public class ReadOnlyInInspectorAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyInInspectorAttribute))]
public class ReadOnlyInInspectorDrawer : UnityEditor.PropertyDrawer
{
    public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label)
        => UnityEditor.EditorGUI.GetPropertyHeight(property, label, true);

    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        using (new UnityEditor.EditorGUI.DisabledScope(true))
            UnityEditor.EditorGUI.PropertyField(position, property, label, true);
    }
}
#endif