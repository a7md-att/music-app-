using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AuraWall;

/// <summary>Monitor helpers (pixel coordinates; the process is Per-Monitor-V2 DPI aware).</summary>
internal static class Monitors
{
    public static Rectangle GetBounds(int index)
    {
        if (index == -1) return SystemInformation.VirtualScreen;
        var all = Screen.AllScreens;
        if (index >= 0 && index < all.Length) return all[index].Bounds;
        return Screen.PrimaryScreen?.Bounds ?? all[0].Bounds;
    }

    public static string[] Describe() =>
        Screen.AllScreens.Select((s, i) => $"Monitor {i + 1}  ({s.Bounds.Width}x{s.Bounds.Height}){(s.Primary ? " - primary" : "")}").ToArray();
}
