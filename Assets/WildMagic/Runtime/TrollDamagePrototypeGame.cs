using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype
{
    /// <summary>
    /// Small 3D proving ground for nested creature layers and detachable segments.
    /// </summary>
    public sealed class TrollDamagePrototypeGame : MonoBehaviour
    {
        private sealed class Projectile
        {
            public GameObject Visual = null!;
            public LayeredDamageSegment Target = null!;
            public Vector3 Start;
            public Vector3 TargetLocalPoint;
            public float StartedAt;
        }

        private readonly List<Projectile> projectiles = new();
        private readonly List<Material> generatedMaterials = new();
        private readonly List<LayeredDamageSegment> segments = new();
        private readonly RaycastHit[] raycastHits = new RaycastHit[24];

        private Camera sceneCamera = null!;
        private Material emberMaterial = null!;
        private ParticleSystem emberParticles = null!;
        private ParticleSystem bloodParticles = null!;
        private Font? uiFont;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? statusStyle;
        private float nextShotTime;
        private float restartEnabledAt;
        private bool restartInProgress;
        private Bounds trollBounds;
        private string status = "Наведите курсор на часть тела и нажмите ЛКМ";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "TrollDamagePrototype")
                return;
            if (FindFirstObjectByType<TrollDamagePrototypeGame>() != null)
                return;

            new GameObject("[Troll Layer Damage Prototype]")
                .AddComponent<TrollDamagePrototypeGame>();
        }

        private void Awake()
        {
            restartEnabledAt = Time.unscaledTime + 0.3f;
            BuildScene();
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            if (!restartInProgress
                && keyboard != null
                && keyboard.rKey.wasPressedThisFrame
                && Time.unscaledTime >= restartEnabledAt)
            {
                restartInProgress = true;
                status = "Перезапуск теста...";
                SceneManager.LoadSceneAsync("TrollDamagePrototype", LoadSceneMode.Single);
                enabled = false;
                return;
            }

            Mouse? mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Time.time >= nextShotTime)
            {
                TryShoot(mouse.position.ReadValue());
            }

            UpdateProjectiles();
        }

        private void OnDestroy()
        {
            foreach (Projectile projectile in projectiles)
            {
                if (projectile.Visual != null)
                    Destroy(projectile.Visual);
            }

            foreach (Material material in generatedMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }

        private void BuildScene()
        {
            foreach (Camera existingCamera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                existingCamera.gameObject.SetActive(false);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.28f, 0.34f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.13f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.04f, 0.05f);
            Physics.gravity = new Vector3(0f, -9.81f, 0f);

            GameObject cameraObject = new("Prototype Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            sceneCamera = cameraObject.AddComponent<Camera>();
            sceneCamera.fieldOfView = 38f;
            sceneCamera.nearClipPlane = 0.05f;
            sceneCamera.farClipPlane = 50f;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(0.018f, 0.024f, 0.032f);
            cameraObject.AddComponent<AudioListener>();
            sceneCamera.GetUniversalAdditionalCameraData().SetRenderer(1);

            CreateLighting();
            CreateEnvironment();
            CreateLayeredTroll();

            Vector3 target = trollBounds.center + new Vector3(0f, 0.05f, 0f);
            sceneCamera.transform.position = target + new Vector3(0.25f, 0.15f, -8.4f);
            sceneCamera.transform.LookAt(target);

            uiFont = Resources.Load<Font>("RobotoMono-Regular");
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void CreateLayeredTroll()
        {
            GameObject trollPrefab = Resources.Load<GameObject>("Models/ModularTrollLayers");
            if (trollPrefab == null)
            {
                Debug.LogError("ModularTrollLayers model is missing from Resources/Models.");
                status = "Не найдена модель ModularTrollLayers";
                return;
            }

            GameObject trollRoot = Instantiate(trollPrefab);
            trollRoot.name = "Modular Troll — Layered Creature";
            trollRoot.transform.SetParent(transform, false);
            // The recovered Hunyuan sculpt is authored at roughly 1.84 units tall.
            // Keep the arena presentation close to the previous prototype size while
            // preserving its native proportions and a sensible wound-to-body ratio.
            trollRoot.transform.localScale = Vector3.one * 2.05f;
            trollRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            string[] segmentNames = { "Torso", "Head", "Arm_L", "Arm_R", "Leg_L", "Leg_R" };
            string[] displayNames = { "корпус", "голова", "левая рука", "правая рука", "левая нога", "правая нога" };
            Shader damageShader = Shader.Find("WildMagic/LayeredDamageCutout");

            bool hasBounds = false;
            for (int index = 0; index < segmentNames.Length; index++)
            {
                string segmentName = segmentNames[index];
                string displayName = displayNames[index];
                Renderer skin = FindRenderer(trollRoot, $"{segmentName}_Skin");
                Renderer flesh = FindRenderer(trollRoot, $"{segmentName}_Flesh");
                Renderer bone = FindRenderer(trollRoot, $"{segmentName}_Bone");

                GameObject segmentRoot = new($"{segmentName} — Damage Segment");
                segmentRoot.transform.SetParent(trollRoot.transform, false);
                skin.transform.SetParent(segmentRoot.transform, true);
                flesh.transform.SetParent(segmentRoot.transform, true);
                bone.transform.SetParent(segmentRoot.transform, true);

                LayeredDamageSegment damageSegment = segmentRoot.AddComponent<LayeredDamageSegment>();
                damageSegment.Initialize(
                    skin,
                    flesh,
                    bone,
                    CreateDamageMaterial(
                        damageShader,
                        $"{segmentName} Skin",
                        new Color(0.25f, 0.48f, 0.20f),
                        new Color(0.20f, 0.022f, 0.012f),
                        0.004f),
                    CreateDamageMaterial(
                        damageShader,
                        $"{segmentName} Flesh",
                        new Color(0.63f, 0.045f, 0.035f),
                        new Color(0.34f, 0.018f, 0.012f),
                        0.003f),
                    CreateDamageMaterial(
                        damageShader,
                        $"{segmentName} Bone",
                        new Color(0.78f, 0.68f, 0.48f),
                        new Color(0.28f, 0.18f, 0.08f),
                        0.0015f));
                damageSegment.StatusChanged += value => status = $"{displayName}: {value}";
                segments.Add(damageSegment);

                if (!hasBounds)
                {
                    trollBounds = skin.bounds;
                    hasBounds = true;
                }
                else
                {
                    trollBounds.Encapsulate(skin.bounds);
                }
            }

            Vector3 alignment = new(-trollBounds.center.x, 0.08f - trollBounds.min.y, -trollBounds.center.z);
            trollRoot.transform.position += alignment;
            trollBounds.center += alignment;

            emberMaterial = CreateSimpleMaterial("Impact Ember", new Color(1f, 0.27f, 0.035f), true);
            Material emberParticleMaterial = CreateParticleMaterial(
                "Ember Particle Material",
                new Color(1f, 0.20f, 0.025f));
            Material bloodParticleMaterial = CreateParticleMaterial(
                "Blood Particle Material",
                new Color(0.72f, 0.015f, 0.025f));
            emberParticles = CreateImpactParticleSystem(
                "Pooled Ember Particles",
                emberParticleMaterial,
                1.15f);
            bloodParticles = CreateImpactParticleSystem(
                "Pooled Blood Particles",
                bloodParticleMaterial,
                3.2f);
        }

        private void CreateLighting()
        {
            GameObject keyObject = new("Key Light");
            keyObject.transform.SetParent(transform, false);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.91f, 0.78f);
            key.intensity = 1.55f;
            key.shadows = LightShadows.Soft;
            keyObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

            GameObject rimObject = new("Cold Rim Light");
            rimObject.transform.SetParent(transform, false);
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(0.24f, 0.55f, 1f);
            rim.intensity = 5.5f;
            rim.range = 8f;
            rimObject.transform.position = new Vector3(2.4f, 2.7f, 1.8f);

            GameObject fillObject = new("Front Fill Light");
            fillObject.transform.SetParent(transform, false);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.72f, 0.86f, 1f);
            fill.intensity = 2.3f;
            fill.range = 10f;
            fill.shadows = LightShadows.None;
            fillObject.transform.position = new Vector3(-0.8f, 2.6f, -4.5f);
        }

        private void CreateEnvironment()
        {
            CreateCube(
                "Floor",
                new Vector3(0f, -0.14f, 0f),
                new Vector3(10f, 0.25f, 7f),
                new Color(0.055f, 0.07f, 0.085f));
            CreateCube(
                "Back Wall",
                new Vector3(0f, 2.5f, 1.9f),
                new Vector3(10f, 5f, 0.18f),
                new Color(0.04f, 0.052f, 0.068f));
        }

        private GameObject CreateCube(string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(transform, true);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = CreateSimpleMaterial($"{name} Material", color);
            return cube;
        }

        private Material CreateDamageMaterial(
            Shader shader,
            string name,
            Color baseColor,
            Color burnColor,
            float burnWidth)
        {
            Material material = new(shader)
            {
                name = name
            };
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_BurnColor", burnColor);
            material.SetFloat("_BurnWidth", burnWidth);
            material.SetFloat("_Smoothness", 0.1f);
            generatedMaterials.Add(material);
            return material;
        }

        private Material CreateSimpleMaterial(string name, Color color, bool emissive = false)
        {
            Shader shader = Shader.Find(emissive
                ? "Universal Render Pipeline/Unlit"
                : "Universal Render Pipeline/Lit");
            Material material = new(shader)
            {
                name = name,
                color = color
            };
            material.SetColor("_BaseColor", color);
            generatedMaterials.Add(material);
            return material;
        }

        private Material CreateParticleMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material material = new(shader)
            {
                name = name,
                color = color
            };
            material.SetColor("_BaseColor", color);
            generatedMaterials.Add(material);
            return material;
        }

        private ParticleSystem CreateImpactParticleSystem(
            string name,
            Material material,
            float lifetime)
        {
            GameObject particleObject = new(name);
            particleObject.transform.SetParent(transform, false);
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = 0.035f;
            main.gravityModifier = 0.65f;
            main.maxParticles = 256;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;

            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.28f;
            collision.bounce = 0.05f;
            collision.lifetimeLoss = 0.25f;

            ParticleSystemRenderer particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sharedMaterial = material;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static Renderer FindRenderer(GameObject root, string objectName)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == objectName)
                    return renderer;
            }

            throw new MissingComponentException($"Layer renderer '{objectName}' was not found.");
        }

        private void TryShoot(Vector2 screenPosition)
        {
            Ray ray = sceneCamera.ScreenPointToRay(screenPosition);
            int hitCount = Physics.RaycastNonAlloc(ray, raycastHits, 50f);
            LayeredDamageSegment? target = null;
            Vector3 targetPoint = default;
            float nearestSurface = float.PositiveInfinity;
            for (int index = 0; index < hitCount; index++)
            {
                LayeredDamageSegment? candidate =
                    raycastHits[index].collider.GetComponentInParent<LayeredDamageSegment>();
                if (candidate == null || !candidate.TryGetSurfacePoint(ray, out Vector3 surfacePoint))
                    continue;

                float distance = (surfacePoint - ray.origin).sqrMagnitude;
                if (distance >= nearestSurface)
                    continue;

                nearestSurface = distance;
                target = candidate;
                targetPoint = surfacePoint;
            }

            if (target == null)
            {
                status = "Выстрел прошёл мимо — наведитесь на часть тела";
                return;
            }

            nextShotTime = Time.time + 0.24f;
            Vector3 start = sceneCamera.transform.position
                + sceneCamera.transform.forward * 1.15f
                - sceneCamera.transform.right * 0.85f
                - sceneCamera.transform.up * 0.55f;

            GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectileObject.name = "Prototype Fireball";
            projectileObject.transform.position = start;
            projectileObject.transform.localScale = Vector3.one * 0.12f;
            projectileObject.GetComponent<Renderer>().sharedMaterial = emberMaterial;
            Destroy(projectileObject.GetComponent<Collider>());

            projectiles.Add(new Projectile
            {
                Visual = projectileObject,
                Target = target,
                Start = start,
                TargetLocalPoint = target.transform.InverseTransformPoint(targetPoint),
                StartedAt = Time.time
            });
        }

        private void UpdateProjectiles()
        {
            const float travelTime = 0.18f;
            for (int index = projectiles.Count - 1; index >= 0; index--)
            {
                Projectile projectile = projectiles[index];
                float progress = Mathf.Clamp01((Time.time - projectile.StartedAt) / travelTime);
                Vector3 currentEnd = projectile.Target.transform.TransformPoint(projectile.TargetLocalPoint);
                projectile.Visual.transform.position = Vector3.Lerp(
                    projectile.Start,
                    currentEnd,
                    1f - Mathf.Pow(1f - progress, 2f));
                projectile.Visual.transform.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.075f, progress);

                if (progress < 1f)
                    continue;

                LayeredDamageSegment.DamageResult result = projectile.Target.ApplyHit(
                    currentEnd,
                    (currentEnd - projectile.Start).normalized);
                EmitImpact(
                    currentEnd,
                    (currentEnd - projectile.Start).normalized,
                    result.FleshDamaged);
                Destroy(projectile.Visual);
                projectiles.RemoveAt(index);
            }
        }

        private void EmitImpact(Vector3 position, Vector3 incomingDirection, bool blood)
        {
            ParticleSystem particles = blood ? bloodParticles : emberParticles;
            int count = blood ? 16 : 10;
            for (int index = 0; index < count; index++)
            {
                Vector3 scatter = -incomingDirection * Random.Range(0.25f, 1.1f)
                    + Random.insideUnitSphere * 1.35f
                    + Vector3.up * Random.Range(0.25f, 1.1f);
                ParticleSystem.EmitParams emit = new()
                {
                    position = position + Random.insideUnitSphere * 0.035f,
                    velocity = scatter,
                    startLifetime = blood ? Random.Range(2.4f, 3.5f) : Random.Range(0.7f, 1.25f),
                    startSize = Random.Range(0.018f, 0.045f),
                    startColor = blood
                        ? new Color(0.72f, 0.015f, 0.025f)
                        : new Color(1f, Random.Range(0.12f, 0.36f), 0.02f)
                };
                particles.Emit(emit, 1);
            }
        }

        private void OnGUI()
        {
            EnsureGuiStyles();

            Rect panel = new(24f, 22f, 520f, 174f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.025f, 0.035f, 0.05f, 0.92f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(44f, 36f, 480f, 34f), "МОДУЛЬНЫЙ ТРОЛЛЬ", titleStyle);
            GUI.Label(new Rect(44f, 75f, 470f, 48f),
                "ЛКМ — огненный шар по части тела\nR — восстановить тролля",
                bodyStyle);
            GUI.Label(new Rect(44f, 129f, 470f, 48f), status, statusStyle);

            Rect stages = new(Screen.width - 330f, 22f, 306f, 148f);
            GUI.color = new Color(0.025f, 0.035f, 0.05f, 0.88f);
            GUI.DrawTexture(stages, Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(stages.x + 20f, stages.y + 16f, 270f, 118f),
                "1 попадание: отверстие в коже\n" +
                "2 попадание: повреждение плоти\n" +
                "3 попадание: отсечение сегмента\n" +
                "Каждая часть повреждается отдельно",
                bodyStyle);
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                font = uiFont,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.93f, 0.82f, 0.54f) }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                font = uiFont,
                fontSize = 15,
                normal = { textColor = new Color(0.82f, 0.88f, 0.94f) }
            };
            statusStyle = new GUIStyle(bodyStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.44f, 0.28f) }
            };
        }
    }
}
