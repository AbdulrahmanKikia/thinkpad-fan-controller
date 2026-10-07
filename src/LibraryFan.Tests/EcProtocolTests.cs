using Xunit;

namespace LibraryFan.Tests;

public class EcProtocolTests
{
    [Fact]
    public void Rpm_is_read_low_byte_first_after_selecting_the_fan()
    {
        var ec = new FakeEc { Fan1Rpm = 1500, Fan2Rpm = 1400 };
        var reading = ThinkPadEc.ReadSnapshot(ec);
        Assert.True(reading.Ok);
        Assert.Equal(1500, reading.Fan1Rpm);
        Assert.Equal(1400, reading.Fan2Rpm);

        var selectFan2 = ec.Ops.IndexOf("out 01");
        var low = ec.Ops.IndexOf("out 84");
        var high = ec.Ops.IndexOf("out 85");
        Assert.True(selectFan2 >= 0 && selectFan2 < low && low < high);
    }

    [Fact]
    public void Stale_rpm_word_is_not_live()
    {
        var ec = new FakeEc { Fan1Rpm = 0x2000, Fan2Rpm = 1000 };
        var reading = ThinkPadEc.ReadSnapshot(ec);
        Assert.False(reading.Fan1Live);
        Assert.Null(reading.Fan1Rpm);
        Assert.True(reading.Fan2Live);
        Assert.Equal(Registers.SnapshotRegisters, reading.RegistersRead);
    }

    [Fact]
    public void Zero_rpm_is_a_live_stopped_fan()
    {
        var ec = new FakeEc { Fan1Rpm = 0, Fan2Rpm = 0 };
        var reading = ThinkPadEc.ReadSnapshot(ec);
        Assert.True(reading.Fan1Live);
        Assert.Equal(0, reading.Fan1Rpm);
    }

    [Fact]
    public void Disengaged_full_speed_is_never_written()
    {
        var ec = new FakeEc();
        Assert.False(ThinkPadEc.TryCommandBoth(ec, Registers.DisengagedFullSpeed));
        Assert.False(ThinkPadEc.TryCommandBoth(ec, 0x47));
        Assert.DoesNotContain(ec.Ops, op => op.StartsWith("out 4", StringComparison.Ordinal));
        Assert.Equal(Registers.BiosControl, ec.FanCommands[0]);
        Assert.Equal(Registers.BiosControl, ec.FanCommands[1]);
    }

    [Fact]
    public void A_level_is_written_to_both_fans_and_read_back()
    {
        var ec = new FakeEc();
        Assert.True(ThinkPadEc.TryCommandBoth(ec, 3));
        Assert.Equal(3, ec.FanCommands[0]);
        Assert.Equal(3, ec.FanCommands[1]);
    }

    [Fact]
    public void A_dropped_write_is_retried_until_the_readback_matches()
    {
        var ec = new FakeEc { DropWrites = 2 };
        Assert.True(ThinkPadEc.TryCommandBoth(ec, 2));
        Assert.Equal(2, ec.FanCommands[0]);
        Assert.Equal(2, ec.FanCommands[1]);
    }

    [Fact]
    public void Restore_writes_0x80_to_both_fans()
    {
        var ec = new FakeEc();
        ec.FanCommands[0] = 2;
        ec.FanCommands[1] = 2;
        Assert.True(ThinkPadEc.TryCommandBoth(ec, Registers.BiosControl));
        Assert.Equal(Registers.BiosControl, ec.FanCommands[0]);
        Assert.Equal(Registers.BiosControl, ec.FanCommands[1]);
    }

    [Fact]
    public void One_timed_out_byte_is_retried_in_place()
    {
        var ec = new FakeEc { FailNextReads = 1 };
        var reading = ThinkPadEc.ReadSnapshot(ec);
        Assert.Equal(Registers.SnapshotRegisters, reading.RegistersRead);
        Assert.True(reading.Fan2Live);
    }
}
