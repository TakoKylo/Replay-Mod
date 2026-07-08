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
        private const byte EventStickSpawned = 14;
        private const byte EventStickDespawned = 15;
        private const byte EventGoalScored = 16;
        internal const string ChunkMagic = "RCH1";
        internal const string KeyframeMagic = "RKF1";
        internal const string IndexMagic = "RIDX";
        internal const string FooterMagic = "RFT1";
        private const int ChunkedFormatVersion = 5;
        private const int FooterSize = 16;

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
                WritePrelude(writer, Math.Min(ReplayModConstants.ReplayBinaryFormatVersion, 4));
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
            int containerVersion;
            return ReadHeader(filePath, out containerVersion);
        }

        public static ReplayHeaderDto ReadHeader(string filePath, out int containerVersion)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                containerVersion = ReadAndValidatePrelude(reader);
                return ReadHeader(reader);
            }
        }

        public static ReplaySessionData Load(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int containerVersion = ReadAndValidatePrelude(reader);
                ReplaySessionData session = new ReplaySessionData
                {
                    Header = ReadHeader(reader),
                    Events = new List<ReplayEventDto>()
                };

                if (containerVersion >= ChunkedFormatVersion)
                {
                    ReadChunkedEventsAndKeyframes(reader, containerVersion, session);
                    return session;
                }

                int eventCount = reader.ReadInt32();
                if (eventCount < 0)
                {
                    throw new InvalidDataException("Replay event count is invalid.");
                }

                session.Events = new List<ReplayEventDto>(eventCount);
                for (int i = 0; i < eventCount; i++)
                {
                    session.Events.Add(ReadEvent(reader, containerVersion));
                }

                return session;
            }
        }

        public static bool TryReadChunkedIndex(string filePath, out ReplayChunkedFileIndex index)
        {
            index = null;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            using (FileStream stream = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int containerVersion = ReadAndValidatePrelude(reader);
                ReplayHeaderDto header = ReadHeader(reader);
                if (containerVersion < ChunkedFormatVersion)
                {
                    return false;
                }

                index = ReadChunkedIndex(reader, containerVersion, header);
                return true;
            }
        }

        public static ReplaySessionData LoadChunkRange(string filePath, ReplayChunkedFileIndex index, int startTick, int endTick)
        {
            if (index == null)
            {
                throw new ArgumentNullException("index");
            }

            if (endTick < startTick)
            {
                endTick = startTick;
            }

            using (FileStream stream = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int containerVersion = ReadAndValidatePrelude(reader);
                ReplayHeaderDto header = ReadHeader(reader);
                if (containerVersion < ChunkedFormatVersion)
                {
                    throw new InvalidDataException("Replay is not chunked.");
                }

                ReplaySessionData session = new ReplaySessionData
                {
                    Header = header,
                    Events = new List<ReplayEventDto>(),
                    Keyframes = index.Keyframes ?? new List<ReplayKeyframeDto>()
                };

                for (int i = 0; i < index.Chunks.Count; i++)
                {
                    ReplayChunkIndexEntry chunk = index.Chunks[i];
                    if (chunk == null || chunk.LastTick < startTick || chunk.FirstTick > endTick)
                    {
                        continue;
                    }

                    ReadChunkEvents(reader, containerVersion, chunk, session.Events, startTick, endTick);
                }

                return session;
            }
        }

        // Pushes every event through `visit` one at a time without ever holding the whole match in
        // memory. Building the save-time timeline summary this way keeps finalize from reloading a
        // full (mostly TransformFrame) recording into the managed heap, which on Unity's
        // non-compacting GC would otherwise spike committed RAM on tight servers.
        public static void StreamEvents(string filePath, Action<ReplayEventDto> visit)
        {
            if (visit == null)
            {
                throw new ArgumentNullException("visit");
            }

            using (FileStream stream = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int containerVersion = ReadAndValidatePrelude(reader);
                ReplayHeaderDto header = ReadHeader(reader);
                if (containerVersion >= ChunkedFormatVersion)
                {
                    ReplayChunkedFileIndex index = ReadChunkedIndex(reader, containerVersion, header);
                    for (int i = 0; i < index.Chunks.Count; i++)
                    {
                        StreamChunkEvents(reader, containerVersion, index.Chunks[i], visit);
                    }

                    return;
                }

                int eventCount = reader.ReadInt32();
                if (eventCount < 0)
                {
                    throw new InvalidDataException("Replay event count is invalid.");
                }

                for (int i = 0; i < eventCount; i++)
                {
                    visit(ReadEvent(reader, containerVersion));
                }
            }
        }

        internal static void WritePrelude(BinaryWriter writer, int containerVersion)
        {
            WriteMagic(writer);
            writer.Write(containerVersion);
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

        private static int ReadAndValidatePrelude(BinaryReader reader)
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
            if (version < 1 || version > ReplayModConstants.ReplayBinaryFormatVersion)
            {
                throw new InvalidDataException("Replay binary format version " + version + " is not supported.");
            }

            return version;
        }

        internal static void WriteHeaderTo(BinaryWriter writer, ReplayHeaderDto header)
        {
            WriteHeader(writer, header);
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

            if (header.FormatVersion > ReplayModConstants.ReplayDtoFormatVersion)
            {
                throw new InvalidDataException("Replay DTO format version " + header.FormatVersion + " is newer than this mod supports.");
            }

            if (header.TickRate <= 0)
            {
                throw new InvalidDataException("Replay tick rate is invalid.");
            }
        }

        internal static void WriteEventTo(BinaryWriter writer, ReplayEventDto replayEvent)
        {
            WriteEvent(writer, replayEvent);
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
                case EventStickSpawned:
                case EventStickDespawned:
                    WriteStickSnapshot(writer, replayEvent.Payload as StickSnapshotPayload);
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
                case EventGoalScored:
                    WriteGoalScored(writer, replayEvent.Payload as GoalScoredPayload);
                    return;
                default:
                    throw new InvalidDataException("Unknown binary event kind: " + eventKind + ".");
            }
        }

        internal static ReplayEventDto ReadEventFrom(BinaryReader reader, int containerVersion)
        {
            return ReadEvent(reader, containerVersion);
        }

        private static ReplayEventDto ReadEvent(BinaryReader reader, int containerVersion)
        {
            int tick = reader.ReadInt32();
            byte eventKind = reader.ReadByte();
            bool hasPayload = reader.ReadBoolean();
            return new ReplayEventDto
            {
                Tick = tick,
                Type = ToEventName(eventKind),
                Payload = hasPayload ? ReadPayload(reader, eventKind, containerVersion) : null
            };
        }

        private static void ReadChunkedEventsAndKeyframes(BinaryReader reader, int containerVersion, ReplaySessionData session)
        {
            ReplayChunkedFileIndex index = ReadChunkedIndex(reader, containerVersion, session.Header);
            int totalEvents = 0;
            for (int i = 0; i < index.Chunks.Count; i++)
            {
                totalEvents += index.Chunks[i].EventCount;
            }

            session.Keyframes = index.Keyframes ?? new List<ReplayKeyframeDto>();
            session.Events = new List<ReplayEventDto>(totalEvents);
            for (int i = 0; i < index.Chunks.Count; i++)
            {
                ReadChunkEvents(reader, containerVersion, index.Chunks[i], session.Events, int.MinValue, int.MaxValue);
            }
        }

        private static ReplayChunkedFileIndex ReadChunkedIndex(BinaryReader reader, int containerVersion, ReplayHeaderDto header)
        {
            Stream stream = reader.BaseStream;
            if (stream.Length < FooterSize)
            {
                throw new InvalidDataException("Chunked replay is missing its footer.");
            }

            stream.Seek(-FooterSize, SeekOrigin.End);
            string footerMagic = ReadFourCharacterCode(reader);
            if (footerMagic != FooterMagic)
            {
                throw new InvalidDataException("Chunked replay footer is missing.");
            }

            long indexOffset = reader.ReadInt64();
            int footerChunkCount = reader.ReadInt32();
            if (indexOffset < 0L || indexOffset >= stream.Length || footerChunkCount < 0)
            {
                throw new InvalidDataException("Chunked replay footer is invalid.");
            }

            ReplayChunkedFileIndex index = new ReplayChunkedFileIndex
            {
                ContainerVersion = containerVersion,
                Header = header,
                Keyframes = new List<ReplayKeyframeDto>(),
                Chunks = new List<ReplayChunkIndexEntry>(footerChunkCount)
            };

            stream.Seek(indexOffset, SeekOrigin.Begin);
            if (PeekFourCharacterCode(reader) == KeyframeMagic)
            {
                ReadFourCharacterCode(reader);
                int keyframeCount = reader.ReadInt32();
                if (keyframeCount < 0)
                {
                    throw new InvalidDataException("Replay keyframe count is invalid.");
                }

                index.Keyframes = new List<ReplayKeyframeDto>(keyframeCount);
                for (int i = 0; i < keyframeCount; i++)
                {
                    int tick = reader.ReadInt32();
                    bool hasSnapshot = reader.ReadBoolean();
                    index.Keyframes.Add(new ReplayKeyframeDto
                    {
                        Tick = tick,
                        Snapshot = hasSnapshot ? ReadInitialSnapshot(reader, containerVersion) : null
                    });
                }
            }

            string indexMagic = ReadFourCharacterCode(reader);
            if (indexMagic != IndexMagic)
            {
                throw new InvalidDataException("Chunked replay index is missing.");
            }

            int chunkCount = reader.ReadInt32();
            if (chunkCount < 0 || chunkCount != footerChunkCount)
            {
                throw new InvalidDataException("Chunked replay index count is invalid.");
            }

            for (int i = 0; i < chunkCount; i++)
            {
                ReplayChunkIndexEntry entry = new ReplayChunkIndexEntry
                {
                    FirstTick = reader.ReadInt32(),
                    LastTick = reader.ReadInt32(),
                    EventCount = reader.ReadInt32(),
                    Offset = reader.ReadInt64(),
                    Length = reader.ReadInt32()
                };
                if (entry.EventCount < 0 || entry.Offset < 0L || entry.Length < 0)
                {
                    throw new InvalidDataException("Chunked replay index entry is invalid.");
                }

                index.Chunks.Add(entry);
            }

            return index;
        }

        private static void ReadChunkEvents(BinaryReader reader, int containerVersion, ReplayChunkIndexEntry chunk, List<ReplayEventDto> events, int startTick, int endTick)
        {
            Stream stream = reader.BaseStream;
            stream.Seek(chunk.Offset, SeekOrigin.Begin);
            string chunkMagic = ReadFourCharacterCode(reader);
            if (chunkMagic != ChunkMagic)
            {
                throw new InvalidDataException("Replay chunk magic is invalid.");
            }

            int firstTick = reader.ReadInt32();
            int lastTick = reader.ReadInt32();
            int eventCount = reader.ReadInt32();
            int payloadLength = reader.ReadInt32();
            if (eventCount != chunk.EventCount || firstTick != chunk.FirstTick || lastTick != chunk.LastTick || payloadLength < 0)
            {
                throw new InvalidDataException("Replay chunk header does not match its index.");
            }

            long payloadEnd = stream.Position + payloadLength;
            for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
            {
                ReplayEventDto replayEvent = ReadEvent(reader, containerVersion);
                if (replayEvent.Tick >= startTick && replayEvent.Tick <= endTick)
                {
                    events.Add(replayEvent);
                }
            }

            stream.Seek(payloadEnd, SeekOrigin.Begin);
        }

        private static void StreamChunkEvents(BinaryReader reader, int containerVersion, ReplayChunkIndexEntry chunk, Action<ReplayEventDto> visit)
        {
            Stream stream = reader.BaseStream;
            stream.Seek(chunk.Offset, SeekOrigin.Begin);
            string chunkMagic = ReadFourCharacterCode(reader);
            if (chunkMagic != ChunkMagic)
            {
                throw new InvalidDataException("Replay chunk magic is invalid.");
            }

            int firstTick = reader.ReadInt32();
            int lastTick = reader.ReadInt32();
            int eventCount = reader.ReadInt32();
            int payloadLength = reader.ReadInt32();
            if (eventCount != chunk.EventCount || firstTick != chunk.FirstTick || lastTick != chunk.LastTick || payloadLength < 0)
            {
                throw new InvalidDataException("Replay chunk header does not match its index.");
            }

            long payloadEnd = stream.Position + payloadLength;
            for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
            {
                visit(ReadEvent(reader, containerVersion));
            }

            stream.Seek(payloadEnd, SeekOrigin.Begin);
        }

        internal static void WriteFourCharacterCode(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            if (bytes.Length != 4)
            {
                throw new InvalidDataException("Four-character code must be exactly four bytes.");
            }

            writer.Write(bytes);
        }

        private static string ReadFourCharacterCode(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
            {
                throw new InvalidDataException("Unexpected end of replay file.");
            }

            return Encoding.ASCII.GetString(bytes);
        }

        private static string PeekFourCharacterCode(BinaryReader reader)
        {
            long position = reader.BaseStream.Position;
            string value = ReadFourCharacterCode(reader);
            reader.BaseStream.Position = position;
            return value;
        }

        private static object ReadPayload(BinaryReader reader, byte eventKind, int containerVersion)
        {
            switch (eventKind)
            {
                case EventInitialSnapshot:
                    return ReadInitialSnapshot(reader, containerVersion);
                case EventTransformFrame:
                    return ReadTransformFrame(reader, containerVersion);
                case EventPlayerSpawned:
                case EventPlayerDespawned:
                    return ReadPlayerLifecycle(reader);
                case EventPlayerBodySpawned:
                case EventPlayerBodyDespawned:
                    return ReadBodyLifecycle(reader, containerVersion);
                case EventStickSpawned:
                case EventStickDespawned:
                    return ReadStickSnapshot(reader);
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
                case EventGoalScored:
                    return ReadGoalScored(reader);
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
                case "StickSpawned":
                    return EventStickSpawned;
                case "StickDespawned":
                    return EventStickDespawned;
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
                case "GoalScored":
                    return EventGoalScored;
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
                case EventStickSpawned:
                    return "StickSpawned";
                case EventStickDespawned:
                    return "StickDespawned";
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
                case EventGoalScored:
                    return "GoalScored";
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
            WritePlayerInputList(writer, payload.PlayerInputs);
        }

        internal static void WriteInitialSnapshotTo(BinaryWriter writer, InitialSnapshotPayload payload)
        {
            WriteInitialSnapshot(writer, payload);
        }

        private static InitialSnapshotPayload ReadInitialSnapshot(BinaryReader reader, int containerVersion)
        {
            InitialSnapshotPayload payload = new InitialSnapshotPayload();
            if (reader.ReadBoolean())
            {
                payload.GameState = ReadGameState(reader);
            }

            payload.Players = ReadPlayerSnapshotList(reader);
            payload.PlayerBodies = ReadBodyLifecycleList(reader, containerVersion);
            payload.Pucks = ReadPuckSnapshotList(reader);
            payload.Sticks = ReadStickSnapshotList(reader);
            payload.PlayerInputs = containerVersion >= 2 ? ReadPlayerInputList(reader) : new List<PlayerInputPayload>();
            return payload;
        }

        private static void WriteTransformFrame(BinaryWriter writer, TransformFramePayload payload)
        {
            payload = payload ?? new TransformFramePayload();
            WritePlayerBodyTransformList(writer, payload.PlayerBodies);
            WriteStickTransformList(writer, payload.Sticks);
            WritePuckTransformList(writer, payload.Pucks);
            WritePlayerInputList(writer, payload.PlayerInputs);
        }

        private static TransformFramePayload ReadTransformFrame(BinaryReader reader, int containerVersion)
        {
            return new TransformFramePayload
            {
                PlayerBodies = ReadPlayerBodyTransformList(reader),
                Sticks = ReadStickTransformList(reader),
                Pucks = ReadPuckTransformList(reader),
                PlayerInputs = containerVersion >= 2 ? ReadPlayerInputList(reader) : new List<PlayerInputPayload>()
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
            writer.Write(payload.Player != null);
            if (payload.Player != null)
            {
                WritePlayerSnapshot(writer, payload.Player);
            }
        }

        private static BodyLifecyclePayload ReadBodyLifecycle(BinaryReader reader, int containerVersion)
        {
            BodyLifecyclePayload payload = new BodyLifecyclePayload
            {
                OwnerClientId = reader.ReadUInt64(),
                Position = ReadVector3(reader),
                Rotation = ReadQuaternion(reader)
            };

            if (containerVersion >= 3 && reader.ReadBoolean())
            {
                payload.Player = ReadPlayerSnapshot(reader);
            }

            return payload;
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

        private static void WriteGoalScored(BinaryWriter writer, GoalScoredPayload payload)
        {
            payload = payload ?? new GoalScoredPayload();
            WriteString(writer, payload.Team);
            writer.Write(payload.BlueScore);
            writer.Write(payload.RedScore);
            WriteNullablePlayerSnapshot(writer, payload.Scorer);
            WriteNullablePlayerSnapshot(writer, payload.Assist);
            WriteNullablePlayerSnapshot(writer, payload.SecondAssist);
            writer.Write(payload.PuckNetworkObjectId);
            writer.Write(payload.PuckSpeed);
            writer.Write(payload.PuckShotSpeed);
        }

        private static GoalScoredPayload ReadGoalScored(BinaryReader reader)
        {
            return new GoalScoredPayload
            {
                Team = ReadString(reader),
                BlueScore = reader.ReadInt32(),
                RedScore = reader.ReadInt32(),
                Scorer = ReadNullablePlayerSnapshot(reader),
                Assist = ReadNullablePlayerSnapshot(reader),
                SecondAssist = ReadNullablePlayerSnapshot(reader),
                PuckNetworkObjectId = reader.ReadUInt64(),
                PuckSpeed = reader.ReadSingle(),
                PuckShotSpeed = reader.ReadSingle()
            };
        }

        private static void WriteNullablePlayerSnapshot(BinaryWriter writer, PlayerSnapshotPayload payload)
        {
            writer.Write(payload != null);
            if (payload != null)
            {
                WritePlayerSnapshot(writer, payload);
            }
        }

        private static PlayerSnapshotPayload ReadNullablePlayerSnapshot(BinaryReader reader)
        {
            return reader.ReadBoolean() ? ReadPlayerSnapshot(reader) : null;
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

        private static void WritePlayerInputList(BinaryWriter writer, List<PlayerInputPayload> items)
        {
            writer.Write(items != null ? items.Count : 0);
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                PlayerInputPayload payload = items[i] ?? new PlayerInputPayload();
                writer.Write(payload.OwnerClientId);
                WriteVector2(writer, payload.LookAngleInput);
                writer.Write(payload.BladeAngleInput);
                writer.Write(payload.TrackInput);
                writer.Write(payload.LookInput);
            }
        }

        private static List<PlayerInputPayload> ReadPlayerInputList(BinaryReader reader)
        {
            int count = ReadCount(reader);
            List<PlayerInputPayload> items = new List<PlayerInputPayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new PlayerInputPayload
                {
                    OwnerClientId = reader.ReadUInt64(),
                    LookAngleInput = ReadVector2(reader),
                    BladeAngleInput = reader.ReadSByte(),
                    TrackInput = reader.ReadBoolean(),
                    LookInput = reader.ReadBoolean()
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

        private static List<BodyLifecyclePayload> ReadBodyLifecycleList(BinaryReader reader, int containerVersion)
        {
            int count = ReadCount(reader);
            List<BodyLifecyclePayload> items = new List<BodyLifecyclePayload>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(ReadBodyLifecycle(reader, containerVersion));
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

        private static void WriteStickSnapshot(BinaryWriter writer, StickSnapshotPayload payload)
        {
            payload = payload ?? new StickSnapshotPayload();
            writer.Write(payload.OwnerClientId);
            WriteVector3(writer, payload.Position);
            WriteQuaternion(writer, payload.Rotation);
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

        private static StickSnapshotPayload ReadStickSnapshot(BinaryReader reader)
        {
            return new StickSnapshotPayload
            {
                OwnerClientId = reader.ReadUInt64(),
                Position = ReadVector3(reader),
                Rotation = ReadQuaternion(reader)
            };
        }

        private static void WriteVector3(BinaryWriter writer, Vector3Dto value)
        {
            writer.Write(value != null ? value.X : 0f);
            writer.Write(value != null ? value.Y : 0f);
            writer.Write(value != null ? value.Z : 0f);
        }

        private static void WriteVector2(BinaryWriter writer, Vector2Dto value)
        {
            writer.Write(value != null ? value.X : 0f);
            writer.Write(value != null ? value.Y : 0f);
        }

        private static Vector2Dto ReadVector2(BinaryReader reader)
        {
            return new Vector2Dto
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle()
            };
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
