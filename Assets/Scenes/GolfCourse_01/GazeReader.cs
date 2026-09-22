using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.Android

public class GazeReader : MonoBehaviour
{
    [SerializeField] private XRGazeInteractor gazeInteractor;

    void Start() {
        if (!Permission.HasUserAuthorizedPermission("android.permission.EYE_TRACKING_FINE")) {
            Permission.RequestUserPermission("android.permission.EYE_TRACKING_FINE");
        }
    }

    void Update() {
        if (gazeInteractor != null && gazeInteractor.enabled)
        {
            // Method 1: Get the forward vector from the interactor's transform
            Vector3 gazeDirection = gazeInteractor.transform.forward;
            Vector3 gazeOrigin = gazeInteractor.transform.position;

            Debug.DrawRay(gazeOrigin, gazeDirection * 5f, Color.red);
        }
    }
}
