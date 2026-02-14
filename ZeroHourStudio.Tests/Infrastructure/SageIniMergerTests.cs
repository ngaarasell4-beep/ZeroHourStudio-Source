using FluentAssertions;
using ZeroHourStudio.Infrastructure.Transfer;
using Xunit;

namespace ZeroHourStudio.Tests.Infrastructure;

/// <summary>
/// اختبارات محرك دمج SAGE INI
/// </summary>
public class SageIniMergerTests
{
    private readonly SageIniMerger _merger = new();

    // =========================================
    // === اختبارات التحليل (Parse) ===
    // =========================================

    [Fact]
    public void ParseContent_SingleWeapon_ExtractsCorrectly()
    {
        var content = """
            Weapon IskanderMissileWeapon
              PrimaryDamage = 500.0
              AttackRange = 1000.0
            End
            """;

        var ini = _merger.ParseContent(content);

        ini.Sections.Should().HaveCount(1);
        ini.Sections[0].Name.Should().Be("IskanderMissileWeapon");
        ini.Sections[0].Type.Should().Be(SectionType.Weapon);
        ini.Sections[0].RawLines.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public void ParseContent_MultipleSections_ExtractsAll()
    {
        var content = """
            Weapon TestWeapon1
              PrimaryDamage = 100.0
            End

            Weapon TestWeapon2
              PrimaryDamage = 200.0
            End

            Armor TestArmor1
              DEFAULT = 100%
            End
            """;

        var ini = _merger.ParseContent(content);

        ini.Sections.Should().HaveCount(3);
        ini.Sections[0].Name.Should().Be("TestWeapon1");
        ini.Sections[1].Name.Should().Be("TestWeapon2");
        ini.Sections[2].Name.Should().Be("TestArmor1");
        ini.Sections[2].Type.Should().Be(SectionType.Armor);
    }

    [Fact]
    public void ParseContent_NestedSubBlocks_HandlesDepthCorrectly()
    {
        var content = """
            Object RussiaTestTank
              Draw = W3DModelDraw ModuleTag_Draw
                DefaultConditionState
                  Model = RUTank
                End
                ConditionState MOVING
                  AnimationMode = LOOP
                End
              End
              Body = ActiveBody ModuleTag_Body
                MaxHealth = 500
              End
            End
            """;

        var ini = _merger.ParseContent(content);

        ini.Sections.Should().HaveCount(1);
        ini.Sections[0].Name.Should().Be("RussiaTestTank");
        ini.Sections[0].Type.Should().Be(SectionType.Object);
    }

    // =========================================
    // === اختبارات الدمج (Merge) ===
    // =========================================

    [Fact]
    public void Merge_NewSection_AddsSuccessfully()
    {
        var target = _merger.ParseContent("");
        var newSection = CreateSection("Weapon", "NewWeapon", "PrimaryDamage = 300.0");

        var result = _merger.Merge(target, newSection, MergeStrategy.Smart);

        result.Success.Should().BeTrue();
        result.Skipped.Should().BeFalse();
        result.Renamed.Should().BeFalse();
        target.Sections.Should().HaveCount(1);
    }

    [Fact]
    public void Merge_DuplicateIdentical_SkipsWithSmartStrategy()
    {
        var content = """
            Weapon ExistingWeapon
              PrimaryDamage = 100.0
            End
            """;
        var target = _merger.ParseContent(content);
        var duplicate = CreateSection("Weapon", "ExistingWeapon", "PrimaryDamage = 100.0");

        var result = _merger.Merge(target, duplicate, MergeStrategy.Smart);

        result.Success.Should().BeTrue();
        result.Skipped.Should().BeTrue();
        target.Sections.Should().HaveCount(1, "لا يجب تكرار بلوك متطابق");
    }

    [Fact]
    public void Merge_DuplicateDifferent_RenamesWithSmartStrategy()
    {
        var content = """
            Weapon ConflictWeapon
              PrimaryDamage = 100.0
            End
            """;
        var target = _merger.ParseContent(content);
        var different = CreateSection("Weapon", "ConflictWeapon", "PrimaryDamage = 999.0");

        var result = _merger.Merge(target, different, MergeStrategy.Smart);

        result.Success.Should().BeTrue();
        result.Renamed.Should().BeTrue();
        result.OriginalName.Should().Be("ConflictWeapon");
        target.Sections.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_SkipStrategy_DoesNotAdd()
    {
        var content = """
            Weapon SkipMe
              PrimaryDamage = 100.0
            End
            """;
        var target = _merger.ParseContent(content);
        var duplicate = CreateSection("Weapon", "SkipMe", "PrimaryDamage = 999.0");

        var result = _merger.Merge(target, duplicate, MergeStrategy.Skip);

        result.Success.Should().BeTrue();
        result.Skipped.Should().BeTrue();
        target.Sections.Should().HaveCount(1);
    }

    [Fact]
    public void Merge_ReplaceStrategy_RemovesOldAndAddsNew()
    {
        var content = """
            Weapon ReplaceMe
              PrimaryDamage = 100.0
            End
            """;
        var target = _merger.ParseContent(content);
        var replacement = CreateSection("Weapon", "ReplaceMe", "PrimaryDamage = 999.0");

        var result = _merger.Merge(target, replacement, MergeStrategy.Replace);

        result.Success.Should().BeTrue();
        result.Replaced.Should().BeTrue();
        target.Sections.Should().HaveCount(1);
        target.Sections[0].InnerContent.Should().Contain("999.0");
    }

    // =========================================
    // === اختبارات الدمج الدفعي (Batch) ===
    // =========================================

    [Fact]
    public void MergeBatch_MultipleSections_MergesAll()
    {
        var target = _merger.ParseContent("");
        var sections = new[]
        {
            CreateSection("Weapon", "BatchWeapon1", "PrimaryDamage = 100.0"),
            CreateSection("Weapon", "BatchWeapon2", "PrimaryDamage = 200.0"),
            CreateSection("Armor", "BatchArmor1", "DEFAULT = 50%"),
        };

        var result = _merger.MergeBatch(target, sections, MergeStrategy.Smart);

        result.Success.Should().BeTrue();
        result.TotalSections.Should().Be(3);
        result.AddedCount.Should().Be(3);
        result.ErrorCount.Should().Be(0);
        target.Sections.Should().HaveCount(3);
    }

    [Fact]
    public void MergeBatch_WithConflicts_ReportsCorrectCounts()
    {
        var content = """
            Weapon ExistingW
              PrimaryDamage = 100.0
            End
            """;
        var target = _merger.ParseContent(content);
        var sections = new[]
        {
            CreateSection("Weapon", "ExistingW", "PrimaryDamage = 100.0"),  // متطابق → تخطي
            CreateSection("Weapon", "ExistingW", "PrimaryDamage = 999.0"),  // مختلف → إعادة تسمية
            CreateSection("Weapon", "BrandNew", "PrimaryDamage = 500.0"),   // جديد → إضافة
        };

        var result = _merger.MergeBatch(target, sections, MergeStrategy.Smart);

        result.Success.Should().BeTrue();
        result.SkippedCount.Should().Be(1);
        result.RenamedCount.Should().BeGreaterOrEqualTo(1);
        result.AddedCount.Should().BeGreaterOrEqualTo(1);
    }

    // =========================================
    // === اختبارات الاستخراج ===
    // =========================================

    [Fact]
    public void ExtractSectionFromContent_FindsByName()
    {
        var content = """
            Weapon Alpha
              Damage = 10
            End

            Weapon Beta
              Damage = 20
            End
            """;

        var section = _merger.ExtractSectionFromContent(content, "Beta");

        section.Should().NotBeNull();
        section!.Name.Should().Be("Beta");
        section.FullContent.Should().Contain("20");
    }

    [Fact]
    public void ExtractSectionFromContent_ReturnsNull_WhenNotFound()
    {
        var content = """
            Weapon Alpha
              Damage = 10
            End
            """;

        var section = _merger.ExtractSectionFromContent(content, "NonExistent");

        section.Should().BeNull();
    }

    // =========================================
    // === أداة مساعدة ===
    // =========================================

    private static IniSection CreateSection(string type, string name, string innerContent)
    {
        var lines = new List<string>
        {
            $"{type} {name}",
        };
        lines.AddRange(innerContent.Split('\n').Select(l => $"  {l.Trim()}"));
        lines.Add("End");

        var sectionType = type switch
        {
            "Object" => SectionType.Object,
            "Weapon" => SectionType.Weapon,
            "Armor" => SectionType.Armor,
            "FXList" => SectionType.FXList,
            "CommandSet" => SectionType.CommandSet,
            _ => SectionType.Other,
        };

        return new IniSection
        {
            TypeName = type,
            Type = sectionType,
            Name = name,
            RawLines = lines,
        };
    }
}
