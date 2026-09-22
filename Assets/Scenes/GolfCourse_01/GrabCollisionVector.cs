using UnityEngine;

public class GrabCollisionVector : MonoBehaviour
{
    // Grab the collision vector on collision
    private void OnCollisionEnter(Collision collision) {

        // Check for rigidbody and that the collision is with the club
        if (TryGetComponent<Rigidbody>(out Rigidbody myRigidbody) && collision.gameObject.CompareTag("Club")) {
            Vector3 myCollisionVector = myRigidbody.linearVelocity;

            Debug.Log("The ball was hit by the club in the vector : " + myCollisionVector);
        }
    }
}
