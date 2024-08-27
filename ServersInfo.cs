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
        public override string ModuleVersion => "v1.1.1";

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

            _serverInfoTimer = new System.Timers.Timer(Config.Settings.AdvTime * 1000);
            _serverInfoTimer.Elapsed += OnServerInfoTimerElapsed;
            _serverInfoTimer.Start();
        }

        private async void OnServerInfoTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var servers = GetAllServers();
            if (servers.Count == 0) return;

            var server = servers[GetNextServerIndex(servers.Count)];
            var info = await GetServerInfo(server.Value.IP, server.Value.DisplayName);

            if (info != null)
            {
                string? connectIP = GetConnectIP(server.Value);
                var serverInfo = string.Format(Localizer["ChatADV"], info.Name, info.Map, info.Players, info.MaxPlayers, connectIP);
                Server.NextFrame(() => Server.PrintToChatAll(serverInfo));
            }
        }

        private int GetNextServerIndex(int serverCount) =>
            Config.Settings.Order ? (_currentServerIndex = (_currentServerIndex + 1) % serverCount) : _random.Next(serverCount);

        private async Task<InfoResponse?> GetServerInfo(string? ip, string? displayName)
        {
            if (!TryParseIP(ip, out var queryConnection))
            {
                LogError(Config.Settings.LogErrors, $"Invalid IP address or port format for server {displayName}: {ip}");
                return null;
            }

            return await QueryServerInfo(queryConnection, displayName);
        }

        private async Task<PlayerResponse?> GetServerPlayers(string gameIp)
        {
            if (!TryParseIP(gameIp, out var queryConnection))
            {
                Logger.LogError($"Invalid game IP format: {gameIp}");
                return null;
            }

            return await QueryServerPlayers(queryConnection, gameIp);
        }

        private static bool TryParseIP(string? ip, out QueryConnection queryConnection)
        {
            queryConnection = new QueryConnection();
            if (string.IsNullOrEmpty(ip)) return false;

            var ipParts = ip.Split(':');
            if (ipParts.Length != 2 || !int.TryParse(ipParts[1], out int port)) return false;

            queryConnection.Host = ipParts[0];
            queryConnection.Port = port;
            return true;
        }

        private async Task<InfoResponse?> QueryServerInfo(QueryConnection queryConnection, string? displayName)
        {
            try
            {
                ((IQueryConnection)queryConnection).Connect(5000);
                var info = await ((IQueryConnection)queryConnection).GetInfoAsync();
                return info;
            }
            catch (Exception ex)
            {
                LogError(Config.Settings.LogErrors, $"Failed to query server {displayName}: {ex.Message}");
                return null;
            }
            finally
            {
                ((IQueryConnection)queryConnection).Disconnect();
            }
        }

        private async Task<PlayerResponse?> QueryServerPlayers(QueryConnection queryConnection, string gameIp)
        {
            try
            {
                ((IQueryConnection)queryConnection).Connect(5000);
                var players = await ((IQueryConnection)queryConnection).GetPlayersAsync();
                return players;
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

        private List<KeyValuePair<string, ServerInfo>> GetAllServers() =>
            Config.Modes.SelectMany(m => m.Value.Servers).ToList();

        private void LogError(bool logErrors, string message)
        {
            if (logErrors)
                Logger.LogError(message);
        }

        [ConsoleCommand("css_servers", "List of all servers")]
        public async void OnServersCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || _menuApi == null) return;

            if (Config.Settings.ShowCategories)
                ShowModesMenu(player);
            else
                await ShowServersMenu(player);
        }

        private void ShowModesMenu(CCSPlayerController player)
        {
            var modesMenu = _menuApi!.NewMenu(Localizer["MenuModeTitle"]);
            foreach (var mode in Config.Modes)
            {
                modesMenu.AddMenuOption(mode.Key, async (_, option) => await ShowServersForMode(player, option.Text, modesMenu));
            }
            modesMenu.Open(player);
        }

        private async Task ShowServersMenu(CCSPlayerController player)
        {
            var serversMenu = _menuApi!.NewMenu(Localizer["MenuServersTitle"]);
            foreach (var mode in Config.Modes)
            {
                foreach (var server in mode.Value.Servers)
                {
                    await AddServerToMenu(serversMenu, player, server.Value);
                }
            }
            serversMenu.Open(player);
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
                await AddServerToMenu(serversMenu, player, server.Value);
            }
            serversMenu.Open(player);
        }

        private async Task AddServerToMenu(IMenu serversMenu, CCSPlayerController player, ServerInfo server)
        {
            var info = await GetServerInfo(server.IP, server.DisplayName);
            string serverItemName = info != null ? $"{server.DisplayName} ({info.Players}/{info.MaxPlayers})" : $"{server.DisplayName} (Offline)";

            serversMenu.AddMenuOption(serverItemName, (_, _) =>
            {
                if (info != null)
                {
                    ShowServerInfoMenu(player, server.DisplayName, info, serversMenu, server);
                }
                else
                {
                    player.PrintToChat($"{Localizer["ServerOffline", server.DisplayName ?? ""]}");
                }
            });
        }

        private void ShowServerInfoMenu(CCSPlayerController player, string? displayName, InfoResponse info, IMenu serversMenu, ServerInfo server)
        {
            if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(server.IP)) return;

            var serverInfoMenu = _menuApi!.NewMenu(Localizer["ServerMenuTitle", displayName], OpenBackMenu(serversMenu));

            string? connectIP = GetConnectIP(server);
            if (connectIP != null)
            {
                serverInfoMenu.AddMenuOption(Localizer["ShowInfoAction"], (_, _) =>
                    player.PrintToChat($"{Localizer["ConnectMessage", info.Name, info.Map, info.Players, info.MaxPlayers, connectIP]}"));

                if (info.Players > 0)
                {
                    serverInfoMenu.AddMenuOption(Localizer["ShowPlayersAction"], async (_, _) =>
                        await ShowPlayersMenu(player, displayName, connectIP, serverInfoMenu));
                }
            }
            serverInfoMenu.Open(player);
        }

        private async Task ShowPlayersMenu(CCSPlayerController player, string displayName, string gameIp, IMenu serverInfoMenu)
        {
            var playersMenu = _menuApi!.NewMenu(Localizer["PlayersMenuTitle", displayName], OpenBackMenu(serverInfoMenu));
            var players = await GetServerPlayers(gameIp);

            if (players?.Players != null)
            {
                foreach (var playerInfo in players.Players)
                {
                    playersMenu.AddMenuOption($"{playerInfo.Name}", (_, _) =>
                        player.PrintToChat($"{playerInfo.Name} - {playerInfo.Score} score, {playerInfo.Duration} on server"));
                }
            }
            playersMenu.Open(player);
        }

        private static string? GetConnectIP(ServerInfo server) => server.IP;

        private static Action<CCSPlayerController> OpenBackMenu(IMenu menu) => p => menu.Open(p);

        public override void Unload(bool hotReload)
        {
            _serverInfoTimer?.Stop();
            _serverInfoTimer?.Dispose();
            _serverInfoTimer = null;
        }
    }
}
