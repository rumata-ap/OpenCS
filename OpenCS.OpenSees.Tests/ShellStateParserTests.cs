using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellStateParserTests
{
    [Fact]
    public void ParseShellLayers_MapsStressAndStrainRowsToRequestedStep()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "state_order.json"), """
            {
              "version": 2,
              "shellLayerGroups": [
                { "sectionTag": 20, "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
                  "elementTags": [10, 11], "fileName": "stress.out", "componentCount": 5, "unit": "Pa",
                  "materialTag": 1, "layerKind": "Concrete", "sourceId": "concrete:1:0",
                  "centerZ": -0.075, "thickness": 0.05, "sectionFingerprint": "fp-a" },
                { "sectionTag": 20, "integrationPoint": 1, "layerIndex": 1, "responseKind": "strain",
                  "elementTags": [10, 11], "fileName": "strain.out", "componentCount": 5, "unit": "Pa",
                  "materialTag": 1, "layerKind": "Concrete", "sourceId": "concrete:1:0",
                  "centerZ": -0.075, "thickness": 0.05, "sectionFingerprint": "fp-a" }
              ],
              "beamFiberLocations": [],
              "optionalResponses": []
            }
            """);
            File.WriteAllText(Path.Combine(directory, "step_status.out"), """
            1 0 0.5 1 0
            2 0 1.0 1 0
            """);
            File.WriteAllText(Path.Combine(directory, "stress.out"),
                "0.5 1 2 3 4 5 6 7 8 9 10\n1.0 11 12 13 14 15 16 17 18 19 20\n");
            File.WriteAllText(Path.Combine(directory, "strain.out"),
                "0.5 0.1 0.2 0.3 0.4 0.5 0.6 0.7 0.8 0.9 1.0\n1.0 1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0\n");

            var parser = new ShellStateParser();
            var catalog = parser.ParseCatalog(directory);
            Assert.Equal(ShellStateCatalogProvenanceKind.V2WithProvenance, catalog.ProvenanceKind);
            var states = parser.ParseShellLayers(directory, catalog, 10, 1, 1, 2);

            var state = Assert.Single(states);
            Assert.Equal(2, state.Key.StepIndex);
            Assert.Equal(1.0, state.Key.LoadFactor);
            Assert.Equal([11d, 12, 13, 14, 15], state.Stress);
            Assert.Equal([1.1, 1.2, 1.3, 1.4, 1.5], state.Strain);
            Assert.NotNull(state.CatalogGroup);
            Assert.Equal(20, state.CatalogGroup!.SectionTag);
            Assert.Equal(1, state.CatalogGroup!.MaterialTag);
            Assert.Equal(ShellLayerKind.Concrete, state.CatalogGroup!.LayerKind);
            Assert.Equal("concrete:1:0", state.CatalogGroup!.SourceId);
            Assert.Equal(-0.075, state.CatalogGroup!.CenterZ!.Value, 12);
            Assert.Equal("fp-a", state.CatalogGroup!.SectionFingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseShellLayers_AllowsMissingFileWhenNoStepConverged()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "state_order.json"), """
            {
              "version": 1,
              "shellLayerGroups": [
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress", "elementTags": [10], "fileName": "missing.out", "componentCount": 5 },
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "strain", "elementTags": [10], "fileName": "missing-strain.out", "componentCount": 5 }
              ],
              "beamFiberLocations": [],
              "optionalResponses": []
            }
            """);
            File.WriteAllText(Path.Combine(directory, "step_status.out"), "1 0 0.5 0 1\n");

            var parser = new ShellStateParser();
            var states = parser.ParseShellLayers(directory, parser.ParseCatalog(directory), 10, 1, 1, 1);

            Assert.Empty(states);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseShellLayers_RejectsWrongColumnCount()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "state_order.json"), """
            {
              "version": 1,
              "shellLayerGroups": [
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress", "elementTags": [10], "fileName": "stress.out", "componentCount": 5 },
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "strain", "elementTags": [10], "fileName": "strain.out", "componentCount": 5 }
              ],
              "beamFiberLocations": [],
              "optionalResponses": []
            }
            """);
            File.WriteAllText(Path.Combine(directory, "step_status.out"), "1 0 0.5 1 0\n");
            File.WriteAllText(Path.Combine(directory, "stress.out"), "0.5 1 2 3\n0.5 1 2 3 4 5\n");
            File.WriteAllText(Path.Combine(directory, "strain.out"), "0.5 1 2 3 4 5\n0.5 1 2 3 4 5\n");

            var ex = Assert.Throws<OpenSeesResultException>(() =>
                new ShellStateParser().ParseShellLayers(
                    directory,
                    new ShellStateParser().ParseCatalog(directory),
                    10, 1, 1, 1));

            Assert.Equal("WrongColumnCount", ex.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseCatalog_V2_RequiresProvenanceMetadata()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "state_order.json"), """
            {
              "version": 2,
              "shellLayerGroups": [
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
                  "elementTags": [10], "fileName": "stress.out", "componentCount": 5 }
              ],
              "beamFiberLocations": [],
              "optionalResponses": []
            }
            """);

            var ex = Assert.Throws<OpenSeesResultException>(() =>
                new ShellStateParser().ParseCatalog(directory));

            Assert.Equal("InvalidStateOrder", ex.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseCatalog_V1_IsLegacyWithoutProvenance()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "state_order.json"), """
            {
              "version": 1,
              "shellLayerGroups": [
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
                  "elementTags": [10], "fileName": "stress.out", "componentCount": 5 }
              ],
              "beamFiberLocations": [],
              "optionalResponses": []
            }
            """);

            ShellStateCatalog catalog = new ShellStateParser().ParseCatalog(directory);

            Assert.Equal(ShellStateCatalogProvenanceKind.V1LegacyMissing, catalog.ProvenanceKind);
            ShellLayerStateGroup group = Assert.Single(catalog.ShellLayerGroups);
            Assert.Null(group.MaterialTag);
            Assert.Null(group.LayerKind);
            Assert.Null(group.SourceId);
            Assert.Null(group.SectionFingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseShellLayers_V1Legacy_RejectsWithProvenanceMissingInsteadOfDefaults()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "state_order.json"), """
            {
              "version": 1,
              "shellLayerGroups": [
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
                  "elementTags": [10], "fileName": "stress.out", "componentCount": 5 },
                { "integrationPoint": 1, "layerIndex": 1, "responseKind": "strain",
                  "elementTags": [10], "fileName": "strain.out", "componentCount": 5 }
              ],
              "beamFiberLocations": [],
              "optionalResponses": []
            }
            """);
            File.WriteAllText(Path.Combine(directory, "step_status.out"), "1 0 1.0 1 0\n");
            File.WriteAllText(Path.Combine(directory, "stress.out"), "1.0 1 2 3 4 5\n");
            File.WriteAllText(Path.Combine(directory, "strain.out"), "1.0 0.1 0.2 0.3 0.4 0.5\n");

            var ex = Assert.Throws<OpenSeesResultException>(() =>
                new ShellStateParser().ParseShellLayers(
                    directory, new ShellStateParser().ParseCatalog(directory), 10, 1, 1, 1));

            Assert.Equal("state_catalog_provenance_missing", ex.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseCatalog_V2_TwoSectionsWithSameLayerCount_ProduceSeparateGroups()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "state_order.json"), """
            {
              "version": 2,
              "shellLayerGroups": [
                { "sectionTag": 20, "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
                  "elementTags": [10], "fileName": "s20.out", "componentCount": 5, "unit": "Pa",
                  "materialTag": 1, "layerKind": "Concrete", "sourceId": "a:0",
                  "centerZ": -0.05, "thickness": 0.1, "sectionFingerprint": "fp-a" },
                { "sectionTag": 21, "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress",
                  "elementTags": [11], "fileName": "s21.out", "componentCount": 5, "unit": "Pa",
                  "materialTag": 2, "layerKind": "Concrete", "sourceId": "b:0",
                  "centerZ": -0.05, "thickness": 0.1, "sectionFingerprint": "fp-b" }
              ],
              "beamFiberLocations": [],
              "optionalResponses": []
            }
            """);

            ShellStateCatalog catalog = new ShellStateParser().ParseCatalog(directory);

            Assert.Equal(2, catalog.ShellLayerGroups.Count);
            Assert.Equal([20, 21], catalog.ShellLayerGroups.Select(group => group.SectionTag).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs-shell-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void MaterialStateKey_PreservesShellLayerIdentityAndFiveComponentState()
    {
        var key = new RCShellMaterialStateKey(
            StepIndex: 2,
            StageIndex: 1,
            LoadFactor: 0.75,
            ElementTag: 10,
            IntegrationPoint: 2,
            LocationIndex: 4,
            LocationKind: ShellMaterialStateLocationKind.ShellLayer);

        var state = new RCShellLayerState(
            key,
            MaterialTag: 8,
            ShellLayerKind.Concrete,
            Stress: [1, 2, 3, 4, 5],
            Strain: [6, 7, 8, 9, 10]);

        Assert.Equal(ShellMaterialStateLocationKind.ShellLayer, state.Key.LocationKind);
        Assert.Equal(4, state.Key.LocationIndex);
        Assert.Equal(5, state.Stress.Count);
        Assert.Equal(5, state.Strain.Count);
    }

    [Fact]
    public void BeamFiberState_UsesScalarStressAndStrain()
    {
        var key = new RCShellMaterialStateKey(
            StepIndex: 3,
            StageIndex: 1,
            LoadFactor: 1.0,
            ElementTag: 200,
            IntegrationPoint: 3,
            LocationIndex: 0,
            LocationKind: ShellMaterialStateLocationKind.BeamFiber);

        var state = new RCShellBeamFiberState(key, StressPa: 120_000_000, Strain: 0.0006);

        Assert.Equal(ShellMaterialStateLocationKind.BeamFiber, state.Key.LocationKind);
        Assert.Equal(0, state.Key.LocationIndex);
        Assert.Equal(120_000_000, state.StressPa);
        Assert.Equal(0.0006, state.Strain);
    }

    [Fact]
    public void RecordingPolicy_DefaultsToBothShellLayersAndBeamFibers()
    {
        var policy = new ShellStateRecordingPolicy();

        Assert.True(policy.RecordShellLayers);
        Assert.True(policy.RecordBeamFibers);
        Assert.Null(policy.ShellIntegrationPoints);
        Assert.Null(policy.BeamIntegrationPoints);
        Assert.Null(policy.BeamFiberIndices);
    }
}
