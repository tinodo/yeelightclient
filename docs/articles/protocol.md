---
title: The protocol at a glance
---

# The protocol at a glance

Yeelight Client implements the [Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf),
which Yeelight publishes for software that wants to work with its lights. You do not need to know the protocol to use the library, but it
explains what the library sends, what the properties are called, and why some things are the way they are. This page is a summary and not a
copy: where it matters, read the specification itself.

The protocol has two parts:

- **Discovery** finds the lights: UDP messages in the style of SSDP, on port 1982, to a multicast group.
- **Control** is a TCP connection to a light, over which commands and their answers are exchanged as JSON, one message on each line.

The protocol has no authentication and no encryption. A light only takes these connections when **LAN Control** is switched on in the Yeelight app.

## Discovery messages

**A search** is sent by whoever wants to find lights, to the multicast group `239.255.255.250` on UDP port 1982. It is four lines, each ended with
CR LF. The `MAN` and `ST` headers are required (`ST` has to be `wifi_bulb`), the `HOST` header is optional, and a message that does not follow the
rules is dropped by the light without an answer, which looks like the absence of lights.

```text
M-SEARCH * HTTP/1.1
HOST: 239.255.255.250:1982
MAN: "ssdp:discover"
ST: wifi_bulb
```

On the lights that were measured, a search was answered the same with and without a closing empty line, and with and without an `MX` header.
They did not answer other search targets (`ssdp:all`, `yeelight`), or a search to port 1900, the port of standard SSDP.

**An answer** is sent by every light that hears a search, to the address and port that the search came from. It says where the control service of the
light is, who the light is, what it supports, and what it is doing (this is an example; the values are those of a made-up light):

```text
HTTP/1.1 200 OK
Cache-Control: max-age=3600
Location: yeelight://192.168.1.50:55443
Server: POSIX UPnP/1.0 YGLC/1
id: 0x00000000045d8e21
model: color
fw_ver: 24
support: get_prop set_default set_power toggle set_bright set_ct_abx set_rgb set_hsv set_name
power: on
bright: 80
color_mode: 2
ct: 2700
rgb: 16711680
hue: 0
sat: 100
name: desk
```

| Header | Means | In the library |
| --- | --- | --- |
| `Location` | Where the control service is: `yeelight://` with the address and the TCP port. | [`Location`](xref:Yeelight.YeelightDevice.Location), [`Address`](xref:Yeelight.YeelightDevice.Address), [`Port`](xref:Yeelight.YeelightDevice.Port) |
| `id` | The id of the light, which is what tells lights apart. | [`Id`](xref:Yeelight.YeelightDevice.Id) |
| `model` | The product model. The specification names `mono` (brightness only), `color`, `stripe`, `ceiling` and `bslamp`, and says that more will follow; real lights say others, for example `colora` and `strip6`. | [`Model`](xref:Yeelight.YeelightDevice.Model) |
| `fw_ver` | The firmware version. | [`FirmwareVersion`](xref:Yeelight.YeelightDevice.FirmwareVersion) |
| `support` | The methods that the light supports, separated by spaces. A light refuses any method that is not in it. | [`SupportedMethods`](xref:Yeelight.YeelightDevice.SupportedMethods) |
| `power` | `on` or `off`. Off is switched off by software, and not without power. | [`IsOn`](xref:Yeelight.YeelightLight.IsOn) |
| `bright` | The brightness, a percentage. | [`Brightness`](xref:Yeelight.YeelightLight.Brightness) |
| `color_mode` | `1` colour (RGB), `2` colour temperature, `3` HSV. | [`ColorMode`](xref:Yeelight.YeelightLight.ColorMode) |
| `ct`, `rgb`, `hue`, `sat` | The colour temperature, the colour as a number, the hue and the saturation. Each is only valid in its own colour mode. | [`ColorTemperature`](xref:Yeelight.YeelightLight.ColorTemperature), [`Rgb`](xref:Yeelight.YeelightLight.Rgb), [`Hue`](xref:Yeelight.YeelightLight.Hue), [`Saturation`](xref:Yeelight.YeelightLight.Saturation) |
| `name` | The name that was stored on the light with `set_name`. | [`Name`](xref:Yeelight.YeelightDevice.Name) |
| `Cache-Control` | `max-age` is the interval at which the light announces itself (3600 seconds in the example of the specification). | |

