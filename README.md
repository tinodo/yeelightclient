# Yeelight Client
[![NuGet](https://img.shields.io/nuget/vpre/YeelightClient.svg?logo=nuget)](https://www.nuget.org/packages/YeelightClient)
[![Downloads](https://img.shields.io/nuget/dt/YeelightClient.svg)](https://www.nuget.org/packages/YeelightClient)
[![Build and Test](https://github.com/tinodo/yeelightclient/actions/workflows/build.yml/badge.svg)](https://github.com/tinodo/yeelightclient/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/github/license/tinodo/yeelightclient.svg)](https://github.com/tinodo/yeelightclient/blob/main/LICENSE)
[![OpenSSF Scorecard](https://img.shields.io/ossf-scorecard/github.com/tinodo/yeelightclient?label=openssf%20scorecard)](https://scorecard.dev/viewer/?uri=github.com/tinodo/yeelightclient)

A complete cross platform .NET client for Yeelight smart lights: find them on your network, control them, and follow what they do.  
Currently implementing: the [**Yeelight WiFi Light Inter-Operation Specification**](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf) (the LAN control that every light has to switch on in the Yeelight app)  
Targets `net10.0`, the current LTS release of .NET (see [Compatibility](https://tinodo.github.io/yeelightclient/#compatibility)).  
Our intent is to create a **complete** and **easy to use** client for Yeelight lights.  
**Documentation, guides and the API reference: [tinodo.github.io/yeelightclient](https://tinodo.github.io/yeelightclient/)**

## Installation
Install from the [NuGet Gallery](https://www.nuget.org/packages/YeelightClient)   
Or through the NuGet CLI: `NuGet\Install-Package YeelightClient -Version 1.0.0-beta.2`  
From the command line: `dotnet add package YeelightClient --version 1.0.0-beta.2`  

**1.0.0-beta.2 is a prerelease**: it is meant to be tried, and its public API can still change before 1.0.0. A plain
`dotnet add package YeelightClient` installs the latest stable release, which there is none of yet, so name the version (or add `--prerelease`) to get it.
Please report what you find in an [issue](https://github.com/tinodo/yeelightclient/issues).

**Every light needs LAN Control.** In the Yeelight app: select the light, open its settings and switch on *LAN Control*. A light without it neither answers a search nor accepts a connection.

## Sample usage

### Simple

```
using Yeelight;

await using var client = new YeelightClient();

// Search the networks of this computer for four seconds.
var lights = await client.DiscoverAsync(TimeSpan.FromSeconds(4));

foreach (var light in lights)
{
    await light.TurnOnAsync();
    await light.SetBrightnessAsync(60, TimeSpan.FromSeconds(1));   // a smooth change, over one second
    await light.SetRgbAsync(255, 140, 0);
}
```

### A light that you know the address of

```
await using var client = new YeelightClient();

// One search, sent straight to that address: it also works across a VPN, or on another subnet.
var light = await client.ProbeAsync("192.168.1.50");
if (light is not null)
{
    await light.ToggleAsync();
}
```

### Follow the lights

```
await using var client = new YeelightClient();

client.DeviceDiscovered += (_, e) => Console.WriteLine($"Found {e.Device.Name} ({e.Device.Id})");
client.DeviceStateChanged += (_, e) => Console.WriteLine($"{e.Device.Name}: {e.PropertyName}, on: {e.Light.IsOn}, brightness: {e.Light.Brightness}%");

client.StartDiscovery();   // search now and every minute (client.StartListening() also takes over what the lights announce by themselves)
await Task.Delay(Timeout.Infinite);
```

The state of a light (`IsOn`, `Brightness`, `ColorMode`, `Rgb`, `ColorTemperature`, `Hue`, `Saturation`, ...) is what the light told: it reports every change to
the connection that the client keeps, whoever made it (the app of Yeelight, a remote, the switch on the wall).

### Colour flows, scenes and the background light

```
var sunrise = new ColorFlow(
[
    ColorFlowStep.ColorTemperature(1700, TimeSpan.FromSeconds(1), brightness: 1),
    ColorFlowStep.ColorTemperature(2700, TimeSpan.FromMinutes(5), brightness: 60),
    ColorFlowStep.ColorTemperature(5000, TimeSpan.FromMinutes(5), brightness: 100),
],
count: 3,
action: ColorFlowAction.Stay);

await light.StartColorFlowAsync(sunrise);
await light.SetSceneAsync(YeelightScene.AutoDelayOff(brightness: 30, minutes: 20));   // on at 30%, and off again after 20 minutes

if (light.SupportsBackgroundLight)
{
    await light.Background.SetRgbAsync(0, 128, 128);   // the same commands, for the second light of a ceiling light
}
```

### Music mode

A light in music mode takes commands without the limit of 60 a minute, which is what it takes to follow music or a screen. The light connects back to
this computer, and the client takes care of it:

```
await using var music = await light.StartMusicModeAsync();
for (var hue = 0; hue < 360; hue += 6)
{
    await music.SetHsvAsync(hue, 100);
    await Task.Delay(50);
}
```

### Failures

Every command returns when the light has answered, and throws a `YeelightException` when it did not succeed. An argument that a light would refuse
(a brightness of 101) is refused with an `ArgumentOutOfRangeException` first, and nothing is sent.

```
try
{
    await light.SetBrightnessAsync(50);
}
catch (YeelightException ex) when (ex.Kind == YeelightFailureKind.DeviceRejected)
{
    Console.WriteLine($"The light refused it: {ex.Error?.Message}");   // for example, it is off
}
catch (YeelightException ex)
{
    Console.WriteLine($"It did not get through: {ex.Message}");        // not connected, no answer in time, the connection was lost
}
```

## Good to know

- **Nothing is sent until you ask.** A client that is made does nothing. `DiscoverAsync`, `StartDiscovery`, `StartListening` and `ProbeAsync` are what use
  the network, and they send UDP searches (to the multicast group of the lights and as a broadcast, from every network of the computer, three times) and
  listen for the answers. The client never scans a network: it only talks to an address that a light announced itself from, and to the one you give it.
- **A light takes four connections and 60 commands a minute on each.** The client keeps one connection per light, keeps within the limit (a command waits for
  its turn, and fails with `YeelightFailureKind.RateLimited` when that would take too long), and a light that refuses with `client quota exceeded`
  is reported as it is. Music mode has no limit.
- **Events are raised by a dispatcher of the client**, one at a time and in the order in which they happened, on a thread that is not the one that reads from a light:
  a handler can wait for the answer to a command, and a slow handler only delays the events behind it. The event of a change says which property changed, not what it
  changed to, and a change that is still waiting when the same property changes again is raised once: read the property in the handler.
- **Every call can be cancelled.** Every command, `ConnectAsync`, `ProbeAsync` and `DiscoverAsync` take a `CancellationToken`: cancelling it ends the wait with an
  `OperationCanceledException` and leaves the connection usable. A command that was already sent is not undone.
- **A lost connection is made again.** `IsConnected` tells you when it is gone and when it is back, commands that were waiting fail at once with
  `YeelightFailureKind.ConnectionLost`, and the client connects again, with a wait that grows, until the light is back or the client is disposed.
- **A light is known by its id**, never by its name or its address: when the router gives a light a new address, the connection follows it.
- **The protocol of Yeelight has no authentication and no encryption.** Any computer on the network can control a light, and any computer can announce one. The client
  only takes an announcement that names the address that it was sent from, which has to be on the local network; it does not move a light that it is connected to
  to another address; and it keeps at most `YeelightClientOptions.MaxDevices` lights (1000 by default). That is as far as a client can go: use it on a network that you trust.
- **See what goes over the wire.** The client logs every message that it sends and receives at the debug level (`Sending: …`, `Received: …`) to the
  `ILogger<YeelightClient>` that you give it.

## Try it: the test app

[`YeelightClient.TestApp`](https://github.com/tinodo/yeelightclient/tree/main/YeelightClient.TestApp) is a Windows (WinUI 3) application to try every command of this library by hand
against your own lights (or against lights that it simulates, when you have none at hand), to watch the events, and to see what goes over the wire. Every
[release](https://github.com/tinodo/yeelightclient/releases) has it as a download (`YeelightClient.TestApp-<version>-win-x64.zip`: unzip it, start
`YeelightClient.TestApp.exe`), and every CI run publishes it as an artifact; see [CONTRIBUTING.md](https://github.com/tinodo/yeelightclient/blob/main/CONTRIBUTING.md#the-test-app).

## How it is tested

The tests run against a simulated light (`YeelightClient.Tests/TestSupport/SimulatedBulb.cs`): an in-process light on the loopback that answers a search over UDP and
speaks the control protocol over TCP the way the specification says, with a state that the commands change, reports of every change, the commands that only a light
that is on accepts, a quota on every connection, four connections at most and music mode. Nothing that a test does leaves the loopback. On top of that sit tests of the
exact messages that every command sends (compared with the specification), of what a hostile network neighbour can send, property-based and fuzz tests, and a
chaos test that cuts the connection at random while the client keeps working.

[![Build and Test](https://github.com/tinodo/yeelightclient/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/tinodo/yeelightclient/actions/workflows/build.yml)
[![CodeQL](https://github.com/tinodo/yeelightclient/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/tinodo/yeelightclient/actions/workflows/codeql.yml)
[![Docs](https://github.com/tinodo/yeelightclient/actions/workflows/docs.yml/badge.svg?branch=main)](https://tinodo.github.io/yeelightclient/)
[![NuGet](https://img.shields.io/nuget/v/yeelightclient.svg?style=flat)](https://www.nuget.org/packages/yeelightclient)  

Yeelight Client is an independent open source project. It is not affiliated with or endorsed by Yeelight.

See [CONTRIBUTING.md](https://github.com/tinodo/yeelightclient/blob/main/CONTRIBUTING.md) for how to build, test, and contribute, and [CHANGELOG.md](https://github.com/tinodo/yeelightclient/blob/main/CHANGELOG.md) for release history.
