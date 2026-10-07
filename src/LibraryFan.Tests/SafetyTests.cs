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
        Assert.Equal(FanSession.ThermalHandoff, trip.Reason);

        var refused = session.Request(ControlMode.Manual, 4);
        Assert.False(refused.HasCommand);
        Assert.Contains("stays in control", refused.Refusal);
        Assert.Equal(ControlMode.Bios, session.Mode);

        var stillLatched = session.Tick(Sample(70));
        Assert.True(stillLatched.IsLatched);
        Assert.False(stillLatched.HasCommand);

        var resumed = session.Request(ControlMode.Library);
        Assert.Equal(ControlMode.Library, resumed.Mode);
        Assert.False(resumed.IsLatched);
        Assert.StartsWith("Library is holding level", resumed.Reason);
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
    public void Bios_mode_also_hands_back_and_latches_at_80()
    {
        var session = new FanSession();
        session.Tick(Sample(60));
        var trip = session.Tick(Sample(81));
        Assert.True(trip.IsLatched);
        Assert.Equal(Registers.BiosControl, trip.Command);
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
        Assert.Equal(FanSession.ReadHandoff, third.Reason);
    }

    [Fact]
    public void A_good_poll_resets_the_bad_poll_streak()
    {
        var session = new FanSession();
        session.Tick(Sample(60));
        session.Request(ControlMode.Library);
        session.Tick(FanReading.Unreadable);
        session.Tick(FanReading.Unreadable);
        session.Tick(Sample(60));
        session.Tick(FanReading.Unreadable);
        var decision = session.Tick(FanReading.Unreadable);
        Assert.False(decision.IsLatched);
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

        Assert.Equal((byte)2, session.Tick(Sample(60)).Command);
        Assert.Equal((byte)1, session.Tick(Sample(58)).Command);
        Assert.Equal((byte)1, session.Tick(Sample(55)).Command);
        Assert.Equal((byte)0, session.Tick(Sample(50)).Command);
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
    public void Choosing_bios_writes_the_restore_once()
    {
        var session = new FanSession();
        session.Tick(Sample(60));
        session.Request(ControlMode.Manual, 2);
        var bios = session.Request(ControlMode.Bios);
        Assert.True(bios.HasCommand);
        Assert.Equal(Registers.BiosControl, bios.Command);
        var steady = session.Tick(Sample(60));
        Assert.False(steady.HasCommand);
    }

    [Fact]
    public void Closing_the_controller_restores_bios_without_touching_ports_in_the_simulator()
    {
        var sim = new SimulatedEc();
        var controller = new FanController(sim, new FanSession(), poll: false);
        controller.Poll();
        controller.Request(ControlMode.Manual, 3);
        Assert.Equal((byte)3, sim.LastCommand);
        controller.Dispose();
        Assert.Equal(Registers.BiosControl, sim.LastCommand);
        Assert.Equal(0, sim.PortTouches);
    }
}
