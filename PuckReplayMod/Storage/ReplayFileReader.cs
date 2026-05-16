using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PuckReplayMod
{
    public class ReplayFileReader
    {
        public List<ReplayFileSummary> GetRecentReplays(string replayDirectory, string summaryDirectory, int maxCount)
        {
            List<ReplayFileSummary> summaries = new List<ReplayFileSummary>();
            if (!Directory.Exists(replayDirectory))
            {
                return summaries;
            }

            FileInfo[] files = new DirectoryInfo(replayDirectory)
                .GetFiles("*" + ReplayModConstants.ReplayFileExtension)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            foreach (FileInfo file in files)
            {
                try
                {
                    summaries.Add(this.ReadSummaryFast(file, summaryDirectory));
                }
                catch (Exception exception)
                {
                    ReplayModLog.Warning("Failed to read replay summary " + file.FullName + ": " + exception.Message);
                }
            }

            return summaries
                .OrderByDescending(summary => summary.IsFavorite)
                .ThenByDescending(summary => summary.LastWriteUtc)
                .Take(Math.Max(0, maxCount))
                .ToList();
        }

        public bool IndexNextMissingSummary(string replayDirectory, string summaryDirectory, int maxCount)
        {
            if (!Directory.Exists(replayDirectory))
            {
                return false;
            }

            FileInfo[] files = new DirectoryInfo(replayDirectory)
                .GetFiles("*" + ReplayModConstants.ReplayFileExtension)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(0, maxCount))
                .ToArray();

            foreach (FileInfo file in files)
            {
                if (this.HasSummaryCache(file, summaryDirectory))
                {
                    continue;
                }

                try
                {
                    this.ReadSummary(file.FullName, summaryDirectory);
                    return true;
                }
                catch (Exception exception)
                {
                    ReplayModLog.Warning("Failed to index replay summary " + file.FullName + ": " + exception.Message);
                }
            }

            return false;
        }

        public ReplayFileSummary ReadSummary(string filePath, string summaryDirectory)
        {
            bool isBinaryReplay = ReplayBinarySerializer.IsBinaryReplay(filePath);
            ReplayHeaderDto header = isBinaryReplay
                ? ReplayBinarySerializer.ReadHeader(filePath)
                : this.ReadHeader(this.ReadRoot(filePath));
            FileInfo file = new FileInfo(filePath);
            ReplayFileSummary cachedSummary = this.TryReadSummaryCache(file, summaryDirectory);
            ReplayFileSummary summary = new ReplayFileSummary
            {
                FilePath = filePath,
                FileName = file.Name,
                SizeBytes = file.Length,
                LastWriteUtc = file.LastWriteTimeUtc,
                ServerName = header.ServerName,
                DisplayName = cachedSummary != null ? cachedSummary.DisplayName : string.Empty,
                RecordedBy = header.RecordedBy,
                ReplayMagic = header.Magic,
                ReplayFormatVersion = header.FormatVersion,
                ReplayContainerFormat = isBinaryReplay ? ReplayModConstants.ReplayBinaryMagic : "JSON",
                ReplayContainerVersion = isBinaryReplay ? ReplayModConstants.ReplayBinaryFormatVersion : 0,
                ModVersion = header.ModVersion,
                GameVersion = header.GameVersion,
                StartedUtcTicks = header.StartedUtcTicks,
                EndedUtcTicks = header.EndedUtcTicks,
                TickRate = header.TickRate,
                TotalTicks = header.TotalTicks,
                EventCount = header.EventCount,
                HasScoreboard = header.HasScoreboard,
                HasChat = header.HasChat,
                HasMarkers = header.HasMarkers,
                IsFavorite = cachedSummary != null && cachedSummary.IsFavorite,
                IsMetadataComplete = true
            };

            this.WriteSummaryCache(summary, summaryDirectory);
            return summary;
        }

        public void WriteSummaryCache(ReplayFileSummary summary, string summaryDirectory)
        {
            if (summary == null || string.IsNullOrEmpty(summary.FilePath))
            {
                return;
            }

            ReplaySummaryCache cache = new ReplaySummaryCache
            {
                FileName = summary.FileName,
                SizeBytes = summary.SizeBytes,
                LastWriteUtcTicks = summary.LastWriteUtc.Ticks,
                ServerName = summary.ServerName,
                DisplayName = summary.DisplayName,
                RecordedBy = summary.RecordedBy,
                ReplayMagic = summary.ReplayMagic,
                ReplayFormatVersion = summary.ReplayFormatVersion,
                ReplayContainerFormat = summary.ReplayContainerFormat,
                ReplayContainerVersion = summary.ReplayContainerVersion,
                ModVersion = summary.ModVersion,
                GameVersion = summary.GameVersion,
                StartedUtcTicks = summary.StartedUtcTicks,
                EndedUtcTicks = summary.EndedUtcTicks,
                TickRate = summary.TickRate,
                TotalTicks = summary.TotalTicks,
                EventCount = summary.EventCount,
                HasScoreboard = summary.HasScoreboard,
                HasChat = summary.HasChat,
                HasMarkers = summary.HasMarkers,
                IsFavorite = summary.IsFavorite
            };

            string summaryPath = GetSummaryPath(summary.FilePath, summaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(summaryPath));
            File.WriteAllText(summaryPath, JsonConvert.SerializeObject(cache, Formatting.None));
        }

        public ReplaySessionData Load(string filePath)
        {
            if (ReplayBinarySerializer.IsBinaryReplay(filePath))
            {
                return ReplayBinarySerializer.Load(filePath);
            }

            JObject root = this.ReadRoot(filePath);
            ReplaySessionData session = new ReplaySessionData
            {
                Header = this.ReadHeader(root),
                Events = new List<ReplayEventDto>()
            };

            JArray events = root["Events"] as JArray;
            if (events == null)
            {
                throw new InvalidDataException("Replay is missing an Events array.");
            }

            foreach (JToken eventToken in events)
            {
                string type = eventToken.Value<string>("Type");
                JToken payloadToken = eventToken["Payload"];
                session.Events.Add(new ReplayEventDto
                {
                    Tick = eventToken.Value<int?>("Tick") ?? 0,
                    Type = type,
                    Payload = this.ReadPayload(type, payloadToken)
                });
            }

            return session;
        }

        private JObject ReadRoot(string filePath)
        {
            using (Stream stream = OpenReplayReadStream(filePath))
            using (StreamReader streamReader = new StreamReader(stream))
            using (JsonTextReader jsonReader = new JsonTextReader(streamReader))
            {
                return JObject.Load(jsonReader);
            }
        }

        private static Stream OpenReplayReadStream(string filePath)
        {
            FileStream fileStream = File.OpenRead(filePath);
            if (fileStream.Length < 2)
            {
                return fileStream;
            }

            int first = fileStream.ReadByte();
            int second = fileStream.ReadByte();
            fileStream.Position = 0L;
            if (first == 0x1F && second == 0x8B)
            {
                return new GZipStream(fileStream, CompressionMode.Decompress);
            }

            return fileStream;
        }

        public static string GetSummaryPath(string replayFilePath, string summaryDirectory)
        {
            return Path.Combine(summaryDirectory, Path.GetFileName(replayFilePath) + ReplayModConstants.ReplaySummaryFileSuffix);
        }

        private ReplayFileSummary ReadSummaryFast(FileInfo file, string summaryDirectory)
        {
            ReplayFileSummary cachedSummary = this.TryReadSummaryCache(file, summaryDirectory);
            if (cachedSummary != null)
            {
                return cachedSummary;
            }

            ReplayFileSummary summary = new ReplayFileSummary
            {
                FilePath = file.FullName,
                FileName = file.Name,
                SizeBytes = file.Length,
                LastWriteUtc = file.LastWriteTimeUtc,
                ServerName = Path.GetFileNameWithoutExtension(file.Name),
                DisplayName = string.Empty,
                RecordedBy = string.Empty,
                ReplayMagic = string.Empty,
                ReplayFormatVersion = 0,
                ReplayContainerFormat = string.Empty,
                ReplayContainerVersion = 0,
                ModVersion = string.Empty,
                GameVersion = string.Empty,
                StartedUtcTicks = 0,
                EndedUtcTicks = 0,
                TickRate = 0,
                TotalTicks = 0,
                EventCount = 0,
                HasScoreboard = false,
                HasChat = false,
                HasMarkers = false,
                IsFavorite = false,
                IsMetadataComplete = false
            };

            return summary;
        }

        private ReplayFileSummary TryReadSummaryCache(FileInfo file, string summaryDirectory)
        {
            string summaryPath = GetSummaryPath(file.FullName, summaryDirectory);
            if (!File.Exists(summaryPath))
            {
                return null;
            }

            ReplaySummaryCache cache = JsonConvert.DeserializeObject<ReplaySummaryCache>(File.ReadAllText(summaryPath));
            if (cache == null)
            {
                return null;
            }

            return new ReplayFileSummary
            {
                FilePath = file.FullName,
                FileName = file.Name,
                SizeBytes = cache.SizeBytes > 0 ? cache.SizeBytes : file.Length,
                LastWriteUtc = cache.LastWriteUtcTicks > 0 ? new DateTime(cache.LastWriteUtcTicks, DateTimeKind.Utc) : file.LastWriteTimeUtc,
                ServerName = cache.ServerName,
                DisplayName = cache.DisplayName,
                RecordedBy = cache.RecordedBy,
                ReplayMagic = cache.ReplayMagic,
                ReplayFormatVersion = cache.ReplayFormatVersion,
                ReplayContainerFormat = cache.ReplayContainerFormat,
                ReplayContainerVersion = cache.ReplayContainerVersion,
                ModVersion = cache.ModVersion,
                GameVersion = cache.GameVersion,
                StartedUtcTicks = cache.StartedUtcTicks,
                EndedUtcTicks = cache.EndedUtcTicks,
                TickRate = cache.TickRate,
                TotalTicks = cache.TotalTicks,
                EventCount = cache.EventCount,
                HasScoreboard = cache.HasScoreboard,
                HasChat = cache.HasChat,
                HasMarkers = cache.HasMarkers,
                IsFavorite = cache.IsFavorite,
                IsMetadataComplete = true
            };
        }

        private bool HasSummaryCache(FileInfo file, string summaryDirectory)
        {
            return File.Exists(GetSummaryPath(file.FullName, summaryDirectory));
        }

        private ReplayHeaderDto ReadHeader(JObject root)
        {
            JToken headerToken = root["Header"];
            if (headerToken == null)
            {
                throw new InvalidDataException("Replay is missing a Header object.");
            }

            ReplayHeaderDto header = headerToken.ToObject<ReplayHeaderDto>();
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
                throw new InvalidDataException("Replay format version " + header.FormatVersion + " is newer than this mod supports.");
            }

            if (header.TickRate <= 0)
            {
                throw new InvalidDataException("Replay tick rate is invalid.");
            }

            return header;
        }

        private object ReadPayload(string type, JToken payloadToken)
        {
            if (payloadToken == null || payloadToken.Type == JTokenType.Null)
            {
                return null;
            }

            switch (type)
            {
                case "InitialSnapshot":
                    return payloadToken.ToObject<InitialSnapshotPayload>();
                case "TransformFrame":
                    return payloadToken.ToObject<TransformFramePayload>();
                case "PlayerSpawned":
                case "PlayerDespawned":
                    return payloadToken.ToObject<PlayerLifecyclePayload>();
                case "PlayerBodySpawned":
                case "PlayerBodyDespawned":
                    return payloadToken.ToObject<BodyLifecyclePayload>();
                case "PuckSpawned":
                case "PuckDespawned":
                    return payloadToken.ToObject<PuckLifecyclePayload>();
                case "PlayerState":
                    return payloadToken.ToObject<PlayerSnapshotPayload>();
                case "GameState":
                    return payloadToken.ToObject<GameStatePayload>();
                case "ScoreboardSnapshot":
                    return payloadToken.ToObject<ScoreboardSnapshotPayload>();
                case "ChatMessage":
                    return payloadToken.ToObject<ChatMessagePayload>();
                case "Marker":
                    return payloadToken.ToObject<MarkerPayload>();
                default:
                    return payloadToken;
            }
        }
    }
}
