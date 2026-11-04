using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Harness.Tests;

// The pure rules of test impact and platform reach, and what TestImpact derives.
public sealed class TestImpactRulesTests
{
    static readonly TestImpactDefinition s_default = TestImpactDefinition.Default;

    [Theory]
    [InlineData("uitest/Delivery/TC1201_SignOffTests.cs", true, Label = "ui-test")]
    [InlineData("tests/Delivery/SignOffViewModelTests.cs", true, Label = "tests-directory")]
    [InlineData("test/Delivery/SignOffViewModelTests.cs", true, Label = "test-directory")]
    [InlineData("src/App.Tests/SignOffViewModelTests.cs", true, Label = "tests-project")]
    [InlineData("src/Delivery/SignOffViewModel.cs", false, Label = "source")]
    [InlineData("tests/Delivery/data.json", false, Label = "not-cs")]
    public void TestGlobs_DefaultGlobs_MatchUiAndUnitTests(string path, bool expected)
    {
        // Arrange
        var tests = Globs.Of(s_default.TestGlobs);

        // Act
        var isTest = tests.Matches(path);

        // Assert
        Assert.Equal(expected, isTest);
    }

    [Theory]
    [InlineData("src/Delivery/SignOffViewModel.cs", true, Label = "cs")]
    [InlineData("src/Delivery/SignOffPage.XAML", true, Label = "xaml-any-case")]
    [InlineData("src/Delivery/strings.resx", false, Label = "resx")]
    public void IsSource_DefaultExtensions_MatchCsAndXaml(string path, bool expected)
    {
        // Arrange
        var definition = s_default;

        // Act
        var isSource = TestImpactRules.IsSource(path, definition);

        // Assert
        Assert.Equal(expected, isSource);
    }

    [Theory]
    [InlineData("src/App/Main.kt", true, Label = "configured")]
    [InlineData("src/App/Main.cs", false, Label = "default-replaced")]
    public void IsSource_ConfiguredExtensions_ReplaceTheDefaults(string path, bool expected)
    {
        // Arrange
        var definition = s_default with { SourceExtensions = [".kt"] };

        // Act
        var isSource = TestImpactRules.IsSource(path, definition);

        // Assert
        Assert.Equal(expected, isSource);
    }

    [Fact]
    public void TestCaseIdsIn_SeveralSpellingsAndARepeat_AreDistinctAndAscending()
    {
        // Arrange
        const string text = "[Trait(\"TestCase\", \"TC1202\")] // TC_1201 and TC-1201 again; TC12 is too short";
        var pattern = s_default.TestCaseIdPattern;

        // Act
        var ids = TestImpactRules.TestCaseIdsIn(text, pattern);

        // Assert
        Assert.Equal([1201, 1202], ids);
    }

