namespace PoC.Runner;
public interface ICustomLogger
{
	public void Log(string message);
	public void Information(string message);
	public void Error(string message);
	public void Warning(string message);
}
