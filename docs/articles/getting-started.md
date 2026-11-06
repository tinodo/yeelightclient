---
title: Getting started
---

# Getting started

This page takes you from nothing to a small program that finds a light on your network and switches it.

> [!NOTE]
> This site documents Yeelight Client **1.0.0-beta.1**, the first release, which is a prerelease: its public API can still change before 1.0.0.
> The snippets on this site assume `using Yeelight;` and the implicit usings of a .NET project (`dotnet new console` turns them on).

## What you need

- **A Yeelight light with LAN Control switched on.** In the Yeelight app, select the light, open its settings and switch on **LAN Control**.
  A light without it neither answers a search nor accepts a connection, so every light needs it, one by one.
- **The light and the computer on the same network.** A search goes to the multicast group of the lights and to the broadcast address of
  each network of the computer, and neither passes a router. For a light on another subnet, behind a VPN or on a guest network, ask its
  address directly: see [A VPN, another subnet or a guest network](discovery.md#a-vpn-another-subnet-or-a-guest-network).
- **The light switched on at the wall.** A light that is off at the wall has no power and answers nothing. (A light that is off in the app,
  the way the protocol means it, still answers.)
- **The .NET 8 SDK or later.** The library targets `net8.0` and `net10.0`.

## Create a project and install the package

```shell
dotnet new console -n LightsHello
cd LightsHello
dotnet add package YeelightClient --prerelease
```

`--prerelease` is needed while 1.0 is in beta: a plain `dotnet add package YeelightClient` installs the latest stable version, and there
is none yet. You can also name the version: `--version 1.0.0-beta.1`. The package has one dependency,
`Microsoft.Extensions.Logging.Abstractions`, which comes with it.

## Find a light and switch it

Replace the content of `Program.cs`:

```csharp
using Yeelight;

await using var client = new YeelightClient();

Console.WriteLine("Searching for four seconds...");
var lights = await client.DiscoverAsync(TimeSpan.FromSeconds(4));

if (lights.Count == 0)
{
    Console.WriteLine("No light answered. Is LAN Control switched on for it?");
    return;
}

foreach (var light in lights)
{
    Console.WriteLine($"{light.Id}: model {light.Model}");
    Console.WriteLine($"  {(light.IsOn ? "on" : "off")}, brightness {light.Brightness}%");
}

var first = lights[0];
try
{
    await first.ToggleAsync();
    Console.WriteLine($"Toggled {first.Id}.");
}
catch (YeelightException ex)
{
    Console.WriteLine($"The light did not do it: {ex.Message}");
}
```

Run it with `dotnet run`. With one colour bulb on the network, the output looks like this:

```text
Searching for four seconds...
0x000000000015243f: model color
  on, brightness 100%
Toggled 0x000000000015243f.
```

Each line shows the [`Id`](xref:Yeelight.YeelightDevice.Id) of a light (the only thing that names it), its [`Model`](xref:Yeelight.YeelightDevice.Model), and the
state that it told in its answer to the search. The light that you toggled is now on if it was off, and off if it was on.

> [!NOTE]
> **Nothing is sent to the network until you ask.** Making a [`YeelightClient`](xref:Yeelight.YeelightClient) sends nothing.
> [`DiscoverAsync`](xref:Yeelight.YeelightClient.DiscoverAsync*) and [`StartDiscovery`](xref:Yeelight.YeelightClient.StartDiscovery*) send
> UDP searches, [`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*) sends one to the address that you give it, and
> [`StartListening`](xref:Yeelight.YeelightClient.StartListening*) only listens. After that the client talks to a light at the address that the
> light announced itself from. It never scans a network.

## What just happened

- `DiscoverAsync` sent a search: a UDP message to the multicast group of the lights and as a broadcast, three times, a second apart, from
  every network of the computer. It collected the answers for four seconds and returned the lights that answered. Why four seconds: a light
  answers within a second, but one that does not hear the multicast group is only found by the second or third search, so wait at least
  three seconds. See [Finding lights](discovery.md).
- Each light that answered is a [`YeelightDevice`](xref:Yeelight.YeelightDevice), with what the light said about itself: its
  [`Id`](xref:Yeelight.YeelightDevice.Id), [`Model`](xref:Yeelight.YeelightDevice.Model), [`Location`](xref:Yeelight.YeelightDevice.Location),
  the methods that it supports, and its state (on or off, brightness, colour).
- The client connects to every light that it finds, reads all its properties, and keeps the connection up. That is how the properties of
  a device stay what the light says, whoever changes it: the Yeelight app, a remote, the switch on the wall. See
  [Events and state](events.md).
- [`ToggleAsync`](xref:Yeelight.YeelightLight.ToggleAsync*) sends the `toggle` command and returns when the light has answered. A command that
  the light refuses, or that gets no answer, throws a [`YeelightException`](xref:Yeelight.YeelightException). See
  [Errors and limits](errors-and-limits.md).
- `await using` disposes the client at the end of the program, which closes the connections. (`using` works too: `Dispose` does its work on
  the thread pool and waits for it, so it cannot deadlock a thread that has a synchronization context.)

## Next steps

- [Finding lights](discovery.md): searching, listening, asking one address, and what the client sends.
- [Controlling a light](controlling.md): every command, transitions, argument checks, and what the state of a light means.
- [Colour flows and scenes](flows-and-scenes.md): programs that a light runs by itself.
- [Music mode](music-mode.md): commands without the limit of 60 a minute.
- [Events and state](events.md): react to what happens to a light.
- [Errors and limits](errors-and-limits.md): failures, timeouts, and the limits that a light puts on connections and commands.
- [The test app](test-app.md): try every command by hand, against your own lights or against simulated ones.
- [A runnable sample](https://github.com/tinodo/yeelightclient/tree/main/samples/ConsoleSample) in the repository: it searches (or asks one
  address), shows what the lights do, and prints the changes it hears (`dotnet run --project samples/ConsoleSample`). CI builds it against the
  package on every change.
