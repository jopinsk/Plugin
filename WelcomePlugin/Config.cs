using System.ComponentModel;

namespace WelcomePlugin
{
    public sealed class Config
    {
        [Description("Включить плагин.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Включить отладочные логи.")]
        public bool Debug { get; set; } = false;

        [Description("Текст приветствия. %player% — ник игрока.")]
        public string WelcomeMessage { get; set; } =
            "<b>Добро пожаловать, <color=#7FFFD4>%player%</color>!</b>\n<size=80%>Приятной игры :)</size>";

        [Description("Длительность приветственного broadcast, сек.")]
        public ushort BroadcastDuration { get; set; } = 8;

        [Description("Оповещать остальных игроков о заходе.")]
        public bool AnnounceJoin { get; set; } = true;

        [Description("Оповещать остальных игроков о выходе.")]
        public bool AnnounceLeave { get; set; } = true;

        [Description("Длительность hint-оповещения, сек.")]
        public ushort HintDuration { get; set; } = 5;
    }
}
