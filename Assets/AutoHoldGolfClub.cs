using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class AutoHoldVR : MonoBehaviour
{
    [Header("Setup")]
    public XRDirectInteractor handInteractor; 

    [Header("Manual Grip Calibration")]
    [Tooltip("Move the club forward/backward/left/right relative to the controller's center")]
    public Vector3 localPositionOffset = new Vector3(0f, -0.1f, 0.2f);
    
    [Tooltip("Tilt the club angles here. Set X to around 45 to angle it like a golf club!")]
    public Vector3 localRotationOffset = new Vector3(45f, 0f, 0f);

    private bool isAttached = false;
    private Transform handTransform;

    void Start()
    {
        if (handInteractor != null)
        {
            XRGrabInteractable grabInteractable = GetComponent<XRGrabInteractable>();
            
            if (grabInteractable != null)
            {
                // Forcing the grab
                handInteractor.interactionManager.SelectEnter(
                    (IXRSelectInteractor)handInteractor, 
                    (IXRSelectInteractable)grabInteractable
                );

                // locking to hand's transform reference
                handTransform = handInteractor.transform;
                isAttached = true;
            }
        }
    }
    void LateUpdate()
    {
        if (isAttached && handTransform != null)
        {
            // Force position to stay to the hand
            transform.position = handTransform.TransformPoint(localPositionOffset);

            // Force rotation angle to stay to the hand
            transform.rotation = handTransform.rotation * Quaternion.Euler(localRotationOffset);
        }
    }
}
