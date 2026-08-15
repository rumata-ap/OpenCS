using System.Xml.Linq;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки локализации preview набора усилий.</summary>
public class FemMemberForceSetLocalizationTests
{
    [Fact]
    public void ForceSetResources_ContainSameKeysInBothDictionaries()
    {
        var ru = ResourceKeys(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.ru-RU.xaml"));
        var en = ResourceKeys(Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.en-US.xaml"));
        var expected = new[]
        {
            "FemResultMemberCreateForceSet", "FemForceSetPreviewTitle",
            "FemForceSetPreviewSchema", "FemForceSetPreviewMember",
            "FemForceSetPreviewStep", "FemForceSetPreviewNode",
            "FemForceSetPreviewPosition", "FemForceSetPreviewSource",
            "FemForceSetPreviewTag", "FemForceSetPreviewDescription",
            "FemForceSetPreviewUnits", "FemForceSetSourceOnly",
            "FemForceSetSourceLeft", "FemForceSetSourceRight",
            "FemForceSetPreviewN", "FemForceSetPreviewMx",
            "FemForceSetPreviewMy", "FemForceSetPreviewVx",
            "FemForceSetPreviewVy", "FemForceSetPreviewT",
            "FemForceSetSave", "FemForceSetCancel",
            "FemForceSetDefaultTag", "FemForceSetDefaultDescription",
            "FemForceSetMemberNotFound", "FemForceSetMissingSourceNode",
            "FemForceSetCannotOrient", "FemForceSetNoMeshElements",
            "FemForceSetMissingMeshNode", "FemForceSetMissingForce",
            "FemForceSetNonFiniteForce", "FemForceSetInvalidTopology",
            "FemForceSetReusedElement", "FemForceSetDuplicateElementPair",
            "FemForceSetEqualElementNodes", "FemForceSetZeroElementLength",
            "FemForceSetNotConverged"
        };

        Assert.All(expected, key =>
        {
            Assert.Contains(key, ru);
            Assert.Contains(key, en);
        });
    }

    static HashSet<string> ResourceKeys(string path) =>
        ResourceDocumentKeys(XDocument.Load(path));

    static HashSet<string> ResourceDocumentKeys(XDocument document)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root!.Elements()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
