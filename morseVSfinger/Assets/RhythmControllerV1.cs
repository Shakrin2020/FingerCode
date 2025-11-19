using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class RhythmControllerV1 : MonoBehaviour
{
    [Header("Classification (two-class: short/long)")]
    [SerializeField] bool requireClassMatch = true;
    [SerializeField] float shortLongCut = 0.45f;   // (not used now, kept for inspector)

    [Header("Strictness")]
    [SerializeField] bool strictCountMatch = true;
    [SerializeField] bool penalizeOffByOne = true;

    private int requiredIntervals = 0;
    private bool ignoreUIWhileCapturing = true;
    [SerializeField] bool autoReplayOnFail = false;

    [SerializeField] bool useHoldDurationMode = true;
    private float pressStartTime = -1f;

    [SerializeField] bool perAttemptRandomize = true;
    private int attemptCounter = 0;
    private bool authenticated = false;

    [Header("Timing")]
    public float firstBeatLeadIn = 0.15f;       // UI/music lead-in (after settle)
    [SerializeField] bool startAutomatically = false;

    [Header("Inputs & Refs")]
    public InputActionReference inputDigit = null;           // trigger/tap
    [SerializeField] XRRayInteractor ray;                    // (optional) UI filtering
    [SerializeField] TOTP totp;                              // seed source
    [SerializeField] ActionBasedController hapticController; // XRI haptics

    [Header("Events (reuse your existing bindings)")]
    [SerializeField] private UnityEvent OnMorseCodeCorrect;
    [SerializeField] private UnityEvent OnMorseCodeIncorrect;

    [Header("Rhythm Pattern")]
    [Range(3, 8)] public int beatCount = 4;
    public float[] palette = { 0.20f, 0.60f };   // short/long
    public float interBeatGap = 0.45f;

    [Header("Matching")]
    [Range(0.05f, 0.4f)] public float normalizedMaeTolerance = 0.18f;
    public bool lengthNormalize = true;
    public bool allowOffByOne = true;
    public float captureIdleGap = 0.9f;

    [Header("Haptics")]
    [Range(0, 1)] public float amplitude = 0.7f;

    [Header("Segmentation")]
    [SerializeField] int totalSegments = 4;
    [SerializeField] int currentSegment = 0;

    [System.Serializable] public class IntEvent : UnityEngine.Events.UnityEvent<int> { }
    [SerializeField] IntEvent OnSegmentStart;
    [SerializeField] IntEvent OnSegmentPass;
    [SerializeField] UnityEvent OnAllSegmentsPass;

    [Header("Dot/Dash durations for UI")]
    [SerializeField] float dotSeconds = 0.30f;
    [SerializeField] float dashSeconds = 0.80f;

    [Header("Input Gating")]
    [SerializeField] bool blockInputWhilePlaying = true;

    // ---- Properties used by UISliderHandler ----
    public bool IsInputHeld => capturing && !playing && pressStartTime > 0f;
    public float HeldSeconds => IsInputHeld ? (Time.time - pressStartTime) : 0f;
    public float HeldValue => Mathf.Clamp01(HeldSeconds / Mathf.Max(0.0001f, dashSeconds));
    public float DotThresholdNorm => Mathf.Clamp01(dotSeconds / Mathf.Max(0.0001f, dashSeconds));

    // NEW: for the UI to know when capture window is active
    public bool IsCapturing => capturing;

    // ---- internal state ----
    private readonly List<List<float>> targets = new();  // per-segment targets
    private readonly List<float> target = new();
    private readonly List<float> userIntervals = new();

    private float lastTap = -1f;
    private bool capturing = false, playing = false;
    private int accentIndex = -1;
    private bool _inPhaseTransition = false;

    // remember if ANY segment mismatched in the current attempt
    private bool hadAnyMismatchThisAttempt = false;

    // Tracked coroutines
    private Coroutine _playbackCo;
    private Coroutine _phaseTickCo;

    // ========= Unity lifecycle =========

    void Awake()
    {
        if (inputDigit != null)
        {
            inputDigit.action.started += OnPress;
            inputDigit.action.canceled += OnRelease;
        }
    }

    void OnDestroy()
    {
        if (inputDigit != null)
        {
            inputDigit.action.started -= OnPress;
            inputDigit.action.canceled -= OnRelease;
        }
    }

    void OnEnable()
    {
        if (inputDigit != null) inputDigit.action.Enable();
    }

    void OnDisable()
    {
        Debug.Log("[Rhythm] OnDisable()");
        if (inputDigit != null) inputDigit.action.Disable();
        capturing = false;
        playing = false;
        pressStartTime = -1f;
        StopAndSilence(StopReason.OnDisable);
    }

    void Update()
    {
        if (!capturing) return;

        // backup end condition if the user stops early
        if (lastTap > 0 && Time.time - lastTap >= captureIdleGap)
        {
            capturing = false;
            Evaluate();
        }
    }

    // ========= Classification =========

    // Dot/long classification: "closer to dotSeconds or dashSeconds?"
    private char Classify(float v)
    {
        float diffDot = Mathf.Abs(v - dotSeconds);
        float diffDash = Mathf.Abs(v - dashSeconds);
        return (diffDot <= diffDash) ? 'S' : 'L';
    }

    // ========= FLOW / PUBLIC ENTRY POINTS =========

    private IEnumerator BeginOtp()
    {
        GeneratePatternFromTotp();
        StopPlayback();
        // play pattern then capture
        yield return StartCoroutine(PlayOnce());
        StartCapture();
    }

    private void StartCapture()
    {
        userIntervals.Clear();
        lastTap = -1f;
        capturing = true;
        pressStartTime = -1f;
    }

    // Call from UI to restart from first phase
    public void StartFromPhase0()
    {
        if (!gameObject.activeInHierarchy || !isActiveAndEnabled)
        {
            Debug.LogWarning("[Rhythm] StartFromPhase0 called while AuthenticationController is inactive.");
            return;
        }

        StopAndSilence();
        authenticated = false;
        attemptCounter++;
        hadAnyMismatchThisAttempt = false;
        currentSegment = 0;

        // OLD: StartSegment(currentSegment);
        StartSegmentSafe(currentSegment);       
    }


    // ========= GENERATION & PLAYBACK =========

    void BuildTargetsIfNeeded()
    {
        // If we don't want per-attempt randomization, reuse existing targets once built
        if (!perAttemptRandomize && targets.Count == totalSegments)
            return;

        // For per-attempt randomization, always rebuild
        targets.Clear();

        // Base seed from TOTP, mixed with attemptCounter so each new attempt differs
        int baseSeed = (totp != null) ? totp.GetCurrentSeed32() : 123456789;
        baseSeed ^= attemptCounter * 92837111;

        for (int seg = 0; seg < totalSegments; seg++)
        {
            int nonce = (attemptCounter * 73856093) ^ (seg * 19349663);
            var rnd = new System.Random(baseSeed ^ nonce);

            var list = new List<float>(beatCount);
            for (int i = 0; i < beatCount; i++)
            {
                int pick = rnd.Next(palette.Length);
                float dur = palette[pick];
                float jitter = (float)(rnd.NextDouble() * 0.04 - 0.02);
                list.Add(Mathf.Max(0.12f, dur + jitter));
            }
            targets.Add(list);
        }
    }


    private enum StopReason
    {
        Manual, OnDisable, IdleTimeout, EvaluateFail, EmptyInput, CountMismatch, ClassMismatch,
        PhasePass, AllPass, Unknown
    }

    private void GeneratePatternFromTotp()
    {
        BuildTargetsIfNeeded();

        if (currentSegment < 0 || currentSegment >= totalSegments)
            currentSegment = 0;

        target.Clear();
        target.AddRange(targets[currentSegment]);

        requiredIntervals = useHoldDurationMode ? target.Count : target.Count - 1;

        OnSegmentStart?.Invoke(currentSegment);
        accentIndex = Mathf.Clamp(accentIndex, 0, target.Count - 1);
    }

    private IEnumerator PlayOnce()
    {
        if (playing) yield break;
        playing = true;

        yield return new WaitForSeconds(0.03f);         // settle
        yield return new WaitForSeconds(firstBeatLeadIn);

        for (int i = 0; i < target.Count; i++)
        {
            float amp = Mathf.Clamp01(amplitude + (i == accentIndex ? 0.15f : 0f));
            SafePulse(target[i], amp);
            yield return new WaitForSeconds(target[i]);
            yield return new WaitForSeconds(interBeatGap);
        }
        playing = false;
        _playbackCo = null;
    }

    // Start button moves to phases UI and kicks off Phase 0
    public void StartOtp()
    {
        StopPhaseTick();
        StopPlayback();

        capturing = false;
        playing = false;
        pressStartTime = -1f;

        authenticated = false;
        attemptCounter++;
        hadAnyMismatchThisAttempt = false;

        if (currentSegment >= totalSegments) currentSegment = 0;
        StartCoroutine(StartSegment(currentSegment));
    }

    public void VibratePhaseStart(int seg)
    {
        if (seg <= 0) return;
        StartCoroutine(_PhaseStartBuzz());
    }

    private IEnumerator _PhaseStartBuzz()
    {
        yield return null; // 1 frame
        SafePulse(0.25f, amplitude);
    }

    private void SafePulse(float duration, float amp)
    {
        if (hapticController == null) return;

        if (!hapticController.isActiveAndEnabled)
            hapticController.gameObject.SetActive(true);

        hapticController.SendHapticImpulse(Mathf.Clamp01(amp), duration);
    }

    private IEnumerator StartSegment(int seg)
    {
        _inPhaseTransition = true;

        BuildTargetsIfNeeded();

        target.Clear();
        target.AddRange(targets[seg]);
        requiredIntervals = useHoldDurationMode ? target.Count : target.Count - 1;

        Debug.Log($"[Rhythm] >>> OnSegmentStart seg={seg} (Phase {seg + 1})");
        OnSegmentStart?.Invoke(seg);

        capturing = false;

        // Play the pattern (left controller haptics)
        yield return StartCoroutine(PlayOnce());

        // Immediately after pattern finishes, we will start capture.
        // UISliderHandler will detect capturing == true and flash green.
        StartCapture();

        yield return new WaitForSeconds(0.08f);
        _inPhaseTransition = false;
    }

    // ========= INPUT HANDLING =========

    // Wrapper so old call sites still compile
    private void RegisterTap(float now) => RegisterTapOnset(now);

    private void RegisterTapOnset(float now)
    {
        if (!capturing) return;
        if (blockInputWhilePlaying && playing) return;

        if (lastTap > 0f)
        {
            userIntervals.Add(now - lastTap);
            if (userIntervals.Count >= requiredIntervals)
            {
                capturing = false;
                Evaluate();
                return;
            }
        }
        lastTap = now;
        SafePulse(0.04f, 0.4f); // tiny ack tick
    }

    private void BeginHold()
    {
        if (!capturing) return;
        if (blockInputWhilePlaying && playing) return;

        pressStartTime = Time.time;
        SafePulse(0.04f, 0.4f); // tiny ack tick

        if (!useHoldDurationMode)
        {
            // tap-based mode: record onset immediately
            RegisterTapOnset(pressStartTime);
        }
    }

    private void EndHold()
    {
        if (!capturing || pressStartTime < 0f) return;
        if (blockInputWhilePlaying && playing) return;

        if (useHoldDurationMode)
        {
            // How long this press actually lasted
            float rawHold = Time.time - pressStartTime;

            // Log it so you can see your dot/dash timing
            Debug.Log(
                $"[Rhythm] Hold seg={currentSegment} index={userIntervals.Count} " +
                $"raw={rawHold:F3}s  (dotSeconds={dotSeconds:F3}, dashSeconds={dashSeconds:F3})"
            );

            // Clamp extremes
            float hold = Mathf.Clamp(rawHold, 0.05f, 1.50f);
            userIntervals.Add(hold);

            if (userIntervals.Count >= requiredIntervals)
            {
                capturing = false;
                Evaluate();
            }
        }

        pressStartTime = -1f;
    }

    // called by Input System (controller trigger etc.)
    private void OnPress(InputAction.CallbackContext ctx)
    {
        BeginHold();
    }

    private void OnRelease(InputAction.CallbackContext ctx)
    {
        EndHold();
    }

    // ========= EVALUATION =========

    private void Evaluate()
    {
        var a = new List<float>(target);
        var b = new List<float>(userIntervals);

        bool segmentPass = true;   // optimistic, prove otherwise

        if (b.Count == 0)
        {
            segmentPass = false;
        }
        else
        {
            int expected = requiredIntervals;
            int diff = Mathf.Abs(b.Count - expected);

            if (strictCountMatch)
            {
                if (diff != 0) segmentPass = false;
            }
            else if (diff > 1)
            {
                segmentPass = false;
            }

            if (segmentPass && requireClassMatch)
            {
                var tgt = useHoldDurationMode ? a : a.Take(a.Count - 1).ToList();
                var usr = b;

                string tgtCls = string.Concat(tgt.Select(Classify));
                string usrCls = string.Concat(usr.Select(Classify));
                if (!tgtCls.Equals(usrCls)) segmentPass = false;
            }

            if (segmentPass)
            {
                if (lengthNormalize) { Normalize(a); Normalize(b); }
                float mae = Mae(a, b);
                if (!strictCountMatch && diff == 1 && penalizeOffByOne) mae += 0.12f;
                segmentPass = mae <= normalizedMaeTolerance;
            }
        }

        if (segmentPass)
        {
            OnSegmentPass?.Invoke(currentSegment);
        }
        else
        {
            hadAnyMismatchThisAttempt = true;
        }

        // Always advance to next segment
        currentSegment++;
        if (currentSegment < totalSegments)
        {
            StartSegmentSafe(currentSegment);
        }
        else
        {
            bool success = !hadAnyMismatchThisAttempt;
            FinishAttempt(success);
        }
    }

    private void FinishAttempt(bool success)
    {
        if (success)
        {
            authenticated = true;
            StopAndSilence(StopReason.AllPass);
            OnAllSegmentsPass?.Invoke();
            OnMorseCodeCorrect?.Invoke();
        }
        else
        {
            authenticated = false;
            StopAndSilence(StopReason.EvaluateFail);
            OnMorseCodeIncorrect?.Invoke();
            if (autoReplayOnFail)
            {
                currentSegment = 0;
                hadAnyMismatchThisAttempt = false;
                StartSegmentSafe(currentSegment);
            }
        }
    }

    // ========= UTILITIES =========

    private static void Normalize(List<float> s)
    {
        float sum = Mathf.Max(s.Sum(), 1e-6f);
        for (int i = 0; i < s.Count; i++) s[i] /= sum;
    }

    private static float Mae(List<float> x, List<float> y)
    {
        int n = Mathf.Min(x.Count, y.Count);
        if (n == 0) return 1f;
        float mae = 0f;
        for (int i = 0; i < n; i++) mae += Mathf.Abs(x[i] - y[i]);
        mae /= n;
        mae += 0.06f * Mathf.Abs(x.Count - y.Count);
        return mae;
    }

    private void StartSegmentSafe(int seg)
    {
        // If this object (or any of its parents) is inactive, don't start coroutines
        if (!gameObject.activeInHierarchy || !isActiveAndEnabled)
        {
            Debug.LogWarning($"[Rhythm] Ignoring StartSegment({seg}) because object is inactive.");
            return;
        }

        StartCoroutine(StartSegment(seg));
    }

    private void StopPlayback()
    {
        if (_playbackCo != null)
        {
            StopCoroutine(_playbackCo);
            _playbackCo = null;
        }
        playing = false;
    }

    private void StopPhaseTick()
    {
        if (_phaseTickCo != null)
        {
            StopCoroutine(_phaseTickCo);
            _phaseTickCo = null;
        }
    }

    private void StopAndSilence(StopReason reason = StopReason.Unknown)
    {
        if (_inPhaseTransition)
        {
            Debug.Log($"[Rhythm] StopAndSilence() IGNORED (in transition) reason={reason}");
            return;
        }

        StopAllCoroutines();
        capturing = false;
        playing = false;
        pressStartTime = -1f;
        hapticController?.SendHapticImpulse(0f, 0.001f);
    }
}
