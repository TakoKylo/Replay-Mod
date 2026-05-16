using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PuckReplayMod
{
    public static class ReplayBinarySerializer
    {
        private const byte EventInitialSnapshot = 1;
        private const byte EventTransformFrame = 2;
        private const byte EventPlayerSpawned = 3;
        private const byte EventPlayerDespawned = 4;
        private const byte EventPlayerBodySpawned = 5;
        private const byte EventPlayerBodyDespawned = 6;
        private const byte EventPuckSpawned = 7;
        private const byte EventPuckDespawned = 8;
        private const byte EventPlayerState = 9;
        private const byte EventGameState = 10;
        private const byte EventScoreboardSnapshot = 11;
        private const byte EventChatMessage = 12;
        private const byte EventMarker = 13;

        public static bool IsBinaryReplay(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            using (FileStream stream = File.OpenRead(filePath))
            {
                return HasBinaryMagic(stream);
            }
        }

        public static void Save(string filePath, ReplaySessionData session)
        {
            if (session == null)
            {
                throw new ArgumentNullException("session");
            }

            using (FileStream stream = File.Create(filePath))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteMagic(writer);
                writer.Write(ReplayModConstants.ReplayBinaryFormatVersion);
                WriteHeader(writer, session.Header);

                List<ReplayEventDto> events = session.Events ?? new List<ReplayEventDto>();
                writer.Write(events.Count);
                for (int i = 0; i < events.Count; i++)
                {
                    WriteEvent(writer, events[i]);
                }
            }
        }

        public static ReplayHeaderDto ReadHeader(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                ReadAndValidatePrelude(reader);
                return ReadHeader(reader);
            }
        }

        public static ReplaySessionData Load(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                ReadAndValidatePrelude(reader);
                ReplaySessionData session = new ReplaySessionData
                {
                    Header = ReadHeader(reader),
                    Events = new List<ReplayEventDto>()
                };

                int eventCount = reader.ReadInt32();
                if (eventCount < 0)
                {
                    throw new InvalidDataException("Replay event count is invalid.");
                }

                session.Events = new List<ReplayEventDto>(eventCount);
                for (int i = 0; i < eventCount; i++)
                {
                    session.Events.Add(ReadEvent(reader));
                }

                return session;
            }
        }

        private static void WriteMagic(BinaryWriter writer)
        {
            byte[] magic = Encoding.ASCII.GetBytes(ReplayModConstants.ReplayBinaryMagic);
            writer.Write(magic);
        }

        private static bool HasBinaryMagic(Stream stream)
        {
            if (stream == null || stream.Length < ReplayModConstants.ReplayBinaryMagic.Length)
            {
                return false;
            }

            long originalPosition = stream.Position;
            byte[] expected = Encoding.ASCII.GetBytes(ReplayModConstants.ReplayBinaryMagic);
            byte[] actual = new byte[expected.Length];
            int read = stream.Read(actual, 0, actual.Length);
            stream.Position = originalPosition;
            if (read != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void ReadAndValidatePrelude(BinaryReader reader)
        {
            byte[] expected = Encoding.ASCII.GetBytes(ReplayModConstants.ReplayBinaryMagic);
            byte[] actual = reader.ReadBytes(expected.Length);
            if (actual.Length != expected.Length)
            {
                throw new InvalidDataException("Replay file is too small.");
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    throw new InvalidDataException("Replay binary magic does not match " + ReplayModConstants.ReplayBinaryMagic + ".");
                }
            }

            int version = reader.ReadInt32();
            if (version != ReplayModConstants.ReplayBinaryFormatVersion)
            {
                throw new InvalidDataException("Replay binary format version " + version + " is not supported.");
            }
        }

        private static void WriteHeader(BinaryWriter writer, ReplayHeaderDto header)
        {
            header = header ?? new ReplayHeaderDto();
            WriteString(writer, header.Magic);
            writer.Write(header.FormatVersion);
            WriteString(writer, header.ModVersion);
            WriteString(writer, header.GameVersion);
            WriteString(writer, header.ServerName);
            WriteString(writer, header.RecordedBy);
            writer.Write(header.StartedUtcTicks);
            writer.Write(header.EndedUtcTicks);
            writer.Write(header.TickRate);
            writer.Write(header.TotalTicks);
            writer.Write(header.EventCount);
            writer.Write(header.HasScoreboard);
            writer.Write(header.HasChat);
            writer.Write(header.HasMarkers);
        }

        private static ReplayHeaderDto ReadHeader(BinaryReader reader)
        {
            ReplayHeaderDto header = new ReplayHeaderDto
            {
                Magic = ReadString(reader),
                FormatVersion = reader.ReadInt32(),
                ModVersion = ReadString(reader),
                GameVersion = ReadString(reader),
                ServerName = ReadString(reader),
                RecordedBy = ReadString(reader),
                StartedUtcTicks = reader.ReadInt64(),
                EndedUtcTicks = reader.ReadInt64(),
                TickRate = reader.ReadInt32(),
                TotalTicks = reader.ReadInt32(),
                EventCount = reader.ReadInt32(),
                HasScoreboard = reader.ReadBoolean(),
                HasChat = reader.ReadBoolean(),
                HasMarkers = reader.ReadBoolean()
            };

            ValidateHeader(header);
            return header;
        }

        private static void ValidateHeader(ReplayHeaderDto header)
        {
            if (header == null)
            {
                throw new InvalidDataException("Replay header could not be read.");
            }

            if (header.Magic != ReplayModConstants.ReplayMagic)
            {
                throw new InvalidDataException("Replay magic does not match " + ReplayModConstants.ReplayMagic + ".");
            }

            if (header.FormatVersion > 2)
            {
                throw new InvalidDataException("Replay DTO format version " + header.FormatVersion + " is newer than this mod supports.");
            }

            if (header.TickRate <= 0)
            {
                throw new InvalidDataException("Replay tick rate is invalid.");
            }
        }

        private static void WriteEvent(BinaryWriter writer, ReplayEventDto replayEvent)
        {
            replayEvent = replayEvent ?? new ReplayEventDto();
            byte eventKind = ToEventKind(replayEvent.Type);
            writer.Write(replayEvent.Tick);
            writer.Write(eventKind);
            writer.Write(replayEvent.Payload != null);
            if (replayEvent.Payload == null)
            {
                return;
            }

            switch (eventKind)
            {
                case EventInitialSnapshot:
                    WriteInitialSnapshot(writer, replayEvent.Payload as InitialSnapshotPayload);
                    return;
                case EventTransformFrame:
                    WriteTransformFrame(writer, replayEvent.Payload as TransformFramePayload);
                    return;
                case EventPlayerSpawned:
                case EventPlayerDespawned:
                    WritePlayerLifecycle(writer, replayEvent.Payload as PlayerLifecyclePayload);
                    return;
                case EventPlayerBodySpawned:
                case EventPlayerBodyDespawned:
                    WriteBodyLifecycle(writer, replayEvent.Payload as BodyLifecyclePayload);
                    return;
                case EventPuckSpawned:
                case EventPuckDespawned:
                    WritePuckLifecycle(writer, replayEvent.Payload as PuckLifecyclePayload);
                    return;
                case EventPlayerState:
                    WritePlayerSnapshot(writer, replayEvent.Payload as PlayerSnapshotPayload);
                    return;
                case EventGameState:
                    WriteGameState(writer, replayEvent.Payload as GameStatePayload);
                    return;
                case EventScoreboardSnapshot:
                    WriteScoreboardSnapshot(writer, replayEvent.Payload as ScoreboardSnapshotPayload);
                    return;
                case EventChatMessage:
                    WriteChatMessage(writer, replayEvent.Payload as ChatMessagePayload);
                    return;
                case EventMarker:
                    WriteMarker(writer, replayEvent.Payload as MarkerPayload);
                    return;
                default:
                    throw new InvalidDataException("Unknown binary event kind: " + eventKind + ".");
            }
        }

        private static ReplayEventDto ReadEvent(BinaryReader reader)
        {
            int tick = reader.ReadInt32();
            byte eventKind = reader.ReadByte();
            bool hasPayload = reader.ReadBoolean();
            return new ReplayEventDto
            {
                Tick = tick,
                Type = ToEventName(eventKind),
                Payload = hasPayload ? ReadPayload(reader, eventKind) : null
            };
        }

        private static object ReadPayload(BinaryReader reader, byte eventKind)
        {
            switch (eventKind)
            {
                case EventInitialSnapshot:
                    return ReadInitialSnapshot(reader);
                case EventTransformFrame:
                    return ReadTransformFrame(reader);
                case EventPlayerSpawned:
                case EventPlayerDespawned:
                    return ReadPlayerLifecycle(reader);
                case EventPlayerBodySpawned:
                case EventPlayerBodyDespawned:
                    return ReadBodyLifecycle(reader);
                case EventPuckSpawned:
                case EventPuckDespawned:
                    return ReadPuckLifecycle(reader);
                case EventPlayerState:
                    return ReadPlayerSnapshot(reader);
                case EventGameState:
                    return ReadGameState(reader);
                case EventScoreboardSnapshot:
                    return ReadScoreboardSnapshot(reader);
                case EventChatMessage:
                    return ReadChatMessage(reader);
                case EventMarker:
                    return ReadMarker(reader);
                default:
                    throw new InvalidDataException("Unknown binary event kind: " + eventKind + ".");
            }
        }

        private static byte ToEventKind(string type)
        {
            switch (type)
            {
                case "InitialSnapshot":
                    return EventInitialSnapshot;
                case "TransformFrame":
                    return EventTransformFrame;
                case "PlayerSpawned":
                    return EventPlayerSpawned;
                case "PlayerDespawned":
                    return EventPlayerDespawned;
                case "PlayerBodySpawned":
                    return EventPlayerBodySpawned;
                case "PlayerBodyDespawned":
                    return EventPlayerBodyDespawned;
                case "PuckSpawned":
                    return EventPuckSpawned;
                case "PuckDespawned":
                    return EventPuckDespawned;
                case "PlayerState":
                    return EventPlayerState;
                case "GameState":
                    return EventGameState;
                case "ScoreboardSnapshot":
                    return EventScoreboardSnapshot;
                case "ChatMessage":
                    return EventChatMessage;
                case "Marker":
                    return EventMarker;
                default:
                    throw new InvalidDataException("Replay event type is not supported by the binary format: " + (type ?? "<null>") + ".");
            }
        }

        private static string ToEventName(byte eventKind)
        {
            switch (eventKind)
            {
                case EventInitialSnapshot:
                    return "InitialSnapshot";
                case EventTransformFrame:
                    return "TransformFrame";
                case EventPlayerSpawned:
                    return "PlayerSpawned";
                case EventPlayerDespawned:
                    return "PlayerDespawned";
                case EventPlayerBodySpawned:
                    return "PlayerBodySpawned";
                case EventPlayerBodyDespawned:
                    return "PlayerBodyDespawned";
                case EventPuckSpawned:
                    return "PuckSpawned";
                case EventPuckDespawned:
                    return "PuckDespawned";
                case EventPlayerState:
                    return "PlayerState";
                case EventGameState:
                    return "GameState";
                case EventScoreboardSnapshot:
                    return "ScoreboardSnapshot";
                case EventChatMessage:
                    return "ChatMessage";
                case EventMarker:
                    return "Marker";
                default:
                    throw new InvalidDataException("Unknown binary event kind: " + eventKind + ".");
            }
        }

        private static void WriteInitialSnapshot(BinaryWriter writer, InitialSnapshotPayload payload)
        {
            payload = payload ?? new InitialSnapshotPayload();
            writer.Write(payload.GameState != null);
            if (payload.GameState != null)
            {
                WriteGameState(writer, payload.GameState);
            }

            WritePlayerSnapshotList(writer, payload.Players);
            WriteBodyLifecycleList(writer, payload.PlayerBodies);
            WritePuckSnapshotList(writer, payload.Pucks);
            WriteStickSnapshotList(writer, payload.Sticks);
        }

        private static InitialSnapshotPayload ReadInitialSnapshot(BinaryReader reader)
        {
            InitialSnapshotPayload payload = new InitialSnapshotPayload();
            if (reader.ReadBoolean())
            {
                payload.GameState = ReadGameState(reader);
            }

            payload.Players = ReadPlayerSnapshotList(reader);
            payload.PlayerBodies = ReadBodyLifecycleList(reader);
            payload.Pucks = ReadPuckSnapshotList(reader);
            payload.Sticks = ReadStickSnapshotList(reader);
            return payload;
        }

        private static void WriteTransformFrame(BinaryWriter writer, TransformFramePayload payload)
        {
            payload = payload ?? new TransformFramePayload();
            WritePlayerBodyTransformList(writer, payload.PlayerBodies);
            WriteStickTransformList(writer, payload.Sticks);
            WritePuckTransformList(writer, payload.Pucks);
        }

        private static TransformFramePayload ReadTransformFrame(BinaryReader reader)
        {
            return new TransformFramePayload
            {
                PlayerBodies = ReadPlayerBodyTransformList(reader),
                Sticks = ReadStickTransformList(reader),
                Pucks = ReadPuckTransformList(reader)
            };
        }

        private static void WritePlayerLifecycle(BinaryWriter writer, PlayerLifecyclePayload payload)
        {
            writer.Write(payload != null && payload.Player != null);
            if (payload != null && payload.Player != null)
            {
                WritePlayerSnapshot(writer, payload.Player);
            }
        }

        private static PlayerLifecyclePayload ReadPlayerLifecycle(BinaryReader reader)
        {
            return new PlayerLifecyclePayload
            {
                Player = reader.ReadBoolean() ? ReadPlayerSnapshot(reader) : null
            };
        }

        private static void WriteBodyLifecycle(BinaryWriter writer, BodyLifecyclePayload payload)
        {
            payload = payload ?? new BodyLifecyclePayload();
            writer.Write(payload.OwnerClientId);
            WriteVector3(writer, payload.Position);
            WriteQuaternion(writer, payload.Rotation);
        }

        private static BodyLifecyclePayload ReadBodyLifecycle(BinaryReader reader)
        {
            return new BodyLifecyclePayload
            {
                OwnerClientId = reader.ReadUInt64(),
                Position = ReadVector3(reader),
                Rotation = ReadQuaternion(reader)
            };
        }

        private static void WritePuckLifecycle(BinaryWriter writer, PuckLifecyclePayload payload)
        {
            payload = payload ?? new PuckLifecyclePayload();
            writer.Write(payload.NetworkObjectId);
            WriteVector3(writer, payload.Position);
            WriteQuaternion(writer, payload.Rotation);
        }

        private static PuckLifecyclePayload ReadPuckLifecycle(BinaryReader reader)
        {
            return new PuckLifecyclePayload
            {
                NetworkObjectId = reader.ReadUInt64(),
                Position = ReadVector3(reader),
                Rotation = ReadQuaternion(reader)
            };
        }

        private static void WritePlayerSnapshot(BinaryWriter writer, PlayerSnapshotPayload payload)
        {
            payload = payload ?? new PlayerSnapshotPayload();
            writer.Write(payload.OwnerClientId);
            WriteString(writer, payload.SteamId);
            WriteString(writer, payload.Username);
            writer.Write(payload.Number);
            writer.Write(payload.Goals);
            writer.Write(payload.Assists);
            writer.Write(payload.PatreonLevel);
            writer.Write(payload.AdminLevel);
            WriteString(writer, payload.Phase);
            WriteString(writer, payload.Team);
            WriteString(writer, payload.Role);
            WriteString(writer, payload.Handedness);
            WriteString(writer, payload.PositionName);
            writer.Write(payload.IsMuted);
            writer.Write(payload.Customization != null);
            if (payload.Customization != null)
            {
                WriteCustomization(writer, payload.Customization);
            }
        }

        private static PlayerSnapshotPayload ReadPlayerSnapshot(BinaryReader reader)
        {
            PlayerSnapshotPayload payload = new PlayerSnapshotPayload
            {
                OwnerClientId = reader.ReadUInt64(),
                SteamId = ReadString(reader),
                Username = ReadString(reader),
                Number = reader.ReadInt32(),
                Goals = reader.ReadInt32(),
                Assists = reader.ReadInt32(),
                PatreonLevel = reader.ReadInt32(),
                AdminLevel = reader.ReadInt32(),
                Phase = ReadString(reader),
                Team = ReadString(reader),
                Role = ReadString(reader),
                Handedness = ReadString(reader),
                PositionName = ReadString(reader),
                IsMuted = reader.ReadBoolean()
            };

            if (reader.ReadBoolean())
            {
                payload.Customization = ReadCustomization(reader);
            }

            return payload;
        }

        private static void WriteCustomization(BinaryWriter writer, PlayerCustomizationPayload payload)
        {
            writer.Write(payload.FlagID);
            writer.Write(payload.HeadgearIDBlueAttacker);
            writer.Write(payload.HeadgearIDRedAttacker);
            writer.Write(payload.HeadgearIDBlueGoalie);
            writer.Write(payload.HeadgearIDRedGoalie);
            writer.Write(payload.MustacheID);
            writer.Write(payload.BeardID);
            writer.Write(payload.JerseyIDBlueAttacker);
            writer.Write(payload.JerseyIDRedAttacker);
            writer.Write(payload.JerseyIDBlueGoalie);
            writer.Write(payload.JerseyIDRedGoalie);
            writer.Write(payload.StickSkinIDBlueAttacker);
            writer.Write(payload.StickSkinIDRedAttacker);
            writer.Write(payload.StickSkinIDBlueGoalie);
            writer.Write(payload.StickSkinIDRedGoalie);
            writer.Write(payload.StickShaftTapeIDBlueAttacker);
            writer.Write(payload.StickShaftTapeIDRedAttacker);
            writer.Write(payload.StickShaftTapeIDBlueGoalie);
            writer.Write(payload.StickShaftTapeIDRedGoalie);
            writer.Write(payload.StickBladeTapeIDBlueAttacker);
            writer.Write(payload.StickBladeTapeIDRedAttacker);
            writer.Write(payload.StickBladeTapeIDBlueGoalie);
            writer.Write(payload.StickBladeTapeIDRedGoalie);
        }

        private static PlayerCustomizationPayload ReadCustomization(BinaryReader reader)
        {
            return new PlayerCustomizationPayload
            {
                FlagID = reader.ReadInt32(),
                HeadgearIDBlueAttacker = reader.ReadInt32(),
                HeadgearIDRedAttacker = reader.ReadInt32(),
                HeadgearIDBlueGoalie = reader.ReadInt32(),
                HeadgearIDRedGoalie = reader.ReadInt32(),
                MustacheID = reader.ReadInt32(),
                BeardID = reader.ReadInt32(),
                JerseyIDBlueAttacker = reader.ReadInt32(),
                JerseyIDRedAttacker = reader.ReadInt32(),
                JerseyIDBlueGoalie = reader.ReadInt32(),
                JerseyIDRedGoalie = reader.ReadInt32(),
                StickSkinIDBlueAttacker = reader.ReadInt32(),
                StickSkinIDRedAttacker = reader.ReadInt32(),
                StickSkinIDBlueGoalie = reader.ReadInt32(),
                StickSkinIDRedGoalie = reader.ReadInt32(),
                StickShaftTapeIDBlueAttacker = reader.ReadInt32(),
                StickShaftTapeIDRedAttacker = reader.ReadInt32(),
                StickShaftTapeIDBlueGoalie = reader.ReadInt32(),
                StickShaftTapeIDRedGoalie = reader.ReadInt32(),
                StickBladeTapeIDBlueAttacker = reader.ReadInt32(),
                StickBladeTapeIDRedAttacker = reader.ReadInt32(),
                StickBladeTapeIDBlueGoalie = reader.ReadInt32(),
                StickBladeTapeIDRedGoalie = reader.ReadInt32()
            };
        }

        private static void WriteGameState(BinaryWriter writer, GameStatePayload payload)
        {
            payload = payload ?? new GameStatePayload();
            WriteString(writer, payload.Phase);
            writer.Write(payload.Tick);
            writer.Write(payload.Period);
            writer.Write(payload.BlueScore);
            writer.Write(payload.RedScore);
            writer.Write(payload.IsOvertime);
        }

        private static GameStatePayload ReadGameState(BinaryReader reader)
        {
            return new GameStatePayload
            {
                Phase = ReadString(reader),
                Tick = reader.ReadInt32(),
                Period = reader.ReadInt32(),
                BlueScore = reader.ReadInt32(),
                RedScore = reader.ReadInt32(),
                IsOvertime = reader.ReadBoolean()
            };
        }

        private static void WriteScoreboardSnapshot(BinaryWriter writer, ScoreboardSnapshotPayload payload)
        {
            payload = payload ?? new ScoreboardSnapshotPayload();
            WritePlayerSnapshotList(writer, payload.Players);
        }

        private static ScoreboardSnapshotPayload ReadScoreboardSnapshot(BinaryReader reader)
        {
            return new ScoreboardSnapshotPayload
            {
                Players = ReadPlayerSnapshotList(reader)
            };
        }

        private static void WriteChatMessage(BinaryWriter writer, ChatMessagePayload payload)
        {
            payload = payload ?? new ChatMessagePayload();
            WriteString(writer, payload.SteamId);
            WriteString(writer, payload.Username);
            WriteString(writer, payload.Team);
            WriteString(writer, payload.Message);
            writer.Write(payload.IsQuickChat);
            writer.Write(payload.IsTeamChat);
            writer.Write(payload.IsSystem);
        }

        private static ChatMessagePayload ReadChatMessage(BinaryReader reader)
        {
            return new ChatMessagePayload
            {
                SteamId = ReadString(reader),
                Username = ReadString(reader),
                Team = ReadString(reader),
                Message = ReadString(reader),
                IsQuickChat = reader.ReadBoolean(),
                IsTeamChat = reader.ReadBoolean(),
                IsSystem = reader.ReadBoolean()
            };
        }

        private static void WriteMarker(BinaryWriter writer, MarkerPayload payload)
        {
            payload = payload ?? new MarkerPayload();
            writer.Write(payload.CreatedUtcTicks);
        }

        private static MarkerPayload ReadMarker(BinaryReader reader)
        {
            return new MarkerPayload
            {
                CreatedUtcTicks = reader.ReadInt64()
            };
        }

        private static void WritePlayerBodyTransformList(BinaryWriter writer, List<PlayerBodyTransformPayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                PlayerBodyTransformPayload payload = items[i] ?? new PlayerBodyTransformPayload();
                writer.Write(payload.OwnerClientId);
                WriteVector3(writer, payload.Position);
                WriteQuaternion(writer, payload.Rotation);
                writer.Write(payload.Stamina);
                writer.Write(payload.Speed);
                writer.Write(payload.IsSprinting);
                writer.Write(payload.IsSliding);
                writer.Write(payload.IsStopping);
                writer.Write(payload.IsExtendedLeft);
                writer.Write(payload.IsExtendedRight);
            }
        }

        private static List<PlayerBodyTransformPayload> ReadPlayerBodyTransformList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<PlayerBodyTransformPayload> items = new List<PlayerBodyTransformPayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new PlayerBodyTransformPayload
                {
                    OwnerClientId = reader.ReadUInt64(),
                    Position = ReadVector3(reader),
                    Rotation = ReadQuaternion(reader),
                    Stamina = reader.ReadSingle(),
                    Speed = reader.ReadSingle(),
                    IsSprinting = reader.ReadBoolean(),
                    IsSliding = reader.ReadBoolean(),
                    IsStopping = reader.ReadBoolean(),
                    IsExtendedLeft = reader.ReadBoolean(),
                    IsExtendedRight = reader.ReadBoolean()
                });
            }

            return items;
        }

        private static void WriteStickTransformList(BinaryWriter writer, List<StickTransformPayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                StickTransformPayload payload = items[i] ?? new StickTransformPayload();
                writer.Write(payload.OwnerClientId);
                WriteVector3(writer, payload.Position);
                WriteQuaternion(writer, payload.Rotation);
            }
        }

        private static List<StickTransformPayload> ReadStickTransformList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<StickTransformPayload> items = new List<StickTransformPayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new StickTransformPayload
                {
                    OwnerClientId = reader.ReadUInt64(),
                    Position = ReadVector3(reader),
                    Rotation = ReadQuaternion(reader)
                });
            }

            return items;
        }

        private static void WritePuckTransformList(BinaryWriter writer, List<PuckTransformPayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                PuckTransformPayload payload = items[i] ?? new PuckTransformPayload();
                writer.Write(payload.NetworkObjectId);
                WriteVector3(writer, payload.Position);
                WriteQuaternion(writer, payload.Rotation);
            }
        }

        private static List<PuckTransformPayload> ReadPuckTransformList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<PuckTransformPayload> items = new List<PuckTransformPayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new PuckTransformPayload
                {
                    NetworkObjectId = reader.ReadUInt64(),
                    Position = ReadVector3(reader),
                    Rotation = ReadQuaternion(reader)
                });
            }

            return items;
        }

        private static void WritePlayerSnapshotList(BinaryWriter writer, List<PlayerSnapshotPayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                WritePlayerSnapshot(writer, items[i]);
            }
        }

        private static List<PlayerSnapshotPayload> ReadPlayerSnapshotList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<PlayerSnapshotPayload> items = new List<PlayerSnapshotPayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(ReadPlayerSnapshot(reader));
            }

            return items;
        }

        private static void WriteBodyLifecycleList(BinaryWriter writer, List<BodyLifecyclePayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                WriteBodyLifecycle(writer, items[i]);
            }
        }

        private static List<BodyLifecyclePayload> ReadBodyLifecycleList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<BodyLifecyclePayload> items = new List<BodyLifecyclePayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(ReadBodyLifecycle(reader));
            }

            return items;
        }

        private static void WritePuckSnapshotList(BinaryWriter writer, List<PuckSnapshotPayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                PuckSnapshotPayload payload = items[i] ?? new PuckSnapshotPayload();
                writer.Write(payload.NetworkObjectId);
                WriteVector3(writer, payload.Position);
                WriteQuaternion(writer, payload.Rotation);
            }
        }

        private static List<PuckSnapshotPayload> ReadPuckSnapshotList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<PuckSnapshotPayload> items = new List<PuckSnapshotPayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new PuckSnapshotPayload
                {
                    NetworkObjectId = reader.ReadUInt64(),
                    Position = ReadVector3(reader),
                    Rotation = ReadQuaternion(reader)
                });
            }

            return items;
        }

        private static void WriteStickSnapshotList(BinaryWriter writer, List<StickSnapshotPayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                StickSnapshotPayload payload = items[i] ?? new StickSnapshotPayload();
                writer.Write(payload.OwnerClientId);
                WriteVector3(writer, payload.Position);
                WriteQuaternion(writer, payload.Rotation);
            }
        }

        private static List<StickSnapshotPayload> ReadStickSnapshotList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<StickSnapshotPayload> items = new List<StickSnapshotPayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new StickSnapshotPayload
                {
                    OwnerClientId = reader.ReadUInt64(),
                    Position = ReadVector3(reader),
                    Rotation = ReadQuaternion(reader)
                });
            }

            return items;
        }

        private static void WriteVector3(BinaryWriter writer, Vector3Dto value)
        {
            writer.Write(value != null ? value.X : 0f);
            writer.Write(value != null ? value.Y : 0f);
            writer.Write(value != null ? value.Z : 0f);
        }

        private static Vector3Dto ReadVector3(BinaryReader reader)
        {
            return new Vector3Dto
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle()
            };
        }

        private static void WriteQuaternion(BinaryWriter writer, QuaternionDto value)
        {
            writer.Write(value != null ? value.X : 0f);
            writer.Write(value != null ? value.Y : 0f);
            writer.Write(value != null ? value.Z : 0f);
            writer.Write(value != null ? value.W : 1f);
        }

        private static QuaternionDto ReadQuaternion(BinaryReader reader)
        {
            return new QuaternionDto
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
                W = reader.ReadSingle()
            };
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value ?? string.Empty);
        }

        private static string ReadString(BinaryReader reader)
        {
            return reader.ReadString();
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException("Replay collection count is invalid.");
            }

            return count;
        }
    }
}
