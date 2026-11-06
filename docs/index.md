---
_layout: landing
title: A complete .NET client for Yeelight lights
_description: A complete cross-platform .NET client for Yeelight smart lights over the local network. Search, control, events and music mode of the Yeelight LAN protocol as typed, async C#.
---

<div class="oc-hero">
<div class="oc-hero-text">

<p class="oc-eyebrow">Yeelight LAN protocol &middot; .NET 8 and 10</p>

# A complete .NET client for Yeelight lights

Find Yeelight lights on your network, switch them, colour them and follow what they do, from any .NET application on Windows, Linux or
macOS. Every command of the Yeelight LAN protocol is a typed, async C# member, a light's state follows the light whoever changes it, and the
client stays within the limits of the lights (four connections, 60 commands a minute) by itself.

<div class="oc-actions">
<a class="btn btn-primary btn-lg" href="articles/getting-started.md">Get started</a>
<a class="btn btn-outline-secondary btn-lg" href="api/index.md">API reference</a>
<a class="btn btn-outline-secondary btn-lg" href="https://github.com/tinodo/yeelightclient"><i class="bi bi-github"></i> GitHub</a>
</div>

```shell
dotnet add package YeelightClient --prerelease
```

</div>
<div class="oc-hero-code">

```csharp
using Yeelight;

await using var client = new YeelightClient();

var lights = await client.DiscoverAsync(TimeSpan.FromSeconds(4));

foreach (var light in lights)
{
    await light.TurnOnAsync();
    await light.SetBrightnessAsync(60, TimeSpan.FromSeconds(1));

    if (light.SupportsColor)
    {
        await light.SetRgbAsync(255, 140, 0);
    }
}
```

</div>
</div>

<div class="oc-banner">
<i class="bi bi-info-circle"></i>
<div>
<strong>1.0 is in beta.</strong> This site documents Yeelight Client 1.0.0-beta.1, the first release. It is for trying out: the public API can
still change before 1.0.0. Install it with <code style="white-space: nowrap">--prerelease</code>, and please report what you find in an
<a href="https://github.com/tinodo/yeelightclient/issues">issue</a>.
</div>
</div>

<div class="oc-section">

## Why Yeelight Client

<div class="oc-cards">

<div class="oc-card">
<i class="bi bi-broadcast"></i>
<h3>Finds lights on real networks</h3>
<p>A search goes out as a multicast and as a broadcast, three times, from every network of the computer. Announcements are heard too, and one address can be asked directly, across a VPN or on another subnet.</p>
</div>

<div class="oc-card">
<i class="bi bi-braces"></i>
<h3>The whole protocol, typed and async</h3>
<p>Power, brightness, colour temperature, RGB, HSV, colour flows, scenes, timers, adjustments and background lights: a method for every command of the specification, with the arguments checked before anything is sent.</p>
</div>

<div class="oc-card">
<i class="bi bi-lightbulb"></i>
<h3>A state that follows the light</h3>
<p>A light reports every change to its connections, so its properties are what it says, whoever changes it: the app, a remote, the wall switch. <code>INotifyPropertyChanged</code> and events tell you when.</p>
</div>

<div class="oc-card">
<i class="bi bi-music-note-beamed"></i>
<h3>Music mode for streaming effects</h3>
<p>Put a light in music mode and send it commands without the limit of 60 a minute, to follow music or a screen. The client runs the server that the light connects to.</p>
</div>

<div class="oc-card">
<i class="bi bi-arrow-repeat"></i>
<h3>A connection you can rely on</h3>
<p>One connection per light that is made again when it is lost, commands that fail at once instead of hanging, a limit of 60 commands a minute that the client keeps for you, and only one of the four connections that a light takes.</p>
</div>

<div class="oc-card">
<i class="bi bi-shield-check"></i>
<h3>Tested against a simulated light</h3>
<p>The tests run against a simulated light on the loopback, and add property-based tests, fuzzing of what a light could send, and a chaos test that cuts, stalls and refuses the connection at random while commands run.</p>
</div>

</div>
</div>

