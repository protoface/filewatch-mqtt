using PoC.Runner;

namespace PoC.FileWatcherMQTT;
internal class ConsoleLogger : ICustomLogger
{
	public void Error(string message) => Console.Error.WriteLine(message);

	public void Information(string message) => Console.Out.WriteLine(message);

	public void Log(string message) => Console.WriteLine(message);

	public void Warning(string message) => Console.Error.WriteLine(message);
}
