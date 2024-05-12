using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoC.FileWatcherMQTT;

internal static class Program
{
	private static async Task Main(string[] args)
	{
		CancellationToken ct = default(CancellationToken);
		string configFile = "config.json";
		if (args.Length > 0)
			configFile = args[0];
		if (!File.Exists(configFile))
			throw new FileNotFoundException(null, configFile);
		Configuration options = JsonSerializer.Deserialize(File.ReadAllText(configFile), JsonContext.Default.Configuration);

		MqttFactory factory = new();

		IMqttClient client = factory.CreateMqttClient();

		await client.ConnectAsync(factory.CreateClientOptionsBuilder()
			.WithTcpServer(options.Server, options.Port)
			.WithClientId(options.ClientId)
			.Build());
		Console.WriteLine($"MQTT: Connected to {options.Server}:{options.Port} as {options.ClientId}");

		if (!Directory.Exists(options.RootMachineDir))
			throw new DirectoryNotFoundException($"\"{options.RootMachineDir}\" doesn't exist");

		FileSystemWatcher rootWatcher = new(options.RootMachineDir, options.MTCPattern);
		rootWatcher.EnableRaisingEvents = true;
		rootWatcher.IncludeSubdirectories = true;
		rootWatcher.Changed += (_, e) => Task.Run(async () =>
		{
			string? macString = Path.GetFileName(Path.GetDirectoryName(e.FullPath));
			if (macString == null || !int.TryParse(macString, out int mac))
				return;

			string content = File.ReadAllText(e.FullPath);
			var data = new MacData(content.Split(options.CSVDelimiter, StringSplitOptions.TrimEntries));
			string payload = JsonSerializer.Serialize(data, JsonContext.Default.MacData);
			await client.PublishStringAsync($"{options.MQTTRootTopic}/{mac}", payload);
			Console.WriteLine($"{mac} published: {payload}");
		});

		ct.WaitHandle.WaitOne();

	}

}

public struct MacData(string[] csvData)
{
	public int Status { get; } = int.Parse(csvData[0]);
	public string Prop2 { get; } = csvData[1];
	public string Prop3 { get; } = csvData[2];
	public string Prop4 { get; } = csvData[3];
}


public struct Configuration()
{
	public string Server { get; set; } = "localhost";
	public int Port { get; set; } = 1883;
	public string ClientId { get; set; } = "WatchPuppy";
	[JsonRequired]
	public string RootMachineDir { get; set; } = null!;
	public string MTCPattern { get; set; } = "MacToServer.txt";
	public string CSVDelimiter { get; set; } = ",";
	public string MQTTRootTopic { get; set; } = "macs";
}

[JsonSerializable(typeof(Configuration))]
[JsonSerializable(typeof(MacData))]
public sealed partial class JsonContext : JsonSerializerContext;