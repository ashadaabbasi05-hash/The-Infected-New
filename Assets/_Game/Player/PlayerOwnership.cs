using UnityEngine;

/// <summary>
/// Tags a player GameObject with ownership metadata.
/// Attached to spawned remote players and the local player.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerOwnership : MonoBehaviour
{
    [SerializeField] string playerId = "";
    [SerializeField] bool isLocal;
    [SerializeField] bool isRemoteHuman;
    [SerializeField] bool isBot;

    public string PlayerId => playerId;
    public bool IsLocal => isLocal;
    public bool IsRemoteHuman => isRemoteHuman;
    public bool IsBot => isBot;

    public void Configure(string id, bool local, bool remoteHuman, bool bot)
    {
        playerId = id ?? "";
        isLocal = local;
        isRemoteHuman = remoteHuman;
        isBot = bot;
    }
}