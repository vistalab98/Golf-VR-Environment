using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

[RequireComponent(typeof(XROrigin))]
[DefaultExecutionOrder(-100)]
public class PlayerSpawnPoint : MonoBehaviour
{
    [Tooltip("An empty object on the course. The player starts above it, facing its blue (Z) arrow.")]
    public Transform spawnPoint;

    [Tooltip("Also turn the player to face the spawn point's direction.")]
    public bool matchFacing = true;

    [Tooltip("First load only: the longest to wait (seconds) for the headset to report tracking.")]
    public float maxWaitForTracking = 3f;

    public static bool IsReady { get; private set; }

    public static float ReadyTime { get; private set; }

    XROrigin origin;

    static bool calibrated;
    static Vector3 savedOriginPosition;
    static Quaternion savedOriginRotation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        calibrated = false;
        IsReady = false;
    }

    void Awake()
    {
        origin = GetComponent<XROrigin>();
        IsReady = false;

        if (calibrated)
        {
            origin.transform.SetPositionAndRotation(savedOriginPosition, savedOriginRotation);
            MarkReady();
        }
    }

    IEnumerator Start()
    {
        if (IsReady) yield break;

        if (spawnPoint == null)
        {
            Debug.LogWarning("PlayerSpawnPoint: no Spawn Point assigned. Starting without moving the player.", this);
            MarkReady();
            yield break;
        }

        Transform head = origin.Camera != null ? origin.Camera.transform : null;
        float giveUpAt = Time.time + maxWaitForTracking;
        while (head != null && Time.time < giveUpAt &&
               head.localPosition == Vector3.zero && head.localRotation == Quaternion.identity)
        {
            yield return null;
        }

        Recenter();
        MarkReady();
    }

    void MarkReady()
    {
        ReadyTime = Time.time;
        IsReady = true;
    }

    void Recenter()
    {
        if (spawnPoint == null || origin.Camera == null) return;

        Transform head = origin.Camera.transform;

        if (matchFacing)
        {
            Vector3 headForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            Vector3 targetForward = Vector3.ProjectOnPlane(spawnPoint.forward, Vector3.up);
            if (headForward.sqrMagnitude > 0.001f && targetForward.sqrMagnitude > 0.001f)
            {
                float angle = Vector3.SignedAngle(headForward, targetForward, Vector3.up);
                origin.RotateAroundCameraUsingOriginUp(angle);
            }
        }


        Vector3 offset = spawnPoint.position - head.position;
        offset.y = 0f;
        origin.transform.position += offset;

        savedOriginPosition = origin.transform.position;
        savedOriginRotation = origin.transform.rotation;
        calibrated = true;
    }

    public static IEnumerator WaitUntilReady()
    {
        if (FindAnyObjectByType<PlayerSpawnPoint>() == null)
        {
            ReadyTime = Time.time;
            IsReady = true;
            yield break;
        }

        while (!IsReady)
            yield return null;
    }
}