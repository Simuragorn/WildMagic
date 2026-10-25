using System;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace WildMagic.Prototype
{
    public enum WildMagicNetworkRole : byte
    {
        Host,
        Client
    }

    public enum WildMagicNetworkSpell : byte
    {
        Fireball,
        WideBeam
    }

    public struct WildMagicNetworkInput
    {
        public uint Sequence;
        public Vector2 Move;
        public Vector2 Aim;
        public WildMagicNetworkSpell Spell;
        public bool Fire;
    }

    public struct WildMagicNetworkSnapshot
    {
        public uint Sequence;
        public Vector2 PlayerOnePosition;
        public Vector2 PlayerTwoPosition;
        public Vector2 PlayerOneDirection;
        public Vector2 PlayerTwoDirection;
        public bool PlayerOneMoving;
        public bool PlayerTwoMoving;
        public bool PlayerOneAlive;
        public bool PlayerTwoAlive;
        public int PlayerOneDamage;
        public int PlayerTwoDamage;
    }

    public readonly struct WildMagicNetworkAttack
    {
        public readonly byte Shooter;
        public readonly WildMagicNetworkSpell Spell;
        public readonly Vector2 Origin;
        public readonly Vector2 Direction;

        public WildMagicNetworkAttack(byte shooter, WildMagicNetworkSpell spell, Vector2 origin,
            Vector2 direction)
        {
            Shooter = shooter;
            Spell = spell;
            Origin = origin;
            Direction = direction;
        }
    }

    public readonly struct WildMagicNetworkHit
    {
        public readonly byte Target;
        public readonly WildMagicNetworkSpell Spell;
        public readonly Vector2 Impact;
        public readonly Vector2 Direction;
        public readonly Vector2 CanonicalHit;
        public readonly Vector2 PixelDirection;
        public readonly int DamagePass;

        public WildMagicNetworkHit(byte target, WildMagicNetworkSpell spell, Vector2 impact,
            Vector2 direction, Vector2 canonicalHit, Vector2 pixelDirection, int damagePass)
        {
            Target = target;
            Spell = spell;
            Impact = impact;
            Direction = direction;
            CanonicalHit = canonicalHit;
            PixelDirection = pixelDirection;
            DamagePass = damagePass;
        }
    }

    public readonly struct WildMagicNetworkFragmentHit
    {
        public readonly byte Shooter;
        public readonly WildMagicNetworkSpell Spell;
        public readonly Vector2 Impact;
        public readonly Vector2 Direction;
        public readonly Vector2 BeamStart;
        public readonly Vector2 BeamEnd;
        public readonly int DamagePass;

        public WildMagicNetworkFragmentHit(byte shooter, WildMagicNetworkSpell spell, Vector2 impact,
            Vector2 direction, Vector2 beamStart, Vector2 beamEnd, int damagePass)
        {
            Shooter = shooter;
            Spell = spell;
            Impact = impact;
            Direction = direction;
            BeamStart = beamStart;
            BeamEnd = beamEnd;
            DamagePass = damagePass;
        }
    }

    /// <summary>
    /// Small two-player protocol for the arena. The desktop build is the authoritative host and
    /// a WebGL build is always the second player. All traffic uses WebSocket so the browser can
    /// connect directly through a Radmin VPN address.
    /// </summary>
    public sealed class WildMagicNetworkSession : IDisposable
    {
        public const ushort Port = 7778;
        public const byte ProtocolVersion = 2;
        private const uint PacketMagic = 0x574D5031; // WMP1

        private enum MessageType : byte
        {
            Input = 1,
            Snapshot = 2,
            MatchSetup = 3,
            Attack = 4,
            Hit = 5,
            RestartRequest = 6,
            FragmentHit = 7
        }

        private NetworkDriver driver;
        private NetworkConnection connection;
        private bool initialized;
        private string status = "СЕТЬ НЕ ЗАПУЩЕНА";

        public WildMagicNetworkRole Role { get; private set; }
        public bool IsHost => Role == WildMagicNetworkRole.Host;
        public bool IsClient => Role == WildMagicNetworkRole.Client;
        public bool HasOpponent { get; private set; }
        public string Status => status;
        public WildMagicNetworkInput RemoteInput { get; private set; }

        public event Action? OpponentConnected;
        public event Action? OpponentDisconnected;
        public event Action<WildMagicNetworkSnapshot>? SnapshotReceived;
        public event Action<int, int, uint>? MatchSetupReceived;
        public event Action<WildMagicNetworkAttack>? AttackReceived;
        public event Action<WildMagicNetworkHit>? HitReceived;
        public event Action<WildMagicNetworkFragmentHit>? FragmentHitReceived;
        public event Action? RestartRequested;

        public void InitializeDefault()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            InitializeClient(GetBrowserHost());
#else
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!string.Equals(arguments[i], "--client", StringComparison.OrdinalIgnoreCase))
                    continue;
                string host = i + 1 < arguments.Length ? arguments[i + 1] : "127.0.0.1";
                InitializeClient(host);
                return;
            }
            InitializeHost();
