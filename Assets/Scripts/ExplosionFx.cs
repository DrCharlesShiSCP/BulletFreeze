using System.Collections.Generic;
using UnityEngine;

// Simple procedural explosion effect that builds its own visuals at runtime.
public class ExplosionFx : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("How long the explosion remains alive before cleaning itself up.")]
    [SerializeField] private float duration = 1.15f;

    [Header("Colors")]
    [SerializeField] private Color flashColor = new Color(1f, 0.92f, 0.65f, 1f);
    [SerializeField] private Color coreColor = new Color(1f, 0.48f, 0.12f, 1f);
    [SerializeField] private Color shockwaveColor = new Color(1f, 0.74f, 0.24f, 0.85f);
    [SerializeField] private Color sparkColor = new Color(1f, 0.62f, 0.2f, 1f);
    [SerializeField] private Color smokeColor = new Color(0.18f, 0.18f, 0.2f, 0.75f);

    [Header("Scale")]
    [SerializeField] private float flashMaxScale = 3.8f;
    [SerializeField] private float coreMaxScale = 2.2f;
    [SerializeField] private float shockwaveMaxScale = 5.4f;
    [SerializeField] private float smokeMaxScale = 3.2f;

    [Header("Light")]
    [SerializeField] private float lightIntensity = 12f;
    [SerializeField] private float lightRange = 12f;

    private readonly List<FxPiece> pieces = new List<FxPiece>();
    private readonly List<Material> runtimeMaterials = new List<Material>();

    private Light explosionLight;
    private float elapsed;

    private void OnEnable()
    {
        BuildIfNeeded();
        ResetFx();
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));

        UpdatePieces(normalizedTime);
        UpdateLight(normalizedTime);

        if (elapsed >= duration)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < runtimeMaterials.Count; i++)
        {
            if (runtimeMaterials[i] != null)
                Destroy(runtimeMaterials[i]);
        }
    }

    private void BuildIfNeeded()
    {
        if (pieces.Count > 0)
            return;

        CreateFlash();
        CreateCore();
        CreateShockwave();
        CreateSmoke();
        CreateSparks(10);
        CreateExplosionLight();
    }

    private void ResetFx()
    {
        elapsed = 0f;

        for (int i = 0; i < pieces.Count; i++)
        {
            FxPiece piece = pieces[i];
            if (piece.transform == null)
                continue;

            piece.transform.localPosition = piece.startLocalPosition;
            piece.transform.localRotation = piece.startLocalRotation;
            piece.transform.localScale = piece.startScale;

            if (piece.renderer != null)
                ApplyColor(piece.renderer, piece.color, 1f);
        }

        if (explosionLight != null)
        {
            explosionLight.enabled = true;
            explosionLight.intensity = lightIntensity;
        }
    }

    private void UpdatePieces(float normalizedTime)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            FxPiece piece = pieces[i];
            if (piece.transform == null)
                continue;

            float lifeT = Mathf.InverseLerp(piece.startTime, piece.endTime, normalizedTime);
            lifeT = Mathf.Clamp01(lifeT);

            piece.transform.localScale = Vector3.Lerp(piece.startScale, piece.endScale, lifeT);
            piece.transform.localPosition = piece.startLocalPosition + piece.velocity * lifeT;
            piece.transform.localRotation = piece.startLocalRotation * Quaternion.Euler(piece.angularVelocity * elapsed);

            if (piece.renderer != null)
            {
                float alpha = 1f - Mathf.Clamp01(Mathf.InverseLerp(piece.fadeStart, piece.fadeEnd, normalizedTime));
                ApplyColor(piece.renderer, piece.color, alpha);
            }
        }
    }

    private void UpdateLight(float normalizedTime)
    {
        if (explosionLight == null)
            return;

        float intensityT = 1f - Mathf.Clamp01(normalizedTime * 1.25f);
        explosionLight.intensity = lightIntensity * intensityT;
        explosionLight.range = Mathf.Lerp(lightRange, lightRange * 0.35f, normalizedTime);

        if (explosionLight.intensity <= 0.01f)
            explosionLight.enabled = false;
    }

    private void CreateFlash()
    {
        GameObject flash = CreatePrimitiveChild("Flash", PrimitiveType.Sphere, flashColor);
        flash.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        pieces.Add(new FxPiece
        {
            transform = flash.transform,
            renderer = flash.GetComponent<Renderer>(),
            startScale = Vector3.one * 0.25f,
            endScale = Vector3.one * flashMaxScale,
            startLocalPosition = flash.transform.localPosition,
            velocity = new Vector3(0f, 0.35f, 0f),
            color = flashColor,
            startTime = 0f,
            endTime = 0.22f,
            fadeStart = 0.03f,
            fadeEnd = 0.22f
        });
    }

    private void CreateCore()
    {
        GameObject core = CreatePrimitiveChild("Core", PrimitiveType.Sphere, coreColor);
        core.transform.localPosition = new Vector3(0f, 0.18f, 0f);

        pieces.Add(new FxPiece
        {
            transform = core.transform,
            renderer = core.GetComponent<Renderer>(),
            startScale = Vector3.one * 0.35f,
            endScale = Vector3.one * coreMaxScale,
            startLocalPosition = core.transform.localPosition,
            velocity = new Vector3(0f, 1.1f, 0f),
            color = coreColor,
            startTime = 0f,
            endTime = 0.4f,
            fadeStart = 0.08f,
            fadeEnd = 0.42f
        });
    }

    private void CreateShockwave()
    {
        GameObject shockwave = CreatePrimitiveChild("Shockwave", PrimitiveType.Cylinder, shockwaveColor);
        shockwave.transform.localPosition = new Vector3(0f, 0.05f, 0f);

        pieces.Add(new FxPiece
        {
            transform = shockwave.transform,
            renderer = shockwave.GetComponent<Renderer>(),
            startScale = new Vector3(0.35f, 0.04f, 0.35f),
            endScale = new Vector3(shockwaveMaxScale, 0.02f, shockwaveMaxScale),
            startLocalPosition = shockwave.transform.localPosition,
            velocity = Vector3.zero,
            color = shockwaveColor,
            startTime = 0.02f,
            endTime = 0.55f,
            fadeStart = 0.12f,
            fadeEnd = 0.58f
        });
    }

    private void CreateSmoke()
    {
        GameObject smoke = CreatePrimitiveChild("Smoke", PrimitiveType.Sphere, smokeColor);
        smoke.transform.localPosition = new Vector3(0f, 0.45f, 0f);

        pieces.Add(new FxPiece
        {
            transform = smoke.transform,
            renderer = smoke.GetComponent<Renderer>(),
            startScale = Vector3.one * 0.7f,
            endScale = Vector3.one * smokeMaxScale,
            startLocalPosition = smoke.transform.localPosition,
            velocity = new Vector3(0f, 2.8f, 0f),
            color = smokeColor,
            startTime = 0.08f,
            endTime = 1f,
            fadeStart = 0.28f,
            fadeEnd = 1f
        });
    }

    private void CreateSparks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject spark = CreatePrimitiveChild($"Spark_{i + 1}", PrimitiveType.Capsule, sparkColor);
            float angle = (360f / count) * i;
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
            Vector3 direction = rotation * new Vector3(0f, 0.45f, 1f).normalized;

            spark.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            spark.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            pieces.Add(new FxPiece
            {
                transform = spark.transform,
                renderer = spark.GetComponent<Renderer>(),
                startScale = new Vector3(0.08f, 0.22f, 0.08f),
                endScale = new Vector3(0.025f, 0.52f, 0.025f),
                startLocalPosition = spark.transform.localPosition,
                startLocalRotation = spark.transform.localRotation,
                velocity = direction * Random.Range(2.6f, 4.2f),
                angularVelocity = new Vector3(Random.Range(-160f, 160f), Random.Range(-160f, 160f), 0f),
                color = sparkColor,
                startTime = 0f,
                endTime = 0.55f,
                fadeStart = 0.08f,
                fadeEnd = 0.55f
            });
        }
    }

    private void CreateExplosionLight()
    {
        GameObject lightObject = new GameObject("ExplosionLight");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 1f, 0f);

        explosionLight = lightObject.AddComponent<Light>();
        explosionLight.type = LightType.Point;
        explosionLight.color = coreColor;
        explosionLight.intensity = lightIntensity;
        explosionLight.range = lightRange;
        explosionLight.shadows = LightShadows.None;
    }

    private GameObject CreatePrimitiveChild(string childName, PrimitiveType primitiveType, Color color)
    {
        GameObject child = GameObject.CreatePrimitive(primitiveType);
        child.name = childName;
        child.transform.SetParent(transform, false);

        Collider collider = child.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        Renderer rendererComponent = child.GetComponent<Renderer>();
        if (rendererComponent != null)
        {
            Material runtimeMaterial = CreateRuntimeMaterial(color);
            if (runtimeMaterial != null)
                rendererComponent.sharedMaterial = runtimeMaterial;
        }

        return child;
    }

    private Material CreateRuntimeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("[ExplosionFx] Could not find a compatible shader for the explosion material.");
            return null;
        }

        Material material = new Material(shader);
        runtimeMaterials.Add(material);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        return material;
    }

    private static void ApplyColor(Renderer targetRenderer, Color color, float alpha)
    {
        if (targetRenderer == null || targetRenderer.sharedMaterial == null)
            return;

        Color tintedColor = color;
        tintedColor.a = alpha;

        if (targetRenderer.sharedMaterial.HasProperty("_BaseColor"))
            targetRenderer.sharedMaterial.SetColor("_BaseColor", tintedColor);

        if (targetRenderer.sharedMaterial.HasProperty("_Color"))
            targetRenderer.sharedMaterial.SetColor("_Color", tintedColor);
    }

    private class FxPiece
    {
        public Transform transform;
        public Renderer renderer;
        public Vector3 startScale;
        public Vector3 endScale;
        public Vector3 startLocalPosition;
        public Quaternion startLocalRotation = Quaternion.identity;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public Color color;
        public float startTime;
        public float endTime = 1f;
        public float fadeStart;
        public float fadeEnd = 1f;
    }
}
