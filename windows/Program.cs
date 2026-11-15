using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace DeskGlow;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Run(args);
        }
        catch (IOException error) when (error.InnerException is AddressInUseException)
        {
            MessageBox.Show("8766 포트가 이미 사용 중입니다. 이전 DeskGlow 창을 닫고 다시 실행하세요.",
                "DeskGlow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "DeskGlow 시작 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Run(string[] args)
    {
        const int port = 8766;
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        try
        {
            var code = LoadPairingCode();
            var expected = Encoding.UTF8.GetBytes($"Bearer {code}");
            var keys = new Dictionary<string, byte> { ["down"] = 0xAE, ["up"] = 0xAF, ["mute"] = 0xAD };
            long lastRequestTicks = 0;
            var enabled = 1;
            app.MapPost("/action/{action}", (HttpContext context, string action) =>
            {
                var supplied = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
                if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
                    return Results.Unauthorized();
                if (Volatile.Read(ref enabled) == 0)
                    return Results.StatusCode(503);

                if (keys.TryGetValue(action, out var key))
                {
                    KeyboardEvent(key, 0, 0, UIntPtr.Zero);
                    KeyboardEvent(key, 0, 2, UIntPtr.Zero);
                }
                else if (action == "site")
                    Process.Start(new ProcessStartInfo("https://www.google.com/") { UseShellExecute = true });
                else if (action != "check")
                    return Results.NotFound();

                if (context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote))
                    Interlocked.Exchange(ref lastRequestTicks, DateTime.UtcNow.Ticks);
                return Results.NoContent();
            });

            app.StartAsync().GetAwaiter().GetResult();
            using var window = new Dashboard(port, code, () => Interlocked.Read(ref lastRequestTicks),
                value =>
                {
                    Volatile.Write(ref enabled, value ? 1 : 0);
                    Interlocked.Exchange(ref lastRequestTicks, 0);
                });
            Application.Run(window);
            app.StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string LoadPairingCode()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskGlow");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pairing-code");
        if (!File.Exists(path))
        {
            var candidate = Path.Combine(directory, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(candidate, Convert.ToHexString(RandomNumberGenerator.GetBytes(12)));
                File.Move(candidate, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another instance saved the code first.
            }
            finally
            {
                File.Delete(candidate);
            }
        }
        var saved = File.ReadAllText(path).Trim();
        if (saved.Length != 24 || !saved.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Invalid pairing code in {path}");
        return saved.ToUpperInvariant();
    }

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeyboardEvent(byte key, byte scan, uint flags, UIntPtr extra);
}
