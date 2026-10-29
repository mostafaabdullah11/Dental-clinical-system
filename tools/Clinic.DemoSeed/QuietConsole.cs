using System.Text;

namespace Clinic.DemoSeed;

/// <summary>
/// The hosted API logs structured JSON lines to the console. This writer drops the informational ones and keeps
/// plain tool output plus any JSON log line of level Error or Fatal.
/// </summary>
internal sealed class QuietConsole(TextWriter inner) : TextWriter
{
    private readonly StringBuilder line = new();

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        if (value == '\n')
        {
            Flush(true);
        }
        else if (value != '\r')
        {
            line.Append(value);
        }
    }

    public override void Write(string? value)
    {
        foreach (var c in value ?? string.Empty)
        {
            Write(c);
        }
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    public override void Flush()
    {
        Flush(false);
        inner.Flush();
    }

    private void Flush(bool endOfLine)
    {
        if (line.Length == 0 && !endOfLine)
        {
            return;
        }

        var text = line.ToString();
        line.Clear();
        var isLog = text.StartsWith("{\"@t\":", StringComparison.Ordinal);
        if (!isLog || text.Contains("\"@l\":\"Error\"", StringComparison.Ordinal) || text.Contains("\"@l\":\"Fatal\"", StringComparison.Ordinal))
        {
            inner.WriteLine(text);
        }
    }
}