    [Fact]
    public void TestCaseIdsIn_GroupTooLargeForAnInt_IsSkipped()
    {
        // Arrange
        var pattern = new Regex("Case([0-9]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        // Act
        var ids = TestImpactRules.TestCaseIdsIn("Case99999999999 Case7", pattern);

        // Assert
        Assert.Equal([7], ids);
    }

    [Theory]
    [InlineData("src/Platforms/Android/MainActivity.cs", null, "android", Label = "android-path")]
    [InlineData("src/Platforms/iOS/AppDelegate.cs", null, "ios", Label = "ios-path")]
    [InlineData("src/Scanner.Android.cs", null, "android", Label = "android-suffix")]
    [InlineData("src/Scanner.iOS.cs", null, "ios", Label = "ios-suffix")]
    [InlineData("src/Scanner.cs", "#if ANDROID\nusing Android.OS;\n#endif\n", "android", Label = "if-android")]
    [InlineData("src/Scanner.cs", "#if IOS\n#endif\n", "ios", Label = "if-ios")]
    [InlineData("src/Scanner.cs", "#if __ANDROID__\n#endif\n", "android", Label = "if-double-underscore")]
    [InlineData("src/Scanner.cs", "  #if ANDROID || IOS\n#endif\n", "android,ios", Label = "if-both")]
    [InlineData("src/Scanner.cs", "#if DEBUG\n#elif __IOS__\n#endif\n", "ios", Label = "elif")]
    [InlineData("src/Scanner.Android.cs", "#if IOS\n#endif\n", "android,ios", Label = "path-and-directive")]
    [InlineData("src/Scanner.cs", "// ANDROID and IOS in a comment\n", "", Label = "words-outside-a-directive")]
    [InlineData("src/Scanner.cs", "#if ANDROIDX\n#endif\n", "", Label = "longer-symbol")]
    [InlineData("src/Scanner.cs", null, "", Label = "shared-by-path")]
    public void PlatformsOf_PathAndContent_NamesThePlatforms(string path, string? content, string expected)
    {
        // Arrange
        var platforms = TestImpactRules.CompileMatchers(s_default.Platforms);

        // Act
        var names = TestImpactRules.PlatformsOf(path, content, platforms);

        // Assert
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), names);
    }

    [Fact]
    public void PlatformsOf_ConfiguredPlatform_IsFoundByItsUpperCasedName()
    {
        // Arrange
        var platforms = TestImpactRules.CompileMatchers(
            ImmutableSortedDictionary<string, ImmutableArray<string>>.Empty.Add("tizen", ["**/Platforms/Tizen/**"]));

        // Act
        var names = TestImpactRules.PlatformsOf("src/Scanner.cs", "#if TIZEN\n#endif\n", platforms);

        // Assert
        Assert.Equal(["tizen"], names);
    }

    [Fact]
    public void Of_ChangedTests_SortsByPathAndDerivesTheIds()
    {
        // Arrange
        TestReference[] changedTests =
        [
            new("uitest/Delivery/TC1202_SignOffPhotoTests.cs", [1202], [], [1202]),
            new("tests/Delivery/SignOffViewModelTests.cs", [1201, 1300], [], []),
        ];

        // Act
        var impact = TestImpact.Of(changedTests);

        // Assert
        Assert.Equal(
            ["tests/Delivery/SignOffViewModelTests.cs", "uitest/Delivery/TC1202_SignOffPhotoTests.cs"],
            impact.ChangedTests.Select(reference => reference.TestFile));
        Assert.Equal([1201, 1202, 1300], impact.TestCaseIds);
    }

    [Fact]
    public void TestsFor_MethodsCarryTheId_ListsTheMethods()
    {
        // Arrange
        var impact = TestImpact.Of(
        [
            new("tests/A.cs", [2], [new("ATests.Second_TC2", [2]), new("ATests.Third_TC3", [3])], []),
            new("tests/B.cs", [2], [new("BTests.Other_TC2", [2])], []),
        ]);

        // Act
        var tests = impact.TestsFor(2);

        // Assert
        Assert.Equal(["ATests.Second_TC2", "BTests.Other_TC2"], tests);
    }

    [Fact]
    public void TestsFor_NoMethodCarriesTheId_ListsTheFilesWhoseNameCarriesIt()
    {
        // Arrange
        var impact = TestImpact.Of(
        [
            new("uitest/TC1201_SignOffTests.cs", [1201], [], [1201]),
            new("tests/C.cs", [1201], [], []),
            new("tests/B.cs", [], [], []),
        ]);

        // Act
        var tests = impact.TestsFor(1201);

        // Assert
        Assert.Equal(["uitest/TC1201_SignOffTests.cs (file name)"], tests);
    }

    [Fact]
    public void TestsFor_IdOnlyInTheText_ListsThePath()
    {
        // Arrange
        var impact = TestImpact.Of([new("tests/C.cs", [1201], [], [])]);

        // Act
        var tests = impact.TestsFor(1201);

        // Assert
        Assert.Equal(["tests/C.cs"], tests);
    }

    [Fact]
    public void TestMethodsIn_IdOnTheDeclarationOrAnAttribute_NamesTheClassAndTheMethod()
    {
        // Arrange
        const string text = """
            /// <summary>The class Unrelated carries TC9 in a comment.</summary>
            public sealed class PeersTests
            {
                [Fact]
                public void Peers_appear_within_5s_TC1202()
                {
                }

                [Test, Property("TestCase", "TC1201")]
                [Category("Smoke")]
                public async Task SignsOff()
                {
                }

                public void Helper()
                {
                }
            }
            """;

        // Act
        var methods = TestImpactRules.TestMethodsIn(text, s_default.TestCaseIdPattern);

        // Assert
        Assert.Equal(["PeersTests.Peers_appear_within_5s_TC1202", "PeersTests.SignsOff"], methods.Select(method => method.Name));
        Assert.Equal([[1202], [1201]], methods.Select(method => method.TestCaseIds.ToArray()));
    }

    [Fact]
    public void TestMethodsIn_IdTooFarAboveADeclaration_IsLeftOut()
    {
        // Arrange
        const string text = """
            class FarTests
            {
                const string Id = "TC1201";
                int _one;
                int _two;
                int _three;
                int _four;
                int _five;
                public void Unrelated()
                {
                }
            }
            """;

        // Act
        var methods = TestImpactRules.TestMethodsIn(text, s_default.TestCaseIdPattern);

        // Assert
        Assert.Empty(methods);
    }

    [Fact]
    public void TestCaseIdsIn_DefaultPatternAfterAnUnderscore_IsAMatch()
    {
        // Arrange
        const string text = "public void Peers_appear_within_5s_TC1201() // not ATC1202 or TC1203a4";

        // Act
        var ids = TestImpactRules.TestCaseIdsIn(text, s_default.TestCaseIdPattern);

        // Assert
        Assert.Equal([1201, 1203], ids);
    }

    [Fact]
    public void TestCaseIdsIn_DefaultPatternOnAFileName_ReadsTheIdBeforeTheUnderscore()
    {
        // Arrange
        var text = "uitest/Delivery/TC1201_SignOffTests.cs";

        // Act
        var ids = TestImpactRules.TestCaseIdsIn(text, TestImpactDefinition.Default.TestCaseIdPattern);

        // Assert
        Assert.Equal([1201], ids);
    }
}
