---
title: Colour flows and scenes
---

# Colour flows and scenes

A **colour flow** is a program that a light runs by itself: a list of steps, each of which changes the colour or the colour temperature
(and, with it, the brightness) over some time, or waits. The specification calls it the most powerful command of a light, and says that the
recommended scenes of Yeelight, a sunrise and a sunset among them, are made with it. A **scene** sets a state directly, and works on a light that
is off. Both are single commands, so they are the way to make an effect that lasts without sending a command for every change.

## Steps

A [`ColorFlowStep`](xref:Yeelight.ColorFlowStep) is made with one of its static methods:

```csharp
using System.Drawing;

var second = TimeSpan.FromSeconds(1);

var red = ColorFlowStep.Rgb(255, 0, 0, second);                    // red, over a second
var blue = ColorFlowStep.Rgb(Color.Blue, second, brightness: 40);  // blue, and 40 percent
var warm = ColorFlowStep.ColorTemperature(2700, 2 * second, brightness: 100);
var pause = ColorFlowStep.Sleep(5 * second);                       // nothing, for five seconds
```

| Method | The step |
| --- | --- |
| [`Rgb`](xref:Yeelight.ColorFlowStep.Rgb*) | changes the colour, as three numbers or as a `Color` |
| [`ColorTemperature`](xref:Yeelight.ColorFlowStep.ColorTemperature*) | changes the colour temperature, 1700 to 6500 kelvin |
| [`Sleep`](xref:Yeelight.ColorFlowStep.Sleep*) | does nothing for the duration |

The duration of a step is the time that the light takes to change gradually, or the time that it sleeps, and is at least
[`YeelightLimits.MinFlowStep`](xref:Yeelight.YeelightLimits.MinFlowStep) (50 milliseconds). The `brightness` of a step, 1 to 100, is the
brightness that the light changes to along with the colour; leave it out to keep the brightness as it is. A step reads back as
[`Duration`](xref:Yeelight.ColorFlowStep.Duration), [`Mode`](xref:Yeelight.ColorFlowStep.Mode) (a
[`ColorFlowMode`](xref:Yeelight.ColorFlowMode)), [`Value`](xref:Yeelight.ColorFlowStep.Value) (the colour as a number, or the kelvin) and
[`Brightness`](xref:Yeelight.ColorFlowStep.Brightness).

## A flow

```csharp
var flow = new ColorFlow(
[
    ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromSeconds(1), brightness: 100),
    ColorFlowStep.Rgb(0, 0, 255, TimeSpan.FromSeconds(1), brightness: 100),
    ColorFlowStep.Sleep(TimeSpan.FromSeconds(2)),
],
count: 6,
action: ColorFlowAction.RecoverState);

await light.StartColorFlowAsync(flow);
```

A [`ColorFlow`](xref:Yeelight.ColorFlow) has three things:

- **The steps**, at least one, as a read-only list ([`Steps`](xref:Yeelight.ColorFlow.Steps)): a flow does not change once it is made. They run in a
  circle: after the last step the first one comes again.
- **The [`Count`](xref:Yeelight.ColorFlow.Count)**: how many steps the light runs before the flow ends, and `0` for a flow that never ends.
  Every step that runs counts, a sleep too: the example in the specification has four steps (one of them a sleep) and a count of four, which
  makes one round. A flow of three steps and a count of six makes two rounds.
- **The [`Action`](xref:Yeelight.ColorFlow.Action)**, what the light does when the flow has ended, a
  [`ColorFlowAction`](xref:Yeelight.ColorFlowAction): `RecoverState` goes back to the state from before the flow, `Stay` stays in the state in
  which the flow ended, and `TurnOff` switches the light off.

