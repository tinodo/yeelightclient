---
title: Events and state
---

# Events and state

A light changes when you command it, and when somebody else does: the Yeelight app, a remote, the switch on the wall. The client tells you
with three kinds of notification, and keeps the properties of the lights up to date, so that you can also just read them.

| Notification | Raised when |
| --- | --- |
| [`YeelightClient.DeviceDiscovered`](xref:Yeelight.YeelightClient.DeviceDiscovered) | A light is found for the first time. |
| [`YeelightClient.DeviceStateChanged`](xref:Yeelight.YeelightClient.DeviceStateChanged) | A property of any light of the client changed. |
| [`YeelightLight.PropertyChanged`](xref:Yeelight.YeelightLight.PropertyChanged) | A property of that one light changed (`INotifyPropertyChanged`). |

## A light is found

```csharp
client.DeviceDiscovered += (_, e) => Console.WriteLine($"Found {e.Device}");
```

[`DeviceDiscovered`](xref:Yeelight.YeelightClient.DeviceDiscovered) is raised once for each light, when the client finds it for the first time, and it
is the first notification of that light: whatever happens to the light afterwards is raised after it.
The light is in [`Devices`](xref:Yeelight.YeelightClient.Devices) by then, and the client is connecting to it. The
[`Device`](xref:Yeelight.DeviceDiscoveredEventArgs.Device) of its arguments is the light. What the light said when it was found (its state in
the answer to a search) is the starting point and not a change: no `PropertyChanged` is raised for it. A light that you forgot with
[`RemoveDeviceAsync`](xref:Yeelight.YeelightClient.RemoveDeviceAsync*) and that is found again is a new object, and raises it again.

## A property changed

```csharp
client.DeviceStateChanged += (_, e) =>
{
    var which = e.Light is YeelightBackgroundLight ? "background light" : "light";
    Console.WriteLine($"{e.Device.Name}, {which}: {e.PropertyName}");
    Console.WriteLine($"  on: {e.Light.IsOn}, brightness: {e.Light.Brightness}%");
};
```

[`DeviceStateChanged`](xref:Yeelight.YeelightClient.DeviceStateChanged) is raised for every property of every light of the client, in one place.
Its arguments say which light, which part of it and which property:

- [`Device`](xref:Yeelight.DeviceStateChangedEventArgs.Device) is the device.
- [`Light`](xref:Yeelight.DeviceStateChangedEventArgs.Light) is the part of the device that changed: the device itself (its main light, and what
  concerns the device as a whole), or its [`Background`](xref:Yeelight.YeelightDevice.Background) light.
- [`PropertyName`](xref:Yeelight.DeviceStateChangedEventArgs.PropertyName) is the name of the property, for example `Brightness` or `IsConnected`.

A light has the same notification for itself. [`YeelightLight`](xref:Yeelight.YeelightLight) implements `INotifyPropertyChanged`, so a light can be
the source of a data binding, and you can listen to one light:

```csharp
light.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(YeelightLight.IsOn))
    {
        Console.WriteLine(light.IsOn ? "Switched on." : "Switched off.");
    }
};
```

For one change, the `PropertyChanged` of the light is raised first, and then `DeviceStateChanged`. A property is only raised when its value changed:
a report that says what the property was already does not raise anything.

**A notification says which property changed, and not what it changed to.** When the same property of the same light changes again while its
notification is still waiting for its turn, no second notification is queued: the handlers run once, and read the latest value. A handler that is
slower than a light therefore sees every property that changed at least once, with its latest value, and not every value that it had in between.
Read the property in the handler, and do not count notifications.

### Which properties there are

| Of | Properties |
| --- | --- |
| Every light (the main light, and the background light) | `IsOn`, `Brightness`, `ColorMode`, `ColorTemperature`, `Rgb`, `Color` (raised together with `Rgb`), `Hue`, `Saturation`, `IsFlowing`, `FlowParameters` |
| The device | `IsConnected`, `Name`, `Model`, `FirmwareVersion`, `SupportedMethods`, `Location`, `Address`, `Port`, `SleepTimerRemaining`, `IsMusicModeOn`, `NightLightBrightness`, `ActiveMode` |

[`LastSeen`](xref:Yeelight.YeelightDevice.LastSeen) is when anything was last heard from the device, and does not raise anything. The
`Supports...` properties are worked out from `SupportedMethods`; only `SupportedMethods` is raised, so read them again when it is.

### What a handler sees

A notification says that something changed, and the object says what it is now. The properties are read when the handler runs, and the
notification waited for its turn, so a property can have changed again by then, more than once: a handler sees the state of the light at that
moment, which is not always the state at the moment of the change that it was told about, and the values that the property had in between are
skipped. Read what you need in the handler, and do not rely on the order of the changes of two different properties.

