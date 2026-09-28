using System.Text;
using FlareSync.Core.Commands;

namespace FlareSync.Cli.CommandLine;

/// <summary><see cref="ICommandConsole"/> over <see cref="Console"/>.</summary>
internal sealed class ConsoleCommandConsole : ICommandConsole
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public void WriteLine(string text = "") => Console.Out.WriteLine(text);

    public void WriteError(string text) => Console.Error.WriteLine(text);

    public void WriteTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var data = rows.ToList();
        var widths = headers.Select((h, i) => Math.Max(h.Length, data.Count == 0 ? 0 : data.Max(r => i < r.Count ? r[i].Length : 0))).ToArray();

        WriteRow(headers, widths);
        WriteRow(widths.Select(w => new string('-', w)).ToList(), widths);
        foreach (var row in data)
        {
            WriteRow(row, widths);
        }
    }

    public string? ReadLine(string prompt)
    {
        Console.Out.Write(prompt);
        return Console.ReadLine();
    }

    public string? ReadSecret(string prompt)
    {
        Console.Out.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine();
        }

        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Out.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }

    public bool Confirm(string prompt, bool defaultValue)
    {
        if (!IsInteractive)
        {
            return defaultValue;
        }

        var answer = ReadLine($"{prompt} [{(defaultValue ? "Y/n" : "y/N")}] ")?.Trim();
        return string.IsNullOrEmpty(answer)
            ? defaultValue
            : answer.StartsWith('y') || answer.StartsWith('Y');
    }

    private static void WriteRow(IReadOnlyList<string> cells, int[] widths)
    {
        var line = string.Join("  ", widths.Select((w, i) => (i < cells.Count ? cells[i] : "").PadRight(w)));
        Console.Out.WriteLine(line.TrimEnd());
    }
}
