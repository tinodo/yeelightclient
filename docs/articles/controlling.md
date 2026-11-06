---
title: Controlling a light
---

# Controlling a light

A [`YeelightDevice`](xref:Yeelight.YeelightDevice) is a light that the client found (see [Finding lights](discovery.md)). Every command is an
`async` method that returns when the light has answered, and throws a [`YeelightException`](xref:Yeelight.YeelightException) when it did not
succeed. This page lists the commands, what they accept, and what the state of a light means.

## The main light and the background light

`YeelightDevice` is the main light of a device. It derives from [`YeelightLight`](xref:Yeelight.YeelightLight), which has the state and the
commands of one light. A device that has a second light (some ceiling lights have a background light) offers it as
[`Background`](xref:Yeelight.YeelightDevice.Background), a [`YeelightBackgroundLight`](xref:Yeelight.YeelightBackgroundLight) with the same
commands and the same properties:

```csharp
await light.SetBrightnessAsync(80);                // the main light

if (light.SupportsBackgroundLight)
{
    await light.Background.SetBrightnessAsync(20);   // the background light
}
```

`Background` always exists as an object, but only a device that has a background light lists its methods: look at
[`SupportsBackgroundLight`](xref:Yeelight.YeelightDevice.SupportsBackgroundLight). The two lights share the connection of the device, and so
its limit of 60 commands a minute. What concerns the device as a whole (its name, its sleep timer, its connection, music mode) is only on the
device.

## The commands

### Power

```csharp
await light.TurnOnAsync();
await light.TurnOffAsync(TimeSpan.FromSeconds(2));   // fades out over two seconds
await light.SetPowerAsync(true, mode: PowerOnMode.ColorTemperature);   // on, in that mode
await light.ToggleAsync();      // on when it was off, off when it was on
await light.ToggleAllAsync();   // the main light and the background light together
```

