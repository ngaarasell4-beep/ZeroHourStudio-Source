using FluentAssertions;
using ZeroHourStudio.Infrastructure.Services;
using Xunit;

namespace ZeroHourStudio.Tests.Infrastructure;

/// <summary>
/// اختبارات CommandChainService — المحرك العلائقي لـ SAGE
/// </summary>
public class CommandChainServiceTests
{
    // ════════════════════════════════════════
    // === نماذج INI للاختبار ===
    // ════════════════════════════════════════

    private const string SampleCommandSetIni = """
        CommandSet ChinaWarFactoryCommandSet
          1 = Command_ConstructChinaBattlemaster
          2 = Command_ConstructChinaDragonTank
          3 = Command_ConstructChinaGatlingTank
          4 = Command_ConstructChinaOverlord
          5 = NONE
          6 = NONE
        End

        CommandSet USAWarFactoryCommandSet
          1 = Command_ConstructUSACrusader
          2 = Command_ConstructUSAPaladin
          3 = Command_ConstructUSAHumvee
          4 = NONE
        End
        """;

    private const string SampleCommandButtonIni = """
        CommandButton Command_ConstructChinaBattlemaster
          Command = DO_PRODUCE
          Object = ChinaBattlemaster
          ButtonImage = SNBattlemaster
          TextLabel = OBJECT:ChinaBattlemaster
        End

        CommandButton Command_ConstructChinaDragonTank
          Command = DO_PRODUCE
          Object = ChinaDragonTank
          ButtonImage = SNDragonTank
          TextLabel = OBJECT:ChinaDragonTank
        End

        CommandButton Command_ConstructUSACrusader
          Command = DO_PRODUCE
          Object = AmericaCrusader
          ButtonImage = SNCrusader
        End
        """;

    private const string SampleObjectIni = """
        Object ChinaWarFactory
          CommandSet = ChinaWarFactoryCommandSet
          Draw = W3DModelDraw ModuleTag_Draw
            DefaultConditionState
              Model = NBWarFact
            End
          End
          Body = ActiveBody ModuleTag_Body
            MaxHealth = 2000
          End
        End

        Object USAWarFactory
          CommandSet = USAWarFactoryCommandSet
          Body = ActiveBody ModuleTag_Body
            MaxHealth = 2500
          End
        End
        """;

    // ════════════════════════════════════════
    // === اختبارات التحليل (Parsing) ===
    // ════════════════════════════════════════

    [Fact]
    public void ParseCommandSets_ExtractsAllSets()
    {
        var service = BuildServiceFromContent();

        service.CommandSetCount.Should().Be(2);
    }

    [Fact]
    public void ParseCommandButtons_ExtractsAllButtons()
    {
        var service = BuildServiceFromContent();

        service.CommandButtonCount.Should().Be(3);
    }

    [Fact]
    public void ParseObjectCommandSets_LinksObjectToSet()
    {
        var service = BuildServiceFromContent();

        service.ObjectCount.Should().Be(2);
    }

    // ════════════════════════════════════════
    // === اختبارات الاستعلام العلائقي ===
    // ════════════════════════════════════════

    [Fact]
    public void GetBuildingCommandBar_ChinaWarFactory_Returns12Slots_PaddedToMinimum()
    {
        var service = BuildServiceFromContent();

        var bar = service.GetBuildingCommandBar("ChinaWarFactory");

        bar.ObjectName.Should().Be("ChinaWarFactory");
        bar.CommandSetName.Should().Be("ChinaWarFactoryCommandSet");
        // SAGE يعرض 12 خانة كحد أدنى — 6 معرّفة + 6 حشو فارغ
        bar.TotalSlots.Should().Be(12);
        bar.OccupiedSlots.Should().Be(4);
        bar.EmptySlots.Should().Be(8);
    }

    [Fact]
    public void GetBuildingCommandBar_ResolvesButtonDetails()
    {
        var service = BuildServiceFromContent();

        var bar = service.GetBuildingCommandBar("ChinaWarFactory");
        var slot1 = bar.Slots[0]; // Slot 1

        slot1.IsOccupied.Should().BeTrue();
        slot1.ButtonName.Should().Be("Command_ConstructChinaBattlemaster");
        slot1.ButtonImage.Should().Be("SNBattlemaster");
        slot1.Command.Should().Be("DO_PRODUCE");
        slot1.UnitObject.Should().Be("ChinaBattlemaster");
    }

