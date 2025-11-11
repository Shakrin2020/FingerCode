using UnityEngine;
using UnityEngine.UI;

/// Attach to the BLUE fill image (child of the rounded black box).
public class UISliderHandler : MonoBehaviour
{
    [Header("Wires")]
    [SerializeField] Image image;                        // the blue fill image
    [SerializeField] RhythmControllerV1 morseController; // provides IsInputHeld, HeldValue [0..1]

    [Header("Visuals")]
    [SerializeField] bool useCurve = false;           // <-- new toggle
    [SerializeField] AnimationCurve sampleCurve = null;
    [SerializeField] Color dotColor = new(0.20f, 0.55f, 1f);
    [SerializeField] Color dashColor = new(0.15f, 0.35f, 1f);
    [SerializeField] RectTransform dotTick;


    [Header("Behaviour")]
    [Range(0f, 1f)] [SerializeField] float startFillValue = 0f;  // keep 0 for true-from-zero
    [SerializeField] float growSpeed = 10f;                    // while holding
    [SerializeField] float releaseSpeed = 6f;                     // after release
    [SerializeField] bool autoConfigureImage = true;


    // Force the tick to the middle (50%)
    const float kFixedDotPos = 0.5f;

    float current;   // smoothed fill [0..1]

    void Reset() { image = GetComponent<Image>(); }
    void OnValidate() { EnsureImageSetup(); }
    void Awake() { EnsureImageSetup(); }

    void OnEnable()
    {
        current = 0f;
        if (image) image.fillAmount = 0f;
        PositionDotTickAtMiddle();
    }

    void Update()
    {
        if (!image || !morseController) return;

        // Target: 0..1 (0.5 ~ dot, 1.0 ~ dash)
        float target = morseController.IsInputHeld ? morseController.HeldValue : 0f;

        // SNAP while the user is holding (no growth smoothing),
        // only smooth on release so it feels responsive for short taps.
        if (morseController.IsInputHeld)
            current = target;
        else
            current = Mathf.MoveTowards(current, 0f, releaseSpeed * Time.deltaTime);

        // Optional easing (disable with useCurve=false)
        float eased = (useCurve && sampleCurve != null) ? sampleCurve.Evaluate(current) : current;

        image.fillAmount = Mathf.Clamp01(startFillValue + eased);
        image.color = Color.Lerp(dotColor, dashColor, eased);
    }


    void EnsureImageSetup()
    {
        if (!image || !autoConfigureImage) return;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left; // Left → Right
        image.fillAmount = 0f;
    }

    void PositionDotTickAtMiddle()
    {
        if (!dotTick) return;

        // Expect dotTick to be a child of the bar. We place it via anchors at 50%.
        var min = dotTick.anchorMin; min.x = kFixedDotPos; dotTick.anchorMin = min;
        var max = dotTick.anchorMax; max.x = kFixedDotPos; dotTick.anchorMax = max;
    }
}
