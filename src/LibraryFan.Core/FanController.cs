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
    public required string CommandLabel { get; init; }
    public required SensorSample[] Sensors { get; init; }
    public required IReadOnlyList<TraceSample> Trace { get; init; }
    public required string DriverStatus { get; init; }
    public required int ManualLevel { get; init; }
}

public sealed class FanController : IDisposable
{
    private readonly IEcClient _client;
    private readonly FanSession _session;
    private readonly object _gate = new();
    private readonly List<TraceSample> _trace = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly Thread _thread;
    private bool _disposed;

    public FanController(IEcClient client, FanSession? session = null)
    {
        _client = client;
        _session = session ?? new FanSession(LibraryCurve.Load(LibraryCurve.DefaultPath()));
        _thread = new Thread(Loop) { IsBackground = true, Name = "Fan controller" };
        _thread.Start();
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

    public void SetSimTemperature(int celsius)
    {
        if (_client is not SimulatedEc simulated)
            return;
        simulated.HottestCelsius = celsius;
        TickNow();
    }

    public void FailSimReads(int count)
    {
        if (_client is not SimulatedEc simulated)
            return;
        simulated.FailReads(count);
        TickNow();
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
        _thread.Join(TimeSpan.FromSeconds(3));
        RestoreBios();
        _client.Dispose();
        _cancel.Dispose();
    }

    private void Loop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            TickNow();
            if (_cancel.Token.WaitHandle.WaitOne(2000))
                break;
        }
    }

    private void TickNow()
    {
        FanUpdate update;
        lock (_gate)
        {
            if (_disposed)
                return;
            var reading = _client.Read();
            var decision = _session.Tick(reading);
            update = Publish(decision, reading);
        }

        Updated?.Invoke(update);
    }

    private FanUpdate Publish(FanDecision decision, FanReading reading)
    {
        var applied = false;
        if (decision.HasCommand && _client.IsOpen)
        {
            var allowed = !decision.CommandRequiresLiveFans || decision.BothFansSeen;
            if (allowed)
                applied = _client.TryWrite(decision.Command);
        }

        var reason = decision.Reason;
        if (decision.CommandRequiresLiveFans && decision.HasCommand && !applied && decision.BothFansSeen)
            reason = "The embedded controller did not accept the fan command.";

        var hottest = reading.Hottest?.Celsius ?? _trace.LastOrDefault()?.Celsius ?? 0;
        var rpm = Math.Max(reading.Fan1Rpm ?? 0, reading.Fan2Rpm ?? 0);
        if (reading.Ok)
        {
            _trace.Add(new TraceSample { Celsius = hottest, Rpm = rpm });
            if (_trace.Count > 90)
                _trace.RemoveAt(0);
        }

        var commandLabel = decision.Mode == ControlMode.Bios
            ? "Firmware"
            : decision.CommandedLevel is int level ? $"Level {level}" : "Waiting";

        return new FanUpdate
        {
            Reason = reason,
            Refusal = decision.Refusal,
            Mode = decision.Mode,
            IsLatched = decision.IsLatched,
            CanTakeControl = decision.CanTakeControl,
            Fan1Rpm = reading.Fan1Rpm,
            Fan2Rpm = reading.Fan2Rpm,
            CommandLabel = commandLabel,
            Sensors = reading.Sensors,
            Trace = _trace.ToArray(),
            DriverStatus = _client.Status,
            ManualLevel = _session.ManualLevel
        };
    }
}
