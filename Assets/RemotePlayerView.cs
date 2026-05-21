using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RemotePlayerView : MonoBehaviour
{
    [SerializeField] public string playerId;
    [SerializeField] public SpriteRenderer spriteRenderer;
    [SerializeField] public TMP_Text nameLabel;
    [SerializeField, Min(1f)] public float interpolationSpeed = 10f;
    [SerializeField] public Vector3 targetPosition;

    [SerializeField] Color normalColor = Color.white;
    [SerializeField] Color infectedColor = new Color(0.72f, 0.22f, 0.95f, 1f);

    bool hasTargetPosition;
    bool isAlive = true;
    bool isInfected;

    void Awake()
    {
        CacheComponents();
    }

    void OnEnable()
    {
        CacheComponents();
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        if (hasTargetPosition)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * interpolationSpeed);
        }

        UpdateVisuals(isInfected, isAlive);
    }

    public void Initialize(string id)
    {
        playerId = id;
        CacheComponents();

        if (nameLabel != null && string.IsNullOrWhiteSpace(nameLabel.text))
        {
            nameLabel.text = id;
        }
    }

    public void ApplyState(FirebasePlayerState state)
    {
        if (state == null)
        {
            return;
        }

        Initialize(state.playerId);
        SetTargetPosition(new Vector2(state.x, state.y));
        isAlive = state.alive;
        isInfected = state.infected;

        if (nameLabel != null)
        {
            nameLabel.text = string.IsNullOrWhiteSpace(state.displayName) ? state.playerId : state.displayName;
        }

        gameObject.SetActive(state.alive);
        UpdateVisuals(isInfected, isAlive);

        if (state.alive && !hasTargetPosition)
        {
            transform.position = targetPosition;
        }
    }

    public void SetTargetPosition(Vector2 pos)
    {
        targetPosition = new Vector3(pos.x, pos.y, transform.position.z);
        hasTargetPosition = true;
    }

    void CacheComponents()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (nameLabel == null)
        {
            nameLabel = GetComponentInChildren<TMP_Text>(true);
        }
    }

    void UpdateVisuals(bool infected, bool alive)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        if (!alive)
        {
            Color color = spriteRenderer.color;
            spriteRenderer.color = new Color(color.r, color.g, color.b, 0.2f);
            return;
        }

        spriteRenderer.color = infected ? infectedColor : normalColor;

        if (nameLabel != null)
        {
            nameLabel.color = infected ? infectedColor : Color.white;
        }
    }
}