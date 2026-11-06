namespace Yeelight
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Drawing;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A light of a Yeelight device: what it is doing, and the commands that change it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A device has one or two lights. Its main light is the <see cref="YeelightDevice"/> itself (<c>set_power</c>, <c>set_bright</c>, ...),
    /// and a device that has a background light (some ceiling lights) offers it as <see cref="YeelightDevice.Background"/>
    /// (<c>bg_set_power</c>, <c>bg_set_bright</c>, ...). The two have the same commands and the same properties.
    /// </para>
    /// <para>
    /// <b>The properties are what the light told.</b> A light reports every change to the connections it has, and the client reads all
    /// its properties when a connection is made, so the properties follow the light, whoever changes it (the app of Yeelight, a remote,
    /// the switch on the wall). A command does not change them: its effect arrives as a report a moment later. A property is
    /// <c>0</c> (or empty) until the light has reported it. <see cref="PropertyChanged"/> is raised for the changes, by the dispatcher of the
    /// client (see <see cref="YeelightClient"/>): it says which property changed, and a change that is still waiting when the same property
    /// changes again is raised once, so read the property when it is raised.
    /// </para>
    /// <para>
    /// <b>Every command</b> returns when the light has answered, and throws a <see cref="YeelightException"/> when it did not succeed: the
    /// light refused it, there is no connection, the answer did not come in time, or the command was held back by the limit of
    /// 60 commands a minute that a light puts on a connection. An argument that a light would refuse (a brightness of 101) is refused
    /// first, with an <see cref="ArgumentOutOfRangeException"/>, and nothing is sent. A command that the light does not list among the methods
    /// that it supports throws a <see cref="NotSupportedException"/>. Every command takes a <see cref="CancellationToken"/>: cancelling it ends
    /// the wait with an <see cref="OperationCanceledException"/> and leaves the connection usable (a command that was already sent is not
    /// undone). Most commands are only accepted by a light that is on: see the notes of the individual commands.
    /// </para>
    /// <para>
    /// A <b>transition</b> makes a change gradual: <c>null</c> (or <see cref="TimeSpan.Zero"/>) is a sudden change, anything else is
    /// smooth, over that time, which is at least <see cref="YeelightLimits.MinTransition"/>.
    /// </para>
    /// </remarks>
    public abstract class YeelightLight : INotifyPropertyChanged
    {
        private readonly string _prefix;
        private readonly LightState _state = new();

        internal YeelightLight(DeviceContext context, string prefix)
        {
            this.Context = context;
            this._prefix = prefix;
        }

        /// <summary>
        /// Raised when a property of the light changed, by the dispatcher of the client: one at a time, in the order in which they
        /// happened, on a thread that is not the one that reads from the light.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets whether the light is on (<c>false</c> means it is switched off by software: it is not without power).
        /// </summary>
        public bool IsOn => this._state.IsOn;

        /// <summary>
        /// Gets the brightness, a percentage from 1 to 100 (<c>0</c> until the light has reported it).
        /// </summary>
        public int Brightness => this._state.Brightness;

        /// <summary>
        /// Gets the mode that decides which of <see cref="Rgb"/>, <see cref="ColorTemperature"/> and <see cref="Hue"/> with
        /// <see cref="Saturation"/> is what the light shows. It is <see cref="Yeelight.ColorMode.Rgb"/> until the light has reported it.
        /// </summary>
        public ColorMode ColorMode => this._state.ColorMode;

        /// <summary>
        /// Gets the colour temperature, in kelvin. It is only what the light shows when <see cref="ColorMode"/> is
        /// <see cref="Yeelight.ColorMode.ColorTemperature"/>. The range depends on the model.
        /// </summary>
        public int ColorTemperature => this._state.ColorTemperature;

        /// <summary>
        /// Gets the colour as a number: red times 65536 plus green times 256 plus blue. It is only what the light shows when
        /// <see cref="ColorMode"/> is <see cref="Yeelight.ColorMode.Rgb"/>.
        /// </summary>
        public int Rgb => this._state.Rgb;

        /// <summary>
        /// Gets <see cref="Rgb"/> as a colour.
        /// </summary>
        public Color Color
        {
            get
            {
                var rgb = this._state.Rgb;
                return Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
        }

        /// <summary>
        /// Gets the hue, from 0 to 359. It is only what the light shows when <see cref="ColorMode"/> is <see cref="Yeelight.ColorMode.Hsv"/>.
        /// </summary>
        public int Hue => this._state.Hue;

        /// <summary>
        /// Gets the saturation, from 0 to 100. It is only what the light shows when <see cref="ColorMode"/> is <see cref="Yeelight.ColorMode.Hsv"/>.
        /// </summary>
        public int Saturation => this._state.Saturation;

        /// <summary>
        /// Gets whether a colour flow is running.
        /// </summary>
        public bool IsFlowing => this._state.IsFlowing;

        /// <summary>
        /// Gets the parameters of the flow that is running (empty when none is), as the light reports them: read them with <see cref="ColorFlow.TryParse(string?, out ColorFlow?)"/>.
        /// </summary>
        public string FlowParameters => this._state.FlowParameters;

        internal DeviceContext Context { get; }

        /// <summary>Whether changes are not reported: while a device is being made, its starting state is not news.</summary>
        internal volatile bool Muted;

        /// <summary>The device that this light belongs to (the device itself for its main light).</summary>
        internal abstract YeelightDevice Owner { get; }

        /// <summary>Sends a command to the device, with the method as the protocol names it.</summary>
        internal abstract Task<YeelightResult> ExecuteAsync(string method, IReadOnlyList<object?> parameters, CancellationToken cancellationToken);

        /// <summary>Whether the device may be sent the method: it lists it, or it has not said what it lists.</summary>
        internal abstract bool AllowsMethod(string method);

        /// <summary>
        /// Sends a command by its method and parameters, as the protocol has them (<c>"set_power"</c> with <c>"on"</c>, <c>"smooth"</c>,
        /// <c>500</c>), for what this class has no method for. Nothing is added to the method and nothing is checked.
        /// </summary>
        /// <param name="method">The method, as the protocol names it.</param>
        /// <param name="parameters">The parameters: numbers, text and truth values.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>The answer of the light. This does not throw for a command that did not succeed: look at <see cref="YeelightResult.Success"/>.</returns>
        /// <exception cref="ArgumentException"><paramref name="method"/> is empty, or a parameter is not a number, text or a truth value.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
        public Task<YeelightResult> SendCommandAsync(string method, IReadOnlyList<object?>? parameters = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(method);
            return this.ExecuteAsync(method, parameters ?? [], cancellationToken);
        }

        /// <summary>
        /// Switches the light on or off.
        /// </summary>
        /// <param name="on"><c>true</c> to switch on, <c>false</c> to switch off.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="mode">The mode to switch on in (only when switching on).</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The transition is shorter than the light accepts.</exception>
        /// <exception cref="ArgumentException">A mode other than <see cref="PowerOnMode.Normal"/> is given when switching off.</exception>
        public Task SetPowerAsync(bool on, TimeSpan? transition = null, PowerOnMode mode = PowerOnMode.Normal, CancellationToken cancellationToken = default)
        {
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            if (!Enum.IsDefined(mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "That is not a value of PowerOnMode.");
            }

            if (!on && mode != PowerOnMode.Normal)
            {
                throw new ArgumentException("A mode is for switching on.", nameof(mode));
            }

            // The mode is only sent when it is not the normal one: the older firmware versions know three parameters.
            IReadOnlyList<object?> parameters = mode == PowerOnMode.Normal
                ? [on ? "on" : "off", effect, duration]
                : [on ? "on" : "off", effect, duration, (int)mode];
            return this.SendAsync("set_power", parameters, cancellationToken);
        }

        /// <summary>
        /// Switches the light on.
        /// </summary>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="mode">The mode to switch on in.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The transition is shorter than the light accepts.</exception>
        public Task TurnOnAsync(TimeSpan? transition = null, PowerOnMode mode = PowerOnMode.Normal, CancellationToken cancellationToken = default)
        {
            return this.SetPowerAsync(true, transition, mode, cancellationToken);
        }

        /// <summary>
        /// Switches the light off.
        /// </summary>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The transition is shorter than the light accepts.</exception>
        public Task TurnOffAsync(TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            return this.SetPowerAsync(false, transition, PowerOnMode.Normal, cancellationToken);
        }

        /// <summary>
        /// Switches the light off when it is on, and on when it is off, without having to know which it is.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        public Task ToggleAsync(CancellationToken cancellationToken = default)
        {
            return this.SendAsync("toggle", [], cancellationToken);
        }

        /// <summary>
        /// Sets the brightness. Only a light that is on accepts it.
        /// </summary>
        /// <param name="brightness">The brightness, a percentage from 1 to 100.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public Task SetBrightnessAsync(int brightness, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            Validate.Brightness(brightness, nameof(brightness));
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_bright", [brightness, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Sets the colour temperature, which switches the light to <see cref="Yeelight.ColorMode.ColorTemperature"/>. Only a light that is on accepts it.
        /// </summary>
        /// <param name="kelvin">The colour temperature, from 1700 to 6500. The range of a light depends on its model.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public Task SetColorTemperatureAsync(int kelvin, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            Validate.ColorTemperature(kelvin, nameof(kelvin));
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_ct_abx", [kelvin, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Sets the colour, which switches the light to <see cref="Yeelight.ColorMode.Rgb"/>. Only a light that is on accepts it.
        /// </summary>
        /// <param name="red">The red part of the colour, 0 to 255.</param>
        /// <param name="green">The green part of the colour, 0 to 255.</param>
        /// <param name="blue">The blue part of the colour, 0 to 255.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public Task SetRgbAsync(int red, int green, int blue, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            var rgb = Validate.Rgb(red, green, blue);
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_rgb", [rgb, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Sets the colour, which switches the light to <see cref="Yeelight.ColorMode.Rgb"/>. Only a light that is on accepts it.
        /// </summary>
        /// <param name="color">The colour. Its alpha is ignored.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The transition is shorter than the light accepts.</exception>
        public Task SetRgbAsync(Color color, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            return this.SetRgbAsync(color.R, color.G, color.B, transition, cancellationToken);
        }

        /// <summary>
        /// Sets the colour as a hue and a saturation, which switches the light to <see cref="Yeelight.ColorMode.Hsv"/>. Only a light that is on accepts it.
        /// </summary>
        /// <param name="hue">The hue, 0 to 359.</param>
        /// <param name="saturation">The saturation, 0 to 100.</param>
        /// <param name="transition">How long the change takes, or <c>null</c> for a sudden one.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public Task SetHsvAsync(int hue, int saturation, TimeSpan? transition = null, CancellationToken cancellationToken = default)
        {
            Validate.Hue(hue, nameof(hue));
            Validate.Saturation(saturation, nameof(saturation));
            var (effect, duration) = Validate.Transition(transition, nameof(transition));
            return this.SendAsync("set_hsv", [hue, saturation, effect, duration], cancellationToken);
        }

        /// <summary>
        /// Saves the current state of the light in its memory, so that it shows that state when it is switched on at the wall again (after a
        /// loss of power). Only a light that is on accepts it.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        public Task SetDefaultAsync(CancellationToken cancellationToken = default)
        {
            return this.SendAsync("set_default", [], cancellationToken);
        }

        /// <summary>
        /// Starts a colour flow. Only a light that is on accepts it.
        /// </summary>
        /// <param name="flow">The flow.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="flow"/> is <c>null</c>.</exception>
        public Task StartColorFlowAsync(ColorFlow flow, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(flow);
            return this.SendAsync("start_cf", [flow.Count, (int)flow.Action, flow.Expression], cancellationToken);
        }

        /// <summary>
        /// Stops the colour flow that is running.
        /// </summary>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        public Task StopColorFlowAsync(CancellationToken cancellationToken = default)
        {
            return this.SendAsync("stop_cf", [], cancellationToken);
        }

        /// <summary>
        /// Sets the light directly to a scene, switching it on first when it is off.
        /// </summary>
        /// <param name="scene">The scene.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <c>null</c>.</exception>
        public Task SetSceneAsync(YeelightScene scene, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scene);
            return this.SendAsync("set_scene", scene.GetParameters(), cancellationToken);
        }

        /// <summary>
        /// Changes the brightness, the colour temperature or the colour without knowing what it is now: a step up, a step down, or a step up
        /// that starts again at the bottom when it reaches the top.
        /// </summary>
        /// <param name="action">The direction. Only <see cref="AdjustAction.Circle"/> is valid for <see cref="AdjustProperty.Color"/>.</param>
        /// <param name="property">The property to change.</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentException">The action is not <see cref="AdjustAction.Circle"/> and the property is <see cref="AdjustProperty.Color"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The action or the property is not a value of its enumeration.</exception>
        public Task AdjustAsync(AdjustAction action, AdjustProperty property, CancellationToken cancellationToken = default)
        {
            var direction = action switch
            {
                AdjustAction.Increase => "increase",
                AdjustAction.Decrease => "decrease",
                AdjustAction.Circle => "circle",
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "That is not a value of AdjustAction."),
            };
            var what = property switch
            {
                AdjustProperty.Brightness => "bright",
                AdjustProperty.ColorTemperature => "ct",
                AdjustProperty.Color => "color",
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, "That is not a value of AdjustProperty."),
            };
            if (property == AdjustProperty.Color && action != AdjustAction.Circle)
            {
                throw new ArgumentException("The colour can only be adjusted in a circle.", nameof(action));
            }

            return this.SendAsync("set_adjust", [direction, what], cancellationToken);
        }

        /// <summary>
        /// Changes the brightness by a percentage of what it is now.
        /// </summary>
        /// <param name="percentage">The percentage to change it by, from -100 to 100 (-20 makes it 20 percent less).</param>
        /// <param name="duration">How long the change takes, or <c>null</c> for the shortest time that a light supports (30 milliseconds).</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public Task AdjustBrightnessAsync(int percentage, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            Validate.Percentage(percentage, nameof(percentage));
            return this.SendAsync("adjust_bright", [percentage, Validate.Duration(duration, nameof(duration))], cancellationToken);
        }

        /// <summary>
        /// Changes the colour temperature by a percentage of what it is now.
        /// </summary>
        /// <param name="percentage">The percentage to change it by, from -100 to 100.</param>
        /// <param name="duration">How long the change takes, or <c>null</c> for the shortest time that a light supports (30 milliseconds).</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of the range that the protocol allows.</exception>
        public Task AdjustColorTemperatureAsync(int percentage, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            Validate.Percentage(percentage, nameof(percentage));
            return this.SendAsync("adjust_ct", [percentage, Validate.Duration(duration, nameof(duration))], cancellationToken);
        }

        /// <summary>
        /// Moves the colour on. How far is the light's own affair: the protocol has no say in it.
        /// </summary>
        /// <param name="duration">How long the change takes, or <c>null</c> for the shortest time that a light supports (30 milliseconds).</param>
        /// <param name="cancellationToken">To stop waiting for the answer.</param>
        /// <returns>A task that ends when the light has answered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The duration is shorter than the light accepts.</exception>
        public Task AdjustColorAsync(TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            // The protocol has a percentage here, and says that the light ignores it.
            return this.SendAsync("adjust_color", [0, Validate.Duration(duration, nameof(duration))], cancellationToken);
        }

        /// <summary>
        /// Takes over one property that the light reported, and raises <see cref="PropertyChanged"/> when it changed something.
        /// </summary>
        /// <param name="key">The name of the property of the light, without the prefix of a background light (<c>power</c>, <c>bright</c>, ...).</param>
        /// <param name="value">The value, as the light sent it.</param>
        /// <returns>Whether the property is one that belongs to the state of a light.</returns>
        internal bool ApplyState(string key, string value)
        {
            var changed = this._state.Apply(key, value, out var handled);
            if (changed is not null)
            {
                this.Notify(changed);
                if (changed == nameof(this.Rgb))
                {
                    this.Notify(nameof(this.Color));
                }
            }

            return handled;
        }

        /// <summary>
        /// Tells the application that a property changed: <see cref="PropertyChanged"/>, and the event of the client.
        /// </summary>
        internal void Notify(string propertyName)
        {
            if (this.Muted)
            {
                return;
            }

            this.Context.Dispatcher.Post(this, propertyName, () =>
            {
                this.Context.Dispatcher.RaisePropertyChanged(this.PropertyChanged, this, new PropertyChangedEventArgs(propertyName));
                this.Context.StateChanged(this.Owner, this, propertyName);
            });
        }

        private protected async Task SendAsync(string method, IReadOnlyList<object?> parameters, CancellationToken cancellationToken)
        {
            var name = this._prefix + method;
            if (!this.AllowsMethod(name))
            {
                throw new NotSupportedException($"The light does not support '{name}': it is not one of the methods that it lists.");
            }

            var result = await this.ExecuteAsync(name, parameters, cancellationToken).ConfigureAwait(false);
            result.EnsureSuccess(name);
        }
    }
}
