# РћРґРЅРѕРѕСЃРЅР°СЏ РґРёР°РіСЂР°РјРјР° N-M: РїР»Р°РЅ СЂРµР°Р»РёР·Р°С†РёРё

> **Р”Р»СЏ Р°РіРµРЅС‚РЅС‹С… РёСЃРїРѕР»РЅРёС‚РµР»РµР№:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (СЂРµРєРѕРјРµРЅРґСѓРµС‚СЃСЏ) РёР»Рё `superpowers:executing-plans` РґР»СЏ РІС‹РїРѕР»РЅРµРЅРёСЏ СЌС‚РѕРіРѕ РїР»Р°РЅР° РїРѕ Р·Р°РґР°С‡Р°Рј. РЁР°РіРё РёСЃРїРѕР»СЊР·СѓСЋС‚ С‡РµРєР±РѕРєСЃС‹ (`- [x]`) РґР»СЏ РѕС‚СЃР»РµР¶РёРІР°РЅРёСЏ.

**Р¦РµР»СЊ:** Р”РѕР±Р°РІРёС‚СЊ Р±РёР±Р»РёРѕС‚РµС‡РЅС‹Р№ Рё OpenCS-task РІРµСЂС‚РёРєР°Р»СЊРЅС‹Р№ СЃСЂРµР· РѕРґРЅРѕРѕСЃРЅРѕР№ РґРёР°РіСЂР°РјРјС‹ `N-M`, РІС‹РїРѕР»РЅСЏСЋС‰РёР№ РїРѕСЃР»РµРґРѕРІР°С‚РµР»СЊРЅСѓСЋ СЃРµСЂРёСЋ РёР·РѕР»РёСЂРѕРІР°РЅРЅС‹С… monotonic momentвЂ“curvature СЂР°СЃС‡С‘С‚РѕРІ РґР»СЏ Р·Р°РґР°РЅРЅС‹С… РїСЂРѕРґРѕР»СЊРЅС‹С… СЃРёР».

**РђСЂС…РёС‚РµРєС‚СѓСЂР°:** РЎСѓС‰РµСЃС‚РІСѓСЋС‰РёР№ `SectionAnalysisService` СЃС‚Р°РЅРµС‚ СЂРµР°Р»РёР·Р°С†РёРµР№ РЅРµР±РѕР»СЊС€РѕРіРѕ РєРѕРЅС‚СЂР°РєС‚Р° РёСЃРїРѕР»РЅРёС‚РµР»СЏ. РќРѕРІС‹Р№ `SectionInteractionService` Р±СѓРґРµС‚ РІР°Р»РёРґРёСЂРѕРІР°С‚СЊ СЃРїРёСЃРѕРє СЃРёР», РїРѕСЃР»РµРґРѕРІР°С‚РµР»СЊРЅРѕ РІС‹Р·С‹РІР°С‚СЊ СЌС‚РѕС‚ РєРѕРЅС‚СЂР°РєС‚, РІС‹Р±РёСЂР°С‚СЊ РїРѕСЃР»РµРґРЅСЋСЋ СЃРѕС€РµРґС€СѓСЋСЃСЏ СЃС‚СЂРѕРєСѓ РєР°Р¶РґРѕР№ РёСЃС‚РѕСЂРёРё Рё Р°РіСЂРµРіРёСЂРѕРІР°С‚СЊ СЃС‚Р°С‚СѓСЃС‹. WPF РѕСЃС‚Р°С‘С‚СЃСЏ Р±РµР· РѕС‚РґРµР»СЊРЅРѕРіРѕ РіСЂР°С„РёРєР°: task СЃРѕС…СЂР°РЅСЏРµС‚ С‚РёРїРёР·РёСЂРѕРІР°РЅРЅС‹Р№ `SectionInteractionResult` РІ `CalcResult.DataJson`, Р° РєР°Р¶РґР°СЏ С‚РѕС‡РєР° СЃРѕС…СЂР°РЅСЏРµС‚ СЃРѕР±СЃС‚РІРµРЅРЅС‹Рµ Р°СЂС‚РµС„Р°РєС‚С‹ stage 0вЂ“1.

**РўРµС…РЅРѕР»РѕРіРёРё:** .NET 9, C#, СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёР№ `OpenCS.OpenSees`, `System.Text.Json`, `CancellationToken`, xUnit, РІРЅРµС€РЅРёР№ Tcl/OpenSees backend.

---

## РљРѕРЅС‚РµРєСЃС‚ Рё РЅРµРёР·РјРµРЅСЏРµРјС‹Рµ СЃРѕРіР»Р°С€РµРЅРёСЏ

- Р Р°Р±РѕС‚Р°С‚СЊ РІ РІРµС‚РєРµ `feature/opensees-section-interaction` РІ worktree `C:\Users\ponomarev\Documents\devel\OpenCS\.worktrees\opensees-section-interaction`.
- Backend С…СЂР°РЅРёС‚ СЃРёР»С‹ РІ `N`, РјРѕРјРµРЅС‚С‹ РІ `NВ·m`, РєСЂРёРІРёР·РЅСѓ РІ `1/m`; РїР°СЂР°РјРµС‚СЂС‹ OpenCS task РїСЂРёРЅРёРјР°СЋС‚ `axialForces` РІ `kN` Рё РєРѕРЅРІРµСЂС‚РёСЂСѓСЋС‚ РёС… СЂРѕРІРЅРѕ РѕРґРёРЅ СЂР°Р· С‡РµСЂРµР· `CScoreUnitConverter.KiloNewtonsToNewtons`.
- РџРѕСЂСЏРґРѕРє `axialForces` СЃРѕС…СЂР°РЅСЏРµС‚СЃСЏ. РЎРїРёСЃРѕРє РЅРµ РїСѓСЃС‚РѕР№, РІСЃРµ Р·РЅР°С‡РµРЅРёСЏ РєРѕРЅРµС‡РЅС‹Рµ, С‚РѕС‡РЅС‹Рµ РґСѓР±Р»РёРєР°С‚С‹ Р·Р°РїСЂРµС‰РµРЅС‹.
- Р”Р»СЏ РєР°Р¶РґРѕР№ СЃРёР»С‹ СЃРѕР·РґР°С‘С‚СЃСЏ РѕС‚РґРµР»СЊРЅС‹Р№ РІС‹Р·РѕРІ `SectionAnalysisService`; РµРіРѕ `OpenSeesArtifactStore` СѓР¶Рµ СЃРѕР·РґР°С‘С‚ СѓРЅРёРєР°Р»СЊРЅС‹Р№ РєР°С‚Р°Р»РѕРі Рё СЃРѕС…СЂР°РЅСЏРµС‚ `script.tcl`, `manifest.json`, `stdout.txt`, `stderr.txt`, `exit.json` Рё recorder-С„Р°Р№Р»С‹.
- РџРѕСЃР»РµРґРЅСЏСЏ С‚РѕС‡РєР° РґРёР°РіСЂР°РјРјС‹ Р±РµСЂС‘С‚СЃСЏ РєР°Рє `Rows.LastOrDefault(row => row.Converged)`, Р° РЅРµ РєР°Рє РїРѕСЃР»РµРґРЅСЏСЏ СЃС‚СЂРѕРєР° СЃС…РѕРґРёРјРѕСЃС‚Рё. РџРѕСЌС‚РѕРјСѓ С‡Р°СЃС‚РёС‡РЅРѕ СЃРѕС€РµРґС€Р°СЏСЃСЏ РёСЃС‚РѕСЂРёСЏ РјРѕР¶РµС‚ СЃРѕС…СЂР°РЅРёС‚СЊ РїРѕР»РµР·РЅСѓСЋ РїРѕСЃР»РµРґРЅСЋСЋ СЃРѕС€РµРґС€СѓСЋСЃСЏ РїР°СЂСѓ `N-M`, РЅРѕ СЃР°РјР° С‚РѕС‡РєР° РѕСЃС‚Р°С‘С‚СЃСЏ `not_converged`.
- РС‚РѕРіРѕРІС‹Р№ СЃС‚Р°С‚СѓСЃ: `error`, РµСЃР»Рё С…РѕС‚СЏ Р±С‹ РѕРґРЅР° С‚РѕС‡РєР° РёРјРµРµС‚ `error`; РёРЅР°С‡Рµ `not_converged`, РµСЃР»Рё С…РѕС‚СЏ Р±С‹ РѕРґРЅР° С‚РѕС‡РєР° РЅРµ РёРјРµРµС‚ `ok`; РёРЅР°С‡Рµ `ok`.
- `N-Mx-My`, target-force РїР°СЂС‹, РїР°СЂР°Р»Р»РµР»СЊРЅС‹Рµ Р·Р°РїСѓСЃРєРё Рё WPF-РіСЂР°С„РёРє РІ СЌС‚РѕС‚ РїР»Р°РЅ РЅРµ РІС…РѕРґСЏС‚.

