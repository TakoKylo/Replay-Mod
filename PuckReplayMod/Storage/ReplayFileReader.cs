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
                if (this.HasCurrentSummaryCache(file, summaryDirectory))
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
            int replayContainerVersion = 0;
            ReplayHeaderDto header = isBinaryReplay
                ? ReplayBinarySerializer.ReadHeader(filePath, out replayContainerVersion)
                : this.ReadHeader(this.ReadRoot(filePath));
            FileInfo file = new FileInfo(filePath);
            ReplayFileSummary cachedSummary = this.TryReadSummaryCache(file, summaryDirectory);
            int goalCount;
            int markerCount;
            List<ReplayGameSegmentSummary> gameSegments;
            List<ReplayTimelineEntrySummary> timelineEvents = this.GetTimelineEvents(filePath, isBinaryReplay, header.TotalTicks, cachedSummary, out goalCount, out markerCount, out gameSegments);
            ReplayFileSummary summary = new ReplayFileSummary
            {
                FilePath = filePath,
                FileName = file.Name,
                SizeBytes = file.Length,
                FileCreatedUtcTicks = file.CreationTimeUtc.Ticks,
                LastWriteUtc = file.LastWriteTimeUtc,
                ServerName = header.ServerName,
                DisplayName = cachedSummary != null ? cachedSummary.DisplayName : string.Empty,
                RecordedBy = header.RecordedBy,
                ReplayMagic = header.Magic,
                ReplayFormatVersion = header.FormatVersion,
                ReplayContainerFormat = isBinaryReplay ? ReplayModConstants.ReplayBinaryMagic : "JSON",
                ReplayContainerVersion = replayContainerVersion,
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
                HasGoals = goalCount > 0,
                GoalCount = goalCount,
                MarkerCount = markerCount,
                TimelineEvents = timelineEvents,
                GameSegments = gameSegments,
                IsFavorite = cachedSummary != null && cachedSummary.IsFavorite,
                IsImported = cachedSummary != null && cachedSummary.IsImported,
                ImportedUtcTicks = cachedSummary != null ? cachedSummary.ImportedUtcTicks : 0L,
                IsMetadataComplete = true,
                SummaryCacheVersion = ReplayModConstants.ReplaySummaryCacheVersion,
                SummaryGeneratedUtcTicks = DateTime.UtcNow.Ticks,
                SummaryGeneratedByModVersion = ReplayModConstants.ModVersion
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
                FileCreatedUtcTicks = summary.FileCreatedUtcTicks,
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
                HasGoals = summary.HasGoals,
                GoalCount = summary.GoalCount,
                MarkerCount = summary.MarkerCount,
                TimelineEvents = summary.TimelineEvents ?? new List<ReplayTimelineEntrySummary>(),
                GameSegments = summary.GameSegments ?? new List<ReplayGameSegmentSummary>(),
                IsFavorite = summary.IsFavorite,
                IsImported = summary.IsImported,
                ImportedUtcTicks = summary.ImportedUtcTicks,
                SummaryGeneratedUtcTicks = DateTime.UtcNow.Ticks,
                SummaryGeneratedByModVersion = ReplayModConstants.ModVersion
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

        public ReplaySessionData LoadForPlaybackStart(string filePath, int initialSeconds)
        {
            ReplayChunkedFileIndex index;
            if (ReplayBinarySerializer.IsBinaryReplay(filePath) && ReplayBinarySerializer.TryReadChunkedIndex(filePath, out index))
            {
                int tickRate = index.Header != null ? Math.Max(1, index.Header.TickRate) : 30;
                int totalTicks = index.Header != null ? Math.Max(0, index.Header.TotalTicks) : 0;
                int endTick = Math.Min(totalTicks, Math.Max(tickRate, tickRate * Math.Max(10, initialSeconds)));
                ReplaySessionData session = ReplayBinarySerializer.LoadChunkRange(filePath, index, 0, endTick);
                session.Header = index.Header;
                session.Keyframes = index.Keyframes ?? new List<ReplayKeyframeDto>();
                session.IsLazyChunkRange = true;
                session.LazyLoadedThroughTick = endTick;
                return session;
            }

            return this.Load(filePath);
        }

        public bool TryReadChunkedIndex(string filePath, out ReplayChunkedFileIndex index)
        {
            index = null;
            if (!ReplayBinarySerializer.IsBinaryReplay(filePath))
            {
                return false;
            }

            return ReplayBinarySerializer.TryReadChunkedIndex(filePath, out index);
        }

        public ReplaySessionData LoadChunkRange(string filePath, ReplayChunkedFileIndex index, int startTick, int endTick)
        {
            return ReplayBinarySerializer.LoadChunkRange(filePath, index, startTick, endTick);
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
                FileCreatedUtcTicks = file.CreationTimeUtc.Ticks,
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
                HasGoals = false,
                GoalCount = 0,
                MarkerCount = 0,
                TimelineEvents = new List<ReplayTimelineEntrySummary>(),
                GameSegments = new List<ReplayGameSegmentSummary>(),
                IsFavorite = false,
                IsImported = false,
                ImportedUtcTicks = 0L,
                IsMetadataComplete = false,
                SummaryCacheVersion = 0,
                SummaryGeneratedUtcTicks = 0L,
                SummaryGeneratedByModVersion = string.Empty
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
                FileCreatedUtcTicks = cache.FileCreatedUtcTicks > 0 ? cache.FileCreatedUtcTicks : file.CreationTimeUtc.Ticks,
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
                HasGoals = cache.HasGoals,
                GoalCount = cache.GoalCount,
                MarkerCount = cache.MarkerCount,
                TimelineEvents = cache.TimelineEvents ?? new List<ReplayTimelineEntrySummary>(),
                GameSegments = cache.GameSegments ?? new List<ReplayGameSegmentSummary>(),
                IsFavorite = cache.IsFavorite,
                IsImported = cache.IsImported,
                ImportedUtcTicks = cache.ImportedUtcTicks,
                IsMetadataComplete = true,
                SummaryCacheVersion = cache.CacheVersion,
                SummaryGeneratedUtcTicks = cache.SummaryGeneratedUtcTicks,
                SummaryGeneratedByModVersion = cache.SummaryGeneratedByModVersion
            };
        }

        private bool HasCurrentSummaryCache(FileInfo file, string summaryDirectory)
        {
            string summaryPath = GetSummaryPath(file.FullName, summaryDirectory);
            if (!File.Exists(summaryPath))
            {
                return false;
            }

            try
            {
                ReplaySummaryCache cache = JsonConvert.DeserializeObject<ReplaySummaryCache>(File.ReadAllText(summaryPath));
                return cache != null && cache.CacheVersion >= ReplayModConstants.ReplaySummaryCacheVersion;
            }
            catch
            {
                return false;
            }
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

            if (header.FormatVersion > ReplayModConstants.ReplayDtoFormatVersion)
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
                case "StickSpawned":
                case "StickDespawned":
                    return payloadToken.ToObject<StickSnapshotPayload>();
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
                case "GoalScored":
                    return payloadToken.ToObject<GoalScoredPayload>();
                default:
                    return payloadToken;
            }
        }

        private List<ReplayTimelineEntrySummary> GetTimelineEvents(string filePath, bool isBinaryReplay, int totalTicks, ReplayFileSummary cachedSummary, out int goalCount, out int markerCount, out List<ReplayGameSegmentSummary> gameSegments)
        {
            goalCount = cachedSummary != null ? cachedSummary.GoalCount : 0;
            markerCount = cachedSummary != null ? cachedSummary.MarkerCount : 0;
            gameSegments = cachedSummary != null && cachedSummary.GameSegments != null ? cachedSummary.GameSegments : new List<ReplayGameSegmentSummary>();
            if (cachedSummary != null &&
                cachedSummary.SummaryCacheVersion >= ReplayModConstants.ReplaySummaryCacheVersion &&
                cachedSummary.TimelineEvents != null)
            {
                return cachedSummary.TimelineEvents;
            }

            try
            {
                if (isBinaryReplay)
                {
                    // Stream the events past the timeline accumulator so building the summary at save
                    // time never holds the whole match in memory — the finalize that follows a chunked
                    // recording would otherwise reload every TransformFrame and spike server RAM.
                    ReplayTimelineIndexBuilder.Accumulator accumulator = new ReplayTimelineIndexBuilder.Accumulator();
                    ReplayBinarySerializer.StreamEvents(filePath, accumulator.Add);
                    return accumulator.Complete(totalTicks, out goalCount, out markerCount, out gameSegments);
                }

                ReplaySessionData session = this.Load(filePath);
                return ReplayTimelineIndexBuilder.Build(session, out goalCount, out markerCount, out gameSegments);
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to build replay timeline index for " + filePath + ": " + exception.Message);
            }

            return cachedSummary != null && cachedSummary.TimelineEvents != null
                ? cachedSummary.TimelineEvents
                : new List<ReplayTimelineEntrySummary>();
        }
    }
}
