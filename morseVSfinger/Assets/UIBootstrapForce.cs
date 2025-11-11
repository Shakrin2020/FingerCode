using UnityEngine;

public class UIBootstrapForce : MonoBehaviour
{
    [SerializeField] Canvas rootCanvas;
    [SerializeField] Camera xrCam;

    void Awake()
    {
        if (!rootCanvas) rootCanvas = GetComponentInChildren<Canvas>(true);
        if (!xrCam) xrCam = Camera.main;

        // Ensure the whole UI hierarchy is on
        gameObject.SetActive(true);
        if (rootCanvas) rootCanvas.enabled = true;

        // Make render mode safe for APK
        if (rootCanvas)
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            rootCanvas.worldCamera = xrCam;
            rootCanvas.planeDistance = 1f;
        }

        // Put the UI in view even if transforms were odd
        if (xrCam && rootCanvas && rootCanvas.renderMode == RenderMode.WorldSpace)
        {
            var t = rootCanvas.transform;
            t.position = xrCam.transform.position + xrCam.transform.forward * 1.2f;
            t.rotation = Quaternion.LookRotation(t.position - xrCam.transform.position);
            t.localScale = Vector3.one;
        }

        Debug.Log("[UI] Forced UI visible in build.");
    }
}
