using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class CameraFollow : MonoBehaviour
{
	[Header("Target")]
	[Tooltip("Transform the camera will follow. Leave empty to disable following.")]
	public Transform target;

	[Header("Offset")]
	[Tooltip("Local offset from the target position.")]
	public Vector3 offset = new Vector3(0f, 1f, 0f);

	[Header("Smoothing")]
	[Tooltip("Smooth time used by Vector3.SmoothDamp. Lower values = snappier follow.")]
	[Min(0.001f)]
	public float smoothTime = 0.18f;

	[Tooltip("Optional maximum speed for smoothing. Leave as Mathf.Infinity for no limit.")]
	public float maxSpeed = Mathf.Infinity;

	[Header("Look Ahead")]
	[Tooltip("Enable a small look-ahead in the direction the player is moving.")]
	public bool enableLookAhead = true;

	[Tooltip("Maximum distance of look-ahead from the target based on movement direction.")]
	public float lookAheadDistance = 1.2f;

	[Tooltip("How quickly the look-ahead target smooths. Higher = slower follow of look-ahead.")]
	[Min(0f)]
	public float lookAheadSmoothTime = 0.18f;

	[Tooltip("Velocity magnitude threshold below which look-ahead is ignored to avoid jitter.")]
	public float velocityThreshold = 0.05f;

	Vector3 velocity;
	Vector3 lookAheadVelocity;
	Vector3 currentLookAhead;

	Vector3 previousTargetPosition;
	const float FixedZ = -10f;

	Rigidbody2D targetRigidbody;

	void Awake()
	{
		var p = transform.position;
		p.z = FixedZ;
		transform.position = p;
	}

	void Start()
	{
		velocity = Vector3.zero;
		lookAheadVelocity = Vector3.zero;
		currentLookAhead = Vector3.zero;
		if (target != null)
		{
			previousTargetPosition = target.position;
			targetRigidbody = target.GetComponent<Rigidbody2D>();
		}
	}

	void LateUpdate()
	{
		if (target == null) return;

		// Update cached Rigidbody if target changed or not yet cached
		if (targetRigidbody == null) targetRigidbody = target.GetComponent<Rigidbody2D>();

		// Compute a stable velocity for look-ahead without relying on input axes.
		Vector3 worldVelocity;
		if (targetRigidbody != null)
		{
			worldVelocity = new Vector3(targetRigidbody.linearVelocity.x, targetRigidbody.linearVelocity.y, 0f);
		}
		else
		{
			// Fallback: derive velocity from position delta. Use Time.unscaledDeltaTime to avoid time scale issues.
			float dt = Time.deltaTime;
			if (dt > 0f) worldVelocity = (target.position - previousTargetPosition) / dt;
			else worldVelocity = Vector3.zero;
		}

		// Determine desired look-ahead based on velocity magnitude and direction
		Vector3 desiredLookAhead = Vector3.zero;
		if (enableLookAhead && worldVelocity.sqrMagnitude > velocityThreshold * velocityThreshold)
		{
			Vector3 dir = worldVelocity.normalized;
			desiredLookAhead = dir * lookAheadDistance;
		}

		// Smooth the look-ahead target to avoid jitter on small joystick inputs
		currentLookAhead = Vector3.SmoothDamp(currentLookAhead, desiredLookAhead, ref lookAheadVelocity, lookAheadSmoothTime, Mathf.Infinity, Time.deltaTime);

		// Final target position with offset and look-ahead
		Vector3 targetPos = target.position + offset + currentLookAhead;
		targetPos.z = FixedZ;

		// Smoothly move camera towards final target. This creates the slight lag and cinematic feel.
		transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, smoothTime, maxSpeed, Time.deltaTime);

		// Cache for next frame
		previousTargetPosition = target.position;
	}

	void OnValidate()
	{
		if (smoothTime < 0.001f) smoothTime = 0.001f;
		if (lookAheadSmoothTime < 0f) lookAheadSmoothTime = 0f;
		if (lookAheadDistance < 0f) lookAheadDistance = 0f;
		if (velocityThreshold < 0f) velocityThreshold = 0f;
	}
}
