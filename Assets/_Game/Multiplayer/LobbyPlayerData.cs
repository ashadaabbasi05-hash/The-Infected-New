using System;

[Serializable]
public class LobbyPlayerData
{
    public string playerId;
    public string displayName;
    public bool isHost;
    public bool isReady;
    public bool isAlive;
    public bool isBot;
    public long lastSeenAt;
}
