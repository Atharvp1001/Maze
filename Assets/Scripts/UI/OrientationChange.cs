using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps one landscape canvas fitted across different screen resolutions.
/// </summary>
[DisallowMultipleComponent]
internal sealed class OrientationChange : MonoBehaviour
{
    [SerializeField] private CanvasScaler canvasScaler;

    private Vector2 referenceResolution;
    private int lastWidth;
    private int lastHeight;

    private void Awake()
    {
        if (canvasScaler == null)
        {
            canvasScaler = GetComponent<CanvasScaler>();
        }

        if (canvasScaler == null)
        {
            enabled = false;
            return;
        }

        referenceResolution = canvasScaler.referenceResolution;
    }

    private void Start()
    {
        ApplyMatch(Screen.width, Screen.height);
    }

    private void Update()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            ApplyMatch(Screen.width, Screen.height);
        }
    }

    private void ApplyMatch(int width, int height)
    {
        lastWidth = width;
        lastHeight = height;

        float widthScale = width / referenceResolution.x;
        float heightScale = height / referenceResolution.y;
        canvasScaler.matchWidthOrHeight = widthScale <= heightScale ? 0f : 1f;
    }
}
