---
title: Music mode
---

# Music mode

A light takes 60 commands a minute on a connection, which is not enough to follow music or the colours of a screen. In **music mode** the
roles are reversed: your computer runs a TCP server and tells the light where it is, and the light connects to it. Over that connection the
light carries out the commands that it gets as they come, **without answering them and without a quota**, and it reports no changes of its
properties. The client does the work of it: [`StartMusicModeAsync`](xref:Yeelight.YeelightDevice.StartMusicModeAsync*) returns a
[`YeelightMusicSession`](xref:Yeelight.YeelightMusicSession) that you send commands to.

```csharp
await light.TurnOnAsync();

await using var music = await light.StartMusicModeAsync();
await music.SetRgbAsync(255, 0, 0);
await music.SetRgbAsync(0, 0, 255);
```

## Starting music mode

`StartMusicModeAsync` does this:

1. It makes sure that there is a control connection to the light, and makes one when there is none.
2. It opens a TCP listener on the address of this computer that the light can reach and on a port (see the parameters below).
3. It sends `set_music` over the control connection, with that address and port.
4. It waits up to five seconds for the light to connect, and accepts only a connection that comes **from the address of the light**. Any other
   connection to that port is closed, and the wait goes on.
5. It closes the listener, so the port is only open while the light is connecting, and returns the session.

| Parameter | Default | Meaning |
| --- | --- | --- |
| `localAddress` | `null` | The address of this computer that the light connects to. `null` is the address that this computer uses to talk to the light, which is the right one on a network with one route to the light. Give it when the computer has more than one address on the network of the light, or when the address of the control connection is not the one that the light can reach. |
| `port` | `0` | The TCP port to listen on. `0` is a free port that the operating system chooses, and so another one each time; give a port when your firewall has to name it. |
| `cancellationToken` | none | Ends the wait. |

The light has to be on a network from which it can reach the computer, and **the firewall of the computer has to let the connection of the
light in**. When the light does not connect within five seconds, the start fails with a
[`YeelightException`](xref:Yeelight.YeelightException) of the kind [`NotConnected`](xref:Yeelight.YeelightFailureKind), and a message that asks
whether the firewall lets the light in.

A light that was told to connect and never did would stay in music mode, and report no changes. So when the light had accepted `set_music` and then
did not connect, or you cancelled the wait for it to connect, or the light did not answer `set_music` in time, the client sends `set_music` with 0 over
the control connection. That is a best effort: it waits for two seconds at most, and a failure of it is only logged (at the debug level). When the
light refused `set_music` (or the command was never sent), nothing is sent, because the light is not in music mode.

| Exception | When |
| --- | --- |
| `NotSupportedException` | The light does not list `set_music`: look at [`SupportsMusicMode`](xref:Yeelight.YeelightDevice.SupportsMusicMode). |
| `YeelightException` | The light refused the command, there is no control connection, or the light did not connect in five seconds. |
| `ArgumentOutOfRangeException` | `port` is not 0 to 65535. |
| `OperationCanceledException` | The wait was cancelled. |

## Commands in music mode

