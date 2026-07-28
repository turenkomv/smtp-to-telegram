public static class Log
{
    public static void Info(string message) =>
        Console.WriteLine($"[INFO] {message}");

    public static void Error(Exception ex, string context = "") =>
        Console.Error.WriteLine(
            "[ERROR] " +
            $"{context}{ex.Message}" +
            (ex.InnerException != null ? $" → {ex.InnerException.Message}" : ""));

    public static void Error(string message) =>
        Console.Error.WriteLine("[ERROR] " + message);
}
