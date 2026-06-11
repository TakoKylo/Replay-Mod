using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace PuckReplayMod
{
    public sealed class ReplayChunkedRecordingWriter : IDisposable
    {
        private const int MaxEventsPerChunk = 300;
        private const int MaxChunkPayloadBytes = 1024 * 1024;
        // Flush-to-disk (fsync) stalls the game thread for several milliseconds, so it runs on a
        // time budget instead of every chunk. Sealed chunks are still pushed to the OS cache per
        // chunk and survive a process crash; only a full power loss can lose this window.
        private const int DurableFlushIntervalSeconds = 30;

        private readonly string tempPath;
        private readonly FileStream tempStream;
        private readonly BinaryWriter tempWriter;
        private readonly string recoveryPath;
        private readonly ReplayRecoveryManifest recoveryManifest;
        private readonly MemoryStream chunkPayload;
        private readonly BinaryWriter chunkWriter;
        private readonly List<ReplayChunkRecord> chunks = new List<ReplayChunkRecord>();
        private readonly List<ReplayKeyframeDto> keyframes = new List<ReplayKeyframeDto>();
        private bool disposed;
        private int currentChunkFirstTick;
        private int currentChunkLastTick;
        private int currentChunkEventCount;
        private long lastDurableFlushTimestamp;

        public ReplayChunkedRecordingWriter(string tempPath, ReplayHeaderDto header)
        {
            if (string.IsNullOrEmpty(tempPath))
            {
                throw new ArgumentException("Temp path is empty.", "tempPath");
            }

            this.tempPath = tempPath;
            this.recoveryPath = tempPath + ".recovery.json";
            this.recoveryManifest = new ReplayRecoveryManifest
            {
                TempFileName = Path.GetFileName(tempPath),
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                Header = header ?? new ReplayHeaderDto(),
                ContainerVersion = ReplayModConstants.ReplayBinaryFormatVersion
            };
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath));
            this.tempStream = File.Create(tempPath);
            this.tempWriter = new BinaryWriter(this.tempStream, Encoding.UTF8);
            this.chunkPayload = new MemoryStream(256 * 1024);
            this.chunkWriter = new BinaryWriter(this.chunkPayload, Encoding.UTF8);
            this.currentChunkFirstTick = -1;
            this.currentChunkLastTick = -1;
            this.lastDurableFlushTimestamp = Stopwatch.GetTimestamp();
            this.WriteRecoveryManifest();
        }

        public string TempPath
        {
            get { return this.tempPath; }
        }

        public int ChunkCount
        {
            get { return this.chunks.Count + (this.currentChunkEventCount > 0 ? 1 : 0); }
        }

        public void AppendEvent(ReplayEventDto replayEvent)
        {
            this.ThrowIfDisposed();
            replayEvent = replayEvent ?? new ReplayEventDto();
            if (this.currentChunkEventCount == 0)
            {
                this.currentChunkFirstTick = replayEvent.Tick;
            }

            this.currentChunkLastTick = replayEvent.Tick;
            ReplayBinarySerializer.WriteEventTo(this.chunkWriter, replayEvent);
            this.currentChunkEventCount++;
            if (this.currentChunkEventCount >= MaxEventsPerChunk || this.chunkPayload.Length >= MaxChunkPayloadBytes)
            {
                this.FlushChunk();
            }
        }

        public void AddKeyframe(int tick, InitialSnapshotPayload snapshot)
        {
            this.ThrowIfDisposed();
            if (snapshot == null)
            {
                return;
            }

            this.keyframes.Add(new ReplayKeyframeDto
            {
                Tick = tick,
                Snapshot = snapshot
            });
        }

        public void Complete(string finalPath, ReplayHeaderDto header)
        {
            this.ThrowIfDisposed();
            if (string.IsNullOrEmpty(finalPath))
            {
                throw new ArgumentException("Final path is empty.", "finalPath");
            }

            this.FlushChunk();
            this.tempWriter.Flush();
            this.tempStream.Flush();

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
            List<ReplayChunkRecord> finalIndex = new List<ReplayChunkRecord>(this.chunks.Count);
            using (FileStream finalStream = File.Create(finalPath))
            using (BinaryWriter finalWriter = new BinaryWriter(finalStream, Encoding.UTF8))
            {
                ReplayBinarySerializer.WritePrelude(finalWriter, ReplayModConstants.ReplayBinaryFormatVersion);
                ReplayBinarySerializer.WriteHeaderTo(finalWriter, header);

                byte[] buffer = new byte[128 * 1024];
                for (int i = 0; i < this.chunks.Count; i++)
                {
                    ReplayChunkRecord chunk = this.chunks[i];
                    long finalOffset = finalStream.Position;
                    this.tempStream.Seek(chunk.TempOffset, SeekOrigin.Begin);
                    CopyBytes(this.tempStream, finalStream, chunk.Length, buffer);
                    chunk.FinalOffset = finalOffset;
                    finalIndex.Add(chunk);
                }

                long indexOffset = finalStream.Position;
                if (this.keyframes.Count > 0)
                {
                    ReplayBinarySerializer.WriteFourCharacterCode(finalWriter, ReplayBinarySerializer.KeyframeMagic);
                    finalWriter.Write(this.keyframes.Count);
                    for (int i = 0; i < this.keyframes.Count; i++)
                    {
                        ReplayKeyframeDto keyframe = this.keyframes[i];
                        finalWriter.Write(keyframe.Tick);
                        finalWriter.Write(keyframe.Snapshot != null);
                        if (keyframe.Snapshot != null)
                        {
                            ReplayBinarySerializer.WriteInitialSnapshotTo(finalWriter, keyframe.Snapshot);
                        }
                    }
                }

                ReplayBinarySerializer.WriteFourCharacterCode(finalWriter, ReplayBinarySerializer.IndexMagic);
                finalWriter.Write(finalIndex.Count);
                for (int i = 0; i < finalIndex.Count; i++)
                {
                    ReplayChunkRecord chunk = finalIndex[i];
                    finalWriter.Write(chunk.FirstTick);
                    finalWriter.Write(chunk.LastTick);
                    finalWriter.Write(chunk.EventCount);
                    finalWriter.Write(chunk.FinalOffset);
                    finalWriter.Write(chunk.Length);
                }

                ReplayBinarySerializer.WriteFourCharacterCode(finalWriter, ReplayBinarySerializer.FooterMagic);
                finalWriter.Write(indexOffset);
                finalWriter.Write(finalIndex.Count);
            }

            this.Dispose();
            TryDelete(this.tempPath);
            TryDelete(this.recoveryPath);
        }

        public void Abort()
        {
            this.Dispose();
            TryDelete(this.tempPath);
            TryDelete(this.recoveryPath);
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.chunkWriter.Dispose();
            this.chunkPayload.Dispose();
            this.tempWriter.Dispose();
            this.tempStream.Dispose();
        }

        private void FlushChunk()
        {
            if (this.currentChunkEventCount <= 0)
            {
                return;
            }

            this.chunkWriter.Flush();
            long tempOffset = this.tempStream.Position;
            ReplayBinarySerializer.WriteFourCharacterCode(this.tempWriter, ReplayBinarySerializer.ChunkMagic);
            this.tempWriter.Write(this.currentChunkFirstTick);
            this.tempWriter.Write(this.currentChunkLastTick);
            this.tempWriter.Write(this.currentChunkEventCount);
            this.tempWriter.Write((int)this.chunkPayload.Length);
            // WriteTo streams the buffered chunk directly into the temp file; ToArray here would
            // allocate a large-object-heap copy per chunk that Unity's non-compacting GC retains
            // until a full collection, ballooning the heap on long server sessions.
            this.chunkPayload.WriteTo(this.tempStream);
            this.tempWriter.Flush();

            this.chunks.Add(new ReplayChunkRecord
            {
                FirstTick = this.currentChunkFirstTick,
                LastTick = this.currentChunkLastTick,
                EventCount = this.currentChunkEventCount,
                TempOffset = tempOffset,
                Length = (int)(this.tempStream.Position - tempOffset)
            });

            this.chunkPayload.SetLength(0L);
            this.currentChunkFirstTick = -1;
            this.currentChunkLastTick = -1;
            this.currentChunkEventCount = 0;

            long now = Stopwatch.GetTimestamp();
            if (now - this.lastDurableFlushTimestamp >= Stopwatch.Frequency * DurableFlushIntervalSeconds)
            {
                this.lastDurableFlushTimestamp = now;
                try
                {
                    this.tempStream.Flush(true);
                }
                catch
                {
                    this.tempStream.Flush();
                }

                this.UpdateRecoveryManifest();
            }
        }

        private void UpdateRecoveryManifest()
        {
            this.recoveryManifest.LastCompletedChunkUtcTicks = DateTime.UtcNow.Ticks;
            this.recoveryManifest.CompletedChunkCount = this.chunks.Count;
            this.recoveryManifest.CompletedEventCount = 0;
            this.recoveryManifest.LastTick = 0;
            for (int i = 0; i < this.chunks.Count; i++)
            {
                this.recoveryManifest.CompletedEventCount += this.chunks[i].EventCount;
                this.recoveryManifest.LastTick = Math.Max(this.recoveryManifest.LastTick, this.chunks[i].LastTick);
            }

            this.WriteRecoveryManifest();
        }

        private void WriteRecoveryManifest()
        {
            try
            {
                File.WriteAllText(this.recoveryPath, JsonConvert.SerializeObject(this.recoveryManifest, Formatting.None));
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to write replay recovery manifest: " + exception.Message);
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException("ReplayChunkedRecordingWriter");
            }
        }

        private static void CopyBytes(Stream source, Stream destination, int byteCount, byte[] buffer)
        {
            int remaining = byteCount;
            while (remaining > 0)
            {
                int read = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of replay chunk temp file.");
                }

                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private struct ReplayChunkRecord
        {
            public int FirstTick;
            public int LastTick;
            public int EventCount;
            public long TempOffset;
            public long FinalOffset;
            public int Length;
        }
    }
}
