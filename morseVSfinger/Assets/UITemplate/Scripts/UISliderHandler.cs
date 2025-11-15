using UnityEngine;
using UnityEngine.UI;

public class UISliderHandler : MonoBehaviour
{
    [Header("Wires")]
    [SerializeField] private Image leftImage;            // left half (anchors 0..0.5)
    [SerializeField] private Image rightImage;           // right half (anchors 0.5..1)
    [SerializeField] private RhythmControllerV1 morseController;

    [Header("Visuals")]
    [SerializeField] private bool useCurve = false;
    [SerializeField] private AnimationCurve sampleCurve;
    [SerializeField] private Color dotColor = new(0.20f, 0.55f, 1f);
    [SerializeField] private Color dashColor = new(0.15f, 0.35f, 1f);
    [SerializeField] private RectTransform dotTick;

    [Header("Behaviour")]
    [SerializeField] private float releaseSpeed = 6f;   // shrink speed when released

    // Tick fixed in the middle (50%)
    private const float kFixedDotPos = 0.5f;

    private float current;   // 0..1

    private void Awake()
    {
        ConfigureImage(leftImage, isLeft: true);
        ConfigureImage(rightImage, isLeft: false);
        PositionDotTickAtMiddle();
    }

    private void OnEnable()
    {
        current = 0f;
        if (leftImage) leftImage.fillAmount = 0f;
        if (rightImage) rightImage.fillAmount = 0f;
    }

    private void Update()
    {
        if (!morseController) return;

        // 0..1 based on hold time / dashSeconds
        float target = morseController.IsInputHeld ? morseController.HeldValue : 0f;

        // Snap while holding, smooth only on release
        if (morseController.IsInputHeld)
            current = target;
        else
            current = Mathf.MoveTowards(current, 0f, releaseSpeed * Time.deltaTime);

        float eased = (useCurve && sampleCurve != null) ? sampleCurve.Evaluate(current) : current;
        eased = Mathf.Clamp01(eased);

        // Same fill amount on both halves → grows from center outward
        if (leftImage) leftImage.fillAmount = eased;
        if (rightImage) rightImage.fillAmount = eased;

        // Optional color gradient (dot → dash)
        Color c = Color.Lerp(dotColor, dashColor, eased);
        if (leftImage) leftImage.color = c;
        if (rightImage) rightImage.color = c;
    }

    private void ConfigureImage(Image img, bool isLeft)
    {
        if (!img) return;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;

        // Left half should fill from the center towards the LEFT → origin = Right
        // Right half should fill from the center towards the RIGHT → origin = Left
        img.fillOrigin = isLeft
            ? (int)Image.OriginHorizontal.Right   // center → left
            : (int)Image.OriginHorizontal.Left;   // center → right;

        img.fillAmount = 0f;
    }

    private void PositionDotTickAtMiddle()
    {
        if (!dotTick) return;

        var min = dotTick.anchorMin; min.x = kFixedDotPos; dotTick.anchorMin = min;
        var max = dotTick.anchorMax; max.x = kFixedDotPos; dotTick.anchorMax = max;
    }
}
