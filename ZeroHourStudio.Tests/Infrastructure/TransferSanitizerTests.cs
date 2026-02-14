using FluentAssertions;
using ZeroHourStudio.Infrastructure.Transfer;
using Xunit;

namespace ZeroHourStudio.Tests.Infrastructure;

/// <summary>
/// اختبارات TransferSanitizer — مطهّر كود INI المنقول
/// </summary>
public class TransferSanitizerTests
{
    private readonly TransferSanitizer _sanitizer = new();

    // ════════════════════════════════════════
    // === إزالة القيود ===
    // ════════════════════════════════════════

    [Fact]
    public void Sanitize_RemovesPrerequisite()
    {
        var content = """
            Object TestUnit
              Prerequisite = AmericaStrategyCenter
              Prerequisite = AmericaWarFactory
              BuildCost = 1000
            End
            """;

        var result = _sanitizer.Sanitize(content);

        result.Success.Should().BeTrue();
        result.LinesRemoved.Should().Be(2);
        result.SanitizedContent.Should().NotContain("Prerequisite");
        result.SanitizedContent.Should().Contain("BuildCost = 1000");
    }

    [Fact]
    public void Sanitize_RemovesRequiredUpgrade()
    {
        var content = """
            Weapon TestWeapon
              RequiredUpgrade = Upgrade_AmericaChemicalSuits
              PrimaryDamage = 500.0
            End
            """;

        var result = _sanitizer.Sanitize(content);

        result.LinesRemoved.Should().Be(1);
        result.SanitizedContent.Should().NotContain("RequiredUpgrade");
        result.SanitizedContent.Should().Contain("PrimaryDamage = 500.0");
    }

    [Fact]
    public void Sanitize_RemovesScienceRequired()
    {
        var content = """
            CommandButton Command_Test
              ScienceRequired = SCIENCE_General
              Command = DO_PRODUCE
            End
            """;

        var result = _sanitizer.Sanitize(content);

        result.LinesRemoved.Should().Be(1);
        result.SanitizedContent.Should().NotContain("ScienceRequired");
    }

    [Fact]
    public void Sanitize_RemovesRank()
    {
        var content = """
            Object TestUnit
              Rank = 3
              BuildCost = 2000
            End
            """;

        var result = _sanitizer.Sanitize(content);

        result.LinesRemoved.Should().Be(1);
        result.SanitizedContent.Should().NotContain("Rank");
    }

    // ════════════════════════════════════════
    // === خيار التعليق ===
    // ════════════════════════════════════════

    [Fact]
    public void Sanitize_CommentOut_PreservesOriginalAsComment()
    {
        var content = """
            Object TestUnit
              Prerequisite = SomeBuilding
              BuildCost = 500
            End
            """;

        var options = new SanitizeOptions { CommentOutInsteadOfRemove = true };
        var result = _sanitizer.Sanitize(content, options);

        result.LinesCommented.Should().Be(1);
        result.LinesRemoved.Should().Be(0);
        result.SanitizedContent.Should().Contain("; [ZHS-SANITIZED]");
        result.SanitizedContent.Should().Contain("Prerequisite");
    }

    // ════════════════════════════════════════
    // === ضمان قابلية البناء ===
    // ════════════════════════════════════════

    [Fact]
    public void SanitizeCommandButton_InjectsDoProduceIfMissing()
    {
        var content = """
            CommandButton Command_Test
              ButtonImage = SNTest
              Object = TestUnit
            End
            """;

        var result = _sanitizer.SanitizeCommandButton(content);

        result.LinesInjected.Should().Be(1);
        result.SanitizedContent.Should().Contain("Command = DO_PRODUCE");
    }

    [Fact]
    public void SanitizeCommandButton_DoesNotInjectIfCommandExists()
    {
        var content = """
            CommandButton Command_Test
              Command = DO_PRODUCE
              ButtonImage = SNTest
            End
            """;

        var result = _sanitizer.SanitizeCommandButton(content);

        result.LinesInjected.Should().Be(0);
    }

    // ════════════════════════════════════════
    // === خيارات انتقائية ===
    // ════════════════════════════════════════

    [Fact]
    public void Sanitize_SelectiveOptions_OnlyRemovesSelected()
    {
        var content = """
            Object TestUnit
              Prerequisite = SomeBuilding
              RequiredUpgrade = SomeUpgrade
              BuildCost = 1000
            End
            """;

        var options = new SanitizeOptions
        {
            StripPrerequisites = true,
            StripUpgradeRequirements = false  // لا تحذف RequiredUpgrade
        };

        var result = _sanitizer.Sanitize(content, options);

        result.LinesRemoved.Should().Be(1);
        result.SanitizedContent.Should().NotContain("Prerequisite");
        result.SanitizedContent.Should().Contain("RequiredUpgrade");
    }

    [Fact]
    public void Sanitize_PreservesCommentsAndEmptyLines()
    {
        var content = """
            ; This is a comment
            Object TestUnit
              ; Internal comment
              Prerequisite = SomeBuilding
              BuildCost = 1000
            End
            """;

        var result = _sanitizer.Sanitize(content);

        result.SanitizedContent.Should().Contain("; This is a comment");
        result.SanitizedContent.Should().Contain("; Internal comment");
    }

    // ════════════════════════════════════════
    // === تطهير حزمة كاملة ===
    // ════════════════════════════════════════

    [Fact]
    public void SanitizeTransferBundle_ProcessesAllParts()
    {
        var objectContent = """
            Object TestUnit
              Prerequisite = SomeBuilding
              BuildCost = 1000
            End
            """;

        var weaponContent = """
            Weapon TestWeapon
              RequiredUpgrade = SomeUpgrade
              PrimaryDamage = 500
            End
            """;

        var buttonContent = """
            CommandButton Command_Test
              ScienceRequired = SCIENCE_General
              ButtonImage = SNTest
              Object = TestUnit
            End
            """;

        var result = _sanitizer.SanitizeTransferBundle(
            objectContent,
            new[] { weaponContent },
            new[] { buttonContent });

        result.LinesRemoved.Should().Be(3); // Prerequisite + RequiredUpgrade + ScienceRequired
        result.LinesInjected.Should().Be(1); // Command = DO_PRODUCE
        result.Changes.Should().HaveCountGreaterOrEqualTo(4);
    }
}
