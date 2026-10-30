using System;
using System.Collections.Generic;

/// <summary>Keeps a short local history of visible Escape mode events for the HUD.</summary>
public static class EscapeModeHudFeed
{
    private const int Capacity = 5;
    private static readonly List<string> messages = new List<string>(Capacity);
    private static string lastMessage = "";

    public static IReadOnlyList<string> Messages => messages;
    public static void Clear()
    {
        messages.Clear();
        lastMessage = "";
    }

    public static void Publish(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (lastMessage == message) return;
        lastMessage = message;
        messages.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        if (messages.Count > Capacity) messages.RemoveAt(0);
    }
}
