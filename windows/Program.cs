using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using QRCoder;

const int port = 8766;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();
var code = LoadPairingCode();
var addresses = NetworkInterface.GetAllNetworkInterfaces()
    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
        adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
    .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
        !IPAddress.IsLoopback(address.Address))
    .Select(address => $"http://{address.Address}:{port}")
    .Distinct()
    .ToArray();

app.MapGet("/pair", (HttpContext context) =>
{
    if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
        context.Request.Host.Host != "127.0.0.1")
        return Results.NotFound();

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src data:; frame-ancestors 'none'";
    var page = new StringBuilder("<!doctype html><html lang='ko'><meta charset='utf-8'><title>DeskGlow QR</title><h1>DeskGlow QR 연결</h1><p>갤럭시에서 ‘QR로 연결’을 누르고 PC의 Wi-Fi 주소에 해당하는 QR을 스캔하세요.</p>");
    if (addresses.Length == 0)
        page.Append("<p>PC를 Wi-Fi에 연결한 뒤 DeskGlow를 다시 실행하세요.</p>");
    foreach (var address in addresses)
    {
        using var data = QRCodeGenerator.GenerateQrCode($"deskglow:{address}#{code}", QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        var png = Convert.ToBase64String(qr.GetGraphic(8));
        page.Append($"<section><h2>{address}</h2><img alt='DeskGlow 페어링 QR' src='data:image/png;base64,{png}'></section>");
    }
    return Results.Content(page.ToString(), "text/html; charset=utf-8");
});
var keys = new Dictionary<string, byte> { ["down"] = 0xAE, ["up"] = 0xAF, ["mute"] = 0xAD };
app.MapPost("/action/{action}", (HttpRequest request, string action) =>
{
    var supplied = Encoding.UTF8.GetBytes(request.Headers.Authorization.ToString());
    if (!CryptographicOperations.FixedTimeEquals(supplied, Encoding.UTF8.GetBytes($"Bearer {code}")))
        return Results.Unauthorized();

    if (keys.TryGetValue(action, out var key))
    {
        KeyboardEvent(key, 0, 0, UIntPtr.Zero);
        KeyboardEvent(key, 0, 2, UIntPtr.Zero);
    }
    else if (action == "site")
        Process.Start(new ProcessStartInfo("https://www.google.com/") { UseShellExecute = true });
    else if (action != "check")
        return Results.NotFound();
    return Results.NoContent();
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var address in addresses)
        Console.WriteLine($"DeskGlow PC address: {address}");
    Console.WriteLine($"Open on this PC to show the pairing QR: http://127.0.0.1:{port}/pair");
    Console.WriteLine($"Pairing code: {code}");
});
app.Run();

static string LoadPairingCode()
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
static extern void KeyboardEvent(byte key, byte scan, uint flags, UIntPtr extra);