[`TurnOnAsync`](xref:Yeelight.YeelightLight.TurnOnAsync*), [`TurnOffAsync`](xref:Yeelight.YeelightLight.TurnOffAsync*) and
[`SetPowerAsync`](xref:Yeelight.YeelightLight.SetPowerAsync*) take a [transition](#transitions). A light that is off is switched off by
software: it still has power, and still answers. [`ToggleAsync`](xref:Yeelight.YeelightLight.ToggleAsync*) flips the state without your having
to know it. [`ToggleAllAsync`](xref:Yeelight.YeelightDevice.ToggleAllAsync*) does that for the main light and the background light at the same
time.

[`PowerOnMode`](xref:Yeelight.PowerOnMode) says in which mode the light is switched on, and is only for switching on (asking for a mode when
switching off is an `ArgumentException`):

| Mode | The light is switched on |
| --- | --- |
| `Normal` | as it was before it was switched off (the default) |
| `ColorTemperature`, `Rgb`, `Hsv` | and put in that [colour mode](#colour-mode) |
| `ColorFlow` | and put in colour flow mode |
| `NightLight` | and put in night light mode (ceiling lights only) |

The mode is only sent when it is not `Normal`, because older firmware versions only know the three parameters before it.

### Brightness, colour temperature and colour

```csharp
using System.Drawing;

await light.SetBrightnessAsync(60);           // 1 to 100 percent
await light.SetColorTemperatureAsync(2700);   // 1700 to 6500 kelvin
await light.SetRgbAsync(255, 140, 0);         // red, green and blue, each 0 to 255
await light.SetHsvAsync(210, 80);             // hue 0 to 359, saturation 0 to 100

// A System.Drawing.Color works too (its alpha is ignored), with a transition.
await light.SetRgbAsync(Color.CornflowerBlue, TimeSpan.FromSeconds(1));
```

[`SetBrightnessAsync`](xref:Yeelight.YeelightLight.SetBrightnessAsync*), [`SetColorTemperatureAsync`](xref:Yeelight.YeelightLight.SetColorTemperatureAsync*),
[`SetRgbAsync`](xref:Yeelight.YeelightLight.SetRgbAsync*) and [`SetHsvAsync`](xref:Yeelight.YeelightLight.SetHsvAsync*) all take a
[transition](#transitions). A light that cannot show colours (a `mono` light has brightness only) does not list the colour commands: see
[What the light supports](#what-the-light-supports). The range of colour temperatures of a light depends on its model; the library only checks
the range of the protocol, 1700 to 6500 kelvin.

### Adjusting without knowing the current value

```csharp
await light.AdjustAsync(AdjustAction.Increase, AdjustProperty.Brightness);   // one step up
await light.AdjustAsync(AdjustAction.Circle, AdjustProperty.Color);          // the next colour
await light.AdjustBrightnessAsync(-20, TimeSpan.FromMilliseconds(500));      // 20 percent less
await light.AdjustColorTemperatureAsync(20);                                 // 20 percent more
await light.AdjustColorAsync();
```

[`AdjustAsync`](xref:Yeelight.YeelightLight.AdjustAsync*) is for controllers, such as a dial or a remote: it steps a property up or down without
knowing its value, and the size of a step is the light's own. `Circle` steps up, and starts again at the bottom when it reaches the top; it is
the only action that is valid for the colour. [`AdjustBrightnessAsync`](xref:Yeelight.YeelightLight.AdjustBrightnessAsync*) and
[`AdjustColorTemperatureAsync`](xref:Yeelight.YeelightLight.AdjustColorTemperatureAsync*) change the value by a percentage of what it is now,
from -100 to 100, over a duration (`null` is the shortest time that a light supports, 30 milliseconds). [`AdjustColorAsync`](xref:Yeelight.YeelightLight.AdjustColorAsync*) has no
amount: the protocol has a percentage there that the light ignores, and the client sends 0.

### Saving the state as the default

```csharp
await light.SetDefaultAsync();
```

[`SetDefaultAsync`](xref:Yeelight.YeelightLight.SetDefaultAsync*) saves the current state of the light in its memory, so that it shows that
state when it is switched on at the wall again after a loss of power.

### The name

```csharp
await light.SetNameAsync("desk");
```

[`SetNameAsync`](xref:Yeelight.YeelightDevice.SetNameAsync*) stores a name on the light (at most 64 bytes in UTF-8). The Yeelight app keeps
its own names in the cloud, so the two can differ. The specification suggests that a name with characters that are not ASCII is encoded with
Base64 first; the client sends the text as you give it and reports it as the light does, so decode it yourself if you encode it.
[`Name`](xref:Yeelight.YeelightDevice.Name) is what the light reports, and is empty when it has none.

### Timers

```csharp
await light.SetSleepTimerAsync(TimeSpan.FromMinutes(20));   // switch off in 20 minutes
TimeSpan? timer = await light.GetSleepTimerAsync();         // null when there is none
Console.WriteLine(light.SleepTimerRemaining);               // what the light reports
await light.CancelSleepTimerAsync();
```

A device has one sleep timer, and a new one replaces it. [`SetSleepTimerAsync`](xref:Yeelight.YeelightDevice.SetSleepTimerAsync*) takes whole
minutes (a part of a minute is rounded up), at least one, and is only accepted by a light that is on. The specification gives the range of the
remaining time that a light reports as 1 to 60 minutes, and no maximum for the timer itself: a light may refuse a longer one.
[`GetSleepTimerAsync`](xref:Yeelight.YeelightDevice.GetSleepTimerAsync*) returns the length that the light reports for its timer, and
[`SleepTimerRemaining`](xref:Yeelight.YeelightDevice.SleepTimerRemaining) is the time that is left, as the light reports it (`TimeSpan.Zero` when
there is no timer).
[`CancelSleepTimerAsync`](xref:Yeelight.YeelightDevice.CancelSleepTimerAsync*) stops it.

### Reading properties

```csharp
await light.RefreshAsync();   // all the properties

// Some of them, by their names in the protocol.
var values = await light.GetPropertiesAsync(["power", "bright"]);
Console.WriteLine($"{values["power"]}, {values["bright"]}%");
```

[`RefreshAsync`](xref:Yeelight.YeelightDevice.RefreshAsync*) reads all the properties that the protocol defines with one command, and takes
them over, so that the properties of the objects are what the light says now. [`GetPropertiesAsync`](xref:Yeelight.YeelightDevice.GetPropertiesAsync*)
reads the ones that you name, returns them as text, and takes them over as well. A name that the light does not know has an empty value.

### Colour flows, scenes and music mode

A colour flow is a program that a light runs by itself, and a scene sets a state directly, also on a light that is off: see
[Colour flows and scenes](flows-and-scenes.md). In music mode a light takes commands without the limit of 60 a minute: see
[Music mode](music-mode.md).

## Transitions

A command that changes what the light shows takes a `TimeSpan?` as its transition. `null`, or `TimeSpan.Zero`, is a sudden change; anything
else is a smooth change over that time, rounded to whole milliseconds, and at least
[`YeelightLimits.MinTransition`](xref:Yeelight.YeelightLimits.MinTransition) (30 milliseconds). A shorter one throws an
`ArgumentOutOfRangeException` before anything is sent.

```csharp
await light.SetBrightnessAsync(10);                              // sudden
await light.SetBrightnessAsync(100, TimeSpan.FromSeconds(3));    // smooth, over three seconds
```

[`AdjustBrightnessAsync`](xref:Yeelight.YeelightLight.AdjustBrightnessAsync*), [`AdjustColorTemperatureAsync`](xref:Yeelight.YeelightLight.AdjustColorTemperatureAsync*)
and [`AdjustColorAsync`](xref:Yeelight.YeelightLight.AdjustColorAsync*) take a duration with the same limit, and without a sudden effect to choose: none or zero is the
shortest time that a light supports (30 milliseconds, which is what is sent), anything else is at least 30 milliseconds. The specification gives a shortest time and no longest one, and a light was not tried here with a long one.

## Arguments are checked before anything is sent

An argument that a light would refuse is refused first, with an `ArgumentOutOfRangeException` that names the argument. Nothing is sent, and
nothing is counted against the limit of commands.

| Argument | Accepted | Constants |
| --- | --- | --- |
| Brightness | 1 to 100 | [`MinBrightness`](xref:Yeelight.YeelightLimits.MinBrightness), [`MaxBrightness`](xref:Yeelight.YeelightLimits.MaxBrightness) |
| Colour temperature | 1700 to 6500 kelvin | [`MinColorTemperature`](xref:Yeelight.YeelightLimits.MinColorTemperature), [`MaxColorTemperature`](xref:Yeelight.YeelightLimits.MaxColorTemperature) |
| Red, green, blue | 0 to 255 each | [`MaxRgb`](xref:Yeelight.YeelightLimits.MaxRgb) is the number that they make, `0xFFFFFF` |
| Hue, saturation | 0 to 359, 0 to 100 | [`MaxHue`](xref:Yeelight.YeelightLimits.MaxHue), [`MaxSaturation`](xref:Yeelight.YeelightLimits.MaxSaturation) |
| Transition | none or zero, or at least 30 milliseconds | [`MinTransition`](xref:Yeelight.YeelightLimits.MinTransition) |
| Percentage of an adjustment | -100 to 100 | [`MaxAdjustPercentage`](xref:Yeelight.YeelightLimits.MaxAdjustPercentage) |
| Sleep timer | at least one minute | |
| Name | at most 64 bytes in UTF-8 (an `ArgumentException`) | |

An argument that cannot be combined with another is an `ArgumentException`: a mode other than `Normal` for switching off, and an
[`AdjustAction`](xref:Yeelight.AdjustAction) other than `Circle` for [`AdjustProperty.Color`](xref:Yeelight.AdjustProperty). The steps of a
colour flow have their own limits: see [Colour flows and scenes](flows-and-scenes.md#limits).

## What the light supports

A light lists the methods that it supports in its answer to a search, and refuses any other. The client keeps the list:

```csharp
Console.WriteLine(string.Join(" ", light.SupportedMethods));    // set_power set_bright ...

if (light.Supports("set_rgb"))
{
    await light.SetRgbAsync(0, 128, 128);
}

Console.WriteLine($"colour: {light.SupportsColor}, white: {light.SupportsColorTemperature}");
Console.WriteLine($"background: {light.SupportsBackgroundLight}");
Console.WriteLine($"music mode: {light.SupportsMusicMode}");
```

- [`SupportedMethods`](xref:Yeelight.YeelightDevice.SupportedMethods) is the list (read-only), as the protocol names the methods (`set_power`, `set_rgb`,
  `bg_set_power`, ...). [`Supports`](xref:Yeelight.YeelightDevice.Supports*) asks about one.
- [`SupportsColor`](xref:Yeelight.YeelightDevice.SupportsColor) (it lists `set_rgb` or `set_hsv`),
  [`SupportsColorTemperature`](xref:Yeelight.YeelightDevice.SupportsColorTemperature),
  [`SupportsBackgroundLight`](xref:Yeelight.YeelightDevice.SupportsBackgroundLight) and
  [`SupportsMusicMode`](xref:Yeelight.YeelightDevice.SupportsMusicMode) answer the questions that come up most.
- A command whose method is not on the list throws a `NotSupportedException` before anything is sent.
- While the list is empty, which is until the light has said what it supports, `Supports` is `false` for everything, and the commands are not
  held back: a light that does not know a method answers `unsupported method` (a [`YeelightException`](xref:Yeelight.YeelightException)).
- Do not decide what a light can do from its [`Model`](xref:Yeelight.YeelightDevice.Model): the specification names five models (`mono`,
  `color`, `stripe`, `ceiling` and `bslamp`) and says that more will follow, and real lights say other things (`colora`, `strip6`). The list
  of methods is what the light itself says it can do.

## The state is what the light told

The properties of a light ([`IsOn`](xref:Yeelight.YeelightLight.IsOn), [`Brightness`](xref:Yeelight.YeelightLight.Brightness),
[`ColorMode`](xref:Yeelight.YeelightLight.ColorMode), [`ColorTemperature`](xref:Yeelight.YeelightLight.ColorTemperature),
[`Rgb`](xref:Yeelight.YeelightLight.Rgb), [`Hue`](xref:Yeelight.YeelightLight.Hue), [`Saturation`](xref:Yeelight.YeelightLight.Saturation),
[`IsFlowing`](xref:Yeelight.YeelightLight.IsFlowing), ...) are what the light told the client. A light reports every change to each connection
that it has, and the client reads all the properties when it connects, so they follow the light, whoever changes it: the Yeelight app, a
remote, the switch on the wall. See [Events and state](events.md) for the events.

**A command does not change a property by itself.** Its effect arrives as a report from the light, a moment later, and that can be after the
command has returned:

```csharp
await light.SetBrightnessAsync(50);
Console.WriteLine(light.Brightness);   // can still be the old value

await light.RefreshAsync();            // one more command: the light says what it is now
Console.WriteLine(light.Brightness);
```

[`PropertyChanged`](xref:Yeelight.YeelightLight.PropertyChanged) tells when the report arrives, without another command: see
[Events and state](events.md#waiting-for-a-light-to-do-it).

A property is `0` (or empty) until the light has reported it, and [`ColorMode`](xref:Yeelight.YeelightLight.ColorMode) is `Rgb` until then. A
light that was found by a search has told most of its state in its answer: on or off, brightness, colour mode and the colour values (the
specification lists them in the answer). The rest comes when the client has connected and read the properties. While music mode is on, the
light reports no changes.

## Colour mode

A light that can show colours does it in one of three modes, and [`ColorMode`](xref:Yeelight.ColorMode) says which. The mode decides which of the
colour properties is what the light shows; the others hold what they had, and mean nothing.

| `ColorMode` | The light shows | Valid properties |
| --- | --- | --- |
| `Rgb` | a colour | [`Rgb`](xref:Yeelight.YeelightLight.Rgb), and [`Color`](xref:Yeelight.YeelightLight.Color), which is the same colour as a `System.Drawing.Color` |
| `ColorTemperature` | a white | [`ColorTemperature`](xref:Yeelight.YeelightLight.ColorTemperature), in kelvin |
| `Hsv` | a colour | [`Hue`](xref:Yeelight.YeelightLight.Hue) and [`Saturation`](xref:Yeelight.YeelightLight.Saturation), together |

Setting a value switches the light to its mode: `SetRgbAsync` to `Rgb`, `SetColorTemperatureAsync` to `ColorTemperature` and `SetHsvAsync` to
`Hsv`. So read the property that belongs to the mode:

```csharp
string Describe(YeelightLight light)
{
    return light.ColorMode switch
    {
        ColorMode.ColorTemperature => $"{light.ColorTemperature} K",
        ColorMode.Rgb => $"colour #{light.Rgb:X6}",
        _ => $"hue {light.Hue}, saturation {light.Saturation}%",
    };
}
```

The background light has its own mode (the protocol calls it `bg_lmode`).

## Commands that need the light to be on

The specification says that these commands are only accepted by a light that is on. A light that is off answers with an error, which the
client throws as a [`YeelightException`](xref:Yeelight.YeelightException) of the kind [`DeviceRejected`](xref:Yeelight.YeelightFailureKind),
with the message of the light.

| Command | Method of the protocol |
| --- | --- |
| [`SetBrightnessAsync`](xref:Yeelight.YeelightLight.SetBrightnessAsync*) | `set_bright` |
| [`SetColorTemperatureAsync`](xref:Yeelight.YeelightLight.SetColorTemperatureAsync*) | `set_ct_abx` |
| [`SetRgbAsync`](xref:Yeelight.YeelightLight.SetRgbAsync*) | `set_rgb` |
| [`SetHsvAsync`](xref:Yeelight.YeelightLight.SetHsvAsync*) | `set_hsv` |
| [`SetDefaultAsync`](xref:Yeelight.YeelightLight.SetDefaultAsync*) | `set_default` |
| [`StartColorFlowAsync`](xref:Yeelight.YeelightLight.StartColorFlowAsync*) | `start_cf` |
| [`SetSleepTimerAsync`](xref:Yeelight.YeelightDevice.SetSleepTimerAsync*) | `cron_add` |

The same goes for their twins on the background light: the specification says that those work like the commands of the main light. For the
other commands the specification makes no such statement. A scene is the exception on the other side: it is accepted by a light that is on and
by one that is off, and switches the light on first. So these do the same:

```csharp
await light.TurnOnAsync();
await light.SetRgbAsync(255, 140, 0);

// One command: switches the light on when it is off, and sets a colour and a brightness.
await light.SetSceneAsync(YeelightScene.Rgb(255, 140, 0, 60));
```

## Cancelling a command

Every command takes a `CancellationToken` as its last parameter. Cancelling it ends the wait for the answer, and the command throws an
`OperationCanceledException`. A command that was already sent is not undone: the light may still carry it out. The connection stays usable.

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

try
{
    await light.SetBrightnessAsync(50, cancellationToken: cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Stopped waiting.");
}
```

A command that takes too long on its own is not cancelled, but fails with [`Timeout`](xref:Yeelight.YeelightFailureKind): see
[Errors and limits](errors-and-limits.md).

## Several commands at once

Commands can be sent from several threads at the same time. The client writes them one at a time over the connection of the light and matches
each answer to its command. They all count against the limit of 60 commands a minute, which the client keeps for you: see
[Errors and limits](errors-and-limits.md#the-limits-of-a-light).

## Any other command

[`SendCommandAsync`](xref:Yeelight.YeelightLight.SendCommandAsync*) sends a command by its method and parameters, as the protocol has them, for
what the typed methods do not cover. **Nothing is added to the method and nothing is checked**: no support check, no argument check, and no
`bg_` prefix (to command the background light with it, name the method `bg_set_power` yourself). The parameters are numbers, text and truth
values. It returns the answer of the light as a [`YeelightResult`](xref:Yeelight.YeelightResult), and does not throw for a command that the
light refused:

```csharp
var result = await light.SendCommandAsync("get_prop", ["power", "bright"]);

if (result.Success)
{
    Console.WriteLine($"power {result.GetString(0)}, brightness {result.GetString(1)}");
}
else
{
    Console.WriteLine($"Refused: {result.Error!.Message}");
}
```

Look at [`Success`](xref:Yeelight.YeelightResult.Success), or call [`EnsureSuccess`](xref:Yeelight.YeelightResult.EnsureSuccess*) to get the
exception that a typed command would have thrown. [`Values`](xref:Yeelight.YeelightResult.Values) holds the values of the answer as JSON elements.