[`StartColorFlowAsync`](xref:Yeelight.YeelightLight.StartColorFlowAsync*) starts it, and
[`StopColorFlowAsync`](xref:Yeelight.YeelightLight.StopColorFlowAsync*) stops the flow that is running. Starting needs a light that is on (see
[Commands that need the light to be on](controlling.md#commands-that-need-the-light-to-be-on)); what the light does after you stop a flow is
not specified, so look at its state. The background light has its own flow: `light.Background.StartColorFlowAsync(flow)`.

A flow is sent as one command, whatever its length: a `start_cf` with the count, the action and the steps as one text of four numbers per step
(duration in milliseconds, mode, value, brightness, where `-1` is no brightness). The flow above is `6,0,1000,1,16711680,100,1000,1,255,100,2000,7,0,0`.
[`ColorFlow.ToString`](xref:Yeelight.ColorFlow.ToString*) gives that form.

## Reading the flow that a light runs

A light reports whether it runs a flow, and which, whoever started it:

```csharp
var parsed = ColorFlow.TryParse(light.FlowParameters, out var running);

if (light.IsFlowing && parsed && running is not null)
{
    Console.WriteLine($"{running.Steps.Count} steps, then {running.Action}");
    foreach (var step in running.Steps)
    {
        Console.WriteLine(step);
    }
}
```

[`IsFlowing`](xref:Yeelight.YeelightLight.IsFlowing) is whether a flow runs, and [`FlowParameters`](xref:Yeelight.YeelightLight.FlowParameters) are
its parameters as the light reports them (empty when there is none, and only meaningful while a flow runs).
[`ColorFlow.TryParse`](xref:Yeelight.ColorFlow.TryParse*) reads them back into a `ColorFlow`, and returns `false` for a text that is not a flow.

## Scenes

A [`YeelightScene`](xref:Yeelight.YeelightScene) is a state that a light is set to directly, switching it on first when it is off:

```csharp
await light.SetSceneAsync(YeelightScene.ColorTemperature(2700, 40));   // warm white, 40 percent
await light.SetSceneAsync(YeelightScene.Rgb(255, 140, 0, 60));         // orange, 60 percent

// On at 30 percent, and off again after 20 minutes.
await light.SetSceneAsync(YeelightScene.AutoDelayOff(brightness: 30, minutes: 20));
```

| Method | The scene |
| --- | --- |
| [`Rgb`](xref:Yeelight.YeelightScene.Rgb*) | a colour (three numbers or a `Color`) at a brightness |
| [`Hsv`](xref:Yeelight.YeelightScene.Hsv*) | a colour as a hue and a saturation, at a brightness |
| [`ColorTemperature`](xref:Yeelight.YeelightScene.ColorTemperature*) | a white at a brightness |
| [`Flow`](xref:Yeelight.YeelightScene.Flow*) | starts a colour flow |
| [`AutoDelayOff`](xref:Yeelight.YeelightScene.AutoDelayOff*) | switches the light on at a brightness, and starts a sleep timer that switches it off after the minutes |

[`SetSceneAsync`](xref:Yeelight.YeelightLight.SetSceneAsync*) sends it. Unlike most commands, a scene is accepted by a light that is on and by one
that is off. That makes `YeelightScene.Flow(flow)` the way to start a flow on a light that may be off: it switches the light on and starts the
flow in one command, where `StartColorFlowAsync` needs a light that is on already.

## Recipes

**A sunrise.** From the dimmest warm white to full daylight, in two steps of five minutes. Three steps and a count of three make one round, and
the light stays at the last state:

```csharp
var sunrise = new ColorFlow(
[
    ColorFlowStep.ColorTemperature(1700, TimeSpan.FromSeconds(1), brightness: 1),
    ColorFlowStep.ColorTemperature(2700, TimeSpan.FromMinutes(5), brightness: 60),
    ColorFlowStep.ColorTemperature(5000, TimeSpan.FromMinutes(5), brightness: 100),
],
count: 3,
action: ColorFlowAction.Stay);

await light.SetSceneAsync(YeelightScene.Flow(sunrise));
```

**Police lights.** Red and blue, 300 milliseconds each, twenty changes, and then back to what the light showed. This needs a light that can show
colours (`light.SupportsColor`):

```csharp
var police = new ColorFlow(
[
    ColorFlowStep.Rgb(255, 0, 0, TimeSpan.FromMilliseconds(300), brightness: 100),
    ColorFlowStep.Rgb(0, 0, 255, TimeSpan.FromMilliseconds(300), brightness: 100),
],
count: 20,
action: ColorFlowAction.RecoverState);

await light.SetSceneAsync(YeelightScene.Flow(police));
```

**A candle.** Warm whites that vary in brightness, without an end (a count of 0). Stop it with `StopColorFlowAsync`:

```csharp
var candle = new ColorFlow(
[
    ColorFlowStep.ColorTemperature(1800, TimeSpan.FromMilliseconds(400), brightness: 45),
    ColorFlowStep.ColorTemperature(1900, TimeSpan.FromMilliseconds(250), brightness: 62),
    ColorFlowStep.ColorTemperature(1800, TimeSpan.FromMilliseconds(500), brightness: 38),
    ColorFlowStep.ColorTemperature(2000, TimeSpan.FromMilliseconds(300), brightness: 55),
    ColorFlowStep.ColorTemperature(1700, TimeSpan.FromMilliseconds(600), brightness: 30),
    ColorFlowStep.ColorTemperature(1900, TimeSpan.FromMilliseconds(350), brightness: 58),
]);

await light.SetSceneAsync(YeelightScene.Flow(candle));
// ...
await light.StopColorFlowAsync();
```

**A night light that fades out.** One step that takes twenty minutes to reach the lowest brightness, and then the light switches off:

```csharp
var fadeOut = new ColorFlow(
    [ColorFlowStep.ColorTemperature(2200, TimeSpan.FromMinutes(20), brightness: 1)],
    count: 1,
    action: ColorFlowAction.TurnOff);

await light.SetSceneAsync(YeelightScene.Flow(fadeOut));
```

The range of colour temperatures of a light depends on its model: a light that does not go down to 1700 kelvin may refuse the steps of these
recipes that ask for it, so use the lowest value that your light has.

## Limits

- A step lasts at least 50 milliseconds, its colour is 0 to `0xFFFFFF`, its colour temperature 1700 to 6500, its brightness 1 to 100 or left
  out, and a flow has at least one step and a count that is not negative. Anything else is an `ArgumentOutOfRangeException` or an
  `ArgumentException` when the step or the flow is made, before anything is sent.
- The specification gives no longest duration, no largest number of steps and no longest flow. The flow is one text in one command, and what a
  light accepts was not measured here. A light that refuses a flow answers with an error, which is thrown as a
  [`YeelightException`](xref:Yeelight.YeelightException) of the kind [`DeviceRejected`](xref:Yeelight.YeelightFailureKind).
- A flow made of steps that the light cannot show (colours for a light that has none) is not checked by the client: look at
  [`SupportsColor`](xref:Yeelight.YeelightDevice.SupportsColor) and [`SupportsColorTemperature`](xref:Yeelight.YeelightDevice.SupportsColorTemperature)
  first.
