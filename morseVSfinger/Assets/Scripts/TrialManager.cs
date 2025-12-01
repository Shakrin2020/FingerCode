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

    [Header("Morse Feedback Panels")]
    [SerializeField] GameObject morseMatchPanel;    // green "Pattern Matched!"
    [SerializeField] GameObject morseMismatchPanel; // red "Pattern Mismatched!"

    // Track practice completion
    bool fingerprintPracticeFinished = false;
    bool morsePracticeFinished = false;
    bool practiceDonePopupShown = false;

    float fingerprintTrialStartTime = -1f;
    float morseTrialStartTime = -1f;


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

    const int AttemptsPerMethod = 4;

    bool SessionDone()
    {
        // session is done when *both* methods reached 4 attempts
        return fingerprintAttempts >= AttemptsPerMethod &&
               morseAttempts >= AttemptsPerMethod;
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
    public void OnFingerprintResult(bool success, string errorType = "")
    {
        if (!experimentEnabled || !sessionActive) return;

        IncAttempts(AuthMethod.Fingerprint);
        int attemptIndex = fingerprintAttempts;
        bool isPractice = (attemptIndex == 1);

        float duration = 0f;
        if (fingerprintTrialStartTime > 0f)
            duration = Time.time - fingerprintTrialStartTime;

        int errorFlag = success ? 0 : 1;

        // 🔹 get device user id from AuthUI
        string deviceUserId = authUI != null ? authUI.LastDeviceUserId : "";

        // 🔹 if we didn’t pass any errorType, deduce from success
        if (string.IsNullOrEmpty(errorType))
            errorType = success ? "none" : "mismatch";    // fingerprint has its own messages though

        CSVLogger.Instance?.LogTrial(
            currentUser,
            "fingerprint",
            attemptIndex,
            isPractice,
            success,
            duration,
            errorFlag,
            deviceUserId,
            errorType
        );

        OnAnyTrialFinished(AuthMethod.Fingerprint);
    }

    // Call this AFTER a Morse login attempt completed
    public void OnMorseResult(bool success, string errorType = "")
    {
        if (!experimentEnabled || !sessionActive) return;

        IncAttempts(AuthMethod.Morse);
        int attemptIndex = morseAttempts;
        bool isPractice = (attemptIndex == 1);

        float duration = 0f;
        if (morseTrialStartTime > 0f)
            duration = Time.time - morseTrialStartTime;

        int errorFlag = success ? 0 : 1;

        string deviceUserId = authUI != null ? authUI.LastDeviceUserId : "";

        // 🔹 NEW: fill in default if caller didn’t specify one
        if (string.IsNullOrEmpty(errorType))
            errorType = success ? "none" : "mismatch";

        CSVLogger.Instance?.LogTrial(
            currentUser,
            "morse",
            attemptIndex,
            isPractice,
            success,
            duration,
            errorFlag,
            deviceUserId,
            errorType
        );

        OnAnyTrialFinished(AuthMethod.Morse);
    }

    IEnumerator MorseResultThenNext(float delay)
    {
        // Wait while the "Pattern Matched / Mismatched" panel is visible
        yield return new WaitForSeconds(delay);

        // Now continue with the normal trial scheduling
        OnAnyTrialFinished(AuthMethod.Morse);
    }


    private IEnumerator SessionCompleteRoutine()
    {
        // 🔹 1) Show the popup
        ShowPopup("Task completed", 3f);   // adjust 3f if you want longer/shorter

        // 🔹 2) Wait so the user can read it
        yield return new WaitForSeconds(3f);

        // 🔹 3) Then go back to the login / registration panel
        if (authUI != null)
        {
            authUI.BackToLogin();
        }
    }

    void OnAnyTrialFinished(AuthMethod methodJustFinished)
    {
        Debug.Log(
            $"[TrialManager] Trial finished: {methodJustFinished} | " +
            $"FP={fingerprintAttempts}, MC={morseAttempts}"
        );

        //Check completion FIRST
        if (SessionDone())
        {
            sessionActive = false;
            Debug.Log($"[TrialManager] All 8 attempts finished for '{currentUser}'.");
            StartCoroutine(SessionCompleteRoutine());
            return;
        }

        // Not done yet → pick the other method and start after delay
        currentMethod = (methodJustFinished == AuthMethod.Fingerprint)
            ? AuthMethod.Morse
            : AuthMethod.Fingerprint;

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

        //Show "Practice" popup BEFORE the method panel opens
        if (isPractice)
        {
            ShowPopup($"{currentMethod} Practice", 1.5f);   // e.g. "Fingerprint Practice"
            yield return new WaitForSeconds(1.5f);
        }

        //Mark trial start time *now*, right before the user actually does FP/MC
        if (currentMethod == AuthMethod.Fingerprint)
            fingerprintTrialStartTime = Time.time;
        else
            morseTrialStartTime = Time.time;

        //Now start the actual method (your existing logic)
        if (currentMethod == AuthMethod.Fingerprint)
        {
            authUI.OnChooseFingerprint();   
        }
        else
        {
            authUI.OnChooseMorse();         
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

    void ShowMorseFeedback(bool success, float duration)
    {
        // safety: turn both off first
        if (morseMatchPanel) morseMatchPanel.SetActive(false);
        if (morseMismatchPanel) morseMismatchPanel.SetActive(false);

        GameObject panelToShow = success ? morseMatchPanel : morseMismatchPanel;
        if (panelToShow == null) return;

        StartCoroutine(MorseFeedbackRoutine(panelToShow, duration));
    }

    IEnumerator MorseFeedbackRoutine(GameObject panel, float duration)
    {
        panel.SetActive(true);
        yield return new WaitForSeconds(duration);
        panel.SetActive(false);
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
