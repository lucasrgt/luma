using Luma.Abstractions;

namespace Luma.Runtime;

internal sealed class FileModLogger : IModLogger
{
    private readonly string path;
    private readonly object gate = new();

    public FileModLogger(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warn(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(string level, string message)
    {
        string line = $"[{DateTimeOffset.Now:O}] [{level}] {message}{Environment.NewLine}";
        lock (gate)
        {
            File.AppendAllText(path, line);
        }
    }
}
