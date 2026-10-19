using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
internal sealed class EndpointPulse : MonoBehaviour
{
    [SerializeField, Min(1f)] private float popScale = 1.3f;
    [SerializeField, Min(0.02f)] private float popDuration = 0.3f;
    [SerializeField, Min(0f)] private float pauseAfterPop = 0.12f;

    private Vector3 baseScale;
    private Sequence heartbeatSequence;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        StartHeartbeat();
    }

    private void OnDisable()
    {
        StopHeartbeat();
    }

    private void OnDestroy()
    {
        StopHeartbeat();
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();

        heartbeatSequence = DOTween.Sequence()
            .Append(transform.DOScale(baseScale * popScale, popDuration).SetEase(Ease.OutQuad))
            .Append(transform.DOScale(baseScale, popDuration).SetEase(Ease.InQuad))
            .AppendInterval(pauseAfterPop)
            .SetLoops(-1, LoopType.Restart);
    }

    private void StopHeartbeat()
    {
        if (heartbeatSequence != null)
        {
            heartbeatSequence.Kill();
            heartbeatSequence = null;
        }

        transform.localScale = baseScale;
    }
}
