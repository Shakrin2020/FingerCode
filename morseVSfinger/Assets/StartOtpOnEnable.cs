using System.Collections;
using UnityEngine;

public class StartOtpOnEnable : MonoBehaviour
{
    [SerializeField] private RhythmControllerV1 rhythm;   // drag AuthenticationController here
    [SerializeField] private bool restartFromPhase0 = true;
    [SerializeField] private bool waitOneFrame = true;    // avoids buzz “on click”

    private bool _arming;   // prevents double-fire if panel toggles quickly

    private void OnEnable()
    {
        if (rhythm == null || _arming) return;
        StartCoroutine(Begin());
    }

    private IEnumerator Begin()
    {
        _arming = true;
        if (waitOneFrame) yield return null;   // wait until UI is active

        if (restartFromPhase0)
        {
            rhythm.StartFromPhase0();          // uses method in section B
        }
        else
        {
            rhythm.StartOtp();                 // continue from currentSegment
        }
        _arming = false;
    }

    public void ForceStart()
    {
        StartFromPhase0();
    }

    private void StartFromPhase0()
    {
        rhythm.StartFromPhase0();
    }

}
