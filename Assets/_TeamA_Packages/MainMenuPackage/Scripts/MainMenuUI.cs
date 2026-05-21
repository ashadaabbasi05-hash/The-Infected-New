using System;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    public Action OnStartClicked;
    public Action OnSettingsClicked;
    public Action OnQuitClicked;

    private void Awake()
    {
        BindButton("StartButton", ClickStart);
        BindButton("SettingsButton", ClickSettings);
        BindButton("QuitButton", ClickQuit);
    }

    private void BindButton(string buttonName, Action clickAction)
    {
        Transform buttonTransform = transform.Find(buttonName);
        if (buttonTransform == null)
        {
            return;
        }

        Button button = buttonTransform.GetComponent<Button>();
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => clickAction());
    }

    public void ClickStart()
    {
        Debug.Log("MainMenuUI: Start clicked");
        OnStartClicked?.Invoke();
    }

    public void ClickSettings()
    {
        Debug.Log("MainMenuUI: Settings clicked");
        OnSettingsClicked?.Invoke();
    }

    public void ClickQuit()
    {
        Debug.Log("MainMenuUI: Quit clicked");
        OnQuitClicked?.Invoke();
    }
}