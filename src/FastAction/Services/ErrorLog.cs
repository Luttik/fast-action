using System.Text;

namespace FastAction.Services;

public static class ErrorLog
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FastAction",
        "last-error.log");

    public static void Write(string step, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("O")).Append("] ").Append(step);
            if (ex is not null)
            {
                sb.AppendLine();
                sb.Append(ex);
            }

            sb.AppendLine().AppendLine();
            File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Ignore logging failures.
        }
    }

    public static void Reset()
    {
        try
        {
            if (File.Exists(LogPath))
            {
                File.Delete(LogPath);
            }
        }
        catch
        {
            // Ignore.
        }
    }
}
