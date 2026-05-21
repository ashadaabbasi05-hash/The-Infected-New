using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MultiplayerRoomUIController : MonoBehaviour
{
    [SerializeField] GameObject roomPanel;
    [SerializeField] TMP_InputField roomCodeInput;
    [SerializeField] TMP_InputField playerIdInput;
    [SerializeField] Button createRoomButton;
    [SerializeField] Button joinRoomButton;
    [SerializeField] Button leaveRoomButton;
    [SerializeField] TMP_Text statusText;
    [SerializeField] FirebaseMultiplayerClient client;

    bool isWired;

    void Awake()
    {
        AutoFindReferences();
        WireButtons();
        RefreshStatus();
    }

    void OnEnable()
    {
        AutoFindReferences();
        WireButtons();
        RefreshStatus();
    }

    void OnDisable()
    {
        UnwireButtons();
    }

    void Update()
    {
        RefreshStatus();
    }

    void AutoFindReferences()
    {
        if (roomPanel == null)
        {
            roomPanel = GameObject.Find("MultiplayerRoomPanel");
        }

        if (roomCodeInput == null)
        {
            GameObject found = GameObject.Find("RoomCodeInput");
            if (found != null)
            {
                roomCodeInput = found.GetComponent<TMP_InputField>();
            }
        }

        if (playerIdInput == null)
        {
            GameObject found = GameObject.Find("PlayerIdInput");
            if (found != null)
            {
                playerIdInput = found.GetComponent<TMP_InputField>();
            }
        }

        if (createRoomButton == null)
        {
            GameObject found = GameObject.Find("CreateRoomButton");
            if (found != null)
            {
                createRoomButton = found.GetComponent<Button>();
            }
        }

        if (joinRoomButton == null)
        {
            GameObject found = GameObject.Find("JoinRoomButton");
            if (found != null)
            {
                joinRoomButton = found.GetComponent<Button>();
            }
        }

        if (leaveRoomButton == null)
        {
            GameObject found = GameObject.Find("LeaveRoomButton");
            if (found != null)
            {
                leaveRoomButton = found.GetComponent<Button>();
            }
        }

        if (statusText == null)
        {
            GameObject found = GameObject.Find("MultiplayerStatusText");
            if (found != null)
            {
                statusText = found.GetComponent<TMP_Text>();
            }
        }

        if (client == null)
        {
            client = FirebaseMultiplayerClient.TryGetActiveClient();
        }
    }

    void WireButtons()
    {
        if (isWired)
        {
            return;
        }

        if (createRoomButton != null)
        {
            createRoomButton.onClick.AddListener(HandleCreateRoomClicked);
        }

        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.AddListener(HandleJoinRoomClicked);
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.onClick.AddListener(HandleLeaveRoomClicked);
        }

        isWired = true;
    }

    void UnwireButtons()
    {
        if (!isWired)
        {
            return;
        }

        if (createRoomButton != null)
        {
            createRoomButton.onClick.RemoveListener(HandleCreateRoomClicked);
        }

        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.RemoveListener(HandleJoinRoomClicked);
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.onClick.RemoveListener(HandleLeaveRoomClicked);
        }

        isWired = false;
    }

    void HandleCreateRoomClicked()
    {
        if (client == null)
        {
            client = FirebaseMultiplayerClient.TryGetActiveClient();
        }

        if (client != null)
        {
            client.CreateRoom(GetRoomCode());
        }

        RefreshStatus();
    }

    void HandleJoinRoomClicked()
    {
        if (client == null)
        {
            client = FirebaseMultiplayerClient.TryGetActiveClient();
        }

        if (client != null)
        {
            client.JoinRoom(GetRoomCode(), GetPlayerId());
        }

        RefreshStatus();
    }

    void HandleLeaveRoomClicked()
    {
        if (client == null)
        {
            client = FirebaseMultiplayerClient.TryGetActiveClient();
        }

        if (client != null)
        {
            client.LeaveRoom();
        }

        RefreshStatus();
    }

    string GetRoomCode()
    {
        if (roomCodeInput == null || string.IsNullOrWhiteSpace(roomCodeInput.text))
        {
            return "ROOM123";
        }

        return roomCodeInput.text.Trim();
    }

    string GetPlayerId()
    {
        if (playerIdInput == null || string.IsNullOrWhiteSpace(playerIdInput.text))
        {
            return "player_1";
        }

        return playerIdInput.text.Trim();
    }

    void RefreshStatus()
    {
        if (statusText == null)
        {
            return;
        }

        if (client == null)
        {
            client = FirebaseMultiplayerClient.TryGetActiveClient();
        }

        if (client == null || !client.IsOnline)
        {
            statusText.text = "Firebase disabled - local demo mode";
            return;
        }

        statusText.text = client.IsHost ? $"Host ready: {GetRoomCode()}" : $"Joined room: {GetRoomCode()}";
    }
}