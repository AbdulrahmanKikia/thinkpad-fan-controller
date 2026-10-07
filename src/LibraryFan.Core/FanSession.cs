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
    public required bool CanTakeControl { get; init; }
    public string? Refusal { get; init; }
    public bool HasCommand { get; set; }
    public byte Command { get; init; }
    public bool CommandRequiresLiveFans { get; init; }
    public int? CommandedLevel { get; init; }
    public bool BothFansSeen { get; set; }
}

/// <summary>
/// The safety guard in front of every write to register 0x2F. Library and Manual both go through it.
/// One valid sensor at 80°C hands both fans to firmware and latches there. Three unreadable polls
/// do the same. Level writes wait until both fan speeds have been read live this session.
/// </summary>
public sealed class FanSession
{
    public const int DangerCelsius = 80;
    public const int BadPollsBeforeHandoff = 3;

    public const string BiosSentence = "BIOS is in control.";
    public const string ThermalHandoff = "Handed control back to the machine because a sensor reached 80°C.";
    public const string ReadHandoff = "Handed control back to the machine because the controller could not be read.";
    public const string StillHot = "A sensor is still at 80°C, so the machine stays in control.";

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

    public LibraryCurve Curve => _curve;
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

        if (reading.IsDangerous)
            return Trip(ThermalHandoff);

        if (!reading.Ok || !reading.ValidSensors.Any())
            return NoteBadPoll();

        _badPolls = 0;
        if (_latched)
            return HoldLatch();

        return Speak(ApplyActive(reading));
    }

    public FanDecision Request(ControlMode mode, int manualLevel = 1)
    {
        manualLevel = Math.Clamp(manualLevel, 0, Registers.MaxLevel);
        if (mode != ControlMode.Bios && _last.IsDangerous)
        {
            return Speak(new FanDecision
            {
                Mode = ControlMode.Bios,
                IsLatched = _latched,
                Reason = _latched ? _latchReason : BiosSentence,
                Refusal = StillHot,
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
            return Trip(ReadHandoff);

        var reason = _latched ? _latchReason : _mode switch
        {
            ControlMode.Library => "Library is waiting for a temperature reading.",
            ControlMode.Manual => $"Manual is holding level {_manualLevel}.",
            _ => BiosSentence
        };

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

    private FanDecision HoldLatch() => Speak(new FanDecision
    {
        Mode = ControlMode.Bios,
        IsLatched = true,
        Reason = _latchReason,
        CanTakeControl = !_last.IsDangerous
    });

    private FanDecision ApplyActive(FanReading reading)
    {
        if (_mode == ControlMode.Bios)
        {
            var restore = _restorePending;
            _restorePending = false;
            return new FanDecision
            {
                Mode = ControlMode.Bios,
                IsLatched = false,
                Reason = BiosSentence,
                HasCommand = restore,
                Command = Registers.BiosControl,
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
            var restore = _restorePending;
            _restorePending = false;
            return new FanDecision
            {
                Mode = ControlMode.Bios,
                IsLatched = false,
                Reason = BiosSentence,
                HasCommand = restore,
                Command = Registers.BiosControl,
                CanTakeControl = !_last.IsDangerous
            };
        }

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
            var level = decision.CommandedLevel?.ToString() ?? "a level";
            var who = decision.Mode == ControlMode.Manual ? "Manual" : "Library";
            decision.Reason = $"{who} wants level {level}. Both fan speeds have not been read yet, so the controller has not taken over.";
        }

        return decision;
    }
}
