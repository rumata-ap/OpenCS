# Crack-width η (СП 63 п. 8.1.15) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) or superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Подключить η п. 8.1.15 к `crack_width` / `crack_width_batch` как в `strain_state`, с авто-ψ из `|M_long/M_total|`.

**Architecture:** После merge `master` → feature-ветка: тонкий хелпер `CrackWidthEta` (AutoPsi + масштаб моментов + сборка eta-JSON); handlers читают η через `LimitForceParams.Parse` и вызывают `RodEtaWiring.Apply` по полной нагрузке; UI расширяет `SupportsEta` и скрывает ψ для трещин; Commit мержит η-ключи в `ParamsJson`.

**Tech Stack:** C# / .NET 9, CScore.Sp63 (`RodEtaWiring`, `EccentricityAmplifier`), OpenCS WPF, xUnit (`CScore.Tests`).

**Spec:** `docs/superpowers/specs/2026-07-13-crack-width-eta-design.md`

---

## File map

| File | Role |
|------|------|
| `CScore/Sp63/CrackWidthEta.cs` | **Create** — `AutoPsi`, `ScaleLongTotal`, `BuildEtaData` |
| `CScore.Tests/CrackWidthEtaTests.cs` | **Create** — unit-тесты хелпера |
| `OpenCS/Tasks/CrackWidthHandler.cs` | **Modify** — η перед `Compute` |
| `OpenCS/Tasks/CrackWidthBatchHandler.cs` | **Modify** — η построчно |
| `OpenCS/Views/CalcTaskPropsDialog.xaml.cs` | **Modify** — `SupportsEta`, `ShowEtaPsiFields`, Commit/restore |
| `OpenCS/Views/CalcTaskPropsDialog.xaml` | **Modify** — Visibility ψ → `ShowEtaPsiFields` |
| `OpenCS/Resources/Strings.ru-RU.xaml` | **Modify** — подсказка авто-ψ |
| `OpenCS/Resources/Strings.en-US.xaml` | **Modify** — EN |
| `OpenCS/Views/CrackWidthResultView.xaml(.cs)` | **Modify** — блок η в результате |

---

### Task 0: Зафиксировать WIP трещин и влить master

**Files:** текущие untracked/modified на `feature/crack-width-tasks`; merge `master` (worktree tip `a0b2697` или актуальный).

- [ ] **Step 1: Commit незавершённый UI трещин (если ещё не в индексе)**

```powershell
cd C:\Users\ponomarev\Documents\devel\OpenCS
git status -sb
git add OpenCS/ViewModels/CrackWidthBatchVM.cs OpenCS/ViewModels/CrackingBatchVM.cs `
  OpenCS/Views/CrackWidth*.xaml OpenCS/Views/CrackWidth*.xaml.cs `
  OpenCS/Views/Cracking*.xaml OpenCS/Views/Cracking*.xaml.cs `
  OpenCS/ViewModels/CalcTasksTreeVM.cs OpenCS/Views/CalcResultView.xaml.cs `
  OpenCS/Views/CalcTaskPropsDialog.xaml OpenCS/Views/CalcTaskPropsDialog.xaml.cs `
  OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat(crack): UI dialog, result views and localization for cracking/crack_width"
```

- [ ] **Step 2: Merge master into feature branch**

```powershell
git merge master -m "Merge master into feature/crack-width-tasks (η + SCAD XLS)"
```

Expected: возможны конфликты в `CalcTaskPropsDialog.*`, `Strings.*.xaml`, `CalcResultView.xaml.cs`. Разрешить, сохранив **и** η UI с master, **и** crack kinds/UI с feature.

- [ ] **Step 3: Build**

```powershell
dotnet build OpenCS.sln 2>&1 | Select-Object -Last 15
```

Expected: `Ошибок: 0`.

- [ ] **Step 4: Commit merge (если merge создал незакоммиченное разрешение)**

Только если Step 2 оставил unresolved→resolved без commit.

---

### Task 1: Хелпер AutoPsi + ScaleLongTotal (TDD)

**Files:**
- Create: `CScore/Sp63/CrackWidthEta.cs`
- Test: `CScore.Tests/CrackWidthEtaTests.cs`

- [ ] **Step 1: Write failing tests**

Создать `CScore.Tests/CrackWidthEtaTests.cs`:

```csharp
using CScore.Sp63;
using Xunit;

namespace CScore.Tests;

