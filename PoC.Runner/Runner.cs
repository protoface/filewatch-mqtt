using MQTTnet;
using MQTTnet.Client;
using PoC.FileWatcherMQTT;
using System.Text.Json;

namespace PoC.Runner;

public sealed class Runner
{
	readonly Configuration options;
	readonly IMqttClient client = new MqttFactory().CreateMqttClient();
	readonly ICustomLogger? logger;
	readonly Dictionary<int, string> lastMTS = [];
	CancellationToken cancellationToken;

	private Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
	{
		string[] topicSegments = arg.ApplicationMessage.Topic[options.RootTopic.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);

		// Extract mac from topic
		string macString = topicSegments[0];
		if (!int.TryParse(macString, out var mac))
			return Task.CompletedTask;

		if (topicSegments[1] != options.InTopic)
			return Task.CompletedTask;

		// check for mac exclusion
		if (options.MacExclusions.Contains(mac))
			return Task.CompletedTask;

		// Get Payload
		string payload = arg.ApplicationMessage.ConvertPayloadToString();

		// Build File Path
		string filePath;
		if (options.EnableFileUpload && topicSegments[^1] != options.InTopic)
		{
			filePath = Path.Combine(options.RootMachineDir, macString, options.MacFilesDirName, topicSegments[^1]);
			if (!Directory.Exists(Path.GetDirectoryName(filePath)))
				Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
		}
		else
		{
			filePath = Path.Combine(options.RootMachineDir, macString, options.STMFileName);
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

		// check for mac exclusion
		if (options.MacExclusions.Contains(mac))
			return;

		// read file
		string content;
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
		if (lastMTS.TryGetValue(mac, out string? value))
		{
			if (content == value) // no change
				return;

			lastMTS[mac] = content;
		}
		else
		{
			lastMTS.Add(mac, content); // first publish
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
		MqttClientPublishResult mqttClientPublishResult = await client.PublishStringAsync($"{options.RootTopic}/{mac}/{options.OutTopic}", payload);
		if (mqttClientPublishResult.IsSuccess)
		{

			logger?.Information($"{mac} published: {payload}");
		}
		else
		{
			logger?.Error($"Failed to publish {mac}: {payload}");
		}

	}

	public Runner(ICustomLogger? log, string? configFile = null)
	{
		logger = log;

		// find & load config
		configFile ??= Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "config.json");
		options = Configuration.FromJSON(File.ReadAllText(configFile));
	}

	public async Task Run(CancellationToken ct)
	{
		cancellationToken = ct;
		await ConnectMQTT();
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

		ct.WaitHandle.WaitOne();
	}

	private async Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
	{
		logger?.Warning($"MQTT: Disconnected. Reason: {arg.Reason}. Reconnecting");
		await ConnectMQTT();
	}

	private async Task ConnectMQTT()
	{
		// connect & subscribe to MQTT
		var clientOptions = new MqttClientOptionsBuilder()
					.WithTcpServer(options.Server, options.Port)
					.WithClientId(options.ClientId);

		// authentication is optional
		if (!string.IsNullOrEmpty(options.AuthUser) && !string.IsNullOrEmpty(options.AuthPwd))
		{
			clientOptions.WithCredentials(options.AuthUser, options.AuthPwd);
		}

		await client.ConnectAsync(clientOptions.Build(), cancellationToken);

		if (!client.IsConnected)
		{
			throw new($"MQTT: Failed to connect to {options.Server}:{options.Port} as {options.ClientId}");
		}

		// file uploads
		if (options.EnableFileUpload)
		{
			await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
				.WithTopicFilter(new MqttTopicFilterBuilder().WithTopic($"{options.RootTopic}/+/{options.InTopic}/+").Build())
				.Build(), cancellationToken);
		}

		// STM file upload
		await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
			.WithTopicFilter(new MqttTopicFilterBuilder().WithTopic($"{options.RootTopic}/+/{options.InTopic}").Build())
			.Build(), cancellationToken);

		logger?.Information($"MQTT: Connected to {options.Server}:{options.Port} as {options.ClientId}");
	}
}