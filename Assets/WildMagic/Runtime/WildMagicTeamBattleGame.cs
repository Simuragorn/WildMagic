using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype
{
    public sealed class WildMagicTeamBattleGame : MonoBehaviour
    {
        private const float Ppu = 32f;
        private const float Scale = 1.28f;
        private const float HalfW = 8.7f;
        private const float HalfH = 4.25f;
        private readonly List<Mage> mages = new();
        private readonly List<Shot> shots = new();
        private readonly List<Particle> particles = new();
        private readonly List<Wound> wounds = new();
        private readonly List<Corpse> corpses = new();
        private readonly List<UnityEngine.Object> art = new();
        private Camera cameraRef = null!;
        private Sprite white = null!;
        private Font font = null!;
        private TextMesh score = null!;
        private bool finished;
        private bool paused;
        private bool escapeHeld;
        private bool restartHeld;
        private string result = string.Empty;

        private static readonly (string name, Color robe, Color magic)[] Palette =
        {
            ("АЛЫЙ", Hex("EF5350"), Hex("FFD0A1")), ("ЛАЗУРНЫЙ", Hex("45C8FF"), Hex("D5FAFF")),
            ("ФИОЛЕТОВЫЙ", Hex("B46CFF"), Hex("F5D7FF")), ("ИЗУМРУДНЫЙ", Hex("49D98A"), Hex("CFFFF0")),
            ("ЗОЛОТОЙ", Hex("F5BE47"), Hex("FFF2B5")), ("РОЗОВЫЙ", Hex("FF6FAE"), Hex("FFD7E9"))
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "TeamBattle3v3" || FindFirstObjectByType<WildMagicTeamBattleGame>() != null)
                return;
            new GameObject("[WildMagic Team Battle]").AddComponent<WildMagicTeamBattleGame>();
        }

        private void Awake()
        {
            BuildWorld();
            StartBattle();
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            bool r = keyboard != null && keyboard.rKey.isPressed;
            if (r && !restartHeld) StartBattle();
            restartHeld = r;
            bool esc = keyboard != null && keyboard.escapeKey.isPressed;
            if (esc && !escapeHeld && !finished) paused = !paused;
            escapeHeld = esc;
            if (paused) return;

            if (!finished)
            {
                UpdatePlayer(keyboard);
                for (int i = 0; i < mages.Count; i++) if (!mages[i].Player) UpdateAi(mages[i], i);
                UpdateShots();
                CheckVictory();
            }
            UpdateMages();
            UpdateWounds();
            UpdateCorpses();
            UpdateParticles();
        }

        private void BuildWorld()
        {
            foreach (Camera camera in FindObjectsByType<Camera>()) camera.gameObject.SetActive(false);
            font = Resources.Load<Font>("RobotoMono-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            white = MakeWhite();
            GameObject cam = new("Team Arena Camera"); cam.transform.SetParent(transform);
            cameraRef = cam.AddComponent<Camera>(); cameraRef.orthographic = true; cameraRef.orthographicSize = 5.6f;
            cameraRef.backgroundColor = Hex("070A12"); cameraRef.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(0, .35f, -10);
            Panel("Floor", Vector2.zero, new Vector2(18.2f, 9.2f), Hex("101827"), -30);
            for (int x = -8; x <= 8; x++) Panel("Grid", new Vector2(x, 0), new Vector2(.018f, 8.4f), new Color(.12f,.21f,.29f,.22f), -28);
            for (int y = -4; y <= 4; y++) Panel("Grid", new Vector2(0, y), new Vector2(17.4f,.018f), new Color(.12f,.21f,.29f,.22f), -28);
            Panel("North", new Vector2(0, HalfH+.22f), new Vector2(18.2f,.52f), Hex("43546B"), -20);
            Panel("South", new Vector2(0,-HalfH-.22f), new Vector2(18.2f,.52f), Hex("43546B"), -20);
            Panel("West", new Vector2(-HalfW-.22f,0), new Vector2(.52f,9), Hex("43546B"), -20);
            Panel("East", new Vector2(HalfW+.22f,0), new Vector2(.52f,9), Hex("43546B"), -20);
            Panel("Header", new Vector2(0,5.18f), new Vector2(18.4f,.8f), Hex("070B15"), -5);
            Glyph("WILD MAGIC  //  3 НА 3", new Vector2(0,5.2f), Hex("D6E2EF"), .18f, 20);
            score = Glyph(string.Empty, new Vector2(0,4.82f), Hex("D6E2EF"), .12f, 20);
            Glyph("WASD / СТРЕЛКИ — ДВИЖЕНИЕ    ЛКМ — ОГНЕННЫЙ ШАР    R — НОВЫЙ БОЙ    ESC — МЕНЮ",
                new Vector2(0,-4.88f), new Color(.84f,.89f,.94f,.72f), .1f, 20);
        }

        private void StartBattle()
        {
            ClearBattle(); finished = false; paused = false; result = string.Empty;
            Vector2[] left = { new(-5.6f,0), new(-4.9f,2), new(-4.9f,-2) };
            Vector2[] right = { new(5.6f,0), new(4.9f,2), new(4.9f,-2) };
            List<int> colors = new() {0,1,2,3,4,5};
            for (int i=0;i<6;i++) { int j=UnityEngine.Random.Range(i,6); (colors[i],colors[j])=(colors[j],colors[i]); }
            for (int i=0;i<3;i++) CreateMage(0, i==0, left[i], colors[i]);
            for (int i=0;i<3;i++) CreateMage(1, false, right[i], colors[i+3]);
            RefreshScore();
        }

        private void CreateMage(int team, bool player, Vector2 position, int colorIndex)
        {
            var color = Palette[colorIndex];
            Mage mage = new() { Team=team, Player=player, Position=position, Alive=true, Hp=3,
                Aim=team==0?Vector2.right:Vector2.left, NextDecision=Time.time, NextShot=Time.time+.5f };
            GameObject go = new(player?"Player Mage":$"Team {team+1} Mage"); go.transform.SetParent(transform); go.transform.localScale=Vector3.one*Scale;
            mage.Renderer=go.AddComponent<SpriteRenderer>(); mage.Renderer.sortingOrder=5;
            mage.Idle=MakeFrames($"{color.name} Team Idle",color.robe,color.magic,false);
            mage.Walk=MakeFrames($"{color.name} Team Walk",color.robe,color.magic,true);
            Panel("Team Mark", position+new Vector2(0,-.08f), new Vector2(.62f,.09f), team==0?Hex("3C8DFF"):Hex("FF4E62"), 3, go.transform, true);
            mages.Add(mage);
        }

        private Frame[] MakeFrames(string name, Color robe, Color magic, bool walking)
        {
            Sprite[] sprites=WildMagicShowcase.CreateMageFrames(name,robe,magic,walking); Frame[] frames=new Frame[sprites.Length];
            for(int f=0;f<sprites.Length;f++)
            {
                Color32[] outer=sprites[f].texture.GetPixels32(); Color32[] flesh=WildMagicShowcase.CreateMageFleshPixels(f,walking);
                Color32[] bones=WildMagicShowcase.CreateMageSkeletonPixels(f,walking);
                frames[f]=new Frame{Sprite=sprites[f],Outer=outer,Flesh=flesh,Bones=bones,Width=sprites[f].texture.width,Height=sprites[f].texture.height};
                Rebuild(frames[f]); art.Add(sprites[f]); art.Add(sprites[f].texture);
            }
            return frames;
        }

        private void UpdatePlayer(Keyboard? keyboard)
        {
            Mage p=mages[0]; if(!p.Alive)return;
            Vector2 move=Vector2.zero;
            if(keyboard!=null){if(keyboard.aKey.isPressed||keyboard.leftArrowKey.isPressed)move.x--;if(keyboard.dKey.isPressed||keyboard.rightArrowKey.isPressed)move.x++;if(keyboard.sKey.isPressed||keyboard.downArrowKey.isPressed)move.y--;if(keyboard.wKey.isPressed||keyboard.upArrowKey.isPressed)move.y++;}
            p.Moving=move.sqrMagnitude>.01f; if(p.Moving)p.Position=Clamp(p.Position+move.normalized*3.15f*Time.deltaTime);
            if(Mouse.current!=null){Vector3 world=cameraRef.ScreenToWorldPoint(Mouse.current.position.ReadValue());p.Aim=((Vector2)world-p.Position).normalized;if(Mouse.current.leftButton.isPressed&&Time.time>=p.NextShot){p.NextShot=Time.time+.3f;Fire(p,p.Aim);}}
        }

        private void UpdateAi(Mage mage,int index)
        {
            if(!mage.Alive)return; Mage? target=NearestEnemy(mage); if(target==null)return;
            Vector2 delta=target.Position-mage.Position; float distance=delta.magnitude; mage.Aim=delta.normalized;
            Vector2 move=distance>4.7f?mage.Aim:distance<2.5f?-mage.Aim:new Vector2(-mage.Aim.y,mage.Aim.x)*Mathf.Sign(Mathf.Sin(Time.time*.9f+index*1.7f));
            mage.Moving=move.sqrMagnitude>.01f; mage.Position=Clamp(mage.Position+move.normalized*2.15f*Time.deltaTime);
            if(Time.time>=mage.NextShot&&distance<7.2f){mage.NextShot=Time.time+UnityEngine.Random.Range(.72f,1.22f);Fire(mage,(delta+UnityEngine.Random.insideUnitCircle*.18f).normalized);}
        }

        private Mage? NearestEnemy(Mage source)
        {
            Mage? best=null;float d=float.MaxValue;foreach(Mage candidate in mages){if(!candidate.Alive||candidate.Team==source.Team)continue;float n=(candidate.Position-source.Position).sqrMagnitude;if(n<d){d=n;best=candidate;}}return best;
        }

        private void Fire(Mage owner,Vector2 direction)
        {
            GameObject go=new("Team Fireball");go.transform.SetParent(transform);go.transform.localScale=Vector3.one*.13f;
            SpriteRenderer r=go.AddComponent<SpriteRenderer>();r.sprite=white;r.color=Hex("FF7A22");r.sortingOrder=12;
            Vector2 pos=owner.Position+direction*.5f;go.transform.position=pos;shots.Add(new Shot{Object=go,Team=owner.Team,Position=pos,Velocity=direction*9.5f,Life=2.2f});
        }

        private void UpdateShots()
        {
            for(int i=shots.Count-1;i>=0;i--){Shot s=shots[i];s.Position+=s.Velocity*Time.deltaTime;s.Life-=Time.deltaTime;s.Object.transform.position=s.Position;
                Mage? hit=null;foreach(Mage m in mages)if(m.Alive&&m.Team!=s.Team&&(m.Position-s.Position).sqrMagnitude<.32f){hit=m;break;}
                if(hit!=null){Hit(hit,s.Position,s.Velocity.normalized);RemoveShot(i);continue;}if(s.Life<=0||Mathf.Abs(s.Position.x)>HalfW||Mathf.Abs(s.Position.y)>HalfH)RemoveShot(i);}
        }

        private void Hit(Mage mage,Vector2 world,Vector2 direction)
        {
            Sprite sprite=mage.Renderer.sprite;Vector2 local=mage.Renderer.transform.InverseTransformPoint(world);if(mage.Renderer.flipX)local.x=-local.x;
            Vector2 pixel=local*Ppu+sprite.pivot;float u=pixel.x/sprite.texture.width,v=pixel.y/sprite.texture.height;int flesh=0;
            foreach(Frame[] animation in new[]{mage.Idle,mage.Walk})foreach(Frame frame in animation)flesh=Mathf.Max(flesh,DamageFrame(frame,u,v));
            mage.Hp--;mage.BurnUntil=Time.time+3f;Burst(world,direction,flesh>0?Hex("D82038"):Hex("FF6A22"),flesh>0?12:8);
            if(flesh>0)wounds.Add(new Wound{Mage=mage,U=u,V=v,Severity=flesh,Until=Time.time+5f,Next=Time.time+.05f});
            if(mage.Hp<=0)Kill(mage,direction);RefreshScore();
        }

        private static int DamageFrame(Frame frame,float u,float v)
        {
            int cx=Mathf.RoundToInt(u*frame.Width),cy=Mathf.RoundToInt(v*frame.Height),flesh=0;
            for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++){int index=y*frame.Width+x;float d=Vector2.Distance(new Vector2(x,y),new Vector2(cx,cy));if(d<=3){if(frame.Outer[index].a>0)frame.Outer[index]=default;else if(frame.Flesh[index].a>0){frame.Flesh[index]=default;flesh++;}else if(frame.Bones[index].a>0)frame.Bones[index]=Scorch(frame.Bones[index],Hex("4B4238"),.55f);}else if(d<5&&frame.Outer[index].a>0)frame.Outer[index]=Scorch(frame.Outer[index],Hex("0B090A"),.85f);}
            Rebuild(frame);return flesh;
        }

        private void Kill(Mage mage,Vector2 direction)
        {
            mage.Alive=false;mage.Moving=false;GameObject go=new("Fallen Mage");go.transform.SetParent(transform);go.transform.position=mage.Position;go.transform.localScale=Vector3.one*Scale;
            SpriteRenderer r=go.AddComponent<SpriteRenderer>();r.sprite=mage.Renderer.sprite;r.flipX=mage.Renderer.flipX;r.sortingOrder=4;
            corpses.Add(new Corpse{Object=go,Position=mage.Position,Velocity=direction*.45f,Spin=UnityEngine.Random.Range(-130f,130f),Life=.75f});mage.Renderer.gameObject.SetActive(false);
            wounds.RemoveAll(w=>w.Mage==mage);Burst(mage.Position+Vector2.up*.45f,direction,Hex("D82038"),18);
        }

        private void UpdateMages()
        {
            int idle=Mathf.FloorToInt(Time.time*3.5f)%4,walk=Mathf.FloorToInt(Time.time*8f)%4;
            foreach(Mage m in mages){if(!m.Alive)continue;m.Renderer.sprite=m.Moving?m.Walk[walk].Sprite:m.Idle[idle].Sprite;m.Renderer.flipX=m.Aim.x<0;m.Renderer.transform.position=m.Position;
                if(Time.time<m.BurnUntil&&UnityEngine.Random.value<Time.deltaTime*9f)ParticleAt(m.Position+new Vector2(UnityEngine.Random.Range(-.25f,.25f),UnityEngine.Random.Range(.15f,1.1f)),Hex("FF8B21"),.06f,1f);}
        }

        private void UpdateWounds()
        {
            for(int i=wounds.Count-1;i>=0;i--){Wound w=wounds[i];if(!w.Mage.Alive||Time.time>=w.Until){wounds.RemoveAt(i);continue;}if(Time.time<w.Next)continue;
                float power=Mathf.Lerp(1f,3.2f,Mathf.InverseLerp(1,35,w.Severity));w.Next=Time.time+UnityEngine.Random.Range(.12f,.2f)/power;
                Sprite sprite=w.Mage.Renderer.sprite;Vector2 px=new(w.U*sprite.texture.width,w.V*sprite.texture.height);Vector2 local=(px-sprite.pivot)/Ppu;if(w.Mage.Renderer.flipX)local.x=-local.x;Vector2 source=w.Mage.Renderer.transform.TransformPoint(local);ParticleAt(source,Hex("C91F36"),.055f,2.4f);}
        }

        private void UpdateCorpses(){foreach(Corpse c in corpses)if(c.Life>0){c.Life-=Time.deltaTime;c.Position+=c.Velocity*Time.deltaTime;c.Velocity*=Mathf.Pow(.12f,Time.deltaTime);c.Object.transform.position=c.Position;c.Object.transform.Rotate(0,0,c.Spin*Time.deltaTime);c.Spin*=Mathf.Pow(.08f,Time.deltaTime);}}
        private void Burst(Vector2 at,Vector2 direction,Color color,int count){for(int i=0;i<count;i++){ParticleAt(at,color,UnityEngine.Random.Range(.04f,.075f),UnityEngine.Random.Range(1.2f,3.2f),direction*.7f+UnityEngine.Random.insideUnitCircle*1.5f);}}
        private void ParticleAt(Vector2 at,Color color,float size,float gravity,Vector2 velocity=default){GameObject go=new("Battle Particle");go.transform.SetParent(transform);go.transform.position=at;go.transform.localScale=Vector3.one*size;SpriteRenderer r=go.AddComponent<SpriteRenderer>();r.sprite=white;r.color=color;r.sortingOrder=13;particles.Add(new Particle{Object=go,Renderer=r,Position=at,Velocity=velocity+UnityEngine.Random.insideUnitCircle*.18f,Life=UnityEngine.Random.Range(.45f,1.1f),MaxLife=1.1f,Gravity=gravity});}
        private void UpdateParticles(){for(int i=particles.Count-1;i>=0;i--){Particle p=particles[i];p.Life-=Time.deltaTime;p.Velocity.y-=p.Gravity*Time.deltaTime;p.Position+=p.Velocity*Time.deltaTime;p.Object.transform.position=p.Position;Color c=p.Renderer.color;c.a=Mathf.Clamp01(p.Life/.25f);p.Renderer.color=c;if(p.Life<=0){Destroy(p.Object);particles.RemoveAt(i);}}}

        private void CheckVictory(){int blue=mages.FindAll(m=>m.Alive&&m.Team==0).Count,red=mages.FindAll(m=>m.Alive&&m.Team==1).Count;if(blue==0||red==0){finished=true;result=blue>0?"СИНЯЯ КОМАНДА ПОБЕДИЛА":"КРАСНАЯ КОМАНДА ПОБЕДИЛА";}}
        private void RefreshScore(){int blue=mages.FindAll(m=>m.Alive&&m.Team==0).Count,red=mages.FindAll(m=>m.Alive&&m.Team==1).Count;score.text=$"СИНЯЯ КОМАНДА  {blue}   ◆   {red}  КРАСНАЯ КОМАНДА";}
        private void ClearBattle(){foreach(Shot s in shots)Destroy(s.Object);foreach(Particle p in particles)Destroy(p.Object);foreach(Corpse c in corpses)Destroy(c.Object);foreach(Mage m in mages){Destroy(m.Renderer.gameObject);foreach(Frame f in m.Idle)DestroyFrame(f);foreach(Frame f in m.Walk)DestroyFrame(f);}shots.Clear();particles.Clear();corpses.Clear();wounds.Clear();mages.Clear();}
        private void DestroyFrame(Frame f){art.Remove(f.Sprite.texture);art.Remove(f.Sprite);Destroy(f.Sprite.texture);Destroy(f.Sprite);}
        private void RemoveShot(int i){Destroy(shots[i].Object);shots.RemoveAt(i);}
        private static Vector2 Clamp(Vector2 p){p.x=Mathf.Clamp(p.x,-HalfW+.5f,HalfW-.5f);p.y=Mathf.Clamp(p.y,-HalfH+.45f,HalfH-.45f);return p;}

        private void OnGUI(){if(!paused&&!finished)return;GUI.Box(new Rect(Screen.width/2-190,Screen.height/2-90,380,180),finished?result:"ПАУЗА");if(GUI.Button(new Rect(Screen.width/2-110,Screen.height/2+10,220,42),"НАЧАТЬ ЗАНОВО"))StartBattle();}
        private void OnDestroy(){ClearBattle();foreach(UnityEngine.Object item in art)if(item!=null)Destroy(item);}
        private void Panel(string name,Vector2 pos,Vector2 size,Color color,int order,Transform? parent=null,bool local=false){GameObject go=new(name);go.transform.SetParent(parent??transform,false);go.transform.position=local?Vector3.zero:pos;go.transform.localScale=new Vector3(size.x,size.y,1);SpriteRenderer r=go.AddComponent<SpriteRenderer>();r.sprite=white;r.color=color;r.sortingOrder=order;}
        private TextMesh Glyph(string text,Vector2 pos,Color color,float size,int order){GameObject go=new("Text");go.transform.SetParent(transform);go.transform.position=pos;TextMesh mesh=go.AddComponent<TextMesh>();mesh.text=text;mesh.font=font;mesh.fontSize=64;mesh.characterSize=size;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=color;MeshRenderer r=go.GetComponent<MeshRenderer>();r.material=font.material;r.sortingOrder=order;return mesh;}
        private Sprite MakeWhite(){Texture2D t=new(1,1,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,hideFlags=HideFlags.HideAndDontSave};t.SetPixel(0,0,Color.white);t.Apply();Sprite s=Sprite.Create(t,new Rect(0,0,1,1),new Vector2(.5f,.5f),1);art.Add(t);art.Add(s);return s;}
        private static void Rebuild(Frame f){Color32[] c=new Color32[f.Outer.Length];for(int i=0;i<c.Length;i++)c[i]=f.Outer[i].a>0?f.Outer[i]:f.Flesh[i].a>0?f.Flesh[i]:f.Bones[i];f.Sprite.texture.SetPixels32(c);f.Sprite.texture.Apply(false,false);}
        private static Color32 Scorch(Color32 source,Color target,float strength){Color c=Color.Lerp(source,target,strength);c.a=source.a;return c;}
        private static Color Hex(string hex){ColorUtility.TryParseHtmlString("#"+hex,out Color c);return c;}

        private sealed class Mage{public int Team,Hp;public bool Player,Alive,Moving;public Vector2 Position,Aim;public float NextShot,NextDecision,BurnUntil;public SpriteRenderer Renderer=null!;public Frame[] Idle=Array.Empty<Frame>(),Walk=Array.Empty<Frame>();}
        private sealed class Frame{public Sprite Sprite=null!;public Color32[] Outer=Array.Empty<Color32>(),Flesh=Array.Empty<Color32>(),Bones=Array.Empty<Color32>();public int Width,Height;}
        private sealed class Shot{public GameObject Object=null!;public int Team;public Vector2 Position,Velocity;public float Life;}
        private sealed class Wound{public Mage Mage=null!;public float U,V,Until,Next;public int Severity;}
        private sealed class Particle{public GameObject Object=null!;public SpriteRenderer Renderer=null!;public Vector2 Position,Velocity;public float Life,MaxLife,Gravity;}
        private sealed class Corpse{public GameObject Object=null!;public Vector2 Position,Velocity;public float Spin,Life;}
    }
}
