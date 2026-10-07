using LibraryFan;
using Xunit;
using static LibraryFan.Tests.Samples;

namespace LibraryFan.Tests;

public class SafetyTests
{
    [Fact]
    public void Manual_trips_at_80_and_refuses_until_it_cools()
    {
        var session = new FanSession();
        session.Tick(Sample(60));
        var manual = session.Request(ControlMode.Manual, 1);
        Assert.Equal("Manual is holding level 1.", manual.Reason);
        Assert.Equal((byte)1, manual.Command);

        var trip = session.Tick(Sample(80));
        Assert.Equal(ControlMode.Bios, trip.Mode);
        Assert.True(trip.IsLatched);
        Assert.True(trip.HasCommand);
        Assert.Equal(Registers.BiosControl, trip.Command);
        Assert.Equal("Handed control back to the machine because a sensor reached 80°C.", trip.Reason);

        var refused = session.Request(ControlMode.Manual, 4);
        Assert.False(refused.HasCommand);
        Assert.Contains("stays in control", refused.Refusal);
        Assert.Equal(ControlMode.Bios, session.Mode);

        session.Tick(Sample(70));
        var resumed = session.Request(ControlMode.Library);
        Assert.Equal(ControlMode.Library, resumed.Mode);
        Assert.False(resumed.IsLatched);
        Assert.Contains("Library is holding level", resumed.Reason);
    }

