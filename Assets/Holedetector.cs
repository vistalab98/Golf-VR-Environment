using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// When the golf ball hits the trigger, a message appears.
[RequireComponent(typeof(Collider))]
public class HoleDetector : MonoBehaviour
{
    [Header("Ball")]
    public Rigidbody ball;
    public string ballName = "Golf_Ball";

    [Header("Detection")]
    public float maxSpeed = 1.5f;
    public float settleTime = 0.3f;

    [Header("Message")]
    public string message = "Nice shot!";
    public Color messageColor = new Color(1f, 0.85f, 0.2f);
    public float fontSize = 1f;
    [Tooltip("How far in front of the player's eyes the message appears (meters).")]
    public float distanceFromHead = 1.5f;
    [Tooltip("How far below eye level the message appears (meters).")]
    public float heightOffset = -0.1f;
    public float displaySeconds = 3f;
    public float fadeSeconds = 0.75f;

    [Header("Optional")]
    public AudioClip sound;
    public UnityEvent onBallInHole;

    float timeInside;
    bool scored;
    TextMeshPro messageText;
    Coroutine showRoutine;

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;

        if (ball == null)
        {
            var found = GameObject.Find(ballName);
            if (found != null) ball = found.GetComponent<Rigidbody>();
        }
        if (ball == null)
            Debug.LogWarning($"HoleDetector: no Rigidbody named '{ballName}' found. Assign the ball in the Inspector.", this);
    }

    void OnTriggerStay(Collider other)
    {
        if (scored || ball == null || other.attachedRigidbody != ball) return;

        if (ball.linearVelocity.magnitude <= maxSpeed)
            timeInside += Time.fixedDeltaTime;
        else
            timeInside = 0f;

        if (timeInside >= settleTime)
        {
            scored = true;
            BallInHole();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (ball != null && other.attachedRigidbody == ball)
        {
            timeInside = 0f;
            scored = false;
        }
    }

    void BallInHole()
    {
        Debug.Log("HoleDetector: ball is in the hole!");

        if (sound != null)
            AudioSource.PlayClipAtPoint(sound, transform.position);

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
        Vector3 position = head.position + flatForward * distanceFromHead + Vector3.up * heightOffset;
        messageText.transform.position = position;

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
}