## РљР°СЂС‚Р° С„Р°Р№Р»РѕРІ

РЎРѕР·РґР°С‚СЊ:

- `OpenCS.OpenSees/Analysis/SectionInteractionRequest.cs` вЂ” Р·Р°РїСЂРѕСЃ Рё РІР°Р»РёРґР°С†РёСЏ СЃРїРёСЃРєР° СЃРёР».
- `OpenCS.OpenSees/Analysis/SectionInteractionPoint.cs` вЂ” РѕРґРЅР° С‚РѕС‡РєР° СЂРµР·СѓР»СЊС‚Р°С‚Р°.
- `OpenCS.OpenSees/Analysis/SectionInteractionResult.cs` вЂ” РёС‚РѕРі РєСЂРёРІРѕР№ Рё Р°РіСЂРµРіРёСЂРѕРІР°РЅРЅС‹Р№ СЃС‚Р°С‚СѓСЃ.
- `OpenCS.OpenSees/Services/ISectionAnalysisExecutor.cs` вЂ” РєРѕРЅС‚СЂР°РєС‚ РѕРґРЅРѕРіРѕ РІРЅСѓС‚СЂРµРЅРЅРµРіРѕ Р°РЅР°Р»РёР·Р°.
- `OpenCS.OpenSees/Services/SectionInteractionService.cs` вЂ” РїРѕСЃР»РµРґРѕРІР°С‚РµР»СЊРЅР°СЏ РѕСЂРєРµСЃС‚СЂР°С†РёСЏ С‚РѕС‡РµРє.
- `OpenCS/Tasks/OpenSeesSectionInteractionParams.cs` вЂ” JSON-РїР°СЂР°РјРµС‚СЂС‹ РЅРѕРІРѕР№ task.
- `OpenCS/Tasks/OpenSeesSectionInteractionHandler.cs` вЂ” Р°РґР°РїС‚Р°С†РёСЏ `CrossSection` Рё Р·Р°РїСѓСЃРє СЃРµСЂРІРёСЃР°.

РР·РјРµРЅРёС‚СЊ:

- `OpenCS.OpenSees/Services/SectionAnalysisService.cs` вЂ” СЂРµР°Р»РёР·РѕРІР°С‚СЊ `ISectionAnalysisExecutor`.
- `OpenCS.OpenSees.Tests/SectionInteractionTests.cs` вЂ” unit-С‚РµСЃС‚С‹ РјРѕРґРµР»Рё Рё РѕСЂРєРµСЃС‚СЂР°С†РёРё.
- `OpenCS.OpenSees.Tests/OpenSeesIntegrationTests.cs` вЂ” СЂРµР°Р»СЊРЅС‹Р№ opt-in С‚РµСЃС‚ С‚СЂС‘С… С‚РѕС‡РµРє `N-M`.
- `OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs` вЂ” JSON-РєРѕРЅС‚СЂР°РєС‚, РєРѕРЅРІРµСЂС‚Р°С†РёСЏ РµРґРёРЅРёС† Рё СЂРµРіРёСЃС‚СЂР°С†РёСЏ kind.
- `OpenCS/Tasks/TaskRunner.cs` вЂ” Р·Р°СЂРµРіРёСЃС‚СЂРёСЂРѕРІР°С‚СЊ `opensees_section_interaction_nm`.
- `OpenCS/Views/CalcTaskPropsDialog.xaml.cs` вЂ” РґРѕР±Р°РІРёС‚СЊ Р·Р°РґР°С‡Сѓ РІ СЃРїРёСЃРѕРє РІС‹Р±РѕСЂР°.
- `OpenCS/Resources/Strings.ru-RU.xaml` вЂ” СЂСѓСЃСЃРєРѕРµ РЅР°Р·РІР°РЅРёРµ task.
- `OpenCS/Resources/Strings.en-US.xaml` вЂ” Р°РЅРіР»РёР№СЃРєРѕРµ РЅР°Р·РІР°РЅРёРµ task.
- `OpenCS.OpenSees/README.md` вЂ” РґРѕРєСѓРјРµРЅС‚РёСЂРѕРІР°С‚СЊ JSON, СЃС‚Р°С‚СѓСЃС‹ Рё РєР°С‚Р°Р»РѕРіРё С‚РѕС‡РµРє.

---

### Р—Р°РґР°С‡Р° 1: Р”РѕР±Р°РІРёС‚СЊ РєРѕРЅС‚СЂР°РєС‚С‹ Р·Р°РїСЂРѕСЃР° Рё СЂРµР·СѓР»СЊС‚Р°С‚Р°

**Р¤Р°Р№Р»С‹:**

- РЎРѕР·РґР°С‚СЊ: `OpenCS.OpenSees/Analysis/SectionInteractionRequest.cs`
- РЎРѕР·РґР°С‚СЊ: `OpenCS.OpenSees/Analysis/SectionInteractionPoint.cs`
- РЎРѕР·РґР°С‚СЊ: `OpenCS.OpenSees/Analysis/SectionInteractionResult.cs`
- РўРµСЃС‚: `OpenCS.OpenSees.Tests/SectionInteractionTests.cs`

- [x] **РЁР°Рі 1: РќР°РїРёСЃР°С‚СЊ РїР°РґР°СЋС‰РёРµ С‚РµСЃС‚С‹ РІР°Р»РёРґР°С†РёРё Рё С„РѕСЂРјС‹ СЂРµР·СѓР»СЊС‚Р°С‚Р°.**

