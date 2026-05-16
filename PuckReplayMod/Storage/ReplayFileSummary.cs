using System;

namespace PuckReplayMod
{
    public class ReplayFileSummary
    {
        public string FilePath;
        public string FileName;
        public long SizeBytes;
        public DateTime LastWriteUtc;
        public string ServerName;
        public string DisplayName;
        public string RecordedBy;
        public string ReplayMagic;
        public int ReplayFormatVersion;
        public string ReplayContainerFormat;
        public int ReplayContainerVersion;
        public string ModVersion;
        public string GameVersion;
        public long StartedUtcTicks;
        public long EndedUtcTicks;
        public int TickRate;
        public int TotalTicks;
        public int EventCount;
        public bool HasScoreboard;
        public bool HasChat;
        public bool HasMarkers;
        public bool IsFavorite;
        public bool IsMetadataComplete;

        public float DurationSeconds
        {
            get
            {
                if (this.TickRate <= 0)
                {
                    return 0f;
                }

                return this.TotalTicks / (float)this.TickRate;
            }
        }
    }

    public class ReplaySummaryCache
    {
        public int CacheVersion = 2;
        public string FileName;
        public long SizeBytes;
        public long LastWriteUtcTicks;
        public string ServerName;
        public string DisplayName;
        public string RecordedBy;
        public string ReplayMagic;
        public int ReplayFormatVersion;
        public string ReplayContainerFormat;
        public int ReplayContainerVersion;
        public string ModVersion;
        public string GameVersion;
        public long StartedUtcTicks;
        public long EndedUtcTicks;
        public int TickRate;
        public int TotalTicks;
        public int EventCount;
        public bool HasScoreboard;
        public bool HasChat;
        public bool HasMarkers;
        public bool IsFavorite;
    }
}
