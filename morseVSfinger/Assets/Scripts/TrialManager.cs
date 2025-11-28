using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum AuthMethod
{
    Fingerprint,
    Morse
}


public class TrialManager : MonoBehaviour
{
    public static TrialManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] AuthUIController authUI;      // drag your UIController object here
    [SerializeField] RhythmControllerV1 morse;     // drag your Morse controller here (if you want)

    [Header("Experiment Settings")]
    public bool experimentEnabled = true;          // turn this on when you want 8-attempt logic
    public bool fingerprintFirst = true;           // if false, Morse first

    string currentUser;
    int fingerprintAttempts;   // 0..4 (1 practice + 3 actual)
    int morseAttempts;         // 0..4
    bool sessionActive;
    AuthMethod currentMethod;
    bool firstTrialStarted;

    [Header("Popup UI")]
    [SerializeField] GameObject popupPanel;
    [SerializeField] TMP_Text popupLabel;

    Coroutine _popupCo;

    [Header("Delays (seconds)")]
    public float fingerprintResultDelay = 2.0f;  // time to show 'verified / wrong finger'
    public float morseResultDelay = 1.0f;        // time to breathe after Morse

    // Track practice completion
    bool fingerprintPracticeFinished = false;
    bool morsePracticeFinished = false;
    bool practiceDonePopupShown = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Called when ESP replies "user exist" after login-by-name
    public void OnUserExists(string userName)
    {
        if (!experimentEnabled) return;

        currentUser = userName;
        fingerprintAttempts = 0;
        morseAttempts = 0;
        sessionActive = true;
        firstTrialStarted = false;
        currentMethod = fingerprintFirst ? AuthMethod.Fingerprint : AuthMethod.Morse;

        // 🔹 reset practice flags
        fingerprintPracticeFinished = false;
        morsePracticeFinished = false;
        practiceDonePopupShown = false;

        Debug.Log($"[TrialManager] Session started for user '{currentUser}'. " +
                  $"First method = {currentMethod}");
        // AuthUIController will still show the AuthMethod panel as usual.
    }

    bool SessionDone()
    {
        return fingerprintAttempts >= 4 && morseAttempts >= 4;
    }

    int GetAttempts(AuthMethod m)
    {
        return (m == AuthMethod.Fingerprint) ? fingerprintAttempts : morseAttempts;
    }

    void IncAttempts(AuthMethod m)
    {
        if (m == AuthMethod.Fingerprint) fingerprintAttempts++;
        else morseAttempts++;
    }

    AuthMethod GetNextMethod()
    {
        AuthMethod alt = (currentMethod == AuthMethod.Fingerprint)
            ? AuthMethod.Morse
            : AuthMethod.Fingerprint;

        // if alternate already has 4 attempts, stay on current if possible
        if (GetAttempts(alt) >= 4 && GetAttempts(currentMethod) < 4)
            return currentMethod;

        return alt;
    }

    // Call this AFTER a fingerprint login attempt completed
    public void OnFingerprintResult(bool success)
    {
        if (!experimentEnabled || !sessionActive) return;

        IncAttempts(AuthMethod.Fingerprint);
        Debug.Log($"[TrialManager] FP attempt #{fingerprintAttempts} for '{currentUser}', success={success}");

        OnAnyTrialFinished(AuthMethod.Fingerprint);
    }

    // Call this AFTER a Morse login attempt completed
    public void OnMorseResult(bool success)
    {
        if (!experimentEnabled || !sessionActive) return;

        IncAttempts(AuthMethod.Morse);
        Debug.Log($"[TrialManager] Morse attempt #{morseAttempts} for '{currentUser}', success={success}");

        OnAnyTrialFinished(AuthMethod.Morse);
    }

    void OnAnyTrialFinished(AuthMethod methodJustFinished)
    {
        // attempts AFTER increment
        int attemptsForThisMethod = GetAttempts(methodJustFinished);

        // 🔹 mark practice finished for this method if this was its first attempt
        if (attemptsForThisMethod == 1)
        {
            if (methodJustFinished == AuthMethod.Fingerprint)
                fingerprintPracticeFinished = true;
            else
                morsePracticeFinished = true;

            // Only when BOTH FP and MC have done their first attempt,
            // and we haven't shown this before, show "Practice done".
            if (fingerprintPracticeFinished && morsePracticeFinished && !practiceDonePopupShown)
            {
                practiceDonePopupShown = true;
                ShowPopup("Practice done", 2f);
            }
        }

        // 🔹 Check if all 8 attempts (4 FP + 4 MC) are done
        if (SessionDone())
        {
            sessionActive = false;
            Debug.Log($"[TrialManager] All 8 attempts finished for '{currentUser}'.");

            ShowPopup("Task completed", 3f);
            return;
        }

        // Decide which method is next and start it after the usual delay
        currentMethod = GetNextMethod();
        StartCoroutine(StartNextAfterDelay(methodJustFinished));
    }


    // Called by AuthUIController when the user clicks FP or MC on the method panel
    public bool OnUserClickedMethod(AuthMethod method)
    {
        if (!experimentEnabled || !sessionActive)
            return false;  // let AuthUI do its normal work

        // If we already started the first trial, ignore user clicks;
        // the scheduler controls the rest.
        if (firstTrialStarted)
            return false;

        // This is the VERY FIRST method the user chose
        firstTrialStarted = true;
        currentMethod = method;

        Debug.Log($"[TrialManager] First method chosen: {method}");

        // Start first trial with normal practice popup flow
        StartNextTrial();   // this will run StartNextTrialCo and show "Practice"
        return true;        // tell AuthUI: "I handled this click, don't run your usual code"
    }


    private System.Collections.IEnumerator StartNextAfterDelay(AuthMethod finishedMethod)
    {
        if (finishedMethod == AuthMethod.Fingerprint)
        {
            // Show "verified"/"wrong finger" for a bit
            yield return new WaitForSeconds(fingerprintResultDelay);
        }
        else if (finishedMethod == AuthMethod.Morse)
        {
            yield return new WaitForSeconds(morseResultDelay);
        }

        StartNextTrial();
    }


    public void StartNextTrial()
    {
        if (!experimentEnabled || !sessionActive) return;
        StartCoroutine(StartNextTrialCo());
    }

    IEnumerator StartNextTrialCo()
    {
        int nextIndex = GetAttempts(currentMethod) + 1;
        bool isPractice = (nextIndex == 1);

        Debug.Log($"[TrialManager] Starting {currentMethod} trial #{nextIndex} " +
                  $"(practice={isPractice}) for '{currentUser}'");

        // 🔹 Show "Practice" popup BEFORE the method panel opens
        if (isPractice)
        {
            ShowPopup($"{currentMethod} Practice", 1.5f);   // e.g. "Fingerprint Practice"
            yield return new WaitForSeconds(1.5f);
        }

        if (currentMethod == AuthMethod.Fingerprint)
        {
            authUI.OnChooseFingerprint();   // your existing logic
        }
        else
        {
            authUI.OnChooseMorse();         // your existing logic
        }
    }


    public void NotifyMethodStarted(AuthMethod method)
    {
        if (!experimentEnabled || !sessionActive) return;

        // For the very first trial, remember which method really started (FP or Morse)
        if (!firstTrialStarted)
        {
            firstTrialStarted = true;
            currentMethod = method;
            Debug.Log($"[TrialManager] First trial started with {method}");
        }
        else
        {
            // For later trials this is just informational; no change needed.
            Debug.Log($"[TrialManager] Trial started with {method}");
        }
    }

    void ShowPopup(string message, float duration = 2f)
    {
        if (popupPanel == null || popupLabel == null) return;

        if (_popupCo != null)
            StopCoroutine(_popupCo);

        _popupCo = StartCoroutine(PopupRoutine(message, duration));
    }

    IEnumerator PopupRoutine(string message, float duration)
    {
        popupLabel.text = message;
        popupPanel.SetActive(true);

        yield return new WaitForSeconds(duration);

        popupPanel.SetActive(false);
        _popupCo = null;
    }

}
