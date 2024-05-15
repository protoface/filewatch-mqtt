using PoC.FileWatcherMQTT;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoC.Runner;

public struct Configuration()
{
	public static Configuration FromJSON(string json)
	{
		var config = JsonSerializer.Deserialize(json, JsonContext.Default.Configuration);
		if (config.MTSFormat.Values.GroupBy(e => e).Any(e => e.Count() > 1))
			throw new("Configuration: Format: each index can only be used once");
		return config;
	}

	[JsonRequired] public string RootMachineDir { get; set; } = string.Empty;
	[JsonRequired] public string MacFilesDirName { get; set; } = string.Empty;
	public string MTSFileName { get; set; } = "MacToServer.txt";
	public string STMFileName { get; set; } = "ServerToMac.txt";
	public string CSVSeparator { get; set; } = ",";

	public string Server { get; set; } = "localhost";
	public int Port { get; set; } = 1883;
	public string ClientId { get; set; } = "WatchPuppy";

	public string RootTopic { get; set; } = "macs";
	public string OutTopic { get; set; } = "out";
	public string InTopic { get; set; } = "in";

	[JsonRequired]
	public Dictionary<string, int> MTSFormat { get; set; } = new();

	public string? AuthUser { get; set; } = null;
	public string? AuthPwd { get; set; } = null;
}
