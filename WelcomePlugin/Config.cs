using System.ComponentModel;

namespace WelcomePlugin
{
    public sealed class Config
    {
        [Description("Master toggle for the plugin.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Enable verbose debug logging.")]
        public bool Debug { get; set; } = false;

        [Description("Welcome broadcast text. {nick} = player nickname.")]
        public string WelcomeMessage { get; set; } =
            "<b>Welcome, <color=#7FFFD4>{nick}</color>!</b>";

        [Description("Show personal stats summary in the join broadcast.")]
        public bool ShowStatsOnJoin { get; set; } = true;

        [Description("Announce other players' joins.")]
        public bool AnnounceJoin { get; set; } = true;

        [Description("Announce other players' leaves.")]
        public bool AnnounceLeave { get; set; } = true;

        [Description("Show top-3 killers broadcast at round end.")]
        public bool ShowRoundEndLeaderboard { get; set; } = true;

        [Description("Duration of personal broadcasts (seconds).")]
        public ushort BroadcastDuration { get; set; } = 8;

        [Description("Duration of join/leave announcements (seconds).")]
        public ushort AnnounceDuration { get; set; } = 5;

        [Description("Duration of the round-end leaderboard broadcast (seconds).")]
        public ushort LeaderboardDuration { get; set; } = 15;

        [Description("Pretty-print the saved stats JSON file.")]
        public bool PrettyPrintJson { get; set; } = true;

        [Description("Auto-save stats every N kills. 0 = save only at round-end / disconnect.")]
        public int AutoSaveEveryKills { get; set; } = 25;

        [Description("Track friendly-fire kills as a separate counter.")]
        public bool TrackFriendlyFire { get; set; } = true;
    }
}
