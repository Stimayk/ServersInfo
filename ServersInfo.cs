using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using MenuManager;
using Microsoft.Extensions.Logging;
using Okolni.Source.Query;
using Okolni.Source.Query.Responses;

namespace ServersInfo
{
    public class ServersInfo : BasePlugin, IPluginConfig<ServersInfoConfig>
    {
        public override string ModuleName => "ServersInfo";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private IMenuApi? _menuApi;
        private readonly PluginCapability<IMenuApi?> _menuCapability = new("menu:nfcore");

        public ServersInfoConfig Config { get; set; } = new();

        private System.Timers.Timer? _serverInfoTimer;
        private int _currentServerIndex = 0;
        private readonly Random _random = new();

        public void OnConfigParsed(ServersInfoConfig config)
        {
            Config = config;
        }

        public override void Load(bool hotReload)
        {
            _menuApi = _menuCapability.Get();

            _serverInfoTimer = new System.Timers.Timer(Config.Settings.AdvTime * 1000);
            _serverInfoTimer.Elapsed += OnServerInfoTimerElapsed;
            _serverInfoTimer.Start();
        }

        private async void OnServerInfoTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var servers = Config.Modes.SelectMany(m => m.Value.Servers).ToList();
            int serverIndex = GetNextServerIndex(servers.Count);
            var server = servers[serverIndex];
            var info = await GetServerInfo(server.Value.IP, server.Value.DisplayName);

            if (info != null)
            {
                string serverInfo = string.Format(Localizer["ChatADV"], info.Name, info.Map, info.Players, info.MaxPlayers);
                Server.NextFrame(() => Server.PrintToChatAll(serverInfo));
            }
        }

        private int GetNextServerIndex(int serverCount)
        {
            return Config.Settings.Order ? (_currentServerIndex = (_currentServerIndex + 1) % serverCount) : _random.Next(serverCount);
        }

        private async Task<InfoResponse?> GetServerInfo(string? ip, string? displayName)
        {
            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(displayName))
                return null;

            var ipParts = ip.Split(':');
            if (ipParts.Length != 2 || !int.TryParse(ipParts[1], out int port))
            {
                Logger.LogError($"Invalid IP address or port format for server {displayName}: {ip}");
                return null;
            }

            var queryConnection = new QueryConnection { Host = ipParts[0], Port = port };
            try
            {
                ((IQueryConnection)queryConnection).Connect(5000);
                var info = await ((IQueryConnection)queryConnection).GetInfoAsync();
                ((IQueryConnection)queryConnection).Disconnect();
                return info;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to query server {displayName} ({ip}): {ex.Message}");
            }
            return null;
        }

        [ConsoleCommand("css_servers", "List of all servers")]
        public void OnServersCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || _menuApi == null) return;

            var modesMenu = _menuApi.NewMenu(Localizer["MenuModeTitle"]);
            foreach (var mode in Config.Modes)
            {
                modesMenu.AddMenuOption(mode.Key, async (_, option) => await ShowServersForMode(player, option.Text, modesMenu));
            }
            modesMenu.Open(player);
        }

        private async Task ShowServersForMode(CCSPlayerController player, string modeName, IMenu modesMenu)
        {
            if (!Config.Modes.TryGetValue(modeName, out var value))
            {
                player.PrintToChat("Invalid mode selected.");
                return;
            }

            var serversMenu = _menuApi!.NewMenu(Localizer["MenuServersTitle", modeName], OpenBackMenu(modesMenu));
            foreach (var server in value.Servers)
            {
                var info = await GetServerInfo(server.Value.IP, server.Value.DisplayName);
                string serverItemName = info != null ? $"{server.Value.DisplayName} ({info.Players}/{info.MaxPlayers})" : $"{server.Value.DisplayName} (Offline)";
                serversMenu.AddMenuOption(serverItemName, (_, _) =>
                {
                    if (info != null && server.Value.IP != null)
                    {
                        ShowServerInfoMenu(player, server.Value.DisplayName, info, serversMenu, server.Value);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(server.Value.DisplayName))
                        {
                            player.PrintToChat($"{Localizer["ServerOffline", server.Value.DisplayName]}");
                        }
                    }
                });
            }
            serversMenu.Open(player);
        }

        private void ShowServerInfoMenu(CCSPlayerController player, string? displayName, InfoResponse info, IMenu serversMenu, ServerInfo server)
        {
            if (displayName == null || server.IP == null) return;

            var serverInfoMenu = _menuApi!.NewMenu(Localizer["ServerMenuTitle", displayName], OpenBackMenu(serversMenu));
            serverInfoMenu.AddMenuOption(Localizer["ShowInfoAction"], (_, _) =>
                player.PrintToChat($"{Localizer["ConnectMessage", info.Name, info.Map, info.Players, info.MaxPlayers, server.IP]}"));

            if (info.Players > 0)
            {
                serverInfoMenu.AddMenuOption(Localizer["ShowPlayersAction"], async (_, _) =>
                    await ShowPlayersMenu(player, displayName, server.IP, serverInfoMenu));
            }
            serverInfoMenu.Open(player);
        }

        private async Task ShowPlayersMenu(CCSPlayerController player, string displayName, string? gameIp, IMenu serverInfoMenu)
        {
            if (string.IsNullOrEmpty(gameIp))
            {
                player.PrintToChat("Server IP is not available.");
                return;
            }

            var playersMenu = _menuApi!.NewMenu(Localizer["PlayersMenuTitle", displayName], OpenBackMenu(serverInfoMenu));
            var players = await GetServerPlayers(gameIp);

            if (players != null)
            {
                foreach (var playerInfo in players.Players)
                {
                    playersMenu.AddMenuOption(playerInfo.Name, (_, _) => { }, true);
                }
            }
            playersMenu.Open(player);
        }

        private async Task<PlayerResponse?> GetServerPlayers(string gameIp)
        {
            var ipParts = gameIp.Split(':');
            if (ipParts.Length != 2 || !int.TryParse(ipParts[1], out int port))
            {
                Logger.LogError($"Invalid game IP format: {gameIp}");
                return null;
            }

            var queryConnection = new QueryConnection { Host = ipParts[0], Port = port };
            try
            {
                ((IQueryConnection)queryConnection).Connect(5000);
                var players = await ((IQueryConnection)queryConnection).GetPlayersAsync();
                ((IQueryConnection)queryConnection).Disconnect();
                return players;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to query server players from {gameIp}: {ex.Message}");
            }
            return null;
        }

        private static Action<CCSPlayerController> OpenBackMenu(IMenu menu) => p => menu.Open(p);
    }
}