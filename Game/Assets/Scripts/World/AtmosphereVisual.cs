using UnityEngine;

/// <summary>
/// 昼夜 × 天气共 12 种氛围：天空色、环境光、雨粒、雾效、晴空日月。
/// 清屏色跟随时段/天气，不再使用林地绿当天空。
/// </summary>
public class AtmosphereVisual : MonoBehaviour
{
    public static AtmosphereVisual Instance;

    private Transform root;
    private MeshRenderer skyRenderer;
    private Material skyMaterial;
    private Material particleMaterial;
    private Material fogMaterial;
    private Texture2D softFogTex;
    private Texture2D rainStreakTex;
    private ParticleSystem rainPs;
    private ParticleSystem fogPs;
    private SpriteRenderer celestialRenderer;
    private Sprite sunSprite;
    private Sprite moonSprite;
    private Color currentSky = new Color(0.45f, 0.7f, 0.95f);

    public Color CurrentSkyColor => currentSky;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (skyMaterial != null) Destroy(skyMaterial);
        if (particleMaterial != null) Destroy(particleMaterial);
        if (fogMaterial != null) Destroy(fogMaterial);
        if (softFogTex != null) Destroy(softFogTex);
        if (rainStreakTex != null) Destroy(rainStreakTex);
        if (sunSprite != null) Destroy(sunSprite.texture);
        if (moonSprite != null) Destroy(moonSprite.texture);
    }

    public void Build()
    {
        if (root != null)
            Destroy(root.gameObject);

        EnsureMaterials();
        root = new GameObject("Atmosphere").transform;
        root.SetParent(transform, false);

        BuildSkyDome();
        BuildRain();
        BuildSoftFog();
        BuildCelestial();
        Refresh();
    }

    public void Refresh()
    {
        if (root == null)
            Build();

        int round = TurnManager.Instance != null ? TurnManager.Instance.RoundNumber : 1;
        var period = GameClock.GetPeriod(round);
        var weather = WeatherService.Current;

        currentSky = GetSkyColor(period, weather);
        ApplySky(currentSky);
        BoardCameraController.Instance?.ApplySkyColor(currentSky);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = GetAmbient(period, weather);
        ApplyDistanceFog(period, weather);

        SetRainActive(weather == WeatherType.Rain, period);
        SetSoftFogActive(weather == WeatherType.Fog, period);
        UpdateCelestial(period, weather);
    }

    /// <summary>用引擎距离雾做柔和霾气，避免粒子白块。</summary>
    private static void ApplyDistanceFog(GameClock.Period period, WeatherType weather)
    {
        bool foggy = weather == WeatherType.Fog;
        bool night = period == GameClock.Period.Night;
        RenderSettings.fog = foggy || night || weather == WeatherType.Rain;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = GetFogColor(period, weather);

        if (foggy)
        {
            // 近景可读，远景（树林外）溶进天色
            switch (period)
            {
                case GameClock.Period.Night:
                    RenderSettings.fogStartDistance = 4f;
                    RenderSettings.fogEndDistance = 16f;
                    break;
                case GameClock.Period.Dawn:
                case GameClock.Period.Dusk:
                    RenderSettings.fogStartDistance = 6f;
                    RenderSettings.fogEndDistance = 22f;
                    break;
                default:
                    RenderSettings.fogStartDistance = 7f;
                    RenderSettings.fogEndDistance = 26f;
                    break;
            }
            return;
        }

        if (weather == WeatherType.Rain)
        {
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = night ? 28f : 36f;
            return;
        }

        // 晴夜轻微空气透视；晴昼几乎无雾
        if (night)
        {
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 40f;
        }
        else
        {
            RenderSettings.fog = false;
        }
    }

    public static Color GetSkyColor(GameClock.Period period, WeatherType weather)
    {
        // 绝不使用林地绿；晴/雨/雾各有区分
        switch (weather)
        {
            case WeatherType.Rain:
                switch (period)
                {
                    case GameClock.Period.Dawn: return new Color(0.55f, 0.58f, 0.68f);
                    case GameClock.Period.Day: return new Color(0.42f, 0.48f, 0.58f);
                    case GameClock.Period.Dusk: return new Color(0.38f, 0.36f, 0.48f);
                    default: return new Color(0.12f, 0.14f, 0.22f); // Night
                }
            case WeatherType.Fog:
                switch (period)
                {
                    case GameClock.Period.Dawn: return new Color(0.72f, 0.7f, 0.74f);
                    case GameClock.Period.Day: return new Color(0.68f, 0.72f, 0.76f);
                    case GameClock.Period.Dusk: return new Color(0.55f, 0.5f, 0.52f);
                    default: return new Color(0.22f, 0.24f, 0.3f);
                }
            default: // Clear
                switch (period)
                {
                    case GameClock.Period.Dawn: return new Color(0.95f, 0.72f, 0.58f);
                    case GameClock.Period.Day: return new Color(0.45f, 0.72f, 0.95f);
                    case GameClock.Period.Dusk: return new Color(0.85f, 0.42f, 0.28f);
                    default: return new Color(0.06f, 0.1f, 0.22f);
                }
        }
    }

    private static Color GetAmbient(GameClock.Period period, WeatherType weather)
    {
        var sky = GetSkyColor(period, weather);
        float dim = weather == WeatherType.Clear ? 0.55f : (weather == WeatherType.Rain ? 0.4f : 0.48f);
        if (period == GameClock.Period.Night)
            dim *= 0.55f;
        else if (period == GameClock.Period.Dusk || period == GameClock.Period.Dawn)
            dim *= 0.85f;
        return Color.Lerp(sky, Color.white, 0.15f) * dim;
    }

    private static Color GetFogColor(GameClock.Period period, WeatherType weather)
    {
        var sky = GetSkyColor(period, weather);
        if (weather == WeatherType.Fog)
            return Color.Lerp(sky, Color.white, 0.35f);
        if (period == GameClock.Period.Night)
            return Color.Lerp(sky, Color.black, 0.35f);
        return sky;
    }

    private void EnsureMaterials()
    {
        if (skyMaterial == null)
        {
            var shader = Shader.Find("Unlit/Color")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Standard");
            skyMaterial = new Material(shader) { name = "AtmosphereSky" };
        }

        // 雨丝：专用粒子透明材质（Sprites/Default 挂在 ParticleSystem 上经常几乎看不见）
        if (rainStreakTex != null)
            Destroy(rainStreakTex);
        rainStreakTex = CreateRainStreakTexture(4, 48);
        if (particleMaterial != null)
            Destroy(particleMaterial);
        {
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default");
            particleMaterial = new Material(shader) { name = "AtmosphereRain" };
            particleMaterial.mainTexture = rainStreakTex;
            if (particleMaterial.HasProperty("_MainTex"))
                particleMaterial.SetTexture("_MainTex", rainStreakTex);
            if (particleMaterial.HasProperty("_BaseMap"))
                particleMaterial.SetTexture("_BaseMap", rainStreakTex);
            if (particleMaterial.HasProperty("_Mode"))
                particleMaterial.SetFloat("_Mode", 2f); // Fade
            if (particleMaterial.HasProperty("_Color"))
                particleMaterial.SetColor("_Color", Color.white);
            if (particleMaterial.HasProperty("_BaseColor"))
                particleMaterial.SetColor("_BaseColor", Color.white);
            particleMaterial.color = Color.white;
            particleMaterial.renderQueue = 3200; // 略高于场景透明物，避免被雾面吃掉
        }

        if (fogMaterial == null)
        {
            if (softFogTex == null)
                softFogTex = CreateSoftFogTexture(128);
            // Sprites/Default 可靠吃 alpha；默认粒子材质常把柔边渲成白方块
            var shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            fogMaterial = new Material(shader) { name = "AtmosphereSoftFog" };
            fogMaterial.mainTexture = softFogTex;
            if (fogMaterial.HasProperty("_MainTex"))
                fogMaterial.SetTexture("_MainTex", softFogTex);
            if (fogMaterial.HasProperty("_BaseMap"))
                fogMaterial.SetTexture("_BaseMap", softFogTex);
            fogMaterial.color = Color.white;
        }
    }

    private static Texture2D CreateSoftFogTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float cx = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - cx) / cx;
                float ny = (y - cx) / cx;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                // 中心淡、边缘彻底透明，避免硬边方块
                float a = Mathf.Clamp01(1f - d);
                a = a * a * 0.55f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    private void ApplySky(Color sky)
    {
        if (skyMaterial == null)
            return;
        if (skyMaterial.HasProperty("_BaseColor"))
            skyMaterial.SetColor("_BaseColor", sky);
        if (skyMaterial.HasProperty("_Color"))
            skyMaterial.SetColor("_Color", sky);
        skyMaterial.color = sky;
        if (skyRenderer != null)
            skyRenderer.sharedMaterial = skyMaterial;
    }

    private void BuildSkyDome()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "SkyDome";
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * 90f;
        var col = go.GetComponent<Collider>();
        if (col != null)
            Destroy(col);
        skyRenderer = go.GetComponent<MeshRenderer>();
        skyRenderer.sharedMaterial = skyMaterial;
        skyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        skyRenderer.receiveShadows = false;
        // 只看内表面
        skyRenderer.transform.localScale = new Vector3(-90f, 90f, 90f);
    }

    /// <summary>极细雨丝：中心一条发丝，两侧几乎透明。</summary>
    private static Texture2D CreateRainStreakTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float cx = (w - 1) * 0.5f;
        for (int y = 0; y < h; y++)
        {
            float fy = y / (float)(h - 1);
            float tip = Mathf.SmoothStep(0f, 1f, fy * 4f) * Mathf.SmoothStep(0f, 1f, (1f - fy) * 3f);
            for (int x = 0; x < w; x++)
            {
                float nx = Mathf.Abs(x - cx) / Mathf.Max(0.35f, cx);
                // 只保留中缝，两侧迅速掉到 0
                float edge = Mathf.Exp(-nx * nx * 9f);
                float a = tip * edge * 0.95f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, false);
        return tex;
    }

    private void BuildRain()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(root, false);
        rainPs = go.AddComponent<ParticleSystem>();
        var main = rainPs.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
        main.startSpeed = 0f;
        main.startSize3D = false;
        // 介于「粉笔道」与「看不见」之间：细、但屏幕上成线
        main.startSize = new ParticleSystem.MinMaxCurve(0.016f, 0.026f);
        main.startColor = new Color(0.88f, 0.92f, 0.98f, 0.7f);
        main.maxParticles = 5500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = rainPs.emission;
        emission.rateOverTime = 0f;

        var shape = rainPs.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(12f, 0.5f, 10f);

        var vel = rainPs.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-2f, -0.8f);
        vel.y = new ParticleSystem.MinMaxCurve(-16f, -11f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        var col = rainPs.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.06f),
                new GradientAlphaKey(0.9f, 0.8f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.6f;
        renderer.velocityScale = 0.05f;
        renderer.cameraVelocityScale = 0f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingFudge = -50f;
        renderer.allowRoll = false;

        rainPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void BuildSoftFog()
    {
        var go = new GameObject("SoftFog");
        go.transform.SetParent(root, false);
        fogPs = go.AddComponent<ParticleSystem>();

        var main = fogPs.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startColor = new Color(0.92f, 0.94f, 0.96f, 0.22f);
        main.maxParticles = 36;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var emission = fogPs.emission;
        emission.rateOverTime = 0f;

        var shape = fogPs.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(26f, 3f, 26f);

        var col = fogPs.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(0.85f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sz = fogPs.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.75f, 1f, 1.15f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = fogMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = 10f;

        fogPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void SetSoftFogActive(bool on, GameClock.Period period)
    {
        if (fogPs == null)
            return;
        var emission = fogPs.emission;
        if (!on)
        {
            emission.rateOverTime = 0f;
            if (fogPs.isPlaying)
                fogPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        var main = fogPs.main;
        switch (period)
        {
            case GameClock.Period.Night:
                main.startColor = new Color(0.4f, 0.45f, 0.55f, 0.2f);
                emission.rateOverTime = 4f;
                break;
            case GameClock.Period.Dusk:
                main.startColor = new Color(0.9f, 0.78f, 0.72f, 0.2f);
                emission.rateOverTime = 5f;
                break;
            case GameClock.Period.Dawn:
                main.startColor = new Color(0.95f, 0.9f, 0.88f, 0.2f);
                emission.rateOverTime = 5f;
                break;
            default:
                main.startColor = new Color(0.93f, 0.95f, 0.97f, 0.18f);
                emission.rateOverTime = 5.5f;
                break;
        }

        if (!fogPs.isPlaying)
            fogPs.Play();
    }

    private void BuildCelestial()
    {
        sunSprite = CreateOrbSprite(new Color(1f, 0.92f, 0.55f), new Color(1f, 0.7f, 0.25f));
        moonSprite = CreateOrbSprite(new Color(0.85f, 0.9f, 1f), new Color(0.45f, 0.55f, 0.75f));

        var go = new GameObject("Celestial");
        go.transform.SetParent(root, false);
        celestialRenderer = go.AddComponent<SpriteRenderer>();
        celestialRenderer.sprite = sunSprite;
        celestialRenderer.sortingOrder = -50;
        go.AddComponent<CameraBillboard>();
        go.SetActive(false);
    }

    private static Sprite CreateOrbSprite(Color core, Color rim)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float cx = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cx)) / (size * 0.5f);
                if (d > 1f)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }
                float a = d < 0.55f ? 1f : Mathf.Clamp01(1f - (d - 0.55f) / 0.45f);
                var c = Color.Lerp(core, rim, d);
                c.a = a * (d < 0.7f ? 1f : a);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
    }

    private void SetRainActive(bool on, GameClock.Period period)
    {
        if (rainPs == null)
            return;
        var emission = rainPs.emission;
        if (!on)
        {
            emission.rateOverTime = 0f;
            if (rainPs.isPlaying)
                rainPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        var main = rainPs.main;
        var vel = rainPs.velocityOverLifetime;
        var renderer = rainPs.GetComponent<ParticleSystemRenderer>();
        ApplyRainStyleForPeriod(period, main, emission, vel, renderer);

        if (!rainPs.isPlaying)
            rainPs.Play();
    }

    /// <summary>四时段细雨：清晰可见但不至于粗白条；密度/色调随时段略变。</summary>
    private static void ApplyRainStyleForPeriod(
        GameClock.Period period,
        ParticleSystem.MainModule main,
        ParticleSystem.EmissionModule emission,
        ParticleSystem.VelocityOverLifetimeModule vel,
        ParticleSystemRenderer renderer)
    {
        main.startSize3D = false;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.016f, 0.026f);
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2.6f;
            renderer.velocityScale = 0.05f;
        }

        switch (period)
        {
            case GameClock.Period.Dawn:
                main.startColor = new Color(0.9f, 0.92f, 0.96f, 0.68f);
                emission.rateOverTime = 3800f;
                vel.x = new ParticleSystem.MinMaxCurve(-2.2f, -0.9f);
                vel.y = new ParticleSystem.MinMaxCurve(-15f, -10f);
                break;
            case GameClock.Period.Dusk:
                main.startColor = new Color(0.82f, 0.76f, 0.88f, 0.72f);
                emission.rateOverTime = 4200f;
                vel.x = new ParticleSystem.MinMaxCurve(-2f, -0.7f);
                vel.y = new ParticleSystem.MinMaxCurve(-17f, -11f);
                if (renderer != null) { renderer.lengthScale = 2.8f; renderer.velocityScale = 0.055f; }
                break;
            case GameClock.Period.Night:
                main.startColor = new Color(0.75f, 0.8f, 0.95f, 0.78f);
                emission.rateOverTime = 3400f;
                vel.x = new ParticleSystem.MinMaxCurve(-1.6f, -0.5f);
                vel.y = new ParticleSystem.MinMaxCurve(-18f, -12f);
                if (renderer != null) { renderer.lengthScale = 3f; renderer.velocityScale = 0.058f; }
                break;
            default: // Day
                main.startColor = new Color(0.88f, 0.92f, 0.99f, 0.7f);
                emission.rateOverTime = 4600f;
                vel.x = new ParticleSystem.MinMaxCurve(-1.8f, -0.6f);
                vel.y = new ParticleSystem.MinMaxCurve(-16f, -11f);
                break;
        }
        vel.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
    }

    private void UpdateCelestial(GameClock.Period period, WeatherType weather)
    {
        if (celestialRenderer == null)
            return;

        bool showSun = weather == WeatherType.Clear
            && (period == GameClock.Period.Dawn || period == GameClock.Period.Day || period == GameClock.Period.Dusk);
        bool showMoon = weather == WeatherType.Clear && period == GameClock.Period.Night;

        if (!showSun && !showMoon)
        {
            celestialRenderer.gameObject.SetActive(false);
            return;
        }

        celestialRenderer.gameObject.SetActive(true);
        celestialRenderer.sprite = showMoon ? moonSprite : sunSprite;
        float scale = showMoon ? 2.2f : (period == GameClock.Period.Dawn || period == GameClock.Period.Dusk ? 2.8f : 2.4f);
        celestialRenderer.transform.localScale = Vector3.one * scale;

        var grid = GridManager.Instance;
        float cx = grid != null ? grid.gridWidth * grid.cellSize * 0.5f : 9f;
        float cz = grid != null ? grid.gridHeight * grid.cellSize * 0.5f : 9f;
        // 天空位置：清晨偏东，白天偏顶，黄昏偏西，夜晚偏顶
        Vector3 pos;
        switch (period)
        {
            case GameClock.Period.Dawn:
                pos = new Vector3(cx + 18f, 14f, cz + 6f);
                celestialRenderer.color = new Color(1f, 0.75f, 0.45f, 0.95f);
                break;
            case GameClock.Period.Dusk:
                pos = new Vector3(cx - 18f, 12f, cz + 4f);
                celestialRenderer.color = new Color(1f, 0.45f, 0.25f, 0.95f);
                break;
            case GameClock.Period.Night:
                pos = new Vector3(cx - 8f, 16f, cz + 14f);
                celestialRenderer.color = new Color(0.85f, 0.9f, 1f, 0.9f);
                break;
            default:
                pos = new Vector3(cx + 6f, 18f, cz + 10f);
                celestialRenderer.color = Color.white;
                break;
        }
        celestialRenderer.transform.position = pos;
    }

    private void LateUpdate()
    {
        if (root == null || Camera.main == null)
            return;

        // 天空罩跟随相机；雨幕落在棋盘上方
        var cam = Camera.main.transform;
        if (skyRenderer != null)
            skyRenderer.transform.position = cam.position;

        var grid = GridManager.Instance;
        Vector3 boardCenter = grid != null
            ? grid.originPosition + new Vector3(
                grid.gridWidth * grid.cellSize * 0.5f,
                12f,
                grid.gridHeight * grid.cellSize * 0.5f)
            : cam.position + Vector3.up * 10f;

        if (rainPs != null)
        {
            // 贴在镜头前方中上部生成，下落穿过视野（第三人称近距也能看见）
            Vector3 flatFwd = cam.forward;
            flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude < 0.001f)
                flatFwd = Vector3.forward;
            flatFwd.Normalize();
            rainPs.transform.position = cam.position + flatFwd * 3.5f + Vector3.up * 5.5f;
        }
        if (fogPs != null)
            fogPs.transform.position = boardCenter + Vector3.down * 7f;
    }
}
