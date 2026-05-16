using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class InfectionSystem : MonoBehaviour
{
    [SerializeField, Min(0f)]
    float safePhaseDuration = 10f;

    [SerializeField, Min(0f)]
    float gasWaveDuration = 5f;

    [SerializeField]
    AudioSource alarmSource;

    [SerializeField]
    AudioSource ambientSource;

    [SerializeField]
    AudioClip alarmClip;

    [SerializeField]
    AudioClip ambientClip;

    [SerializeField, Min(0f)]
    float ambientGasVolume = 0.25f;

    [SerializeField]
    Image gasOverlay;

    [SerializeField, Min(0f)]
    float overlayFadeSpeed = 1.5f;

    float timer;

    bool isGasWaveActive;

    PlayerIdentity currentInfectedPlayer;

    float ambientSafeVolume = 1f;

    void Start()
    {
        InitializePresentation();
        StartSafePhase(true);
    }

    public void InfectRandomPlayer()
    {
        PlayerIdentity[] allPlayers = FindObjectsByType<PlayerIdentity>(FindObjectsInactive.Include);

        if (allPlayers == null || allPlayers.Length == 0)
        {
            return;
        }

        ClearExistingInfections(allPlayers);

        List<PlayerIdentity> eligiblePlayers = new List<PlayerIdentity>();

        for (int index = 0; index < allPlayers.Length; index++)
        {
            PlayerIdentity player = allPlayers[index];

            if (player == null)
            {
                continue;
            }

            if (!player.isAlive || player.isInfected)
            {
                continue;
            }

            eligiblePlayers.Add(player);
        }

        if (eligiblePlayers.Count == 0)
        {
            return;
        }

        int selectedIndex = Random.Range(0, eligiblePlayers.Count);
        PlayerIdentity selectedPlayer = eligiblePlayers[selectedIndex];

        selectedPlayer.SetInfected(true);
        currentInfectedPlayer = selectedPlayer;
        Debug.Log($"InfectionSystem: {selectedPlayer.playerName} was infected.", selectedPlayer);

        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }
    }

    void Update()
    {
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
        }

        if (timer <= 0f)
        {
            if (isGasWaveActive)
            {
                StartSafePhase();
            }
            else
            {
                StartGasWave();
            }
        }

        UpdateGasOverlay();
    }

    void StartSafePhase(bool isInitialStart = false)
    {
        isGasWaveActive = false;
        timer = safePhaseDuration;

        ClearExistingInfections(PlayerIdentity.GetAllPlayers());
        currentInfectedPlayer = null;

        ApplySafePhaseAudio();

        if (isInitialStart)
        {
            return;
        }
    }

    void StartGasWave()
    {
        isGasWaveActive = true;
        timer = gasWaveDuration;

        ApplyGasWaveAudio();
        InfectRandomPlayer();
    }

    void InitializePresentation()
    {
        if (ambientSource != null)
        {
            ambientSafeVolume = ambientSource.volume;
            ambientSource.loop = true;
        }

        if (alarmSource != null)
        {
            alarmSource.loop = true;
        }

        if (gasOverlay != null)
        {
            SetOverlayAlpha(0f);
        }
    }

    void ApplySafePhaseAudio()
    {
        if (alarmSource != null)
        {
            alarmSource.Stop();
        }

        if (ambientSource != null)
        {
            if (ambientClip != null && ambientSource.clip != ambientClip)
            {
                ambientSource.clip = ambientClip;
            }

            ambientSource.loop = true;
            ambientSource.volume = ambientSafeVolume;

            if (ambientSource.clip != null && !ambientSource.isPlaying)
            {
                ambientSource.Play();
            }
        }
    }

    void ApplyGasWaveAudio()
    {
        if (ambientSource != null)
        {
            if (ambientClip != null && ambientSource.clip != ambientClip)
            {
                ambientSource.clip = ambientClip;
            }

            ambientSource.loop = true;
            ambientSource.volume = ambientGasVolume;

            if (ambientSource.clip != null && !ambientSource.isPlaying)
            {
                ambientSource.Play();
            }
        }

        if (alarmSource != null)
        {
            if (alarmClip != null && alarmSource.clip != alarmClip)
            {
                alarmSource.clip = alarmClip;
            }

            alarmSource.loop = true;

            if (alarmSource.clip != null)
            {
                alarmSource.Stop();
                alarmSource.Play();
            }
        }
    }

    void UpdateGasOverlay()
    {
        if (gasOverlay == null)
        {
            return;
        }

        float targetAlpha = isGasWaveActive ? 0.5f : 0f;
        Color overlayColor = Color.red;
        overlayColor.a = Mathf.MoveTowards(gasOverlay.color.a, targetAlpha, overlayFadeSpeed * Time.deltaTime);
        gasOverlay.color = overlayColor;
    }

    void SetOverlayAlpha(float alpha)
    {
        if (gasOverlay == null)
        {
            return;
        }

        Color overlayColor = Color.red;
        overlayColor.a = alpha;
        gasOverlay.color = overlayColor;
    }

    void ClearExistingInfections(PlayerIdentity[] allPlayers)
    {
        for (int index = 0; index < allPlayers.Length; index++)
        {
            PlayerIdentity player = allPlayers[index];

            if (player == null || !player.isInfected)
            {
                continue;
            }

            player.SetInfected(false);

            if (player == currentInfectedPlayer)
            {
                currentInfectedPlayer = null;
            }
        }
    }

}