A command does not change a property by itself. The change arrives as a report from the light a moment after the command, and can come after the
command has returned. See [The state is what the light told](controlling.md#the-state-is-what-the-light-told).

## Rules for handlers

The notifications are raised by a dispatcher of the client:

- **Handlers run one at a time, in the order in which the changes happened**, on a thread of the thread pool that is not the thread that reads
  from a light. A slow handler delays the events behind it, and nothing else: the answers to your commands are read all the same, so a handler
  can wait for the answer to a command. An `async` handler that awaits does not hold up the events behind it while it waits:

  ```csharp
  client.DeviceStateChanged += async (_, e) =>
  {
      try
      {
          if (e.Device == hallway && e.PropertyName == nameof(YeelightLight.IsOn) && e.Light.IsOn)
          {
              // The light of the porch follows the light of the hallway.
              await porch.TurnOnAsync();
          }
      }
      catch (YeelightException ex)
      {
          // Nothing catches an exception that leaves an async handler: it ends the process.
          Console.WriteLine($"Could not switch the porch light: {ex.Message}");
      }
  };
  ```

- **The queue stays small.** The notification of a change of a property carries only the name of the property, so it is queued once for as long
  as it waits for its turn (see [A property changed](#a-property-changed)). Once its handlers have started, a new change queues another one. The
  number of notifications that wait is therefore bounded by the number of lights and of their properties, however fast a light reports, and
  nothing that your application has to know about is dropped: only the values in between. The client logs a warning when 1000 notifications are
  waiting. Keep handlers short anyway, and do long work in a task of your own.
- **When you dispose the client, what is still waiting is dropped**, and a notification that is being raised stops before its next handler.
  Disposing does not wait for a handler that is running, and does not interrupt it, so a handler can still be running after it returned: a
  handler that uses something that you dispose together with the client has to check for that itself.
- **An exception in a handler does not harm the connection.** The client logs it (at the error level) and goes on with the other handlers and the
  next notification. The `try` and `catch` above is for the part of an `async` handler after its first `await`, which the client cannot see.
- **A notification is not guaranteed to come before the answer of the command that caused it.** `await light.SetBrightnessAsync(50)` can return
  before `Brightness` changed, or after: the two are on different threads.
- **A user interface has its own thread.** Hand the work over to it before you touch a control: see
  [Showing lights in a user interface](#showing-lights-in-a-user-interface).

## Waiting for a light to do it

When you have to act on the new state, you can read it again with [`RefreshAsync`](xref:Yeelight.YeelightDevice.RefreshAsync*), which costs one
more command. Or you can wait for the report, which costs nothing, with a helper that watches `PropertyChanged`:

```csharp
using System.ComponentModel;

static async Task<bool> WaitForAsync(
    YeelightLight light, Func<bool> condition, TimeSpan timeout)
{
    var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    void Check(object? sender, PropertyChangedEventArgs e)
    {
        if (condition())
        {
            reached.TrySetResult();
        }
    }

    light.PropertyChanged += Check;
    try
    {
        Check(null, new PropertyChangedEventArgs(null));   // it can be true already
        await reached.Task.WaitAsync(timeout);
        return true;
    }
    catch (TimeoutException)
    {
        return false;
    }
    finally
    {
        light.PropertyChanged -= Check;
    }
}
```

```csharp
await light.SetBrightnessAsync(50);

if (!await WaitForAsync(light, () => light.Brightness == 50, TimeSpan.FromSeconds(3)))
{
    Console.WriteLine("The light did not report 50 percent.");
}
```

The helper checks the condition each time that a notification of the light arrives, and a notification skips the values that the property had in
between (see [A property changed](#a-property-changed)). So wait for a state that stays, like the brightness that you just set, and not for a
value that the light only passes through.

## The connection

[`IsConnected`](xref:Yeelight.YeelightDevice.IsConnected) is whether there is a control connection to the device right now. It changes when the
connection is made and when it is lost, and raises the notifications like any other property.

- When a connection is lost, the commands that wait for their answer fail at once with
  [`ConnectionLost`](xref:Yeelight.YeelightFailureKind), and the client connects again by itself. The wait between two attempts starts at a
  second and doubles with every failure, up to 30 seconds; a connection that stayed up for ten seconds starts again from a second.
- When a connection is made, the client reads all the properties of the light again (unless
  [`RefreshOnConnect`](xref:Yeelight.YeelightClientOptions.RefreshOnConnect) is off), so what changed while the connection was down is noticed.
- A light that lost its power does not close its connection. The client finds out when the connection has been quiet for
  [`KeepAliveInterval`](xref:Yeelight.YeelightClientOptions.KeepAliveInterval), which is a minute by default: see
  [Errors and limits](errors-and-limits.md#the-limits-of-a-light).
- A light that the router gave another address is moved to it when its connection is down and the client hears the light at the new address: see
  [A light is known by its id](discovery.md#a-light-is-known-by-its-id).

## Showing lights in a user interface

The handlers run on a thread of the thread pool and never on the thread of your user interface, and a control can only be changed on its own
thread. Hand the work over, with the `DispatcherQueue` of WinUI 3, the `Dispatcher` of WPF, or, in any of them, the
`SynchronizationContext` that you capture on the UI thread:

```csharp
using System.Collections.ObjectModel;

var lights = new ObservableCollection<YeelightDevice>();
var ui = SynchronizationContext.Current;   // captured on the UI thread

client.DeviceDiscovered += (_, e) => ui?.Post(_ => lights.Add(e.Device), null);
```

A data binding to a property of a light updates when `PropertyChanged` is raised, and so on the thread that raises it. Whether the binding
engine of a framework accepts a notification from another thread is up to the framework: a binding in WinUI 3 that is updated from another
thread fails with a `COMException` ("The application called an interface that was marshalled for a different thread"). Do not depend on it. A
view model that you own, which listens to the light and raises its own `PropertyChanged` on the UI thread, works everywhere:

```csharp
using System.ComponentModel;

public sealed class LightViewModel : INotifyPropertyChanged
{
    private readonly YeelightLight _light;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current
        ?? throw new InvalidOperationException("Make the view model on the UI thread.");

    public LightViewModel(YeelightLight light)
    {
        _light = light;
        light.PropertyChanged += (_, e) => _ui.Post(Raise, e.PropertyName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOn => _light.IsOn;

    public int Brightness => _light.Brightness;

    public Task ToggleAsync() => _light.ToggleAsync();

    private void Raise(object? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs((string?)name));
}
```

The properties of the view model carry the names of the properties of the light, so a binding to `Brightness` follows it. Bind a command to
`ToggleAsync`, and show what a failed command throws, in the way that your framework does.
