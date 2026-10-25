using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildMagic.Prototype
{
    /// <summary>
    /// First playable arena slice. The scene is intentionally assembled at runtime so the
    /// prototype can keep using the untouched SampleScene while its rules are still changing.
    /// </summary>
    public sealed class WildMagicArenaGame : MonoBehaviour
    {
        private const float ArenaHalfWidth = 7.9f;
        private const float ArenaHalfHeight = 4.25f;
        private const float PlayerSpeed = 3.15f;
        private const float AiSpeed = 2.45f;
        private const float MageScale = 1.45f;
        private const float FireballSpeed = 10.5f;
        private const float FireCooldown = 0.28f;
        private const float BeamCooldown = 0.62f;
        private const float BeamHalfWidth = 0.24f;
        private const int MaxDamageUnits = 6;
        private const float PixelsPerUnit = 32f;

        private static readonly Color Background = Hex("070A12");
        private static readonly Color ArenaFloor = Hex("101827");
        private static readonly Color GridColor = Hex("20354A");
        private static readonly Color WallColor = Hex("43546B");
        private static readonly Color WallHighlight = Hex("91A5BC");
        private static readonly Color UiInk = Hex("D6E2EF");
        private static readonly Color Gold = Hex("FFE28A");
        private static readonly Color BloodFresh = Hex("C91F36");
        private static readonly Color BloodBright = Hex("F0444F");
        private static readonly Color BloodDark = Hex("631727");

        private static readonly MageColor[] Palette =
        {
            new("АЛЫЙ", Hex("EF5350"), Hex("FFD0A1")),
            new("ЛАЗУРНЫЙ", Hex("45C8FF"), Hex("D5FAFF")),
            new("ФИОЛЕТОВЫЙ", Hex("B46CFF"), Hex("F5D7FF")),
            new("ИЗУМРУДНЫЙ", Hex("49D98A"), Hex("CFFFF0")),
            new("ЗОЛОТОЙ", Hex("F5BE47"), Hex("FFF2B5")),
            new("РОЗОВЫЙ", Hex("FF6FAE"), Hex("FFD7E9")),
            new("БЕЛЫЙ", Hex("DCE7F2"), Hex("FFFFFF")),
            new("ОРАНЖЕВЫЙ", Hex("FF8A45"), Hex("FFE0B8"))
        };

        private readonly List<UnityEngine.Object> generatedArt = new();
        private readonly List<Fireball> fireballs = new();
        private readonly List<BeamVisual> beamVisuals = new();
        private readonly List<PixelFragment> fragments = new();
        private readonly List<EffectPixel> effectPixels = new();
        private readonly List<BloodPixel> bloodPixels = new();
        private readonly List<LivingWound> livingWounds = new();

        private Sprite? whiteSprite;
        private Sprite? fireballSprite;
        private Font? font;
        private Camera? sceneCamera;
        private SpriteRenderer? playerRenderer;
        private SpriteRenderer? aiRenderer;
        private Sprite[] playerIdle = Array.Empty<Sprite>();
        private Sprite[] playerWalk = Array.Empty<Sprite>();
        private Sprite[] aiIdle = Array.Empty<Sprite>();
        private Sprite[] aiWalk = Array.Empty<Sprite>();
        private LayeredMageAnimation? playerIdleLayers;
        private LayeredMageAnimation? playerWalkLayers;
        private LayeredMageAnimation? aiIdleLayers;
        private LayeredMageAnimation? aiWalkLayers;
        private TextMesh? playerHudName;
        private TextMesh? aiHudName;
        private TextMesh? controlsLabel;
        private TextMesh? spellLabel;
        private Vector2 playerPosition;
        private Vector2 aiPosition;
        private Vector2 aiTarget;
        private Vector2 lastPlayerDirection = Vector2.right;
        private Vector2 lastAiDirection = Vector2.left;
        private float nextAiDecisionTime;
        private float nextShotTime;
        private float nextRemoteShotTime;
        private float nextInputSendTime;
        private float nextSnapshotTime;
        private float aiBurnUntil;
        private float playerBurnUntil;
        private float nextBurnParticleTime;
        private float nextPlayerBurnParticleTime;
        private int aiDamageUnits;
        private int playerDamageUnits;
        private int damageSequence;
        private bool aiAlive = true;
        private bool playerAlive = true;
        private bool mouseHeld;
        private string aiDisplayName = string.Empty;
        private string playerDisplayName = string.Empty;
        private Color aiDisplayColor = Color.white;
        private bool playerMoving;
        private bool aiMoving;
        private bool escapeHeld;
        private bool restartHeld;
        private bool pauseOpen;
        private bool matchFinished;
        private SpellKind selectedSpell;
        private SpellKind remoteSelectedSpell;
        private string winnerName = string.Empty;
        private TextMesh? networkLabel;
        private WildMagicNetworkSession? network;
        private uint inputSequence;
        private uint snapshotSequence;
        private uint roundSequence;
        private int currentPlayerColorIndex;
        private int currentAiColorIndex;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? buttonStyle;
        private Texture2D? overlayTexture;
        private Texture2D? panelTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<WildMagicArenaGame>() != null)
                return;

            GameObject root = new("[WildMagic Arena]");
            root.AddComponent<WildMagicArenaGame>();
        }

        private void Awake()
        {
            network = new WildMagicNetworkSession();
            network.OpponentConnected += OnOpponentConnected;
            network.OpponentDisconnected += OnOpponentDisconnected;
            network.SnapshotReceived += OnSnapshotReceived;
            network.MatchSetupReceived += OnMatchSetupReceived;
            network.AttackReceived += OnNetworkAttackReceived;
            network.HitReceived += OnNetworkHitReceived;
            network.FragmentHitReceived += OnNetworkFragmentHitReceived;
            network.RestartRequested += OnNetworkRestartRequested;
            network.InitializeDefault();
            BuildScene();
            StartNewMatch();
        }

        private void Update()
        {
            network?.Tick();
            RefreshNetworkHud();

            Keyboard? keyboard = Keyboard.current;
            bool restartPressed = keyboard != null && keyboard.rKey.isPressed;
            if (restartPressed && !restartHeld)
                RequestNewMatch();
            restartHeld = restartPressed;

            bool escapePressed = keyboard != null && keyboard.escapeKey.isPressed;
            if (escapePressed && !escapeHeld)
            {
                if (!matchFinished)
                    pauseOpen = !pauseOpen;
            }
            escapeHeld = escapePressed;

            if (pauseOpen)
            {
                if (network != null && network.IsClient && Time.unscaledTime >= nextInputSendTime)
                {
                    nextInputSendTime = Time.unscaledTime + 1f / 15f;
                    network.SendInput(new WildMagicNetworkInput
                    {
                        Sequence = ++inputSequence,
                        Move = Vector2.zero,
                        Aim = lastAiDirection,
                        Spell = selectedSpell == SpellKind.Fireball
                            ? WildMagicNetworkSpell.Fireball
                            : WildMagicNetworkSpell.WideBeam,
                        Fire = false
                    });
                }
                return;
            }

            if (network != null && network.IsClient)
            {
                UpdateClientPlayer(keyboard);
            }
            else
            {
                UpdatePlayer(keyboard);
                UpdateShooting();
                if (network != null && network.HasOpponent)
                    UpdateRemoteNetworkPlayer();
                else if (aiAlive)
                    UpdateAi();
            }
            UpdateFireballs();
            UpdateBeamVisuals();
            UpdateFragments();
            UpdateLivingBleeding();
            UpdateEffects();
            UpdateBlood();
            UpdateAnimation();
            if (network != null && network.IsHost && network.HasOpponent)
                SendNetworkSnapshot();
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            network?.Dispose();
            ClearCombatObjects();
            foreach (UnityEngine.Object asset in generatedArt)
            {
                if (asset != null)
                    Destroy(asset);
            }
        }

        private void BuildScene()
        {
            foreach (Camera existingCamera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                existingCamera.gameObject.SetActive(false);

            font = CreateFont();
            whiteSprite = CreateWhiteSprite();
            generatedArt.Add(whiteSprite);
            generatedArt.Add(whiteSprite.texture);

            GameObject cameraObject = new("Arena Camera");
            cameraObject.transform.SetParent(transform, false);
            sceneCamera = cameraObject.AddComponent<Camera>();
            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = 5.55f;
            sceneCamera.backgroundColor = Background;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.transform.localPosition = new Vector3(0f, 0.35f, -10f);
            sceneCamera.nearClipPlane = 0.1f;
            sceneCamera.farClipPlane = 30f;

            fireballSprite = CreateFireballSprite();
            generatedArt.Add(fireballSprite);
            generatedArt.Add(fireballSprite.texture);

            BuildArena();
            BuildHud();
            BuildMages();
        }

        private void BuildArena()
        {
            CreatePanel("Arena Floor", new Vector2(0f, 0f), new Vector2(16.55f, 9.15f), ArenaFloor, -30);

            for (int x = -7; x <= 7; x++)
                CreatePanel($"Grid V {x}", new Vector2(x, 0f), new Vector2(0.018f, 8.45f),
                    new Color(GridColor.r, GridColor.g, GridColor.b, x == 0 ? 0.46f : 0.2f), -28);

            for (int y = -4; y <= 4; y++)
                CreatePanel($"Grid H {y}", new Vector2(0f, y), new Vector2(15.8f, 0.018f),
                    new Color(GridColor.r, GridColor.g, GridColor.b, y == 0 ? 0.46f : 0.2f), -28);

            CreateWall("North Wall", new Vector2(0f, ArenaHalfHeight + 0.22f), new Vector2(16.5f, 0.52f));
            CreateWall("South Wall", new Vector2(0f, -ArenaHalfHeight - 0.22f), new Vector2(16.5f, 0.52f));
            CreateWall("West Wall", new Vector2(-ArenaHalfWidth - 0.22f, 0f), new Vector2(0.52f, 9f));
            CreateWall("East Wall", new Vector2(ArenaHalfWidth + 0.22f, 0f), new Vector2(0.52f, 9f));

            Vector2[] corners =
            {
                new(-ArenaHalfWidth - 0.22f, ArenaHalfHeight + 0.22f),
                new(ArenaHalfWidth + 0.22f, ArenaHalfHeight + 0.22f),
                new(-ArenaHalfWidth - 0.22f, -ArenaHalfHeight - 0.22f),
                new(ArenaHalfWidth + 0.22f, -ArenaHalfHeight - 0.22f)
            };
            foreach (Vector2 corner in corners)
            {
                CreatePanel("Corner Pillar", corner, new Vector2(0.76f, 0.76f), WallColor, -17);
                CreateGlyph("Pillar Rune", "◇", corner, Gold, 0.18f, -15);
            }

            CreateGlyph("Floor Mark", "○       ✦       ○", Vector2.zero,
                new Color(Gold.r, Gold.g, Gold.b, 0.16f), 0.4f, -26);
        }

        private void BuildHud()
        {
            CreatePanel("Header", new Vector2(0f, 5.18f), new Vector2(17.3f, 0.8f),
                new Color(0.025f, 0.04f, 0.075f, 0.98f), -5);
            CreateGlyph("Title", "WILD MAGIC  //  ДУЭЛЬ", new Vector2(0f, 5.2f), UiInk, 0.18f, 20);
            playerHudName = CreateGlyph("Player HUD", string.Empty, new Vector2(-7.75f, 4.78f),
                UiInk, 0.14f, 20, TextAnchor.MiddleLeft);
            aiHudName = CreateGlyph("AI HUD", string.Empty, new Vector2(7.75f, 4.78f),
                UiInk, 0.14f, 20, TextAnchor.MiddleRight);
            networkLabel = CreateGlyph("Network Status", string.Empty, new Vector2(0f, 4.79f),
                new Color(UiInk.r, UiInk.g, UiInk.b, 0.76f), 0.105f, 20);
            spellLabel = CreateGlyph("Selected Spell", string.Empty, new Vector2(0f, -4.55f),
                Hex("FFB14A"), 0.105f, 20);
            controlsLabel = CreateGlyph("Controls", "WASD / СТРЕЛКИ — ДВИЖЕНИЕ    ЛКМ — ПРИМЕНИТЬ    КОЛЕСО — ЗАКЛИНАНИЕ    R — НОВЫЙ БОЙ    ESC — МЕНЮ", new Vector2(0f, -4.88f),
                new Color(UiInk.r, UiInk.g, UiInk.b, 0.72f), 0.105f, 20);
        }

        private void BuildMages()
        {
            GameObject player = new("Player Mage");
            player.transform.SetParent(transform, false);
            playerRenderer = player.AddComponent<SpriteRenderer>();
            playerRenderer.sortingOrder = 5;
            player.transform.localScale = Vector3.one * MageScale;
            CreateShadow(player.transform, "Player Shadow");

            GameObject ai = new("AI Mage");
            ai.transform.SetParent(transform, false);
            aiRenderer = ai.AddComponent<SpriteRenderer>();
            aiRenderer.sortingOrder = 5;
            ai.transform.localScale = Vector3.one * MageScale;
            CreateShadow(ai.transform, "AI Shadow");
        }

        private void StartNewMatch(int forcedPlayerColorIndex = -1, int forcedAiColorIndex = -1,
            bool broadcast = true)
        {
            ClearCombatObjects();
            pauseOpen = false;
            matchFinished = false;
            winnerName = string.Empty;
            playerAlive = true;
            aiAlive = true;
            playerDamageUnits = 0;
            aiDamageUnits = 0;
            damageSequence = 0;
            aiBurnUntil = 0f;
            playerBurnUntil = 0f;
            nextBurnParticleTime = 0f;
            nextPlayerBurnParticleTime = 0f;
            livingWounds.Clear();
            nextShotTime = 0f;
            nextRemoteShotTime = 0f;
            mouseHeld = false;
            selectedSpell = SpellKind.Fireball;
            remoteSelectedSpell = SpellKind.Fireball;
            RefreshSpellHud();

            int playerColorIndex = forcedPlayerColorIndex >= 0
                ? Mathf.Clamp(forcedPlayerColorIndex, 0, Palette.Length - 1)
                : UnityEngine.Random.Range(0, Palette.Length);
            int aiColorIndex = forcedAiColorIndex >= 0
                ? Mathf.Clamp(forcedAiColorIndex, 0, Palette.Length - 1)
                : UnityEngine.Random.Range(0, Palette.Length - 1);
            if (aiColorIndex >= playerColorIndex)
            {
                if (forcedAiColorIndex < 0)
                    aiColorIndex++;
                else if (aiColorIndex == playerColorIndex)
                    aiColorIndex = (aiColorIndex + 1) % Palette.Length;
            }

            currentPlayerColorIndex = playerColorIndex;
            currentAiColorIndex = aiColorIndex;

            MageColor playerColor = Palette[playerColorIndex];
            MageColor aiColor = Palette[aiColorIndex];
            ReplaceMageArt(ref playerIdle, ref playerWalk, ref playerIdleLayers, ref playerWalkLayers,
                "Player", playerColor);
            ReplaceMageArt(ref aiIdle, ref aiWalk, ref aiIdleLayers, ref aiWalkLayers,
                "AI", aiColor);

            if (playerRenderer != null)
            {
                playerRenderer.gameObject.SetActive(true);
                playerRenderer.sprite = playerIdle[0];
                playerRenderer.flipX = false;
            }
            if (aiRenderer != null)
            {
                aiRenderer.gameObject.SetActive(true);
                aiRenderer.sprite = aiIdle[0];
                aiRenderer.flipX = true;
            }

            playerDisplayName = playerColor.Name;
            ApplyName(playerHudName, $"ИГРОК 1  //  {playerColor.Name}", playerColor.Robe);
            aiDisplayName = aiColor.Name;
            aiDisplayColor = aiColor.Robe;
            RefreshAiHud();

            playerPosition = new Vector2(-4.8f, 0f);
            aiPosition = new Vector2(4.8f, 0f);
            playerMoving = false;
            aiMoving = false;
            lastPlayerDirection = Vector2.right;
            lastAiDirection = Vector2.left;
            PickAiTarget();
            ApplyMagePositions();
            RefreshPlayerHud();
            RefreshAiHud();

            if (broadcast && network != null && network.IsHost && network.HasOpponent)
                network.SendMatchSetup(currentPlayerColorIndex, currentAiColorIndex, ++roundSequence);
        }

        private void UpdatePlayer(Keyboard? keyboard)
        {
            Vector2 input = ReadMovementInput(keyboard);
            playerMoving = input.sqrMagnitude > 0.01f;
            if (playerMoving)
            {
                lastPlayerDirection = input;
                playerPosition += input * (PlayerSpeed * Time.deltaTime);
                playerPosition = ClampToArena(playerPosition);
                if (playerRenderer != null && Mathf.Abs(input.x) > 0.05f)
                    playerRenderer.flipX = input.x < 0f;
            }
        }

        private void UpdateClientPlayer(Keyboard? keyboard)
        {
            if (network == null)
                return;

            Vector2 input = ReadMovementInput(keyboard);
            aiMoving = input.sqrMagnitude > 0.01f;
            if (aiMoving && aiAlive)
            {
                aiPosition = ClampToArena(aiPosition + input * (PlayerSpeed * Time.deltaTime));
                lastAiDirection = input;
                if (aiRenderer != null && Mathf.Abs(input.x) > 0.05f)
                    aiRenderer.flipX = input.x < 0f;
            }

            Mouse? mouse = Mouse.current;
            Vector2 aim = lastAiDirection.sqrMagnitude > 0.01f ? lastAiDirection : Vector2.left;
            bool fire = false;
            if (mouse != null && sceneCamera != null)
            {
                if (Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)
                {
                    selectedSpell = selectedSpell == SpellKind.Fireball ? SpellKind.WideBeam : SpellKind.Fireball;
                    RefreshSpellHud();
                }

                Vector3 screen = mouse.position.ReadValue();
                Vector3 world = sceneCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y,
                    -sceneCamera.transform.position.z));
                aim = (Vector2)world - (aiPosition + new Vector2(0f, 0.55f));
                if (aim.sqrMagnitude < 0.01f)
                    aim = lastAiDirection.sqrMagnitude > 0.01f ? lastAiDirection : Vector2.left;
                aim.Normalize();
                fire = mouse.leftButton.isPressed && aiAlive;
                lastAiDirection = aim;
                if (aiRenderer != null && Mathf.Abs(aim.x) > 0.05f)
                    aiRenderer.flipX = aim.x < 0f;
            }

            if (Time.unscaledTime >= nextInputSendTime)
            {
                nextInputSendTime = Time.unscaledTime + 1f / 30f;
                network.SendInput(new WildMagicNetworkInput
                {
                    Sequence = ++inputSequence,
                    Move = input,
                    Aim = aim,
                    Spell = selectedSpell == SpellKind.Fireball
                        ? WildMagicNetworkSpell.Fireball
                        : WildMagicNetworkSpell.WideBeam,
                    Fire = fire
                });
            }
        }

        private void UpdateRemoteNetworkPlayer()
        {
            if (network == null || !aiAlive)
                return;

            WildMagicNetworkInput input = network.RemoteInput;
            Vector2 movement = Vector2.ClampMagnitude(input.Move, 1f);
            aiMoving = movement.sqrMagnitude > 0.01f;
            if (aiMoving)
            {
                aiPosition = ClampToArena(aiPosition + movement * (PlayerSpeed * Time.deltaTime));
                if (aiRenderer != null && Mathf.Abs(movement.x) > 0.05f)
                    aiRenderer.flipX = movement.x < 0f;
            }

            Vector2 aim = input.Aim.sqrMagnitude > 0.01f ? input.Aim.normalized : lastAiDirection;
            if (aim.sqrMagnitude < 0.01f)
                aim = Vector2.left;
            lastAiDirection = aim;
            if (aiRenderer != null && Mathf.Abs(aim.x) > 0.05f)
                aiRenderer.flipX = aim.x < 0f;
            remoteSelectedSpell = input.Spell == WildMagicNetworkSpell.WideBeam
                ? SpellKind.WideBeam
                : SpellKind.Fireball;

            if (!input.Fire || Time.time < nextRemoteShotTime)
                return;

            Vector2 origin = aiPosition + new Vector2(0f, 0.55f) + aim * 0.48f;
            if (remoteSelectedSpell == SpellKind.Fireball)
            {
                SpawnFireball(origin, aim, 1);
                network.SendAttack(1, WildMagicNetworkSpell.Fireball, origin, aim);
                nextRemoteShotTime = Time.time + FireCooldown;
            }
            else
            {
                CastWideBeam(origin, aim, 1, true);
                network.SendAttack(1, WildMagicNetworkSpell.WideBeam, origin, aim);
                nextRemoteShotTime = Time.time + BeamCooldown;
            }
        }

        private static Vector2 ReadMovementInput(Keyboard? keyboard)
        {
            Vector2 input = Vector2.zero;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            }
            return Vector2.ClampMagnitude(input, 1f);
        }

        private void UpdateShooting()
        {
            if (!playerAlive)
                return;
            Mouse? mouse = Mouse.current;
            if (mouse == null || sceneCamera == null || fireballSprite == null)
                return;

            if (Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)
            {
                selectedSpell = selectedSpell == SpellKind.Fireball ? SpellKind.WideBeam : SpellKind.Fireball;
                RefreshSpellHud();
            }

            bool pressed = mouse.leftButton.isPressed;
            if (pressed && (!mouseHeld || Time.time >= nextShotTime))
            {
                Vector3 screen = mouse.position.ReadValue();
                Vector3 world = sceneCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -sceneCamera.transform.position.z));
                Vector2 aim = (Vector2)world - (playerPosition + new Vector2(0f, 0.55f));
                if (aim.sqrMagnitude < 0.01f)
                    aim = lastPlayerDirection.sqrMagnitude > 0.01f ? lastPlayerDirection : Vector2.right;
                aim.Normalize();

                Vector2 origin = playerPosition + new Vector2(0f, 0.55f) + aim * 0.48f;
                if (selectedSpell == SpellKind.Fireball)
                {
                    SpawnFireball(origin, aim, 0);
                    network?.SendAttack(0, WildMagicNetworkSpell.Fireball, origin, aim);
                    nextShotTime = Time.time + FireCooldown;
                }
                else
                {
                    CastWideBeam(origin, aim, 0, true);
                    network?.SendAttack(0, WildMagicNetworkSpell.WideBeam, origin, aim);
                    nextShotTime = Time.time + BeamCooldown;
                }
                lastPlayerDirection = aim;
                if (playerRenderer != null && Mathf.Abs(aim.x) > 0.05f)
                    playerRenderer.flipX = aim.x < 0f;
            }

            mouseHeld = pressed;
        }

        private void SpawnFireball(Vector2 origin, Vector2 aim, byte owner)
        {
            GameObject projectileObject = new("Fireball");
            projectileObject.transform.SetParent(transform, false);
            projectileObject.transform.position = new Vector3(origin.x, origin.y, 0f);
            SpriteRenderer renderer = projectileObject.AddComponent<SpriteRenderer>();
            renderer.sprite = fireballSprite;
            renderer.sortingOrder = 14;
            fireballs.Add(new Fireball
            {
                Object = projectileObject,
                Renderer = renderer,
                Position = origin,
                Velocity = aim * FireballSpeed,
                Owner = owner,
                Life = 2.2f
            });
        }

        private void CastWideBeam(Vector2 origin, Vector2 direction, byte owner, bool resolveDamage)
        {
            Vector2 end = GetBeamEnd(origin, direction);
            CreateBeamVisual(origin, end, BeamHalfWidth * 2f, new Color(0.2f, 0.82f, 1f, 0.28f));
            CreateBeamVisual(origin, end, BeamHalfWidth * 0.72f, new Color(0.82f, 0.98f, 1f, 0.94f));
            for (int i = 0; i < 18; i++)
            {
                Vector2 point = Vector2.Lerp(origin, end, UnityEngine.Random.value);
                Vector2 normal = new(-direction.y, direction.x);
                SpawnEffectPixel(point + normal * UnityEngine.Random.Range(-BeamHalfWidth, BeamHalfWidth),
                    normal * UnityEngine.Random.Range(-0.28f, 0.28f),
                    UnityEngine.Random.value > 0.4f ? Hex("C9FAFF") : Hex("4DDCFF"),
                    UnityEngine.Random.Range(0.12f, 0.28f), UnityEngine.Random.Range(0.035f, 0.065f), 0f);
            }

            if (!resolveDamage)
                return;

            PixelFragment[] existingFragments = fragments.ToArray();
            bool targetPlayer = owner == 1;
            Vector2 mageCenter = (targetPlayer ? playerPosition : aiPosition) + new Vector2(0f, 0.55f);
            bool targetAlive = targetPlayer ? playerAlive : aiAlive;
            if (targetAlive && SegmentPointDistanceSquared(origin, end, mageCenter) <
                (0.42f + BeamHalfWidth) * (0.42f + BeamHalfWidth))
            {
                Vector2 impact = ClosestPointOnSegment(origin, end, mageCenter);
                HitLivingMageWithBeam(targetPlayer, impact, direction, true);
            }

            foreach (PixelFragment fragment in existingFragments)
                if (fragments.Contains(fragment))
                    DamageFragmentWithBeam(fragment, origin, end, direction, owner, true);
        }

        private void UpdateFireballs()
        {
            float delta = Time.deltaTime;
            for (int i = fireballs.Count - 1; i >= 0; i--)
            {
                Fireball fireball = fireballs[i];
                Vector2 previous = fireball.Position;
                fireball.Position += fireball.Velocity * delta;
                fireball.Life -= delta;
                fireball.TrailClock -= delta;
                fireball.Object.transform.position = new Vector3(fireball.Position.x, fireball.Position.y, 0f);
                fireball.Object.transform.Rotate(0f, 0f, 420f * delta);

                if (fireball.TrailClock <= 0f)
                {
                    fireball.TrailClock = 0.035f;
                    SpawnEffectPixel(previous, -fireball.Velocity.normalized * 0.45f + UnityEngine.Random.insideUnitCircle * 0.22f,
                        UnityEngine.Random.value > 0.45f ? Hex("FFB52E") : Hex("FF5A1F"), 0.24f, 0.075f, 0f);
                }

                bool consumed = false;
                bool resolveDamage = network == null || network.IsHost;
                bool targetPlayer = fireball.Owner == 1;
                bool targetAlive = targetPlayer ? playerAlive : aiAlive;
                Vector2 targetPosition = targetPlayer ? playerPosition : aiPosition;
                if (resolveDamage && targetAlive && SegmentPointDistanceSquared(previous, fireball.Position,
                        targetPosition + new Vector2(0f, 0.55f)) < 0.42f * 0.42f)
                {
                    Vector2 impact = ClosestPointOnSegment(previous, fireball.Position,
                        targetPosition + new Vector2(0f, 0.55f));
                    consumed = HitLivingMage(targetPlayer, impact, fireball.Velocity.normalized, true);
                }

                if (resolveDamage && !consumed &&
                    TryFindFragmentHit(previous, fireball.Position, out PixelFragment? fragment, out Vector2 fragmentHit))
                {
                    DamageFragment(fragment!, fragmentHit, fireball.Velocity.normalized);
                    network?.SendFragmentHit(fireball.Owner, WildMagicNetworkSpell.Fireball,
                        fragmentHit, fireball.Velocity.normalized, Vector2.zero, Vector2.zero, 0);
                    consumed = true;
                }

                if (consumed || fireball.Life <= 0f || Mathf.Abs(fireball.Position.x) > ArenaHalfWidth + 1f ||
                    Mathf.Abs(fireball.Position.y) > ArenaHalfHeight + 1f)
                    DestroyFireballAt(i);
            }
        }

        private void UpdateBeamVisuals()
        {
            for (int i = beamVisuals.Count - 1; i >= 0; i--)
            {
                BeamVisual visual = beamVisuals[i];
                visual.Life -= Time.deltaTime;
                if (visual.Life <= 0f)
                {
                    Destroy(visual.Object);
                    beamVisuals.RemoveAt(i);
                    continue;
                }

                Color color = visual.Color;
                color.a *= Mathf.Clamp01(visual.Life / visual.MaxLife);
                visual.Renderer.color = color;
            }
        }

        private Vector2 GetBeamEnd(Vector2 origin, Vector2 direction)
        {
            float distance = 30f;
            if (direction.x > 0.001f)
                distance = Mathf.Min(distance, (ArenaHalfWidth - origin.x) / direction.x);
            else if (direction.x < -0.001f)
                distance = Mathf.Min(distance, (-ArenaHalfWidth - origin.x) / direction.x);
            if (direction.y > 0.001f)
                distance = Mathf.Min(distance, (ArenaHalfHeight - origin.y) / direction.y);
            else if (direction.y < -0.001f)
                distance = Mathf.Min(distance, (-ArenaHalfHeight - origin.y) / direction.y);
            return origin + direction * Mathf.Max(0.1f, distance);
        }

        private void CreateBeamVisual(Vector2 start, Vector2 end, float width, Color color)
        {
            Vector2 delta = end - start;
            GameObject beamObject = new("Wide Beam");
            beamObject.transform.SetParent(transform, false);
            beamObject.transform.position = (start + end) * 0.5f;
            beamObject.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            beamObject.transform.localScale = new Vector3(delta.magnitude, width, 1f);
            SpriteRenderer renderer = beamObject.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = 12;
            beamVisuals.Add(new BeamVisual
            {
                Object = beamObject,
                Renderer = renderer,
                Color = color,
                Life = 0.16f,
                MaxLife = 0.16f
            });
        }

        private void SpawnBeamImpactEffects(Vector2 impact, Vector2 direction, int fleshRemoved)
        {
            Vector2 normal = new(-direction.y, direction.x);
            for (int i = 0; i < 22; i++)
            {
                Vector2 position = impact + normal * UnityEngine.Random.Range(-BeamHalfWidth, BeamHalfWidth);
                Vector2 velocity = direction * UnityEngine.Random.Range(0.15f, 0.7f) +
                                   normal * UnityEngine.Random.Range(-1.1f, 1.1f);
                SpawnEffectPixel(position, velocity,
                    UnityEngine.Random.value > 0.35f ? Hex("BDF8FF") : Hex("3BD8FF"),
                    UnityEngine.Random.Range(0.22f, 0.48f), UnityEngine.Random.Range(0.04f, 0.075f), 0.4f);
            }
            if (fleshRemoved > 0)
                SpawnBloodBurst(impact, direction, Mathf.Clamp(5 + fleshRemoved / 6, 6, 13));
        }

        private bool HitLivingMage(bool targetPlayer, Vector2 impact, Vector2 direction, bool replicate,
            bool useAuthoritativeHit = false, Vector2 authoritativeCanonical = default,
            int authoritativeDamagePass = 0)
        {
            SpriteRenderer? targetRenderer = targetPlayer ? playerRenderer : aiRenderer;
            LayeredMageAnimation? idleLayers = targetPlayer ? playerIdleLayers : aiIdleLayers;
            LayeredMageAnimation? walkLayers = targetPlayer ? playerWalkLayers : aiWalkLayers;
            int targetDamage = targetPlayer ? playerDamageUnits : aiDamageUnits;
            if (targetRenderer == null || targetRenderer.sprite == null)
                return true;

            Sprite current = targetRenderer.sprite;
            LayeredMageFrame? currentLayers = FindLayeredFrame(current, idleLayers, walkLayers);
            if (currentLayers == null)
                return true;
            Vector2Int texturePixel = WorldToPixel(targetRenderer.transform, current, impact, targetRenderer.flipX);
            texturePixel = FindNearestOpaquePixel(current, texturePixel, 6);
            Vector2 canonicalHit = useAuthoritativeHit
                ? authoritativeCanonical
                : currentLayers.CanonicalCoordinates[texturePixel.y * currentLayers.Width + texturePixel.x];
            Color32 chipColor = SamplePixel(current, texturePixel);
            int hitNumber = useAuthoritativeHit ? authoritativeDamagePass : ++damageSequence;
            damageSequence = Mathf.Max(damageSequence, hitNumber);
            int craterRadius = targetDamage >= 4 ? 4 : 3;
            LayerDamageResult damage = ApplyLayeredDamage(currentLayers, canonicalHit, craterRadius, hitNumber);
            ApplyLayeredDamage(idleLayers, canonicalHit, craterRadius, hitNumber, currentLayers);
            ApplyLayeredDamage(walkLayers, canonicalHit, craterRadius, hitNumber, currentLayers);
            targetDamage = Mathf.Min(MaxDamageUnits, targetDamage + 2);
            if (targetPlayer)
            {
                playerDamageUnits = targetDamage;
                playerBurnUntil = Mathf.Max(playerBurnUntil, Time.time + 3.25f);
                nextPlayerBurnParticleTime = 0f;
                RefreshPlayerHud();
            }
            else
            {
                aiDamageUnits = targetDamage;
                aiBurnUntil = Mathf.Max(aiBurnUntil, Time.time + 3.25f);
                nextBurnParticleTime = 0f;
                RefreshAiHud();
            }

            if (replicate)
                network?.SendHit(targetPlayer ? (byte)0 : (byte)1, WildMagicNetworkSpell.Fireball,
                    impact, direction, canonicalHit, Vector2.zero, hitNumber);

            for (int i = 0; i < 17; i++)
            {
                Vector2 velocity = -direction * UnityEngine.Random.Range(0.15f, 0.8f) +
                                   UnityEngine.Random.insideUnitCircle * 1.35f;
                SpawnEffectPixel(impact, velocity, i % 3 == 0 ? Hex("FF6A22") : chipColor,
                    UnityEngine.Random.Range(0.35f, 0.72f), UnityEngine.Random.Range(0.045f, 0.085f), 2.2f);
            }

            if (damage.FleshRemoved > 0)
            {
                SpawnBloodBurst(impact, direction, Mathf.Clamp(4 + damage.FleshRemoved / 8, 5, 11));
                AddLivingWound(targetPlayer, damage, direction);
            }
            if (damage.BoneDamaged > 0)
                SpawnBoneChips(impact, direction, Mathf.Clamp(3 + damage.BoneDamaged / 5, 4, 9));

            if (targetDamage < MaxDamageUnits)
                return true;

            DefeatLivingMageFromDamage(targetPlayer, currentLayers, current, impact, direction, damage);
            return true;
        }

        private void HitLivingMageWithBeam(bool targetPlayer, Vector2 impact, Vector2 direction, bool replicate,
            bool useAuthoritativeHit = false, Vector2 authoritativeCanonical = default,
            Vector2 authoritativePixelDirection = default, int authoritativeDamagePass = 0)
        {
            SpriteRenderer? targetRenderer = targetPlayer ? playerRenderer : aiRenderer;
            LayeredMageAnimation? idleLayers = targetPlayer ? playerIdleLayers : aiIdleLayers;
            LayeredMageAnimation? walkLayers = targetPlayer ? playerWalkLayers : aiWalkLayers;
            int targetDamage = targetPlayer ? playerDamageUnits : aiDamageUnits;
            if (targetRenderer == null || targetRenderer.sprite == null)
                return;

            Sprite current = targetRenderer.sprite;
            LayeredMageFrame? currentLayers = FindLayeredFrame(current, idleLayers, walkLayers);
            if (currentLayers == null)
                return;

            Vector2Int center = WorldToPixel(targetRenderer.transform, current, impact, targetRenderer.flipX);
            Vector2 canonicalCenter = useAuthoritativeHit
                ? authoritativeCanonical
                : currentLayers.CanonicalCoordinates[center.y * currentLayers.Width + center.x];
            Vector2 pixelDirection = useAuthoritativeHit
                ? authoritativePixelDirection
                : new Vector2(direction.x * (targetRenderer.flipX ? -1f : 1f), direction.y).normalized;
            int damagePass = useAuthoritativeHit ? authoritativeDamagePass : ++damageSequence;
            damageSequence = Mathf.Max(damageSequence, damagePass);
            LayerDamageResult damage = ApplyBeamDamage(currentLayers, canonicalCenter, pixelDirection, damagePass);
            ApplyBeamDamage(idleLayers, canonicalCenter, pixelDirection, damagePass, currentLayers);
            ApplyBeamDamage(walkLayers, canonicalCenter, pixelDirection, damagePass, currentLayers);
            targetDamage = Mathf.Min(MaxDamageUnits, targetDamage + 3);
            if (targetPlayer)
            {
                playerDamageUnits = targetDamage;
                RefreshPlayerHud();
            }
            else
            {
                aiDamageUnits = targetDamage;
                RefreshAiHud();
            }

            if (replicate)
                network?.SendHit(targetPlayer ? (byte)0 : (byte)1, WildMagicNetworkSpell.WideBeam,
                    impact, direction, canonicalCenter, pixelDirection, damagePass);

            SpawnBeamImpactEffects(impact, direction, damage.FleshRemoved);
            if (damage.FleshRemoved > 0)
                AddLivingWound(targetPlayer, damage, direction);
            if (targetDamage >= MaxDamageUnits)
                DefeatLivingMageFromDamage(targetPlayer, currentLayers, current, impact, direction, damage);
        }

        private void DefeatLivingMageFromDamage(bool targetPlayer, LayeredMageFrame currentLayers,
            Sprite current, Vector2 impact,
            Vector2 direction, LayerDamageResult damage)
        {
            SpriteRenderer? targetRenderer = targetPlayer ? playerRenderer : aiRenderer;
            if (targetRenderer == null)
                return;
            Color32[] corpseOuter = (Color32[])currentLayers.OuterPixels.Clone();
            Color32[] corpseFlesh = (Color32[])currentLayers.FleshPixels.Clone();
            Color32[] corpseBones = (Color32[])currentLayers.BonePixels.Clone();
            byte[] corpseDurability = (byte[])currentLayers.BoneDurability.Clone();
            bool[] corpseStaff = (bool[])currentLayers.StaffMask.Clone();
            bool[] corpseBurning = (bool[])currentLayers.BurningMask.Clone();
            if (targetRenderer.flipX)
            {
                FlipPixelsHorizontally(corpseOuter, current.texture.width, current.texture.height);
                FlipPixelsHorizontally(corpseFlesh, current.texture.width, current.texture.height);
                FlipPixelsHorizontally(corpseBones, current.texture.width, current.texture.height);
                FlipPixelsHorizontally(corpseDurability, current.texture.width, current.texture.height);
                FlipPixelsHorizontally(corpseStaff, current.texture.width, current.texture.height);
                FlipPixelsHorizontally(corpseBurning, current.texture.width, current.texture.height);
            }

            Vector2Int visualCut = WorldToPixel(targetRenderer.transform, current, impact, false);
            Vector2 parentVisual = targetRenderer.transform.position;
            float burnUntil = targetPlayer ? playerBurnUntil : aiBurnUntil;
            CreateInitialCorpse(corpseOuter, corpseFlesh, corpseBones, corpseDurability, corpseStaff,
                corpseBurning,
                current.texture.width, current.texture.height, visualCut, direction, current.pivot,
                damage.FleshRemoved, parentVisual, burnUntil);
            livingWounds.RemoveAll(wound => wound.TargetPlayer == targetPlayer);
            targetRenderer.gameObject.SetActive(false);
            if (targetPlayer)
            {
                playerAlive = false;
                playerMoving = false;
                RefreshPlayerHud();
            }
            else
            {
                aiAlive = false;
                aiMoving = false;
                RefreshAiHud();
            }
            DefeatMage(targetPlayer);
        }

        private void UpdateFragments()
        {
            float delta = Time.deltaTime;
            for (int i = 0; i < fragments.Count; i++)
            {
                PixelFragment fragment = fragments[i];
                if (!fragment.Landed)
                {
                    fragment.GroundPosition += fragment.PlanarVelocity * delta;
                    fragment.PlanarVelocity *= Mathf.Pow(0.42f, delta);
                    fragment.VerticalVelocity -= 5.8f * delta;
                    fragment.Height += fragment.VerticalVelocity * delta;
                    fragment.AngularVelocity *= Mathf.Pow(0.68f, delta);

                    if (fragment.Height <= 0f)
                    {
                        fragment.Height = 0f;
                        if (Mathf.Abs(fragment.VerticalVelocity) > 0.5f)
                        {
                            fragment.VerticalVelocity = -fragment.VerticalVelocity * 0.32f;
                            fragment.PlanarVelocity *= 0.72f;
                            fragment.AngularVelocity *= 0.72f;
                        }
                        else
                        {
                            fragment.VerticalVelocity = 0f;
                            fragment.PlanarVelocity = Vector2.zero;
                            fragment.AngularVelocity = 0f;
                            fragment.Landed = true;
                        }
                    }

                    Vector2 clamped = ClampFragmentToArena(fragment.GroundPosition);
                    if (!Mathf.Approximately(clamped.x, fragment.GroundPosition.x))
                        fragment.PlanarVelocity.x *= -0.35f;
                    if (!Mathf.Approximately(clamped.y, fragment.GroundPosition.y))
                        fragment.PlanarVelocity.y *= -0.35f;
                    fragment.GroundPosition = clamped;
                }

                fragment.Object.transform.position = new Vector3(fragment.GroundPosition.x,
                    fragment.GroundPosition.y + fragment.Height, 0f);
                fragment.Object.transform.Rotate(0f, 0f, fragment.AngularVelocity * delta);
                fragment.Shadow.transform.position = new Vector3(fragment.GroundPosition.x,
                    fragment.GroundPosition.y - 0.02f, 0f);
                Color shadowColor = fragment.ShadowRenderer.color;
                shadowColor.a = Mathf.Lerp(0.38f, 0.12f, Mathf.Clamp01(fragment.Height / 1.4f));
                fragment.ShadowRenderer.color = shadowColor;

                for (int woundIndex = fragment.Wounds.Count - 1; woundIndex >= 0; woundIndex--)
                {
                    FragmentWound wound = fragment.Wounds[woundIndex];
                    if (Time.time >= wound.BleedUntil)
                    {
                        fragment.Wounds.RemoveAt(woundIndex);
                        continue;
                    }
                    if (Time.time < wound.NextBloodTime || wound.Pixels.Count == 0)
                        continue;

                    float intensity = BleedingIntensity(wound.FleshRemoved);
                    wound.NextBloodTime = Time.time + UnityEngine.Random.Range(0.12f, 0.2f) / intensity;
                    int droplets = Mathf.Clamp(1 + wound.FleshRemoved / 18, 1, 3);
                    for (int drop = 0; drop < droplets; drop++)
                    {
                        Vector2 woundPixel = wound.Pixels[UnityEngine.Random.Range(0, wound.Pixels.Count)];
                        Vector2 local = (woundPixel - fragment.Sprite.pivot) / PixelsPerUnit;
                        Vector2 woundWorld = fragment.Object.transform.TransformPoint(local);
                        Vector2 ground = woundWorld - Vector2.up * fragment.Height;
                        SpawnBloodPixel(ground, fragment.Height + 0.035f,
                            fragment.PlanarVelocity * 0.35f + UnityEngine.Random.insideUnitCircle *
                            Mathf.Lerp(0.24f, 0.48f, Mathf.InverseLerp(1f, 40f, wound.FleshRemoved)),
                            UnityEngine.Random.Range(0.35f, 0.95f),
                            UnityEngine.Random.value > 0.28f ? BloodFresh : BloodBright,
                            UnityEngine.Random.Range(0.042f, 0.072f), UnityEngine.Random.Range(5.5f, 8.5f));
                    }
                }

                if (Time.time < fragment.BurnUntil && Time.time >= fragment.NextFireTime)
                {
                    fragment.NextFireTime = Time.time + UnityEngine.Random.Range(0.055f, 0.11f);
                    if (TryPickBurningPixel(fragment.BurningMask, fragment.OuterPixels,
                            fragment.FleshPixels, fragment.BonePixels, fragment.Width, out Vector2 pixel))
                    {
                        Vector2 local = (pixel - fragment.Sprite.pivot) / PixelsPerUnit;
                        Vector2 source = fragment.Object.transform.TransformPoint(local);
                        SpawnFlameFromBurningPixel(source, fragment.PlanarVelocity * 0.18f);
                    }
                }
            }
        }

        private void AddLivingWound(bool targetPlayer, LayerDamageResult damage, Vector2 direction)
        {
            if (damage.FleshRemoved <= 0 || damage.FleshWoundCoordinates == null ||
                damage.FleshWoundCoordinates.Count == 0)
                return;

            livingWounds.Add(new LivingWound
            {
                TargetPlayer = targetPlayer,
                CanonicalPixels = SelectWoundSamples(damage.FleshWoundCoordinates, 14),
                Direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right,
                FleshRemoved = damage.FleshRemoved,
                BleedUntil = Time.time + 5f,
                NextBloodTime = Time.time + UnityEngine.Random.Range(0.035f, 0.085f)
            });
        }

        private void UpdateLivingBleeding()
        {
            for (int i = livingWounds.Count - 1; i >= 0; i--)
            {
                LivingWound wound = livingWounds[i];
                bool targetAlive = wound.TargetPlayer ? playerAlive : aiAlive;
                if (!targetAlive || Time.time >= wound.BleedUntil)
                {
                    livingWounds.RemoveAt(i);
                    continue;
                }
                if (Time.time < wound.NextBloodTime ||
                    !TryGetLivingWoundPosition(wound, out Vector2 source, out Vector2 ground))
                    continue;

                float intensity = BleedingIntensity(wound.FleshRemoved);
                wound.NextBloodTime = Time.time + UnityEngine.Random.Range(0.13f, 0.21f) / intensity;
                int droplets = Mathf.Clamp(1 + wound.FleshRemoved / 18, 1, 3);
                for (int drop = 0; drop < droplets; drop++)
                {
                    float height = Mathf.Max(0.045f, source.y - ground.y);
                    Vector2 velocity = wound.Direction * UnityEngine.Random.Range(0.04f, 0.22f) +
                                       UnityEngine.Random.insideUnitCircle *
                                       Mathf.Lerp(0.2f, 0.46f,
                                           Mathf.InverseLerp(1f, 40f, wound.FleshRemoved));
                    SpawnBloodPixel(ground, height, velocity, UnityEngine.Random.Range(0.25f, 0.8f),
                        UnityEngine.Random.value > 0.24f ? BloodFresh : BloodBright,
                        UnityEngine.Random.Range(0.042f, 0.074f), UnityEngine.Random.Range(5.5f, 8.5f));
                }
            }
        }

        private bool TryGetLivingWoundPosition(LivingWound wound, out Vector2 source, out Vector2 ground)
        {
            SpriteRenderer? renderer = wound.TargetPlayer ? playerRenderer : aiRenderer;
            LayeredMageAnimation? idle = wound.TargetPlayer ? playerIdleLayers : aiIdleLayers;
            LayeredMageAnimation? walk = wound.TargetPlayer ? playerWalkLayers : aiWalkLayers;
            if (renderer == null || renderer.sprite == null || wound.CanonicalPixels.Count == 0)
            {
                source = default;
                ground = default;
                return false;
            }

            LayeredMageFrame? frame = FindLayeredFrame(renderer.sprite, idle, walk);
            if (frame == null)
            {
                source = default;
                ground = default;
                return false;
            }

            Vector2 canonical = wound.CanonicalPixels[UnityEngine.Random.Range(0, wound.CanonicalPixels.Count)];
            int pixelIndex = FindClosestCanonicalPixel(frame, canonical);
            Vector2 pixel = new(pixelIndex % frame.Width + 0.5f, pixelIndex / frame.Width + 0.5f);
            Vector2 local = (pixel - renderer.sprite.pivot) / PixelsPerUnit;
            if (renderer.flipX)
                local.x = -local.x;
            source = renderer.transform.TransformPoint(local);
            ground = new Vector2(source.x, renderer.transform.position.y);
            return true;
        }

        private static int FindClosestCanonicalPixel(LayeredMageFrame frame, Vector2 canonical)
        {
            int bestIndex = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < frame.CanonicalCoordinates.Length; i++)
            {
                float distance = (frame.CanonicalCoordinates[i] - canonical).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestIndex = i;
                if (distance < 0.001f)
                    break;
            }
            return bestIndex;
        }

        private void AddFragmentWound(PixelFragment fragment, IReadOnlyList<Vector2> pixels,
            int fleshRemoved)
        {
            if (fleshRemoved <= 0 || pixels.Count == 0)
                return;
            fragment.Wounds.Add(new FragmentWound
            {
                Pixels = SelectWoundSamples(pixels, 14),
                FleshRemoved = fleshRemoved,
                BleedUntil = Time.time + 5f,
                NextBloodTime = Time.time + UnityEngine.Random.Range(0.035f, 0.085f)
            });
        }

        private static List<Vector2> SelectWoundSamples(IReadOnlyList<Vector2> pixels, int maximum)
        {
            List<Vector2> selected = new(Mathf.Min(pixels.Count, maximum));
            if (pixels.Count <= maximum)
            {
                for (int i = 0; i < pixels.Count; i++)
                    selected.Add(pixels[i]);
                return selected;
            }

            for (int i = 0; i < maximum; i++)
            {
                int index = Mathf.RoundToInt(i * (pixels.Count - 1f) / (maximum - 1f));
                selected.Add(pixels[index]);
            }
            return selected;
        }

        private static float BleedingIntensity(int fleshRemoved)
        {
            return Mathf.Lerp(1f, 3.5f, Mathf.InverseLerp(1f, 48f, fleshRemoved));
        }

        private void UpdateEffects()
        {
            float delta = Time.deltaTime;
            for (int i = effectPixels.Count - 1; i >= 0; i--)
            {
                EffectPixel pixel = effectPixels[i];
                pixel.Life -= delta;
                pixel.Velocity += Vector2.down * (pixel.Gravity * delta);
                pixel.Position += pixel.Velocity * delta;
                pixel.Object.transform.position = new Vector3(pixel.Position.x, pixel.Position.y, 0f);
                float normalized = Mathf.Clamp01(pixel.Life / pixel.MaxLife);
                pixel.Object.transform.localScale = Vector3.one * (pixel.StartScale * Mathf.Lerp(0.45f, 1f, normalized));
                Color color = pixel.Renderer.color;
                color.a = normalized;
                pixel.Renderer.color = color;
                if (pixel.Life <= 0f)
                {
                    Destroy(pixel.Object);
                    effectPixels.RemoveAt(i);
                }
            }

            UpdateMageBurning(playerAlive, playerRenderer, playerIdleLayers, playerWalkLayers,
                playerBurnUntil, ref nextPlayerBurnParticleTime);
            UpdateMageBurning(aiAlive, aiRenderer, aiIdleLayers, aiWalkLayers,
                aiBurnUntil, ref nextBurnParticleTime);
        }

        private void UpdateMageBurning(bool alive, SpriteRenderer? renderer,
            LayeredMageAnimation? idleLayers, LayeredMageAnimation? walkLayers,
            float burnUntil, ref float nextParticleTime)
        {
            if (!alive || renderer == null || Time.time >= burnUntil || Time.time < nextParticleTime)
                return;

            nextParticleTime = Time.time + UnityEngine.Random.Range(0.045f, 0.085f);
            LayeredMageFrame? frame = renderer.sprite != null
                ? FindLayeredFrame(renderer.sprite, idleLayers, walkLayers)
                : null;
            if (frame == null || !TryPickBurningPixel(frame.BurningMask, frame.OuterPixels,
                    frame.FleshPixels, frame.BonePixels, frame.Width, out Vector2 pixel))
                return;

            float visualX = renderer.flipX ? frame.Width - pixel.x : pixel.x;
            Vector2 local = (new Vector2(visualX, pixel.y) - frame.Sprite.pivot) / PixelsPerUnit;
            Vector2 source = renderer.transform.TransformPoint(local);
            SpawnFlameFromBurningPixel(source, Vector2.zero);
        }

        private void UpdateBlood()
        {
            float delta = Time.deltaTime;
            for (int i = bloodPixels.Count - 1; i >= 0; i--)
            {
                BloodPixel blood = bloodPixels[i];
                blood.Life -= delta;
                if (!blood.Landed)
                {
                    blood.GroundPosition += blood.PlanarVelocity * delta;
                    blood.PlanarVelocity *= Mathf.Pow(0.28f, delta);
                    blood.VerticalVelocity -= 6.4f * delta;
                    blood.Height += blood.VerticalVelocity * delta;
                    if (blood.Height <= 0f)
                    {
                        blood.Height = 0f;
                        blood.Landed = true;
                        blood.PlanarVelocity = Vector2.zero;
                        blood.VerticalVelocity = 0f;
                        blood.Renderer.sortingOrder = 1;
                        blood.Renderer.color = Color.Lerp(blood.Renderer.color, BloodDark, 0.46f);
                        blood.Object.transform.localScale = new Vector3(blood.StartScale * 1.45f,
                            blood.StartScale * 0.62f, 1f);
                    }
                }

                blood.GroundPosition = ClampFragmentToArena(blood.GroundPosition);
                blood.Object.transform.position = new Vector3(blood.GroundPosition.x,
                    blood.GroundPosition.y + blood.Height, 0f);
                if (blood.Landed && blood.Life < 1.6f)
                {
                    Color faded = blood.Renderer.color;
                    faded.a = Mathf.Clamp01(blood.Life / 1.6f);
                    blood.Renderer.color = faded;
                }

                if (blood.Life <= 0f)
                {
                    Destroy(blood.Object);
                    bloodPixels.RemoveAt(i);
                }
            }
        }

        private void UpdateAi()
        {
            if (Time.time >= nextAiDecisionTime || Vector2.Distance(aiPosition, aiTarget) < 0.35f)
                PickAiTarget();

            Vector2 direction = aiTarget - aiPosition;
            Vector2 separation = aiPosition - playerPosition;
            if (separation.sqrMagnitude < 2.6f)
                direction += separation.normalized * 2.2f;

            aiMoving = direction.sqrMagnitude > 0.08f;
            if (!aiMoving)
                return;

            direction.Normalize();
            lastAiDirection = direction;
            aiPosition += direction * (AiSpeed * Time.deltaTime);
            aiPosition = ClampToArena(aiPosition);
            if (aiRenderer != null && Mathf.Abs(direction.x) > 0.05f)
                aiRenderer.flipX = direction.x < 0f;
        }

        private void UpdateAnimation()
        {
            int idleFrame = Mathf.FloorToInt(Time.time * 3.5f) % 4;
            int walkFrame = Mathf.FloorToInt(Time.time * 8f) % 4;
            if (playerRenderer != null)
                playerRenderer.sprite = playerMoving ? playerWalk[walkFrame] : playerIdle[idleFrame];
            if (aiRenderer != null && aiAlive)
                aiRenderer.sprite = aiMoving ? aiWalk[walkFrame] : aiIdle[idleFrame];
            ApplyMagePositions();
        }

        private void CreateInitialCorpse(Color32[] outer, Color32[] flesh, Color32[] bones,
            byte[] boneDurability, bool[] staffMask, bool[] burningMask,
            int width, int height, Vector2 cutPixel,
            Vector2 worldDirection, Vector2 sourcePivot, int fleshRemoved,
            Vector2 parentVisual, float burnUntil)
        {
            Color32[] composite = ComposeLayers(outer, flesh, bones);
            List<Color32[]> components = FindConnectedPixelComponents(composite, width, height);
            bool separated = components.Count > 1;
            for (int i = 0; i < components.Count; i++)
            {
                Color32[] component = components[i];
                Color32[] componentOuter = FilterLayerByComponent(outer, component);
                Color32[] componentFlesh = FilterLayerByComponent(flesh, component);
                Color32[] componentBones = FilterLayerByComponent(bones, component);
                byte[] componentDurability = FilterBytesByComponent(boneDurability, component);
                bool[] componentStaff = FilterBoolsByComponent(staffMask, component);
                bool[] componentBurning = FilterBoolsByComponent(burningMask, component);
                Vector2 localOutward = GetOpaqueCenter(component, width, height) - cutPixel;
                if (localOutward.sqrMagnitude < 0.01f)
                    localOutward = UnityEngine.Random.insideUnitCircle.normalized;
                Vector2 outward = localOutward.normalized;
                List<Vector2>? wounds = fleshRemoved > 0
                    ? CollectCraterWoundPixels(componentFlesh, width, height, cutPixel, 4)
                    : null;
                CreateFragmentFromLayers(componentOuter, componentFlesh, componentBones,
                    componentDurability, componentStaff, componentBurning,
                    width, height, parentVisual, sourcePivot, MageScale, 0f,
                    worldDirection * 0.18f + outward * (separated ? 0.68f : 0.22f),
                    0.035f, UnityEngine.Random.Range(1.05f, 1.7f),
                    UnityEngine.Random.Range(-115f, 115f), burnUntil, wounds,
                    Mathf.Max(1, fleshRemoved / Mathf.Max(1, components.Count)));
            }

            Vector2 damageWorld = parentVisual + (cutPixel - sourcePivot) / PixelsPerUnit * MageScale;
            SpawnCutEffects(damageWorld, worldDirection, Hex("FF7A22"));
            if (separated && fleshRemoved > 0)
                SpawnBloodBurst(damageWorld, worldDirection, 18);
        }

        private void DamageFragment(PixelFragment fragment, Vector2 worldHit, Vector2 worldDirection)
        {
            if (!TryWorldToPixel(fragment, worldHit, out Vector2Int hitPixel))
                return;

            float rotation = fragment.Object.transform.eulerAngles.z;
            Vector2 localDirection = Rotate(worldDirection, -rotation).normalized;
            Vector2Int damagePixel = new(
                Mathf.Clamp(Mathf.RoundToInt(hitPixel.x + localDirection.x * 2f), 0, fragment.Width - 1),
                Mathf.Clamp(Mathf.RoundToInt(hitPixel.y + localDirection.y * 2f), 0, fragment.PixelHeight - 1));
            LayerDamageResult damage = ApplyLayeredDamageToPixels(fragment.OuterPixels,
                fragment.FleshPixels, fragment.BonePixels, fragment.BoneDurability, fragment.StaffMask,
                fragment.BurningMask,
                fragment.Width, fragment.PixelHeight, damagePixel, 3, 5);
            fragment.Pixels = ComposeLayers(fragment.OuterPixels, fragment.FleshPixels, fragment.BonePixels);
            fragment.Texture.SetPixels32(fragment.Pixels);
            fragment.Texture.Apply(false, false);
            SpawnCutEffects(worldHit, worldDirection, Hex("FF5A1F"));
            if (damage.FleshRemoved > 0)
            {
                SpawnBloodBurst(worldHit, worldDirection, Mathf.Clamp(4 + damage.FleshRemoved / 3, 5, 12));
                AddFragmentWound(fragment, CollectCraterWoundPixels(fragment.FleshPixels,
                    fragment.Width, fragment.PixelHeight, damagePixel, 3), damage.FleshRemoved);
            }
            if (damage.BoneDamaged > 0)
                SpawnBoneChips(worldHit, worldDirection, Mathf.Clamp(3 + damage.BoneDamaged / 2, 4, 10));
            if (damage.ScorchedPixels > 0)
            {
                fragment.BurnUntil = Mathf.Max(fragment.BurnUntil, Time.time + 3.25f);
                fragment.NextFireTime = Mathf.Min(fragment.NextFireTime, Time.time + 0.035f);
            }

            ResolveFragmentConnectivity(fragment, damagePixel, worldDirection, damage, 3);
        }

        private void DamageFragmentWithBeam(PixelFragment fragment, Vector2 beamStart, Vector2 beamEnd,
            Vector2 worldDirection, byte owner, bool replicate, int authoritativeDamagePass = 0)
        {
            LayerDamageResult damage = default;
            Vector2 totalHit = Vector2.zero;
            Vector2 totalLocal = Vector2.zero;
            int hitPixels = 0;
            int damagePass = authoritativeDamagePass > 0 ? authoritativeDamagePass : ++damageSequence;
            damageSequence = Mathf.Max(damageSequence, damagePass);
            Color32[] oldFlesh = (Color32[])fragment.FleshPixels.Clone();
            for (int y = 0; y < fragment.PixelHeight; y++)
            for (int x = 0; x < fragment.Width; x++)
            {
                int index = y * fragment.Width + x;
                if (fragment.OuterPixels[index].a == 0 && fragment.FleshPixels[index].a == 0 &&
                    fragment.BonePixels[index].a == 0)
                    continue;
                Vector2 localPixel = new(x + 0.5f, y + 0.5f);
                Vector2 local = (localPixel - fragment.Sprite.pivot) / PixelsPerUnit;
                Vector2 world = fragment.Object.transform.TransformPoint(local);
                if (SegmentPointDistanceSquared(beamStart, beamEnd, world) > BeamHalfWidth * BeamHalfWidth)
                    continue;

                hitPixels++;
                totalHit += world;
                totalLocal += localPixel;
                if (fragment.OuterPixels[index].a > 0)
                {
                    fragment.OuterPixels[index] = default;
                    fragment.BurningMask[index] = false;
                    damage.OuterRemoved++;
                }
                if (fragment.FleshPixels[index].a > 0 && ShouldBeamRemoveFlesh(x, y, damagePass))
                {
                    fragment.FleshPixels[index] = default;
                    fragment.BurningMask[index] = false;
                    damage.FleshRemoved++;
                }
            }

            if (hitPixels == 0 || (damage.OuterRemoved == 0 && damage.FleshRemoved == 0))
                return;

            Vector2 worldHit = totalHit / hitPixels;
            Vector2 averageLocal = totalLocal / hitPixels;
            Vector2Int damagePixel = new(Mathf.Clamp(Mathf.FloorToInt(averageLocal.x), 0, fragment.Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(averageLocal.y), 0, fragment.PixelHeight - 1));
            fragment.Pixels = ComposeLayers(fragment.OuterPixels, fragment.FleshPixels, fragment.BonePixels);
            fragment.Texture.SetPixels32(fragment.Pixels);
            fragment.Texture.Apply(false, false);
            if (replicate)
                network?.SendFragmentHit(owner, WildMagicNetworkSpell.WideBeam, worldHit,
                    worldDirection, beamStart, beamEnd, damagePass);
            SpawnBeamImpactEffects(worldHit, worldDirection, damage.FleshRemoved);
            if (damage.FleshRemoved > 0)
            {
                List<Vector2> woundPixels = new();
                for (int i = 0; i < oldFlesh.Length; i++)
                    if (oldFlesh[i].a > 0 && fragment.FleshPixels[i].a == 0)
                        woundPixels.Add(new Vector2(i % fragment.Width + 0.5f,
                            i / fragment.Width + 0.5f));
                AddFragmentWound(fragment, woundPixels, damage.FleshRemoved);
            }
            ResolveFragmentConnectivity(fragment, damagePixel, worldDirection, damage, 5);
        }

        private void ResolveFragmentConnectivity(PixelFragment fragment, Vector2Int damagePixel,
            Vector2 worldDirection, LayerDamageResult damage, int woundRadius)
        {
            Vector2 damageLocal = (new Vector2(damagePixel.x + 0.5f, damagePixel.y + 0.5f) -
                                   fragment.Sprite.pivot) / PixelsPerUnit;
            Vector2 worldHit = fragment.Object.transform.TransformPoint(damageLocal);
            List<Color32[]> components = FindConnectedPixelComponents(fragment.Pixels,
                fragment.Width, fragment.PixelHeight);
            if (components.Count == 0)
            {
                RemoveFragment(fragment);
                return;
            }
            if (components.Count == 1)
            {
                return;
            }

            Vector2 parentVisual = fragment.Object.transform.position;
            Vector2 inheritedVelocity = fragment.PlanarVelocity + worldDirection * 0.16f;
            float inheritedVertical = Mathf.Max(0f, fragment.VerticalVelocity);
            float scale = fragment.Object.transform.localScale.x;
            Vector2 pivot = fragment.Sprite.pivot;
            float height = fragment.Height;
            float rotation = fragment.Object.transform.eulerAngles.z;

            for (int i = 0; i < components.Count; i++)
            {
                Color32[] component = components[i];
                Color32[] componentOuter = FilterLayerByComponent(fragment.OuterPixels, component);
                Color32[] componentFlesh = FilterLayerByComponent(fragment.FleshPixels, component);
                Color32[] componentBones = FilterLayerByComponent(fragment.BonePixels, component);
                byte[] componentDurability = FilterBytesByComponent(fragment.BoneDurability, component);
                bool[] componentStaff = FilterBoolsByComponent(fragment.StaffMask, component);
                bool[] componentBurning = FilterBoolsByComponent(fragment.BurningMask, component);
                Vector2 localOutward = GetOpaqueCenter(component, fragment.Width, fragment.PixelHeight) - damagePixel;
                if (localOutward.sqrMagnitude < 0.01f)
                    localOutward = UnityEngine.Random.insideUnitCircle.normalized;
                Vector2 outward = Rotate(localOutward.normalized, rotation);
                List<Vector2> wounds = CollectCraterWoundPixels(componentFlesh, fragment.Width,
                    fragment.PixelHeight, damagePixel, woundRadius);
                CreateFragmentFromLayers(componentOuter, componentFlesh, componentBones,
                    componentDurability, componentStaff, componentBurning,
                    fragment.Width, fragment.PixelHeight,
                    parentVisual, pivot, scale, rotation, inheritedVelocity + outward * 0.62f,
                    height + 0.015f, inheritedVertical + UnityEngine.Random.Range(0.7f, 1.25f),
                    fragment.AngularVelocity + UnityEngine.Random.Range(-95f, 95f), fragment.BurnUntil, wounds,
                    Mathf.Max(1, damage.FleshRemoved / Mathf.Max(1, components.Count)));
            }
            RemoveFragment(fragment);
            if (damage.FleshRemoved > 0)
                SpawnBloodBurst(worldHit, worldDirection, 15);
        }

        private PixelFragment? CreateFragmentFromLayers(Color32[] outer, Color32[] flesh, Color32[] bones,
            byte[] boneDurability, bool[] staffMask, bool[] burningMask, int width, int height,
            Vector2 parentVisualPosition, Vector2 parentPivot, float scale, float rotation,
            Vector2 velocity, float heightAboveGround, float verticalVelocity, float angularVelocity,
            float burnUntil, List<Vector2>? woundPixels = null, int woundSeverity = 0)
        {
            Color32[] composite = ComposeLayers(outer, flesh, bones);
            if (!TryCropOpaque(composite, width, height, out Color32[] cropped, out int cropWidth,
                    out int cropHeight, out int minX, out int minY))
                return null;

            Color32[] croppedOuter = CropColors(outer, width, minX, minY, cropWidth, cropHeight);
            Color32[] croppedFlesh = CropColors(flesh, width, minX, minY, cropWidth, cropHeight);
            Color32[] croppedBones = CropColors(bones, width, minX, minY, cropWidth, cropHeight);
            byte[] croppedDurability = CropBytes(boneDurability, width, minX, minY, cropWidth, cropHeight);
            bool[] croppedStaff = CropBools(staffMask, width, minX, minY, cropWidth, cropHeight);
            bool[] croppedBurning = CropBools(burningMask, width, minX, minY, cropWidth, cropHeight);

            Texture2D texture = new(cropWidth, cropHeight, TextureFormat.RGBA32, false)
            {
                name = "Severed mage pixels",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(cropped);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, cropWidth, cropHeight),
                new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprite.name = "Physical pixel fragment";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            Vector2 cropPivotInParent = new(minX + cropWidth * 0.5f, minY + cropHeight * 0.5f);
            Vector2 localOffset = (cropPivotInParent - parentPivot) / PixelsPerUnit * scale;
            Vector2 worldOffset = Rotate(localOffset, rotation);
            Vector2 visualPosition = parentVisualPosition + worldOffset;

            GameObject pieceObject = new("Severed Sprite Piece");
            pieceObject.transform.SetParent(transform, false);
            pieceObject.transform.position = new Vector3(visualPosition.x, visualPosition.y, 0f);
            pieceObject.transform.localScale = Vector3.one * scale;
            pieceObject.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            SpriteRenderer renderer = pieceObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 7;

            GameObject shadow = new("Fragment Shadow");
            shadow.transform.SetParent(transform, false);
            shadow.transform.position = new Vector3(visualPosition.x, visualPosition.y - heightAboveGround, 0f);
            shadow.transform.localScale = new Vector3(
                Mathf.Clamp(cropWidth / PixelsPerUnit * scale * 0.58f, 0.16f, 0.62f), 0.085f, 1f);
            SpriteRenderer shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = whiteSprite;
            shadowRenderer.color = new Color(0f, 0f, 0f, 0.34f);
            shadowRenderer.sortingOrder = 3;

            PixelFragment fragment = new()
            {
                Object = pieceObject,
                Renderer = renderer,
                Shadow = shadow,
                ShadowRenderer = shadowRenderer,
                Sprite = sprite,
                Texture = texture,
                Pixels = cropped,
                OuterPixels = croppedOuter,
                FleshPixels = croppedFlesh,
                BonePixels = croppedBones,
                BoneDurability = croppedDurability,
                StaffMask = croppedStaff,
                BurningMask = croppedBurning,
                Width = cropWidth,
                PixelHeight = cropHeight,
                GroundPosition = visualPosition - Vector2.up * heightAboveGround,
                PlanarVelocity = velocity,
                Height = heightAboveGround,
                VerticalVelocity = verticalVelocity,
                AngularVelocity = angularVelocity,
                BurnUntil = burnUntil,
                NextFireTime = Time.time + UnityEngine.Random.Range(0.025f, 0.07f)
            };
            if (woundPixels != null)
            {
                List<Vector2> croppedWounds = new(woundPixels.Count);
                foreach (Vector2 woundPixel in woundPixels)
                    croppedWounds.Add(woundPixel - new Vector2(minX, minY));
                AddFragmentWound(fragment, croppedWounds, woundSeverity);
            }
            fragments.Add(fragment);
            return fragment;
        }

        private static List<Color32[]> FindConnectedPixelComponents(Color32[] source, int width, int height)
        {
            bool[] visited = new bool[source.Length];
            List<Color32[]> components = new();
            Stack<int> pending = new();
            List<int> indices = new();

            for (int start = 0; start < source.Length; start++)
            {
                if (visited[start] || source[start].a == 0)
                    continue;

                pending.Clear();
                indices.Clear();
                pending.Push(start);
                visited[start] = true;
                while (pending.Count > 0)
                {
                    int index = pending.Pop();
                    indices.Add(index);
                    int x = index % width;
                    int y = index / width;
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                            continue;
                        int nextX = x + offsetX;
                        int nextY = y + offsetY;
                        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                            continue;
                        int next = nextY * width + nextX;
                        if (visited[next] || source[next].a == 0)
                            continue;
                        visited[next] = true;
                        pending.Push(next);
                    }
                }

                // Single isolated sparks and loose pixels become impact debris instead of
                // permanent physics objects. Everything larger remains a real fragment.
                if (indices.Count < 3)
                    continue;
                Color32[] component = new Color32[source.Length];
                foreach (int index in indices)
                    component[index] = source[index];
                components.Add(component);
            }

            components.Sort((left, right) => CountOpaque(right).CompareTo(CountOpaque(left)));
            return components;
        }

        private static int CountOpaque(Color32[] pixels)
        {
            int count = 0;
            foreach (Color32 pixel in pixels)
                if (pixel.a > 0)
                    count++;
            return count;
        }

        private static Vector2 GetOpaqueCenter(Color32[] pixels, int width, int height)
        {
            Vector2 total = Vector2.zero;
            int count = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a == 0)
                    continue;
                total += new Vector2(x + 0.5f, y + 0.5f);
                count++;
            }
            return count > 0 ? total / count : new Vector2(width * 0.5f, height * 0.5f);
        }

        private static List<Vector2> CollectCraterWoundPixels(Color32[] component, int width, int height,
            Vector2 craterCenter, int craterRadius)
        {
            List<Vector2> candidates = new();
            float maximumDistance = craterRadius + 1.65f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (component[y * width + x].a == 0)
                    continue;
                float distance = Vector2.Distance(new Vector2(x, y), craterCenter);
                if (distance >= craterRadius - 0.35f && distance <= maximumDistance)
                    candidates.Add(new Vector2(x + 0.5f, y + 0.5f));
            }

            if (candidates.Count <= 5)
                return candidates;
            List<Vector2> selected = new(5);
            for (int i = 0; i < 5; i++)
                selected.Add(candidates[Mathf.RoundToInt(i * (candidates.Count - 1) / 4f)]);
            return selected;
        }

        private static bool TryCropOpaque(Color32[] source, int width, int height, out Color32[] cropped,
            out int cropWidth, out int cropHeight, out int minX, out int minY)
        {
            minX = width;
            minY = height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (source[y * width + x].a == 0)
                    continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }

            if (maxX < minX || maxY < minY)
            {
                cropped = Array.Empty<Color32>();
                cropWidth = 0;
                cropHeight = 0;
                return false;
            }

            cropWidth = maxX - minX + 1;
            cropHeight = maxY - minY + 1;
            cropped = new Color32[cropWidth * cropHeight];
            for (int y = 0; y < cropHeight; y++)
            for (int x = 0; x < cropWidth; x++)
                cropped[y * cropWidth + x] = source[(minY + y) * width + minX + x];
            return true;
        }

        private static Color32[] FilterLayerByComponent(Color32[] layer, Color32[] component)
        {
            Color32[] filtered = new Color32[layer.Length];
            for (int i = 0; i < layer.Length; i++)
                if (component[i].a > 0)
                    filtered[i] = layer[i];
            return filtered;
        }

        private static byte[] FilterBytesByComponent(byte[] source, Color32[] component)
        {
            byte[] filtered = new byte[source.Length];
            for (int i = 0; i < source.Length; i++)
                if (component[i].a > 0)
                    filtered[i] = source[i];
            return filtered;
        }

        private static bool[] FilterBoolsByComponent(bool[] source, Color32[] component)
        {
            bool[] filtered = new bool[source.Length];
            for (int i = 0; i < source.Length; i++)
                filtered[i] = component[i].a > 0 && source[i];
            return filtered;
        }

        private static Color32[] CropColors(Color32[] source, int sourceWidth, int minX, int minY,
            int cropWidth, int cropHeight)
        {
            Color32[] cropped = new Color32[cropWidth * cropHeight];
            for (int y = 0; y < cropHeight; y++)
            for (int x = 0; x < cropWidth; x++)
                cropped[y * cropWidth + x] = source[(minY + y) * sourceWidth + minX + x];
            return cropped;
        }

        private static byte[] CropBytes(byte[] source, int sourceWidth, int minX, int minY,
            int cropWidth, int cropHeight)
        {
            byte[] cropped = new byte[cropWidth * cropHeight];
            for (int y = 0; y < cropHeight; y++)
            for (int x = 0; x < cropWidth; x++)
                cropped[y * cropWidth + x] = source[(minY + y) * sourceWidth + minX + x];
            return cropped;
        }

        private static bool[] CropBools(bool[] source, int sourceWidth, int minX, int minY,
            int cropWidth, int cropHeight)
        {
            bool[] cropped = new bool[cropWidth * cropHeight];
            for (int y = 0; y < cropHeight; y++)
            for (int x = 0; x < cropWidth; x++)
                cropped[y * cropWidth + x] = source[(minY + y) * sourceWidth + minX + x];
            return cropped;
        }

        private bool TryFindFragmentHit(Vector2 start, Vector2 end, out PixelFragment? hitFragment,
            out Vector2 hitPoint)
        {
            float distance = Vector2.Distance(start, end);
            int steps = Mathf.Clamp(Mathf.CeilToInt(distance / 0.035f), 2, 32);
            for (int step = 0; step <= steps; step++)
            {
                Vector2 sample = Vector2.Lerp(start, end, step / (float)steps);
                for (int i = 0; i < fragments.Count; i++)
                {
                    PixelFragment candidate = fragments[i];
                    if (TryWorldToPixel(candidate, sample, out _))
                    {
                        hitFragment = candidate;
                        hitPoint = sample;
                        return true;
                    }
                }
            }

            hitFragment = null;
            hitPoint = default;
            return false;
        }

        private static bool TryWorldToPixel(PixelFragment fragment, Vector2 world, out Vector2Int pixel)
        {
            Vector2 offset = world - (Vector2)fragment.Object.transform.position;
            Vector2 local = Rotate(offset, -fragment.Object.transform.eulerAngles.z) /
                            fragment.Object.transform.localScale.x;
            int x = Mathf.FloorToInt(fragment.Sprite.pivot.x + local.x * fragment.Sprite.pixelsPerUnit);
            int y = Mathf.FloorToInt(fragment.Sprite.pivot.y + local.y * fragment.Sprite.pixelsPerUnit);
            pixel = new Vector2Int(x, y);
            return x >= 0 && x < fragment.Width && y >= 0 && y < fragment.PixelHeight &&
                   fragment.Pixels[y * fragment.Width + x].a > 0;
        }

        private void SpawnCutEffects(Vector2 position, Vector2 direction, Color color)
        {
            Vector2 normal = new(-direction.y, direction.x);
            for (int i = 0; i < 14; i++)
            {
                Vector2 velocity = direction * UnityEngine.Random.Range(0.1f, 1.2f) +
                                   normal * UnityEngine.Random.Range(-1.7f, 1.7f);
                SpawnEffectPixel(position + normal * UnityEngine.Random.Range(-0.18f, 0.18f), velocity,
                    i % 3 == 0 ? Hex("FFD04A") : color, UnityEngine.Random.Range(0.35f, 0.72f),
                    UnityEngine.Random.Range(0.045f, 0.09f), 2.6f);
            }
        }

        private void SpawnBoneChips(Vector2 position, Vector2 direction, int count)
        {
            Vector2 normal = new(-direction.y, direction.x);
            for (int i = 0; i < count; i++)
            {
                Vector2 velocity = -direction * UnityEngine.Random.Range(0.05f, 0.55f) +
                                   normal * UnityEngine.Random.Range(-1.25f, 1.25f) +
                                   Vector2.up * UnityEngine.Random.Range(0.2f, 0.8f);
                Color color = i % 3 == 0 ? Hex("FFF0C8") : i % 3 == 1 ? Hex("D8C9A6") : Hex("7C705E");
                SpawnEffectPixel(position, velocity, color, UnityEngine.Random.Range(0.38f, 0.7f),
                    UnityEngine.Random.Range(0.035f, 0.065f), UnityEngine.Random.Range(2.8f, 4.2f));
            }
        }

        private void SpawnFlameFromBurningPixel(Vector2 source, Vector2 inheritedVelocity)
        {
            Color flame = UnityEngine.Random.value > 0.56f ? Hex("FFD04A") :
                UnityEngine.Random.value > 0.32f ? Hex("FF8A24") : Hex("F0461C");
            Color ember = UnityEngine.Random.value > 0.45f ? Hex("FF9E2C") : Hex("F04A1C");
            SpawnEffectPixel(source, inheritedVelocity * 0.12f, ember,
                UnityEngine.Random.Range(0.12f, 0.22f), UnityEngine.Random.Range(0.06f, 0.09f), 0f);
            Vector2 velocity = inheritedVelocity + new Vector2(
                UnityEngine.Random.Range(-0.16f, 0.16f), UnityEngine.Random.Range(0.72f, 1.28f));
            SpawnEffectPixel(source + UnityEngine.Random.insideUnitCircle * 0.012f, velocity, flame,
                UnityEngine.Random.Range(0.28f, 0.52f), UnityEngine.Random.Range(0.045f, 0.082f), 0.18f);
        }

        private static bool TryPickBurningPixel(bool[] burningMask, Color32[] outer,
            Color32[] flesh, Color32[] bones, int width, out Vector2 pixel)
        {
            int count = 0;
            for (int i = 0; i < burningMask.Length; i++)
            {
                if (burningMask[i] && (outer[i].a > 0 || flesh[i].a > 0 || bones[i].a > 0))
                    count++;
            }

            if (count == 0)
            {
                pixel = default;
                return false;
            }

            int selected = UnityEngine.Random.Range(0, count);
            for (int i = 0; i < burningMask.Length; i++)
            {
                if (!burningMask[i] || (outer[i].a == 0 && flesh[i].a == 0 && bones[i].a == 0))
                    continue;
                if (selected-- != 0)
                    continue;
                pixel = new Vector2(i % width + 0.5f, i / width + 0.5f);
                return true;
            }

            pixel = default;
            return false;
        }

        private void SpawnBloodBurst(Vector2 position, Vector2 direction, int count)
        {
            Vector2 normal = new(-direction.y, direction.x);
            for (int i = 0; i < count; i++)
            {
                float side = UnityEngine.Random.value > 0.5f ? 1f : -1f;
                Vector2 planarVelocity = direction * UnityEngine.Random.Range(0.15f, 1.15f) +
                                         normal * side * UnityEngine.Random.Range(0.35f, 2.15f);
                SpawnBloodPixel(position + normal * UnityEngine.Random.Range(-0.12f, 0.12f),
                    UnityEngine.Random.Range(0.03f, 0.16f), planarVelocity,
                    UnityEngine.Random.Range(0.85f, 2.15f),
                    i % 4 == 0 ? BloodBright : BloodFresh,
                    UnityEngine.Random.Range(0.045f, 0.085f), UnityEngine.Random.Range(6f, 9f));
            }
        }

        private void SpawnBloodPixel(Vector2 groundPosition, float height, Vector2 planarVelocity,
            float verticalVelocity, Color color, float scale, float life)
        {
            GameObject bloodObject = new("Blood Pixel");
            bloodObject.transform.SetParent(transform, false);
            bloodObject.transform.position = new Vector3(groundPosition.x, groundPosition.y + height, 0f);
            bloodObject.transform.localScale = Vector3.one * scale;
            SpriteRenderer renderer = bloodObject.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = 12;
            bloodPixels.Add(new BloodPixel
            {
                Object = bloodObject,
                Renderer = renderer,
                GroundPosition = groundPosition,
                PlanarVelocity = planarVelocity,
                Height = height,
                VerticalVelocity = verticalVelocity,
                Life = life,
                MaxLife = life,
                StartScale = scale
            });
        }

        private void PickAiTarget()
        {
            aiTarget = new Vector2(
                UnityEngine.Random.Range(-ArenaHalfWidth + 0.8f, ArenaHalfWidth - 0.8f),
                UnityEngine.Random.Range(-ArenaHalfHeight + 0.75f, ArenaHalfHeight - 0.75f));
            nextAiDecisionTime = Time.time + UnityEngine.Random.Range(1.4f, 3.2f);
        }

        private void ApplyMagePositions()
        {
            if (playerRenderer != null)
                playerRenderer.transform.localPosition = new Vector3(playerPosition.x, playerPosition.y, 0f);
            if (aiRenderer != null)
                aiRenderer.transform.localPosition = new Vector3(aiPosition.x, aiPosition.y, 0f);
        }

        private void RequestNewMatch()
        {
            if (network != null && network.IsClient)
                network.SendRestartRequest();
            else
                StartNewMatch();
        }

        private void OnOpponentConnected()
        {
            if (network != null && network.IsHost)
                StartNewMatch();
        }

        private void OnOpponentDisconnected()
        {
            if (network != null && network.IsHost)
                StartNewMatch(broadcast: false);
        }

        private void OnNetworkRestartRequested()
        {
            if (network != null && network.IsHost)
                StartNewMatch();
        }

        private void OnMatchSetupReceived(int playerOneColor, int playerTwoColor, uint round)
        {
            if (network == null || !network.IsClient || round < roundSequence)
                return;
            roundSequence = round;
            StartNewMatch(playerOneColor, playerTwoColor, false);
        }

        private void OnSnapshotReceived(WildMagicNetworkSnapshot snapshot)
        {
            if (network == null || !network.IsClient)
                return;

            playerPosition = snapshot.PlayerOnePosition;
            aiPosition = Vector2.Lerp(aiPosition, snapshot.PlayerTwoPosition, 0.55f);
            lastPlayerDirection = snapshot.PlayerOneDirection;
            if (!aiMoving)
                lastAiDirection = snapshot.PlayerTwoDirection;
            playerMoving = snapshot.PlayerOneMoving;
            aiMoving = snapshot.PlayerTwoMoving;
            playerDamageUnits = snapshot.PlayerOneDamage;
            aiDamageUnits = snapshot.PlayerTwoDamage;
            RefreshPlayerHud();
            RefreshAiHud();

            if (playerRenderer != null && Mathf.Abs(lastPlayerDirection.x) > 0.05f)
                playerRenderer.flipX = lastPlayerDirection.x < 0f;
            if (aiRenderer != null && Mathf.Abs(lastAiDirection.x) > 0.05f)
                aiRenderer.flipX = lastAiDirection.x < 0f;
        }

        private void OnNetworkAttackReceived(WildMagicNetworkAttack attack)
        {
            if (attack.Spell == WildMagicNetworkSpell.Fireball)
                SpawnFireball(attack.Origin, attack.Direction, attack.Shooter);
            else
                CastWideBeam(attack.Origin, attack.Direction, attack.Shooter, false);
        }

        private void OnNetworkHitReceived(WildMagicNetworkHit hit)
        {
            bool targetPlayer = hit.Target == 0;
            if (hit.Spell == WildMagicNetworkSpell.Fireball)
            {
                ConsumeNetworkFireball(targetPlayer ? (byte)1 : (byte)0, hit.Impact);
                if (targetPlayer ? playerAlive : aiAlive)
                    HitLivingMage(targetPlayer, hit.Impact, hit.Direction, false, true,
                        hit.CanonicalHit, hit.DamagePass);
            }
            else if (targetPlayer ? playerAlive : aiAlive)
            {
                HitLivingMageWithBeam(targetPlayer, hit.Impact, hit.Direction, false, true,
                    hit.CanonicalHit, hit.PixelDirection, hit.DamagePass);
            }
        }

        private void OnNetworkFragmentHitReceived(WildMagicNetworkFragmentHit hit)
        {
            if (!TryFindFragmentHit(hit.Impact, hit.Impact, out PixelFragment? fragment, out Vector2 fragmentHit))
                return;

            if (hit.Spell == WildMagicNetworkSpell.Fireball)
            {
                ConsumeNetworkFireball(hit.Shooter, hit.Impact);
                DamageFragment(fragment!, fragmentHit, hit.Direction);
            }
            else
            {
                DamageFragmentWithBeam(fragment!, hit.BeamStart, hit.BeamEnd, hit.Direction,
                    hit.Shooter, false, hit.DamagePass);
            }
        }

        private void ConsumeNetworkFireball(byte owner, Vector2 impact)
        {
            int nearest = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < fireballs.Count; i++)
            {
                if (fireballs[i].Owner != owner)
                    continue;
                float distance = (fireballs[i].Position - impact).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearest = i;
            }
            if (nearest >= 0)
                DestroyFireballAt(nearest);
        }

        private void SendNetworkSnapshot()
        {
            if (network == null || Time.unscaledTime < nextSnapshotTime)
                return;
            nextSnapshotTime = Time.unscaledTime + 1f / 20f;
            network.SendSnapshot(new WildMagicNetworkSnapshot
            {
                Sequence = ++snapshotSequence,
                PlayerOnePosition = playerPosition,
                PlayerTwoPosition = aiPosition,
                PlayerOneDirection = lastPlayerDirection,
                PlayerTwoDirection = lastAiDirection,
                PlayerOneMoving = playerMoving,
                PlayerTwoMoving = aiMoving,
                PlayerOneAlive = playerAlive,
                PlayerTwoAlive = aiAlive,
                PlayerOneDamage = playerDamageUnits,
                PlayerTwoDamage = aiDamageUnits
            });
        }

        /// <summary>Entry point for the upcoming spell/health system.</summary>
        public void DefeatMage(bool playerWasDefeated)
        {
            if (matchFinished)
                return;

            matchFinished = true;
            pauseOpen = false;
            winnerName = playerWasDefeated ? aiDisplayName : playerDisplayName;
        }

        private void OnGUI()
        {
            if (!pauseOpen && !matchFinished)
                return;

            EnsureGuiStyles();
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.72f, 1.35f);
            float width = (matchFinished ? 610f : 520f) * scale;
            float height = (matchFinished ? 145f : 380f) * scale;
            if (pauseOpen)
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), overlayTexture!, ScaleMode.StretchToFill);
            Rect panel = matchFinished
                ? new Rect((Screen.width - width) * 0.5f, 22f * scale, width, height)
                : new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.DrawTexture(panel, panelTexture!, ScaleMode.StretchToFill);

            string title = matchFinished ? $"ПОБЕДИЛ: {winnerName}" : "БОЙ ПРИОСТАНОВЛЕН";
            float titleY = matchFinished ? 12f : 42f;
            GUI.Label(new Rect(panel.x + 30f * scale, panel.y + titleY * scale, panel.width - 60f * scale, 55f * scale),
                title, titleStyle!);

            if (!matchFinished)
            {
                GUI.Label(new Rect(panel.x + 40f * scale, panel.y + 105f * scale, panel.width - 80f * scale, 55f * scale),
                    "ESC — вернуться на арену", bodyStyle!);
                if (GUI.Button(new Rect(panel.x + 70f * scale, panel.y + 180f * scale,
                        panel.width - 140f * scale, 58f * scale), "ПРОДОЛЖИТЬ", buttonStyle!))
                    pauseOpen = false;
            }

            float restartY = matchFinished ? 75f : 265f;
            float sidePadding = matchFinished ? 165f : 70f;
            if (GUI.Button(new Rect(panel.x + sidePadding * scale, panel.y + restartY * scale,
                    panel.width - sidePadding * 2f * scale, 48f * scale), "НАЧАТЬ ЗАНОВО", buttonStyle!))
                RequestNewMatch();
        }

        private void RefreshPlayerHud()
        {
            if (playerHudName == null)
                return;
            string state = playerAlive
                ? new string('◆', Mathf.Clamp(playerDamageUnits, 0, MaxDamageUnits)) +
                  new string('◇', Mathf.Clamp(MaxDamageUnits - playerDamageUnits, 0, MaxDamageUnits))
                : "УНИЧТОЖЕН";
            Color color = Palette[Mathf.Clamp(currentPlayerColorIndex, 0, Palette.Length - 1)].Robe;
            ApplyName(playerHudName, $"ИГРОК 1  //  {playerDisplayName}  {state}", color);
        }

        private void RefreshAiHud()
        {
            if (aiHudName == null)
                return;

            string state = aiAlive
                ? new string('◆', Mathf.Clamp(aiDamageUnits, 0, MaxDamageUnits)) +
                  new string('◇', Mathf.Clamp(MaxDamageUnits - aiDamageUnits, 0, MaxDamageUnits))
                : "УНИЧТОЖЕН";
            string role = network != null && (network.HasOpponent || network.IsClient) ? "ИГРОК 2" : "ИИ";
            ApplyName(aiHudName, $"{role}  //  {aiDisplayName}  {state}", aiDisplayColor);
        }

        private void RefreshNetworkHud()
        {
            if (networkLabel == null || network == null)
                return;
            networkLabel.text = network.Status;
            networkLabel.color = network.HasOpponent ? Hex("79F2A6") : Hex("F2C879");
        }

        private void RefreshSpellHud()
        {
            if (spellLabel == null)
                return;
            bool fireball = selectedSpell == SpellKind.Fireball;
            ApplyName(spellLabel,
                fireball
                    ? "ЗАКЛИНАНИЕ: ОГНЕННЫЙ ШАР    [КОЛЕСО — СМЕНИТЬ]"
                    : "ЗАКЛИНАНИЕ: ШИРОКИЙ ЛУЧ    [КОЛЕСО — СМЕНИТЬ]",
                fireball ? Hex("FFB14A") : Hex("6FEAFF"));
        }

        private static LayeredMageFrame? FindLayeredFrame(Sprite sprite,
            params LayeredMageAnimation?[] animations)
        {
            foreach (LayeredMageAnimation? animation in animations)
            {
                if (animation == null)
                    continue;
                foreach (LayeredMageFrame frame in animation.Frames)
                    if (frame.Sprite == sprite)
                        return frame;
            }
            return null;
        }

        private static LayerDamageResult ApplyLayeredDamage(LayeredMageAnimation? animation,
            Vector2 center, int radius, int damagePass, LayeredMageFrame? skipFrame = null)
        {
            LayerDamageResult total = default;
            if (animation == null)
                return total;
            foreach (LayeredMageFrame frame in animation.Frames)
            {
                if (frame == skipFrame)
                    continue;
                LayerDamageResult frameDamage = ApplyLayeredDamage(frame, center, radius, damagePass);
                total.Add(frameDamage);
            }
            return total;
        }

        private static LayerDamageResult ApplyLayeredDamage(LayeredMageFrame frame,
            Vector2 center, int radius, int damagePass)
        {
            LayerDamageResult damage = ApplyLayeredDamageToPixels(frame.OuterPixels,
                frame.FleshPixels, frame.BonePixels, frame.BoneDurability, frame.StaffMask,
                frame.BurningMask,
                frame.Width, frame.Height, center, radius, damagePass, frame.CanonicalCoordinates);
            RebuildComposite(frame);
            return damage;
        }

        private static LayerDamageResult ApplyBeamDamage(LayeredMageAnimation? animation,
            Vector2 center, Vector2 direction, int damagePass, LayeredMageFrame? skipFrame = null)
        {
            LayerDamageResult total = default;
            if (animation == null)
                return total;
            foreach (LayeredMageFrame frame in animation.Frames)
            {
                if (frame == skipFrame)
                    continue;
                LayerDamageResult frameDamage = ApplyBeamDamage(frame, center, direction, damagePass);
                total.Add(frameDamage);
            }
            return total;
        }

        private static LayerDamageResult ApplyBeamDamage(LayeredMageFrame frame,
            Vector2 center, Vector2 direction, int damagePass)
        {
            LayerDamageResult damage = ApplyBeamDamageToPixels(frame.OuterPixels, frame.FleshPixels,
                frame.BonePixels, frame.BurningMask, frame.Width, frame.Height, center, direction,
                4.5f, damagePass, frame.CanonicalCoordinates);
            RebuildComposite(frame);
            return damage;
        }

        private static LayerDamageResult ApplyBeamDamageToPixels(Color32[] outer, Color32[] flesh,
            Color32[] bones, bool[] burningMask, int width, int height, Vector2 center,
            Vector2 direction, float halfWidth, int damagePass, Vector2[]? canonicalCoordinates = null)
        {
            LayerDamageResult result = default;
            Vector2 normal = new(-direction.y, direction.x);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Vector2 coordinate = canonicalCoordinates != null
                    ? canonicalCoordinates[index]
                    : new Vector2(x, y);
                Vector2 offset = coordinate - center;
                if (Mathf.Abs(Vector2.Dot(offset, normal)) > halfWidth)
                    continue;

                if (outer[index].a > 0)
                {
                    outer[index] = default;
                    burningMask[index] = false;
                    result.OuterRemoved++;
                }

                int materialX = Mathf.RoundToInt(coordinate.x);
                int materialY = Mathf.RoundToInt(coordinate.y);
                if (flesh[index].a > 0 && ShouldBeamRemoveFlesh(materialX, materialY, damagePass))
                {
                    flesh[index] = default;
                    burningMask[index] = false;
                    result.RecordFleshRemoval(coordinate);
                }
            }
            return result;
        }

        private static bool ShouldBeamRemoveFlesh(int x, int y, int damagePass)
        {
            uint hash = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ damagePass * 83492791));
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 1u) == 0u;
        }

        private static LayerDamageResult ApplyLayeredDamageToPixels(Color32[] outer, Color32[] flesh,
            Color32[] bones, byte[] boneDurability, bool[] staffMask, bool[] burningMask,
            int width, int height,
            Vector2 center, int radius, int damagePass, Vector2[]? canonicalCoordinates = null)
        {
            LayerDamageResult result = default;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Vector2 coordinate = canonicalCoordinates != null
                    ? canonicalCoordinates[index]
                    : new Vector2(x, y);
                Vector2 delta = coordinate - center;
                float distance = delta.magnitude;
                if (distance <= radius)
                {
                    if (staffMask[index] && outer[index].a > 0)
                    {
                        outer[index] = Scorch(outer[index], Hex("160D0B"), 0.92f);
                        burningMask[index] = true;
                        result.StaffScorched++;
                        result.ScorchedPixels++;
                    }
                    else if (outer[index].a > 0)
                    {
                        outer[index] = default;
                        burningMask[index] = false;
                        result.OuterRemoved++;
                    }
                    else if (flesh[index].a > 0)
                    {
                        flesh[index] = default;
                        burningMask[index] = false;
                        result.RecordFleshRemoval(coordinate);
                    }
                    else if (bones[index].a > 0)
                    {
                        result.BoneDamaged++;
                        if (boneDurability[index] > 1)
                        {
                            boneDurability[index]--;
                            bones[index] = Scorch(bones[index], Hex("4B4238"), 0.58f);
                            burningMask[index] = true;
                            result.ScorchedPixels++;
                        }
                        else
                        {
                            boneDurability[index] = 0;
                            bones[index] = default;
                            burningMask[index] = false;
                            result.BoneRemoved++;
                        }
                    }
                    continue;
                }

                Color32 visible = outer[index].a > 0 ? outer[index] :
                    flesh[index].a > 0 ? flesh[index] : bones[index];
                if (visible.a == 0)
                    continue;

                int materialX = Mathf.RoundToInt(coordinate.x);
                int materialY = Mathf.RoundToInt(coordinate.y);
                int hash = unchecked(materialX * 73856093 ^ materialY * 19349663 ^ damagePass * 83492791);
                float strength;
                if (distance <= radius + 1.35f)
                {
                    strength = 0.91f;
                }
                else if (distance <= radius + 2.9f && (hash & 3) != 0)
                {
                    strength = 0.72f;
                }
                else
                    continue;

                if (outer[index].a > 0)
                {
                    // Cloth and equipment retain a broad, nearly black burn halo. It should
                    // look dry and heavily charred compared with the warmer tissue beneath it.
                    float clothStrength = distance <= radius + 1.35f
                        ? Mathf.Max(strength, 0.96f)
                        : Mathf.Max(strength, 0.84f);
                    outer[index] = Scorch(outer[index], Hex("0B090A"), clothStrength);
                    burningMask[index] = true;
                    result.ScorchedPixels++;
                }
                else if (flesh[index].a > 0)
                {
                    // Flesh darkens locally but keeps a readable red value. Its outer halo is
                    // intentionally faint so it never resembles the blackened robe layer.
                    float fleshStrength = distance <= radius + 1.35f ? 0.48f : strength * 0.24f;
                    flesh[index] = Scorch(flesh[index], Hex("3A1017"), fleshStrength);
                    burningMask[index] = true;
                    result.ScorchedPixels++;
                }
                else if (bones[index].a > 0)
                {
                    bones[index] = Scorch(bones[index], Hex("4B4238"), strength * 0.42f);
                    burningMask[index] = true;
                    result.ScorchedPixels++;
                }
            }
            return result;
        }

        private static Color32 Scorch(Color32 pixel, Color burnColor, float strength)
        {
            Color original = pixel;
            Color scorched = Color.Lerp(original, burnColor, strength);
            scorched.a = original.a;
            return scorched;
        }

        private static void RebuildComposite(LayeredMageFrame frame)
        {
            Color32[] composite = ComposeLayers(frame.OuterPixels, frame.FleshPixels, frame.BonePixels);
            frame.Texture.SetPixels32(composite);
            frame.Texture.Apply(false, false);
        }

        private static Color32[] ComposeLayers(Color32[] outer, Color32[] flesh, Color32[] bones)
        {
            Color32[] composite = new Color32[outer.Length];
            for (int i = 0; i < composite.Length; i++)
                composite[i] = outer[i].a > 0 ? outer[i] : flesh[i].a > 0 ? flesh[i] : bones[i];
            return composite;
        }

        private static Vector2Int WorldToPixel(Transform spriteTransform, Sprite sprite, Vector2 world,
            bool horizontallyFlipped)
        {
            Vector3 local = spriteTransform.InverseTransformPoint(world);
            int x = Mathf.FloorToInt(sprite.pivot.x + local.x * sprite.pixelsPerUnit);
            int y = Mathf.FloorToInt(sprite.pivot.y + local.y * sprite.pixelsPerUnit);
            if (horizontallyFlipped)
                x = sprite.texture.width - 1 - x;
            return new Vector2Int(Mathf.Clamp(x, 0, sprite.texture.width - 1),
                Mathf.Clamp(y, 0, sprite.texture.height - 1));
        }

        private static Vector2Int FindNearestOpaquePixel(Sprite sprite, Vector2Int origin, int searchRadius)
        {
            Color32[] pixels = sprite.texture.GetPixels32();
            int width = sprite.texture.width;
            int height = sprite.texture.height;
            for (int radius = 0; radius <= searchRadius; radius++)
            {
                for (int y = origin.y - radius; y <= origin.y + radius; y++)
                for (int x = origin.x - radius; x <= origin.x + radius; x++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height)
                        continue;
                    if (pixels[y * width + x].a > 0)
                        return new Vector2Int(x, y);
                }
            }
            return origin;
        }

        private static Color32 SamplePixel(Sprite sprite, Vector2Int pixel)
        {
            Color32[] pixels = sprite.texture.GetPixels32();
            int x = Mathf.Clamp(pixel.x, 0, sprite.texture.width - 1);
            int y = Mathf.Clamp(pixel.y, 0, sprite.texture.height - 1);
            Color32 sampled = pixels[y * sprite.texture.width + x];
            return sampled.a == 0 ? new Color32(94, 55, 73, 255) : sampled;
        }

        private static void FlipPixelsHorizontally(Color32[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width / 2; x++)
            {
                int left = y * width + x;
                int right = y * width + width - 1 - x;
                (pixels[left], pixels[right]) = (pixels[right], pixels[left]);
            }
        }

        private static void FlipPixelsHorizontally(byte[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width / 2; x++)
            {
                int left = y * width + x;
                int right = y * width + width - 1 - x;
                (pixels[left], pixels[right]) = (pixels[right], pixels[left]);
            }
        }

        private static void FlipPixelsHorizontally(bool[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width / 2; x++)
            {
                int left = y * width + x;
                int right = y * width + width - 1 - x;
                (pixels[left], pixels[right]) = (pixels[right], pixels[left]);
            }
        }

        private void SpawnEffectPixel(Vector2 position, Vector2 velocity, Color color, float life,
            float scale, float gravity)
        {
            GameObject effectObject = new("Spell Pixel");
            effectObject.transform.SetParent(transform, false);
            effectObject.transform.position = new Vector3(position.x, position.y, 0f);
            effectObject.transform.localScale = Vector3.one * scale;
            SpriteRenderer renderer = effectObject.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = 13;
            effectPixels.Add(new EffectPixel
            {
                Object = effectObject,
                Renderer = renderer,
                Position = position,
                Velocity = velocity,
                Life = life,
                MaxLife = life,
                StartScale = scale,
                Gravity = gravity
            });
        }

        private void DestroyFireballAt(int index)
        {
            Destroy(fireballs[index].Object);
            fireballs.RemoveAt(index);
        }

        private void RemoveFragment(PixelFragment fragment)
        {
            fragments.Remove(fragment);
            if (fragment.Object != null)
                Destroy(fragment.Object);
            if (fragment.Shadow != null)
                Destroy(fragment.Shadow);
            if (fragment.Sprite != null)
                Destroy(fragment.Sprite);
            if (fragment.Texture != null)
                Destroy(fragment.Texture);
        }

        private void ClearCombatObjects()
        {
            foreach (Fireball fireball in fireballs)
                if (fireball.Object != null)
                    Destroy(fireball.Object);
            fireballs.Clear();

            foreach (BeamVisual beam in beamVisuals)
                if (beam.Object != null)
                    Destroy(beam.Object);
            beamVisuals.Clear();

            for (int i = fragments.Count - 1; i >= 0; i--)
                RemoveFragment(fragments[i]);

            foreach (EffectPixel pixel in effectPixels)
                if (pixel.Object != null)
                    Destroy(pixel.Object);
            effectPixels.Clear();

            foreach (BloodPixel blood in bloodPixels)
                if (blood.Object != null)
                    Destroy(blood.Object);
            bloodPixels.Clear();
            livingWounds.Clear();
        }

        private static float SegmentPointDistanceSquared(Vector2 start, Vector2 end, Vector2 point)
        {
            return (ClosestPointOnSegment(start, end, point) - point).sqrMagnitude;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 start, Vector2 end, Vector2 point)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.0001f)
                return start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return start + segment * t;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(vector.x * cosine - vector.y * sine, vector.x * sine + vector.y * cosine);
        }

        private static Vector2 ClampFragmentToArena(Vector2 position)
        {
            position.x = Mathf.Clamp(position.x, -ArenaHalfWidth + 0.18f, ArenaHalfWidth - 0.18f);
            position.y = Mathf.Clamp(position.y, -ArenaHalfHeight + 0.18f, ArenaHalfHeight - 0.18f);
            return position;
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
                return;

            overlayTexture = MakeGuiTexture(new Color(0.01f, 0.015f, 0.035f, 0.78f));
            panelTexture = MakeGuiTexture(new Color(0.045f, 0.075f, 0.12f, 0.98f));
            generatedArt.Add(overlayTexture);
            generatedArt.Add(panelTexture);
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Gold }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 19,
                normal = { textColor = UiInk }
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UiInk },
                hover = { textColor = Gold },
                active = { textColor = Color.white }
            };
        }

        private void ReplaceMageArt(ref Sprite[] idle, ref Sprite[] walk,
            ref LayeredMageAnimation? idleLayers, ref LayeredMageAnimation? walkLayers,
            string prefix, MageColor color)
        {
            DestroyFrames(idle);
            DestroyFrames(walk);
            idle = WildMagicShowcase.CreateMageFrames($"{prefix} {color.Name} Idle", color.Robe, color.Magic, false);
            walk = WildMagicShowcase.CreateMageFrames($"{prefix} {color.Name} Walk", color.Robe, color.Magic, true);
            idleLayers = CreateLayeredMageAnimation(idle, false);
            walkLayers = CreateLayeredMageAnimation(walk, true);
            TrackFrames(idle);
            TrackFrames(walk);
        }

        private static LayeredMageAnimation CreateLayeredMageAnimation(Sprite[] sprites, bool walking)
        {
            LayeredMageFrame[] frames = new LayeredMageFrame[sprites.Length];
            for (int frameIndex = 0; frameIndex < sprites.Length; frameIndex++)
            {
                Sprite sprite = sprites[frameIndex];
                Color32[] outer = sprite.texture.GetPixels32();
                Color32[] flesh = WildMagicShowcase.CreateMageFleshPixels(frameIndex, walking);
                Color32[] bones = WildMagicShowcase.CreateMageSkeletonPixels(frameIndex, walking);
                byte[] boneDurability = new byte[bones.Length];
                bool[] staffMask = new bool[bones.Length];
                bool[] burningMask = new bool[bones.Length];
                int width = sprite.texture.width;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i].a > 0)
                        boneDurability[i] = 2;
                    int x = i % width;
                    int y = i / width;
                    staffMask[i] = x >= 17 && y >= 5 && outer[i].a > 0 &&
                                   flesh[i].a == 0 && bones[i].a == 0;
                }

                LayeredMageFrame frame = new()
                {
                    Sprite = sprite,
                    Texture = sprite.texture,
                    OuterPixels = outer,
                    FleshPixels = flesh,
                    BonePixels = bones,
                    BoneDurability = boneDurability,
                    StaffMask = staffMask,
                    BurningMask = burningMask,
                    CanonicalCoordinates = BuildMageCanonicalCoordinates(frameIndex, walking, outer,
                        flesh, bones, staffMask, width, sprite.texture.height),
                    Width = width,
                    Height = sprite.texture.height
                };
                RebuildComposite(frame);
                frames[frameIndex] = frame;
            }
            return new LayeredMageAnimation { Frames = frames };
        }

        private static Vector2[] BuildMageCanonicalCoordinates(int frame, bool walking,
            Color32[] outer, Color32[] flesh, Color32[] bones, bool[] staffMask, int width, int height)
        {
            Vector2[] coordinates = new Vector2[width * height];
            int bob = walking ? new[] { 0, 1, 0, 1 }[frame] : new[] { 0, 1, 1, 0 }[frame];
            int hemSwing = walking ? new[] { -1, 0, 1, 0 }[frame] : 0;
            int hoodSway = walking ? new[] { -1, 0, 1, 0 }[frame] : 0;
            int handX = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int handY = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int staffTilt = walking ? new[] { -1, 0, 1, 0 }[frame] : 0;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                float canonicalX = x;
                float canonicalY = y - bob;
                bool occupied = outer[index].a > 0 || flesh[index].a > 0 || bones[index].a > 0;

                if (occupied && staffMask[index])
                {
                    float staffT = Mathf.InverseLerp(5f, 25f, canonicalY - handY);
                    canonicalX -= handX + Mathf.RoundToInt(staffTilt * staffT);
                    canonicalY -= handY;
                }
                else if (occupied && walking)
                {
                    if (canonicalY >= 25f)
                        canonicalX -= hoodSway * (canonicalY >= 28f ? 2f : 1f);
                    else if (canonicalY >= 13f && canonicalY <= 20f && x <= 6 + handX)
                    {
                        canonicalX -= handX;
                        canonicalY += handY;
                    }
                    else if (canonicalY >= 13f && canonicalY <= 20f && x >= 16 + handX)
                    {
                        canonicalX -= handX;
                        canonicalY -= handY;
                    }
                    else if (canonicalY >= 4f && canonicalY <= 8f)
                        canonicalX -= hemSwing;
                }

                coordinates[index] = new Vector2(canonicalX, canonicalY);
            }
            return coordinates;
        }

        private void TrackFrames(IEnumerable<Sprite> frames)
        {
            foreach (Sprite sprite in frames)
            {
                generatedArt.Add(sprite);
                generatedArt.Add(sprite.texture);
            }
        }

        private void DestroyFrames(IEnumerable<Sprite> frames)
        {
            foreach (Sprite sprite in frames)
            {
                if (sprite == null)
                    continue;
                generatedArt.Remove(sprite.texture);
                generatedArt.Remove(sprite);
                Destroy(sprite.texture);
                Destroy(sprite);
            }
        }

        private void CreateWall(string name, Vector2 position, Vector2 size)
        {
            CreatePanel(name, position, size, WallColor, -20);
            Vector2 highlightSize = size.x > size.y
                ? new Vector2(size.x - 0.15f, 0.055f)
                : new Vector2(0.055f, size.y - 0.15f);
            CreatePanel(name + " Highlight", position + new Vector2(-0.035f, 0.035f),
                highlightSize, WallHighlight, -18);
        }

        private void CreateShadow(Transform parent, string name)
        {
            GameObject shadow = new(name);
            shadow.transform.SetParent(parent, false);
            shadow.transform.localPosition = new Vector3(0f, -0.09f, 0f);
            shadow.transform.localScale = new Vector3(0.58f, 0.14f, 1f);
            SpriteRenderer renderer = shadow.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = new Color(0f, 0f, 0f, 0.42f);
            renderer.sortingOrder = 3;
        }

        private SpriteRenderer CreatePanel(string name, Vector2 position, Vector2 size, Color color, int order)
        {
            GameObject panel = new(name);
            panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(position.x, position.y, 0f);
            panel.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = panel.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private TextMesh CreateGlyph(string name, string text, Vector2 position, Color color, float size,
            int order, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            GameObject glyph = new(name);
            glyph.transform.SetParent(transform, false);
            glyph.transform.localPosition = new Vector3(position.x, position.y, 0f);
            TextMesh mesh = glyph.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = size * 0.4f;
            mesh.anchor = anchor;
            mesh.alignment = anchor is TextAnchor.MiddleLeft or TextAnchor.UpperLeft or TextAnchor.LowerLeft
                ? TextAlignment.Left
                : anchor is TextAnchor.MiddleRight or TextAnchor.UpperRight or TextAnchor.LowerRight
                    ? TextAlignment.Right
                    : TextAlignment.Center;
            mesh.text = text;
            mesh.color = color;
            mesh.richText = false;
            MeshRenderer renderer = glyph.GetComponent<MeshRenderer>();
            if (font != null)
                renderer.sharedMaterial = font.material;
            renderer.sortingOrder = order;
            return mesh;
        }

        private static void ApplyName(TextMesh? label, string text, Color color)
        {
            if (label == null)
                return;
            label.text = text;
            label.color = color;
        }

        private static Vector2 ClampToArena(Vector2 position)
        {
            position.x = Mathf.Clamp(position.x, -ArenaHalfWidth + 0.48f, ArenaHalfWidth - 0.48f);
            position.y = Mathf.Clamp(position.y, -ArenaHalfHeight + 0.52f, ArenaHalfHeight - 0.52f);
            return position;
        }

        private static Font CreateFont()
        {
            Font bundled = Resources.Load<Font>("RobotoMono-Regular");
            if (bundled != null)
                return bundled;
            try
            {
                return Font.CreateDynamicFontFromOSFont(new[] { "Cascadia Mono", "Consolas", "Arial" }, 64);
            }
            catch
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        private static Sprite CreateFireballSprite()
        {
            const int size = 9;
            Color32[] pixels = new Color32[size * size];
            Color32 dark = Hex("9E2814");
            Color32 orange = Hex("FF6A1F");
            Color32 gold = Hex("FFC43D");
            Color32 core = Hex("FFF1A6");
            Vector2 center = new(4f, 4f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                if (distance <= 3.7f)
                    pixels[y * size + x] = distance > 2.8f ? dark : distance > 1.75f ? orange :
                        distance > 0.75f ? gold : core;
            }
            pixels[4 * size] = orange;
            pixels[3 * size + 1] = gold;
            pixels[5 * size + 1] = orange;

            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "WildMagic Fireball",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprite.name = "WildMagic Fireball Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateWhiteSprite()
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
            {
                name = "WildMagic Arena White Pixel",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = "WildMagic Arena White Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Texture2D MakeGuiTexture(Color color)
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static Color Hex(string value)
        {
            if (!ColorUtility.TryParseHtmlString("#" + value, out Color color))
                throw new ArgumentException($"Invalid HTML color: {value}", nameof(value));
            return color;
        }

        private readonly struct MageColor
        {
            public MageColor(string name, Color robe, Color magic)
            {
                Name = name;
                Robe = robe;
                Magic = magic;
            }

            public string Name { get; }
            public Color Robe { get; }
            public Color Magic { get; }
        }

        private enum SpellKind
        {
            Fireball,
            WideBeam
        }

        private sealed class Fireball
        {
            public GameObject Object = null!;
            public SpriteRenderer Renderer = null!;
            public Vector2 Position;
            public Vector2 Velocity;
            public byte Owner;
            public float Life;
            public float TrailClock;
        }

        private sealed class BeamVisual
        {
            public GameObject Object = null!;
            public SpriteRenderer Renderer = null!;
            public Color Color;
            public float Life;
            public float MaxLife;
        }

        private sealed class PixelFragment
        {
            public GameObject Object = null!;
            public SpriteRenderer Renderer = null!;
            public GameObject Shadow = null!;
            public SpriteRenderer ShadowRenderer = null!;
            public Sprite Sprite = null!;
            public Texture2D Texture = null!;
            public Color32[] Pixels = Array.Empty<Color32>();
            public Color32[] OuterPixels = Array.Empty<Color32>();
            public Color32[] FleshPixels = Array.Empty<Color32>();
            public Color32[] BonePixels = Array.Empty<Color32>();
            public byte[] BoneDurability = Array.Empty<byte>();
            public bool[] StaffMask = Array.Empty<bool>();
            public bool[] BurningMask = Array.Empty<bool>();
            public int Width;
            public int PixelHeight;
            public Vector2 GroundPosition;
            public Vector2 PlanarVelocity;
            public float Height;
            public float VerticalVelocity;
            public float AngularVelocity;
            public bool Landed;
            public readonly List<FragmentWound> Wounds = new();
            public float BurnUntil;
            public float NextFireTime;
        }

        private sealed class LivingWound
        {
            public bool TargetPlayer;
            public List<Vector2> CanonicalPixels = new();
            public Vector2 Direction;
            public int FleshRemoved;
            public float BleedUntil;
            public float NextBloodTime;
        }

        private sealed class FragmentWound
        {
            public List<Vector2> Pixels = new();
            public int FleshRemoved;
            public float BleedUntil;
            public float NextBloodTime;
        }

        private sealed class LayeredMageAnimation
        {
            public LayeredMageFrame[] Frames = Array.Empty<LayeredMageFrame>();
        }

        private sealed class LayeredMageFrame
        {
            public Sprite Sprite = null!;
            public Texture2D Texture = null!;
            public Color32[] OuterPixels = Array.Empty<Color32>();
            public Color32[] FleshPixels = Array.Empty<Color32>();
            public Color32[] BonePixels = Array.Empty<Color32>();
            public byte[] BoneDurability = Array.Empty<byte>();
            public bool[] StaffMask = Array.Empty<bool>();
            public bool[] BurningMask = Array.Empty<bool>();
            public Vector2[] CanonicalCoordinates = Array.Empty<Vector2>();
            public int Width;
            public int Height;
        }

        private struct LayerDamageResult
        {
            public int OuterRemoved;
            public int FleshRemoved;
            public List<Vector2>? FleshWoundCoordinates;
            public int BoneDamaged;
            public int BoneRemoved;
            public int StaffScorched;
            public int ScorchedPixels;

            public void RecordFleshRemoval(Vector2 coordinate)
            {
                FleshRemoved++;
                FleshWoundCoordinates ??= new List<Vector2>();
                FleshWoundCoordinates.Add(coordinate);
            }

            public void Add(LayerDamageResult other)
            {
                OuterRemoved += other.OuterRemoved;
                FleshRemoved += other.FleshRemoved;
                if (other.FleshWoundCoordinates != null)
                {
                    FleshWoundCoordinates ??= new List<Vector2>();
                    FleshWoundCoordinates.AddRange(other.FleshWoundCoordinates);
                }
                BoneDamaged += other.BoneDamaged;
                BoneRemoved += other.BoneRemoved;
                StaffScorched += other.StaffScorched;
                ScorchedPixels += other.ScorchedPixels;
            }
        }

        private sealed class EffectPixel
        {
            public GameObject Object = null!;
            public SpriteRenderer Renderer = null!;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public float StartScale;
            public float Gravity;
        }

        private sealed class BloodPixel
        {
            public GameObject Object = null!;
            public SpriteRenderer Renderer = null!;
            public Vector2 GroundPosition;
            public Vector2 PlanarVelocity;
            public float Height;
            public float VerticalVelocity;
            public float Life;
            public float MaxLife;
            public float StartScale;
            public bool Landed;
        }
    }
}