```csharp
using OpenCS.OpenSees.Analysis;

namespace OpenCS.OpenSees.Tests;

public sealed class SectionInteractionTests
{
    [Fact]
    public void Request_requires_nonempty_finite_unique_axial_forces()
    {
        SectionInteractionRequest valid = new()
        {
            AxialForcesN = [-100_000, 0, 100_000],
            MaxCurvature = 0.01,
            Increments = 20
        };

        valid.Validate();

        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0, double.NaN], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0, 0], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0], MaxCurvature = 0, Increments = 20
        }.Validate());
    }

    [Fact]
    public void Request_preserves_input_order()
    {
        SectionInteractionRequest request = new() { AxialForcesN = [100, -200, 0] };

        Assert.Equal(new[] { 100d, -200d, 0d }, request.AxialForcesN);
    }

    [Fact]
    public void Point_can_keep_last_converged_row_for_not_converged_analysis()
    {
        SectionHistoryRow row = new() { Step = 2, Converged = true, BendingMomentNm = 123 };
        SectionInteractionPoint point = new()
        {
            AxialForceN = 10,
            BendingMomentNm = row.BendingMomentNm,
            TerminalRow = row,
            Status = "not_converged"
        };

        Assert.Equal(123, point.BendingMomentNm);
        Assert.Equal(2, point.TerminalRow!.Step);
        Assert.Equal("not_converged", point.Status);
    }
}
```

- [x] **РЁР°Рі 2: Р—Р°РїСѓСЃС‚РёС‚СЊ С‚РѕР»СЊРєРѕ РЅРѕРІС‹Рµ С‚РµСЃС‚С‹ Рё СѓР±РµРґРёС‚СЊСЃСЏ, С‡С‚Рѕ РѕРЅРё РЅРµ РєРѕРјРїРёР»РёСЂСѓСЋС‚СЃСЏ РёР·-Р·Р° РѕС‚СЃСѓС‚СЃС‚РІСѓСЋС‰РёС… С‚РёРїРѕРІ.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: `FAIL` СЃ РѕС‚СЃСѓС‚СЃС‚РІСѓСЋС‰РёРјРё `SectionInteractionRequest`, `SectionInteractionPoint` Рё `SectionHistoryRow` РІ РЅРѕРІРѕРј С„Р°Р№Р»Рµ С‚РµСЃС‚Р°.

- [x] **РЁР°Рі 3: Р РµР°Р»РёР·РѕРІР°С‚СЊ РјРёРЅРёРјР°Р»СЊРЅС‹Рµ РєРѕРЅС‚СЂР°РєС‚С‹.**

`SectionInteractionRequest` РґРѕР»Р¶РµРЅ СЃРѕРґРµСЂР¶Р°С‚СЊ `IReadOnlyList<double> AxialForcesN = []`, `MaxCurvature = 0.01`, `Increments = 20`, `SectionBendingAxis Axis = Mx` Рё `OpenSeesCoordinateConvention Convention = CScoreDefault`. `Validate()` РґРѕР»Р¶РµРЅ РїСЂРѕРІРµСЂРёС‚СЊ РЅРµРїСѓСЃС‚РѕР№ СЃРїРёСЃРѕРє, РєРѕРЅРµС‡РЅРѕСЃС‚СЊ РєР°Р¶РґРѕРіРѕ Р·РЅР°С‡РµРЅРёСЏ, С‚РѕС‡РЅС‹Рµ РґСѓР±Р»РёРєР°С‚С‹, Р° Р·Р°С‚РµРј С‚Рµ Р¶Рµ `MaxCurvature`/`Increments`, С‡С‚Рѕ РїСЂРѕРІРµСЂСЏРµС‚ `SectionAnalysisRequest`.

```csharp
public void Validate()
{
    if (AxialForcesN.Count == 0 || AxialForcesN.Any(force => !double.IsFinite(force)))
        throw new ArgumentException("AxialForcesN must contain finite values.", nameof(AxialForcesN));
    if (AxialForcesN.Count != AxialForcesN.Distinct().Count())
        throw new ArgumentException("AxialForcesN must not contain duplicates.", nameof(AxialForcesN));
    if (!double.IsFinite(MaxCurvature) || MaxCurvature <= 0)
        throw new ArgumentException("MaxCurvature must be positive and finite.", nameof(MaxCurvature));
    if (Increments <= 0)
        throw new ArgumentException("Increments must be positive.", nameof(Increments));
}
```

`SectionInteractionPoint` СЃРѕРґРµСЂР¶РёС‚ `AxialForceN`, nullable `BendingMomentNm`, nullable `Curvature`, nullable `SectionHistoryRow TerminalRow`, `Status = "error"`, `Diagnostics = []` Рё `ArtifactDirectory = ""`. `SectionInteractionResult` СЃРѕРґРµСЂР¶РёС‚ `Status = "error"`, `Points = []` Рё `Diagnostics = []`. Р’СЃРµ public-С‚РёРїС‹ Рё СЃРІРѕР№СЃС‚РІР° РїРѕР»СѓС‡Р°СЋС‚ СЂСѓСЃСЃРєРёРµ XML-РєРѕРјРјРµРЅС‚Р°СЂРёРё.

- [x] **РЁР°Рі 4: Р—Р°РїСѓСЃС‚РёС‚СЊ С‚РµСЃС‚С‹ Рё РїСЂРѕРІРµСЂРёС‚СЊ PASS.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: РІСЃРµ С‚РµСЃС‚С‹ `SectionInteractionTests` РїСЂРѕС…РѕРґСЏС‚.

- [x] **РЁР°Рі 5: Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ РєРѕРЅС‚СЂР°РєС‚ РѕС‚РґРµР»СЊРЅС‹Рј РєРѕРјРјРёС‚РѕРј.**

```powershell
git add OpenCS.OpenSees/Analysis/SectionInteractionRequest.cs OpenCS.OpenSees/Analysis/SectionInteractionPoint.cs OpenCS.OpenSees/Analysis/SectionInteractionResult.cs OpenCS.OpenSees.Tests/SectionInteractionTests.cs
git commit -m "feat(opensees): add N-M interaction contracts"
```

### Р—Р°РґР°С‡Р° 2: Р”РѕР±Р°РІРёС‚СЊ executor-РєРѕРЅС‚СЂР°РєС‚ Рё СЃРµСЂРІРёСЃ РїРѕСЃР»РµРґРѕРІР°С‚РµР»СЊРЅРѕР№ РѕСЂРєРµСЃС‚СЂР°С†РёРё

**Р¤Р°Р№Р»С‹:**

- РЎРѕР·РґР°С‚СЊ: `OpenCS.OpenSees/Services/ISectionAnalysisExecutor.cs`
- РЎРѕР·РґР°С‚СЊ: `OpenCS.OpenSees/Services/SectionInteractionService.cs`
- РР·РјРµРЅРёС‚СЊ: `OpenCS.OpenSees/Services/SectionAnalysisService.cs`
- РР·РјРµРЅРёС‚СЊ: `OpenCS.OpenSees.Tests/SectionInteractionTests.cs`

- [x] **РЁР°Рі 1: Р”РѕР±Р°РІРёС‚СЊ С‚РµСЃС‚РѕРІС‹Р№ fake executor Рё РїР°РґР°СЋС‰РёРµ С‚РµСЃС‚С‹ РїРѕСЂСЏРґРєР°, РІС‹Р±РѕСЂР° СЃС‚СЂРѕРєРё Рё СЃС‚Р°С‚СѓСЃРѕРІ.**

Fake РґРѕР»Р¶РµРЅ Р·Р°РїРёСЃС‹РІР°С‚СЊ РєР°Р¶РґС‹Р№ `SectionAnalysisRequest` Рё РІРѕР·РІСЂР°С‰Р°С‚СЊ СЂРµР·СѓР»СЊС‚Р°С‚С‹ РёР· РѕС‡РµСЂРµРґРё:

```csharp
private sealed class FakeSectionAnalysisExecutor : ISectionAnalysisExecutor
{
    public List<SectionAnalysisRequest> Requests { get; } = [];
    public Queue<SectionAnalysisResult> Results { get; } = [];

    public Task<SectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Results.Dequeue());
    }
}
```

