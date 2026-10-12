using Xunit;

namespace LibraryFan.Tests;

public class HardwareProfileTests
{
    private static readonly MachineIdentity P1 = new("20TH000XUS", "N2VET41W (1.26)");
    private static readonly MachineIdentity Other = new("20XX0000", "XXXX");

    [Fact]
    public void The_built_in_map_is_this_machine_and_no_other()
    {
        Assert.True(EcMap.BuiltIn.Matches(P1));
        Assert.False(EcMap.BuiltIn.Matches(Other));
        Assert.False(EcMap.BuiltIn.Matches(new MachineIdentity("20TH000XUS", "OTHER")));
        Assert.Equal(Registers.FanSelect, EcMap.BuiltIn.FanSelect);
        Assert.Equal(Registers.TemperatureAddresses, EcMap.BuiltIn.Temperatures);
        Assert.Equal(Registers.SnapshotRegisters, EcMap.BuiltIn.SnapshotRegisters);
    }

    [Fact]
    public void An_unknown_machine_is_not_given_the_built_in_map()
    {
        var choice = HardwareProfile.Choose(Other, Missing(), Missing());
        Assert.Null(choice.Map);
        Assert.Contains("hardware-profile.json", choice.Problem);
    }

    [Fact]
    public void This_machine_uses_the_built_in_map_when_no_file_is_present()
    {
        var choice = HardwareProfile.Choose(P1, Missing(), Missing());
        Assert.Same(EcMap.BuiltIn, choice.Map);
        Assert.Null(choice.Problem);
    }

    [Fact]
    public void The_setup_list_names_lenovo_dell_hp_and_framework()
    {
        Assert.Contains(KnownMachines.All, machine => machine.Manufacturer == "Lenovo" && machine.Map is not null);
        Assert.Contains(KnownMachines.All, machine => machine.Manufacturer == "Dell" && machine.Map is null);
        Assert.Contains(KnownMachines.All, machine => machine.Manufacturer == "HP" && machine.Map is null);
        Assert.Contains(KnownMachines.All, machine => machine.Manufacturer == "Framework" && machine.Map is null);
    }

    [Fact]
    public void A_confirmed_thinkpad_is_used_only_when_its_type_is_selected_and_matches()
    {
        var x1 = new MachineIdentity("20TK001US", "N2VET");
        var unselected = HardwareProfile.Choose(x1, Missing(), Missing());
        Assert.Null(unselected.Map);

        var selected = HardwareProfile.Choose(x1, Missing(), Missing(), "lenovo-x1-extreme-gen3");
        Assert.NotNull(selected.Map);
        Assert.Equal("ThinkPad X1 Extreme Gen 3", selected.Map!.Name);
        Assert.Equal(EcMap.BuiltIn.FanCommand, selected.Map.FanCommand);
        Assert.Equal(EcMap.BuiltIn.Temperatures, selected.Map.Temperatures);

        var p15 = HardwareProfile.Choose(new MachineIdentity("20YR00ABCD", ""), Missing(), Missing(), "lenovo-p15-gen2");
        Assert.Equal("ThinkPad P15 Gen 2", p15.Map!.Name);
        var p14s = HardwareProfile.Choose(new MachineIdentity("21G3001ABC", ""), Missing(), Missing(), "lenovo-p14s-gen5-intel");
        Assert.Equal("ThinkPad P14s Gen 5 Intel", p14s.Map!.Name);

        var wrongPickOnTheP1 = HardwareProfile.Choose(P1, Missing(), Missing(), "lenovo-x1-extreme-gen3");
        Assert.Same(EcMap.BuiltIn, wrongPickOnTheP1.Map);
    }

    [Fact]
    public void Another_manufacturer_is_never_given_the_thinkpad_map()
    {
        var dell = HardwareProfile.Choose(Other, Missing(), Missing(), "dell-smm");
        Assert.Null(dell.Map);
        Assert.Contains("different fan controller", dell.Problem);

        var dellOnTheP1 = HardwareProfile.Choose(P1, Missing(), Missing(), "dell-smm");
        Assert.Same(EcMap.BuiltIn, dellOnTheP1.Map);
    }

    [Fact]
    public void A_profile_is_used_only_on_the_product_it_names()
    {
        var path = TempProfile("""
            {
              "name": "Other ThinkPad",
              "product": "20XX0000",
              "fanSelect": "0x31",
              "fans": ["0x00"],
              "fanCommand": "0x2F",
              "speedLow": "0x84",
              "speedHigh": "0x85",
              "temperatures": ["0x78"]
            }
            """);
        try
        {
            var onItsMachine = HardwareProfile.Choose(Other, path, Missing());
            Assert.NotNull(onItsMachine.Map);
            Assert.Equal("Other ThinkPad", onItsMachine.Map!.Name);
            Assert.Equal(new byte[] { 0x00 }, onItsMachine.Map.Fans);
            Assert.Equal(new byte[] { 0x78 }, onItsMachine.Map.Temperatures);

            var onTheP1 = HardwareProfile.Choose(P1, path, Missing());
            Assert.Null(onTheP1.Map);
            Assert.Contains("20XX0000", onTheP1.Problem);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_profile_cannot_move_the_ports_or_turn_off_the_handoff()
    {
        Assert.Contains("0x62", Reject("""{"name":"X","product":"20XX0000","dataPort":"0x1600","controlPort":"0x1604","fanSelect":"0x31","fans":["0x00"],"fanCommand":"0x2F","speedLow":"0x84","speedHigh":"0x85","temperatures":["0x78"]}"""));
        Assert.Contains("80", Reject("""{"name":"X","product":"20XX0000","dangerCelsius":90,"fanSelect":"0x31","fans":["0x00"],"fanCommand":"0x2F","speedLow":"0x84","speedHigh":"0x85","temperatures":["0x78"]}"""));
        Assert.Contains("firmware", Reject("""{"name":"X","product":"20XX0000","biosControl":"0x40","fanSelect":"0x31","fans":["0x00"],"fanCommand":"0x2F","speedLow":"0x84","speedHigh":"0x85","temperatures":["0x78"]}"""));
    }

    [Fact]
    public void A_broken_profile_is_not_replaced_with_the_built_in_map()
    {
        var path = TempProfile("{ not json");
        try
        {
            var choice = HardwareProfile.Choose(P1, path, Missing());
            Assert.Null(choice.Map);
            Assert.NotNull(choice.Problem);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_custom_map_still_refuses_disengaged_full_speed()
    {
        var map = EcMap.Load("""
            {
              "name": "One fan",
              "product": "20XX0000",
              "fanSelect": "0x31",
              "fans": ["0x00", "0x01"],
              "fanCommand": "0x2F",
              "speedLow": "0x84",
              "speedHigh": "0x85",
              "temperatures": ["0x78", "0x7A"]
            }
            """).Map!;
        var ec = new FakeEc { Fan1Rpm = 1200, Fan2Rpm = 1100 };
        var reading = ThinkPadEc.ReadSnapshot(ec, map);
        Assert.Equal(4, reading.RegistersTotal);
        Assert.Equal(1200, reading.Fan1Rpm);
        Assert.False(ThinkPadEc.TryCommandBoth(ec, Registers.DisengagedFullSpeed, map));
        Assert.Equal(Registers.BiosControl, ec.FanCommands[0]);
    }

    private static string Reject(string json)
    {
        var path = TempProfile(json);
        try
        {
            return HardwareProfile.Choose(Other, path, Missing()).Problem ?? "";
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Missing() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "hardware-profile.json");

    private static string TempProfile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "library-fan-profiles", Guid.NewGuid().ToString("N"), "hardware-profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }
}
