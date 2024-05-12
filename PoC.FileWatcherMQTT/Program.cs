namespace PoC.FileWatcherMQTT;

internal static class Program
{
	private static async Task Main(string[] args)
	{
		string configFile = "config.json";
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] is "-c" or "--config")
			{
				configFile = args[++i];
			}
		}

		await new PoC.Runner.Runner(new ConsoleLogger(), configFile).Run(default);
	}
}
