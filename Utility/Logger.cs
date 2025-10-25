using System.Diagnostics;
using Godot;

public static class Logger
{
    [Conditional("DEBUG")]
    public static void Log(string message)
    {
        GD.Print("[LOG]: " + message);
    }

    [Conditional("DEBUG")]
    public static void Warn(string message)
    {
        GD.PrintErr("[WARNING]: " + message);
    }

    [Conditional("DEBUG")]
    public static void Error(string message)
    {
        GD.PrintErr("[ERROR]: " + message);
    }
}