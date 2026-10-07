namespace LibraryFan;

public enum ControlMode
{
    Bios,
    Library,
    Manual
}

public sealed class FanDecision
{
    public required ControlMode Mode { get; init; }
    public required bool IsLatched { get; init; }
    public required string Reason { get; set; }
    public string? Refusal { get; init; }
    public bool HasCommand { get; set; }
    public byte Command { get; init; }
    public bool CommandRequiresLiveFans { get; init; }
    public int? CommandedLevel { get; init; }
    public bool BothFansSeen { get; set; }
    public bool CanTakeControl { get; init; }
}

public sealed class FanSession
{
    public const int DangerCelsius = 80;
    public const int BadPollsBeforeHandoff = 3;

    private readonly LibraryCurve _curve;
    private ControlMode _mode = ControlMode.Bios;
    private int _manualLevel = 1;
    private bool _latched;
    private string _latchReason = "";
    private int _badPolls;
    private bool _seenFan1;
    private bool _seenFan2;
    private int? _heldLevel;
    private bool _restorePending;
    private FanReading _last = FanReading.Unreadable;

    public FanSession(LibraryCurve? curve = null)
    {
        _curve = curve ?? new LibraryCurve();
    }

    public ControlMode Mode => _mode;
    public bool IsLatched => _latched;
    public bool BothFansSeen => _seenFan1 && _seenFan2;
    public int ManualLevel => _manualLevel;
    public FanReading LastReading => _last;

    public FanDecision Tick(FanReading reading)
    {
        _last = reading;
        if (reading.Fan1Live)
            _seenFan1 = true;
        if (reading.Fan2Live)
            _seenFan2 = true;

        if (reading.Ok && reading.IsDangerous)
            return Trip("Handed control back to the machine because a sensor reached 80°C.");

        if (!reading.Ok || !reading.ValidSensors.Any())
            return NoteBadPoll();

        _badPolls = 0;
        if (_latched)
            return HoldLatch();

        return Speak(ApplyActive(reading));
    }

    public FanDecision Request(ControlMode mode, int manualLevel = 1)
    {
        manualLevel = Math.Clamp(manualLevel, 0, 7);
        if (mode != ControlMode.Bios && _last.IsDangerous)
        {
            return Speak(new FanDecision
            {
                Mode = ControlMode.Bios,
                IsLatched = _latched,
                Reason = _latched
                    ? _latchReason
                    : "BIOS is in control.",
                Refusal = "A sensor is still at 80°C, so the machine stays in control.",
                CanTakeControl = false
            });
        }

        _latched = false;
        _latchReason = "";
        _mode = mode;
        if (mode == ControlMode.Manual)
            _manualLevel = manualLevel;
        if (mode == ControlMode.Library)
            _heldLevel = null;
        _restorePending = mode == ControlMode.Bios;

        if (!_last.Ok || !_last.ValidSensors.Any())
            return Speak(DescribeIdle());

        return Speak(ApplyActive(_last));
    }

    private FanDecision NoteBadPoll()
    {
        _badPolls++;
        if (_badPolls >= BadPollsBeforeHandoff)
            return Trip("Handed control back to the machine because the controller could not be read.");

        var reason = _mode switch
        {
            ControlMode.Library => "Library is waiting for a temperature reading.",
            ControlMode.Manual => $"Manual is holding level {_manualLevel}.",
            _ => "BIOS is in control."
        };
        if (_latched)
            reason = _latchReason;

        return Speak(new FanDecision
        {
            Mode = _latched ? ControlMode.Bios : _mode,
            IsLatched = _latched,
            Reason = reason,
            CanTakeControl = !_last.IsDangerous
        });
    }

    private FanDecision Trip(string reason)
    {
        _latched = true;
        _latchReason = reason;
        _mode = ControlMode.Bios;
        _heldLevel = null;
        _restorePending = false;
        _badPolls = 0;
        return Speak(new FanDecision
        {
            Mode = ControlMode.Bios,
            IsLatched = true,
            Reason = reason,
            HasCommand = true,
            Command = Registers.BiosControl,
            CanTakeControl = false
        });
    }

    private FanDecision HoldLatch()
    {
        return Speak(new FanDecision
        {
            Mode = ControlMode.Bios,
            IsLatched = true,
            Reason = _latchReason,
            CanTakeControl = !_last.IsDangerous
        });
    }

    private FanDecision ApplyActive(FanReading reading)
    {
        if (_restorePending && _mode == ControlMode.Bios)
        {
            _restorePending = false;
            return new FanDecision
            {
                Mode = ControlMode.Bios,
                IsLatched = false,
                Reason = "BIOS is in control.",
                HasCommand = true,
                Command = Registers.BiosControl,
                CanTakeControl = true
            };
        }

        if (_mode == ControlMode.Bios)
        {
            return new FanDecision
            {
                Mode = ControlMode.Bios,
                IsLatched = false,
                Reason = "BIOS is in control.",
                CanTakeControl = true
            };
        }

        if (_mode == ControlMode.Manual)
        {
            return new FanDecision
            {
                Mode = ControlMode.Manual,
                IsLatched = false,
                Reason = $"Manual is holding level {_manualLevel}.",
                HasCommand = true,
                Command = (byte)_manualLevel,
                CommandRequiresLiveFans = true,
                CommandedLevel = _manualLevel,
                CanTakeControl = true
            };
        }

        var hottest = reading.Hottest!;
        var level = _curve.Choose(hottest.Celsius!.Value, ref _heldLevel);
        return new FanDecision
        {
            Mode = ControlMode.Library,
            IsLatched = false,
            Reason = $"Library is holding level {level} because {hottest.Label} is {hottest.Celsius}°C.",
            HasCommand = true,
            Command = (byte)level,
            CommandRequiresLiveFans = true,
            CommandedLevel = level,
            CanTakeControl = true
        };
    }

    private FanDecision DescribeIdle()
    {
        if (_mode == ControlMode.Bios)
        {
            return new FanDecision
            {
                Mode = ControlMode.Bios,
                IsLatched = _latched,
                Reason = _latched ? _latchReason : "BIOS is in control.",
                HasCommand = _restorePending,
                Command = Registers.BiosControl,
                CanTakeControl = !_last.IsDangerous
            };
        }

        _restorePending = false;
        return new FanDecision
        {
            Mode = _mode,
            IsLatched = false,
            Reason = _mode == ControlMode.Manual
                ? $"Manual is holding level {_manualLevel}."
                : "Library is waiting for a temperature reading.",
            CanTakeControl = true
        };
    }

    private FanDecision Speak(FanDecision decision)
    {
        decision.BothFansSeen = BothFansSeen;
        if (decision.HasCommand && decision.CommandRequiresLiveFans && !BothFansSeen)
        {
            decision.HasCommand = false;
            decision.Reason = decision.Mode switch
            {
                ControlMode.Library => WaitingLibrary(decision),
                ControlMode.Manual => $"Manual wants level {_manualLevel}. Both fan speeds have not been read yet, so the controller has not taken over.",
                _ => decision.Reason
            };
        }

        return decision;
    }

    private static string WaitingLibrary(FanDecision decision)
    {
        var level = decision.CommandedLevel?.ToString() ?? "a level";
        return $"Library wants level {level}. Both fan speeds have not been read yet, so the controller has not taken over.";
    }
}
