using MQTTnet;
using MQTTnet.Client;
using PoC.FileWatcherMQTT;
using System.Text.Json;

namespace PoC.Runner;

public class Runner
{
	readonly Configuration options;
	readonly IMqttClient client = new MqttFactory().CreateMqttClient();
	readonly ICustomLogger? logger;
	Dictionary<int, string> lastMTS = new();

	private Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
	{
		string topic = arg.ApplicationMessage.Topic.TrimStart(options.RootTopic.ToCharArray());
		string[] fileName = [];

		// Extract mac
		// string macString = topic.TrimEnd(options.InTopic.ToCharArray()).Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
		string[] strings = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
		string macString = strings[0];

		if (strings[1] != options.InTopic)
			return Task.CompletedTask;

		if (!int.TryParse(macString, out var mac))
			return Task.CompletedTask;

		// Get Payload
		string payload = arg.ApplicationMessage.ConvertPayloadToString();


		// Build File Path
		string filePath = string.Empty;
		if (strings[^1] != options.InTopic)
		{
			filePath = Path.Combine(options.RootMachineDir, macString, options.MacFilesDirName, strings[^1]);
			if (!Directory.Exists(Path.GetDirectoryName(filePath)))
				Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
		}
		else
		{
			filePath = Path.Combine(options.RootMachineDir, mac.ToString(), options.STMFileName);
		}

		// Write to File
		File.WriteAllText(filePath, payload);

		logger?.Information($"{mac} written: \"{filePath}\"");

		return Task.CompletedTask;
	}

	private async void MTS_Changed(object _, FileSystemEventArgs e)
	{
		// Extract mac from path
		string? macString = Path.GetFileName(Path.GetDirectoryName(e.FullPath));
		if (macString == null || !int.TryParse(macString, out int mac))
			return;

		// read file
		string content = string.Empty;
		try
		{
			content = File.ReadAllText(e.FullPath);
		}
		catch (IOException ex)
		{
			logger?.Error(ex.ToString());
			return;
		}

		// Only publish when real changes occur
		if (!lastMTS.ContainsKey(mac))
		{
			lastMTS.Add(mac, content);
		}
		else
		{
			if (content == lastMTS[mac])
			{
				return;
			}
			lastMTS[mac] = content;
		}

		// map content to output type
		var data = content.Split(options.CSVSeparator, StringSplitOptions.TrimEntries);
		var result = new Dictionary<string, string>();
		foreach (var prop in options.MTSFormat)
		{
			if (data.Length <= prop.Value)
				continue;
			result.TryAdd(prop.Key, data[prop.Value]);
		}

		// publish to MQTT
		string payload = JsonSerializer.Serialize(result, JsonContext.Default.DictionaryStringString);
		await client.PublishStringAsync($"{options.RootTopic}/{mac}/{options.OutTopic}", payload);
		logger?.Information($"{mac} published: {payload}");
	}

	public Runner(ICustomLogger? log, string? configFile = null)
	{
		configFile ??= Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "config.json");

		logger = log;

		// find & load config
		if (!File.Exists(configFile))
			throw new FileNotFoundException(null, configFile);
		options = Configuration.FromJSON(File.ReadAllText(configFile));

	}

	public async Task Run(CancellationToken cancellationToken)
	{
		await ConnectMQTT(cancellationToken);
		client.DisconnectedAsync += Client_DisconnectedAsync;

		// subscribe to changes
		if (!Directory.Exists(options.RootMachineDir))
			throw new DirectoryNotFoundException($"\"{options.RootMachineDir}\" doesn't exist");

		FileSystemWatcher rootWatcher = new(options.RootMachineDir, options.MTSFileName)
		{
			EnableRaisingEvents = true,
			IncludeSubdirectories = true
		};

		rootWatcher.Changed += MTS_Changed;
		client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;

		cancellationToken.WaitHandle.WaitOne();
	}

	private async Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
	{
		logger?.Warning($"MQTT: Disconnected. Reason: {arg.Reason}. Reconnecting");
		await ConnectMQTT(default);
	}

	private async Task ConnectMQTT(CancellationToken cancellationToken)
	{
		// connect & subscribe to MQTT
		var clientOptions = new MqttClientOptionsBuilder()
					.WithTcpServer(options.Server, options.Port)
					//.WithTimeout(TimeSpan.FromSeconds(10))
					//.WithKeepAlivePeriod(TimeSpan.FromSeconds(5))
					.WithClientId(options.ClientId);

		// authentication is optional
		if (options.AuthMethod != null && options.AuthDataBase64 != null)
		{
			clientOptions.WithAuthentication(options.AuthMethod, Convert.FromBase64String(options.AuthDataBase64));
		}

		await client.ConnectAsync(clientOptions.Build(), cancellationToken);

		if (!client.IsConnected)
		{
			logger?.Error($"MQTT: Failed to connect to {options.Server}:{options.Port} as {options.ClientId}");
			throw new("Failed to connect to mqtt");
		}

		await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
			.WithTopicFilter(new MqttTopicFilterBuilder().WithTopic($"{options.RootTopic}/+/{options.InTopic}/+").Build())
			.Build(), cancellationToken);

		logger?.Information($"MQTT: Connected to {options.Server}:{options.Port} as {options.ClientId}");
	}
}