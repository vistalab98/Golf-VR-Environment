using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class HoleDetector : MonoBehaviour
{
    public enum ResetMode
    {
        [InspectorName("Off (stay until R or F)")] Off,
        [InspectorName("Like F (attempt marked discarded)")] LikeF,
        [InspectorName("Like R (attempt marked saved)")] LikeR
    }

    [Header("Ball")]
    [Tooltip("Leave empty to find an object named Golf_Ball automatically.")]
    public Rigidbody ball;
    public string ballName = "Golf_Ball";

    [Header("Hole Size (only used when this object has no collider)")]
    public float radius = 0f;
    [Tooltip("How far above the hole's surface the ball's center can be and still count (meters).")]
    public float maxHeightAbove = 0.1f;

    [Header("Detection")]
    [Tooltip("On: the ball counts the instant it touches the zone, at any speed.\nOff: it must be slower than Max Speed and stay for Settle Time.")]
    public bool countInstantly = true;
    [Tooltip("Only used when Count Instantly is off. The ball must be slower than this (m/s) to count.")]
    public float maxSpeed = 3f;
    [Tooltip("Only used when Count Instantly is off. How long (seconds) the ball must stay in the zone.")]
    public float settleTime = 0.05f;
    [Tooltip("Print to the Console whenever the ball is near the hole but isn't counted, and why.")]
    public bool debugLogs = true;

    [Header("Message")]
    public string message = "Nice shot!\nThe ball is in the hole!";
    public Color messageColor = new Color(1f, 0.85f, 0.2f);
    public float fontSize = 1f;
    [Tooltip("How far in front of the player's eyes the message appears (meters).")]
    public float distanceFromHead = 1.5f;
    [Tooltip("How far below eye level the message appears (meters).")]
    public float heightOffset = -0.1f;
    public float displaySeconds = 3f;
    public float fadeSeconds = 0.75f;

    [Header("After Scoring")]
    [Tooltip("Remove the ball from the course once it's in. It comes back when the scene reloads (R or F).")]
    public bool hideBallWhenScored = true;
    [Tooltip("Automatically reset the scene after a countdown once the ball is in.")]
    public ResetMode autoReset = ResetMode.LikeF;
    [Tooltip("Seconds to count down before the automatic reset.")]
    public float resetCountdown = 3f;
    [Tooltip("Countdown text shown under the message. {0} is replaced by the seconds left.")]
    public string countdownText = "Resetting in {0}";

    [Header("Optional")]
    public AudioClip sound;
    [Tooltip("Extra things to trigger, e.g. SessionRecorder.SaveSession or a particle effect.")]
    public UnityEvent onBallInHole;

    Collider zone;
    Vector3 lastBallPos;
    bool hasLastBallPos;
    float timeInside;
    bool scored;
    float lastDebugLog;
    TextMeshPro messageText;
    Coroutine showRoutine;

    void Awake()
    {
        zone = GetComponent<Collider>();
        if (zone != null)
            zone.isTrigger = true;

        if (ball == null)
        {
            var found = GameObject.Find(ballName);
            if (found != null) ball = found.GetComponent<Rigidbody>();
        }
        if (ball == null)
            Debug.LogWarning($"HoleDetector: no Rigidbody named '{ballName}' found. Assign the ball in the Inspector.", this);
    }
    Vector3 HoleCenter()
    {
        var r = GetComponent<Renderer>();
        if (r != null)
        {
            Vector3 c = r.bounds.center;
            c.y = r.bounds.max.y;
            return c;
        }
        return transform.position;
    }

    float HoleRadius()
    {
        if (radius > 0f) return radius;
        var r = GetComponent<Renderer>();
        if (r != null) return Mathf.Min(r.bounds.extents.x, r.bounds.extents.z);
        return 0.054f;
    }

    void FixedUpdate()
    {
        if (ball == null || scored) return;

        Vector3 ballPos = ball.position;
        float speed = ball.linearVelocity.magnitude;
        bool slowEnough = countInstantly || speed <= maxSpeed;

        bool passedThrough = false;
        if (countInstantly && zone != null && hasLastBallPos)
        {
            Vector3 step = ballPos - lastBallPos;
            float stepLength = step.magnitude;
            if (stepLength > 0.0001f)
            {
                passedThrough = zone.Raycast(new Ray(lastBallPos, step / stepLength), out _, stepLength)
                             || zone.Raycast(new Ray(ballPos, -step / stepLength), out _, stepLength);
            }
        }
        lastBallPos = ballPos;
        hasLastBallPos = true;

        bool withinRadius, lowEnough;
        float distance, holeRadius, heightAbove;

        if (zone != null)
        {
            Vector3 closest = zone.ClosestPoint(ballPos);
            distance = Vector3.Distance(closest, ballPos);
            holeRadius = zone.bounds.extents.magnitude;
            withinRadius = distance < 0.0001f;
            lowEnough = true;
            heightAbove = 0f;
        }
        else
        {
            Vector3 center = HoleCenter();
            Vector3 flat = ballPos - center;
            heightAbove = flat.y;
            flat.y = 0f;
            distance = flat.magnitude;
            holeRadius = HoleRadius();
            withinRadius = distance <= holeRadius;
            lowEnough = heightAbove <= maxHeightAbove;
        }

        if (passedThrough || (withinRadius && lowEnough && slowEnough))
        {
            timeInside += Time.fixedDeltaTime;
            if (countInstantly || timeInside >= settleTime)
            {
                scored = true;
                BallInHole();
            }
            return;
        }

        timeInside = 0f;

        bool near = zone != null ? distance <= 0.3f : distance <= holeRadius * 2f;
        if (debugLogs && near && Time.time - lastDebugLog > 0.25f)
        {
            lastDebugLog = Time.time;
            string reason = !withinRadius
                ? (zone != null ? $"{distance:F3} m outside the {zone.GetType().Name}"
                                : $"{distance:F3} m from center (radius {holeRadius:F3} m)")
                          : !lowEnough ? $"too high: {heightAbove:F3} m above the hole (max {maxHeightAbove} m)"
                          : $"too fast: {speed:F2} m/s (max {maxSpeed} m/s)";
            Debug.Log($"HoleDetector: ball near the hole but not counted, {reason}.", this);
        }
    }

    void BallInHole()
    {
        Debug.Log("HoleDetector: ball is in the hole!", this);

        if (sound != null)
            AudioSource.PlayClipAtPoint(sound, transform.position);

        if (hideBallWhenScored && ball != null)
        {
            if (!ball.isKinematic)
            {
                ball.linearVelocity = Vector3.zero;
                ball.angularVelocity = Vector3.zero;
            }
            ball.gameObject.SetActive(false);
        }

        if (showRoutine != null) StopCoroutine(showRoutine);
        showRoutine = StartCoroutine(ShowMessage());

        onBallInHole?.Invoke();
    }

    IEnumerator ShowMessage()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("HoleDetector: no camera tagged MainCamera, can't place the message.");
            if (autoReset != ResetMode.Off)
            {
                yield return new WaitForSeconds(resetCountdown);
                ResetScene();
            }
            yield break;
        }
        Transform head = cam.transform;

        if (messageText == null)
        {
            var go = new GameObject("HoleMessage");
            messageText = go.AddComponent<TextMeshPro>();
            messageText.alignment = TextAlignmentOptions.Center;
            messageText.rectTransform.sizeDelta = new Vector2(3f, 1f);
        }

        messageText.text = message;
        messageText.fontSize = fontSize;
        messageText.color = messageColor;
        messageText.alpha = 1f;
        messageText.gameObject.SetActive(true);

        Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.01f) flatForward = head.up;
        messageText.transform.position = head.position + flatForward * distanceFromHead + Vector3.up * heightOffset;

        if (autoReset != ResetMode.Off)
        {
            float remaining = resetCountdown;
            while (remaining > 0f)
            {
                messageText.transform.rotation = Quaternion.LookRotation(messageText.transform.position - head.position);
                messageText.text = message + "\n" + string.Format(countdownText, Mathf.CeilToInt(remaining));
                remaining -= Time.deltaTime;
                yield return null;
            }
            ResetScene();
            yield break;
        }

        float elapsed = 0f;
        float total = displaySeconds + fadeSeconds;
        while (elapsed < total)
        {
            messageText.transform.rotation = Quaternion.LookRotation(messageText.transform.position - head.position);

            if (elapsed > displaySeconds)
                messageText.alpha = 1f - (elapsed - displaySeconds) / fadeSeconds;

            elapsed += Time.deltaTime;
            yield return null;
        }

        messageText.gameObject.SetActive(false);
        showRoutine = null;
    }

    void ResetScene()
    {
        var recorder = FindAnyObjectByType<SessionRecorder>();
        if (recorder != null)
        {
            Debug.Log($"HoleDetector: countdown finished, resetting {(autoReset == ResetMode.LikeR ? "like R (saved)" : "like F (discarded)")}.", this);
            if (autoReset == ResetMode.LikeR)
                recorder.SaveSession();
            else
                recorder.DiscardSession();
            return;
        }

        int index = SceneManager.GetActiveScene().buildIndex;
        if (index >= 0)
            SceneManager.LoadScene(index);
        else
            Debug.LogError("HoleDetector: this scene isn't in the build's scene list, so it can't be reloaded.", this);
    }

    void OnDrawGizmosSelected()
    {
        if (GetComponent<Collider>() != null) return;
        Vector3 center = HoleCenter();
        float r = HoleRadius();
        Gizmos.color = Color.green;
        const int segments = 32;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 p0 = center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r;
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p0 + Vector3.up * maxHeightAbove, p1 + Vector3.up * maxHeightAbove);
        }
    }
}