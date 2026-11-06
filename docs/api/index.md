---
title: API reference
---

# API reference

This reference is generated from the XML documentation in the source of the library. Every public member has a description, the exceptions that
it throws and, where it matters, the limits of the protocol that it keeps to.

## Start here

| Type | What it is |
| --- | --- |
| [`YeelightClient`](xref:Yeelight.YeelightClient) | The client: it finds the lights, keeps the list of them and a connection to each, and raises the events. |
| [`YeelightDevice`](xref:Yeelight.YeelightDevice) | A light that was found: its identity, its state, the commands of its main light, and what concerns the device as a whole (its name, its timer, its connection, music mode). |
| [`YeelightLight`](xref:Yeelight.YeelightLight) | The state and the commands of one light. `YeelightDevice` is one, and so is its background light. |
| [`YeelightBackgroundLight`](xref:Yeelight.YeelightBackgroundLight) | The second light of a device that has one (some ceiling lights): the same commands and properties, from [`YeelightDevice.Background`](xref:Yeelight.YeelightDevice.Background). |
| [`YeelightMusicSession`](xref:Yeelight.YeelightMusicSession) | A light in [music mode](../articles/music-mode.md): commands without the limit of 60 a minute. |
| [`ColorFlow`](xref:Yeelight.ColorFlow), [`ColorFlowStep`](xref:Yeelight.ColorFlowStep), [`YeelightScene`](xref:Yeelight.YeelightScene) | [Colour flows and scenes](../articles/flows-and-scenes.md). |
| [`YeelightException`](xref:Yeelight.YeelightException), [`YeelightResult`](xref:Yeelight.YeelightResult) | What a failed command throws, and the answer that `SendCommandAsync` returns. See [Errors and limits](../articles/errors-and-limits.md). |
| [`YeelightClientOptions`](xref:Yeelight.YeelightClientOptions) | The settings of a client: connecting, timeouts, the limit of commands, and how lights are searched for. |
| [`YeelightLimits`](xref:Yeelight.YeelightLimits) | The limits of the protocol: ranges of values, the shortest transition, the connections and commands that a light accepts. |

## The namespace

Everything is in one namespace, [`Yeelight`](xref:Yeelight).

| Contains | Types |
| --- | --- |
| The client and the lights | `YeelightClient`, `YeelightDevice`, `YeelightLight`, `YeelightBackgroundLight`, `YeelightMusicSession` |
| Flows and scenes | `ColorFlow`, `ColorFlowStep`, `YeelightScene` |
| Options and limits | `YeelightClientOptions`, `YeelightLimits` |
| Failures | `YeelightException`, `YeelightResult`, `YeelightError`, and the enumeration `YeelightFailureKind` |
| Events | `DeviceDiscoveredEventArgs`, `DeviceStateChangedEventArgs` |
| Enumerations | [`ColorMode`](xref:Yeelight.ColorMode), [`PowerOnMode`](xref:Yeelight.PowerOnMode), [`AdjustAction`](xref:Yeelight.AdjustAction), [`AdjustProperty`](xref:Yeelight.AdjustProperty), [`ColorFlowAction`](xref:Yeelight.ColorFlowAction), [`ColorFlowMode`](xref:Yeelight.ColorFlowMode), [`ActiveMode`](xref:Yeelight.ActiveMode), [`YeelightFailureKind`](xref:Yeelight.YeelightFailureKind) |

## Looking for a command?

The commands are `async` methods of the lights, named after what they do. The commands of one light are on
[`YeelightLight`](xref:Yeelight.YeelightLight), for example [`SetBrightnessAsync`](xref:Yeelight.YeelightLight.SetBrightnessAsync*) and
[`StartColorFlowAsync`](xref:Yeelight.YeelightLight.StartColorFlowAsync*), and a [`YeelightDevice`](xref:Yeelight.YeelightDevice) and its
[`Background`](xref:Yeelight.YeelightDevice.Background) light both have them. What concerns the whole device is on `YeelightDevice`, for example
[`SetNameAsync`](xref:Yeelight.YeelightDevice.SetNameAsync*), [`SetSleepTimerAsync`](xref:Yeelight.YeelightDevice.SetSleepTimerAsync*) and
[`StartMusicModeAsync`](xref:Yeelight.YeelightDevice.StartMusicModeAsync*). Finding lights is on
[`YeelightClient`](xref:Yeelight.YeelightClient): [`DiscoverAsync`](xref:Yeelight.YeelightClient.DiscoverAsync*) and
[`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*).

Use the search box at the top of the page, or open the class and look through its members. [The articles](../articles/getting-started.md) explain
the concepts, [The protocol at a glance](../articles/protocol.md) lists the member that sends each method of the protocol, and
[the test app](../articles/test-app.md) lets you try a command by hand.
