using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DelayedBallSpawn : MonoBehaviour
{
    [Tooltip("Seconds after the run starts before the ball appears.")]
    public float spawnDelay = 2f;

    [Tooltip("Optional: sound to play when the ball appears.")]
    public AudioClip spawnSound;

    /// <summary>True once the ball has appeared.</summary>
    public bool HasSpawned { get; private set; }

    Rigidbody rb;
    Renderer[] renderers;
    Collider[] colliders;
    Vector3 startPosition;
    Quaternion startRotation;
    bool wasKinematic;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);

        startPosition = transform.position;
        startRotation = transform.rotation;
        wasKinematic = rb.isKinematic;

        SetBallActive(false);
    }

    IEnumerator Start()
    {
        yield return PlayerSpawnPoint.WaitUntilReady();
        float spawnAt = PlayerSpawnPoint.ReadyTime + spawnDelay;
        while (Time.time < spawnAt)
            yield return null;
        Spawn();
    }
    public void Spawn()
    {
        rb.position = startPosition;
        rb.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);

        SetBallActive(true);

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (spawnSound != null)
            AudioSource.PlayClipAtPoint(spawnSound, startPosition);

        HasSpawned = true;
    }

    void SetBallActive(bool active)
    {
        foreach (var r in renderers) r.enabled = active;
        foreach (var c in colliders) c.enabled = active;

        if (active)
        {
            rb.isKinematic = wasKinematic;
            rb.WakeUp();
        }
        else
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
        }
    }
}