using PoC.Runner;

namespace PoC.Worker;

public class Worker(ILogger<Worker> logger, ILogger<PoC.Runner.Runner> runnerLogger, [FromKeyedServices("cmdArgs")] string[] args) : BackgroundService
{

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			string? configFile = null;
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] is "-c" or "--config")
					configFile = args[++i];
			}

			PoC.Runner.Runner runner = new(new ILoggerLogger(runnerLogger));

			await runner.Run(stoppingToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "{Message}", ex.Message);
			Environment.Exit(1);
		}
	}

	private class ILoggerLogger(ILogger logger) : ICustomLogger
	{
		public void Error(string message) => logger.LogError(message);

		public void Information(string message) => logger.LogInformation(message);

		public void Log(string message) => logger.Log(LogLevel.None, message);

		public void Warning(string message) => logger.LogWarning(message);
	}
}
