using UnityEngine;

public class GolfPhysics : MonoBehaviour
{
    [Header("Physics Tweaks")]
    public float powerMultiplier = 2.5f; 
    public float liftAmount = 0.3f;      

    private Vector3 previousPosition;
    public Vector3 clubVelocity;
    public Vector3 clubStrikeVelocity = Vector3.zero;

    void Start()
    {
        previousPosition = transform.position;
    }

    void Update()
    {
        clubVelocity = (transform.position - previousPosition) / Time.deltaTime;
        previousPosition = transform.position;
    }
    private void OnCollisionEnter(Collision collision)
    {
        // Check if the solid club hit the golf ball
        if (collision.gameObject.CompareTag("Golf Ball"))
        {
            Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();

            if (ballRb != null)
            {
                ballRb.linearVelocity = Vector3.zero;
                ballRb.angularVelocity = Vector3.zero;

                Vector3 strikeDirection = clubVelocity.normalized;
                strikeDirection.y += liftAmount;
                strikeDirection = strikeDirection.normalized;

                if (clubStrikeVelocity == Vector3.zero) {
                    Debug.Log($"Strike with vector: {clubVelocity}");
                    clubStrikeVelocity.x = clubVelocity.x;
                    clubStrikeVelocity.y = clubVelocity.y;
                    clubStrikeVelocity.z = clubVelocity.z;
                }

                float swingSpeed = clubVelocity.magnitude;
                float launchForce = swingSpeed * powerMultiplier;

                ballRb.AddForce(strikeDirection * launchForce, ForceMode.Impulse);
            }
        }
    }
}
