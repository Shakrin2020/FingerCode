using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Attach to UIController
public class AuthUIController : MonoBehaviour
{
    [Header("UserSelection panel (UserSelection)")]
    [SerializeField] GameObject userSelectionPanel;           // UIController/UserSelection
    [SerializeField] Button register;                         // Reg (Button)

    [Header("Login panel (LogReg)")]
    [SerializeField] GameObject loginPanel;                   // UIController/LogReg
    [SerializeField] TMP_InputField userNameField;            // LogReg/InputField (TMP)
    [SerializeField] Button registerButton;                   // LogReg/Reg (Button)
    [SerializeField] Button loginButton;                      // LogReg/Login (Button)

    [Header("Auth method panel (AuthMethod)")]
    [SerializeField] GameObject authMethodPanel;              // panel with "Fingerprint" / "Morse Code"
    [SerializeField] Button fingerprintMethodButton;          // green "Fingerprint" button
    [SerializeField] Button morseMethodButton;                // green "Morse Code" button

    [Header("Fingerprint prompt panel (RegFingerPrint)")]
    [SerializeField] GameObject fingerprintPanel;             // UIController/RegFingerPrint
    [SerializeField] TMP_Text fingerprintText;                // RegFingerPrint/Text (TMP)
    [SerializeField] Button okButton;
    [SerializeField] TMP_Text okButtonLabel;

    [Header("OTP panel (Morse UI)")]
    [SerializeField] GameObject otpPanel;

    bool _busy;
    bool _subscribed;

    enum Flow { None, Register, Login }
    Flow _flow = Flow.None;

    // who is currently registering / logging in
    string _currentUserName;
    bool _waitingForExistCheck;

    string _pendingRegisterName;


    void Awake()
    {
        _flow = Flow.None;

        registerButton.onClick.RemoveAllListeners();
        loginButton.onClick.RemoveAllListeners();
        registerButton.onClick.AddListener(OnPressRegister);
        loginButton.onClick.AddListener(OnPressLogin);

        if (fingerprintMethodButton)
        {
            fingerprintMethodButton.onClick.RemoveAllListeners();
            fingerprintMethodButton.onClick.AddListener(OnChooseFingerprint);
        }

        if (morseMethodButton)
        {
            morseMethodButton.onClick.RemoveAllListeners();
            morseMethodButton.onClick.AddListener(OnChooseMorse);
        }

        // default: OK sends Button-A (device prompt will say when to press)
        okButton.onClick.RemoveAllListeners();
        okButton.onClick.AddListener(() => FingerprintWsClient.I?.PressA());
        if (okButtonLabel) okButtonLabel.text = "OK";

        // start in login view, hide auth-method & fingerprint panels
        SetPanels(true, "");
        if (authMethodPanel) authMethodPanel.SetActive(false);
        if (otpPanel) otpPanel.SetActive(false);

        EnsureWsSubscriptions();
    }


    void OnEnable()
    {
        var ws = FingerprintWsClient.I;
        if (ws == null) return;

        // Make sure we don't double-subscribe
        ws.OnEnrollSample -= HandleEnrollSample;
        ws.OnDeviceMessage -= HandleDeviceMsg;

        ws.OnEnrollSample += HandleEnrollSample;
        ws.OnDeviceMessage += HandleDeviceMsg;
        _subscribed = true;
    }

    void OnDisable()
    {
        var ws = FingerprintWsClient.I;
        if (ws == null) return;

        ws.OnEnrollSample -= HandleEnrollSample;
        ws.OnDeviceMessage -= HandleDeviceMsg;
        _subscribed = false;
    }

    void OnDestroy()
    {
        var ws = FingerprintWsClient.I;
        if (ws != null && _subscribed)
        {
            ws.OnEnrollSample -= HandleEnrollSample;
            ws.OnDeviceMessage -= HandleDeviceMsg;
        }
        _subscribed = false;
    }

    void EnsureWsSubscriptions()
    {
        var ws = FingerprintWsClient.I;
        if (ws == null || _subscribed) return;

        ws.OnEnrollSample += HandleEnrollSample;
        ws.OnDeviceMessage += HandleDeviceMsg;
        _subscribed = true;
    }

    // ===== Buttons (manual name typed) =====

    public void OnPressRegister()
    {
        if (_busy) return;

        var name = userNameField?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            SetPanels(true, "Please enter your name first.");
            return;
        }

        _pendingRegisterName = name;
        _flow = Flow.Register;

