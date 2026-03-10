using UnityEngine;

/// <summary>
/// Animates a water texture by scrolling and optionally distorting UV offsets.
/// Works with URP (BaseMap instead of MainTex)
/// </summary>
[RequireComponent(typeof(Renderer))]
public class WaterTextureAnimator : MonoBehaviour
{
    [Header("Scroll Speed")]
    public float scrollSpeedX = 0.05f;
    public float scrollSpeedY = 0.03f;

    [Header("Wave Distortion")]
    public bool enableSecondaryWave = true;
    public float secondarySpeedX = -0.03f;
    public float secondarySpeedY = 0.02f;

    [Header("Tiling")]
    public Vector2 textureTiling = new Vector2(2f, 2f);

    private Material _mat;
    private float _offsetX;
    private float _offsetY;

    // URP uses _BaseMap
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

    void Start()
    {
        _mat = GetComponent<Renderer>().material;

        // Set tiling
        _mat.SetTextureScale(BaseMap, textureTiling);
    }

    void Update()
    {
        _offsetX += scrollSpeedX * Time.deltaTime;
        _offsetY += scrollSpeedY * Time.deltaTime;

        _offsetX = Mathf.Repeat(_offsetX, 1f);
        _offsetY = Mathf.Repeat(_offsetY, 1f);

        if (enableSecondaryWave)
        {
            float blendX = Mathf.Sin(Time.time * secondarySpeedX * Mathf.PI * 2f) * 0.05f;
            float blendY = Mathf.Cos(Time.time * secondarySpeedY * Mathf.PI * 2f) * 0.05f;

            _mat.SetTextureOffset(BaseMap, new Vector2(_offsetX + blendX, _offsetY + blendY));
        }
        else
        {
            _mat.SetTextureOffset(BaseMap, new Vector2(_offsetX, _offsetY));
        }
    }

    void OnDestroy()
    {
        if (_mat != null)
            Destroy(_mat);
    }
}