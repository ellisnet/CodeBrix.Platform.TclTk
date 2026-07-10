using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Tests.Oracle;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The pack layout oracle: every scenario under Assets/LayoutOracle is
/// replayed through the TkCanvas layout engine and its geometry compared,
/// line by line, against the fixture captured from REAL Tk 8.6 (wish) by
/// tools/layout-oracle/generate_fixtures.sh. A failure means the engine
/// diverges from classic Tk behavior.
/// </summary>
public class PackOracleTests
{
    /// <summary>Enumerates the scenario file names (without directory).</summary>
    public static TheoryData<string> ScenarioNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(OracleScenario.FixtureDirectory, "*.scenario"))
            {
                data.Add(Path.GetFileName(path));
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Scenario_geometry_matches_real_tk(string scenarioName)
    {
        //Arrange
        string scenarioPath = Path.Combine(OracleScenario.FixtureDirectory, scenarioName);
        string fixturePath = Path.ChangeExtension(scenarioPath, ".expected");
        string[] expected = File.ReadAllLines(fixturePath);

        //Act
        IReadOnlyList<string> actual = OracleScenario.Run(scenarioPath);

        //Assert
        actual.Should().Equal(expected);
    }
}
