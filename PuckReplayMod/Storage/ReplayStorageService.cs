using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace PuckReplayMod
{
    public class ReplayStorageService
    {
        public string RootDirectory { get; private set; }

        public string ReplaysDirectory { get; private set; }

        public string SummariesDirectory { get; private set; }

        public string TempDirectory { get; private set; }

        private readonly object saveLock = new object();

        public void Initialize()
        {
            this.RootDirectory = Path.Combine(Application.persistentDataPath, "PuckReplayMod");
            this.ReplaysDirectory = Path.Combine(this.RootDirectory, "Replays");
            this.SummariesDirectory = Path.Combine(this.RootDirectory, "Summaries");
            this.TempDirectory = Path.Combine(this.RootDirectory, "Temp");

            Directory.CreateDirectory(this.RootDirectory);
            Directory.CreateDirectory(this.ReplaysDirectory);
            Directory.CreateDirectory(this.SummariesDirectory);
            Directory.CreateDirectory(this.TempDirectory);
        }

        public string SaveReplay(ReplaySessionData session)
        {
            ReplaySaveResult result = this.SaveReplayCore(session, 0, 0);
            if (!result.Success)
            {
                throw new IOException(result.ErrorMessage);
            }

            ReplayModLog.Info("Saved replay: " + result.FilePath + " (" + result.SizeBytes + " bytes).");
            return result.FilePath;
        }

        public Task<ReplaySaveResult> SaveReplayAsync(ReplaySessionData session, int minimumLengthSeconds, int storageLimitMb)
        {
            return Task.Run(delegate
            {
                lock (this.saveLock)
                {
                    return this.SaveReplayCore(session, minimumLengthSeconds, storageLimitMb);
                }
            });
        }

        private ReplaySaveResult SaveReplayCore(ReplaySessionData session, int minimumLengthSeconds, int storageLimitMb)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string finalPath = Path.Combine(this.ReplaysDirectory, "replay_" + stamp + ReplayModConstants.ReplayFileExtension);
            string tempPath = Path.Combine(this.TempDirectory, Path.GetFileName(finalPath) + ".tmp");

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            try
            {
                ReplayBinarySerializer.Save(tempPath, session);
                File.Move(tempPath, finalPath);
                this.WriteReplaySummary(finalPath, session);

                if (minimumLengthSeconds > 0)
                {
                    this.CleanupShortReplays(minimumLengthSeconds);
                }

                if (storageLimitMb > 0)
                {
                    this.CleanupOldReplays(storageLimitMb);
                }

                FileInfo file = new FileInfo(finalPath);
                stopwatch.Stop();
                return new ReplaySaveResult
                {
                    Success = true,
                    FilePath = finalPath,
                    SizeBytes = file.Length,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception exception)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }

                stopwatch.Stop();
                return new ReplaySaveResult
                {
                    Success = false,
                    FilePath = finalPath,
                    ErrorMessage = exception.Message,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
        }

        public void CleanupOldReplays(int storageLimitMb)
        {
            if (storageLimitMb <= 0 || !Directory.Exists(this.ReplaysDirectory))
            {
                return;
            }

            FileInfo[] files = new DirectoryInfo(this.ReplaysDirectory)
                .GetFiles("*" + ReplayModConstants.ReplayFileExtension)
                .OrderBy(file => file.CreationTimeUtc)
                .ToArray();

            long limitBytes = (long)storageLimitMb * 1024L * 1024L;
            long totalBytes = files.Sum(file => file.Length);
            foreach (FileInfo file in files)
            {
                if (totalBytes <= limitBytes)
                {
                    return;
                }

                try
                {
                    long length = file.Length;
                    this.DeleteReplayFile(file);
                    totalBytes -= length;
                    ReplayModLog.Info("Deleted old replay to enforce storage limit: " + file.FullName);
                }
                catch (Exception e)
                {
                    ReplayModLog.Warning("Failed to delete old replay " + file.FullName + ": " + e.Message);
                }
            }
        }

        public int CleanupShortReplays(int minimumLengthSeconds)
        {
            if (minimumLengthSeconds <= 0 || !Directory.Exists(this.ReplaysDirectory))
            {
                return 0;
            }

            int deletedCount = 0;
            FileInfo[] files = new DirectoryInfo(this.ReplaysDirectory)
                .GetFiles("*" + ReplayModConstants.ReplayFileExtension)
                .ToArray();

            foreach (FileInfo file in files)
            {
                try
                {
                    if (!this.IsReplayShorterThan(file.FullName, minimumLengthSeconds))
                    {
                        continue;
                    }

                    this.DeleteReplayFile(file);
                    deletedCount++;
                    ReplayModLog.Info("Deleted short replay: " + file.FullName);
                }
                catch (Exception e)
                {
                    ReplayModLog.Warning("Failed to check short replay cleanup for " + file.FullName + ": " + e.Message);
                }
            }

            return deletedCount;
        }

        public void DeleteReplay(string filePath)
        {
            FileInfo file = this.GetValidatedReplayFile(filePath, false);
            if (!file.Exists)
            {
                this.DeleteReplaySummary(filePath);
                return;
            }

            this.DeleteReplayFile(file);
            ReplayModLog.Info("Deleted replay: " + file.FullName);
        }

        public void SetReplayDisplayName(string filePath, string displayName)
        {
            FileInfo file = this.GetValidatedReplayFile(filePath, true);
            ReplaySummaryCache cache = this.ReadOrCreateSummaryCache(file);
            cache.DisplayName = (displayName ?? string.Empty).Trim();
            this.WriteSummaryCache(file.FullName, cache);
            ReplayModLog.Info("Updated replay name: " + filePath);
        }

        public void SetReplayFavorite(string filePath, bool isFavorite)
        {
            FileInfo file = this.GetValidatedReplayFile(filePath, true);
            ReplaySummaryCache cache = this.ReadOrCreateSummaryCache(file);
            cache.IsFavorite = isFavorite;
            this.WriteSummaryCache(file.FullName, cache);
            ReplayModLog.Info((isFavorite ? "Favorited replay: " : "Unfavorited replay: ") + filePath);
        }

        private bool IsReplayShorterThan(string filePath, int minimumLengthSeconds)
        {
            ReplayFileSummary summary = new ReplayFileReader().ReadSummary(filePath, this.SummariesDirectory);
            if (summary == null || summary.TickRate <= 0 || summary.TotalTicks <= 0)
            {
                return false;
            }

            float durationSeconds = summary.TotalTicks / (float)summary.TickRate;
            return durationSeconds < minimumLengthSeconds;
        }

        private void WriteReplaySummary(string replayPath, ReplaySessionData session)
        {
            try
            {
                FileInfo file = new FileInfo(replayPath);
                ReplayHeaderDto header = session != null ? session.Header : null;
                ReplaySummaryCache cache = new ReplaySummaryCache
                {
                    FileName = file.Name,
                    SizeBytes = file.Length,
                    LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                    ServerName = header != null ? header.ServerName : string.Empty,
                    DisplayName = string.Empty,
                    RecordedBy = header != null ? header.RecordedBy : string.Empty,
                    ReplayMagic = header != null ? header.Magic : ReplayModConstants.ReplayMagic,
                    ReplayFormatVersion = header != null ? header.FormatVersion : 2,
                    ReplayContainerFormat = ReplayModConstants.ReplayBinaryMagic,
                    ReplayContainerVersion = ReplayModConstants.ReplayBinaryFormatVersion,
                    ModVersion = header != null ? header.ModVersion : ReplayModConstants.ModVersion,
                    GameVersion = header != null ? header.GameVersion : string.Empty,
                    StartedUtcTicks = header != null ? header.StartedUtcTicks : 0L,
                    EndedUtcTicks = header != null ? header.EndedUtcTicks : 0L,
                    TickRate = header != null ? header.TickRate : 0,
                    TotalTicks = header != null ? header.TotalTicks : 0,
                    EventCount = header != null ? header.EventCount : 0,
                    HasScoreboard = header != null && header.HasScoreboard,
                    HasChat = header != null && header.HasChat,
                    HasMarkers = header != null && header.HasMarkers,
                    IsFavorite = false
                };

                this.WriteSummaryCache(replayPath, cache);
            }
            catch (Exception e)
            {
                ReplayModLog.Warning("Failed to write replay summary cache for " + replayPath + ": " + e.Message);
            }
        }

        private void DeleteReplayFile(FileInfo file)
        {
            file.Delete();
            this.DeleteReplaySummary(file.FullName);
        }

        private void DeleteReplaySummary(string replayPath)
        {
            string summaryPath = ReplayFileReader.GetSummaryPath(replayPath, this.SummariesDirectory);
            if (File.Exists(summaryPath))
            {
                File.Delete(summaryPath);
            }
        }

        private ReplaySummaryCache ReadOrCreateSummaryCache(FileInfo file)
        {
            string summaryPath = ReplayFileReader.GetSummaryPath(file.FullName, this.SummariesDirectory);
            if (File.Exists(summaryPath))
            {
                ReplaySummaryCache cache = JsonConvert.DeserializeObject<ReplaySummaryCache>(File.ReadAllText(summaryPath));
                if (cache != null)
                {
                    return cache;
                }
            }

            return new ReplaySummaryCache
            {
                FileName = file.Name,
                SizeBytes = file.Length,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                ServerName = Path.GetFileNameWithoutExtension(file.Name),
                DisplayName = string.Empty,
                RecordedBy = string.Empty,
                ReplayMagic = ReplayModConstants.ReplayMagic,
                ReplayFormatVersion = 2,
                ReplayContainerFormat = string.Empty,
                ReplayContainerVersion = 0,
                ModVersion = ReplayModConstants.ModVersion,
                GameVersion = string.Empty,
                StartedUtcTicks = 0L,
                EndedUtcTicks = 0L,
                TickRate = 0,
                TotalTicks = 0,
                EventCount = 0,
                HasScoreboard = false,
                HasChat = false,
                HasMarkers = false,
                IsFavorite = false
            };
        }

        private void WriteSummaryCache(string replayPath, ReplaySummaryCache cache)
        {
            string summaryPath = ReplayFileReader.GetSummaryPath(replayPath, this.SummariesDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(summaryPath));
            File.WriteAllText(summaryPath, JsonConvert.SerializeObject(cache, Formatting.None));
        }

        private FileInfo GetValidatedReplayFile(string filePath, bool requireExists)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("Replay path is empty.", "filePath");
            }

            FileInfo file = new FileInfo(filePath);
            if (!string.Equals(file.Extension, ReplayModConstants.ReplayFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to manage a file that is not a replay: " + filePath);
            }

            string replayDirectory = Path.GetFullPath(this.ReplaysDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string replayPath = Path.GetFullPath(file.FullName);
            if (!replayPath.StartsWith(replayDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to manage a replay outside the replay directory: " + filePath);
            }

            if (requireExists && !file.Exists)
            {
                throw new FileNotFoundException("Replay file not found.", filePath);
            }

            return file;
        }
    }

    public class ReplaySaveResult
    {
        public bool Success;
        public string FilePath;
        public long SizeBytes;
        public long ElapsedMilliseconds;
        public string ErrorMessage;
    }
}
