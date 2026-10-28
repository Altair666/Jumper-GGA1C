using System.Text;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

/// <summary>
/// Байты задания. В GoLabel PrintJob.SendStringToPrinter пишет каждую строку в порт сразу
/// (всегда UTF-8, PrintJob.cs:2045); здесь — в память, задание отдаётся целиком.
/// </summary>
public sealed class EzplWriter
{
    private readonly MemoryStream _buffer = new();

    public long Length => _buffer.Length;

    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _buffer.Write(bytes, 0, bytes.Length);
    }

    public void WriteLine(string text) => Write(text + "\r\n");

    public void WriteBytes(ReadOnlySpan<byte> bytes) => _buffer.Write(bytes);

    public byte[] ToArray() => _buffer.ToArray();
}