    [Fact]
    public void Library_trips_on_one_hot_sample()
    {
        var session = new FanSession();
        session.Tick(Sample(64));
        session.Request(ControlMode.Library);
        var trip = session.Tick(Sample(80));
        Assert.Equal(Registers.BiosControl, trip.Command);
        Assert.True(trip.IsLatched);
        Assert.DoesNotContain("level", trip.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_sensor_byte_0x80_is_not_eighty_degrees()
    {
        var sensors = CoolSensors(50);
        sensors[0] = new SensorSample { Address = 0x78, Raw = 0x80 };
        var session = new FanSession();
        session.Tick(Sample(50));
        session.Request(ControlMode.Manual, 2);
        var decision = session.Tick(Reading(sensors));
        Assert.Equal(ControlMode.Manual, decision.Mode);
        Assert.False(decision.IsLatched);
        Assert.Equal((byte)2, decision.Command);
    }

    [Fact]
    public void Three_bad_polls_hand_control_back_and_one_does_not()
    {
        var session = new FanSession();
        session.Tick(Sample(60));
        session.Request(ControlMode.Library);

        var once = session.Tick(FanReading.Unreadable);
        Assert.Equal(ControlMode.Library, once.Mode);
        Assert.False(once.IsLatched);

        session.Tick(FanReading.Unreadable);
        var third = session.Tick(FanReading.Unreadable);
        Assert.True(third.IsLatched);
        Assert.Equal(Registers.BiosControl, third.Command);
        Assert.Contains("could not be read", third.Reason);
    }

    [Fact]
    public void Library_stays_off_only_below_55_and_steps_down_after_three_degrees()
    {
        var session = new FanSession();
        session.Tick(Sample(48));
        var quiet = session.Request(ControlMode.Library);
        Assert.Equal((byte)0, quiet.Command);

        var level2 = session.Tick(Sample(64));
        Assert.Equal((byte)2, level2.Command);
        Assert.Contains("0x78 is 64°C", level2.Reason);

        var held = session.Tick(Sample(60));
        Assert.Equal((byte)2, held.Command);

        var down = session.Tick(Sample(58));
        Assert.Equal((byte)1, down.Command);

        var stillOn = session.Tick(Sample(55));
        Assert.Equal((byte)1, stillOn.Command);
        Assert.NotEqual((byte)0, stillOn.Command);
    }

    [Fact]
    public void Level_writes_wait_until_both_fans_have_been_read()
    {
        var session = new FanSession();
        var blocked = session.Tick(Sample(64, liveFans: false));
        var library = session.Request(ControlMode.Library);
        Assert.False(library.HasCommand);
        Assert.Contains("have not been read", library.Reason);

        var live = session.Tick(Sample(64));
        Assert.True(live.BothFansSeen);
        Assert.True(live.HasCommand);
        Assert.Equal((byte)2, live.Command);
        Assert.False(blocked.BothFansSeen);
    }

    [Fact]
    public void Disengaged_full_speed_is_rejected()
    {
        var ports = new FakePorts();
        Assert.False(ThinkPadEc.TryCommandBoth(ports, Registers.DisengagedFullSpeed));
        Assert.DoesNotContain(ports.Ops, op => op.Contains("out 40", StringComparison.Ordinal));
    }

    [Fact]
    public void Rpm_low_byte_is_read_before_the_high_byte()
    {
        var ports = new FakePorts { Fan1 = 1500, Fan2 = 1400 };
        Assert.True(ThinkPadEc.TryReadSnapshot(ports, out var reading));
        Assert.Equal(1500, reading.Fan1Rpm);
        Assert.Equal(1400, reading.Fan2Rpm);
        var fan2 = ports.Ops.IndexOf("out 01");
        var low = ports.Ops.IndexOf("out 84");
        var high = ports.Ops.IndexOf("out 85");
        Assert.True(fan2 >= 0 && fan2 < low && low < high);
    }

    [Fact]
    public void Stale_rpm_is_not_a_live_reading()
    {
        var ports = new FakePorts { Fan1 = 0x2000, Fan2 = 1000 };
        Assert.True(ThinkPadEc.TryReadSnapshot(ports, out var reading));
        Assert.False(reading.Fan1Live);
        Assert.Null(reading.Fan1Rpm);
        Assert.True(reading.Fan2Live);
    }

    [Fact]
    public void Closing_the_controller_restores_bios_without_touching_ports_in_the_simulator()
    {
        var sim = new SimulatedEc();
        var controller = new FanController(sim, new FanSession());
        controller.Request(ControlMode.Manual, 3);
        controller.Dispose();
        Assert.Equal(Registers.BiosControl, sim.LastCommand);
        Assert.Equal(0, sim.PortTouches);
    }
}

static class Samples
{
    public static FanReading Sample(int temperature, bool liveFans = true) 
    {
        var sensors = CoolSensors(temperature);
        sensors[0] = new SensorSample { Address = 0x78, Raw = (byte)temperature };
        return Reading(sensors, liveFans);
    }

    public static SensorSample[] CoolSensors(int temperature)
    {
        return Registers.TemperatureAddresses
            .Select(address => new SensorSample
            {
                Address = address,
                Raw = (byte)Math.Clamp(temperature - 5, 1, 79)
            })
            .ToArray();
    }

    public static FanReading Reading(SensorSample[] sensors, bool liveFans = true) => new()
    {
        Ok = true,
        Fan1Rpm = liveFans ? 1200 : null,
        Fan2Rpm = liveFans ? 1100 : null,
        Fan1Live = liveFans,
        Fan2Live = liveFans,
        Sensors = sensors
    };
}

sealed class FakePorts : IPortIo
{
    public byte[] Ram = new byte[256];
    public List<string> Ops = new();
    public int Fan1 { get; set; } = 1000;
    public int Fan2 { get; set; } = 1000;
    private byte _status;
    private byte _pending;
    private byte _writeOffset;
    private int _writePhase;

    public bool ReadPort(ushort port, out byte value)
    {
        if (port == Registers.ControlPort)
        {
            value = _status;
            return true;
        }

        value = _pending;
        _status = 0;
        Ops.Add($"in {value:X2}");
        return true;
    }

    public bool WritePort(ushort port, byte value)
    {
        if (port == Registers.ControlPort)
        {
            _writePhase = value == Registers.CommandWrite ? 1 : 0;
            _status = 0;
            Ops.Add($"cmd {value:X2}");
            return true;
        }

        Ops.Add($"out {value:X2}");
        if (_writePhase == 1)
        {
            _writeOffset = value;
            _writePhase = 2;
            _status = 0;
            return true;
        }

        if (_writePhase == 2)
        {
            Ram[_writeOffset] = value;
            _writePhase = 0;
            _status = 0;
            return true;
        }

        _pending = value switch
        {
            Registers.FanSpeedLow => (byte)(SelectedRpm & 0xFF),
            Registers.FanSpeedHigh => (byte)((SelectedRpm >> 8) & 0xFF),
            _ => Ram[value]
        };
        _status = Registers.StatusOutputFull;
        return true;
    }

    private int SelectedRpm => Ram[Registers.FanSelect] == Registers.Fan2 ? Fan2 : Fan1;
}
