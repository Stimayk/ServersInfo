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
        public override string ModuleVersion => "v1.1";

        private IMenuApi? _menuApi;
        private readonly PluginCapability<IMenuApi?> _menuCapability = new("menu:nfcore");

        public ServersInfoConfig Config { get; set; } = new();

        private System.Timers.Timer? _serverInfoTimer;
        private int _currentServerIndex = 0;
        private readonly Random _random = new();

        public void OnConfigParsed(ServersInfoConfig config) => Config = config;

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _menuApi = _menuCapability.Get();
            StartServerInfoTimer();
        }

        private void StartServerInfoTimer()
        {
            _serverInfoTimer = new System.Timers.Timer(Config.Settings.AdvTime * 1000);
            _serverInfoTimer.Elapsed += async (sender, e) => await OnServerInfoTimerElapsed();
            _serverInfoTimer.Start();
        }

        private async Task OnServerInfoTimerElapsed()
        {
            var servers = Config.Modes.SelectMany(m => m.Value.Servers).ToList();
            var server = servers[GetNextServerIndex(servers.Count)];
            var info = await GetServerInfo(server.Value.IP, server.Value.DisplayName);

            if (info != null)
            {
                string serverInfo = string.Format(Localizer["ChatADV"], info.Name, info.Map, info.Players, info.MaxPlayers);
                Server.NextFrame(() => Server.PrintToChatAll(serverInfo));
            }
        }

        private int GetNextServerIndex(int serverCount) =>
            Config.Settings.Order ? (_currentServerIndex = (_currentServerIndex + 1) % serverCount) : _random.Next(serverCount);

        private async Task<InfoResponse?> GetServerInfo(string? ip, string? displayName)
        {
            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(displayName)) return null;

            var (connectIP, port) = ParseIP(ip);
            if (port == null) return null;

            connectIP = GetAliasIPIfExists(connectIP, displayName);

            return await QueryServerInfo(connectIP, port.Value, displayName);
        }

        private (string connectIP, int? port) ParseIP(string ip)
        {
            var ipParts = ip.Split(':');
            if (ipParts.Length != 2 || !int.TryParse(ipParts[1], out int port))
            {
                Logger.LogError($"Invalid IP address or port format: {ip}");
                return (string.Empty, null);
            }
            return (ipParts[0], port);
        }

        private string GetAliasIPIfExists(string connectIP, string? displayName)
        {
            var serverInfo = Config.Modes.SelectMany(m => m.Value.Servers)
                .FirstOrDefault(s => s.Value.DisplayName == displayName);
            return serverInfo.Value?.AliasIP?.Split(':')[0] ?? connectIP;
        }

        private async Task<InfoResponse?> QueryServerInfo(string connectIP, int port, string displayName)
        {
            var queryConnection = new QueryConnection { Host = connectIP, Port = port };
            try
            {
                ((IQueryConnection)queryConnection).Connect(5000);
                var info = await ((IQueryConnection)queryConnection).GetInfoAsync();
                return info;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to query server {displayName} ({connectIP}:{port}): {ex.Message}");
                return null;
            }
            finally
            {
                ((IQueryConnection)queryConnection).Disconnect();
            }
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
            if (!Config.Modes.TryGetValue(modeName, out var mode))
            {
                player.PrintToChat("Invalid mode selected.");
                return;
            }

            var serversMenu = _menuApi!.NewMenu(Localizer["MenuServersTitle", modeName], OpenBackMenu(modesMenu));
            foreach (var server in mode.Servers)
            {
                var info = await GetServerInfo(server.Value.IP, server.Value.DisplayName);
                string serverItemName = info != null ? $"{server.Value.DisplayName} ({info.Players}/{info.MaxPlayers})" : $"{server.Value.DisplayName} (Offline)";
                serversMenu.AddMenuOption(serverItemName, (_, _) => ShowServerInfo(player, server, info, serversMenu));
            }
            serversMenu.Open(player);
        }

        private void ShowServerInfo(CCSPlayerController player, KeyValuePair<string, ServerInfo> server, InfoResponse? info, IMenu serversMenu)
        {
            if (info != null)
            {
                ShowServerInfoMenu(player, server.Value.DisplayName, info, serversMenu, server.Value);
            }
            else
            {
                player.PrintToChat($"{Localizer["ServerOffline", server.Value.DisplayName ?? "Unknown Server"]}");
            }
        }

        private void ShowServerInfoMenu(CCSPlayerController player, string? displayName, InfoResponse info, IMenu serversMenu, ServerInfo server)
        {
            if (displayName == null || server.IP == null) return;

            var serverInfoMenu = _menuApi!.NewMenu(Localizer["ServerMenuTitle", displayName], OpenBackMenu(serversMenu));

            string displayIP = string.IsNullOrEmpty(server.AliasIP) ? server.IP : server.AliasIP;

            serverInfoMenu.AddMenuOption(Localizer["ShowInfoAction"], (_, _) =>
                player.PrintToChat($"{Localizer["ConnectMessage", info.Name, info.Map, info.Players, info.MaxPlayers, displayIP]}"));

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
                    string playerDetails = $"{playerInfo.Name} | {playerInfo.Duration.Minutes} minutes";
                    playersMenu.AddMenuOption(playerDetails, (_, _) => { }, true);
                    playersMenu.AddMenuOption(Localizer["PlayersInfo", playerInfo.Name, playerInfo.Duration.Hours, playerInfo.Duration.Minutes, playerInfo.Duration.Seconds], (_, _) => { }, true);
                }
            }
            playersMenu.Open(player);
        }

        private async Task<PlayerResponse?> GetServerPlayers(string gameIp)
        {
            var (connectIP, port) = ParseIP(gameIp);
            if (port == null) return null;

            var queryConnection = new QueryConnection { Host = connectIP, Port = port.Value };
            try
            {
                ((IQueryConnection)queryConnection).Connect(5000);
                return await ((IQueryConnection)queryConnection).GetPlayersAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to query server players from {gameIp}: {ex.Message}");
                return null;
            }
            finally
            {
                ((IQueryConnection)queryConnection).Disconnect();
            }
        }

        private static Action<CCSPlayerController> OpenBackMenu(IMenu menu) => p => menu.Open(p);
    }
}
