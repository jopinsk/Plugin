using System;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;

namespace WelcomePlugin
{
    public sealed class Plugin : Plugin<Config>
    {
        public override string Name => "WelcomePlugin";
        public override string Description =>
            "Greets players on join and notifies others about joins/leaves.";
        public override string Author => "jopinsk";
        public override Version Version { get; } = new Version(1, 0, 0);
        public override Version RequiredApiVersion { get; } = new Version(1, 1, 0);

        public override void Enable()
        {
            if (!Config.IsEnabled)
            {
                Logger.Info("[WelcomePlugin] Disabled in config.");
                return;
            }

            PlayerEvents.Joined += OnPlayerJoined;
            PlayerEvents.Left   += OnPlayerLeft;

            Logger.Info("[WelcomePlugin] Enabled.");
        }

        public override void Disable()
        {
            PlayerEvents.Joined -= OnPlayerJoined;
            PlayerEvents.Left   -= OnPlayerLeft;

            Logger.Info("[WelcomePlugin] Disabled.");
        }

        private void OnPlayerJoined(PlayerJoinedEventArgs ev)
        {
            Player player = ev.Player;
            if (player is null || player.IsHost)
                return;

            if (Config.Debug)
                Logger.Info($"[WelcomePlugin] {player.Nickname} ({player.UserId}) joined.");

            string text = Config.WelcomeMessage.Replace("%player%", player.Nickname);
            player.SendBroadcast(text, Config.BroadcastDuration);

            if (!Config.AnnounceJoin)
                return;

            string hint = $"<color=#7CFC00>+ {player.Nickname}</color> присоединился к серверу";
            foreach (Player other in Player.List)
            {
                if (other == player || other.IsHost) continue;
                other.SendHint(hint, Config.HintDuration);
            }
        }

        private void OnPlayerLeft(PlayerLeftEventArgs ev)
        {
            Player player = ev.Player;
            if (player is null || player.IsHost || !Config.AnnounceLeave)
                return;

            if (Config.Debug)
                Logger.Info($"[WelcomePlugin] {player.Nickname} ({player.UserId}) left.");

            string hint = $"<color=#FF6B6B>- {player.Nickname}</color> покинул сервер";
            foreach (Player other in Player.List)
            {
                if (other == player || other.IsHost) continue;
                other.SendHint(hint, Config.HintDuration);
            }
        }
    }
}
