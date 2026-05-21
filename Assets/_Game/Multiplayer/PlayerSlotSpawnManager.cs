using UnityEngine;

public static class PlayerSlotSpawnManager
{
    public static bool TryGetSpawnPosition(string playerId, out Vector3 position)
    {
        switch (NormalizePlayerId(playerId))
        {
            case "player_1":
                position = new Vector3(-2f, 0f, 0f);
                return true;
            case "player_2":
                position = new Vector3(2f, 0f, 0f);
                return true;
            case "player_3":
                position = new Vector3(-2f, -2f, 0f);
                return true;
            case "player_4":
                position = new Vector3(2f, -2f, 0f);
                return true;
            default:
                position = Vector3.zero;
                return false;
        }
    }

    static string NormalizePlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return string.Empty;
        }

        return playerId.Trim().ToLowerInvariant();
    }
}
