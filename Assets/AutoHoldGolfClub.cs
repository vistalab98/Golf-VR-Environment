using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class AutoHoldGolfClub : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    public XRDirectInteractor handInteractor;

    void Start()
    {
        if (handInteractor != null)
        {
            XRGrabInteractable grabInteractable = GetComponent<XRGrabInteractable>();

            if (grabInteractable != null)
            {
                handInteractor.interactionManager.SelectEnter(
                    (IXRSelectInteractor)handInteractor,
                    (IXRSelectInteractable)grabInteractable
                );
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
