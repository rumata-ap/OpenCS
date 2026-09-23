using System.Xml.Linq;
using Xunit;

namespace OpenCS.Tests;

public sealed class Sp63DeflectionResourceKeysTests
{
    static readonly string[] Keys =
    [
        "CalcTaskKind_sp63_deflection", "Sp63Deflection_GroupTitle", "Sp63Deflection_Axis",
        "Sp63Deflection_Scheme", "Sp63Deflection_SchemeUniform", "Sp63Deflection_SchemeMidpoint",
        "Sp63Deflection_SchemeCantilever", "Sp63Deflection_Coefficient", "Sp63Deflection_Span",
        "Sp63Deflection_Limit", "Sp63Deflection_Humidity", "Sp63Deflection_ForcesMode",
        "Sp63Deflection_ForcesTotalOnly", "Sp63Deflection_ForcesShare", "Sp63Deflection_ForcesManual",
        "Sp63Deflection_LongShare", "Sp63Deflection_UseManualForces", "Sp63Deflection_NLongManual",
        "Sp63Deflection_MxLongManual", "Sp63Deflection_MyLongManual", "Sp63Deflection_ConstantStiffnessNote",
        "Sp63Deflection_InputsHint", "Sp63Deflection_InvalidTaskParams", "Sp63Deflection_InvalidInput",
        "Sp63Deflection_InvalidOptions", "Sp63Deflection_ShapeNotSupported", "Sp63Deflection_InvalidSpan",
        "Sp63Deflection_InvalidLimit", "Sp63Deflection_InvalidLongTermShare", "Sp63Deflection_NonFiniteLoad",
        "Sp63Deflection_BiaxialLoad", "Sp63Deflection_ZeroMoment", "Sp63Deflection_LongMomentReversed",
        "Sp63Deflection_LongMomentExceedsTotal", "Sp63Deflection_MissingConcreteChars",
        "Sp63Deflection_MissingRebarChars", "Sp63Deflection_InvalidGeometry",
        "Sp63Deflection_CurvatureThroughTension", "Sp63Deflection_MissingConcreteClass",
        "Sp63Deflection_NonFiniteResult", "Sp63Deflection_IdealizedAxisMismatch",
        "Sp63Deflection_IdealizedAxisInconsistent", "Sp63Deflection_IdealizedRebarNotSupported",
        "Sp63Deflection_VerdictPassed", "Sp63Deflection_VerdictFailed", "Sp63Deflection_StatusNotApplicable",
        "Sp63Deflection_StatusInvalidInput", "Sp63Deflection_ResultJsonInvalid", "Sp63Deflection_CurvatureTitle",
        "Sp63Deflection_CheckTitle", "Sp63Deflection_SchemeLabel", "Sp63Deflection_CurvatureLabel",
        "Sp63Deflection_DeflectionLabel", "Sp63Deflection_LimitLabel", "Sp63Deflection_UtilizationLabel",
        "Sp63Deflection_ForceUnits", "Sp63Deflection_ResultContextFormat", "Sp63Deflection_CurvatureIndexColumn",
        "Sp63Deflection_AxialForceColumn", "Sp63Deflection_ModulusColumn",
        "Sp63Deflection_ReportTitle", "Sp63Deflection_ReportCurvature",
        "Sp63Deflection_ReportDeflection", "Sp63Deflection_ReportPassed", "Sp63Deflection_ReportFailed",
        "Sp63Deflection_ReportNotApplicable", "Sp63Deflection_ReportInvalid", "Sp63Deflection_ReportNoCurvature",
        "Sp63Deflection_ReportMessage"
    ];

    [Fact]
    public void RussianAndEnglishDictionariesContainAllDeflectionKeys()
    {
        var ru = KeysIn("Resources/Strings.ru-RU.xaml");
        var en = KeysIn("Resources/Strings.en-US.xaml");
        foreach (var key in Keys)
        {
            Assert.Contains(key, ru);
            Assert.Contains(key, en);
        }
        Assert.Equal(Keys.Length, Keys.Distinct().Count());
    }

    static HashSet<string> KeysIn(string path) => XDocument.Load(path).Descendants()
        .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value)
        .Where(value => value != null).Select(value => value!).ToHashSet();
}