#endif
        }

        private void InitializeHost()
        {
            Role = WildMagicNetworkRole.Host;
            driver = NetworkDriver.Create(new WebSocketNetworkInterface());
            NetworkEndpoint endpoint = NetworkEndpoint.AnyIpv4.WithPort(Port);
            if (driver.Bind(endpoint) != 0 || driver.Listen() != 0)
            {
                status = $"НЕ УДАЛОСЬ ОТКРЫТЬ TCP {Port}";
                Debug.LogError($"WildMagic multiplayer could not listen on WebSocket port {Port}.");
                return;
            }

            initialized = true;
            status = $"ХОСТ: ОЖИДАНИЕ ИГРОКА НА TCP {Port}";
            Debug.Log($"WildMagic host is listening for WebSocket clients on port {Port}.");
        }

        private void InitializeClient(string host)
        {
            Role = WildMagicNetworkRole.Client;
            driver = NetworkDriver.Create(new WebSocketNetworkInterface());
            NetworkEndpoint endpoint;
            try
            {
                endpoint = NetworkEndpoint.Parse(host, Port, NetworkFamily.Ipv4);
            }
            catch (Exception exception)
            {
                status = $"НЕВЕРНЫЙ АДРЕС: {host}";
                Debug.LogException(exception);
                return;
            }

            connection = driver.Connect(endpoint);
            initialized = true;
            status = $"ПОДКЛЮЧЕНИЕ К {host}:{Port}";
            Debug.Log($"WildMagic WebSocket client is connecting to {host}:{Port}.");
        }

        public void Tick()
        {
            if (!initialized || !driver.IsCreated)
                return;

            driver.ScheduleUpdate().Complete();
            if (IsHost)
                TickHost();
            else
                TickClient();
        }

        private void TickHost()
        {
            NetworkConnection accepted;
            while ((accepted = driver.Accept()) != default)
            {
                if (connection.IsCreated)
                {
                    accepted.Disconnect(driver);
                    continue;
                }

                connection = accepted;
                HasOpponent = true;
                status = "ИГРОК 2 ПОДКЛЮЧЁН";
                Debug.Log("WildMagic Player 2 connected to the host.");
                OpponentConnected?.Invoke();
            }

            if (!connection.IsCreated)
                return;

            DataStreamReader reader;
            NetworkEvent.Type eventType;
            while ((eventType = connection.PopEvent(driver, out reader)) != NetworkEvent.Type.Empty)
            {
                if (eventType == NetworkEvent.Type.Data)
                    ReadPacket(reader);
                else if (eventType == NetworkEvent.Type.Disconnect)
                    HandleDisconnect();
            }
        }

        private void TickClient()
        {
            if (!connection.IsCreated)
                return;

            DataStreamReader reader;
            NetworkEvent.Type eventType;
            while ((eventType = connection.PopEvent(driver, out reader)) != NetworkEvent.Type.Empty)
            {
                if (eventType == NetworkEvent.Type.Connect)
                {
                    HasOpponent = true;
                    status = "ПОДКЛЮЧЕНО: ВЫ — ИГРОК 2";
                    Debug.Log("WildMagic client connected to the host.");
                    OpponentConnected?.Invoke();
                }
                else if (eventType == NetworkEvent.Type.Data)
                {
                    ReadPacket(reader);
                }
                else if (eventType == NetworkEvent.Type.Disconnect)
                {
                    HandleDisconnect();
                }
            }
        }

        private void HandleDisconnect()
        {
            connection = default;
            HasOpponent = false;
            status = IsHost ? $"ХОСТ: ОЖИДАНИЕ ИГРОКА НА TCP {Port}" : "СВЯЗЬ С ХОСТОМ ПОТЕРЯНА";
            RemoteInput = default;
            Debug.LogWarning("WildMagic multiplayer connection closed.");
            OpponentDisconnected?.Invoke();
        }

        private void ReadPacket(DataStreamReader reader)
        {
            if (reader.Length < 6 || reader.ReadUInt() != PacketMagic)
                return;
            byte version = reader.ReadByte();
            if (version != ProtocolVersion)
            {
                status = "НЕСОВМЕСТИМАЯ ВЕРСИЯ ИГРЫ";
                return;
            }

            MessageType type = (MessageType)reader.ReadByte();
            switch (type)
            {
                case MessageType.Input when IsHost:
                    RemoteInput = new WildMagicNetworkInput
                    {
                        Sequence = reader.ReadUInt(),
                        Move = ReadVector2(ref reader),
                        Aim = ReadVector2(ref reader),
                        Spell = (WildMagicNetworkSpell)reader.ReadByte(),
                        Fire = reader.ReadByte() != 0
                    };
                    break;
                case MessageType.Snapshot when IsClient:
                    SnapshotReceived?.Invoke(new WildMagicNetworkSnapshot
                    {
                        Sequence = reader.ReadUInt(),
                        PlayerOnePosition = ReadVector2(ref reader),
                        PlayerTwoPosition = ReadVector2(ref reader),
                        PlayerOneDirection = ReadVector2(ref reader),
                        PlayerTwoDirection = ReadVector2(ref reader),
                        PlayerOneMoving = reader.ReadByte() != 0,
                        PlayerTwoMoving = reader.ReadByte() != 0,
                        PlayerOneAlive = reader.ReadByte() != 0,
                        PlayerTwoAlive = reader.ReadByte() != 0,
                        PlayerOneDamage = reader.ReadInt(),
                        PlayerTwoDamage = reader.ReadInt()
                    });
                    break;
                case MessageType.MatchSetup when IsClient:
                    MatchSetupReceived?.Invoke(reader.ReadInt(), reader.ReadInt(), reader.ReadUInt());
                    break;
                case MessageType.Attack when IsClient:
                    AttackReceived?.Invoke(new WildMagicNetworkAttack(reader.ReadByte(),
                        (WildMagicNetworkSpell)reader.ReadByte(), ReadVector2(ref reader),
                        ReadVector2(ref reader)));
                    break;
                case MessageType.Hit when IsClient:
                    HitReceived?.Invoke(new WildMagicNetworkHit(reader.ReadByte(),
                        (WildMagicNetworkSpell)reader.ReadByte(), ReadVector2(ref reader),
                        ReadVector2(ref reader), ReadVector2(ref reader), ReadVector2(ref reader),
                        reader.ReadInt()));
                    break;
                case MessageType.RestartRequest when IsHost:
                    RestartRequested?.Invoke();
                    break;
                case MessageType.FragmentHit when IsClient:
                    FragmentHitReceived?.Invoke(new WildMagicNetworkFragmentHit(reader.ReadByte(),
                        (WildMagicNetworkSpell)reader.ReadByte(), ReadVector2(ref reader),
                        ReadVector2(ref reader), ReadVector2(ref reader), ReadVector2(ref reader),
                        reader.ReadInt()));
                    break;
            }
        }

        public void SendInput(WildMagicNetworkInput input)
        {
            if (!CanSend || !IsClient)
                return;
            BeginPacket(MessageType.Input, out DataStreamWriter writer);
            writer.WriteUInt(input.Sequence);
            WriteVector2(ref writer, input.Move);
            WriteVector2(ref writer, input.Aim);
            writer.WriteByte((byte)input.Spell);
            writer.WriteByte(input.Fire ? (byte)1 : (byte)0);
            EndPacket(writer);
        }

        public void SendSnapshot(WildMagicNetworkSnapshot snapshot)
        {
            if (!CanSend || !IsHost)
                return;
            BeginPacket(MessageType.Snapshot, out DataStreamWriter writer);
            writer.WriteUInt(snapshot.Sequence);
            WriteVector2(ref writer, snapshot.PlayerOnePosition);
            WriteVector2(ref writer, snapshot.PlayerTwoPosition);
            WriteVector2(ref writer, snapshot.PlayerOneDirection);
            WriteVector2(ref writer, snapshot.PlayerTwoDirection);
            writer.WriteByte(snapshot.PlayerOneMoving ? (byte)1 : (byte)0);
            writer.WriteByte(snapshot.PlayerTwoMoving ? (byte)1 : (byte)0);
            writer.WriteByte(snapshot.PlayerOneAlive ? (byte)1 : (byte)0);
            writer.WriteByte(snapshot.PlayerTwoAlive ? (byte)1 : (byte)0);
            writer.WriteInt(snapshot.PlayerOneDamage);
            writer.WriteInt(snapshot.PlayerTwoDamage);
            EndPacket(writer);
        }

        public void SendMatchSetup(int playerOneColor, int playerTwoColor, uint round)
        {
            if (!CanSend || !IsHost)
                return;
            BeginPacket(MessageType.MatchSetup, out DataStreamWriter writer);
            writer.WriteInt(playerOneColor);
            writer.WriteInt(playerTwoColor);
            writer.WriteUInt(round);
            EndPacket(writer);
        }

        public void SendAttack(byte shooter, WildMagicNetworkSpell spell, Vector2 origin, Vector2 direction)
        {
            if (!CanSend || !IsHost)
                return;
            BeginPacket(MessageType.Attack, out DataStreamWriter writer);
            writer.WriteByte(shooter);
            writer.WriteByte((byte)spell);
            WriteVector2(ref writer, origin);
            WriteVector2(ref writer, direction);
            EndPacket(writer);
        }

        public void SendHit(byte target, WildMagicNetworkSpell spell, Vector2 impact, Vector2 direction,
            Vector2 canonicalHit, Vector2 pixelDirection, int damagePass)
        {
            if (!CanSend || !IsHost)
                return;
            BeginPacket(MessageType.Hit, out DataStreamWriter writer);
            writer.WriteByte(target);
            writer.WriteByte((byte)spell);
            WriteVector2(ref writer, impact);
            WriteVector2(ref writer, direction);
            WriteVector2(ref writer, canonicalHit);
            WriteVector2(ref writer, pixelDirection);
            writer.WriteInt(damagePass);
            EndPacket(writer);
        }

        public void SendRestartRequest()
        {
            if (!CanSend || !IsClient)
                return;
            BeginPacket(MessageType.RestartRequest, out DataStreamWriter writer);
            EndPacket(writer);
        }

        public void SendFragmentHit(byte shooter, WildMagicNetworkSpell spell, Vector2 impact,
            Vector2 direction, Vector2 beamStart, Vector2 beamEnd, int damagePass)
        {
            if (!CanSend || !IsHost)
                return;
            BeginPacket(MessageType.FragmentHit, out DataStreamWriter writer);
            writer.WriteByte(shooter);
            writer.WriteByte((byte)spell);
            WriteVector2(ref writer, impact);
            WriteVector2(ref writer, direction);
            WriteVector2(ref writer, beamStart);
            WriteVector2(ref writer, beamEnd);
            writer.WriteInt(damagePass);
            EndPacket(writer);
        }

        private bool CanSend => initialized && connection.IsCreated && HasOpponent;

        private void BeginPacket(MessageType type, out DataStreamWriter writer)
        {
            driver.BeginSend(connection, out writer);
            writer.WriteUInt(PacketMagic);
            writer.WriteByte(ProtocolVersion);
            writer.WriteByte((byte)type);
        }

        private void EndPacket(DataStreamWriter writer)
        {
            int result = driver.EndSend(writer);
            if (result < 0)
                Debug.LogWarning($"WildMagic network send failed with code {result}.");
        }

        private static void WriteVector2(ref DataStreamWriter writer, Vector2 value)
        {
            writer.WriteFloat(value.x);
            writer.WriteFloat(value.y);
        }

        private static Vector2 ReadVector2(ref DataStreamReader reader)
        {
            return new Vector2(reader.ReadFloat(), reader.ReadFloat());
        }

        private static string GetBrowserHost()
        {
            if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri? uri) &&
                !string.IsNullOrWhiteSpace(uri.Host))
                return uri.Host;
            return "127.0.0.1";
        }

        public void Dispose()
        {
            if (!driver.IsCreated)
                return;
            if (connection.IsCreated)
            {
                connection.Disconnect(driver);
                driver.ScheduleUpdate().Complete();
            }
            driver.Dispose();
            initialized = false;
        }
    }
}