    [Fact]
    public void GetBuildingCommandBar_NONESlots_AreEmpty()
    {
        var service = BuildServiceFromContent();

        var bar = service.GetBuildingCommandBar("ChinaWarFactory");
        var slot5 = bar.Slots[4]; // Slot 5

        slot5.IsOccupied.Should().BeFalse();
        slot5.ButtonName.Should().BeNull();
    }

    [Fact]
    public void GetBuildingCommandBar_UnknownObject_Returns12EmptySlots()
    {
        var service = BuildServiceFromContent();

        var bar = service.GetBuildingCommandBar("NonExistentBuilding");

        bar.CommandSetName.Should().Be("UNKNOWN");
        bar.TotalSlots.Should().Be(12);
        bar.EmptySlots.Should().Be(12);
    }

    [Fact]
    public void GetBuildingCommandBar_USAWarFactory_HasCorrectSlots()
    {
        var service = BuildServiceFromContent();

        var bar = service.GetBuildingCommandBar("USAWarFactory");

        bar.CommandSetName.Should().Be("USAWarFactoryCommandSet");
        bar.OccupiedSlots.Should().Be(3);
        bar.Slots[0].ButtonName.Should().Be("Command_ConstructUSACrusader");
        bar.Slots[0].ButtonImage.Should().Be("SNCrusader");
    }

    // ════════════════════════════════════════
    // === اختبارات البحث عن خانة فارغة ===
    // ════════════════════════════════════════

    [Fact]
    public void FindEmptySlotInBuilding_ChinaWarFactory_FindsSlot5()
    {
        var service = BuildServiceFromContent();

        var (hasSpace, slotNumber, _) = service.FindEmptySlotInBuilding("ChinaWarFactory");

        hasSpace.Should().BeTrue();
        slotNumber.Should().Be(5);
    }

    [Fact]
    public void FindEmptySlotInBuilding_UnknownObject_ReturnsSlot1()
    {
        var service = BuildServiceFromContent();

        var (hasSpace, slotNumber, _) = service.FindEmptySlotInBuilding("NonExistent");

        hasSpace.Should().BeTrue();
        slotNumber.Should().Be(1);
    }

    // ════════════════════════════════════════
    // === اختبارات الأدوات المساعدة ===
    // ════════════════════════════════════════

    [Fact]
    public void GetAllBuildingObjects_ReturnsAllObjects()
    {
        var service = BuildServiceFromContent();

        var objects = service.GetAllBuildingObjects();

        objects.Should().Contain("ChinaWarFactory");
        objects.Should().Contain("USAWarFactory");
    }

    [Fact]
    public void FindObjectByCommandSet_FindsCorrectObject()
    {
        var service = BuildServiceFromContent();

        var obj = service.FindObjectByCommandSet("ChinaWarFactoryCommandSet");

        obj.Should().Be("ChinaWarFactory");
    }

    // ════════════════════════════════════════
    // === أدوات البناء ===
    // ════════════════════════════════════════

    /// <summary>
    /// بناء الخدمة من محتوى نصي مباشر (بدون ملفات حقيقية)
    /// </summary>
    private static CommandChainService BuildServiceFromContent()
    {
        var service = new CommandChainService();

        // استخدام الـ Reflection للوصول إلى دوال التحليل الداخلية
        // أو الأفضل: استدعاء الدوال العامة البديلة
        var parseCommandSets = typeof(CommandChainService)
            .GetMethod("ParseCommandSets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var parseCommandButtons = typeof(CommandChainService)
            .GetMethod("ParseCommandButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var parseObjectCommandSets = typeof(CommandChainService)
            .GetMethod("ParseObjectCommandSets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        parseCommandSets?.Invoke(service, new object[] { SampleCommandSetIni });
        parseCommandButtons?.Invoke(service, new object[] { SampleCommandButtonIni });
        parseObjectCommandSets?.Invoke(service, new object[] { SampleObjectIni });

        return service;
    }
}
