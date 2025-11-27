using TMPro;
using UnityEngine;

public class EnrollTimerUI : MonoBehaviour
{
    [SerializeField] TMP_Text timerText;

    private FingerprintWsClient client;

    void Awake()
    {
        if (timerText == null)
        {
            Debug.LogWarning("EnrollTimerUI: timerText is not assigned in Inspector.");
        }

        // Try to cache the client if it already exists
        client = FingerprintWsClient.I ?? FindObjectOfType<FingerprintWsClient>();
    }

    void OnEnable()
    {
        // Make sure we have a client reference
        if (client == null)
        {
            client = FingerprintWsClient.I ?? FindObjectOfType<FingerprintWsClient>();
        }

        if (client == null)
        {
            Debug.LogWarning("EnrollTimerUI: no FingerprintWsClient found in scene.");
            return;
        }

        client.OnEnrollTimer += HandleTimer;
        client.OnVerifyTimer += HandleTimer;
    }

    void OnDisable()
    {
        if (client == null) return;

        client.OnEnrollTimer -= HandleTimer;
        client.OnVerifyTimer -= HandleTimer;
    }

    void HandleTimer(int secs)
    {
        if (timerText == null) return;

        // show blank when 0
        timerText.text = secs > 0 ? secs.ToString() : "";
    }
}
