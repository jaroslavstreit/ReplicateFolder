using System.Text;

namespace ReplicateFolder;

public sealed class Logger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private bool _disposed;

    public Logger(string filePath)
    {
        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    public void Log(string message) => Write("INFO", message, Console.Out);

    public void LogError(string message) => Write("ERROR", message, Console.Error);

    private void Write(string level, string message, TextWriter console)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (_gate)
        {
            if (_disposed) return;
            console.WriteLine(line);
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
