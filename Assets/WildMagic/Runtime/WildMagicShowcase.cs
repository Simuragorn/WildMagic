using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildMagic.Prototype
{
    /// <summary>
    /// A self-contained visual experiment for Wild Magic. It deliberately uses no imported art:
    /// every mage, rune and spell is assembled from procedural pixel art, text glyphs and flat shapes.
    /// </summary>
    public sealed class WildMagicShowcase : MonoBehaviour
    {
        private const float LoopDuration = 13f;

        private static readonly Color Background = Hex("070A12");
        private static readonly Color Arena = Hex("0D1422");
        private static readonly Color Grid = Hex("1A3040");
        private static readonly Color Ink = Hex("B8C7D9");
        private static readonly Color Cyan = Hex("55E6FF");
        private static readonly Color Magenta = Hex("FF5CCF");
        private static readonly Color Fire = Hex("FF7A38");
        private static readonly Color FireHot = Hex("FFE28A");
        private static readonly Color Water = Hex("4AC7FF");
        private static readonly Color Lightning = Hex("ECFF70");

        private readonly List<TextMesh> leftRunes = new();
        private readonly List<TextMesh> rightRunes = new();
        private readonly List<TextMesh> fireTrail = new();
        private readonly List<TextMesh> waterWall = new();
        private readonly List<TextMesh> steamCloud = new();
        private readonly List<TextMesh> lightningArc = new();
        private readonly List<TextMesh> motes = new();
        private readonly List<Vector3> moteOrigins = new();

        private SpriteRenderer? leftMage;
        private SpriteRenderer? rightMage;
        private Sprite[] leftIdleFrames = Array.Empty<Sprite>();
        private Sprite[] leftWalkFrames = Array.Empty<Sprite>();
        private Sprite[] rightIdleFrames = Array.Empty<Sprite>();
        private Sprite[] rightWalkFrames = Array.Empty<Sprite>();
        private SpriteRenderer? ogre;
        private Sprite[] ogreIdleFrames = Array.Empty<Sprite>();
        private Sprite[] ogreWalkFrames = Array.Empty<Sprite>();
        private TextMesh? leftWard;
        private TextMesh? rightWard;
        private TextMesh? clashCore;
        private TextMesh? status;
        private SpriteRenderer? arenaPulse;
        private Font? glyphFont;
        private Sprite? whiteSprite;
        private bool built;
        private bool manualTime;
        private float previewTime;

        public Camera SceneCamera { get; private set; } = null!;

        public static WildMagicShowcase CreateScene()
        {
            GameObject root = new("[WildMagic Visual Prototype]");
            WildMagicShowcase showcase = root.AddComponent<WildMagicShowcase>();
            showcase.Build();
            return showcase;
        }

        private void Awake()
        {
            Build();
        }

        private void Update()
        {
            float time = manualTime ? previewTime : Time.unscaledTime;
            RenderAt(time);
        }

        public void SetPreviewTime(float time)
        {
            manualTime = true;
            previewTime = time;
            RenderAt(time);
        }

        private void Build()
        {
            if (built)
                return;

            built = true;
            glyphFont = CreateFont();
            whiteSprite = CreateWhiteSprite();

            CreateCamera();
            CreateArena();
            CreateInterface();
            CreateMages();
            CreateOgre();
            CreateSpellActors();
            RenderAt(7.35f);
        }

        private void CreateCamera()
        {
            GameObject cameraObject = new("Prototype Camera");
            cameraObject.transform.SetParent(transform, false);
            SceneCamera = cameraObject.AddComponent<Camera>();
            SceneCamera.orthographic = true;
            SceneCamera.orthographicSize = 6.15f;
            SceneCamera.backgroundColor = Background;
            SceneCamera.clearFlags = CameraClearFlags.SolidColor;
            SceneCamera.transform.position = new Vector3(0f, 0f, -10f);
            SceneCamera.nearClipPlane = 0.1f;
            SceneCamera.farClipPlane = 30f;
        }

        private void CreateArena()
        {
            CreatePanel("Arena", Vector2.zero, new Vector2(19.2f, 10.35f), Arena, -20);
            arenaPulse = CreatePanel("Pulse", Vector2.zero, new Vector2(18.75f, 9.9f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.025f), -19);

            for (int x = -9; x <= 9; x++)
                CreatePanel($"Grid V {x}", new Vector2(x, -0.2f), new Vector2(0.012f, 9.35f),
                    new Color(Grid.r, Grid.g, Grid.b, x == 0 ? 0.48f : 0.19f), -17);

            for (int y = -4; y <= 4; y++)
                CreatePanel($"Grid H {y}", new Vector2(0f, y - 0.2f), new Vector2(18.4f, 0.012f),
                    new Color(Grid.r, Grid.g, Grid.b, y == 0 ? 0.48f : 0.19f), -17);

            CreatePanel("Border Top", new Vector2(0f, 4.68f), new Vector2(18.6f, 0.045f), Cyan * 0.65f, -15);
            CreatePanel("Border Bottom", new Vector2(0f, -4.98f), new Vector2(18.6f, 0.045f), Magenta * 0.55f, -15);
            CreatePanel("Border Left", new Vector2(-9.3f, -0.15f), new Vector2(0.045f, 9.7f), Cyan * 0.65f, -15);
            CreatePanel("Border Right", new Vector2(9.3f, -0.15f), new Vector2(0.045f, 9.7f), Magenta * 0.55f, -15);

            System.Random random = new(27);
            string[] dust = { ".", "·", "+", "'" };
            for (int i = 0; i < 26; i++)
            {
                float x = Mathf.Lerp(-8.9f, 8.9f, (float)random.NextDouble());
                float y = Mathf.Lerp(-4.5f, 4.2f, (float)random.NextDouble());
                Color color = i % 3 == 0 ? Cyan : Ink;
                TextMesh mote = CreateGlyph($"Mote {i}", dust[random.Next(dust.Length)],
                    new Vector2(x, y), new Color(color.r, color.g, color.b, 0.16f), 0.11f, -10);
                mote.transform.localScale = Vector3.one * Mathf.Lerp(0.65f, 1.15f, (float)random.NextDouble());
                motes.Add(mote);
                moteOrigins.Add(mote.transform.localPosition);
            }
        }

        private void CreateInterface()
        {
            CreatePanel("Header", new Vector2(0f, 5.18f), new Vector2(19.2f, 0.72f),
                new Color(0.025f, 0.04f, 0.075f, 0.98f), -5);
            CreatePanel("Footer", new Vector2(0f, -5.48f), new Vector2(19.2f, 0.62f),
                new Color(0.025f, 0.04f, 0.075f, 0.98f), -5);

            CreateGlyph("Title", "ДИКАЯ МАГИЯ  //  НЕИЗВЕСТНЫЕ ЗАКОНЫ", new Vector2(-9.05f, 5.2f),
                Ink, 0.17f, 20, TextAnchor.MiddleLeft);
            CreateGlyph("Law", "ЗАКОН 27-Б   [ ЖАР:?  ПРОВОДИМОСТЬ:?  СВЯЗЬ:? ]", new Vector2(9.05f, 5.2f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.82f), 0.12f, 20, TextAnchor.MiddleRight);

            CreateGlyph("Left Name", "ФИОЛЕТОВЫЙ МАГ", new Vector2(-8.75f, 4.48f), Magenta, 0.15f, 20,
                TextAnchor.MiddleLeft);
            CreateGlyph("Left Circles", "ЗАЩИТА  (O) (O) (O)", new Vector2(-8.75f, 4.13f), Ink, 0.12f, 20,
                TextAnchor.MiddleLeft);
            CreateGlyph("Right Name", "ЛАЗУРНЫЙ МАГ", new Vector2(8.75f, 4.48f), Cyan, 0.15f, 20,
                TextAnchor.MiddleRight);
            CreateGlyph("Right Circles", "ЗАЩИТА  (O) (O) (O)", new Vector2(8.75f, 4.13f), Ink, 0.12f, 20,
                TextAnchor.MiddleRight);

            status = CreateGlyph("Status", "ОТКРЫТИЕ:  БАРЬЕР ПРОВОДИТ ЗАРЯД", new Vector2(0f, -5.47f),
                Lightning, 0.13f, 20, TextAnchor.MiddleCenter);
        }

        private void CreateMages()
        {
            leftIdleFrames = CreateMageFrames("Violet Idle", Magenta, FireHot, false);
            leftWalkFrames = CreateMageFrames("Violet Walk", Magenta, FireHot, true);
            rightIdleFrames = CreateMageFrames("Cyan Idle", Cyan, Lightning, false);
            rightWalkFrames = CreateMageFrames("Cyan Walk", Cyan, Lightning, true);

            leftMage = CreatePixelMage("Violet Mage", new Vector2(-7.15f, -0.6f), leftIdleFrames[0], false);
            rightMage = CreatePixelMage("Cyan Mage", new Vector2(7.15f, 0.55f), rightIdleFrames[0], true);

            CreateGlyph("Left Focus", "*", new Vector2(-6.72f, -0.1f), FireHot, 0.15f, 9);
            CreateGlyph("Right Focus", "*", new Vector2(6.72f, 1.05f), Water, 0.15f, 9);

            leftWard = CreateGlyph("Left Ward", "(       )", new Vector2(-7.15f, -0.35f),
                new Color(Magenta.r, Magenta.g, Magenta.b, 0.45f), 0.13f, 2);
            rightWard = CreateGlyph("Right Ward", "[       ]", new Vector2(7.15f, 0.8f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.56f), 0.13f, 2);

            string[] leftSymbols = { "∆", "○", "ϟ" };
            string[] rightSymbols = { "~", "□", "?" };
            for (int i = 0; i < 3; i++)
            {
                leftRunes.Add(CreateGlyph($"Left Rune {i}", leftSymbols[i], Vector2.zero,
                    i == 2 ? Lightning : FireHot, 0.14f, 12));
                rightRunes.Add(CreateGlyph($"Right Rune {i}", rightSymbols[i], Vector2.zero,
                    i == 2 ? Ink : Water, 0.14f, 12));
            }

            CreateRuneCircle(new Vector2(-7.15f, -1.18f), Magenta, "∆·○·ϟ·");
            CreateRuneCircle(new Vector2(7.15f, -0.03f), Cyan, "~·□·?·");
        }

        private void CreateOgre()
        {
            Color skin = Hex("78964B");
            Color accent = Hex("E7D39A");
            ogreIdleFrames = CreateOgreFrames("Ogre Idle", skin, accent, false);
            ogreWalkFrames = CreateOgreFrames("Ogre Walk", skin, accent, true);
            ogre = CreatePixelCreature("Ogre", new Vector2(0f, -3.05f), ogreIdleFrames[0], 1.42f, 6);
        }

        private void CreateSpellActors()
        {
            string[] fireCharacters = { ">", ">", "*", "^", "+", ">" };
            for (int i = 0; i < 12; i++)
            {
                Color color = i % 4 == 0 ? FireHot : Fire;
                fireTrail.Add(CreateGlyph($"Fire {i}", fireCharacters[i % fireCharacters.Length],
                    Vector2.zero, color, i % 3 == 0 ? 0.22f : 0.15f, 8));
            }

            string[] waterCharacters = { "~", "[", "]", "≈", "|" };
            for (int i = 0; i < 12; i++)
            {
                waterWall.Add(CreateGlyph($"Water Wall {i}", waterCharacters[i % waterCharacters.Length],
                    Vector2.zero, Water, i % 3 == 0 ? 0.25f : 0.18f, 9));
            }

            string[] steamCharacters = { "~", ".", "(", ")", "°" };
            for (int i = 0; i < 12; i++)
            {
                steamCloud.Add(CreateGlyph($"Steam {i}", steamCharacters[i % steamCharacters.Length],
                    Vector2.zero, Ink, 0.16f, 10));
            }

            string[] lightningCharacters = { "-", "=", "z", "+", "ϟ" };
            for (int i = 0; i < 11; i++)
            {
                lightningArc.Add(CreateGlyph($"Lightning {i}", lightningCharacters[i % lightningCharacters.Length],
                    Vector2.zero, Lightning, i % 4 == 0 ? 0.23f : 0.14f, 14));
            }

            clashCore = CreateGlyph("Clash Core", "{*}", new Vector2(0.45f, 0.1f), FireHot, 0.30f, 18);
        }

        private void CreateRuneCircle(Vector2 center, Color color, string symbols)
        {
            const int runeCount = 10;
            for (int i = 0; i < runeCount; i++)
            {
                float angle = i / (float)runeCount * Mathf.PI * 2f;
                char symbol = symbols[i % symbols.Length];
                Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.42f) * 0.78f;
                TextMesh rune = CreateGlyph($"Ground Rune {center.x} {i}", symbol.ToString(), position,
                    new Color(color.r, color.g, color.b, i % 2 == 0 ? 0.38f : 0.14f), 0.065f, 1);
                rune.transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            }
        }

        private void RenderAt(float absoluteTime)
        {
            if (!built)
                return;

            float t = Mathf.Repeat(absoluteTime, LoopDuration);
            float pulse = 0.5f + 0.5f * Mathf.Sin(absoluteTime * 2.4f);

            if (arenaPulse != null)
                arenaPulse.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.018f + pulse * 0.018f);

            AnimateMotes(absoluteTime);
            AnimateRunes(leftRunes, new Vector2(-7.15f, -0.02f), absoluteTime, 1f, 0.82f);
            AnimateRunes(rightRunes, new Vector2(7.15f, 1.13f), absoluteTime, -0.75f, 0.78f);

            AnimateMage(leftMage, leftIdleFrames, leftWalkFrames, new Vector2(-7.15f, -0.6f),
                t, absoluteTime, 1f);
            AnimateMage(rightMage, rightIdleFrames, rightWalkFrames, new Vector2(7.15f, 0.55f),
                t, absoluteTime, -1f);
            AnimateOgre(ogre, ogreIdleFrames, ogreWalkFrames, new Vector2(0f, -3.05f), t, absoluteTime);

            if (leftWard != null)
                SetAlpha(leftWard, 0.25f + pulse * 0.25f);
            if (rightWard != null)
                SetAlpha(rightWard, 0.34f + (1f - pulse) * 0.26f);

            AnimateFire(t, absoluteTime);
            AnimateWaterWall(t, absoluteTime);
            AnimateSteam(t, absoluteTime);
            AnimateLightning(t, absoluteTime);

            if (status != null)
            {
                status.text = t < 3.4f
                    ? "СПЛЕТЕНИЕ:  ∆  +  ○  +  НЕИЗВЕСТНОЕ"
                    : t < 6.2f
                        ? "НАБЛЮДЕНИЕ:  ВОДА ГАСИТ ЖАР"
                        : t < 9.8f
                            ? "ОТКРЫТИЕ:  БАРЬЕР ПРОВОДИТ ЗАРЯД"
                            : "ОПАСНОСТЬ:  ЗАРЯД ВОЗВРАЩАЕТСЯ К ЗАКЛИНАТЕЛЮ";
            }
        }

        private void AnimateMotes(float time)
        {
            for (int i = 0; i < motes.Count; i++)
            {
                TextMesh mote = motes[i];
                Vector3 position = moteOrigins[i];
                position.y += Mathf.Sin(time * (0.22f + i % 5 * 0.035f) + i * 1.73f) * 0.06f;
                mote.transform.localPosition = position;
                SetAlpha(mote, 0.08f + 0.12f * (0.5f + 0.5f * Mathf.Sin(time * 0.8f + i)));
            }
        }

        private static void AnimateRunes(IReadOnlyList<TextMesh> runes, Vector2 center, float time,
            float direction, float radius)
        {
            for (int i = 0; i < runes.Count; i++)
            {
                float angle = time * direction + i / (float)runes.Count * Mathf.PI * 2f;
                Vector2 position = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.58f);
                runes[i].transform.localPosition = new Vector3(position.x, position.y, 0f);
                runes[i].transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(angle * 1.7f) * 10f);
            }
        }

        private static void AnimateMage(SpriteRenderer? mage, IReadOnlyList<Sprite> idleFrames,
            IReadOnlyList<Sprite> walkFrames, Vector2 origin, float loopTime, float absoluteTime, float direction)
        {
            if (mage == null || idleFrames.Count == 0 || walkFrames.Count == 0)
                return;

            const float moveStart = 9.25f;
            const float moveEnd = 12.55f;
            bool moving = loopTime >= moveStart && loopTime <= moveEnd;
            float frameRate = moving ? 8f : 2.8f;
            int frame = Mathf.FloorToInt(absoluteTime * frameRate) % idleFrames.Count;
            mage.sprite = moving ? walkFrames[frame] : idleFrames[frame];

            float excursion = 0f;
            float lift = Mathf.Sin(absoluteTime * 2.2f) * 0.018f;
            if (moving)
            {
                float progress = Mathf.InverseLerp(moveStart, moveEnd, loopTime);
                excursion = Mathf.Sin(progress * Mathf.PI) * 0.72f;
                lift += Mathf.Abs(Mathf.Sin(absoluteTime * 8f)) * 0.035f;
            }

            mage.transform.localPosition = new Vector3(origin.x + excursion * direction, origin.y + lift, 0f);
        }

        private static void AnimateOgre(SpriteRenderer? renderer, IReadOnlyList<Sprite> idleFrames,
            IReadOnlyList<Sprite> walkFrames, Vector2 origin, float loopTime, float absoluteTime)
        {
            if (renderer == null || idleFrames.Count == 0 || walkFrames.Count == 0)
                return;

            const float moveStart = 8.1f;
            const float moveEnd = 12.35f;
            bool moving = loopTime >= moveStart && loopTime <= moveEnd;
            float frameRate = moving ? 6f : 2.2f;
            int frame = Mathf.FloorToInt(absoluteTime * frameRate) % idleFrames.Count;
            renderer.sprite = moving ? walkFrames[frame] : idleFrames[frame];

            float x = origin.x;
            float y = origin.y + Mathf.Sin(absoluteTime * 1.55f) * 0.018f;
            if (moving)
            {
                float progress = Mathf.InverseLerp(moveStart, moveEnd, loopTime);
                x += Mathf.Sin(progress * Mathf.PI) * 1.25f;
                y += Mathf.Abs(Mathf.Sin(absoluteTime * 6f)) * 0.045f;
                renderer.flipX = Mathf.Cos(progress * Mathf.PI) < 0f;
            }
            else
            {
                renderer.flipX = false;
            }

            renderer.transform.localPosition = new Vector3(x, y, 0f);
        }

        private SpriteRenderer CreatePixelCreature(string name, Vector2 position, Sprite firstFrame,
            float scale, int sortingOrder)
        {
            GameObject creature = new(name);
            creature.transform.SetParent(transform, false);
            creature.transform.localPosition = new Vector3(position.x, position.y, 0f);
            creature.transform.localScale = Vector3.one * scale;
            SpriteRenderer renderer = creature.AddComponent<SpriteRenderer>();
            renderer.sprite = firstFrame;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private SpriteRenderer CreatePixelMage(string name, Vector2 position, Sprite firstFrame, bool flipX)
        {
            GameObject mage = new(name);
            mage.transform.SetParent(transform, false);
            mage.transform.localPosition = new Vector3(position.x, position.y, 0f);
            mage.transform.localScale = Vector3.one * 1.3f;
            SpriteRenderer renderer = mage.AddComponent<SpriteRenderer>();
            renderer.sprite = firstFrame;
            renderer.flipX = flipX;
            renderer.sortingOrder = 7;
            return renderer;
        }

        public static Sprite[] CreateMageFrames(string name, Color robe, Color magic, bool walking)
        {
            const int width = 24;
            const int height = 32;
            const float pixelsPerUnit = 32f;
            Sprite[] frames = new Sprite[4];

            for (int frame = 0; frame < frames.Length; frame++)
            {
                Color32[] pixels = new Color32[width * height];
                DrawMageFrame(pixels, width, height, robe, magic, frame, walking);

                Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
                {
                    name = $"{name} {frame}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixels32(pixels);
                // Arena combat removes and splits real sprite pixels at runtime, so mage
                // textures must remain CPU-readable after they are uploaded to the GPU.
                texture.Apply(false, false);

                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height),
                    new Vector2(0.5f, 0.08f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = $"{name} {frame}";
                sprite.hideFlags = HideFlags.HideAndDontSave;
                frames[frame] = sprite;
            }

            return frames;
        }

        public static Color32[] CreateMageFleshPixels(int frame, bool walking)
        {
            const int width = 24;
            const int height = 32;
            Color32[] pixels = new Color32[width * height];
            Color32 deep = Hex("3A111A");
            Color32 dark = Hex("6E1E2B");
            Color32 flesh = Hex("B23A46");
            Color32 light = Hex("D46464");
            Color32 blood = Hex("8D101D");
            int bob = walking ? new[] { 0, 1, 0, 1 }[frame] : new[] { 0, 1, 1, 0 }[frame];
            int handX = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int handY = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int leftLift = walking ? new[] { 0, 1, 0, 0 }[frame] : 0;
            int rightLift = walking ? new[] { 0, 0, 0, 1 }[frame] : 0;
            int leftBoot = walking ? new[] { 7, 8, 10, 8 }[frame] : 8;
            int rightBoot = walking ? new[] { 14, 13, 12, 14 }[frame] : 14;
            const int center = 11;

            PaintLine(pixels, width, height, leftBoot + 1, 2 + leftLift,
                center - 2, 11 + bob, 1, deep);
            PaintLine(pixels, width, height, leftBoot + 1, 2 + leftLift,
                center - 2, 11 + bob, 0, flesh);
            PaintLine(pixels, width, height, rightBoot + 1, 2 + rightLift,
                center + 2, 11 + bob, 1, deep);
            PaintLine(pixels, width, height, rightBoot + 1, 2 + rightLift,
                center + 2, 11 + bob, 0, flesh);

            PaintRect(pixels, width, height, center - 4, 9 + bob, center + 4, 13 + bob, deep);
            PaintRect(pixels, width, height, center - 3, 10 + bob, center + 3, 14 + bob, dark);
            PaintRect(pixels, width, height, center - 4, 14 + bob, center + 4, 19 + bob, deep);
            PaintRect(pixels, width, height, center - 3, 14 + bob, center + 3, 19 + bob, flesh);
            PaintRect(pixels, width, height, center - 2, 15 + bob, center - 1, 18 + bob, light);
            PaintRect(pixels, width, height, center - 1, 19 + bob, center + 1, 21 + bob, dark);

            int freeX = walking ? handX : 0;
            int freeY = walking ? -handY : 0;
            PaintLine(pixels, width, height, center - 3, 18 + bob,
                center - 7 + freeX, 15 + bob + freeY, 1, deep);
            PaintLine(pixels, width, height, center - 3, 18 + bob,
                center - 7 + freeX, 15 + bob + freeY, 0, flesh);
            PaintLine(pixels, width, height, center + 3, 18 + bob,
                center + 7 + handX, 15 + bob + handY, 1, deep);
            PaintLine(pixels, width, height, center + 3, 18 + bob,
                center + 7 + handX, 15 + bob + handY, 0, flesh);
            PaintPixel(pixels, width, height, center - 7 + freeX, 15 + bob + freeY, light);
            PaintPixel(pixels, width, height, center + 7 + handX, 15 + bob + handY, light);

            PaintRect(pixels, width, height, center - 3, 21 + bob, center + 3, 25 + bob, deep);
            PaintRect(pixels, width, height, center - 2, 21 + bob, center + 2, 26 + bob, flesh);
            PaintRect(pixels, width, height, center - 1, 24 + bob, center, 25 + bob, light);
            PaintPixel(pixels, width, height, center - 2, 22 + bob, blood);
            PaintPixel(pixels, width, height, center + 2, 22 + bob, blood);

            Color32[] outerMask = new Color32[width * height];
            DrawMageFrame(outerMask, width, height, Color.white, Color.white, frame, walking);
            ClipLayerToMask(pixels, outerMask);
            return pixels;
        }

        public static Color32[] CreateMageSkeletonPixels(int frame, bool walking)
        {
            const int width = 24;
            const int height = 32;
            Color32[] pixels = new Color32[width * height];
            Color32 shade = Hex("7C705E");
            Color32 bone = Hex("D8C9A6");
            Color32 light = Hex("FFF0C8");
            Color32 socket = Hex("211A20");
            int bob = walking ? new[] { 0, 1, 0, 1 }[frame] : new[] { 0, 1, 1, 0 }[frame];
            int handX = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int handY = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int leftLift = walking ? new[] { 0, 1, 0, 0 }[frame] : 0;
            int rightLift = walking ? new[] { 0, 0, 0, 1 }[frame] : 0;
            int leftBoot = walking ? new[] { 7, 8, 10, 8 }[frame] : 8;
            int rightBoot = walking ? new[] { 14, 13, 12, 14 }[frame] : 14;
            const int center = 11;

            PaintLine(pixels, width, height, leftBoot + 1, 2 + leftLift,
                center - 2, 11 + bob, 0, bone);
            PaintPixel(pixels, width, height, leftBoot, 2 + leftLift, light);
            PaintPixel(pixels, width, height, leftBoot + 2, 2 + leftLift, light);
            PaintLine(pixels, width, height, rightBoot + 1, 2 + rightLift,
                center + 2, 11 + bob, 0, bone);
            PaintPixel(pixels, width, height, rightBoot, 2 + rightLift, light);
            PaintPixel(pixels, width, height, rightBoot + 2, 2 + rightLift, light);

            PaintRect(pixels, width, height, center - 3, 10 + bob, center + 3, 12 + bob, shade);
            PaintRect(pixels, width, height, center - 2, 11 + bob, center + 2, 12 + bob, bone);
            PaintLine(pixels, width, height, center, 12 + bob, center, 21 + bob, 0, light);
            PaintLine(pixels, width, height, center - 1, 13 + bob, center - 1, 20 + bob, 0, shade);
            foreach (int y in new[] { 16, 18, 20 })
            {
                PaintLine(pixels, width, height, center, y + bob, center - 4, y - 1 + bob, 0, bone);
                PaintLine(pixels, width, height, center, y + bob, center + 4, y - 1 + bob, 0, bone);
            }

            int freeX = walking ? handX : 0;
            int freeY = walking ? -handY : 0;
            PaintLine(pixels, width, height, center - 3, 19 + bob,
                center - 7 + freeX, 15 + bob + freeY, 0, bone);
            PaintLine(pixels, width, height, center + 3, 19 + bob,
                center + 7 + handX, 15 + bob + handY, 0, bone);
            PaintPixel(pixels, width, height, center - 7 + freeX, 15 + bob + freeY, light);
            PaintPixel(pixels, width, height, center + 7 + handX, 15 + bob + handY, light);

            PaintRect(pixels, width, height, center - 2, 21 + bob, center + 2, 25 + bob, shade);
            PaintRect(pixels, width, height, center - 2, 22 + bob, center + 2, 25 + bob, bone);
            PaintPixel(pixels, width, height, center - 2, 25 + bob, light);
            PaintPixel(pixels, width, height, center + 2, 25 + bob, light);
            PaintPixel(pixels, width, height, center - 2, 22 + bob, socket);
            PaintPixel(pixels, width, height, center + 2, 22 + bob, socket);
            PaintPixel(pixels, width, height, center, 21 + bob, socket);

            ClipLayerToMask(pixels, CreateMageFleshPixels(frame, walking));
            return pixels;
        }

        private static void ClipLayerToMask(Color32[] layer, Color32[] mask)
        {
            for (int i = 0; i < layer.Length; i++)
            {
                if (layer[i].a > 0 && mask[i].a == 0)
                    layer[i] = default;
            }
        }

        private static Sprite[] CreateOgreFrames(string name, Color skin, Color tusks, bool walking)
        {
            const int width = 40;
            const int height = 44;
            const float pixelsPerUnit = 32f;
            Sprite[] frames = new Sprite[4];

            for (int frame = 0; frame < frames.Length; frame++)
            {
                Color32[] pixels = new Color32[width * height];
                DrawOgreFrame(pixels, width, height, skin, tusks, frame, walking);

                Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
                {
                    name = $"{name} {frame}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);

                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height),
                    new Vector2(0.5f, 0.07f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = $"{name} {frame}";
                sprite.hideFlags = HideFlags.HideAndDontSave;
                frames[frame] = sprite;
            }

            return frames;
        }

        private static void DrawOgreFrame(Color32[] pixels, int width, int height, Color skin,
            Color tusks, int frame, bool walking)
        {
            Color32 outline = Color.Lerp(Background, skin, 0.16f);
            Color32 skinShadow = Color.Lerp(Background, skin, 0.48f);
            Color32 skinBase = skin;
            Color32 skinLight = Color.Lerp(skin, Color.white, 0.28f);
            Color32 leatherDark = Hex("291B18");
            Color32 leather = Hex("6D4330");
            Color32 leatherLight = Hex("A66A43");
            Color32 clubDark = Hex("241A16");
            Color32 club = Hex("75513A");
            Color32 eye = Hex("FFD85A");
            Color32 tooth = tusks;

            int bob = !walking && frame == 1 ? 1 : 0;
            int leftStep = walking ? new[] { -2, -1, 2, 0 }[frame] : 0;
            int rightStep = walking ? new[] { 2, 0, -2, -1 }[frame] : 0;

            // Knotted tree club sits behind the body and changes angle slightly with each step.
            int weaponSwing = walking ? new[] { -1, 0, 1, 0 }[frame] : 0;
            PaintLine(pixels, width, height, 33 + weaponSwing, 6, 36 + weaponSwing, 36 + bob, 2, clubDark);
            PaintLine(pixels, width, height, 33 + weaponSwing, 7, 35 + weaponSwing, 35 + bob, 1, club);
            PaintRect(pixels, width, height, 33 + weaponSwing, 34 + bob,
                38 + weaponSwing, 40 + bob, clubDark);
            PaintRect(pixels, width, height, 34 + weaponSwing, 35 + bob,
                37 + weaponSwing, 39 + bob, club);
            PaintPixel(pixels, width, height, 34 + weaponSwing, 38 + bob, leatherLight);
            PaintPixel(pixels, width, height, 37 + weaponSwing, 36 + bob, outline);

            // Heavy feet and uneven stride. The torso never shifts sideways relative to the pivot.
            PaintRect(pixels, width, height, 8 + leftStep, 1, 16 + leftStep, 6, outline);
            PaintRect(pixels, width, height, 10 + leftStep, 2, 16 + leftStep, 5, leatherDark);
            PaintRect(pixels, width, height, 23 + rightStep, 1, 31 + rightStep, 6, outline);
            PaintRect(pixels, width, height, 23 + rightStep, 2, 29 + rightStep, 5, leatherDark);

            PaintRect(pixels, width, height, 11 + leftStep / 2, 5, 18 + leftStep / 2, 16 + bob, outline);
            PaintRect(pixels, width, height, 12 + leftStep / 2, 6, 18 + leftStep / 2, 15 + bob, skinShadow);
            PaintRect(pixels, width, height, 21 + rightStep / 2, 5, 28 + rightStep / 2, 16 + bob, outline);
            PaintRect(pixels, width, height, 21 + rightStep / 2, 6, 27 + rightStep / 2, 15 + bob, skinShadow);

            // Long ape-like arms establish the D&D ogre silhouette even when viewed from far away.
            PaintRect(pixels, width, height, 3, 13 + bob, 10, 29 + bob, outline);
            PaintRect(pixels, width, height, 4, 14 + bob, 9, 28 + bob, skinBase);
            PaintRect(pixels, width, height, 5, 14 + bob, 6, 26 + bob, skinLight);
            PaintRect(pixels, width, height, 2, 10 + bob, 9, 16 + bob, outline);
            PaintRect(pixels, width, height, 3, 11 + bob, 8, 15 + bob, skinBase);
            PaintPixel(pixels, width, height, 3, 11 + bob, skinLight);

            PaintRect(pixels, width, height, 29, 19 + bob, 36, 29 + bob, outline);
            PaintRect(pixels, width, height, 30, 20 + bob, 35, 28 + bob, skinShadow);
            PaintRect(pixels, width, height, 30 + weaponSwing, 13 + bob,
                36 + weaponSwing, 22 + bob, outline);
            PaintRect(pixels, width, height, 31 + weaponSwing, 14 + bob,
                35 + weaponSwing, 21 + bob, skinShadow);
            PaintRect(pixels, width, height, 31 + weaponSwing, 11 + bob,
                37 + weaponSwing, 17 + bob, outline);
            PaintRect(pixels, width, height, 31 + weaponSwing, 12 + bob,
                36 + weaponSwing, 16 + bob, skinBase);
            PaintRect(pixels, width, height, 34 + weaponSwing, 13 + bob,
                36 + weaponSwing, 15 + bob, leatherLight);

            // Broad belly and raised shoulders taper into a short neck.
            const int center = 20;
            for (int y = 14; y <= 31; y++)
            {
                int halfWidth;
                if (y < 22)
                    halfWidth = 11;
                else
                    halfWidth = Mathf.RoundToInt(Mathf.Lerp(14f, 8f, (y - 22) / 9f));

                PaintRect(pixels, width, height, center - halfWidth, y + bob,
                    center + halfWidth, y + bob, outline);
                PaintRect(pixels, width, height, center - halfWidth + 1, y + bob,
                    center + halfWidth - 1, y + bob, skinBase);
            }

            PaintRect(pixels, width, height, 11, 20 + bob, 14, 28 + bob, skinLight);
            PaintRect(pixels, width, height, 27, 19 + bob, 31, 27 + bob, skinShadow);
            PaintRect(pixels, width, height, 13, 15 + bob, 27, 18 + bob, leatherDark);
            PaintRect(pixels, width, height, 15, 16 + bob, 25, 17 + bob, leather);
            PaintRect(pixels, width, height, 19, 15 + bob, 21, 18 + bob, leatherLight);

            // Ragged loincloth and patchwork seams.
            PaintRect(pixels, width, height, 14, 8 + bob, 26, 15 + bob, leatherDark);
            PaintRect(pixels, width, height, 16, 9 + bob, 24, 15 + bob, leather);
            PaintRect(pixels, width, height, 16, 8 + bob, 18, 10 + bob, leatherLight);
            PaintRect(pixels, width, height, 22, 8 + bob, 24, 11 + bob, leatherLight);
            PaintPixel(pixels, width, height, 20, 12 + bob, tooth);

            // Low, wide head with protruding ears, heavy brow, flat nose and tusks.
            PaintRect(pixels, width, height, 12, 33 + bob, 15, 37 + bob, outline);
            PaintRect(pixels, width, height, 13, 34 + bob, 15, 36 + bob, skinLight);
            PaintRect(pixels, width, height, 25, 33 + bob, 28, 37 + bob, outline);
            PaintRect(pixels, width, height, 25, 34 + bob, 27, 36 + bob, skinShadow);

            for (int y = 29; y <= 41; y++)
            {
                int halfWidth = y <= 36 ? 7 : Mathf.Max(3, 7 - (y - 36));
                PaintRect(pixels, width, height, center - halfWidth, y + bob,
                    center + halfWidth, y + bob, outline);
                PaintRect(pixels, width, height, center - halfWidth + 1, y + bob,
                    center + halfWidth - 1, y + bob, skinBase);
            }

            PaintRect(pixels, width, height, 14, 37 + bob, 26, 39 + bob, skinShadow);
            PaintRect(pixels, width, height, 15, 38 + bob, 25, 39 + bob, outline);
            bool blinking = !walking && frame == 3;
            PaintRect(pixels, width, height, 16, 36 + bob, 18, 36 + bob, outline);
            PaintRect(pixels, width, height, 22, 36 + bob, 24, 36 + bob, outline);
            if (!blinking)
            {
                PaintPixel(pixels, width, height, 17, 36 + bob, eye);
                PaintPixel(pixels, width, height, 23, 36 + bob, eye);
            }

            PaintRect(pixels, width, height, 18, 32 + bob, 22, 35 + bob, skinShadow);
            PaintRect(pixels, width, height, 19, 33 + bob, 21, 34 + bob, skinLight);
            PaintRect(pixels, width, height, 16, 29 + bob, 24, 31 + bob, outline);
            PaintRect(pixels, width, height, 18, 30 + bob, 22, 30 + bob, leatherDark);
            PaintRect(pixels, width, height, 15, 29 + bob, 17, 32 + bob, tooth);
            PaintRect(pixels, width, height, 23, 29 + bob, 25, 32 + bob, tooth);
            PaintPixel(pixels, width, height, 14, 34 + bob, skinLight);
            PaintPixel(pixels, width, height, 26, 31 + bob, skinShadow);

            // A few scars and stitches add detail without turning the sprite into noise.
            PaintLine(pixels, width, height, 14, 23 + bob, 18, 20 + bob, 0, leatherDark);
            PaintPixel(pixels, width, height, 26, 23 + bob, leatherDark);
            PaintPixel(pixels, width, height, 27, 22 + bob, leatherDark);
            PaintPixel(pixels, width, height, 28, 23 + bob, leatherDark);
        }

        private static void DrawMageFrame(Color32[] pixels, int width, int height, Color robe,
            Color magic, int frame, bool walking)
        {
            Color32 outline = Color.Lerp(Background, robe, 0.18f);
            Color32 shadow = Color.Lerp(Background, robe, 0.42f);
            Color32 baseRobe = Color.Lerp(robe, Color.white, 0.06f);
            Color32 highlight = Color.Lerp(robe, Color.white, 0.38f);
            Color32 face = Hex("130F20");
            Color32 staff = Hex("8895A8");
            Color32 glow = Color.Lerp(magic, Color.white, frame % 2 == 0 ? 0.28f : 0.08f);

            // Four classic phases: contact, passing, opposite contact, passing. The torso rises
            // on passing frames while one supporting foot keeps the ground line stable.
            int bob = walking ? new[] { 0, 1, 0, 1 }[frame] : new[] { 0, 1, 1, 0 }[frame];
            int hemSwing = walking ? new[] { -1, 0, 1, 0 }[frame] : 0;
            int hemSpread = walking ? new[] { 1, 0, 1, 0 }[frame] : 0;
            int hoodSway = walking ? new[] { -1, 0, 1, 0 }[frame] : new[] { 0, 0, 1, 0 }[frame];
            int staffHandX = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int staffHandY = walking ? new[] { 0, 1, 0, -1 }[frame] : 0;
            int staffTilt = walking ? new[] { -1, 0, 1, 0 }[frame] : 0;
            int freeHandX = walking ? staffHandX : 0;
            int freeHandY = walking ? -staffHandY : 0;
            int leftBootLift = walking ? new[] { 0, 1, 0, 0 }[frame] : 0;
            int rightBootLift = walking ? new[] { 0, 0, 0, 1 }[frame] : 0;
            // Keep the torso locked to the sprite pivot. Earlier walk frames shifted the entire
            // silhouette left and right, which looked like lateral sliding on top of world movement.
            int center = 11;

            // Boots remain visible beneath the robe and alternate clearly during a walk cycle.
            int leftBoot = walking ? new[] { 7, 8, 10, 8 }[frame] : 8;
            int rightBoot = walking ? new[] { 14, 13, 12, 14 }[frame] : 14;
            PaintRect(pixels, width, height, leftBoot, 1 + leftBootLift,
                leftBoot + 2, 4 + leftBootLift, outline);
            PaintRect(pixels, width, height, rightBoot, 1 + rightBootLift,
                rightBoot + 2, 4 + rightBootLift, outline);
            PaintPixel(pixels, width, height, leftBoot, 1 + leftBootLift, shadow);
            PaintPixel(pixels, width, height, rightBoot + 2, 1 + rightBootLift, shadow);

            // Staff and its glowing focus give the small silhouette a readable facing direction.
            int staffBottomX = 20 + staffHandX;
            int staffTopX = staffBottomX + staffTilt;
            int staffBottomY = 5 + bob + staffHandY;
            int staffTopY = 25 + bob + staffHandY;
            PaintLine(pixels, width, height, staffBottomX, staffBottomY,
                staffTopX, staffTopY, 1, outline);
            PaintLine(pixels, width, height, staffBottomX, staffBottomY + 1,
                staffTopX, staffTopY - 1, 0, staff);
            PaintRect(pixels, width, height, staffTopX - 2, staffTopY,
                staffTopX + 2, staffTopY + 3, outline);
            PaintRect(pixels, width, height, staffTopX - 1, staffTopY + 1,
                staffTopX + 1, staffTopY + 2, glow);
            PaintPixel(pixels, width, height, staffTopX, staffTopY + 3, glow);

            // Wide triangular robe with a dark outline and a few deliberate folds.
            for (int y = 4; y <= 18; y++)
            {
                float taper = (y - 4) / 14f;
                int halfWidth = Mathf.RoundToInt(Mathf.Lerp(7f, 4f, taper));
                int animatedHalfWidth = halfWidth + (y <= 6 ? hemSpread : 0);
                int rowShift = y <= 8 ? hemSwing : 0;
                PaintRect(pixels, width, height, center - animatedHalfWidth + rowShift, y + bob,
                    center + animatedHalfWidth + rowShift, y + bob, outline);
                PaintRect(pixels, width, height, center - animatedHalfWidth + rowShift + 1, y + bob,
                    center + animatedHalfWidth + rowShift - 1, y + bob, baseRobe);
            }

            PaintRect(pixels, width, height, center - 6, 5 + bob, center - 5, 15 + bob, shadow);
            PaintRect(pixels, width, height, center + 4, 5 + bob, center + 5, 15 + bob, shadow);
            PaintRect(pixels, width, height, center - 3, 5 + bob, center - 2, 16 + bob, highlight);
            PaintRect(pixels, width, height, center, 4 + bob, center, 13 + bob, shadow);
            PaintPixel(pixels, width, height, center - 6, 4 + bob, highlight);
            PaintPixel(pixels, width, height, center + 6, 4 + bob, shadow);

            // Sleeves are chunky enough to read at game scale, but do not widen the whole character.
            PaintRect(pixels, width, height, center - 8 + freeHandX, 14 + bob + freeHandY,
                center - 5, 19 + bob + freeHandY, outline);
            PaintRect(pixels, width, height, center - 7 + freeHandX, 15 + bob + freeHandY,
                center - 5, 18 + bob + freeHandY, baseRobe);
            PaintRect(pixels, width, height, center + 5, 14 + bob + staffHandY,
                center + 8 + staffHandX, 19 + bob + staffHandY, outline);
            PaintRect(pixels, width, height, center + 5, 15 + bob + staffHandY,
                center + 7 + staffHandX, 18 + bob + staffHandY, shadow);
            PaintPixel(pixels, width, height, center - 8 + freeHandX,
                14 + bob + freeHandY, glow);
            PaintPixel(pixels, width, height, center + 8 + staffHandX,
                14 + bob + staffHandY, glow);

            // Belt, buckle and a tiny rune prevent the robe from becoming one flat triangle.
            PaintRect(pixels, width, height, center - 5, 13 + bob, center + 5, 14 + bob, shadow);
            PaintRect(pixels, width, height, center, 13 + bob, center + 1, 14 + bob, glow);
            PaintPixel(pixels, width, height, center, 16 + bob, glow);
            PaintPixel(pixels, width, height, center - 1, 17 + bob, glow);
            PaintPixel(pixels, width, height, center + 1, 17 + bob, glow);

            // Deep hood with a pointed crown and two bright eyes.
            for (int y = 19; y <= 29; y++)
            {
                int halfWidth = y <= 23 ? 6 : Mathf.Max(0, 6 - (y - 23));
                // The base stays locked to the shoulders. Only the loose upper fabric sways,
                // otherwise a one-pixel lift reads as the entire head detaching from the robe.
                int hoodCenter = center;
                if (walking && y >= 25)
                    hoodCenter += hoodSway;
                if (walking && y >= 28)
                    hoodCenter += hoodSway;

                PaintRect(pixels, width, height, hoodCenter - halfWidth, y + bob,
                    hoodCenter + halfWidth, y + bob, outline);
                if (halfWidth > 1)
                    PaintRect(pixels, width, height, hoodCenter - halfWidth + 1, y + bob,
                        hoodCenter + halfWidth - 1, y + bob, baseRobe);
            }

            PaintRect(pixels, width, height, center - 4, 20 + bob,
                center + 4, 24 + bob, face);
            bool blinking = !walking && frame == 3;
            if (!blinking)
            {
                PaintPixel(pixels, width, height, center - 2, 22 + bob, glow);
                PaintPixel(pixels, width, height, center + 2, 22 + bob, glow);
            }
            PaintRect(pixels, width, height, center - 5, 24 + bob,
                center - 4, 25 + bob, highlight);

            if (walking)
            {
                // Alternating loose pixels make the lower edge flap rather than slide as one block.
                PaintPixel(pixels, width, height, center - 7 + hemSwing, 4 + bob, shadow);
                PaintPixel(pixels, width, height, center + 7 + hemSwing, 4 + bob, highlight);
                PaintPixel(pixels, width, height, center - 6 - hemSwing, 6 + bob, highlight);
                PaintPixel(pixels, width, height, center + 6 - hemSwing, 5 + bob, shadow);
            }
        }

        private static void PaintLine(Color32[] pixels, int width, int height, int x0, int y0,
            int x1, int y1, int radius, Color32 color)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                PaintRect(pixels, width, height, x0 - radius, y0 - radius,
                    x0 + radius, y0 + radius, color);
                if (x0 == x1 && y0 == y1)
                    break;

                int twiceError = error * 2;
                if (twiceError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (twiceError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private static void PaintRect(Color32[] pixels, int width, int height, int xMin, int yMin,
            int xMax, int yMax, Color32 color)
        {
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
                PaintPixel(pixels, width, height, x, y, color);
        }

        private static void PaintPixel(Color32[] pixels, int width, int height, int x, int y, Color32 color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;

            pixels[y * width + x] = color;
        }

        private void AnimateFire(float t, float absoluteTime)
        {
            float progress = Mathf.Clamp01((t - 1.4f) / 3.6f);
            float visibility = SmoothWindow(t, 0.9f, 5.9f, 0.45f);
            Vector2 head = Vector2.Lerp(new Vector2(-6.72f, -0.15f), new Vector2(0.15f, 0.15f), progress);

            for (int i = 0; i < fireTrail.Count; i++)
            {
                float distance = i * 0.28f;
                float jitter = Mathf.Sin(absoluteTime * 8f + i * 2.17f) * (0.035f + i * 0.002f);
                Vector2 position = head + new Vector2(-distance, jitter);
                fireTrail[i].transform.localPosition = new Vector3(position.x, position.y, 0f);
                fireTrail[i].transform.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Sin(absoluteTime * 5f + i) * 11f);
                SetAlpha(fireTrail[i], visibility * Mathf.Clamp01(1.2f - i / 12f));
            }
        }

        private void AnimateWaterWall(float t, float absoluteTime)
        {
            float visibility = SmoothWindow(t, 3.0f, 10.4f, 0.55f);
            for (int i = 0; i < waterWall.Count; i++)
            {
                int column = i % 3;
                int row = i / 3;
                float sway = Mathf.Sin(absoluteTime * 3.4f + row * 0.9f + column) * 0.09f;
                Vector2 position = new(0.48f + (column - 1) * 0.24f + sway,
                    -1.05f + row * 0.55f);
                waterWall[i].transform.localPosition = new Vector3(position.x, position.y, 0f);
                waterWall[i].transform.localRotation = Quaternion.Euler(0f, 0f, sway * 35f);
                SetAlpha(waterWall[i], visibility * (0.55f + 0.35f * Mathf.Sin(absoluteTime * 5f + i)));
            }
        }

        private void AnimateSteam(float t, float absoluteTime)
        {
            float age = Mathf.Clamp01((t - 4.5f) / 4.4f);
            float visibility = SmoothWindow(t, 4.35f, 10.1f, 0.75f);
            for (int i = 0; i < steamCloud.Count; i++)
            {
                float angle = i * 2.39996f + Mathf.Sin(i * 1.7f) * 0.2f;
                float radius = (0.25f + (i % 8) / 8f * 2.2f) * (0.35f + age);
                Vector2 position = new Vector2(0.48f, 0.12f) +
                    new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.72f);
                position.y += age * (0.35f + i % 4 * 0.09f);
                position += new Vector2(Mathf.Sin(absoluteTime * 1.4f + i) * 0.08f, 0f);
                steamCloud[i].transform.localPosition = new Vector3(position.x, position.y, 0f);
                SetAlpha(steamCloud[i], visibility * (0.18f + (i % 4) * 0.075f));
            }

            if (clashCore != null)
            {
                clashCore.transform.localScale = Vector3.one * (0.85f + Mathf.Sin(absoluteTime * 9f) * 0.16f);
                SetAlpha(clashCore, SmoothWindow(t, 4.25f, 7.2f, 0.35f));
            }
        }

        private void AnimateLightning(float t, float absoluteTime)
        {
            float visibility = SmoothWindow(t, 6.05f, 10.9f, 0.22f);
            Vector2 start = new(0.75f, 0.45f);
            Vector2 end = new(6.72f, 1.1f);

            for (int i = 0; i < lightningArc.Count; i++)
            {
                float fraction = i / (float)(lightningArc.Count - 1);
                Vector2 position = Vector2.Lerp(start, end, fraction);
                float jag = Mathf.Sin(i * 8.31f + Mathf.Floor(absoluteTime * 12f) * 1.7f) * 0.24f;
                position.y += jag * Mathf.Sin(fraction * Mathf.PI);
                lightningArc[i].transform.localPosition = new Vector3(position.x, position.y, 0f);
                lightningArc[i].transform.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Sin(i * 5.1f + absoluteTime * 9f) * 22f);
                float flicker = Mathf.Repeat(absoluteTime * 17f + i * 0.37f, 1f) > 0.15f ? 1f : 0.15f;
                SetAlpha(lightningArc[i], visibility * flicker);
            }
        }

        private SpriteRenderer CreatePanel(string name, Vector2 position, Vector2 size, Color color, int sortingOrder)
        {
            GameObject panel = new(name);
            panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(position.x, position.y, 0f);
            panel.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = panel.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private TextMesh CreateGlyph(string name, string text, Vector2 position, Color color, float size,
            int sortingOrder, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            GameObject glyph = new(name);
            glyph.transform.SetParent(transform, false);
            glyph.transform.localPosition = new Vector3(position.x, position.y, 0f);
            TextMesh mesh = glyph.AddComponent<TextMesh>();
            mesh.font = glyphFont;
            mesh.fontSize = 64;
            // TextMesh's dynamic-font metrics are much larger than one world unit at fontSize 64.
            // Keep the public sizes intuitive and apply one visual scale in a single place.
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
            if (glyphFont != null)
                renderer.sharedMaterial = glyphFont.material;
            renderer.sortingOrder = sortingOrder;
            return mesh;
        }

        private static Font CreateFont()
        {
            string[] preferred = { "Cascadia Mono", "Consolas", "Courier New" };
            try
            {
                return Font.CreateDynamicFontFromOSFont(preferred, 64);
            }
            catch
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        private static Sprite CreateWhiteSprite()
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
            {
                name = "WildMagic White Pixel",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = "WildMagic White Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static float SmoothWindow(float value, float start, float end, float edge)
        {
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, start + edge, value));
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(end - edge, end, value));
            return fadeIn * fadeOut;
        }

        private static void SetAlpha(TextMesh text, float alpha)
        {
            Color color = text.color;
            color.a = Mathf.Clamp01(alpha);
            text.color = color;
        }

        private static Color Hex(string value)
        {
            if (!ColorUtility.TryParseHtmlString("#" + value, out Color color))
                throw new ArgumentException($"Invalid HTML color: {value}", nameof(value));
            return color;
        }
    }
}