**An announcement** is sent by a light by itself, to the multicast group, right after it joined the network and then at a fixed interval. It starts with
`NOTIFY * HTTP/1.1`, has `NTS: ssdp:alive`, and the same headers as an answer. Whoever receives it must not answer it.

## Control messages

After discovery, a program connects to the TCP port of the `Location`. Every message is one JSON object on one line, ended with CR LF. A light
accepts four connections at once. There are three kinds of message.

**A command** goes to the light. It has an `id` that the sender chooses, a `method` and an array of `params`. The light accepts only the methods that it
listed in `support`.

```text
{"id":1,"method":"set_bright","params":[60,"smooth",1000]}
```

**A result** comes back with the same `id`: `result` is an array, with `"ok"` for a command that was carried out, or the values for `get_prop`.
When the command failed, there is an `error` with a `code` and a `message` instead. A light answers `-1` to everything that it refuses. A line that
has an `id` and neither a `result` array nor an `error` object is not an answer: the library ignores it (it is logged at the debug level), and
the command ends with a `Timeout`.

```text
{"id":1,"result":["ok"]}
{"id":2,"error":{"code":-1,"message":"unsupported method"}}
{"id":3,"method":"get_prop","params":["power","bright","not_a_property"]}
{"id":3,"result":["on","60",""]}
```

A property that the light does not know is an empty string in the result of `get_prop`.

**A notification** is sent by a light, to every connection that it has, whenever its state changes. Its method is `props`, and its `params` are the
properties that changed, with their values as text:

```text
{"method":"props","params":{"power":"on","bright":"75"}}
```

These are the properties of the protocol, and what the library calls them:

| Property | Values | In the library |
| --- | --- | --- |
| `power` | `on`, `off` | [`IsOn`](xref:Yeelight.YeelightLight.IsOn) |
| `bright` | 1 to 100 | [`Brightness`](xref:Yeelight.YeelightLight.Brightness) |
| `ct` | 1700 to 6500 kelvin | [`ColorTemperature`](xref:Yeelight.YeelightLight.ColorTemperature) |
| `rgb` | the colour as a number | [`Rgb`](xref:Yeelight.YeelightLight.Rgb), [`Color`](xref:Yeelight.YeelightLight.Color) |
| `hue`, `sat` | 0 to 359, 0 to 100 | [`Hue`](xref:Yeelight.YeelightLight.Hue), [`Saturation`](xref:Yeelight.YeelightLight.Saturation) |
| `color_mode` | 1 colour, 2 colour temperature, 3 HSV | [`ColorMode`](xref:Yeelight.YeelightLight.ColorMode) |
| `flowing`, `flow_params` | whether a colour flow runs, and its parameters | [`IsFlowing`](xref:Yeelight.YeelightLight.IsFlowing), [`FlowParameters`](xref:Yeelight.YeelightLight.FlowParameters) |
| `delayoff` | the remaining time of the sleep timer, in minutes | [`SleepTimerRemaining`](xref:Yeelight.YeelightDevice.SleepTimerRemaining) |
| `music_on` | 1 when music mode is on | [`IsMusicModeOn`](xref:Yeelight.YeelightDevice.IsMusicModeOn) |
| `name` | the name stored on the light | [`Name`](xref:Yeelight.YeelightDevice.Name) |
| `nl_br` | the brightness of the night light of a ceiling light | [`NightLightBrightness`](xref:Yeelight.YeelightDevice.NightLightBrightness) |
| `active_mode` | 0 daylight, 1 moonlight (ceiling lights) | [`ActiveMode`](xref:Yeelight.YeelightDevice.ActiveMode) |
| `bg_power`, `bg_bright`, `bg_ct`, `bg_rgb`, `bg_hue`, `bg_sat`, `bg_flowing`, `bg_flow_params` | the same, for the background light | the same properties of [`Background`](xref:Yeelight.YeelightDevice.Background) |
| `bg_lmode` | the colour mode of the background light | [`ColorMode`](xref:Yeelight.YeelightLight.ColorMode) of `Background` |

## The methods

