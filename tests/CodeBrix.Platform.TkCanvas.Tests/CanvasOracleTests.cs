using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Tests.Oracle;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The canvas behavior oracle: every scenario under Assets/CanvasOracle is
/// replayed through <c>CanvasWidget.Execute</c> and its query outputs
/// compared, line by line, against the fixture captured from REAL Tk 8.6
/// (wish) by tools/layout-oracle/generate_fixtures.sh. A failure means the
/// canvas engine diverges from classic Tk behavior.
/// </summary>
public class CanvasOracleTests
{
    /// <summary>Enumerates the scenario file names (without directory).</summary>
    public static TheoryData<string> ScenarioNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(CanvasOracleScenario.FixtureDirectory, "*.scenario"))
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
        string scenarioPath = Path.Combine(CanvasOracleScenario.FixtureDirectory, scenarioName);
        string fixturePath = Path.ChangeExtension(scenarioPath, ".expected");
        string[] expected = File.ReadAllLines(fixturePath);

        //Act
        IReadOnlyList<string> actual = CanvasOracleScenario.Run(scenarioPath);

        //Assert
        actual.Should().Equal(expected);
    }
}