Р”РѕР±Р°РІРёС‚СЊ С‚РµСЃС‚, РєРѕС‚РѕСЂС‹Р№ РїРµСЂРµРґР°С‘С‚ `AxialForcesN = [100, -200, 300]`, Р° fake РІРѕР·РІСЂР°С‰Р°РµС‚ РґРІРµ converged-СЃС‚СЂРѕРєРё, Р·Р°С‚РµРј `not_converged` СЃ РѕРґРЅРѕР№ РїСЂРµРґС‹РґСѓС‰РµР№ converged-СЃС‚СЂРѕРєРѕР№, Р·Р°С‚РµРј `error`. РўРµСЃС‚ РґРѕР»Р¶РµРЅ РїСЂРѕРІРµСЂРёС‚СЊ СЃРѕС…СЂР°РЅС‘РЅРЅС‹Р№ РїРѕСЂСЏРґРѕРє Р·Р°РїСЂРѕСЃРѕРІ, СЃРёР»С‹ `100/-200/300`, РІС‹Р±РѕСЂ РїРѕСЃР»РµРґРЅРµР№ converged-СЃС‚СЂРѕРєРё РІРѕ РІС‚РѕСЂРѕР№ С‚РѕС‡РєРµ, РѕС‚РґРµР»СЊРЅС‹Рµ `ArtifactDirectory` РёР· СЂРµР·СѓР»СЊС‚Р°С‚РѕРІ Рё РёС‚РѕРіРѕРІС‹Р№ СЃС‚Р°С‚СѓСЃ `error`.

Р”РѕР±Р°РІРёС‚СЊ РѕС‚РґРµР»СЊРЅС‹Р№ С‚РµСЃС‚ Р±РµР· `error`, РЅРѕ СЃ РѕРґРЅРѕР№ `not_converged` С‚РѕС‡РєРѕР№; РѕР¶РёРґР°РµРјС‹Р№ РёС‚РѕРіРѕРІС‹Р№ СЃС‚Р°С‚СѓСЃ вЂ” `not_converged`. Р”РѕР±Р°РІРёС‚СЊ С‚РµСЃС‚ СЃ РѕС‚РјРµРЅС‘РЅРЅС‹Рј С‚РѕРєРµРЅРѕРј РјРµР¶РґСѓ С‚РѕС‡РєР°РјРё Рё РїСЂРѕРІРµСЂРёС‚СЊ, С‡С‚Рѕ executor РІС‹Р·РІР°РЅ С‚РѕР»СЊРєРѕ РґР»СЏ СѓР¶Рµ РЅР°С‡Р°РІС€РёС…СЃСЏ С‚РѕС‡РµРє, Р° `OperationCanceledException` РЅРµ РїСЂРµРІСЂР°С‰Р°РµС‚СЃСЏ РІ СѓСЃРїРµС€РЅС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚.

- [x] **РЁР°Рі 2: Р—Р°РїСѓСЃС‚РёС‚СЊ С‚РµСЃС‚С‹ Рё СѓР±РµРґРёС‚СЊСЃСЏ, С‡С‚Рѕ РѕС‚СЃСѓС‚СЃС‚РІСѓРµС‚ executor/service.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: `FAIL` РґРѕ РїРѕСЏРІР»РµРЅРёСЏ `ISectionAnalysisExecutor` Рё `SectionInteractionService`.

- [x] **РЁР°Рі 3: Р’РІРµСЃС‚Рё РєРѕРЅС‚СЂР°РєС‚ Рё РїРѕРґРєР»СЋС‡РёС‚СЊ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёР№ СЃРµСЂРІРёСЃ.**

РљРѕРЅС‚СЂР°РєС‚ РґРѕР»Р¶РµРЅ Р±С‹С‚СЊ С‚Р°РєРёРј:

```csharp
public interface ISectionAnalysisExecutor
{
    Task<SectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken);
}
```

РР·РјРµРЅРёС‚СЊ РѕР±СЉСЏРІР»РµРЅРёРµ `SectionAnalysisService` РЅР° `public sealed class SectionAnalysisService : ISectionAnalysisExecutor`; СЃРёРіРЅР°С‚СѓСЂСѓ Рё РїРѕРІРµРґРµРЅРёРµ РµРіРѕ С‚РµРєСѓС‰РµРіРѕ `RunAsync` РЅРµ РјРµРЅСЏС‚СЊ.

- [x] **РЁР°Рі 4: Р РµР°Р»РёР·РѕРІР°С‚СЊ `SectionInteractionService`.**

РљРѕРЅСЃС‚СЂСѓРєС‚РѕСЂ РїСЂРёРЅРёРјР°РµС‚ `ISectionAnalysisExecutor executor`. РњРµС‚РѕРґ:

```csharp
public Task<SectionInteractionResult> RunAsync(
    OpenSeesSectionModel model,
    SectionInteractionRequest request,
    OpenSeesRunRequest processRequest,
    CancellationToken cancellationToken);
```

РћРЅ РґРѕР»Р¶РµРЅ РІС‹Р·РІР°С‚СЊ `model.Validate()` Рё `request.Validate()` РґРѕ РїРµСЂРІРѕРіРѕ Р·Р°РїСѓСЃРєР°. Р”Р»СЏ РєР°Р¶РґРѕРіРѕ `force` РІ РёСЃС…РѕРґРЅРѕРј РїРѕСЂСЏРґРєРµ СЃРѕР·РґР°С‚СЊ `SectionAnalysisRequest` СЃ С‚РµРјРё Р¶Рµ `MaxCurvature`, `Increments`, `Axis`, `Convention` Рё `AxialForceN = force`. РџРµСЂРµРґ РєР°Р¶РґРѕР№ С‚РѕС‡РєРѕР№ РІС‹Р·РІР°С‚СЊ `cancellationToken.ThrowIfCancellationRequested()`.

Р РµР·СѓР»СЊС‚Р°С‚ РѕРґРЅРѕР№ С‚РѕС‡РєРё СЃС‚СЂРѕРёС‚СЃСЏ С‚Р°Рє:

```csharp
SectionHistoryRow? lastConverged = analysis.Rows.LastOrDefault(row => row.Converged);
new SectionInteractionPoint
{
    AxialForceN = force,
    BendingMomentNm = lastConverged?.BendingMomentNm,
    Curvature = lastConverged?.Curvature,
    TerminalRow = lastConverged,
    Status = analysis.Status,
    Diagnostics = analysis.Diagnostics,
    ArtifactDirectory = analysis.ArtifactDirectory
};
```

