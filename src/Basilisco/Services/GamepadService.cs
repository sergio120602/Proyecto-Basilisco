using System.Windows.Threading;
using SDL;

namespace Basilisco.Services;

/// <summary>Acciones del mando, con el nombre de lo que hacen en Basilisco (no del botón físico).</summary>
public enum PadAction
{
    Confirm,
    Back,
    Rotate,
    Customize,
    PreviousFilter,
    NextFilter,
    Format,
    FullScreen,
    PageLeft,
    PageRight,
    /// <summary>Pulsar el stick derecho (R3): el buscador.</summary>
    Search,
    /// <summary>Pulsar el stick izquierdo (L3): el orden.</summary>
    Sort,
}

/// <summary>Familia del mando, para enseñar sus botones con los nombres y símbolos que trae impresos.</summary>
public enum PadStyle { Xbox, PlayStation, Nintendo }

/// <summary>
/// Lee los mandos con SDL3, que trae los mapeos correctos de los de Xbox (One, Series, 360), PlayStation
/// (DualSense de PS5 y DualShock 4) y Nintendo Switch Pro, por cable o por Bluetooth.
/// Convierte los botones en acciones de Basilisco, con repetición al mantener pulsada una dirección.
/// </summary>
public sealed unsafe class GamepadService : IDisposable
{
    const double TickSeconds = 1 / 60.0;
    const double StickThreshold = 0.5;
    const double RepeatDelay = 0.4;
    const double RepeatInterval = 0.11;
    const double HotplugInterval = 1.0;

    readonly Dictionary<SDL_JoystickID, nint> _pads = [];
    readonly Dictionary<PadAction, bool> _held = [];
    readonly DispatcherTimer _timer;
    readonly bool _available;

    double _sinceHotplug = HotplugInterval;
    int _heldDirection;
    int _heldVertical;
    double _repeatIn;

    /// <summary>Solo se lee el mando cuando esto devuelve true (Basilisco en primer plano), para no reaccionar mientras juegas.</summary>
    public Func<bool> IsActive { get; set; } = () => true;

    public event Action<PadAction>? Pressed;

    /// <summary>-1 izquierda, +1 derecha; se repite mientras se mantiene la cruceta o el stick.</summary>
    public event Action<int>? Navigate;

    /// <summary>-1 arriba, +1 abajo (cruceta o stick izquierdo); un aviso por pulsación, sin repetición.</summary>
    public event Action<int>? NavigateVertical;

    /// <summary>Stick derecho (de -1 a 1), en cada fotograma en que está inclinado.</summary>
    public event Action<double, double>? RightStick;

    /// <summary>Se ha conectado un mando: su nombre y su familia.</summary>
    public event Action<string, PadStyle>? Connected;

    public event Action? Disconnected;

    /// <summary>Familia del último mando usado.</summary>
    public PadStyle Style { get; private set; } = PadStyle.Xbox;

    public bool Available => _available;

    public GamepadService()
    {
        try
        {
            // No hay ventana de SDL: sin esto, SDL podría ignorar el mando por creer que la aplicación no tiene el foco.
            // El foco de verdad lo comprueba IsActive.
            SDL3.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            _available = SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _available = false;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromSeconds(TickSeconds) };
        _timer.Tick += (_, _) => Tick();
        if (_available)
            _timer.Start();
    }

    void Tick()
    {
        SDL3.SDL_PumpEvents();
        // Basilisco no usa la cola de eventos de SDL: se lee el estado directamente y se vacía para que no crezca.
        SDL3.SDL_FlushEvents(0, 0xFFFF);
        SDL3.SDL_UpdateGamepads();

        _sinceHotplug += TickSeconds;
        if (_sinceHotplug >= HotplugInterval)
        {
            _sinceHotplug = 0;
            DetectPads();
        }

        if (_pads.Count == 0)
            return;

        if (!IsActive())
        {
            // Sin foco (por ejemplo, jugando): se olvida lo pulsado para no disparar nada al volver.
            _held.Clear();
            _heldDirection = 0;
            _heldVertical = 0;
            return;
        }

        ReadButtons();
        ReadNavigation();
        ReadVertical();
        ReadRightStick();
    }

    void DetectPads()
    {
        using var ids = SDL3.SDL_GetGamepads();
        if (ids is null)
            return;

        var present = new HashSet<SDL_JoystickID>();
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            present.Add(id);
            if (_pads.ContainsKey(id))
                continue;

            var pad = SDL3.SDL_OpenGamepad(id);
            if (pad == null)
                continue;
            _pads[id] = (nint)pad;
            Style = StyleOf(SDL3.SDL_GetGamepadType(pad));
            Connected?.Invoke(SDL3.SDL_GetGamepadName(pad) ?? "Mando", Style);
        }

