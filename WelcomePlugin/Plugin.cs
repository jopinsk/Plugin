using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;

namespace WelcomePlugin
{
    public sealed class Plugin : Plugin<Config>
    {
        public override string Name => "PlayerStatsTracker";
        public override string Description =>
            "Persistent per-player statistics with welcome broadcasts and a round-end leaderboard.";
        public override string Author => "jopinsk";
        public override Version Version { get; } = new Version(1, 0, 0);
        public override Version RequiredApiVersion { get; } = new Version(1, 1, 0);

        private StatsStorage _storage;
        private readonly Dictionary<string, long> _joinTimestamps = new Dictionary<string, long>();
        private int _killsSinceSave;

        public override void Enable()
        {
            if (!Config.IsEnabled)
            {
                Logger.Info("[Stats] Disabled in config.");
                return;
            }

            string dir = this.GetConfigDirectory().FullName;
            _storage = new StatsStorage(dir, Config.PrettyPrintJson);
            _storage.Load();

            PlayerEvents.Joined += OnJoined;
            PlayerEvents.Left += OnLeft;
            PlayerEvents.Death += OnDeath;
            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.RoundEnded += OnRoundEnded;

            Logger.Info($"[Stats] Enabled. {_storage.Count} player record(s) loaded from {_storage.FilePath}");
        }

        public override void Disable()
        {
            PlayerEvents.Joined -= OnJoined;
            PlayerEvents.Left -= OnLeft;
            PlayerEvents.Death -= OnDeath;
            ServerEvents.RoundStarted -= OnRoundStarted;
            ServerEvents.RoundEnded -= OnRoundEnded;

            if (_storage != null)
            {
                _storage.Save();
                Logger.Info("[Stats] Disabled; stats persisted.");
            }
        }

        private void OnJoined(PlayerJoinedEventArgs ev)
        {
            Player p = ev.Player;
            if (p == null || p.IsHost || string.IsNullOrEmpty(p.UserId)) return;

            PlayerStats s = _storage.GetOrCreate(p.UserId, p.Nickname);
            s.LastSeenUnix = Now();
            _joinTimestamps[p.UserId] = Now();

            if (Config.ShowStatsOnJoin)
            {
                string greet = Config.WelcomeMessage.Replace("{nick}", p.Nickname);
                string statsLine =
                    "<size=85%>K/D: <b>" + s.Kills + "/" + s.Deaths + "</b> (" + s.KDRatio.ToString("0.00") + ") " +
                    "| Escapes: <b>" + s.Escapes + "</b> " +
                    "| Rounds: <b>" + s.RoundsPlayed + "</b></size>";
                p.SendBroadcast(greet + "\n" + statsLine, Config.BroadcastDuration);
            }

            if (Config.AnnounceJoin)
            {
                string note = "<color=#7CFC00>+ " + p.Nickname + "</color>";
                foreach (Player o in Player.List)
                {
                    if (o == null || o == p || o.IsHost) continue;
                    o.SendBroadcast(note, Config.AnnounceDuration);
                }
            }

            if (Config.Debug) Logger.Info("[Stats] Joined: " + p.Nickname + " (" + p.UserId + ")");
        }

        private void OnLeft(PlayerLeftEventArgs ev)
        {
            Player p = ev.Player;
            if (p == null || p.IsHost || string.IsNullOrEmpty(p.UserId)) return;

            long joinedAt;
            if (_joinTimestamps.TryGetValue(p.UserId, out joinedAt))
            {
                PlayerStats s = _storage.GetOrCreate(p.UserId, p.Nickname);
                s.TotalPlaytimeSeconds += Math.Max(0, Now() - joinedAt);
                s.LastSeenUnix = Now();
                _joinTimestamps.Remove(p.UserId);
            }

            if (Config.AnnounceLeave)
            {
                string note = "<color=#FF6B6B>- " + p.Nickname + "</color>";
                foreach (Player o in Player.List)
                {
                    if (o == null || o == p || o.IsHost) continue;
                    o.SendBroadcast(note, Config.AnnounceDuration);
                }
            }

            _storage.Save();
            if (Config.Debug) Logger.Info("[Stats] Left: " + p.Nickname + " (" + p.UserId + ")");
        }

        private void OnDeath(PlayerDeathEventArgs ev)
        {
            Player victim = ev.Player;
            Player attacker = ev.Attacker;
            if (victim == null || victim.IsHost || string.IsNullOrEmpty(victim.UserId)) return;

            PlayerStats vs = _storage.GetOrCreate(victim.UserId, victim.Nickname);
            vs.Deaths++;

            string dmgType = ev.DamageHandler != null ? ev.DamageHandler.GetType().Name : "Unknown";
            int dCount;
            vs.DeathsByDamageType[dmgType] = vs.DeathsByDamageType.TryGetValue(dmgType, out dCount) ? dCount + 1 : 1;

            if (attacker != null && !attacker.IsHost && !string.IsNullOrEmpty(attacker.UserId))
            {
                if (attacker == victim)
                {
                    vs.Suicides++;
                }
                else
                {
                    PlayerStats ats = _storage.GetOrCreate(attacker.UserId, attacker.Nickname);
                    ats.Kills++;
                    string vRole = victim.RoleType.ToString();
                    int kCount;
                    ats.KillsByVictimRole[vRole] = ats.KillsByVictimRole.TryGetValue(vRole, out kCount) ? kCount + 1 : 1;

                    if (Config.TrackFriendlyFire && SameTeamSafe(attacker, victim))
                        ats.FriendlyFireKills++;
                }
            }

            _killsSinceSave++;
            if (Config.AutoSaveEveryKills > 0 && _killsSinceSave >= Config.AutoSaveEveryKills)
            {
                _storage.Save();
                _killsSinceSave = 0;
            }
        }

        private void OnRoundStarted(RoundStartedEventArgs ev)
        {
            int counted = 0;
            foreach (Player p in Player.List)
            {
                if (p == null || p.IsHost || string.IsNullOrEmpty(p.UserId)) continue;
                PlayerStats s = _storage.GetOrCreate(p.UserId, p.Nickname);
                s.RoundsPlayed++;
                counted++;
            }
            _storage.Save();
            Logger.Info("[Stats] Round started. Tracked " + counted + " player(s).");
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            if (Config.ShowRoundEndLeaderboard)
            {
                List<PlayerStats> top = _storage.Snapshot()
                    .OrderByDescending(x => x.Kills)
                    .Take(3)
                    .ToList();

                if (top.Count > 0)
                {
                    string board = "<color=#FFD700><b>=== Top Killers (all-time) ===</b></color>\n";
                    for (int i = 0; i < top.Count; i++)
                    {
                        PlayerStats s = top[i];
                        board +=
                            "<b>#" + (i + 1) + "</b> " + s.LastNickname + ": " +
                            "<color=#FF6347>" + s.Kills + "K</color>/" +
                            "<color=#A9A9A9>" + s.Deaths + "D</color> " +
                            "(<color=#87CEEB>" + s.Escapes + " esc</color>)\n";
                    }

                    foreach (Player o in Player.List)
                    {
                        if (o == null || o.IsHost) continue;
                        o.SendBroadcast(board, Config.LeaderboardDuration);
                    }
                }
            }

            _storage.Save();
            Logger.Info("[Stats] Round ended. Stats persisted (" + _storage.Count + " records).");
        }

        private static bool SameTeamSafe(Player a, Player b)
        {
            try { return a.Team == b.Team; }
            catch { return false; }
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
