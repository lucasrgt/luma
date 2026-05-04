namespace Luma.Abstractions;

public interface IModLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);

    void Error(Exception exception, string message);
}
