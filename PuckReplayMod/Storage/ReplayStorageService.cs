using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

        public string ImportsDirectory { get; private set; }

        public string ExportsDirectory { get; private set; }

        // On a dedicated server the replays and the oomtm stats files live side by side in the
        // Puck server directory. The stats mod copies its timestamp into a "stats" folder; we
        // adopt it so each replay pairs with the matching stats JSON.
        private const string ServerReplaysFolderName = "ServerReplays";
        private const string ServerStatsFolderName = "stats";
        private const int ServerStatsMatchWindowSeconds = 10;
        private const string ServerTimestampFallbackFormat = "dd-MM-yyyy_HH-mm-ss";

        private readonly object saveLock = new object();

        private bool isDedicatedServer;
        private string gameInstallDirectory;

        public void Initialize(bool isDedicatedServer)
        {
            this.isDedicatedServer = isDedicatedServer;
            // Resolve the install directory on the main thread; Application.* cannot be read from
            // the background save thread where the stats handshake later runs.
            this.gameInstallDirectory = GetGameInstallDirectory();

            if (isDedicatedServer && this.TryInitializeServerStorage())
            {
                // Server storage was rooted in the Puck server directory.
            }
            else
            {
                this.RootDirectory = Path.Combine(Application.persistentDataPath, "PuckReplayMod");
                this.ReplaysDirectory = Path.Combine(this.RootDirectory, "Replays");
                this.SummariesDirectory = Path.Combine(this.RootDirectory, "Summaries");
                this.TempDirectory = Path.Combine(this.RootDirectory, "Temp");
                this.ImportsDirectory = Path.Combine(this.RootDirectory, "Imports");
                this.ExportsDirectory = Path.Combine(this.RootDirectory, "Exports");
            }

            Directory.CreateDirectory(this.RootDirectory);
            Directory.CreateDirectory(this.ReplaysDirectory);
            Directory.CreateDirectory(this.SummariesDirectory);
            Directory.CreateDirectory(this.TempDirectory);
            Directory.CreateDirectory(this.ImportsDirectory);
            Directory.CreateDirectory(this.ExportsDirectory);
        }

        private bool TryInitializeServerStorage()
        {
            try
            {
                string serverDirectory = this.ResolveServerDirectory();
                if (string.IsNullOrEmpty(serverDirectory))
                {
                    return false;
                }

                string replaysDirectory = Path.Combine(serverDirectory, ServerReplaysFolderName);
                if (!TryEnsureWritableDirectory(replaysDirectory))
                {
                    ReplayModLog.Warning("Server replay directory is not writable: " + replaysDirectory + ". Falling back to the default data folder.");
                    return false;
                }

                this.RootDirectory = replaysDirectory;
                this.ReplaysDirectory = replaysDirectory;
                this.SummariesDirectory = Path.Combine(replaysDirectory, "Summaries");
                this.TempDirectory = Path.Combine(replaysDirectory, "Temp");
                this.ImportsDirectory = Path.Combine(replaysDirectory, "Imports");
                this.ExportsDirectory = Path.Combine(replaysDirectory, "Exports");
                ReplayModLog.Info("Server-side replays will be saved to: " + replaysDirectory);
                return true;
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to root replays in the server directory; using the default data folder instead: " + exception.Message);
                return false;
            }
        }

        // The oomtm stats mod (and the uploader) work relative to the server's working directory,
        // which is usually the install directory but not always. Prefer whichever candidate already
        // holds the "stats" folder so replays sit beside it; otherwise use the first writable one.
        private string ResolveServerDirectory()
        {
            List<string> candidates = this.GetServerRootCandidates();
            foreach (string root in candidates)
            {
                if (Directory.Exists(Path.Combine(root, ServerStatsFolderName)))
                {
                    return root;
                }
            }

            foreach (string root in candidates)
            {
                if (TryEnsureWritableDirectory(root))
                {
                    return root;
                }
            }

            return candidates.Count > 0 ? candidates[0] : null;
        }

        private static string GetGameInstallDirectory()
        {
            try
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                if (parent != null)
                {
                    return parent.FullName;
                }
            }
            catch
            {
            }

            return Application.dataPath;
        }

        private static bool TryEnsureWritableDirectory(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probePath = Path.Combine(directory, ".write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probePath, string.Empty);
                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
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

        public ReplayChunkedRecordingWriter CreateChunkedRecordingWriter()
        {
            return this.CreateChunkedRecordingWriter(null);
        }

        public ReplayChunkedRecordingWriter CreateChunkedRecordingWriter(ReplayHeaderDto header)
        {
            string tempName = "recording_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N") + ".chunks.tmp";
            return new ReplayChunkedRecordingWriter(Path.Combine(this.TempDirectory, tempName), header);
        }

        public Task<ReplaySaveResult> FinalizeChunkedReplayAsync(ReplayChunkedRecordingWriter writer, ReplayHeaderDto header, int minimumLengthSeconds, int storageLimitMb)
        {
            return Task.Run(delegate
            {
                lock (this.saveLock)
                {
                    return this.FinalizeChunkedReplayCore(writer, header, minimumLengthSeconds, storageLimitMb);
                }
            });
        }

        private ReplaySaveResult SaveReplayCore(ReplaySessionData session, int minimumLengthSeconds, int storageLimitMb)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string finalPath = this.GetUniqueFilePath(this.ReplaysDirectory, this.BuildReplayFileName(session != null ? session.Header : null));
            string tempPath = Path.Combine(this.TempDirectory, Path.GetFileName(finalPath) + ".tmp");

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            try
            {
                ReplayBinarySerializer.Save(tempPath, session);
                File.Move(tempPath, finalPath);
                new ReplayFileReader().ReadSummary(finalPath, this.SummariesDirectory);

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

        private ReplaySaveResult FinalizeChunkedReplayCore(ReplayChunkedRecordingWriter writer, ReplayHeaderDto header, int minimumLengthSeconds, int storageLimitMb)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string finalPath = this.GetUniqueFilePath(this.ReplaysDirectory, this.BuildReplayFileName(header));
            string assemblingPath = Path.Combine(this.TempDirectory, Path.GetFileName(finalPath) + ".assembling.tmp");

            long sizeBytes;
            try
            {
                if (writer == null)
                {
                    throw new InvalidOperationException("Replay chunk writer is missing.");
                }

                if (File.Exists(assemblingPath))
                {
                    File.Delete(assemblingPath);
                }

                // Assemble into the temp folder, then publish into the library with an atomic move.
                // A crash or hard server stop during finalize can never leave a half-written replay
                // in the library, and the recoverable chunk temp survives until the publish lands.
                writer.Complete(assemblingPath, header);
                File.Move(assemblingPath, finalPath);
                writer.DiscardWorkingFiles();
                sizeBytes = new FileInfo(finalPath).Length;
            }
            catch (Exception exception)
            {
                try
                {
                    if (writer != null)
                    {
                        writer.Abort();
                    }
                }
                catch
                {
                }

                try
                {
                    if (File.Exists(assemblingPath))
                    {
                        File.Delete(assemblingPath);
                    }
                }
                catch
                {
                }

                try
                {
                    if (File.Exists(finalPath))
                    {
                        File.Delete(finalPath);
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

            // The replay is durably published. Summary indexing and retention are best-effort and
            // must never delete or fail the just-saved match — the summary is rebuilt on demand and
            // a transient I/O hiccup on a busy server should not cost a recorded game.
            try
            {
                new ReplayFileReader().ReadSummary(finalPath, this.SummariesDirectory);

                if (minimumLengthSeconds > 0)
                {
                    this.CleanupShortReplays(minimumLengthSeconds);
                }

                if (storageLimitMb > 0)
                {
                    this.CleanupOldReplays(storageLimitMb);
                }
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Replay saved but post-save summary/retention failed for " + finalPath + ": " + exception.Message);
            }

            stopwatch.Stop();
            return new ReplaySaveResult
            {
                Success = true,
                FilePath = finalPath,
                SizeBytes = sizeBytes,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }

        public List<ReplayRecoveryCandidate> GetRecoverableRecordings()
        {
            List<ReplayRecoveryCandidate> candidates = new List<ReplayRecoveryCandidate>();
            if (string.IsNullOrEmpty(this.TempDirectory) || !Directory.Exists(this.TempDirectory))
            {
                return candidates;
            }

            FileInfo[] files = new DirectoryInfo(this.TempDirectory).GetFiles("*.chunks.tmp");
            foreach (FileInfo file in files)
            {
                ReplayRecoveryCandidate candidate;
                List<ReplayRecoveryChunk> chunks;
                ReplayRecoveryManifest manifest;
                if (this.TryReadRecoveryCandidate(file, out candidate, out chunks, out manifest))
                {
                    candidates.Add(candidate);
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.CreatedUtcTicks)
                .ToList();
        }

        public ReplayRecoveryResult RecoverUnfinishedRecordings()
        {
            ReplayRecoveryResult result = new ReplayRecoveryResult();
            if (string.IsNullOrEmpty(this.TempDirectory) || !Directory.Exists(this.TempDirectory))
            {
                return result;
            }

            // A leftover "*.assembling.tmp" is always a superseded finalize that crashed between
            // assembly and the atomic publish; the chunk temp below is the recoverable source.
            this.SweepStaleAssemblingFiles();

            FileInfo[] files = new DirectoryInfo(this.TempDirectory).GetFiles("*.chunks.tmp");
            result.FoundCount = files.Length;
            foreach (FileInfo file in files)
            {
                try
                {
                    string recoveredPath = this.RecoverUnfinishedRecording(file);
                    result.RecoveredCount++;
                    result.RecoveredFiles.Add(recoveredPath);
                }
                catch (Exception exception)
                {
                    result.FailedCount++;
                    result.Errors.Add(file.Name + ": " + exception.Message);
                    ReplayModLog.Warning("Failed to recover unfinished replay " + file.FullName + ": " + exception.Message);
                }
            }

            return result;
        }

        private void SweepStaleAssemblingFiles()
        {
            try
            {
                FileInfo[] stale = new DirectoryInfo(this.TempDirectory).GetFiles("*.assembling.tmp");
                foreach (FileInfo file in stale)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public ReplayRecoveryResult DiscardUnfinishedRecordings()
        {
            ReplayRecoveryResult result = new ReplayRecoveryResult();
            if (string.IsNullOrEmpty(this.TempDirectory) || !Directory.Exists(this.TempDirectory))
            {
                return result;
            }

            FileInfo[] files = new DirectoryInfo(this.TempDirectory).GetFiles("*.chunks.tmp");
            result.FoundCount = files.Length;
            foreach (FileInfo file in files)
            {
                try
                {
                    file.Delete();
                    string manifestPath = GetRecoveryManifestPath(file.FullName);
                    if (File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                    }

                    result.DiscardedCount++;
                }
                catch (Exception exception)
                {
                    result.FailedCount++;
                    result.Errors.Add(file.Name + ": " + exception.Message);
                }
            }

            return result;
        }

        private string RecoverUnfinishedRecording(FileInfo file)
        {
            ReplayRecoveryCandidate candidate;
            List<ReplayRecoveryChunk> chunks;
            ReplayRecoveryManifest manifest;
            if (!this.TryReadRecoveryCandidate(file, out candidate, out chunks, out manifest))
            {
                throw new InvalidDataException("No complete replay chunks were found.");
            }

            DateTime startTime = candidate.CreatedUtcTicks > 0L
                ? new DateTime(candidate.CreatedUtcTicks, DateTimeKind.Utc)
                : file.CreationTimeUtc;
            string finalPath = this.GetUniqueFilePath(this.ReplaysDirectory, "replay_" + startTime.ToString("yyyyMMdd_HHmmss") + "_recovered" + ReplayModConstants.ReplayFileExtension);
            ReplayHeaderDto header = this.BuildRecoveredHeader(candidate, manifest, chunks);

            Directory.CreateDirectory(this.ReplaysDirectory);
            using (FileStream source = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (FileStream finalStream = File.Create(finalPath))
            using (BinaryWriter finalWriter = new BinaryWriter(finalStream))
            {
                ReplayBinarySerializer.WritePrelude(finalWriter, ReplayModConstants.ReplayBinaryFormatVersion);
                ReplayBinarySerializer.WriteHeaderTo(finalWriter, header);

                byte[] buffer = new byte[128 * 1024];
                for (int i = 0; i < chunks.Count; i++)
                {
                    ReplayRecoveryChunk chunk = chunks[i];
                    chunk.FinalOffset = finalStream.Position;
                    source.Seek(chunk.TempOffset, SeekOrigin.Begin);
                    CopyBytes(source, finalStream, chunk.Length, buffer);
                }

                long indexOffset = finalStream.Position;
                ReplayBinarySerializer.WriteFourCharacterCode(finalWriter, ReplayBinarySerializer.IndexMagic);
                finalWriter.Write(chunks.Count);
                for (int i = 0; i < chunks.Count; i++)
                {
                    ReplayRecoveryChunk chunk = chunks[i];
                    finalWriter.Write(chunk.FirstTick);
                    finalWriter.Write(chunk.LastTick);
                    finalWriter.Write(chunk.EventCount);
                    finalWriter.Write(chunk.FinalOffset);
                    finalWriter.Write(chunk.Length);
                }

                ReplayBinarySerializer.WriteFourCharacterCode(finalWriter, ReplayBinarySerializer.FooterMagic);
                finalWriter.Write(indexOffset);
                finalWriter.Write(chunks.Count);
            }

            new ReplayFileReader().ReadSummary(finalPath, this.SummariesDirectory);
            file.Delete();
            string manifestPath = GetRecoveryManifestPath(file.FullName);
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            ReplayModLog.Info("Recovered unfinished replay: " + finalPath);
            return finalPath;
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

        public string ImportReplay(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                throw new ArgumentException("Replay path is empty.", "sourcePath");
            }

            sourcePath = sourcePath.Trim().Trim('"');
            FileInfo source = new FileInfo(sourcePath);
            if (!source.Exists)
            {
                throw new FileNotFoundException("Replay file not found.", sourcePath);
            }

            if (!string.Equals(source.Extension, ReplayModConstants.ReplayFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only " + ReplayModConstants.ReplayFileExtension + " files can be imported.");
            }

            string replayDirectory = Path.GetFullPath(this.ReplaysDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string sourceFullPath = Path.GetFullPath(source.FullName);
            if (sourceFullPath.StartsWith(replayDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("That replay is already in the Replay Mod library.");
            }

            Directory.CreateDirectory(this.ReplaysDirectory);
            Directory.CreateDirectory(this.TempDirectory);

            string destinationPath = this.GetUniqueFilePath(this.ReplaysDirectory, source.Name);
            string tempPath = this.GetUniqueFilePath(this.TempDirectory, Path.GetFileName(destinationPath));

            try
            {
                File.Copy(source.FullName, tempPath, true);
                new ReplayFileReader().ReadSummary(tempPath, this.TempDirectory);
                this.DeleteSummaryFor(tempPath, this.TempDirectory);
                File.Move(tempPath, destinationPath);
                ReplayFileSummary summary = new ReplayFileReader().ReadSummary(destinationPath, this.SummariesDirectory);
                summary.IsImported = true;
                summary.ImportedUtcTicks = DateTime.UtcNow.Ticks;
                new ReplayFileReader().WriteSummaryCache(summary, this.SummariesDirectory);
                ReplayModLog.Info("Imported replay: " + destinationPath);
                return destinationPath;
            }
            catch
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

                try
                {
                    this.DeleteSummaryFor(tempPath, this.TempDirectory);
                }
                catch
                {
                }

                throw;
            }
        }

        public ReplayImportBatchResult ImportReplaysFromImportsFolder()
        {
            Directory.CreateDirectory(this.ImportsDirectory);
            FileInfo[] files = new DirectoryInfo(this.ImportsDirectory)
                .GetFiles("*" + ReplayModConstants.ReplayFileExtension, SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name)
                .ToArray();

            ReplayImportBatchResult result = new ReplayImportBatchResult
            {
                FoundCount = files.Length
            };

            if (files.Length == 0)
            {
                return result;
            }

            string importedDirectory = Path.Combine(this.ImportsDirectory, "Imported");
            Directory.CreateDirectory(importedDirectory);

            foreach (FileInfo file in files)
            {
                try
                {
                    this.ImportReplay(file.FullName);
                    result.ImportedCount++;
                    string archivePath = this.GetUniqueFilePath(importedDirectory, file.Name);
                    File.Move(file.FullName, archivePath);
                }
                catch (Exception exception)
                {
                    result.FailedCount++;
                    result.Errors.Add(file.Name + ": " + exception.Message);
                    ReplayModLog.Warning("Failed to import replay from Imports folder " + file.FullName + ": " + exception.Message);
                }
            }

            return result;
        }

        public string ExportReplay(string filePath, string displayName)
        {
            FileInfo file = this.GetValidatedReplayFile(filePath, true);
            Directory.CreateDirectory(this.ExportsDirectory);

            string baseName = this.SanitizeFileName(displayName);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = Path.GetFileNameWithoutExtension(file.Name);
            }

            string destinationPath = this.GetUniqueFilePath(this.ExportsDirectory, baseName + ReplayModConstants.ReplayFileExtension);
            File.Copy(file.FullName, destinationPath, false);
            ReplayModLog.Info("Exported replay: " + destinationPath);
            return destinationPath;
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
                int goalCount;
                int markerCount;
                List<ReplayGameSegmentSummary> gameSegments;
                List<ReplayTimelineEntrySummary> timelineEvents = ReplayTimelineIndexBuilder.Build(session, out goalCount, out markerCount, out gameSegments);
                ReplaySummaryCache cache = new ReplaySummaryCache
                {
                    FileName = file.Name,
                    SizeBytes = file.Length,
                    FileCreatedUtcTicks = file.CreationTimeUtc.Ticks,
                    LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                    ServerName = header != null ? header.ServerName : string.Empty,
                    DisplayName = string.Empty,
                    RecordedBy = header != null ? header.RecordedBy : string.Empty,
                    ReplayMagic = header != null ? header.Magic : ReplayModConstants.ReplayMagic,
                    ReplayFormatVersion = header != null ? header.FormatVersion : ReplayModConstants.ReplayDtoFormatVersion,
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
                    HasGoals = goalCount > 0,
                    GoalCount = goalCount,
                    MarkerCount = markerCount,
                    TimelineEvents = timelineEvents,
                    GameSegments = gameSegments,
                    IsFavorite = false,
                    IsImported = false,
                    ImportedUtcTicks = 0L,
                    SummaryGeneratedUtcTicks = DateTime.UtcNow.Ticks,
                    SummaryGeneratedByModVersion = ReplayModConstants.ModVersion
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

        private void DeleteSummaryFor(string replayPath, string summaryDirectory)
        {
            string summaryPath = ReplayFileReader.GetSummaryPath(replayPath, summaryDirectory);
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
                FileCreatedUtcTicks = file.CreationTimeUtc.Ticks,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                ServerName = Path.GetFileNameWithoutExtension(file.Name),
                DisplayName = string.Empty,
                RecordedBy = string.Empty,
                ReplayMagic = ReplayModConstants.ReplayMagic,
                ReplayFormatVersion = 0,
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
                HasGoals = false,
                GoalCount = 0,
                MarkerCount = 0,
                TimelineEvents = new List<ReplayTimelineEntrySummary>(),
                GameSegments = new List<ReplayGameSegmentSummary>(),
                IsFavorite = false,
                IsImported = false,
                ImportedUtcTicks = 0L,
                SummaryGeneratedUtcTicks = DateTime.UtcNow.Ticks,
                SummaryGeneratedByModVersion = ReplayModConstants.ModVersion
            };
        }

        private bool TryReadRecoveryCandidate(FileInfo file, out ReplayRecoveryCandidate candidate, out List<ReplayRecoveryChunk> chunks, out ReplayRecoveryManifest manifest)
        {
            candidate = null;
            chunks = new List<ReplayRecoveryChunk>();
            manifest = this.TryReadRecoveryManifest(file != null ? file.FullName : null);
            if (file == null || !file.Exists || file.Length <= 0L)
            {
                return false;
            }

            RecoveryScanStats stats = new RecoveryScanStats();
            try
            {
                using (FileStream stream = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    while (stream.Position < stream.Length)
                    {
                        long chunkOffset = stream.Position;
                        if (stream.Length - chunkOffset < 20L)
                        {
                            break;
                        }

                        string magic = ReadFourCharacterCode(reader);
                        if (magic != ReplayBinarySerializer.ChunkMagic)
                        {
                            break;
                        }

                        int firstTick = reader.ReadInt32();
                        int lastTick = reader.ReadInt32();
                        int eventCount = reader.ReadInt32();
                        int payloadLength = reader.ReadInt32();
                        if (eventCount <= 0 || payloadLength < 0 || stream.Position + payloadLength > stream.Length)
                        {
                            break;
                        }

                        long payloadEnd = stream.Position + payloadLength;
                        bool chunkValid = true;
                        for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
                        {
                            try
                            {
                                this.UpdateRecoveryStats(stats, ReplayBinarySerializer.ReadEventFrom(reader, ReplayModConstants.ReplayBinaryFormatVersion));
                            }
                            catch
                            {
                                chunkValid = false;
                                break;
                            }
                        }

                        if (!chunkValid || stream.Position > payloadEnd)
                        {
                            break;
                        }

                        chunks.Add(new ReplayRecoveryChunk
                        {
                            FirstTick = firstTick,
                            LastTick = lastTick,
                            EventCount = eventCount,
                            TempOffset = chunkOffset,
                            Length = (int)(payloadEnd - chunkOffset)
                        });
                        stream.Position = payloadEnd;
                    }
                }
            }
            catch
            {
                return false;
            }

            if (chunks.Count == 0 || stats.EventCount <= 0)
            {
                return false;
            }

            int tickRate = manifest != null && manifest.Header != null && manifest.Header.TickRate > 0 ? manifest.Header.TickRate : 30;
            long createdTicks = manifest != null && manifest.Header != null && manifest.Header.StartedUtcTicks > 0L ? manifest.Header.StartedUtcTicks : file.CreationTimeUtc.Ticks;
            candidate = new ReplayRecoveryCandidate
            {
                TempPath = file.FullName,
                ManifestPath = GetRecoveryManifestPath(file.FullName),
                CreatedUtcTicks = createdTicks,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                ChunkCount = chunks.Count,
                EventCount = stats.EventCount,
                FirstTick = chunks[0].FirstTick,
                LastTick = Math.Max(stats.LastTick, chunks[chunks.Count - 1].LastTick),
                TickRate = tickRate,
                ServerName = manifest != null && manifest.Header != null ? manifest.Header.ServerName : string.Empty,
                RecordedBy = manifest != null && manifest.Header != null ? manifest.Header.RecordedBy : string.Empty,
                SizeBytes = file.Length
            };
            return true;
        }

        private ReplayHeaderDto BuildRecoveredHeader(ReplayRecoveryCandidate candidate, ReplayRecoveryManifest manifest, List<ReplayRecoveryChunk> chunks)
        {
            ReplayHeaderDto header = manifest != null && manifest.Header != null ? manifest.Header : new ReplayHeaderDto();
            header.Magic = ReplayModConstants.ReplayMagic;
            header.FormatVersion = ReplayModConstants.ReplayDtoFormatVersion;
            header.ModVersion = string.IsNullOrEmpty(header.ModVersion) ? ReplayModConstants.ModVersion : header.ModVersion;
            header.GameVersion = string.IsNullOrEmpty(header.GameVersion) ? ReplayModConstants.TargetGameVersion : header.GameVersion;
            header.StartedUtcTicks = candidate.CreatedUtcTicks > 0L ? candidate.CreatedUtcTicks : DateTime.UtcNow.Ticks;
            header.EndedUtcTicks = candidate.LastWriteUtcTicks > 0L ? candidate.LastWriteUtcTicks : DateTime.UtcNow.Ticks;
            header.TickRate = candidate.TickRate > 0 ? candidate.TickRate : 30;
            header.TotalTicks = Math.Max(0, candidate.LastTick);
            header.EventCount = Math.Max(0, candidate.EventCount);

            RecoveryScanStats stats = new RecoveryScanStats();
            try
            {
                using (FileStream stream = File.Open(candidate.TempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        ReplayRecoveryChunk chunk = chunks[i];
                        stream.Seek(chunk.TempOffset + 20L, SeekOrigin.Begin);
                        long payloadEnd = chunk.TempOffset + chunk.Length;
                        for (int eventIndex = 0; eventIndex < chunk.EventCount && stream.Position < payloadEnd; eventIndex++)
                        {
                            this.UpdateRecoveryStats(stats, ReplayBinarySerializer.ReadEventFrom(reader, ReplayModConstants.ReplayBinaryFormatVersion));
                        }
                    }
                }
            }
            catch
            {
            }

            header.HasScoreboard = stats.HasScoreboard;
            header.HasChat = stats.HasChat;
            header.HasMarkers = stats.HasMarkers;
            return header;
        }

        private void UpdateRecoveryStats(RecoveryScanStats stats, ReplayEventDto replayEvent)
        {
            if (stats == null || replayEvent == null)
            {
                return;
            }

            stats.EventCount++;
            stats.LastTick = Math.Max(stats.LastTick, replayEvent.Tick);
            if (replayEvent.Type == "ScoreboardSnapshot")
            {
                stats.HasScoreboard = true;
            }
            else if (replayEvent.Type == "ChatMessage")
            {
                stats.HasChat = true;
            }
            else if (replayEvent.Type == "Marker")
            {
                stats.HasMarkers = true;
            }
        }

        private ReplayRecoveryManifest TryReadRecoveryManifest(string tempPath)
        {
            if (string.IsNullOrEmpty(tempPath))
            {
                return null;
            }

            string manifestPath = GetRecoveryManifestPath(tempPath);
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<ReplayRecoveryManifest>(File.ReadAllText(manifestPath));
            }
            catch
            {
                return null;
            }
        }

        private static string GetRecoveryManifestPath(string tempPath)
        {
            return tempPath + ".recovery.json";
        }

        private static string ReadFourCharacterCode(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
            {
                throw new EndOfStreamException("Unexpected end of replay recovery chunk file.");
            }

            return Encoding.ASCII.GetString(bytes);
        }

        private static void CopyBytes(Stream source, Stream destination, int byteCount, byte[] buffer)
        {
            int remaining = byteCount;
            while (remaining > 0)
            {
                int read = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of replay recovery chunk file.");
                }

                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private sealed class RecoveryScanStats
        {
            public int EventCount;
            public int LastTick;
            public bool HasScoreboard;
            public bool HasChat;
            public bool HasMarkers;
        }

        private sealed class ReplayRecoveryChunk
        {
            public int FirstTick;
            public int LastTick;
            public int EventCount;
            public long TempOffset;
            public long FinalOffset;
            public int Length;
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

        private string BuildReplayFileName(ReplayHeaderDto header)
        {
            if (this.isDedicatedServer)
            {
                return this.BuildServerReplayFileName(header);
            }

            string stamp = header != null && header.StartedUtcTicks > 0L
                ? new DateTime(header.StartedUtcTicks, DateTimeKind.Utc).ToString("yyyyMMdd_HHmmss")
                : DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            return "replay_" + stamp + ReplayModConstants.ReplayFileExtension;
        }

        // Server replays are named "match[_<server-slug>]_<timestamp>" so the uploader can tell
        // which server a file came from and pair it with the matching oomtm stats JSON. The
        // timestamp is copied verbatim from the freshest stats file when one is present.
        private string BuildServerReplayFileName(ReplayHeaderDto header)
        {
            string slug = SanitizeServerSlug(header != null ? header.ServerName : null);
            string timestamp = this.TryAdoptStatsTimestamp();
            if (string.IsNullOrEmpty(timestamp))
            {
                timestamp = DateTime.UtcNow.ToString(ServerTimestampFallbackFormat, CultureInfo.InvariantCulture);
            }

            string baseName = "match"
                + (string.IsNullOrEmpty(slug) ? string.Empty : "_" + slug)
                + "_" + timestamp;
            return baseName + ReplayModConstants.ReplayFileExtension;
        }

        // Read the freshest "*_stats.json" / "oomtm450_stats_*.json" in the server's stats folder
        // and return its timestamp segment verbatim, or null if none is recent enough. Runs on the
        // background save thread, so it must not touch any Unity API.
        private string TryAdoptStatsTimestamp()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                foreach (string root in this.GetServerRootCandidates())
                {
                    string statsDirectory = Path.Combine(root, ServerStatsFolderName);
                    if (!Directory.Exists(statsDirectory))
                    {
                        continue;
                    }

                    List<FileInfo> candidates = Directory.GetFiles(statsDirectory, "*_stats.json")
                        .Concat(Directory.GetFiles(statsDirectory, "oomtm450_stats_*.json"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(path => new FileInfo(path))
                        .Where(file => file.Exists && (now - file.LastWriteTimeUtc).Duration().TotalSeconds <= ServerStatsMatchWindowSeconds)
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .ToList();

                    foreach (FileInfo file in candidates)
                    {
                        string timestamp = ExtractStatsTimestampSegment(file.Name);
                        if (!string.IsNullOrEmpty(timestamp))
                        {
                            return timestamp;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Replay stats timestamp handshake failed: " + exception.Message);
            }

            return null;
        }

        private List<string> GetServerRootCandidates()
        {
            List<string> roots = new List<string>(3);
            try
            {
                TryAddRoot(roots, Directory.GetCurrentDirectory());
            }
            catch
            {
            }

            TryAddRoot(roots, this.gameInstallDirectory);

            try
            {
                TryAddRoot(roots, AppDomain.CurrentDomain.BaseDirectory);
            }
            catch
            {
            }

            return roots;
        }

        private static void TryAddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                string full = Path.GetFullPath(path);
                if (!roots.Contains(full))
                {
                    roots.Add(full);
                }
            }
            catch
            {
            }
        }

        // "oomtm450_stats_23-04-2026_14-30-22.json" -> "23-04-2026_14-30-22"
        // "<header>_20260423_143022_stats.json"     -> "20260423_143022"
        private static string ExtractStatsTimestampSegment(string fileName)
        {
            const string legacyPrefix = "oomtm450_stats_";
            const string currentSuffix = "_stats.json";
            const string jsonExtension = ".json";

            if (fileName.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(jsonExtension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(legacyPrefix.Length, fileName.Length - legacyPrefix.Length - jsonExtension.Length);
            }

            if (fileName.EndsWith(currentSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string stem = fileName.Substring(0, fileName.Length - currentSuffix.Length);
                // <header>_<ts> — the timestamp is after the last underscore.
                int lastUnderscore = stem.LastIndexOf('_');
                if (lastUnderscore > 0 && lastUnderscore < stem.Length - 1)
                {
                    return stem.Substring(lastUnderscore + 1);
                }
            }

            return null;
        }

        private static string SanitizeServerSlug(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "puck";
            }

            // Strip HTML/color tags the way the stats mod does, then keep only filename-safe chars.
            name = Regex.Replace(name, "<[^>]+>", string.Empty);
            name = Regex.Replace(name, "[^A-Za-z0-9_-]", "-");
            name = Regex.Replace(name, "-+", "-");
            name = name.Trim('-').ToLowerInvariant();
            if (name.Length > 32)
            {
                name = name.Substring(0, 32).TrimEnd('-');
            }

            return string.IsNullOrEmpty(name) ? "puck" : name;
        }

        private string GetUniqueFilePath(string directory, string fileName)
        {
            string extension = Path.GetExtension(fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "replay";
            }

            string candidate = Path.Combine(directory, baseName + extension);
            int index = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, baseName + "_" + index + extension);
                index++;
            }

            return candidate;
        }

        private string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            string sanitized = new string(chars).Trim();
            return sanitized.Length > 64 ? sanitized.Substring(0, 64).Trim() : sanitized;
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

    public class ReplayImportBatchResult
    {
        public int FoundCount;
        public int ImportedCount;
        public int FailedCount;
        public List<string> Errors = new List<string>();
    }
}
