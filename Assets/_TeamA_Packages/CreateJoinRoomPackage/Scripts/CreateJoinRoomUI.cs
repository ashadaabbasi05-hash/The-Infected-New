using System;
using UnityEngine;
using UnityEngine.UI;

public class CreateJoinRoomUI : MonoBehaviour
{
    public Action<string> OnCreateRoomClicked;
    public Action<string, string> OnJoinRoomClicked;
    public Action OnBackClicked;

    [SerializeField] private string playerName = "Player";
    [SerializeField] private InputField roomCodeInput;
    [SerializeField] private Text errorText;
    [SerializeField] private Text loadingText;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button backButton;

    private void Awake()
    {
        ResolveReferences();
        BindButtons();
        SetLoading(false);
        SetError(string.Empty);
    }

    private void ResolveReferences()
    {
        if (roomCodeInput == null)
        {
            Transform inputTransform = transform.Find("SafeAreaRoot/CenterPanel/RoomCodeInput");
            if (inputTransform != null)
            {
                roomCodeInput = inputTransform.GetComponent<InputField>();
            }
        }

        if (errorText == null)
        {
            Transform errorTransform = transform.Find("SafeAreaRoot/CenterPanel/ErrorText");
            if (errorTransform != null)
            {
                errorText = errorTransform.GetComponent<Text>();
            }
        }

        if (loadingText == null)
        {
            Transform loadingTransform = transform.Find("SafeAreaRoot/CenterPanel/LoadingText");
            if (loadingTransform != null)
            {
                loadingText = loadingTransform.GetComponent<Text>();
            }
        }

        if (joinRoomButton == null)
        {
            Transform joinTransform = transform.Find("SafeAreaRoot/CenterPanel/JoinRoomButton");
            if (joinTransform != null)
            {
                joinRoomButton = joinTransform.GetComponent<Button>();
            }
        }

        if (createRoomButton == null)
        {
            Transform createTransform = transform.Find("SafeAreaRoot/CenterPanel/CreateRoomButton");
            if (createTransform != null)
            {
                createRoomButton = createTransform.GetComponent<Button>();
            }
        }

        if (backButton == null)
        {
            Transform backTransform = transform.Find("SafeAreaRoot/FooterSection/BackButton");
            if (backTransform != null)
            {
                backButton = backTransform.GetComponent<Button>();
            }
        }
    }

    private void BindButtons()
    {
        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.RemoveAllListeners();
            joinRoomButton.onClick.AddListener(ClickJoinRoom);
        }

        if (createRoomButton != null)
        {
            createRoomButton.onClick.RemoveAllListeners();
            createRoomButton.onClick.AddListener(ClickCreateRoom);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(ClickBack);
        }
    }

    public void SetPlayerName(string newPlayerName)
    {
        if (!string.IsNullOrWhiteSpace(newPlayerName))
        {
            playerName = newPlayerName.Trim();
        }
    }

    public void SetRoomCode(string newRoomCode)
    {
        if (roomCodeInput == null)
        {
            return;
        }

        roomCodeInput.text = string.IsNullOrWhiteSpace(newRoomCode) ? string.Empty : newRoomCode.Trim().ToUpperInvariant();
    }

    public void SetError(string message)
    {
        if (errorText == null)
        {
            return;
        }

        errorText.text = message ?? string.Empty;
        errorText.gameObject.SetActive(!string.IsNullOrWhiteSpace(errorText.text));
    }

    public void SetLoading(bool loading)
    {
        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(loading);
            loadingText.text = loading ? "CONNECTING..." : string.Empty;
        }

        if (joinRoomButton != null)
        {
            joinRoomButton.interactable = !loading;
        }

        if (createRoomButton != null)
        {
            createRoomButton.interactable = !loading;
        }
    }

    public void ClearInputs()
    {
        if (roomCodeInput != null)
        {
            roomCodeInput.text = string.Empty;
        }

        SetError(string.Empty);
        SetLoading(false);
    }

    public void ClickJoinRoom()
    {
        string roomCode = roomCodeInput != null ? roomCodeInput.text.Trim().ToUpperInvariant() : string.Empty;
        if (string.IsNullOrEmpty(roomCode))
        {
            SetError("Please enter a room key.");
            Debug.Log("CreateJoinRoomUI: Join clicked - Player / ");
            return;
        }

        SetError(string.Empty);
        SetLoading(true);
        Debug.Log($"CreateJoinRoomUI: Join clicked - {playerName} / {roomCode}");
        OnJoinRoomClicked?.Invoke(playerName, roomCode);
    }

    public void ClickCreateRoom()
    {
        SetError(string.Empty);
        SetLoading(true);
        Debug.Log($"CreateJoinRoomUI: Create clicked - {playerName}");
        OnCreateRoomClicked?.Invoke(playerName);
    }

    public void ClickBack()
    {
        SetError(string.Empty);
        SetLoading(false);
        Debug.Log("CreateJoinRoomUI: Back clicked");
        OnBackClicked?.Invoke();
    }
}