using System.Net;
using Yeelight;

// A small tour of Yeelight Client: find lights, show what they are doing, and listen for changes. It only reads, unless you give --toggle.
//
//   dotnet run --project samples/ConsoleSample -- [address] [--toggle]
//
// Without an address the sample searches the networks of this computer: a UDP search to the multicast group of the lights and a broadcast,
// three times, from every network that the computer is on. With an address (192.168.1.50) it asks that one address, with a search that is sent
// straight to it, which also works across a VPN or another subnet. Nothing else is sent until you ask for it: there is no scan of the network.
// Every light needs LAN Control (Yeelight app: the light, settings, LAN Control), or it neither answers a search nor takes a connection.
//
// --toggle switches the first light that is found (on when it was off, off when it was on). Without it nothing about a light is changed.
var address = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
var toggle = args.Contains("--toggle");

using var client = new YeelightClient();

// The events are raised one at a time, on a thread of the thread pool, in the order in which they happened.
client.DeviceDiscovered += (_, e) => Console.WriteLine($"Found {e.Device.Name} ({e.Device.Id}) at {e.Device.Location}");
client.DeviceStateChanged += (_, e) =>
{
    var light = e.Light;
    Console.WriteLine($"{e.Device.Name}: {e.PropertyName} changed (on: {light.IsOn}, brightness: {light.Brightness}%, connected: {e.Device.IsConnected})");
};

IReadOnlyList<YeelightDevice> lights;
if (address is null)
{
    Console.WriteLine("Searching the networks of this computer for four seconds...");
    lights = await client.DiscoverAsync(TimeSpan.FromSeconds(4));
}
else
{
    if (!IPAddress.TryParse(address, out var ip))
    {
        Console.WriteLine($"'{address}' is not an IP address. Give one like 192.168.1.50: dotnet run --project samples/ConsoleSample -- [address] [--toggle]");
        return 2;
    }

    Console.WriteLine($"Asking {ip}...");
    try
    {
        lights = await client.ProbeAsync(ip) is { } found ? [found] : [];
    }
    catch (ArgumentException ex)
    {
        // A light has an address on the local network: a public address is refused before anything is sent.
        Console.WriteLine(ex.Message);
        return 2;
    }
}

if (lights.Count == 0)
{
    Console.WriteLine("No light answered. Is LAN Control switched on for it (Yeelight app), is it on the same network, and does the firewall let UDP port 1982 answers in?");
    return 1;
}

foreach (var light in lights)
{
    try
    {
        // The client connects by itself; waiting here makes sure that the state below is what the light says now.
        await light.ConnectAsync();
        Console.WriteLine($"{light.Name} ({light.Model}, firmware {light.FirmwareVersion}): {(light.IsOn ? "on" : "off")}, brightness {light.Brightness}%, {Describe(light)}");
    }
    catch (YeelightException ex)
    {
        Console.WriteLine($"{light.Name} could not be connected: {ex.Message}");
    }
}

if (toggle)
{
    var first = lights[0];
    Console.WriteLine($"Toggling {first.Name}...");
    await first.ToggleAsync();
}

Console.WriteLine("Listening for changes. Change a light with its app or switch, or press Enter to stop.");
await Task.Run(Console.ReadLine);
return 0;

// What the light shows is decided by its colour mode: only the property of that mode is valid.
static string Describe(YeelightDevice light)
{
    return light.ColorMode switch
    {
        ColorMode.ColorTemperature => $"{light.ColorTemperature} K",
        ColorMode.Rgb => $"colour #{light.Rgb:X6}",
        _ => $"hue {light.Hue}, saturation {light.Saturation}%",
    };
}
