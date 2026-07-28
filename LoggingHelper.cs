using System.Text;

public static class Log
{
    public static void Info(string message) =>
        Console.WriteLine($"[INFO] {message}");

    public static void Error(Exception ex, string context = "") =>
        Console.Error.WriteLine(
            "[ERROR] " +
            $"{context}{ex.Message}" +
            FormatInnerChain(ex));

    private static string FormatInnerChain(Exception ex)
    {
        StringBuilder sb = new ();
        Exception? current = ex.InnerException;
        while (current != null)
        {
            sb.Append(" → ").Append(current.Message);
            current = current.InnerException;
        }
        return sb.Length > 0 ? sb.ToString() : "";
    }

    public static void Error(string message) =>
        Console.Error.WriteLine("[ERROR] " + message);
}
