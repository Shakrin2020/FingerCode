using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIPanelActions : MonoBehaviour
{
    public GameObject userLoginPanel;  // e.g. "UserSelection"
    public GameObject logRegPanel;     // "LogReg"
    public GameObject motionPanel;     // "Motion"

    private void Awake()
    {
        // Choose default
        ShowUserLogin();     // or ShowLogin() / ShowMotion()
    }

    public void ShowMotion()
    {
        if (userLoginPanel) userLoginPanel.SetActive(false);
        if (logRegPanel) logRegPanel.SetActive(false);
        if (motionPanel) motionPanel.SetActive(true);
    }

    public void ShowLogin()
    {
        if (userLoginPanel) userLoginPanel.SetActive(false);
        if (logRegPanel) logRegPanel.SetActive(true);
        if (motionPanel) motionPanel.SetActive(false);
    }

    public void ShowUserLogin()
    {
        if (userLoginPanel) userLoginPanel.SetActive(true);
        if (logRegPanel) logRegPanel.SetActive(false);
        if (motionPanel) motionPanel.SetActive(false);
    }
}