РџРѕСЃР»Рµ executor СЃРЅРѕРІР° РїСЂРѕРІРµСЂРёС‚СЊ С‚РѕРєРµРЅ, С‡С‚РѕР±С‹ РѕС‚РјРµРЅР°, РїРµСЂРµС…РІР°С‡РµРЅРЅР°СЏ РІРЅСѓС‚СЂРµРЅРЅРёРј СЃРµСЂРІРёСЃРѕРј, РЅРµ РїРѕР·РІРѕР»РёР»Р° РЅР°С‡Р°С‚СЊ СЃР»РµРґСѓСЋС‰СѓСЋ С‚РѕС‡РєСѓ. РќРµРѕР±СЂР°Р±РѕС‚Р°РЅРЅРѕРµ РёСЃРєР»СЋС‡РµРЅРёРµ РѕРґРЅРѕР№ С‚РѕС‡РєРё РїСЂРµРѕР±СЂР°Р·РѕРІР°С‚СЊ РІ С‚РѕС‡РєСѓ `error` СЃ С‚РµРєСЃС‚РѕРј РёСЃРєР»СЋС‡РµРЅРёСЏ Рё РїСЂРѕРґРѕР»Р¶РёС‚СЊ РѕСЃС‚Р°Р»СЊРЅС‹Рµ С‚РѕС‡РєРё; `OperationCanceledException` РїСЂРѕР±СЂРѕСЃРёС‚СЊ. РС‚РѕРіРѕРІС‹Р№ СЃС‚Р°С‚СѓСЃ РІС‹С‡РёСЃР»СЏС‚СЊ РїРѕ РїСЂР°РІРёР»Р°Рј РёР· spec. `Diagnostics` СЂРµР·СѓР»СЊС‚Р°С‚Р° РґРѕР»Р¶РЅС‹ СЃРѕРґРµСЂР¶Р°С‚СЊ С‚РѕР»СЊРєРѕ Р°РіСЂРµРіРёСЂРѕРІР°РЅРЅС‹Рµ СЃРѕРѕР±С‰РµРЅРёСЏ, Р° РїРѕРґСЂРѕР±РЅРѕСЃС‚Рё РєР°Р¶РґРѕР№ С‚РѕС‡РєРё РѕСЃС‚Р°СЋС‚СЃСЏ РІ `Points`.

- [x] **РЁР°Рі 5: Р—Р°РїСѓСЃС‚РёС‚СЊ С‚РµСЃС‚С‹ Рё РїСЂРѕРІРµСЂРёС‚СЊ PASS.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~SectionInteractionTests`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: РІСЃРµ unit-С‚РµСЃС‚С‹ РєРѕРЅС‚СЂР°РєС‚Р°, РїРѕСЂСЏРґРєР°, РІС‹Р±РѕСЂР° СЃС‚СЂРѕРєРё, СЃС‚Р°С‚СѓСЃРѕРІ Рё РѕС‚РјРµРЅС‹ РїСЂРѕС…РѕРґСЏС‚.

- [x] **РЁР°Рі 6: Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ СЃРµСЂРІРёСЃ РѕС‚РґРµР»СЊРЅС‹Рј РєРѕРјРјРёС‚РѕРј.**

```powershell
git add OpenCS.OpenSees/Services/ISectionAnalysisExecutor.cs OpenCS.OpenSees/Services/SectionInteractionService.cs OpenCS.OpenSees/Services/SectionAnalysisService.cs OpenCS.OpenSees.Tests/SectionInteractionTests.cs
git commit -m "feat(opensees): orchestrate sequential N-M analyses"
```

### Р—Р°РґР°С‡Р° 3: Р”РѕР±Р°РІРёС‚СЊ РїР°СЂР°РјРµС‚СЂС‹ Рё task handler OpenCS

**Р¤Р°Р№Р»С‹:**

- РЎРѕР·РґР°С‚СЊ: `OpenCS/Tasks/OpenSeesSectionInteractionParams.cs`
- РЎРѕР·РґР°С‚СЊ: `OpenCS/Tasks/OpenSeesSectionInteractionHandler.cs`
- РР·РјРµРЅРёС‚СЊ: `OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs`

- [x] **РЁР°Рі 1: РќР°РїРёСЃР°С‚СЊ РїР°РґР°СЋС‰РёРµ contract-С‚РµСЃС‚С‹ РґР»СЏ JSON.**

Р”РѕР±Р°РІРёС‚СЊ С‚РµСЃС‚ РґР»СЏ JSON:

```json
{
  "axialForces": [-1000, 0, 1000],
  "maxCurvature": 0.02,
  "increments": 40,
  "axis": "My",
  "timeoutSeconds": 90,
  "executablePath": "C:/OpenSees.exe"
}
```

РџСЂРѕРІРµСЂРёС‚СЊ С‚СЂРё СЃРёР»С‹ РІ `kN`, РїР°СЂР°РјРµС‚СЂС‹ РєСЂРёРІРёР·РЅС‹, РѕСЃСЊ, timeout Рё РїСѓС‚СЊ. РџСЂРѕРІРµСЂРёС‚СЊ, С‡С‚Рѕ `{}` РёСЃРїРѕР»СЊР·СѓРµС‚ `AxialForcesKn = [0]`, РїРѕР»РѕР¶РёС‚РµР»СЊРЅС‹Рµ defaults `MaxCurvature = 0.01`, `Increments = 20`, `TimeoutSeconds = 300`, Р° РїСѓСЃС‚РѕР№ СЃРїРёСЃРѕРє, `NaN`/`Infinity` РїРѕСЃР»Рµ РґРµСЃРµСЂРёР°Р»РёР·Р°С†РёРё, РЅРµРїРѕР»РѕР¶РёС‚РµР»СЊРЅС‹Рµ `increments`/timeout/maxCurvature, РґСѓР±Р»РёРєР°С‚С‹ Рё РЅРµРёР·РІРµСЃС‚РЅР°СЏ РѕСЃСЊ РѕС‚РєР»РѕРЅСЏСЋС‚СЃСЏ `ArgumentException`.

- [x] **РЁР°Рі 2: Р—Р°РїСѓСЃС‚РёС‚СЊ contract-С‚РµСЃС‚С‹ Рё СѓР±РµРґРёС‚СЊСЃСЏ, С‡С‚Рѕ РЅРѕРІС‹Р№ С‚РёРї РѕС‚СЃСѓС‚СЃС‚РІСѓРµС‚.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesTaskContractTests`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: РЅРѕРІС‹Рµ С‚РµСЃС‚С‹ РЅРµ РєРѕРјРїРёР»РёСЂСѓСЋС‚СЃСЏ РґРѕ СЃРѕР·РґР°РЅРёСЏ `OpenSeesSectionInteractionParams`.

- [x] **РЁР°Рі 3: Р РµР°Р»РёР·РѕРІР°С‚СЊ parser РїР°СЂР°РјРµС‚СЂРѕРІ.**

РўРёРї РґРѕР»Р¶РµРЅ РёСЃРїРѕР»СЊР·РѕРІР°С‚СЊ `System.Text.Json` СЃ `PropertyNameCaseInsensitive = true`, РїСѓР±Р»РёС‡РЅРѕРµ СЃРІРѕР№СЃС‚РІРѕ `IReadOnlyList<double> AxialForcesKn`, defaults РёР· С€Р°РіР° 1 Рё РЅРѕСЂРјР°Р»РёР·Р°С†РёСЋ `Axis` Рє СЃС‚СЂРѕРіРѕ `Mx`/`My`. РќРµ РёСЃРїРѕР»СЊР·РѕРІР°С‚СЊ СЂСѓСЃСЃРєРёРµ СЃС‚СЂРѕРєРё РІ task-РєРѕРЅС‚СЂР°РєС‚Рµ, С‚Р°Рє РєР°Рє СЃРѕРѕР±С‰РµРЅРёСЏ parser РЅРµ РїРѕРєР°Р·С‹РІР°СЋС‚СЃСЏ РЅР°РїСЂСЏРјСѓСЋ РІ XAML.

- [x] **РЁР°Рі 4: РџСЂРѕРІРµСЂРёС‚СЊ РµРґРёРЅРёС†С‹ Рё СЃРµСЂРёР°Р»РёР·СѓРµРјРѕСЃС‚СЊ СЂРµР·СѓР»СЊС‚Р°С‚Р°.**

