using System;
using System.Threading;
using System.Windows;

namespace AuraWall;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "AuraWall.SingleInstance", out bool created);
        if (!created) return;

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        new AppHost().Start();
        app.Run();
    }
}
