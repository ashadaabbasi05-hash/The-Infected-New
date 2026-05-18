using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class GasWaveEffectsController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Image gasOverlayImage;
    [SerializeField] Transform cameraShakeTarget;
    [SerializeField] AudioSource alarmSource;
    [SerializeField] AudioClip alarmClip;
    [SerializeField] AudioSource ambientSource;
    [SerializeField] AudioClip ambientLoopClip;

    [Header("Overlay")]
    [SerializeField] Color overlayColor = new Color(0.65f, 0.8f, 0.2f, 1f);
    [SerializeField, Range(0f, 1f)] float maxOverlayAlpha = 0.55f;
    [SerializeField, Min(0.01f)] float fadeInDuration = 1.1f;
    [SerializeField, Min(0.01f)] float fadeOutDuration = 1.35f;

    [Header("Camera Shake")]
    [SerializeField, Min(0f)] float shakeDuration = 0.55f;
    [SerializeField, Min(0f)] float shakeIntensity = 0.08f;
    [SerializeField, Min(0f)] float shakeFrequency = 24f;

    [Header("Gameplay Pressure")]
    [SerializeField, Range(0.5f, 1f)] float gasWaveMoveSpeedMultiplier = 0.9f;
    [SerializeField] bool autoFindPlayerMovements = true;
    [SerializeField] PlayerMovement[] playerMovements;

    [Header("Audio")]
    [SerializeField, Range(0f, 1f)] float alarmVolume = 1f;
    [SerializeField, Range(0f, 1f)] float ambientVolume = 0.55f;

    GameManager gameManager;
    Coroutine overlayRoutine;
    Coroutine cameraShakeRoutine;
    Coroutine ambientFadeRoutine;

    readonly Dictionary<PlayerMovement, float> originalMoveSpeeds = new Dictionary<PlayerMovement, float>();

    Color baseOverlayColor;
    Vector3 cameraShakeBaseLocalPosition;
    bool isGasWaveActive;

    void Awake()
    {
        TryCacheGameManager();
        ResolveDefaults();
        SetOverlayAlpha(0f);
        SetEffectsEnabled(false);
    }

    void OnEnable()
    {
        TryCacheGameManager();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGasWaveStarted -= HandleGasWaveStarted;
            GameManager.Instance.OnGasWaveEnded -= HandleGasWaveEnded;
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;

            GameManager.Instance.OnGasWaveStarted += HandleGasWaveStarted;
            GameManager.Instance.OnGasWaveEnded += HandleGasWaveEnded;
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        }

        SyncToCurrentState();
    }

    void Start()
    {
        SyncToCurrentState();
    }

    void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGasWaveStarted -= HandleGasWaveStarted;
            GameManager.Instance.OnGasWaveEnded -= HandleGasWaveEnded;
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }

        StopAllCoroutines();
        ResetEffectsImmediate();
    }

    void TryCacheGameManager()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
    }

    void ResolveDefaults()
    {
        if (gasOverlayImage != null)
        {
            baseOverlayColor = overlayColor;
            gasOverlayImage.color = new Color(baseOverlayColor.r, baseOverlayColor.g, baseOverlayColor.b, 0f);
            gasOverlayImage.gameObject.SetActive(false);
        }

        if (cameraShakeTarget == null && Camera.main != null)
        {
            cameraShakeTarget = Camera.main.transform;
        }

        if (cameraShakeTarget != null)
        {
            cameraShakeBaseLocalPosition = cameraShakeTarget.localPosition;
        }

        if (ambientSource != null)
        {
            ambientSource.loop = true;
            ambientSource.playOnAwake = false;
            ambientSource.volume = 0f;
        }

        if (alarmSource != null)
        {
            alarmSource.loop = false;
            alarmSource.playOnAwake = false;
        }
    }

    void SyncToCurrentState()
    {
        TryCacheGameManager();

        if (GameManager.Instance == null) return;

        if (GameManager.CurrentPhase == GamePhase.GasWave)
        {
            ActivateGasWaveEffects(false);
        }
        else
        {
            ResetEffectsImmediate();
        }
    }

    void HandlePhaseChanged(GamePhase phase)
    {
        if (phase != GamePhase.GasWave && isGasWaveActive)
        {
            DeactivateGasWaveEffects();
        }
    }

    void HandleGasWaveStarted()
    {
        ActivateGasWaveEffects(true);
    }

    void HandleGasWaveEnded()
    {
        DeactivateGasWaveEffects();
    }

    void ActivateGasWaveEffects(bool playAlarm)
    {
        if (isGasWaveActive)
        {
            return;
        }

        isGasWaveActive = true;
        SetEffectsEnabled(true);
        ApplyMovementPressure();

        if (overlayRoutine != null)
        {
            StopCoroutine(overlayRoutine);
        }

        overlayRoutine = StartCoroutine(FadeOverlayRoutine(0f, maxOverlayAlpha, fadeInDuration));

        if (cameraShakeRoutine != null)
        {
            StopCoroutine(cameraShakeRoutine);
        }

        cameraShakeRoutine = StartCoroutine(CameraShakeRoutine());

        if (playAlarm && alarmSource != null && alarmClip != null)
        {
            alarmSource.PlayOneShot(alarmClip, alarmVolume);
        }

        if (ambientSource != null && ambientLoopClip != null)
        {
            ambientSource.clip = ambientLoopClip;
            if (!ambientSource.isPlaying)
            {
                ambientSource.Play();
            }

            FadeAmbientVolume(ambientVolume);
        }
    }

    void DeactivateGasWaveEffects()
    {
        if (!isGasWaveActive && overlayRoutine == null && cameraShakeRoutine == null)
        {
            ResetEffectsImmediate();
            return;
        }

        isGasWaveActive = false;
        RestoreMovementPressure();

        if (overlayRoutine != null)
        {
            StopCoroutine(overlayRoutine);
        }

        overlayRoutine = StartCoroutine(FadeOverlayRoutine(GetCurrentOverlayAlpha(), 0f, fadeOutDuration, true));

        if (cameraShakeRoutine != null)
        {
            StopCoroutine(cameraShakeRoutine);
            cameraShakeRoutine = null;
        }

        if (ambientSource != null)
        {
            FadeAmbientVolume(0f, true);
        }
    }

    void ApplyMovementPressure()
    {
        if (playerMovements == null || playerMovements.Length == 0)
        {
            if (!autoFindPlayerMovements) return;
            playerMovements = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
        }

        if (playerMovements == null) return;

        foreach (PlayerMovement movement in playerMovements)
        {
            if (movement == null || originalMoveSpeeds.ContainsKey(movement))
            {
                continue;
            }

            originalMoveSpeeds[movement] = movement.maxSpeed;
            movement.maxSpeed *= gasWaveMoveSpeedMultiplier;
        }
    }

    void RestoreMovementPressure()
    {
        foreach (KeyValuePair<PlayerMovement, float> entry in originalMoveSpeeds)
        {
            if (entry.Key != null)
            {
                entry.Key.maxSpeed = entry.Value;
            }
        }

        originalMoveSpeeds.Clear();
    }

    void ResetEffectsImmediate()
    {
        isGasWaveActive = false;

        if (overlayRoutine != null)
        {
            StopCoroutine(overlayRoutine);
            overlayRoutine = null;
        }

        if (cameraShakeRoutine != null)
        {
            StopCoroutine(cameraShakeRoutine);
            cameraShakeRoutine = null;
        }

        if (ambientFadeRoutine != null)
        {
            StopCoroutine(ambientFadeRoutine);
            ambientFadeRoutine = null;
        }

        RestoreMovementPressure();
        SetOverlayAlpha(0f);
        SetEffectsEnabled(false);

        if (ambientSource != null)
        {
            ambientSource.Stop();
            ambientSource.volume = 0f;
        }

        if (alarmSource != null)
        {
            alarmSource.Stop();
        }

        if (cameraShakeTarget != null)
        {
            cameraShakeTarget.localPosition = cameraShakeBaseLocalPosition;
        }
    }

    public void ForceStopGasEffects()
    {
        ResetEffectsImmediate();
        Debug.Log("[GAS] Force stopped gas effects.", this);
    }

    void SetEffectsEnabled(bool enabled)
    {
        if (gasOverlayImage != null)
        {
            gasOverlayImage.gameObject.SetActive(enabled);
        }
    }

    void SetOverlayAlpha(float alpha)
    {
        if (gasOverlayImage == null) return;

        Color color = baseOverlayColor;
        color.a = Mathf.Clamp01(alpha);
        gasOverlayImage.color = color;
    }

    float GetCurrentOverlayAlpha()
    {
        if (gasOverlayImage == null) return 0f;
        return gasOverlayImage.color.a;
    }

    void FadeAmbientVolume(float targetVolume, bool stopWhenFinished = false)
    {
        if (ambientSource == null) return;

        if (ambientFadeRoutine != null)
        {
            StopCoroutine(ambientFadeRoutine);
        }

        ambientFadeRoutine = StartCoroutine(FadeAudioVolumeRoutine(ambientSource, targetVolume, 0.45f, stopWhenFinished));
    }

    IEnumerator FadeOverlayRoutine(float fromAlpha, float toAlpha, float duration, bool disableAtEnd = false)
    {
        if (gasOverlayImage == null)
        {
            overlayRoutine = null;
            yield break;
        }

        SetEffectsEnabled(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            SetOverlayAlpha(Mathf.Lerp(fromAlpha, toAlpha, t));
            yield return null;
        }

        SetOverlayAlpha(toAlpha);

        if (disableAtEnd && Mathf.Approximately(toAlpha, 0f))
        {
            SetEffectsEnabled(false);
        }

        overlayRoutine = null;
    }

    IEnumerator FadeAudioVolumeRoutine(AudioSource source, float targetVolume, float duration, bool stopWhenFinished)
    {
        if (source == null)
        {
            ambientFadeRoutine = null;
            yield break;
        }

        float startVolume = source.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            source.volume = Mathf.Lerp(startVolume, targetVolume, t);
            yield return null;
        }

        source.volume = targetVolume;

        if (stopWhenFinished && Mathf.Approximately(targetVolume, 0f))
        {
            source.Stop();
        }

        ambientFadeRoutine = null;
    }

    IEnumerator CameraShakeRoutine()
    {
        if (cameraShakeTarget == null)
        {
            cameraShakeRoutine = null;
            yield break;
        }

        Vector3 originalLocalPosition = cameraShakeTarget.localPosition;
        float elapsed = 0f;
        float nextPulseTime = 0f;

        while (elapsed < shakeDuration && isGasWaveActive)
        {
            elapsed += Time.deltaTime;
            nextPulseTime -= Time.deltaTime;

            if (nextPulseTime <= 0f)
            {
                nextPulseTime = 1f / Mathf.Max(1f, shakeFrequency);
                Vector2 offset = Random.insideUnitCircle * shakeIntensity;
                cameraShakeTarget.localPosition = originalLocalPosition + new Vector3(offset.x, offset.y, 0f);
            }

            yield return null;
        }

        cameraShakeTarget.localPosition = originalLocalPosition;
        cameraShakeRoutine = null;
    }
}