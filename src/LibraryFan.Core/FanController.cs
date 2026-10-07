namespace LibraryFan;

public sealed class TraceSample
{
    public required double Celsius { get; init; }
    public required int Rpm { get; init; }
}

public sealed class FanUpdate
{
    public required string Reason { get; init; }
    public string? Refusal { get; init; }
    public required ControlMode Mode { get; init; }
    public required bool IsLatched { get; init; }
    public required bool CanTakeControl { get; init; }
    public int? Fan1Rpm { get; init; }
    public int? Fan2Rpm { get; init; }
    public bool Fan1Held { get; init; }
    public bool Fan2Held { get; init; }
    public required string CommandLabel { get; init; }
    public int? CommandedLevel { get; init; }
    public required SensorSample[] Sensors { get; init; }
    public required IReadOnlyList<TraceSample> Trace { get; init; }
    public required string DriverStatus { get; init; }
    public required string Health { get; init; }
    public required int ManualLevel { get; init; }
    public int MissedPolls { get; init; }
    public bool DriverOpen { get; init; }
    public int RegistersRead { get; init; }
    public int RegistersTotal { get; init; } = Registers.SnapshotRegisters;
}

/// <summary>
/// Polls the controller on its own thread, runs every reading through <see cref="FanSession"/>,
/// and writes a level only when it changes. A fan speed that fails to read is shown from the
/// previous poll for a few seconds, and is marked as held; a held value never counts as live.
/// </summary>
public sealed class FanController : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    public const int HoldPolls = 3;
    public const int TraceLength = 180;
    public const string Rejected = "The embedded controller did not accept the fan command.";

    private readonly IEcClient _client;
    private readonly FanSession _session;
    private readonly object _gate = new();
    private readonly List<TraceSample> _trace = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly Thread? _thread;
    private readonly HeldValue _fan1 = new();
    private readonly HeldValue _fan2 = new();
    private byte? _lastWritten;
    private DateTime _lastWriteAt;
    private int _missedPolls;
    private bool _disposed;

    public FanController(IEcClient client, FanSession? session = null, bool poll = true)
    {
        _client = client;
        _session = session ?? new FanSession(LibraryCurve.Load(LibraryCurve.DefaultPath()));
        if (poll)
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Fan controller" };
            _thread.Start();
        }
    }

    public event Action<FanUpdate>? Updated;

    public FanSession Session => _session;

    public FanUpdate Request(ControlMode mode, int manualLevel = 1)
    {
        FanUpdate update;
        lock (_gate)
        {
            var decision = _session.Request(mode, manualLevel);
            update = Publish(decision, _session.LastReading);
        }

        Updated?.Invoke(update);
        return update;
    }

    public FanUpdate Poll()
    {
        FanUpdate? update;
        lock (_gate)
        {
            if (_disposed)
                return Idle();
            var reading = _client.Read();
            var decision = _session.Tick(reading);
            update = Publish(decision, reading);
        }

        Updated?.Invoke(update);
        return update;
    }

    public void SetSimTemperature(int celsius)
    {
        if (_client is not SimulatedEc simulated)
            return;
        simulated.HottestCelsius = celsius;
        Poll();
    }

    public void FailSimReads(int polls)
    {
        if (_client is not SimulatedEc simulated)
            return;
        simulated.FailReads(polls);
        for (var i = 0; i < polls; i++)
            Poll();
    }

    public void DropSimRpm(int polls)
    {
        if (_client is not SimulatedEc simulated)
            return;
        simulated.DropRpm(polls);
        Poll();
    }

    public void RestoreBios()
    {
        lock (_gate)
        {
            if (_client.IsOpen)
                _client.RestoreBios();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancel.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(3));
        RestoreBios();
        _client.Dispose();
        _cancel.Dispose();
    }

    private void Loop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            Poll();
            if (_cancel.Token.WaitHandle.WaitOne(PollInterval))
                break;
        }
    }

    private FanUpdate Publish(FanDecision decision, FanReading reading)
    {
        var reason = decision.Reason;
        if (decision.HasCommand && _client.IsOpen)
        {
            var allowed = !decision.CommandRequiresLiveFans || decision.BothFansSeen;
            if (allowed && ShouldWrite(decision.Command))
            {
                if (_client.TryWrite(decision.Command))
                {
                    _lastWritten = decision.Command;
                    _lastWriteAt = DateTime.UtcNow;
                }
                else
                {
                    _lastWritten = null;
                    reason = Rejected;
                }
            }
        }

        _fan1.Observe(reading.Fan1Rpm);
        _fan2.Observe(reading.Fan2Rpm);
        _missedPolls = reading.Ok ? 0 : _missedPolls + 1;

        if (reading.Ok)
        {
            var hottest = reading.Hottest?.Celsius ?? _trace.LastOrDefault()?.Celsius ?? 0;
            _trace.Add(new TraceSample
            {
                Celsius = hottest,
                Rpm = Math.Max(_fan1.Current ?? 0, _fan2.Current ?? 0)
            });
            if (_trace.Count > TraceLength)
                _trace.RemoveAt(0);
        }

        var label = decision.Mode == ControlMode.Bios
            ? "Firmware"
            : decision.CommandedLevel is int level ? $"Level {level}" : "Waiting";

        return new FanUpdate
        {
            Reason = reason,
            Refusal = decision.Refusal,
            Mode = decision.Mode,
            IsLatched = decision.IsLatched,
            CanTakeControl = decision.CanTakeControl,
            Fan1Rpm = _fan1.Current,
            Fan2Rpm = _fan2.Current,
            Fan1Held = _fan1.Held,
            Fan2Held = _fan2.Held,
            CommandLabel = label,
            CommandedLevel = decision.Mode == ControlMode.Bios ? null : decision.CommandedLevel,
            Sensors = reading.Sensors.Length > 0 ? reading.Sensors : Placeholders(),
            Trace = _trace.ToArray(),
            DriverStatus = _client.Status,
            Health = DescribeHealth(reading),
            ManualLevel = _session.ManualLevel,
            MissedPolls = _missedPolls,
            DriverOpen = _client.IsOpen,
            RegistersRead = reading.RegistersRead,
            RegistersTotal = reading.RegistersTotal
        };
    }

    private bool ShouldWrite(byte command)
    {
        if (_lastWritten != command)
            return true;
        return Registers.IsLevelCommand(command) && DateTime.UtcNow - _lastWriteAt >= RefreshInterval;
    }

    private string DescribeHealth(FanReading reading)
    {
        string text;
        if (!_client.IsOpen)
            text = "";
        else if (!reading.Ok)
            text = _missedPolls == 1
                ? "The controller did not answer the last poll."
                : $"The controller has not answered for {_missedPolls} polls.";
        else if (reading.RegistersRead < reading.RegistersTotal)
            text = $"Read {reading.RegistersRead} of {reading.RegistersTotal} registers.";
        else
            text = $"Read all {reading.RegistersTotal} registers.";

        if (_fan1.Held || _fan2.Held)
            text = (text + " Showing the last fan speed that was read.").Trim();
        return text;
    }

    private FanUpdate Idle() => new()
    {
        Reason = FanSession.BiosSentence,
        Mode = ControlMode.Bios,
        IsLatched = false,
        CanTakeControl = false,
        CommandLabel = "Firmware",
        Sensors = Placeholders(),
        Trace = Array.Empty<TraceSample>(),
        DriverStatus = _client.Status,
        Health = "",
        ManualLevel = _session.ManualLevel
    };

    private static SensorSample[] Placeholders() =>
        Registers.TemperatureAddresses.Select(address => new SensorSample { Address = address }).ToArray();

    private sealed class HeldValue
    {
        private int _age;

        public int? Current { get; private set; }
        public bool Held { get; private set; }

        public void Observe(int? fresh)
        {
            if (fresh is not null)
            {
                Current = fresh;
                Held = false;
                _age = 0;
                return;
            }

            _age++;
            if (Current is not null && _age <= HoldPolls)
            {
                Held = true;
                return;
            }

            Current = null;
            Held = false;
        }
    }
}
