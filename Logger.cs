using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VrchanWin;

public static class Logger
{
    private static readonly object LockObj = new();
    private static readonly List<string> LinesInternal = new();
    private const int MaxLines = 1000;

    public static string[] Lines
    {
        get
        {
            lock (LockObj)
            {
                return LinesInternal.ToArray();
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        if (ex != null)
        {
            Write("ERROR", message + " : " + ex);
        }
        else
        {
            Write("ERROR", message);
        }
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] {level} {message}";
        lock (LockObj)
        {
            LinesInternal.Add(line);
            if (LinesInternal.Count > MaxLines)
            {
                LinesInternal.RemoveAt(0);
            }
        }

        Debug.WriteLine(line);
    }
}
