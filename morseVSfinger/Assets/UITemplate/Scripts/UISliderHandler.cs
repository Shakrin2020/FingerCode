using UnityEngine;
using UnityEngine.UI;

public class UISliderHandler : MonoBehaviour
{
    [Header("Wires")]
    [SerializeField] private Image leftImage;            // left half (anchors 0..0.5)
    [SerializeField] private Image rightImage;           // right half (anchors 0.5..1)
    [SerializeField] private RhythmControllerV1 rhythmController;

    [Header("Visuals")]
    [SerializeField] private bool useCurve = false;
    [SerializeField] private AnimationCurve sampleCurve;
    [SerializeField] private Color dotColor = new(0.20f, 0.55f, 1f);
    [SerializeField] private Color dashColor = new(0.15f, 0.35f, 1f);

    [Header("Behaviour")]
    [SerializeField] private float releaseSpeed = 6f;   // shrink speed when released

    private float current;   // 0..1

    [Header("Ready Flash")]
    [SerializeField] private float readyFlashDuration = 0.25f;
    [SerializeField] private float readyFlashFillAmount = 0.4f;
    [SerializeField] private Color readyFlashColor = new(0f, 1f, 0.2f, 1f); // green

    private bool isFlashingReady = false;
    private float readyFlashTimer = 0f;

    // track capture state to detect "just started capturing"
    private bool wasCapturing = false;

    private void Awake()
    {
        ConfigureImage(leftImage, true);
        ConfigureImage(rightImage, false);
    }

    private void OnValidate()
    {
        ConfigureImage(leftImage, true);
        ConfigureImage(rightImage, false);
    }

    private void ConfigureImage(Image img, bool isLeft)
    {
        if (!img) return;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = isLeft
            ? (int)Image.OriginHorizontal.Right   // centre → left
            : (int)Image.OriginHorizontal.Left;   // centre → right

        img.fillAmount = 0f;
        img.color = dotColor;
    }

    private void ResetBar()
    {
        if (leftImage)
        {
            leftImage.fillAmount = 0f;
            leftImage.color = dotColor;
        }
        if (rightImage)
        {
            rightImage.fillAmount = 0f;
            rightImage.color = dotColor;
        }
    }

    private void Update()
    {
        if (!rhythmController) return;
        if (!leftImage || !rightImage) return;

        bool isCapturingNow = rhythmController.IsCapturing;

        // Detect the moment capture starts (right after pattern finishes)
        if (isCapturingNow && !wasCapturing)
        {
            // Start a green READY flash
            isFlashingReady = true;
            readyFlashTimer = readyFlashDuration;
        }

        wasCapturing = isCapturingNow;

        // 1) READY FLASH OVERRIDE
        if (isFlashingReady)
        {
            readyFlashTimer -= Time.deltaTime;

            float fill = readyFlashFillAmount;

            leftImage.fillAmount = fill;
            rightImage.fillAmount = fill;

            leftImage.color = readyFlashColor;
            rightImage.color = readyFlashColor;

            if (readyFlashTimer <= 0f)
            {
                isFlashingReady = false;
                ResetBar();
            }

            // skip normal bar logic while flashing
            return;
        }

        // 2) NORMAL BAR (centre-out) based on hold
        float target = rhythmController.IsInputHeld ? rhythmController.HeldValue : 0f;

        if (rhythmController.IsInputHeld)
            current = target;
        else
            current = Mathf.MoveTowards(current, 0f, releaseSpeed * Time.deltaTime);

        float eased = (useCurve && sampleCurve != null) ? sampleCurve.Evaluate(current) : current;
        eased = Mathf.Clamp01(eased);

        leftImage.fillAmount = eased;
        rightImage.fillAmount = eased;

        Color c = Color.Lerp(dotColor, dashColor, eased);
        leftImage.color = c;
        rightImage.color = c;
    }
}