The session has the commands that are used to follow something: [`SetPowerAsync`](xref:Yeelight.YeelightMusicSession.SetPowerAsync*),
[`ToggleAsync`](xref:Yeelight.YeelightMusicSession.ToggleAsync*), [`SetBrightnessAsync`](xref:Yeelight.YeelightMusicSession.SetBrightnessAsync*),
[`SetColorTemperatureAsync`](xref:Yeelight.YeelightMusicSession.SetColorTemperatureAsync*), [`SetRgbAsync`](xref:Yeelight.YeelightMusicSession.SetRgbAsync*)
(as three numbers or as a `Color`) and [`SetHsvAsync`](xref:Yeelight.YeelightMusicSession.SetHsvAsync*), with the transitions and the argument checks
of the same commands on a [light](controlling.md#arguments-are-checked-before-anything-is-sent). For anything else, including the commands of
the background light, there is [`SendCommandAsync`](xref:Yeelight.YeelightMusicSession.SendCommandAsync*), which sends the method as you name it:

```csharp
await music.SendCommandAsync("bg_set_rgb", [0xFF0000, "sudden", 0]);   // background light, red
```

The differences with the commands of a light follow from what music mode is:

- **A command returns when it has been sent**, not when the light has done it: there is no answer. A command that the light refuses goes
  unnoticed, so check your values and switch the light on first.
- **A command only fails when the connection does**, with a [`YeelightException`](xref:Yeelight.YeelightException) of the kind
  [`ConnectionLost`](xref:Yeelight.YeelightFailureKind). A session that has ended, or is disposed, throws it too (a disposed session throws an
  `ObjectDisposedException`).
- **The client does not hold music mode commands back.** The specification says that no quota is checked in music mode. How fast a light can
  follow was not measured here: the examples on this page send 20 commands a second, and a light that lags wants fewer.
- **The properties of the light do not follow.** The light reports no changes in music mode, so [`IsOn`](xref:Yeelight.YeelightLight.IsOn),
  [`Brightness`](xref:Yeelight.YeelightLight.Brightness) and the others keep what they had. Call [`RefreshAsync`](xref:Yeelight.YeelightDevice.RefreshAsync*)
  when the session has ended.

## Ending music mode

```csharp
await using var music = await light.StartMusicModeAsync();
// ...
// the end of the scope disposes the session
```

[`DisposeAsync`](xref:Yeelight.YeelightMusicSession.DisposeAsync*) ends the session: it closes the connection, which makes the light leave music
mode, and it also tells the light on the control connection (`set_music` with 0), when there is one; a failure of that is ignored. A session ends
by itself when the light closes the connection, or the connection fails, and when the client is disposed or the light is forgotten with
[`RemoveDeviceAsync`](xref:Yeelight.YeelightClient.RemoveDeviceAsync*): the client then tells the light `set_music` with 0 before it closes the
control connection. [`Completion`](xref:Yeelight.YeelightMusicSession.Completion) is a task that ends when the session ends, and
[`IsConnected`](xref:Yeelight.YeelightMusicSession.IsConnected) tells whether the session is open:

```csharp
try
{
    await music.SetRgbAsync(255, 0, 0);
}
catch (YeelightException ex) when (ex.Kind == YeelightFailureKind.ConnectionLost)
{
    Console.WriteLine("The light left music mode.");
}

await music.Completion;   // ends when the session has ended, for whatever reason
```

Dispose the session when you are done. Disposing the client ends it too.

## Recipes

**A rainbow.** Sixty hues, 50 milliseconds apart:

```csharp
await light.TurnOnAsync();

await using var music = await light.StartMusicModeAsync();
for (var hue = 0; hue < 360; hue += 6)
{
    await music.SetHsvAsync(hue, 100);
    await Task.Delay(50);
}
```

**A light that follows a value.** A loop that reads a value between 0 and 1, such as the level of a sound or the load of a processor, 20 times a
second, and sets the brightness to it. It ends when you cancel it, or when the light ends the session:

```csharp
static async Task FollowAsync(
    YeelightDevice light, Func<double> level, CancellationToken cancellationToken)
{
    await light.TurnOnAsync(cancellationToken: cancellationToken);
    await using var music =
        await light.StartMusicModeAsync(cancellationToken: cancellationToken);

    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
    while (music.IsConnected && await timer.WaitForNextTickAsync(cancellationToken))
    {
        var brightness = 1 + (int)(Math.Clamp(level(), 0, 1) * 99);
        await music.SetBrightnessAsync(brightness, cancellationToken: cancellationToken);
    }
}
```

## Things to know

- **One session for a light.** `set_music` names one address and one port, so a light has one music connection. End a session before you start
  another for the same light; what a light does when it is asked twice is not specified and was not measured here.
- **Only the light is accepted.** The client accepts a connection to the music port from the address of the light only, for at most five
  seconds. Anything else that connects is closed.
- **Set `localAddress` on a computer with several addresses.** The default is the address that the control connection to the light uses. When the
  light reaches your computer by another address (another adapter, or through a router that translates addresses), name that one. A light
  that cannot reach the computer at all, because it is behind a translation that does not forward the connection, cannot use music mode.
- **The connection that the light makes is another one.** Whether it counts against the four connections that a light takes is not stated
  in the specification, and was not measured here. The control connection of the client stays.
- **No encryption and no authentication.** Like the rest of the protocol: use it on a network that you trust.
