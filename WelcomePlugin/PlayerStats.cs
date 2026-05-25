using System.Collections.Generic;

namespace WelcomePlugin
{
    /// <summary>
    /// Persistent per-player statistics. Keyed externally by Steam UserId.
    /// </summary>
    public sealed class PlayerStats
    {
        public string UserId { get; set; } = string.Empty;
        public string LastNickname { get; set; } = string.Empty;

        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Suicides { get; set; }
        public int FriendlyFireKills { get; set; }
        public int Escapes { get; set; }
        public int RoundsPlayed { get; set; }

        public long FirstSeenUnix { get; set; }
        public long LastSeenUnix { get; set; }
        public long TotalPlaytimeSeconds { get; set; }

        public Dictionary<string, int> KillsByVictimRole { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> DeathsByDamageType { get; set; } = new Dictionary<string, int>();

        public double KDRatio
        {
            get { return Deaths == 0 ? Kills : (double)Kills / Deaths; }
        }
    }
}
