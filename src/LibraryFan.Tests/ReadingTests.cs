using Xunit;

namespace LibraryFan.Tests;

public class ReadingTests
{
    [Fact]
    public void One_dead_temperature_register_does_not_lose_the_snapshot()
    {
        var ec = new FakeEc();
        ec.DeadOffsets.Add(0x7B);
        var reading = ThinkPadEc.ReadSnapshot(ec);

        Assert.True(reading.Ok);
        Assert.Equal(Registers.SnapshotRegisters - 1, reading.RegistersRead);
        var dead = Assert.Single(reading.Sensors, sensor => !sensor.WasRead);
        Assert.Equal(0x7B, dead.Address);
        Assert.Equal(Registers.TemperatureAddresses.Length - 1, reading.ValidSensors.Count());
        Assert.True(reading.Fan1Live && reading.Fan2Live);
    }

    [Fact]
    public void Dead_fan_speed_registers_leave_the_temperatures_in_place()
    {
        var ec = new FakeEc();
        ec.DeadOffsets.Add(Registers.FanSpeedLow);
        var reading = ThinkPadEc.ReadSnapshot(ec);

        Assert.True(reading.Ok);
        Assert.Null(reading.Fan1Rpm);
        Assert.Null(reading.Fan2Rpm);
        Assert.False(reading.Fan1Live);
        Assert.Equal(Registers.TemperatureAddresses.Length, reading.ValidSensors.Count());
    }

    [Fact]
    public void A_dead_controller_is_unreadable_and_the_snapshot_is_abandoned_early()
    {
        var ec = new FakeEc { Dead = true };
        var reading = ThinkPadEc.ReadSnapshot(ec);

        Assert.False(reading.Ok);
        Assert.Equal(0, reading.RegistersRead);
        Assert.Empty(reading.ValidSensors);
        var attempted = ec.Ops.Count(op => op.StartsWith("cmd 80", StringComparison.Ordinal));
        Assert.True(attempted <= ThinkPadEc.DeadAfter * EcBus.Attempts * 2, $"{attempted} read commands were sent to a dead controller.");
    }

    [Fact]
    public void Missing_bytes_are_read_but_not_valid()
    {
        var ec = new FakeEc();
        ec.SetTemperature(0x79, 0x00);
        ec.SetTemperature(0xC0, 0x80);
        var reading = ThinkPadEc.ReadSnapshot(ec);

        var missing = reading.Sensors.Where(sensor => sensor.WasRead && !sensor.IsValid).Select(sensor => sensor.Address).ToArray();
        Assert.Equal(new byte[] { 0x79, 0xC0 }, missing);
        Assert.Equal(Registers.SnapshotRegisters, reading.RegistersRead);
    }

    [Fact]
    public void Held_fan_speed_survives_a_missed_poll_and_then_expires()
    {
        var sim = new SimulatedEc();
        var controller = new FanController(sim, new FanSession(), poll: false);
        var first = controller.Poll();
        Assert.False(first.Fan1Held);
        Assert.NotNull(first.Fan1Rpm);

        sim.DropRpm(FanController.HoldPolls + 1);
        var held = controller.Poll();
        Assert.True(held.Fan1Held);
        Assert.Equal(first.Fan1Rpm, held.Fan1Rpm);
        Assert.Contains("last fan speed", held.Health);

        FanUpdate last = held;
        for (var i = 1; i < FanController.HoldPolls; i++)
            last = controller.Poll();
        Assert.True(last.Fan1Held);

        var expired = controller.Poll();
        Assert.False(expired.Fan1Held);
        Assert.Null(expired.Fan1Rpm);

        var back = controller.Poll();
        Assert.False(back.Fan1Held);
        Assert.NotNull(back.Fan1Rpm);
    }

    [Fact]
    public void Held_speed_never_counts_as_a_live_fan()
    {
        var sim = new SimulatedEc();
        sim.DropRpm(1);
        var controller = new FanController(sim, new FanSession(), poll: false);
        controller.Poll();
        var library = controller.Request(ControlMode.Library);
        Assert.Contains("have not been read", library.Reason);
        Assert.Null(sim.LastCommand);
    }

    [Fact]
    public void A_level_is_written_once_and_again_only_when_it_changes()
    {
        var sim = new SimulatedEc();
        var controller = new FanController(sim, new FanSession(), poll: false);
        controller.Poll();
        controller.Request(ControlMode.Manual, 3);
        Assert.Equal(1, sim.WriteCount);

        controller.Poll();
        controller.Poll();
        Assert.Equal(1, sim.WriteCount);

        controller.Request(ControlMode.Manual, 4);
        Assert.Equal(2, sim.WriteCount);
        Assert.Equal((byte)4, sim.LastCommand);
    }

    [Fact]
    public void Three_unreadable_polls_restore_bios_through_the_controller()
    {
        var sim = new SimulatedEc();
        var controller = new FanController(sim, new FanSession(), poll: false);
        controller.Poll();
        controller.Request(ControlMode.Manual, 5);
        Assert.Equal((byte)5, sim.LastCommand);

        controller.FailSimReads(FanSession.BadPollsBeforeHandoff);
        Assert.Equal(Registers.BiosControl, sim.LastCommand);
        Assert.True(controller.Session.IsLatched);
    }

    [Fact]
    public void Health_reports_how_many_registers_answered()
    {
        var sim = new SimulatedEc();
        var controller = new FanController(sim, new FanSession(), poll: false);
        Assert.Equal($"Read all {Registers.SnapshotRegisters} registers.", controller.Poll().Health);
        sim.FailReads(1);
        var missed = controller.Poll().Health;
        Assert.StartsWith("The controller did not answer the last poll.", missed);
        Assert.EndsWith("Showing the last fan speed that was read.", missed);
    }
}
