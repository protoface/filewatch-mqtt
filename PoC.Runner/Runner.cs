using MQTTnet;
using MQTTnet.Client;
using PoC.FileWatcherMQTT;
using System.Text.Json;

namespace PoC.Runner;

public class Runner
{
	Configuration options;
	IMqttClient client = new MqttFactory().CreateMqttClient();
	ICustomLogger? logger;
	private Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
	{
		// Extract mac
		string topic = arg.ApplicationMessage.Topic;
		string macString = topic.TrimEnd(options.InTopic.ToCharArray()).Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
		if (!int.TryParse(macString, out var mac))
			return Task.CompletedTask;

		// Parse payload
		Dictionary<string, string>? payload = JsonSerializer.Deserialize(arg.ApplicationMessage.PayloadSegment, JsonContext.Default.DictionaryStringString);
		if (payload is null)
			return Task.CompletedTask;

		// Build CSV
		string csv = string.Join(options.CSVSeparator, payload.OrderBy(e => options.Format[e.Key]).Select(e => e.Value));

		// Build File Path
		string filePath = Path.Combine(options.RootMachineDir, mac.ToString(), options.STMFileName);

		// Write to File
		File.WriteAllText(filePath, csv);

		logger?.Information($"{mac} written: {csv}");

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

		// map content to output type
		var data = content.Split(options.CSVSeparator, StringSplitOptions.TrimEntries);
		var result = new Dictionary<string, string>();
		foreach (var prop in options.Format)
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
		if (configFile == null)
			configFile = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "config.json");

		logger = log;

		// find & load config
		if (!File.Exists(configFile))
			throw new FileNotFoundException(null, configFile);
		options = Configuration.FromJSON(File.ReadAllText(configFile));
	}

	public async Task Run(CancellationToken cancellationToken)
	{

		// connect & subscribe to MQTT
		await client.ConnectAsync(new MqttClientOptionsBuilder()
			.WithTcpServer(options.Server, options.Port)
			.WithClientId(options.ClientId)
			.Build(), cancellationToken);

		await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
			.WithTopicFilter(new MqttTopicFilterBuilder().WithTopic($"{options.RootTopic}/+/{options.InTopic}").Build())
			.Build(), cancellationToken);

		logger?.Information($"MQTT: Connected to {options.Server}:{options.Port} as {options.ClientId}");

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
}