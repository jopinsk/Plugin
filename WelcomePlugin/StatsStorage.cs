using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabApi.Features.Console;
using Newtonsoft.Json;

namespace WelcomePlugin
{
    /// <summary>
    /// Thread-safe JSON-backed store for <see cref="PlayerStats"/>.
    /// </summary>
    public sealed class StatsStorage
    {
        private readonly string _filePath;
        private readonly bool _pretty;
        private readonly object _lock = new object();
        private Dictionary<string, PlayerStats> _all = new Dictionary<string, PlayerStats>();

        public StatsStorage(string directory, bool pretty)
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "player_stats.json");
            _pretty = pretty;
        }

        public string FilePath { get { return _filePath; } }

        public void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath))
                    {
                        _all = new Dictionary<string, PlayerStats>();
                        return;
                    }
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, PlayerStats>>(json);
                    _all = loaded ?? new Dictionary<string, PlayerStats>();
                    Logger.Info("[Stats] Loaded " + _all.Count + " player records from " + _filePath);
                }
                catch (Exception ex)
                {
                    Logger.Error("[Stats] Failed to load stats: " + ex.Message);
                    _all = new Dictionary<string, PlayerStats>();
                }
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(_all,
                        _pretty ? Formatting.Indented : Formatting.None);
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    Logger.Error("[Stats] Failed to save stats: " + ex.Message);
                }
            }
        }

        public PlayerStats GetOrCreate(string userId, string nickname)
        {
            lock (_lock)
            {
                PlayerStats stats;
                if (!_all.TryGetValue(userId, out stats))
                {
                    stats = new PlayerStats
                    {
                        UserId = userId,
                        LastNickname = nickname ?? string.Empty,
                        FirstSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    };
                    _all[userId] = stats;
                }
                if (!string.IsNullOrEmpty(nickname))
                    stats.LastNickname = nickname;
                return stats;
            }
        }

        public List<PlayerStats> Snapshot()
        {
            lock (_lock) { return _all.Values.ToList(); }
        }

        public int Count
        {
            get { lock (_lock) { return _all.Count; } }
        }
    }
}