Р’ contract-С‚РµСЃС‚Рµ РїСЂРѕРІРµСЂРёС‚СЊ:

```csharp
OpenSeesSectionInteractionParams parameters = OpenSeesSectionInteractionParams.Parse(json);
double[] axialForcesN = parameters.AxialForcesKn
    .Select(CScoreUnitConverter.KiloNewtonsToNewtons)
    .ToArray();

Assert.Equal(new[] { -1_000_000d, 0d, 1_000_000d }, axialForcesN);
string jsonResult = JsonSerializer.Serialize(new SectionInteractionResult
{
    Status = "ok",
    Points = [new SectionInteractionPoint { AxialForceN = 1_000, BendingMomentNm = 2_000 }]
});
Assert.Contains("\"AxialForceN\":1000", jsonResult);
```

- [x] **РЁР°Рі 5: Р РµР°Р»РёР·РѕРІР°С‚СЊ handler.**

`OpenSeesSectionInteractionHandler : ITaskHandler` РїРѕР»СѓС‡Р°РµС‚ kind `opensees_section_interaction_nm`. Р’ `Run` РѕРЅ РґРѕР»Р¶РµРЅ РїРѕРІС‚РѕСЂРёС‚СЊ РїСЂРѕРІРµСЂРµРЅРЅС‹Р№ stage 0вЂ“1 pipeline: РїСЂРѕРІРµСЂРёС‚СЊ cancellation, СЂР°СЃРїР°СЂСЃРёС‚СЊ params, РІС‹Р±СЂР°С‚СЊ `Mx`/`My`, РїРѕР»СѓС‡РёС‚СЊ materials/diagrams РёР· `TaskRunContext.Database`, РІС‹Р·РІР°С‚СЊ `CrossSectionToOpenSeesAdapter.Build`, СЂР°Р·СЂРµС€РёС‚СЊ executable С‡РµСЂРµР· `OpenSeesExecutableResolver`, СЃРєРѕРЅРІРµСЂС‚РёСЂРѕРІР°С‚СЊ `AxialForcesKn` РІ `AxialForcesN`, СЃРѕР·РґР°С‚СЊ `SectionInteractionRequest`, Р·Р°С‚РµРј СЃРѕР·РґР°С‚СЊ `SectionAnalysisService` Рё `SectionInteractionService` СЃ `OpenSeesArtifactStore` РїРѕРґ `AppContext.BaseDirectory/OpenSeesArtifacts`.

Р”Р»СЏ РєР°Р¶РґРѕР№ С‚РѕС‡РєРё РёСЃРїРѕР»СЊР·РѕРІР°С‚СЊ РѕРґРёРЅ Рё С‚РѕС‚ Р¶Рµ `OpenSeesRunRequest` СЃ executable Рё timeout; РІРЅСѓС‚СЂРµРЅРЅРёР№ interaction service РѕР±РµСЃРїРµС‡РёС‚ СѓРЅРёРєР°Р»СЊРЅС‹Рµ РєР°С‚Р°Р»РѕРіРё. Р’РѕР·РІСЂР°С‚РёС‚СЊ `CalcResult` СЃ `Status = result.Status` Рё `DataJson = JsonSerializer.Serialize(result)`. `OperationCanceledException` РІРµСЂРЅСѓС‚СЊ РєР°Рє `not_converged`, РїСЂРѕС‡РёРµ РёСЃРєР»СЋС‡РµРЅРёСЏ вЂ” РєР°Рє `error`, РЅРµ РІС‹Р±СЂР°СЃС‹РІР°СЏ РёС… РЅР°СЂСѓР¶Сѓ.

- [x] **РЁР°Рі 6: Р—Р°РїСѓСЃС‚РёС‚СЊ РІСЃРµ task-contract С‚РµСЃС‚С‹.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesTaskContractTests`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: PASS, РІРєР»СЋС‡Р°СЏ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёРµ С‚РµСЃС‚С‹ momentвЂ“curvature.

- [x] **РЁР°Рі 7: Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ task pipeline РѕС‚РґРµР»СЊРЅС‹Рј РєРѕРјРјРёС‚РѕРј.**

```powershell
git add OpenCS/Tasks/OpenSeesSectionInteractionParams.cs OpenCS/Tasks/OpenSeesSectionInteractionHandler.cs OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs
git commit -m "feat(opensees): add N-M task contract and handler"
```

### Р—Р°РґР°С‡Р° 4: Р—Р°СЂРµРіРёСЃС‚СЂРёСЂРѕРІР°С‚СЊ task Рё Р»РѕРєР°Р»РёР·РѕРІР°С‚СЊ РЅР°Р·РІР°РЅРёРµ

**Р¤Р°Р№Р»С‹:**

- РР·РјРµРЅРёС‚СЊ: `OpenCS/Tasks/TaskRunner.cs`
- РР·РјРµРЅРёС‚СЊ: `OpenCS/Views/CalcTaskPropsDialog.xaml.cs`
- РР·РјРµРЅРёС‚СЊ: `OpenCS/Resources/Strings.ru-RU.xaml`
- РР·РјРµРЅРёС‚СЊ: `OpenCS/Resources/Strings.en-US.xaml`
- РР·РјРµРЅРёС‚СЊ: `OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs`

- [x] **РЁР°Рі 1: Р”РѕР±Р°РІРёС‚СЊ РїСЂРѕРІРµСЂРєСѓ СЂРµРіРёСЃС‚СЂР°С†РёРё.**

Р’ `TaskRunner.KindList` РїСЂРѕРІРµСЂРёС‚СЊ РЅР°Р»РёС‡РёРµ С‚РѕС‡РЅРѕРіРѕ Р·РЅР°С‡РµРЅРёСЏ `opensees_section_interaction_nm` СЂСЏРґРѕРј СЃ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёРј `opensees_section_moment_curvature`.

- [x] **РЁР°Рі 2: Р”РѕР±Р°РІРёС‚СЊ resource keys РІ РѕР±Р° СЃР»РѕРІР°СЂСЏ.**

Р”РѕР±Р°РІРёС‚СЊ РѕРґРёРЅР°РєРѕРІС‹Р№ РєР»СЋС‡ `CalcTaskKind_opensees_section_interaction_nm` РІ `Strings.ru-RU.xaml` Рё `Strings.en-US.xaml`. Р СѓСЃСЃРєРѕРµ Р·РЅР°С‡РµРЅРёРµ РґРѕР»Р¶РЅРѕ Р±С‹С‚СЊ `OpenSees: РґРёР°РіСЂР°РјРјР° NвЂ“M`, Р°РЅРіР»РёР№СЃРєРѕРµ вЂ” `OpenSees: NвЂ“M interaction`. Р’ `.cs` Рё `.xaml` РЅРµ РґРѕР±Р°РІР»СЏС‚СЊ РІРёРґРёРјС‹Р№ С‚РµРєСЃС‚ РЅР°РїСЂСЏРјСѓСЋ: СЃРїРёСЃРѕРє task РґРѕР»Р¶РµРЅ РёСЃРїРѕР»СЊР·РѕРІР°С‚СЊ `Loc.S("CalcTaskKind_opensees_section_interaction_nm")`.

- [x] **РЁР°Рі 3: Р—Р°СЂРµРіРёСЃС‚СЂРёСЂРѕРІР°С‚СЊ РѕР±СЂР°Р±РѕС‚С‡РёРє Рё РґРѕР±Р°РІРёС‚СЊ РїСѓРЅРєС‚ РІС‹Р±РѕСЂР°.**

Р’ СЃР»РѕРІР°СЂСЊ `Handlers` РґРѕР±Р°РІРёС‚СЊ:

```csharp
["opensees_section_interaction_nm"] = new OpenSeesSectionInteractionHandler(),
```

Р’ СЃРїРёСЃРѕРє `CalcTaskPropsDialog` РґРѕР±Р°РІРёС‚СЊ `new()` СЃ `Id = "opensees_section_interaction_nm"`, `Label = Loc.S("CalcTaskKind_opensees_section_interaction_nm")`, С‚РѕР№ Р¶Рµ РіСЂСѓРїРїРѕР№ `other`, С‡С‚Рѕ РёСЃРїРѕР»СЊР·СѓРµС‚СЃСЏ С‚РµРєСѓС‰РµР№ OpenSees task.

- [x] **РЁР°Рі 4: РџСЂРѕРІРµСЂРёС‚СЊ Р»РѕРєР°Р»РёР·Р°С†РёСЋ Рё СЂРµРіРёСЃС‚СЂР°С†РёСЋ СЃР±РѕСЂРєРѕР№.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesTaskContractTests`

