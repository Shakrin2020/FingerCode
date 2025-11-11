using UnityEngine;
using TMPro;

public class PhaseProgressUI : MonoBehaviour
{
    [Header("Single message text (TMP)")]
    [SerializeField] TMP_Text message;

    [Header("Texts (optional to edit in Inspector)")]
    [SerializeField] string phase1Text = "Phase 1";
    [SerializeField] string phase2Text = "Phase 2";
    [SerializeField] string phase3Text = "Phase 3";
    [SerializeField] string successText = "Success";
    [SerializeField] string mismatchText = "OTP Code Mismatch – Try Again";

    void Awake()
    {
        if (message) message.text = "";   // start blank
    }

    // Hook this to RhythmControllerV1.OnSegmentStart(int)
    public void HighlightPhase(int seg)
    {
        if (!message) return;
        gameObject.SetActive(true);

        switch (seg)
        {
            case 0: message.text = phase1Text; break;
            case 1: message.text = phase2Text; break;
            case 2: message.text = phase3Text; break;
            default: message.text = $"Phase {seg + 1}"; break; // fallback for >3
        }
    }

    // Optional no-op you already had
    public void MarkPassed(int seg) { }

    // Hook this to RhythmControllerV1.OnAllSegmentsPass
    public void ShowSuccess()
    {
        if (!message) return;
        gameObject.SetActive(true);
        message.text = successText;
    }

    // NEW: hook this to RhythmControllerV1.OnMorseCodeIncorrect
    public void ShowMismatch()
    {
        if (!message) return;
        gameObject.SetActive(true);
        message.text = mismatchText;
    }

    // Call before starting/retrying
    public void ResetAll()
    {
        if (message) message.text = "";
        gameObject.SetActive(true); // keep visible; set false if you prefer hidden
    }
}