<div class="oc-section">

## A quick tour

#### [Find lights](#tab/find)

```csharp
using Yeelight;

await using var client = new YeelightClient();

// Search for four seconds ...
var lights = await client.DiscoverAsync(TimeSpan.FromSeconds(4));

// ... or ask one address, which also works across a VPN or on another subnet.
var light = await client.ProbeAsync("192.168.1.50");
```

#### [Control a light](#tab/control)

```csharp
await light.TurnOnAsync();
await light.SetBrightnessAsync(60, TimeSpan.FromSeconds(1));   // smooth, over one second
await light.SetColorTemperatureAsync(2700);
await light.SetRgbAsync(255, 140, 0);
await light.Background.SetRgbAsync(0, 128, 128);   // the second light of a ceiling light
```

#### [React to changes](#tab/events)

```csharp
client.DeviceDiscovered += (_, e) => Console.WriteLine($"Found {e.Device}");

client.DeviceStateChanged += (_, e) =>
    Console.WriteLine($"{e.Device.Name}: {e.PropertyName} (on: {e.Light.IsOn})");

client.StartDiscovery();   // a search now, and one every minute
```

#### [Colour flow](#tab/flow)

```csharp
var sunrise = new ColorFlow(
[
    ColorFlowStep.ColorTemperature(1700, TimeSpan.FromSeconds(1), brightness: 1),
    ColorFlowStep.ColorTemperature(2700, TimeSpan.FromMinutes(5), brightness: 60),
    ColorFlowStep.ColorTemperature(5000, TimeSpan.FromMinutes(5), brightness: 100),
],
count: 3,
action: ColorFlowAction.Stay);

await light.SetSceneAsync(YeelightScene.Flow(sunrise));   // the light runs it by itself
```

#### [Music mode](#tab/music)

```csharp
await using var music = await light.StartMusicModeAsync();

for (var hue = 0; hue < 360; hue += 6)
{
    await music.SetHsvAsync(hue, 100);   // no answer to wait for, and no limit of 60 a minute
    await Task.Delay(50);
}
```

---

</div>

<div class="oc-section">

## Compatibility

| Area | Support |
| --- | --- |
| **Lights** | Yeelight WiFi lights that follow the [Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf), with **LAN Control** switched on in the Yeelight app (select the light, settings, LAN Control). What a light can do is what it lists: a mono light has no colours, and only some ceiling lights have a background light. |
| **Target frameworks** | `net8.0` and `net10.0`, the LTS releases of .NET. An application on .NET 9, or on a later version, uses them too. |
| **Platforms** | Windows, Linux and macOS: the client only uses sockets, and its tests run on all three in CI. |
| **Dependencies** | `Microsoft.Extensions.Logging.Abstractions` 8.0.0, which has the `ILogger` that the client logs to. |
| **License** | [MIT](https://github.com/tinodo/yeelightclient/blob/main/LICENSE) |
| **Affiliation** | Yeelight Client is an independent open source project. It is not affiliated with or endorsed by Yeelight. |

</div>

<div class="oc-section">

## Where next

<div class="oc-cards oc-cards-4">

<a class="oc-card oc-card-link" href="articles/getting-started.md">
<i class="bi bi-rocket-takeoff"></i>
<h3>Getting started</h3>
<p>Switch on LAN Control, install the package, find a light and switch it.</p>
</a>

<a class="oc-card oc-card-link" href="articles/discovery.md">
<i class="bi bi-broadcast"></i>
<h3>Finding lights</h3>
<p>Searching, listening and probing, and what to do on a VPN or another subnet.</p>
</a>

<a class="oc-card oc-card-link" href="articles/test-app.md">
<i class="bi bi-window-stack"></i>
<h3>The test app</h3>
<p>A Windows app to try every command by hand, against your own lights or against simulated ones.</p>
</a>

<a class="oc-card oc-card-link" href="api/index.md">
<i class="bi bi-book"></i>
<h3>API reference</h3>
<p>Every type and member of the library, generated from the source.</p>
</a>

</div>
</div>