        SetPanels(false, $"Hi {name},\nPlease place your fingerprint to REGISTER.\n\nPress OK to start.");

        if (okButton)
        {
            okButton.onClick.RemoveAllListeners();
            okButton.onClick.AddListener(OnConfirmRegisterOk);
            if (okButtonLabel) okButtonLabel.text = "OK";
            okButton.interactable = true;
        }
    }

    // LOGIN BUTTON: send exists:<name> to ESP, then wait for reply
    public void OnPressLogin()
    {
        if (_waitingForExistCheck) return;  // avoid double-click spam

        var name = userNameField?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            SetPanels(true, "Please enter your name first.");
            return;
        }

        var ws = FingerprintWsClient.I;
        if (ws == null)
        {
            SetPanels(true, "WS client not found in scene.");
            return;
        }

        EnsureWsSubscriptions();

        _currentUserName = name;
        _waitingForExistCheck = true;

        SetPanels(true, "Checking user on device...");
        ws.SendExists(name);   // your helper in FingerprintWsClient
    }


    public void OnConfirmRegisterOk()
    {
        if (_busy) return;
        if (string.IsNullOrEmpty(_pendingRegisterName))
        {
            SetPanels(true, "Please enter your name first.");
            return;
        }

        // After we start enroll, we want the OK button to be controlled by the device again
        DisableOk();             // will be re-enabled by "press a" messages
        _ = RegisterFlow(_pendingRegisterName);
    }


    // show the "Select Authentication Method" panel
    void ShowAuthMethodPanel()
    {
        if (loginPanel) loginPanel.SetActive(false);
        if (fingerprintPanel) fingerprintPanel.SetActive(false);
        if (authMethodPanel) authMethodPanel.SetActive(true);
        if (otpPanel) otpPanel.SetActive(false);

        if (okButton)
        {
            okButton.gameObject.SetActive(false); // no OK button on method panel
        }
    }

    async Task RegisterFlow(string name)
    {
        _flow = Flow.Register;
        if (_busy) return;
        _busy = true;
        try
        {
            EnsureWsSubscriptions();

            var ws = FingerprintWsClient.I;
            if (ws == null)
            {
                SetPanels(false, "WS client not found in scene.");
                ShowOkBack();
                return;
            }

            if (!await ws.EnsureConnectedAsync())
            {
                SetPanels(false, "Device not found.");
                ShowOkBack();
                return;
            }

            ws.StartEnroll(name);    // actually tells the ESP to start enrolling
        }
        finally { _busy = false; }
    }


    // called from the Fingerprint button on the method panel
    public async void OnChooseFingerprint()
    {
        if (_busy) return;
        if (authMethodPanel) authMethodPanel.SetActive(false);

        await LoginFlow();
    }

    // called from the Morse Code button on the method panel
    // called from the Morse Code button on the method panel
    public void OnChooseMorse()
    {
        if (_busy || _waitingForExistCheck) return;

        // hide the method-choice panel
        if (authMethodPanel) authMethodPanel.SetActive(false);

        // hide fingerprint prompt (if it was ever shown)
        if (fingerprintPanel) fingerprintPanel.SetActive(false);

        // show the OTP / Morse UI panel
        if (otpPanel) otpPanel.SetActive(true);

        Debug.Log($"[AuthUI] Start Morse-code login for user '{_currentUserName}'");

        // TODO: kick off your existing Morse/OTP logic here
        // e.g. if you have a controller script:
        // MorseOtpController.Instance.BeginLogin(_currentUserName);
    }


    async Task LoginFlow()
    {
        _flow = Flow.Login;
        if (_busy) return;
        _busy = true;
        try
        {
            EnsureWsSubscriptions();
            var ws = FingerprintWsClient.I;
            if (ws == null) { SetPanels(false, "WS client not found in scene."); return; }

            // prefer the stored name (so we can hide the input field)
            var name = string.IsNullOrEmpty(_currentUserName)
                ? (userNameField?.text?.Trim() ?? "")
                : _currentUserName;

            if (string.IsNullOrEmpty(name))
            {
                SetPanels(true, "Please enter your name first.");
                return;
            }

            SetPanels(false, "Please place your fingerprint to LOGIN.");

            if (!await ws.EnsureConnectedAsync()) { SetPanels(false, "Device not found."); return; }

            ws.StartVerify(name);
        }
        finally { _busy = false; }
    }

    // ===== Public entry points for the User List =====

    public async void BeginVerifyForSelectedUser(string username, string displayName)
    {
        _flow = Flow.Login;

        _currentUserName = username;

        // reflect the selection into the input field (nice for consistency)
        if (userNameField) userNameField.text = username ?? "";

        SetPanels(false, $"User: {displayName ?? username}\nPlease place your fingerprint to LOGIN.");

        var ws = FingerprintWsClient.I;
        if (ws == null) { SetPanels(false, "WS client not found in scene."); return; }

        EnsureWsSubscriptions();

        if (!await ws.EnsureConnectedAsync())
        {
            SetPanels(false, "Device not found.");
            return;
        }

        ws.StartVerify(username);
    }

    public void GoToFingerprintPrompt(string displayName)
    {
        _flow = Flow.Login;
        SetPanels(false, $"User: {displayName}\nPlace your finger on the sensor…");
        DisableOk(); // will be enabled when device tells to press A
    }

    // ===== OK button modes =====
    void ShowOkPressA()
    {
        if (!okButton) return;
        okButton.onClick.RemoveAllListeners();
        okButton.onClick.AddListener(() => FingerprintWsClient.I?.PressA());
        if (okButtonLabel) okButtonLabel.text = "OK";
        okButton.interactable = true;
    }

    void ShowOkBack()
    {
        if (!okButton) return;
        okButton.onClick.RemoveAllListeners();
        okButton.onClick.AddListener(BackToLogin);
        if (okButtonLabel) okButtonLabel.text = "Back";
        okButton.interactable = true;
    }

    void DisableOk()
    {
        if (!okButton) return;
        okButton.interactable = false;
    }

    // ===== Incoming messages =====
    void HandleEnrollSample(int step, string pretty)
    {
        if (fingerprintPanel && fingerprintPanel.activeInHierarchy && fingerprintText)
            fingerprintText.text = pretty;

        var p = (pretty ?? "").ToLowerInvariant();

        // Unknown user
        if (p.Contains("user not found"))
        {
            if (fingerprintText) fingerprintText.text = "User not found. Please register first.";
            ShowOkBack();
            return;
        }

        if (p.Contains("user exist"))
        {
            ShowOkBack();
            return;
        }

        if (p.Contains("press a") || p.StartsWith("start s"))
        {
            ShowOkPressA();
            return;
        }

        if (step >= 6 || p.Contains("registration done") || p.Contains("verified"))
        {
            ShowOkBack();
            return;
        }

        DisableOk();
    }

    void HandleDeviceMsg(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;

        // ignore JSON/status lines
        if (msg.Length > 0 && (msg[0] == '{' || msg.Contains("sensorReady") || msg.Contains("\"op\"")))
            return;

        var p = msg.ToLowerInvariant();
        Debug.Log($"[AuthUI] DeviceMsg: {p}");

        // ----- 1) Reply to exists:<name> check -----
        if (_waitingForExistCheck)
        {
            _waitingForExistCheck = false;

            if (p.Contains("user not found"))
            {
                // Show error on the fingerprint panel (same style as other messages)
                SetPanels(false, "User not found. Please register first.");
                ShowOkBack();   // if you want the Back button
                return;
            }

            if (p.Contains("user exist"))
            {
                ShowAuthMethodPanel();
                return;
            }
        }

        // ----- 2) Normal fingerprint flow (unchanged) -----
        if (_flow == Flow.None || !fingerprintPanel || !fingerprintPanel.activeInHierarchy)
            return;

        if (p.Contains("user not found"))
        {
            if (fingerprintText) fingerprintText.text = "User not found. Please register first.";
            ShowOkBack();
            return;
        }

        // general device messages during FP flow
        if (fingerprintText) fingerprintText.text = msg;
    }

    public void BackToLogin()
    {
        _flow = Flow.None;
        _currentUserName = null;
        _waitingForExistCheck = false;

        if (authMethodPanel) authMethodPanel.SetActive(false);
        SetPanels(true, "");
        if (userNameField) userNameField.text = "";
    }


    // ---------- UI helper ----------
    // showLogin = true → login panel
    // showLogin = false → fingerprint prompt panel
    void SetPanels(bool showLogin, string message)
    {
        if (loginPanel) loginPanel.SetActive(showLogin);
        if (fingerprintPanel) fingerprintPanel.SetActive(!showLogin);
        if (fingerprintText) fingerprintText.text = message ?? "";

        if (okButton)
        {
            okButton.gameObject.SetActive(!showLogin);
            okButton.interactable = false;     // disabled on initial message
            if (okButtonLabel) okButtonLabel.text = "OK";
        }
    }
}