Р—Р°С‚РµРј: `dotnet build OpenCS/OpenCS.csproj --no-restore`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: contract-С‚РµСЃС‚С‹ Рё WPF-СЃР±РѕСЂРєР° РїСЂРѕС…РѕРґСЏС‚ Р±РµР· РЅРѕРІС‹С… РїСЂРµРґСѓРїСЂРµР¶РґРµРЅРёР№.

- [x] **РЁР°Рі 5: Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ СЂРµРіРёСЃС‚СЂР°С†РёСЋ РѕС‚РґРµР»СЊРЅС‹Рј РєРѕРјРјРёС‚РѕРј.**

```powershell
git add OpenCS/Tasks/TaskRunner.cs OpenCS/Views/CalcTaskPropsDialog.xaml.cs OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml OpenCS.OpenSees.Tests/OpenSeesTaskContractTests.cs
git commit -m "feat(opensees): register N-M calculation task"
```

### Р—Р°РґР°С‡Р° 5: Р”РѕР±Р°РІРёС‚СЊ СЂРµР°Р»СЊРЅСѓСЋ РёРЅС‚РµРіСЂР°С†РёСЋ Рё РґРѕРєСѓРјРµРЅС‚Р°С†РёСЋ

**Р¤Р°Р№Р»С‹:**

- РР·РјРµРЅРёС‚СЊ: `OpenCS.OpenSees.Tests/OpenSeesIntegrationTests.cs`
- РР·РјРµРЅРёС‚СЊ: `OpenCS.OpenSees/README.md`

- [x] **РЁР°Рі 1: РќР°РїРёСЃР°С‚СЊ opt-in integration-С‚РµСЃС‚ С‚СЂС‘С… С‚РѕС‡РµРє.**

РСЃРїРѕР»СЊР·РѕРІР°С‚СЊ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёРµ `OpenSeesTestExecutable.ResolveOrSkip()` Рё `ElasticSection()`. РЎРѕР·РґР°С‚СЊ `SectionInteractionService` РїРѕРІРµСЂС… СЂРµР°Р»СЊРЅС‹С… `SectionAnalysisService`, `SectionMomentCurvatureTclGenerator`, `OpenSeesProcessRunner` Рё РІСЂРµРјРµРЅРЅРѕРіРѕ `OpenSeesArtifactStore`. Р—Р°РїСЂРѕСЃРёС‚СЊ `AxialForcesN = [-100_000, 0, 100_000]`, `MaxCurvature = 1e-5`, `Increments = 2`.

РџСЂРѕРІРµСЂРёС‚СЊ `Status == "ok"`, СЂРѕРІРЅРѕ С‚СЂРё С‚РѕС‡РєРё РІ РёСЃС…РѕРґРЅРѕРј РїРѕСЂСЏРґРєРµ, РєРѕРЅРµС‡РЅС‹Рµ `BendingMomentNm` Рё `Curvature`, РїРѕР»РѕР¶РёС‚РµР»СЊРЅСѓСЋ РєСЂРёРІРёР·РЅСѓ Рё С‚СЂРё СЂР°Р·Р»РёС‡РЅС‹С… СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёС… `ArtifactDirectory`. Р’ РєР°Р¶РґРѕРј РєР°С‚Р°Р»РѕРіРµ РїСЂРѕРІРµСЂРёС‚СЊ `completed.marker`, `section_history.out` Рё `manifest.json`. Р’ `finally` СѓРґР°Р»РёС‚СЊ РІСЂРµРјРµРЅРЅС‹Р№ root С‚Р°Рє Р¶Рµ, РєР°Рє РІ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёС… integration-С‚РµСЃС‚Р°С….

- [x] **РЁР°Рі 2: Р—Р°РїСѓСЃС‚РёС‚СЊ С‚РѕР»СЊРєРѕ integration-С‚РµСЃС‚.**

