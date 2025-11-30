using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Attach to UIController
public class AuthUIController : MonoBehaviour
{
    [SerializeField] RhythmControllerV1 rhythmController;   // for Morse timer control

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

    [Header("Fingerprint timer")]
    [SerializeField] TMP_Text timerText;                      // NEW: RegFingerPrint/Timer (TMP)

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
    bool _verifyInProgressUI = false;

    public string LastDeviceUserId { get; private set; } = "";

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
        ws.OnEnrollTimer -= HandleTimer;   // NEW
        ws.OnVerifyTimer -= HandleTimer;   // NEW

        ws.OnEnrollSample += HandleEnrollSample;
        ws.OnDeviceMessage += HandleDeviceMsg;
        ws.OnEnrollTimer += HandleTimer;   // NEW
        ws.OnVerifyTimer += HandleTimer;   // NEW
        _subscribed = true;
    }

    void OnDisable()
    {
        var ws = FingerprintWsClient.I;
        if (ws == null) return;

        ws.OnEnrollSample -= HandleEnrollSample;
        ws.OnDeviceMessage -= HandleDeviceMsg;
        ws.OnEnrollTimer -= HandleTimer;   // NEW
        ws.OnVerifyTimer -= HandleTimer;   // NEW
        _subscribed = false;
    }

    void OnDestroy()
    {
        var ws = FingerprintWsClient.I;
        if (ws != null && _subscribed)
        {
            ws.OnEnrollSample -= HandleEnrollSample;
            ws.OnDeviceMessage -= HandleDeviceMsg;
            ws.OnEnrollTimer -= HandleTimer;   // NEW
            ws.OnVerifyTimer -= HandleTimer;   // NEW
        }
        _subscribed = false;
    }

    void EnsureWsSubscriptions()
    {
        var ws = FingerprintWsClient.I;
        if (ws == null || _subscribed) return;

        ws.OnEnrollSample += HandleEnrollSample;
        ws.OnDeviceMessage += HandleDeviceMsg;
        ws.OnEnrollTimer += HandleTimer;   // NEW
        ws.OnVerifyTimer += HandleTimer;   // NEW
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

        // Store the name, but DO NOT start fingerprint yet
        _pendingRegisterName = name;
        _currentUserName = name;
        _flow = Flow.Register;

        // Go to "Choose authentication method" screen
        SetPanels(true, "");        // reset to login-style base UI
        ShowAuthMethodPanel();      // this hides login panel and shows AuthMethod panel
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
        if (rhythmController) rhythmController.StopMorseTimer();

        if (TrialManager.Instance != null &&
            TrialManager.Instance.OnUserClickedMethod(AuthMethod.Fingerprint))
            return;

        if (_busy) return;

        if (authMethodPanel) authMethodPanel.SetActive(false);

        if (_flow == Flow.Register)
        {
            // REGISTER FLOW: enroll fingerprint for the name we just entered
            if (string.IsNullOrEmpty(_pendingRegisterName))
            {
                SetPanels(true, "Please enter your name first.");
                return;
            }

            var name = _pendingRegisterName;

            // Show the fingerprint panel *right now* with a friendly message
            SetPanels(false, $"Hi {name},\nPlace your finger on the sensor to REGISTER.");
            //DisableOk();  // OK will be enabled by "start s1 / press a" messages

            // Now actually tell the ESP to start enrolling
            await RegisterFlow(name);
        }
        else
        {
            // LOGIN FLOW (unchanged): fingerprint login
            await LoginFlow();
        }

        // Tell TrialManager that a fingerprint trial has started (for LOGIN only).
        if (_flow == Flow.Login)
        {
            TrialManager.Instance?.NotifyMethodStarted(AuthMethod.Fingerprint);
        }
    }



    // called from the Morse Code button on the method panel
    public void OnChooseMorse()
    {
        if (TrialManager.Instance != null &&
            TrialManager.Instance.OnUserClickedMethod(AuthMethod.Morse))
            return;


        if (_busy || _waitingForExistCheck) return;

        // hide the method-choice panel
        if (authMethodPanel) authMethodPanel.SetActive(false);

        // hide fingerprint prompt (if it was ever shown)
        if (fingerprintPanel) fingerprintPanel.SetActive(false);

        // show the OTP / Morse UI panel
        if (otpPanel) otpPanel.SetActive(true);

        Debug.Log($"[AuthUI] Start Morse-code login for user '{_currentUserName}'");

        if (_flow == Flow.Login)
        {
            TrialManager.Instance?.NotifyMethodStarted(AuthMethod.Morse);
        }
    }


    async Task LoginFlow()
    {
        _flow = Flow.Login;
        if (_busy || _verifyInProgressUI) return;

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

            _verifyInProgressUI = true;
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
        _verifyInProgressUI = true;
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

        /*if (p.Contains("sample") && p.Contains("saved"))
        {
            if (timerText) timerText.text = "";   // clear old countdown
                                                  // you can choose to enable OK here if you want:
            ShowOkPressA();                                   
            return;
        }*/

        if (p.Contains("registration failed") || p.Contains("timeout"))
        {
            if (fingerprintText)
                fingerprintText.text = "Registration failed (timeout). Please try again.";

            if (timerText) timerText.text = "";   // clear "Timer: 2s left"
            ShowOkBack();                         // change OK → Back
            _verifyInProgressUI = false;
            return;
        }


        if (step >= 6 || p.Contains("registration done") || p.Contains("verified"))
        {
            ShowOkBack();
            _verifyInProgressUI = false;
            if (timerText) timerText.text = "";
            return;
        }

        //DisableOk();
    }

    // NEW: timer handler – just updates the small timer text
    void HandleTimer(int secs)
    {
        if (!fingerprintPanel || !fingerprintPanel.activeInHierarchy) return;
        if (!timerText) return;

        Debug.Log($"[AuthUI] Timer update: {secs}");

        if (secs <= 0)
        {
            timerText.text = "";
            return;
        }

        timerText.text = $"Timer: {secs}s left";
    }


    /*void HandleTimer(int secs)
    {
        if (!fingerprintPanel || !fingerprintPanel.activeInHierarchy) return;
        if (!timerText) return;

        timerText.text = secs > 0 ? $"Timer: {secs}s left" : "";
    }*/


    void HandleDeviceMsg(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;

        // ignore JSON/status lines
        if (msg.Length > 0 && (msg[0] == '{' || msg.Contains("sensorReady") || msg.Contains("\"op\"")))
        {
            UpdateDeviceIdFromJson(msg);
            return;   // keep old behaviour of ignoring JSON for UI text
        }

        var p = msg.ToLowerInvariant();
        Debug.Log($"[AuthUI] DeviceMsg: {p}");

        // ----- 1) Reply to exists:<name> check -----
        if (_waitingForExistCheck)
        {
            _waitingForExistCheck = false;

            if (p.Contains("user not found"))
            {
                SetPanels(false, "User not found. Please register first.");
                ShowOkBack();
                return;
            }

            if (p.Contains("user exist"))
            {
                //notify TrialManager (if it exists)
                TrialManager.Instance?.OnUserExists(_currentUserName); 

                ShowAuthMethodPanel();
                return;
            }

            // If it's something else, just fall through
        }

        if (p.Contains("registration failed"))
        {
            if (fingerprintText)
                fingerprintText.text = "Registration failed (timeout). Please try again.";

            if (timerText)
                timerText.text = "";   // stop showing "Timer: 2s left"

            ShowOkBack();
            _verifyInProgressUI = false;
            return;
        }

        // ----- 2) End of a verify attempt (independent of exists check) -----
        if (p.Contains("verify timeout") || p.Contains("wrong finger") || p.Contains("verified"))
        {
            _verifyInProgressUI = false;

            if (p.Contains("verify timeout"))
            {
                // timeout case
                TrialManager.Instance?.OnFingerprintResult(false, "timeout");
            }
            else if (p.Contains("wrong finger"))
            {
                // mismatch / wrong finger
                TrialManager.Instance?.OnFingerprintResult(false, "mismatch");
            }
            else if (p.Contains("verified"))
            {
                // success
                TrialManager.Instance?.OnFingerprintResult(true, "none");
            }
        }


        // ----- 3) Normal fingerprint flow -----
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

    public void UpdateDeviceIdFromJson(string json)
    {
        if (string.IsNullOrEmpty(json) || json[0] != '{') return;

        // find `"last"`
        int lastIdx = json.IndexOf("\"last\"");
        if (lastIdx < 0) return;

        // find `"id":` after `"last"`
        int idIdx = json.IndexOf("\"id\":", lastIdx);
        if (idIdx < 0) return;

        int numStart = idIdx + "\"id\":".Length;

        // skip spaces
        while (numStart < json.Length && char.IsWhiteSpace(json[numStart]))
            numStart++;

        // collect digits (and optional minus)
        string digits = "";
        int i = numStart;
        while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-'))
        {
            digits += json[i];
            i++;
        }

        if (int.TryParse(digits, out int id))
        {
            LastDeviceUserId = id.ToString();
            Debug.Log("[WS] Parsed device user ID = " + LastDeviceUserId);
        }
    }


    public void BackToLogin()
    {
        _flow = Flow.None;
        _currentUserName = null;
        _waitingForExistCheck = false;

        if (authMethodPanel) authMethodPanel.SetActive(false);
        SetPanels(true, "");
        if (userNameField) userNameField.text = "";

        if (rhythmController) rhythmController.StopMorseTimer();
    }


    // ---------- UI helper ----------
    // showLogin = true → login panel
    // showLogin = false → fingerprint prompt panel
    void SetPanels(bool showLogin, string message)
    {
        if (loginPanel) loginPanel.SetActive(showLogin);
        if (fingerprintPanel) fingerprintPanel.SetActive(!showLogin);
        if (fingerprintText) fingerprintText.text = message ?? "";

        if (timerText) timerText.text = "";   // NEW: clear timer when switching panels

        if (okButton)
        {
            okButton.gameObject.SetActive(!showLogin);
            okButton.interactable = false;     // disabled on initial message
            if (okButtonLabel) okButtonLabel.text = "OK";
        }
    }
}
