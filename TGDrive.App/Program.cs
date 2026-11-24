using System;
using System.Windows;

namespace TGDrive.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        var window = new Window
        {
            Title = "TGDrive (v0.2.0)",
            Width = 400,
            Height = 300,
            Content = "UI will be built here (no XAML)"
        };
        
        Console.WriteLine("TGDrive App started.");
        app.Run(window);
    }
}