public class CrackWidthEtaTests
{
    [Theory]
    [InlineData(70, 100, 0.7)]
    [InlineData(-70, -100, 0.7)]
    [InlineData(0, 100, 0.0)]
    [InlineData(100, 100, 1.0)]
    public void AutoPsi_RatioClamped(double ml, double mt, double expected)
        => Assert.Equal(expected, CrackWidthEta.AutoPsi(ml, mt), 9);

    [Fact]
    public void AutoPsi_ZeroTotal_ReturnsOne()
        => Assert.Equal(1.0, CrackWidthEta.AutoPsi(0, 0));

    [Fact]
    public void ScaleLongTotal_PreservesShare()
    {
        double mxLong = -70, mxTotal = -100, myLong = 35, myTotal = 50;
        double mxEff = -130, myEff = 65; // ηx=1.3, ηy=1.3
        var s = CrackWidthEta.ScaleLongTotal(mxLong, mxTotal, myLong, myTotal, mxEff, myEff);
        Assert.Equal(mxEff, s.MxTotalEff, 9);
        Assert.Equal(myEff, s.MyTotalEff, 9);
        Assert.Equal(0.7, s.MxLongEff / s.MxTotalEff, 9);
        Assert.Equal(0.7, s.MyLongEff / s.MyTotalEff, 9);
    }

