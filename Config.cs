using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace ServersInfo
{
    public class ServersInfoConfig : BasePluginConfig
    {

        [JsonPropertyName("Modes")]
        public Dictionary<string, ModeConfig> Modes { get; set; } = [];

        [JsonPropertyName("Settings")]
        public SettingsConfig Settings { get; set; } = new SettingsConfig();

        public ServersInfoConfig()
        {
            Modes.Add("AWP", new ModeConfig
            {
                Servers = new Dictionary<string, ServerInfo>
                {
                    {
                        "server1", new ServerInfo
                        {
                            IP = "127.0.0.1:27015",
                            DisplayName = "AWP #1",
                            AliasIP = ""
                        }
                    },
                    {
                        "server2", new ServerInfo
                        {
                            IP = "127.0.0.1:27016",
                            DisplayName = "AWP #2",
                            AliasIP = ""
                        }
                    }
                }
            });

            Modes.Add("PUBLIC", new ModeConfig
            {
                Servers = new Dictionary<string, ServerInfo>
                {
                    {
                        "server1", new ServerInfo
                        {
                            IP = "127.0.0.1:27017",
                            DisplayName = "Public #1",
                            AliasIP = "203.0.113.1:27016"
                        }
                    }
                }
            });

            Settings = new SettingsConfig
            {
                AdvTime = 60.0f,
                Order = true
            };
        }
    }

    public class ModeConfig
    {
        [JsonPropertyName("servers")]
        public Dictionary<string, ServerInfo> Servers { get; set; } = [];
    }

    public class ServerInfo
    {
        [JsonPropertyName("ip")]
        public string? IP { get; set; }

        [JsonPropertyName("alias_ip")]
        public string? AliasIP { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }

    public class SettingsConfig
    {
        [JsonPropertyName("adv_time")]
        public float AdvTime { get; set; }

        [JsonPropertyName("order")]
        public bool Order { get; set; }

        [JsonPropertyName("show_categories")]
        public bool ShowCategories { get; set; } = true;

        [JsonPropertyName("log_errors")]
        public bool LogErrors { get; set; } = true;
    }
}