        foreach (var id in _pads.Keys.Where(id => !present.Contains(id)).ToList())
        {
            SDL3.SDL_CloseGamepad((SDL_Gamepad*)_pads[id]);
            _pads.Remove(id);
            Disconnected?.Invoke();
        }
    }

    static PadStyle StyleOf(SDL_GamepadType type) => type switch
    {
        SDL_GamepadType.SDL_GAMEPAD_TYPE_PS3 or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS4 or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS5 => PadStyle.PlayStation,
        SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT or
            SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR or
            SDL_GamepadType.SDL_GAMEPAD_TYPE_GAMECUBE => PadStyle.Nintendo,
        _ => PadStyle.Xbox,
    };

    void ReadButtons()
    {
        foreach (var action in Enum.GetValues<PadAction>())
        {
            var (down, pad) = IsDown(action);
            var wasDown = _held.GetValueOrDefault(action);
            _held[action] = down;
            if (!down || wasDown)
                continue;

            if (pad is { } used)
                Style = StyleOf(SDL3.SDL_GetGamepadType((SDL_Gamepad*)used));
            Pressed?.Invoke(action);
        }
    }

    /// <summary>
    /// Si la acción está pulsada en algún mando. SDL da los botones por su posición (abajo, derecha…);
    /// en los de Nintendo se acepta con el de la derecha (A) y se vuelve con el de abajo (B), como en la consola.
    /// </summary>
    (bool Down, nint? Pad) IsDown(PadAction action)
    {
        foreach (var raw in _pads.Values)
        {
            var pad = (SDL_Gamepad*)raw;
            var nintendo = StyleOf(SDL3.SDL_GetGamepadType(pad)) == PadStyle.Nintendo;
            var down = action switch
            {
                PadAction.Confirm => Button(pad, nintendo ? SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST : SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH),
                PadAction.Back => Button(pad, nintendo ? SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH : SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST),
                PadAction.Rotate => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST),
                PadAction.Customize => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH),
                PadAction.PreviousFilter => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER),
                PadAction.NextFilter => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER),
                PadAction.Format => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK),
                PadAction.FullScreen => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START),
                PadAction.PageLeft => Axis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER) > StickThreshold,
                PadAction.PageRight => Axis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) > StickThreshold,
                PadAction.Search => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK),
                PadAction.Sort => Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK),
                _ => false,
            };
            if (down)
                return (true, raw);
        }
        return (false, null);
    }

    void ReadVertical()
    {
        var direction = 0;
        foreach (var raw in _pads.Values)
        {
            var pad = (SDL_Gamepad*)raw;
            var y = Axis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);
            if (Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP) || y < -0.6)
                direction = -1;
            else if (Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN) || y > 0.6)
                direction = 1;
            if (direction != 0)
                break;
        }

        if (direction != 0 && direction != _heldVertical)
            NavigateVertical?.Invoke(direction);
        _heldVertical = direction;
    }

    void ReadNavigation()
    {
        var direction = 0;
        foreach (var raw in _pads.Values)
        {
            var pad = (SDL_Gamepad*)raw;
            var x = Axis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX);
            if (Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT) || x < -StickThreshold)
                direction = -1;
            else if (Button(pad, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT) || x > StickThreshold)
                direction = 1;
            if (direction != 0)
                break;
        }

        if (direction == 0)
        {
            _heldDirection = 0;
            return;
        }

        // Un toque mueve una caja; mantenido, espera un momento y luego avanza solo, como en las consolas.
        if (direction != _heldDirection)
        {
            _heldDirection = direction;
            _repeatIn = RepeatDelay;
            Navigate?.Invoke(direction);
            return;
        }

        _repeatIn -= TickSeconds;
        if (_repeatIn <= 0)
        {
            _repeatIn = RepeatInterval;
            Navigate?.Invoke(direction);
        }
    }

    void ReadRightStick()
    {
        foreach (var raw in _pads.Values)
        {
            var pad = (SDL_Gamepad*)raw;
            var x = Axis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX);
            var y = Axis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY);
            if (Math.Abs(x) > 0.2 || Math.Abs(y) > 0.2)
            {
                RightStick?.Invoke(x, y);
                return;
            }
        }
    }

    static bool Button(SDL_Gamepad* pad, SDL_GamepadButton button) => SDL3.SDL_GetGamepadButton(pad, button);

    static double Axis(SDL_Gamepad* pad, SDL_GamepadAxis axis) => SDL3.SDL_GetGamepadAxis(pad, axis) / 32767.0;

    /// <summary>Texto de cada acción con los botones de la familia del mando, para la barra de abajo.</summary>
    public static string Label(PadAction action, PadStyle style) => (action, style) switch
    {
        (PadAction.Confirm, PadStyle.PlayStation) => "✕",
        (PadAction.Confirm, _) => "A",
        (PadAction.Back, PadStyle.PlayStation) => "○",
        (PadAction.Back, _) => "B",
        (PadAction.Rotate, PadStyle.PlayStation) => "□",
        (PadAction.Rotate, PadStyle.Nintendo) => "Y",
        (PadAction.Rotate, _) => "X",
        (PadAction.Customize, PadStyle.PlayStation) => "△",
        (PadAction.Customize, PadStyle.Nintendo) => "X",
        (PadAction.Customize, _) => "Y",
        (PadAction.PreviousFilter or PadAction.NextFilter, PadStyle.PlayStation) => "L1 R1",
        (PadAction.PreviousFilter or PadAction.NextFilter, PadStyle.Nintendo) => "L R",
        (PadAction.PreviousFilter or PadAction.NextFilter, _) => "LB RB",
        (PadAction.Format, PadStyle.PlayStation) => "Create",
        (PadAction.Format, PadStyle.Nintendo) => "−",
        (PadAction.Format, _) => "View",
        (PadAction.FullScreen, PadStyle.PlayStation) => "Options",
        (PadAction.FullScreen, PadStyle.Nintendo) => "+",
        (PadAction.FullScreen, _) => "Menu",
        (PadAction.PageLeft or PadAction.PageRight, PadStyle.PlayStation) => "L2 R2",
        (PadAction.PageLeft or PadAction.PageRight, PadStyle.Nintendo) => "ZL ZR",
        (PadAction.Search, PadStyle.PlayStation) => "R3",
        (PadAction.Search, _) => "RS",
        (PadAction.Sort, PadStyle.PlayStation) => "L3",
        (PadAction.Sort, _) => "LS",
        _ => "LT RT",
    };

    public void Dispose()
    {
        _timer.Stop();
        if (!_available)
            return;
        foreach (var raw in _pads.Values)
            SDL3.SDL_CloseGamepad((SDL_Gamepad*)raw);
        _pads.Clear();
        SDL3.SDL_Quit();
    }
}
