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

        private Camera sceneCamera = null!;
        private LayeredDamageSegment segment = null!;
        private Material emberMaterial = null!;
        private ParticleSystem emberParticles = null!;
        private ParticleSystem bloodParticles = null!;
        private Font? uiFont;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? statusStyle;
        private float nextShotTime;
        private string status = "Наведите курсор на руку и нажмите ЛКМ";

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
            BuildScene();
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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
            CreateLayeredArm();

            Bounds targetBounds = segment.GetComponentInChildren<Renderer>().bounds;
            Vector3 target = targetBounds.center + new Vector3(0f, -0.1f, 0f);
            sceneCamera.transform.position = target + new Vector3(0.15f, 0.05f, -6.3f);
            sceneCamera.transform.LookAt(target);

            uiFont = Resources.Load<Font>("RobotoMono-Regular");
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void CreateLayeredArm()
        {
            GameObject armPrefab = Resources.Load<GameObject>("Models/TrollArmLayers");
            if (armPrefab == null)
            {
                Debug.LogError("TrollArmLayers model is missing from Resources/Models.");
                status = "Не найдена модель TrollArmLayers";
                return;
            }

            GameObject armRoot = Instantiate(armPrefab);
            armRoot.name = "Troll Arm — Layered Segment";
            armRoot.transform.SetParent(transform, false);
            armRoot.transform.localScale = Vector3.one * 2.35f;
            armRoot.transform.rotation = Quaternion.Euler(0f, 168f, 0f);

            Renderer skin = FindRenderer(armRoot, "Arm_L_Skin");
            Renderer flesh = FindRenderer(armRoot, "Arm_L_Flesh");
            Renderer bone = FindRenderer(armRoot, "Arm_L_Bone");

            Vector3 desiredCenter = new(0f, 1.62f, 0f);
            armRoot.transform.position += desiredCenter - skin.bounds.center;

            Shader damageShader = Shader.Find("WildMagic/LayeredDamageCutout");
            Material skinMaterial = CreateDamageMaterial(
                damageShader,
                "Troll Skin",
                new Color(0.25f, 0.48f, 0.20f),
                new Color(0.07f, 0.025f, 0.012f),
                0.034f);
            Material fleshMaterial = CreateDamageMaterial(
                damageShader,
                "Troll Flesh",
                new Color(0.63f, 0.045f, 0.035f),
                new Color(0.16f, 0.012f, 0.008f),
                0.025f);
            Material boneMaterial = CreateDamageMaterial(
                damageShader,
                "Troll Bone",
                new Color(0.78f, 0.68f, 0.48f),
                new Color(0.28f, 0.18f, 0.08f),
                0.012f);

            segment = armRoot.AddComponent<LayeredDamageSegment>();
            segment.Initialize(skin, flesh, bone, skinMaterial, fleshMaterial, boneMaterial);
            segment.StatusChanged += value => status = value;

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

            CreateMount(skin.bounds);
        }

        private void CreateMount(Bounds armBounds)
        {
            Vector3 shoulder = new(
                armBounds.min.x - 0.18f,
                armBounds.max.y - 0.22f,
                armBounds.center.z + 0.32f);
            GameObject mount = CreateCube(
                "Stone Test Mount",
                shoulder + new Vector3(-0.48f, 0f, 0.18f),
                new Vector3(1.15f, 0.34f, 0.48f),
                new Color(0.12f, 0.16f, 0.20f));
            mount.transform.rotation = Quaternion.Euler(0f, 168f, 0f);
        }

        private void CreateLighting()
        {
            GameObject keyObject = new("Key Light");
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.91f, 0.78f);
            key.intensity = 1.55f;
            key.shadows = LightShadows.Soft;
            keyObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

            GameObject rimObject = new("Cold Rim Light");
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(0.24f, 0.55f, 1f);
            rim.intensity = 5.5f;
            rim.range = 8f;
            rimObject.transform.position = new Vector3(2.4f, 2.7f, 1.8f);
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
            if (!Physics.Raycast(ray, out RaycastHit hit, 50f))
            {
                status = "Выстрел прошёл мимо — наведитесь на руку";
                return;
            }

            LayeredDamageSegment? target = hit.collider.GetComponentInParent<LayeredDamageSegment>();
            if (target == null)
            {
                status = "Нужно попасть именно в руку";
                return;
            }

            Vector3 targetPoint = target.TryGetSurfacePoint(ray, out Vector3 surfacePoint)
                ? surfacePoint
                : hit.point;

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

            GUI.Label(new Rect(44f, 36f, 480f, 34f), "ТЕСТ СЛОЁВ ТРОЛЛЯ", titleStyle);
            GUI.Label(new Rect(44f, 75f, 470f, 48f),
                "ЛКМ — огненный шар по руке\nR — восстановить тест",
                bodyStyle);
            GUI.Label(new Rect(44f, 129f, 470f, 48f), status, statusStyle);

            Rect stages = new(Screen.width - 330f, 22f, 306f, 148f);
            GUI.color = new Color(0.025f, 0.035f, 0.05f, 0.88f);
            GUI.DrawTexture(stages, Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(stages.x + 20f, stages.y + 16f, 270f, 118f),
                "1 попадание: отверстие в коже\n" +
                "2 попадание: повреждение плоти\n" +
                "3 попадание: отсечение\n" +
                "После отсечения можно стрелять дальше",
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
