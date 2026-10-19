using System.Text;

namespace Tidegate.Core;

public static class TerminalText
{
    public static string Clean(string line)
    {
        var buffer = new StringBuilder(line.Length);
        foreach (var ch in line)
        {
            if (ch == '\b')
            {
                if (buffer.Length > 0)
                    buffer.Length--;
                continue;
            }

            if (!char.IsControl(ch))
                buffer.Append(ch);
        }

        return buffer.ToString();
    }
}