    [Fact]
    public void ScaleLongTotal_ZeroTotalAxis_LeavesLongUnchanged()
    {
        var s = CrackWidthEta.ScaleLongTotal(0, 0, 10, 20, 0, 26);
        Assert.Equal(0, s.MxLongEff, 9);
        Assert.Equal(0, s.MxTotalEff, 9);
        Assert.Equal(13, s.MyLongEff, 9); // 10 * (26/20)
        Assert.Equal(26, s.MyTotalEff, 9);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL (type missing)**

```powershell
dotnet test CScore.Tests --filter "FullyQualifiedName~CrackWidthEtaTests" --no-restore 2>&1 | Select-Object -Last 20
```

Если restore нужен: `dotnet test CScore.Tests --filter "FullyQualifiedName~CrackWidthEtaTests"`.

Expected: compile error `CrackWidthEta` not found **или** FAIL.

- [ ] **Step 3: Implement helper**

Создать `CScore/Sp63/CrackWidthEta.cs`:

```csharp
using System;

namespace CScore.Sp63;

/// <summary>
/// Обвязка η (п. 8.1.15) для задач ширины раскрытия трещин:
/// авто-ψ = |M_long/M_total| и одинаковый масштаб long/total после RodEtaWiring.
/// </summary>
public static class CrackWidthEta
{
    public const double MomentEpsilon = 1e-9;

    public static double AutoPsi(double mLong, double mTotal)
    {
        if (Math.Abs(mTotal) < MomentEpsilon)
            return 1.0;
        double r = Math.Abs(mLong / mTotal);
        return Math.Clamp(r, 0.0, 1.0);
    }

    public readonly record struct ScaledMoments(
        double MxLongEff, double MxTotalEff, double MyLongEff, double MyTotalEff);

    public static ScaledMoments ScaleLongTotal(
        double mxLong, double mxTotal, double myLong, double myTotal,
        double mxTotalEff, double myTotalEff)
    {
        double sx = Math.Abs(mxTotal) < MomentEpsilon ? 1.0 : mxTotalEff / mxTotal;
        double sy = Math.Abs(myTotal) < MomentEpsilon ? 1.0 : myTotalEff / myTotal;
        return new ScaledMoments(
            MxLongEff: mxLong * sx,
            MxTotalEff: mxTotalEff,
            MyLongEff: myLong * sy,
            MyTotalEff: myTotalEff);
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

```powershell
dotnet test CScore.Tests --filter "FullyQualifiedName~CrackWidthEtaTests"
```

Expected: все тесты `CrackWidthEtaTests` зелёные.

- [ ] **Step 5: Commit**

```powershell
git add CScore/Sp63/CrackWidthEta.cs CScore.Tests/CrackWidthEtaTests.cs
git commit -m "feat(sp63): CrackWidthEta AutoPsi and moment scaling helpers"
```

---

### Task 2: Подключить η в `CrackWidthHandler`

**Files:**
- Modify: `OpenCS/Tasks/CrackWidthHandler.cs`

- [ ] **Step 1: После сбора mxLong/myLong/mxTotal/… вставить блок η**

Перед `var solver = new CrackWidthSolver(...)` сохранить originals; после создания solver (или до — solver трещин не нужен для jointSolve) применить η.

Паттерн (вставить после switch ForcesMode, перед `new CrackWidthSolver`):

```csharp
double mxLongIn = mxLong, myLongIn = myLong, mxTotalIn = mxTotal, myTotalIn = myTotal;
object? etaData = null;

var etaParams = LimitForceParams.Parse(task.ParamsJson);
if (etaParams.EtaEnabled)
{
    double psiX = CrackWidthEta.AutoPsi(mxLongIn, mxTotalIn);
    double psiY = CrackWidthEta.AutoPsi(myLongIn, myTotalIn);
    double threshold = etaParams.EtaSlendernessThreshold
        ?? CScore.Sp63.EccentricityAmplifier.SlendernessThreshold;

    bool ten = settings.ResolveConcreteTension(CalcType.N);
    var strainSolver = new StrainSolver(section, CalcType.N, ten: ten,
        tol: settings.NewtonTolerance,
        maxIter: settings.NewtonMaxIter,
        h: settings.NewtonDeltaH,
        centralJacobian: settings.NewtonJacobian == "central");

    var wiring = CScore.Sp63.RodEtaWiring.Apply(
        section, nTotal, mxTotalIn, myTotalIn,
        etaParams.EtaL0x, etaParams.EtaL0y,
        psiX, psiY,
        etaParams.EtaIterative,
        (mx, my) => strainSolver.Solve(nTotal, mx, my),
        threshold);

    var scaled = CrackWidthEta.ScaleLongTotal(
        mxLongIn, mxTotalIn, myLongIn, myTotalIn, wiring.MxEff, wiring.MyEff);
    mxLong = scaled.MxLongEff;
    mxTotal = scaled.MxTotalEff;
    myLong = scaled.MyLongEff;
    myTotal = scaled.MyTotalEff;

    etaData = new
    {
        mode = etaParams.EtaIterative ? "iterative" : "formula",
        slendernessThreshold = threshold,
        psiX, psiY,
        mxOriginal = mxTotalIn,
        myOriginal = myTotalIn,
        mxLongOriginal = mxLongIn,
        myLongOriginal = myLongIn,
        l0x = Math.Round(wiring.X.L0, 4),
        hx = Math.Round(wiring.X.H, 4),
        slendernessX = wiring.X.H > 1e-9 ? Math.Round(wiring.X.L0 / wiring.X.H, 2) : (double?)null,
        dX = double.IsFinite(wiring.X.D) ? Math.Round(wiring.X.D, 2) : (double?)null,
        etaX = Math.Round(wiring.X.Eta, 6),
        ncrX = double.IsFinite(wiring.X.Ncr) ? Math.Round(wiring.X.Ncr, 4) : (double?)null,
        slenderX = wiring.X.Slender,
        stableX = wiring.X.Stable,
        extrapolationFailedX = wiring.X.ExtrapolationFailed,
        etaHistoryX = wiring.X.EtaHistory.Select(e => Math.Round(e, 6)).ToArray(),
        l0y = Math.Round(wiring.Y.L0, 4),
        hy = Math.Round(wiring.Y.H, 4),
        slendernessY = wiring.Y.H > 1e-9 ? Math.Round(wiring.Y.L0 / wiring.Y.H, 2) : (double?)null,
        dY = double.IsFinite(wiring.Y.D) ? Math.Round(wiring.Y.D, 2) : (double?)null,
        etaY = Math.Round(wiring.Y.Eta, 6),
        ncrY = double.IsFinite(wiring.Y.Ncr) ? Math.Round(wiring.Y.Ncr, 4) : (double?)null,
        slenderY = wiring.Y.Slender,
        stableY = wiring.Y.Stable,
        extrapolationFailedY = wiring.Y.ExtrapolationFailed,
        etaHistoryY = wiring.Y.EtaHistory.Select(e => Math.Round(e, 6)).ToArray(),
    };
}
```

В anonymous `data` результата:
- поля `Mx_long` / `Mx_total` / … — **уже eff** (как сейчас имена, значения после η);
- добавить `Mx_long_input`, `Mx_total_input`, … (originals) при `etaData != null` **или** всегда писать input = In;
- добавить `eta = etaData`.

Добавить `using System.Linq;` и `using CScore.Sp63;` при необходимости. `ResolveConcreteTension` — метод `CalcSettings` с master.

- [ ] **Step 2: Build OpenCS**

```powershell
dotnet build OpenCS\OpenCS.csproj 2>&1 | Select-Object -Last 20
```

Expected: `Ошибок: 0`.

- [ ] **Step 3: Commit**

```powershell
git add OpenCS/Tasks/CrackWidthHandler.cs
git commit -m "feat(crack): apply η 8.1.15 in CrackWidthHandler with auto-ψ"
```

---

### Task 3: Подключить η в `CrackWidthBatchHandler`

**Files:**
- Modify: `OpenCS/Tasks/CrackWidthBatchHandler.cs`

- [ ] **Step 1: Перед циклом распарсить etaParams; внутри цикла — Apply + Scale**

После определения `mxLong`/`myLong` для строки `i`, до `solver.Compute`:

```csharp
var etaParams = LimitForceParams.Parse(task.ParamsJson);
double etaThreshold = etaParams.EtaSlendernessThreshold
    ?? CScore.Sp63.EccentricityAmplifier.SlendernessThreshold;
bool ten = settings.ResolveConcreteTension(CalcType.N);
```

(вынести парсинг **перед** циклом.)

В цикле:

```csharp
double mxLongIn = mxLong, myLongIn = myLong, mxTotalIn = mxTotal, myTotalIn = myTotal;
object? etaRow = null;
if (etaParams.EtaEnabled)
{
    double psiX = CrackWidthEta.AutoPsi(mxLongIn, mxTotalIn);
    double psiY = CrackWidthEta.AutoPsi(myLongIn, myTotalIn);
    var strainSolver = new StrainSolver(section /* или clone при parallel */, CalcType.N, ten: ten,
        tol: settings.NewtonTolerance, maxIter: settings.NewtonMaxIter,
        h: settings.NewtonDeltaH, centralJacobian: settings.NewtonJacobian == "central");
    var wiring = RodEtaWiring.Apply(
        section, nTotal, mxTotalIn, myTotalIn,
        etaParams.EtaL0x, etaParams.EtaL0y, psiX, psiY, etaParams.EtaIterative,
        (mx, my) => strainSolver.Solve(nTotal, mx, my), etaThreshold);
    var scaled = CrackWidthEta.ScaleLongTotal(mxLongIn, mxTotalIn, myLongIn, myTotalIn, wiring.MxEff, wiring.MyEff);
    mxLong = scaled.MxLongEff; mxTotal = scaled.MxTotalEff;
    myLong = scaled.MyLongEff; myTotal = scaled.MyTotalEff;
    etaRow = new
    {
        mode = etaParams.EtaIterative ? "iterative" : "formula",
        psiX, psiY,
        etaX = Math.Round(wiring.X.Eta, 6),
        etaY = Math.Round(wiring.Y.Eta, 6),
        mxOriginal = mxTotalIn, myOriginal = myTotalIn,
        mxEff = wiring.MxEff, myEff = wiring.MyEff,
        stableX = wiring.X.Stable, stableY = wiring.Y.Stable,
    };
}
```

В объект строки добавить `eta = etaRow`.

Если batch станет медленным с η — оставить последовательный цикл (как сейчас); не требовать Parallel в v1.

- [ ] **Step 2: Build**

```powershell
dotnet build OpenCS\OpenCS.csproj 2>&1 | Select-Object -Last 15
```

Expected: `Ошибок: 0`.

- [ ] **Step 3: Commit**

```powershell
git add OpenCS/Tasks/CrackWidthBatchHandler.cs
git commit -m "feat(crack): apply η 8.1.15 in CrackWidthBatchHandler"
```

---

### Task 4: UI — SupportsEta, скрыть ψ, Commit/restore

**Files:**
- Modify: `OpenCS/Views/CalcTaskPropsDialog.xaml.cs`
- Modify: `OpenCS/Views/CalcTaskPropsDialog.xaml`

- [ ] **Step 1: Расширить SupportsEta и ShowEtaPsiFields**

В `CalcTaskPropsDlgVM` (после merge с master свойство уже есть):

```csharp
public bool SupportsEta => Kind is "strain_state" or "strain_state_batch"
    or "limit_moment" or "limit_moment_batch"
    or "crack_width" or "crack_width_batch";

/// <summary>ψx/ψy вручную — не для трещин (там авто из M_long/M_total).</summary>
public bool ShowEtaPsiFields => ShowEtaFormulaFields && !IsCrackWidthAny;
```

В сеттерах `SelectedKind` / `Kind` добавить `OnPropertyChanged(nameof(ShowEtaPsiFields));` рядом с `ShowEtaFormulaFields`.

Убедиться, что `IsCrackWidthAny` существует после merge (feature UI). Если после merge флагов crack нет — восстановить из pre-merge feature.

- [ ] **Step 2: XAML — Visibility сетки ψ**

В `CalcTaskPropsDialog.xaml` у Grid с `EtaPsiX`/`EtaPsiY` заменить:

```xml
Visibility="{Binding ShowEtaFormulaFields, Converter={StaticResource BoolToVisibility}}"
```

на:

```xml
Visibility="{Binding ShowEtaPsiFields, Converter={StaticResource BoolToVisibility}}"
```

Под CheckBox η или под GroupBox добавить TextBlock (только трещины), например:

```xml
<TextBlock Text="{DynamicResource EtaAutoPsiCrackHint}"
           TextWrapping="Wrap" Margin="0,4,0,0" Opacity="0.85"
           Visibility="{Binding IsCrackWidthAny, Converter={StaticResource BoolToVisibility}}"/>
```

Показывать hint когда `SupportsEta && IsCrackWidthAny` — при необходимости завести `ShowEtaAutoPsiHint => SupportsEta && IsCrackWidthAny`.

- [ ] **Step 3: Commit() — мержить η в ParamsJson трещин**

В ветке `if (IsCrackWidthAny) { ... }` после `var cwp = new CrackWidthTaskParams { ... }` и до `ParamsJson = cwp.ToJson()`:

```csharp
string crackJson = cwp.ToJson();
if (EtaEnabled)
{
    var lfp = new LimitForceParams();
    ApplyEtaParams(lfp, System.Globalization.CultureInfo.InvariantCulture);
    // Для трещин ψ в JSON не обязателен — handler считает AutoPsi.
    // LimitForceParams.ToJson() при EtaEnabled пишет eta*-ключи.
    // Смержить: распарсить оба в JsonObject / Dictionary и объединить.
    using var crackDoc = System.Text.Json.JsonDocument.Parse(crackJson);
    using var etaDoc = System.Text.Json.JsonDocument.Parse(lfp.ToJson());
    var dict = new Dictionary<string, System.Text.Json.JsonElement>();
    foreach (var prop in crackDoc.RootElement.EnumerateObject())
        dict[prop.Name] = prop.Value.Clone();
    foreach (var prop in etaDoc.RootElement.EnumerateObject())
        dict[prop.Name] = prop.Value.Clone();
    crackJson = System.Text.Json.JsonSerializer.Serialize(dict);
}
ParamsJson = crackJson;
```

Либо проще: если `LimitForceParams.ToJson()` возвращает полный объект только с eta-полями — merge как выше.

Также в общей валидации `if (SupportsEta && EtaEnabled)` (проверка L > 0) — уже сработает после расширения `SupportsEta`.

- [ ] **Step 4: Restore при редактировании**

В конструкторе, рядом с restore crack params, если `existing.Kind is "crack_width" or "crack_width_batch"`:

```csharp
var ep = LimitForceParams.Parse(existing.ParamsJson);
EtaEnabled = ep.EtaEnabled;
EtaIterative = ep.EtaIterative;
// ... те же присвоения EtaL, EtaMuX, EtaMuY, threshold, что у strain_state restore
```

Скопировать блок restore η из ветки strain_state в файле (после merge он есть).

- [ ] **Step 5: Build**

```powershell
dotnet build OpenCS\OpenCS.csproj 2>&1 | Select-Object -Last 15
```

Expected: `Ошибок: 0`.

- [ ] **Step 6: Commit**

```powershell
git add OpenCS/Views/CalcTaskPropsDialog.xaml OpenCS/Views/CalcTaskPropsDialog.xaml.cs
git commit -m "feat(ui): enable η dialog for crack_width with auto-ψ (hide ψ fields)"
```

---

### Task 5: Локализация подсказки авто-ψ

**Files:**
- Modify: `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify: `OpenCS/Resources/Strings.en-US.xaml`

- [ ] **Step 1: Добавить ключи**

ru-RU (рядом с `EtaPsiHint`):

```xml
<system:String x:Key="EtaAutoPsiCrackHint">Для ширины трещин ψ = |M_long/M_total| берётся автоматически из режима длительной нагрузки (поля ψ скрыты).</system:String>
```

en-US:

```xml
<system:String x:Key="EtaAutoPsiCrackHint">For crack width, ψ = |M_long/M_total| is taken automatically from the long-term load mode (ψ fields are hidden).</system:String>
```

- [ ] **Step 2: Commit**

```powershell
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat(i18n): auto-ψ hint for crack_width η dialog"
```

---

### Task 6: Показать η в `CrackWidthResultView`

**Files:**
- Modify: `OpenCS/Views/CrackWidthResultView.xaml.cs`
- Modify: `OpenCS/Views/CrackWidthResultView.xaml`

- [ ] **Step 1: VM — свойства минимума**

В inline VM результата добавить:

```csharp
public bool EtaEnabled { get; }
public string EtaModeText { get; } = "";
public string EtaXText { get; } = "—";
public string EtaYText { get; } = "—";
public string EtaMxText { get; } = ""; // original → eff
public string EtaMyText { get; } = "";
public string EtaPsiText { get; } = "";
```

Парсинг из `root.TryGetProperty("eta", out var etaEl)` по образцу `StrainSummaryVM` (достаточно mode, etaX/etaY, mxOriginal, targets из Mx_total в корне или mxEff).

- [ ] **Step 2: XAML — блок под карточкой**

```xml
<Border Visibility="{Binding EtaEnabled, Converter={StaticResource BoolToVisibility}}" ...>
  <StackPanel>
    <TextBlock FontWeight="SemiBold" Text="{DynamicResource EtaParamsHeader}"/>
    <TextBlock Text="{Binding EtaModeText}"/>
    <TextBlock>
      <Run Text="ηx: "/><Run Text="{Binding EtaXText}"/>
      <Run Text="   ηy: "/><Run Text="{Binding EtaYText}"/>
    </TextBlock>
    <TextBlock Text="{Binding EtaPsiText}"/>
    <TextBlock Text="{Binding EtaMxText}"/>
    <TextBlock Text="{Binding EtaMyText}"/>
  </StackPanel>
</Border>
```

Если `BoolToVisibility` нет в ресурсах UserControl — использовать тот же паттерн, что в других result views, или `DataTrigger`.

- [ ] **Step 3: Build + test**

```powershell
dotnet build OpenCS.sln 2>&1 | Select-Object -Last 15
dotnet test CScore.Tests --filter "FullyQualifiedName~CrackWidth|FullyQualifiedName~Cracking|FullyQualifiedName~Eta" 2>&1 | Select-Object -Last 25
```

Expected: build 0 errors; crack/eta tests PASS.

- [ ] **Step 4: Commit**

```powershell
git add OpenCS/Views/CrackWidthResultView.xaml OpenCS/Views/CrackWidthResultView.xaml.cs
git commit -m "feat(ui): show η diagnostics on crack width result"
```

---

### Task 7: Сквозная проверка

- [ ] **Step 1: Полная сборка и тесты**

```powershell
dotnet build OpenCS.sln 2>&1 | Select-Object -Last 20
dotnet test CScore.Tests 2>&1 | Select-Object -Last 20
```

Expected: 0 ошибок; все CScore.Tests зелёные.

- [ ] **Step 2: Ручной smoke (кратко в ответе пользователю)**

`dotnet run --project OpenCS\OpenCS.csproj` — создать `crack_width`, включить η, share=0.7, выполнить; убедиться что ψ в диалоге нет, в результате есть ηx/ηy.

---

## Spec coverage checklist (self-review)

| Spec item | Task |
|-----------|------|
| D1 одна η по total, scale long | T1 Scale + T2/T3 |
| D2 авто-ψ, UI hide ψ | T1 AutoPsi + T4 ShowEtaPsiFields |
| D3 ParamsJson merge η keys | T4 Commit merge |
| D4 cracking* out of scope | — (не трогаем) |
| D5 merge master | T0 |
| Handler P2 LimitForceParams.Parse | T2/T3 |
| Batch row eta | T3 |
| Result UI min η | T6 |
| Localization hint | T5 |
| Unit AutoPsi + share preserve | T1 |
| Build/test | T7 |

**Placeholder scan:** нет TBD/TODO в шагах.  
**Type consistency:** `CrackWidthEta.AutoPsi` / `ScaleLongTotal` / `ScaledMoments` единообразны в T1–T3.
