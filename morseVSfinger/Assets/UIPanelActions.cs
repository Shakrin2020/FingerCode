using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIPanelActions : MonoBehaviour
{
    public GameObject userLoginPanel;  // e.g. "UserSelection"
    public GameObject logRegPanel;     
    public GameObject methodPanel;      

    private void Awake()
    {
        // Choose default
        ShowLogin();     // or ShowLogin() / ShowMotion()
    }

    public void ShowMethod()
    {
        if (userLoginPanel) userLoginPanel.SetActive(false);
        if (logRegPanel) logRegPanel.SetActive(false);
        if (methodPanel) methodPanel.SetActive(true);
    }

    public void ShowLogin()
    {
        if (userLoginPanel) userLoginPanel.SetActive(false);
        if (logRegPanel) logRegPanel.SetActive(true);
        if (methodPanel) methodPanel.SetActive(false);
    }

    public void ShowUserLogin()
    {
        if (userLoginPanel) userLoginPanel.SetActive(true);
        if (logRegPanel) logRegPanel.SetActive(false);
        if (methodPanel) methodPanel.SetActive(false);
    }
}
