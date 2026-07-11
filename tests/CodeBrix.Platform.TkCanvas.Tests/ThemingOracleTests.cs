using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Tests.Oracle;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The B.12 theming oracle: every scenario under Assets/ThemingOracle —
/// option-database matching/priority, ttk::style lookup resolution, and
/// tk_setPalette shade derivation — is replayed through the Theming engines
/// and compared, line by line, against the fixture captured from REAL Tk 8.6
/// (wish) by tools/layout-oracle/generate_fixtures.sh.
/// </summary>
public class ThemingOracleTests
{
    /// <summary>Enumerates the scenario file names (without directory).</summary>
    public static TheoryData<string> ScenarioNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(ThemingOracleScenario.FixtureDirectory, "*.scenario"))
            {
                data.Add(Path.GetFileName(path));
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Scenario_results_match_real_tk(string scenarioName)
    {
        //Arrange
        string scenarioPath = Path.Combine(ThemingOracleScenario.FixtureDirectory, scenarioName);
        string fixturePath = Path.ChangeExtension(scenarioPath, ".expected");
        string[] expected = File.ReadAllLines(fixturePath);

        //Act
        IReadOnlyList<string> actual = ThemingOracleScenario.Run(scenarioPath);

        //Assert
        actual.Should().Equal(expected);
    }
}
