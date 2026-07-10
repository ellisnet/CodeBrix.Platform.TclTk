using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Tests.Oracle;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The bind-dispatch oracle: every scenario under Assets/BindOracle is
/// replayed through the TkCanvas event system and its binding-firing log
/// compared, line by line, against the fixture captured from REAL Tk 8.6
/// (wish + event generate) by tools/layout-oracle/generate_fixtures.sh.
/// A failure means bind-tag order, pattern specificity, break semantics, or
/// substitution values diverge from classic Tk.
/// </summary>
public class BindOracleTests
{
    /// <summary>Enumerates the scenario file names (without directory).</summary>
    public static TheoryData<string> ScenarioNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(BindOracleScenario.FixtureDirectory, "*.scenario"))
            {
                data.Add(Path.GetFileName(path));
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Scenario_dispatch_matches_real_tk(string scenarioName)
    {
        //Arrange
        string scenarioPath = Path.Combine(BindOracleScenario.FixtureDirectory, scenarioName);
        string fixturePath = Path.ChangeExtension(scenarioPath, ".expected");
        string[] expected = File.ReadAllLines(fixturePath);

        //Act
        IReadOnlyList<string> actual = BindOracleScenario.Run(scenarioPath);

        //Assert
        actual.Should().Equal(expected);
    }
}
