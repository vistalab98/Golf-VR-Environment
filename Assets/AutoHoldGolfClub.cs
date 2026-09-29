using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRGrabInteractable))]
public class AutoHoldGolfClub : MonoBehaviour
{
    public XRDirectInteractor handInteractor;

    public string handNameContains = "Right";


    public Transform gripPoint;

    public Vector3 localPositionOffset = new Vector3(0f, -0.1f, 0.2f);

    public Vector3 localRotationOffset = new Vector3(45f, 0f, 0f);

    XRGrabInteractable grab;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        grab.useDynamicAttach = false;
    }

    IEnumerator Start()
    {
        yield return null;

        if (handInteractor == null)
            handInteractor = FindHand();

        if (handInteractor == null)
        {
            Debug.LogWarning($"AutoHoldGolfClub: no XR Direct Interactor containing '{handNameContains}' found under the XR Origin.", this);
            yield break;
        }

        if (gripPoint != null)
            grab.attachTransform = gripPoint;
        else
            CreateGripPoint();

        handInteractor.StartManualInteraction((IXRSelectInteractable)grab);
    }

    public void Drop()
    {
        if (handInteractor != null)
            handInteractor.EndManualInteraction();
    }

    void CreateGripPoint()
    {
        Transform handAttach = handInteractor.GetAttachTransform(grab);

        transform.SetPositionAndRotation(
            handAttach.TransformPoint(localPositionOffset),
            handAttach.rotation * Quaternion.Euler(localRotationOffset));

        gripPoint = new GameObject("GripPoint").transform;
        gripPoint.SetParent(transform, false);
        gripPoint.SetPositionAndRotation(handAttach.position, handAttach.rotation);

        grab.attachTransform = gripPoint;
    }

    XRDirectInteractor FindHand()
    {
        var origin = FindAnyObjectByType<XROrigin>();
        if (origin == null) return null;

        foreach (var hand in origin.GetComponentsInChildren<XRDirectInteractor>(true))
        {
            for (Transform t = hand.transform; t != null; t = t.parent)
            {
                if (t.name.Contains(handNameContains))
                    return hand;
            }
        }
        return null;
    }
}