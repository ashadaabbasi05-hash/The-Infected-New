using System.Collections.Generic;

/// <summary>
/// DTOs for Team A backend API - PRD V4 contract.
/// Compatible with Unity JsonUtility.
/// All fields are public and use camelCase to match JSON exactly.
/// </summary>

[System.Serializable]
public class HealthResponse
{
    public string status;
    public string service;
    public string contractVersion;
    public string aiMode;
    public string llmProvider;
    public bool firebaseConfigured;
}

[System.Serializable]
public class RegisterBotRequest
{
    public string matchId;
    public string botId;
    public string botName;
    public int wave;
    public int cycle;
    public string phase;
    public string[] alivePlayers;
    public string[] humanPlayers;
    public string[] infectedPlayers;
    public int taskProgress;
}

[System.Serializable]
public class RegisterBotResponse
{
    public bool ok;
    public string botId;
    public string personality;
    public string behaviorMode;
    public string trace;
}

[System.Serializable]
public class UnregisterBotRequest
{
    public string matchId;
    public string botId;
    public string reason;
}

[System.Serializable]
public class UnregisterBotResponse
{
    public bool ok;
    public string botId;
    public string trace;
}

[System.Serializable]
public class DecideActionRequest
{
    public string matchId;
    public string phase;
    public int wave;
    public int cycle;
    public string botId;
    public string botName;
    public string[] infectedPlayers;
    public string[] humanPlayers;
    public string[] alivePlayers;
    public int taskProgress;
    public string nearestHuman;
    public string botRoom;
    public string nearestHumanRoom;
    public float secondsSinceLastSeenHuman;
    public bool isFinalChase;
}

[System.Serializable]
public class DecideActionResponse
{
    public string botId;
    public string behaviorMode;
    public string targetRoom;
    public string targetPlayer;
    public bool shouldChase;
    public int nextDecisionInSeconds;
    public string trace;
}

[System.Serializable]
public class ChatMessageDto
{
    public string sender;
    public string senderName;
    public string text;
}

[System.Serializable]
public class RespondRequest
{
    public string matchId;
    public string phase;
    public int wave;
    public int cycle;
    public string botId;
    public string botName;
    public string personality;
    public string message;
    public ChatMessageDto latestMessage;
    public ChatMessageDto[] recentChat;
    public string[] alivePlayers;
    public string[] humanPlayers;
    public string[] infectedPlayers;
}

[System.Serializable]
public class RespondResponse
{
    public string botId;
    public bool respond;
    public string[] messages;
    public float typingDelaySeconds;
    public float secondMessageDelaySeconds;
    public string trace;
}

[System.Serializable]
public class VoteRequest
{
    public string matchId;
    public string phase;
    public int wave;
    public int cycle;
    public string botId;
    public string botName;
    public string[] alivePlayers;
    public string[] humanPlayers;
    public string[] infectedPlayers;
    public ChatMessageDto[] recentChat;
}

[System.Serializable]
public class VoteResponse
{
    public string botId;
    public string voteTarget;
    public string reason;
    public string trace;
}

[System.Serializable]
public class TraceEntry
{
    public string ts;
    public string eventType;
    public string matchId;
    public string botId;
    public string input;
    public string output;
    public string trace;
}

[System.Serializable]
public class TraceResponse
{
    public string matchId;
    public int count;
    public TraceEntry[] traces;
}