The specification lists 35 methods. This is every one of them, and the member of the library that sends it. A command that changes what a light
shows takes an effect (`sudden` or `smooth`) and a duration in milliseconds, which the library makes of a
[transition](controlling.md#transitions).

### Main light and device

| Method | Parameters | Sent by | Notes |
| --- | --- | --- | --- |
| `get_prop` | the names of properties | [`GetPropertiesAsync`](xref:Yeelight.YeelightDevice.GetPropertiesAsync*), [`RefreshAsync`](xref:Yeelight.YeelightDevice.RefreshAsync*) (all the names) | The keep-alive uses it for `power`. |
| `set_power` | `on` or `off`, effect, duration, mode | [`SetPowerAsync`](xref:Yeelight.YeelightLight.SetPowerAsync*), [`TurnOnAsync`](xref:Yeelight.YeelightLight.TurnOnAsync*), [`TurnOffAsync`](xref:Yeelight.YeelightLight.TurnOffAsync*) | The mode is only sent when it is not `Normal`. |
| `toggle` | none | [`ToggleAsync`](xref:Yeelight.YeelightLight.ToggleAsync*) | |
| `set_bright` | brightness, effect, duration | [`SetBrightnessAsync`](xref:Yeelight.YeelightLight.SetBrightnessAsync*) | Light on. |
| `set_ct_abx` | kelvin, effect, duration | [`SetColorTemperatureAsync`](xref:Yeelight.YeelightLight.SetColorTemperatureAsync*) | Light on. |
| `set_rgb` | colour as a number, effect, duration | [`SetRgbAsync`](xref:Yeelight.YeelightLight.SetRgbAsync*) | Light on. |
| `set_hsv` | hue, saturation, effect, duration | [`SetHsvAsync`](xref:Yeelight.YeelightLight.SetHsvAsync*) | Light on. |
| `set_default` | none | [`SetDefaultAsync`](xref:Yeelight.YeelightLight.SetDefaultAsync*) | Light on. |
| `start_cf` | count, action, expression | [`StartColorFlowAsync`](xref:Yeelight.YeelightLight.StartColorFlowAsync*) | Light on. |
| `stop_cf` | none | [`StopColorFlowAsync`](xref:Yeelight.YeelightLight.StopColorFlowAsync*) | |
| `set_scene` | class, and values that depend on it | [`SetSceneAsync`](xref:Yeelight.YeelightLight.SetSceneAsync*) | Light on or off. The classes are `color`, `hsv`, `ct`, `cf` and `auto_delay_off`: see [`YeelightScene`](xref:Yeelight.YeelightScene). |
| `set_adjust` | action, property | [`AdjustAsync`](xref:Yeelight.YeelightLight.AdjustAsync*) | The colour can only be adjusted with `circle`. |
| `adjust_bright` | percentage, duration | [`AdjustBrightnessAsync`](xref:Yeelight.YeelightLight.AdjustBrightnessAsync*) | |
| `adjust_ct` | percentage, duration | [`AdjustColorTemperatureAsync`](xref:Yeelight.YeelightLight.AdjustColorTemperatureAsync*) | |
| `adjust_color` | percentage, duration | [`AdjustColorAsync`](xref:Yeelight.YeelightLight.AdjustColorAsync*) | The light ignores the percentage, and the library sends 0. |
| `cron_add` | type 0 (switch off), minutes | [`SetSleepTimerAsync`](xref:Yeelight.YeelightDevice.SetSleepTimerAsync*) | Light on. |
| `cron_get` | type 0 | [`GetSleepTimerAsync`](xref:Yeelight.YeelightDevice.GetSleepTimerAsync*) | |
| `cron_del` | type 0 | [`CancelSleepTimerAsync`](xref:Yeelight.YeelightDevice.CancelSleepTimerAsync*) | |
| `set_name` | the name | [`SetNameAsync`](xref:Yeelight.YeelightDevice.SetNameAsync*) | |
| `set_music` | 1 or 0, the address and the port of the server | [`StartMusicModeAsync`](xref:Yeelight.YeelightDevice.StartMusicModeAsync*) (1, and 0 when the light accepted it and did not connect), and disposing the [session](xref:Yeelight.YeelightMusicSession) (0) | See [Music mode](music-mode.md). |
| `dev_toggle` | none | [`ToggleAllAsync`](xref:Yeelight.YeelightDevice.ToggleAllAsync*) | Main light and background light. |

### Background light

The 14 methods of the background light have the name of the method of the main light with `bg_` in front, and the same parameters. The library
sends them from the members of [`Background`](xref:Yeelight.YeelightDevice.Background) that have the same name as the member of the main light:

| Method | Sent by (on `Background`) |
| --- | --- |
| `bg_set_power` | [`SetPowerAsync`](xref:Yeelight.YeelightLight.SetPowerAsync*), [`TurnOnAsync`](xref:Yeelight.YeelightLight.TurnOnAsync*), [`TurnOffAsync`](xref:Yeelight.YeelightLight.TurnOffAsync*) |
| `bg_toggle` | [`ToggleAsync`](xref:Yeelight.YeelightLight.ToggleAsync*) |
| `bg_set_bright` | [`SetBrightnessAsync`](xref:Yeelight.YeelightLight.SetBrightnessAsync*) |
| `bg_set_ct_abx` | [`SetColorTemperatureAsync`](xref:Yeelight.YeelightLight.SetColorTemperatureAsync*) |
| `bg_set_rgb` | [`SetRgbAsync`](xref:Yeelight.YeelightLight.SetRgbAsync*) |
| `bg_set_hsv` | [`SetHsvAsync`](xref:Yeelight.YeelightLight.SetHsvAsync*) |
| `bg_set_default` | [`SetDefaultAsync`](xref:Yeelight.YeelightLight.SetDefaultAsync*) |
| `bg_start_cf` | [`StartColorFlowAsync`](xref:Yeelight.YeelightLight.StartColorFlowAsync*) |
| `bg_stop_cf` | [`StopColorFlowAsync`](xref:Yeelight.YeelightLight.StopColorFlowAsync*) |
| `bg_set_scene` | [`SetSceneAsync`](xref:Yeelight.YeelightLight.SetSceneAsync*) |
| `bg_set_adjust` | [`AdjustAsync`](xref:Yeelight.YeelightLight.AdjustAsync*) |
| `bg_adjust_bright` | [`AdjustBrightnessAsync`](xref:Yeelight.YeelightLight.AdjustBrightnessAsync*) |
| `bg_adjust_ct` | [`AdjustColorTemperatureAsync`](xref:Yeelight.YeelightLight.AdjustColorTemperatureAsync*) |
| `bg_adjust_color` | [`AdjustColorAsync`](xref:Yeelight.YeelightLight.AdjustColorAsync*) |

[`SendCommandAsync`](xref:Yeelight.YeelightLight.SendCommandAsync*) sends any method, as you name it. The specification spells one class of
`set_scene` in two ways in its text (`auto_dealy_off` once, `auto_delay_off` in its description and in its example); the library sends
`auto_delay_off`.

## What the library adds

The protocol is a set of messages. The library turns it into something that you can use in a program:

- **Discovery on real networks**: the search from every network of the computer, as a multicast and as a broadcast, three times, and a probe of one
  address. Both because of what real lights do: see [Troubleshooting](troubleshooting.md#a-light-is-missing).
- **Identity**: a light is its `id`, and its connection follows it to a new address when the connection to the old one is down. A light that is
  connected is not moved by an announcement, so that nobody on the network can take a light over by announcing its id.
- **Checks before sending**: ranges, durations and the list of supported methods, so that a command that a light would refuse is not sent.
- **State**: the properties of a light are kept from the answers to a search, the answers to `get_prop` and the `props` notifications, and are read
  again when a connection is made.
- **Limits**: one connection for each light, a pace of 60 commands a minute, and commands that fail at once when they cannot be sent.
- **A connection that holds up**: a keep-alive, reconnecting with a growing wait, and pending commands that fail the moment that a connection is lost.
- **Events**: the notifications are handed to your code by a dispatcher, one at a time and in order.
- **Music mode**: the TCP server that the light connects to, and the check that it is the light that connects.
- **Safety with input**: the messages of a light, and of anything that pretends to be one, are read as input that may be malformed, too large or lying.

What it does not do: it does not use the cloud of Yeelight or its app, it does not talk to a light that has LAN Control off, and it does not find
or control a light that answers no search (the id, the model and the supported methods only come from the answer to a search).