Р—Р°РїСѓСЃРє: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~OpenSeesIntegrationTests`

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: С‚РµСЃС‚ РїСЂРѕС…РѕРґРёС‚ РїСЂРё Р·Р°РґР°РЅРЅРѕРј `OPENSEES_EXE`; Р±РµР· executable opt-in-С‚РµСЃС‚С‹ РїСЂРѕРїСѓСЃРєР°СЋС‚СЃСЏ С€С‚Р°С‚РЅС‹Рј `SkipException`.

- [x] **РЁР°Рі 3: РћР±РЅРѕРІРёС‚СЊ README.**

Р”РѕР±Р°РІРёС‚СЊ JSON-РїСЂРёРјРµСЂ:

```powershell
$env:OPENSEES_EXE = 'C:\path\to\OpenSees.exe'
```

```json
{
  "axialForces": [-1000, 0, 1000],
  "maxCurvature": 0.01,
  "increments": 20,
  "axis": "Mx",
  "timeoutSeconds": 300
}
```

РЇРІРЅРѕ СѓРєР°Р·Р°С‚СЊ, С‡С‚Рѕ `axialForces` Р·Р°РґР°СЋС‚СЃСЏ РІ `kN`, backend СЂР°Р±РѕС‚Р°РµС‚ РІ SI, С‚РѕС‡РєРё Р·Р°РїСѓСЃРєР°СЋС‚СЃСЏ РїРѕСЃР»РµРґРѕРІР°С‚РµР»СЊРЅРѕ, РєР°Р¶РґР°СЏ С‚РѕС‡РєР° РїРѕР»СѓС‡Р°РµС‚ СЃРѕР±СЃС‚РІРµРЅРЅС‹Р№ РєР°С‚Р°Р»РѕРі, Р° РїРѕСЃР»РµРґРЅСЏСЏ converged-СЃС‚СЂРѕРєР° СЃРѕС…СЂР°РЅСЏРµС‚СЃСЏ РґР°Р¶Рµ РїСЂРё СЃС‚Р°С‚СѓСЃРµ `not_converged`. РџРµСЂРµС‡РёСЃР»РёС‚СЊ Р°РіСЂРµРіРёСЂРѕРІР°РЅРЅС‹Рµ СЃС‚Р°С‚СѓСЃС‹ Рё РёСЃРєР»СЋС‡РёС‚СЊ РёР· С‚РµРєСѓС‰РµР№ РІРµСЂСЃРёРё `N-Mx-My`, target-force Рё parallel batch.

- [x] **РЁР°Рі 4: Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ РёРЅС‚РµРіСЂР°С†РёСЋ Рё РґРѕРєСѓРјРµРЅС‚Р°С†РёСЋ.**

```powershell
git add OpenCS.OpenSees.Tests/OpenSeesIntegrationTests.cs OpenCS.OpenSees/README.md
git commit -m "test(opensees): cover N-M interaction integration"
```

### Р—Р°РґР°С‡Р° 6: РџРѕР»РЅР°СЏ РїСЂРѕРІРµСЂРєР° stage Рё handoff

- [x] **РЁР°Рі 1: Р—Р°РїСѓСЃС‚РёС‚СЊ РїРѕР»РЅС‹Р№ РЅР°Р±РѕСЂ OpenSees-С‚РµСЃС‚РѕРІ.**

```powershell
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj
```

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: РІСЃРµ pure-С‚РµСЃС‚С‹ РїСЂРѕС…РѕРґСЏС‚, integration-С‚РµСЃС‚С‹ РїСЂРѕС…РѕРґСЏС‚ РїСЂРё `OPENSEES_EXE` РёР»Рё С€С‚Р°С‚РЅРѕ РїСЂРѕРїСѓСЃРєР°СЋС‚СЃСЏ Р±РµР· РЅРµРіРѕ.

- [x] **РЁР°Рі 2: Р—Р°РїСѓСЃС‚РёС‚СЊ РґРѕРјРµРЅРЅС‹Рµ С‚РµСЃС‚С‹ Рё СЃР±РѕСЂРєСѓ СЂРµС€РµРЅРёСЏ.**

```powershell
dotnet test CScore.Tests/CScore.Tests.csproj
dotnet build OpenCS.sln --no-restore
```

РћР¶РёРґР°РµРјС‹Р№ СЂРµР·СѓР»СЊС‚Р°С‚: С‚РµСЃС‚С‹ Рё solution build СѓСЃРїРµС€РЅС‹; СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёРµ РїСЂРµРґСѓРїСЂРµР¶РґРµРЅРёСЏ РІРЅРµ OpenSees РЅРµ РёСЃРїСЂР°РІР»СЏС‚СЊ РІ СЂР°РјРєР°С… СЌС‚РѕРіРѕ СЃСЂРµР·Р°.

- [x] **РЁР°Рі 3: РџСЂРѕРІРµСЂРёС‚СЊ Р°СЂС‚РµС„Р°РєС‚С‹ Рё Git-СЃРѕСЃС‚РѕСЏРЅРёРµ.**

РџСЂРѕРІРµСЂРёС‚СЊ РѕРґРёРЅ СЂРµР°Р»СЊРЅС‹Р№ РєР°С‚Р°Р»РѕРі РІР·Р°РёРјРѕРґРµР№СЃС‚РІРёСЏ: С‚СЂРё РѕС‚РґРµР»СЊРЅС‹С… РїРѕРґРєР°С‚Р°Р»РѕРіР°, SI-Р·РЅР°С‡РµРЅРёСЏ РІ `script.tcl`, РїРѕСЂСЏРґРѕРє `N`, РЅР°Р»РёС‡РёРµ `manifest.json`, `exit.json`, `section_history.out` Рё `completed.marker`. Р’С‹РїРѕР»РЅРёС‚СЊ `git diff --check` Рё `git status -sb`; РІ СЂР°Р±РѕС‡РµРј РґРµСЂРµРІРµ РґРѕР»Р¶РЅС‹ РѕСЃС‚Р°С‚СЊСЃСЏ С‚РѕР»СЊРєРѕ РѕСЃРѕР·РЅР°РЅРЅС‹Рµ РёР·РјРµРЅРµРЅРёСЏ/РєРѕРјРјРёС‚С‹ СЌС‚РѕР№ РІРµС‚РєРё.

- [x] **РЁР°Рі 4: Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ РІС‹РїРѕР»РЅРµРЅРёРµ РїР»Р°РЅР° Рё handoff.**

```powershell
git add docs/superpowers/plans/2026-07-15-opensees-section-interaction.md
git commit -m "docs(opensees): add N-M implementation plan"
```

РџРѕСЃР»Рµ СЌС‚РѕРіРѕ СЃР»РµРґСѓСЋС‰Р°СЏ РіСЂР°РЅРёС†Р° СЂРµР°Р»РёР·Р°С†РёРё вЂ” РѕС‚РґРµР»СЊРЅР°СЏ СЃРїРµС†РёС„РёРєР°С†РёСЏ `N-Mx-My` РёР»Рё target-force, Р±РµР· СЂР°СЃС€РёСЂРµРЅРёСЏ С‚РµРєСѓС‰РµРіРѕ task СЃРєСЂС‹С‚С‹РјРё РїР°СЂР°РјРµС‚СЂР°РјРё.

## РЎР°РјРѕРїСЂРѕРІРµСЂРєР° РїР»Р°РЅР°

- Р’СЃРµ С‚СЂРµР±РѕРІР°РЅРёСЏ СЃРїРµС†РёС„РёРєР°С†РёРё РїРѕРєСЂС‹С‚С‹ Р·Р°РґР°С‡Р°РјРё 1вЂ“6: РјРѕРґРµР»Рё, РїРѕСЃР»РµРґРѕРІР°С‚РµР»СЊРЅС‹Р№ executor, СЃС‚Р°С‚СѓСЃС‹, Р°СЂС‚РµС„Р°РєС‚С‹, cancellation, task contract, Р»РѕРєР°Р»РёР·Р°С†РёСЏ Рё opt-in integration.
- Р’ РїР»Р°РЅРµ РЅРµС‚ С€Р°РіРѕРІ СЃ `TODO`, `TBD`, РЅРµРѕРїСЂРµРґРµР»С‘РЅРЅС‹РјРё С„Р°Р№Р»Р°РјРё РёР»Рё СЃСЃС‹Р»РєР°РјРё РЅР° РЅРµСЃСѓС‰РµСЃС‚РІСѓСЋС‰РёРµ РјРµС‚РѕРґС‹; РєРѕРЅС‚СЂР°РєС‚ `ISectionAnalysisExecutor.RunAsync` РѕРїСЂРµРґРµР»С‘РЅ РґРѕ РµРіРѕ РёСЃРїРѕР»СЊР·РѕРІР°РЅРёСЏ.
- РЎСѓС‰РµСЃС‚РІСѓСЋС‰Р°СЏ task momentвЂ“curvature РЅРµ РјРµРЅСЏРµС‚ СЃРµРјР°РЅС‚РёРєСѓ Р·Р°РїСЂРѕСЃР°; РЅРѕРІР°СЏ task РёСЃРїРѕР»СЊР·СѓРµС‚ РѕС‚РґРµР»СЊРЅС‹Р№ kind Рё РѕС‚РґРµР»СЊРЅС‹Р№ params-РєР»Р°СЃСЃ.
- РџР°СЂР°Р»Р»РµР»РёР·Рј, РїСЂРѕСЃС‚СЂР°РЅСЃС‚РІРµРЅРЅР°СЏ РґРёР°РіСЂР°РјРјР° Рё target-force СЏРІРЅРѕ РёСЃРєР»СЋС‡РµРЅС‹, РїРѕСЌС‚РѕРјСѓ РїР»Р°РЅ РѕСЃС‚Р°С‘С‚СЃСЏ РѕРґРЅРёРј С‚РµСЃС‚РёСЂСѓРµРјС‹Рј РІРµСЂС‚РёРєР°Р»СЊРЅС‹Рј СЃСЂРµР·РѕРј.
