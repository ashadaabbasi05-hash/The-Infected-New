using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(9999)]
public class V5IntegrationReadinessChecker : MonoBehaviour
{
    public bool runOnStart = true;
    public bool verbose = true;

    void Start()
    {
        if (runOnStart)
        {
            RunCheck();
        }
    }

    [ContextMenu("Run V5 Integration Check")]
    public void RunCheck()
    {
        int pass = 0, warn = 0, fail = 0;

        bool hasFlow = FindAnyObjectByType<GameFlowManager>() != null;
        bool hasFirebase = FindAnyObjectByType<FirebaseMultiplayerClient>() != null;
        bool hasTeamA = FindAnyObjectByType<TeamAApiClient>() != null;
        bool hasPlayer1 = false;
        var players = PlayerIdentity.GetAllPlayers();
        if (players != null)
        {
            foreach (var p in players)
            {
                if (p != null && p.playerId == 1) { hasPlayer1 = true; break; }
            }
        }
        bool hasGameManager = FindAnyObjectByType<GameManager>() != null;
        bool hasCanvas = FindAnyObjectByType<Canvas>() != null;
        bool hasEvent = FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null;

        if (hasFlow) pass++; else fail++;
        if (hasFirebase) pass++; else fail++;
        if (hasTeamA) pass++; else warn++;
        if (hasPlayer1) pass++; else warn++;
        if (hasGameManager) pass++; else fail++;
        if (hasCanvas) pass++; else fail++;
        if (hasEvent) pass++; else fail++;

        Debug.Log("[V5 CHECK] SUMMARY: PASS=" + pass + " WARN=" + warn + " FAIL=" + fail);
    }

#if UNITY_EDITOR
    [MenuItem("Tools/The Infected/Run V5 Integration Check")]
    public static void MenuRun()
    {
        var go = new GameObject("V5IntegrationReadinessChecker");
        var comp = go.AddComponent<V5IntegrationReadinessChecker>();
        comp.runOnStart = false;
        comp.RunCheck();
        DestroyImmediate(go);
    }
#endif
}
