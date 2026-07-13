# η (п. 8.1.15 СП 63) для задач ширины раскрытия трещин — Design

**Date:** 2026-07-13  
**Status:** draft → pending user review  
**Branch context:** реализовывать на `feature/crack-width-tasks` **после** merge `master` (на `master` уже есть `RodEtaWiring`, UI η для `strain_state` / `limit_moment`).

## Goal

Подключить учёт коэффициента η по п. 8.1.15 СП 63.13330 к задачам `crack_width` и `crack_width_batch` **по тому же контуру**, что у поиска плоскости деформаций (`strain_state`), с автоматизацией параметра длительности ψ для формульного режима.

## Non-goals

- Не подключать η к `cracking` / `cracking_batch` (нет пары длительных/полных моментов).
- Не менять формулы `CrackWidthSolver` (acrc, ψs трещин, ls и т.д.) — только входные моменты.
- Не выводить длительную долю из одной строки РСУ без явного режима (`share` / `manual` / `force_item_long` / `two_sets`).
- Не автоматизировать импорт «длительных» vs «полных» наборов из SCAD в рамках этой фичи.

## Background (как сделано в strain_state)

1. UI: `SupportsEta` → блок η в `CalcTaskPropsDialog` (L, μx/μy, ψx/ψy, iterative, порог гибкости).
2. Params: поля η сериализуются через `LimitForceParams` (`etaEnabled`, `etaIterative`, `etaL`, `etaMuX`/`etaMuY`, `etaPsiX`/`etaPsiY`, `etaSlendernessThreshold`).
3. Runtime: при `EtaEnabled` вызывается `CScore.Sp63.RodEtaWiring.Apply(section, N, Mx, My, l0x, l0y, psiX, psiY, iterative, jointSolve, …)` → `MxEff`/`MyEff`.
4. Результат: в `DataJson` блок `eta` с диагностикой по осям (η, Ncr, D, l0, h, history, …).

Формульный режим использует ψ из п. 8.1.15: **ψ = M₁ₗ / M₁** (доля момента от длительных нагрузок), `φₗ = 1+ψ` (не более 2).

## Decisions

| # | Решение | Обоснование |
|---|---------|-------------|
| D1 | Одна η по **полной** нагрузке `(N, Mx_total, My_total)`; те же η усиливают и длительные, и полные моменты | Как у `strain_state`; пользователь явно выбрал этот вариант |
| D2 | ψx/ψy для формульного режима **считать автоматически** из `\|M_long/M_total\|` по осям; в UI для трещин поля ψ скрыты | ψ по смыслу СП 63 совпадает с уже вводимой парой long/total |
| D3 | Поля η хранить в том же `ParamsJson`, что и `CrackWidthTaskParams` (ключи в стиле `LimitForceParams`) | Один JSON на задачу; UI уже умеет `ApplyEtaParams` |
| D4 | `cracking*` вне scope | Нет long/total |
| D5 | Предусловие: merge `master` → feature-ветка трещин | Иначе нет `RodEtaWiring` / UI η |

## Behaviour

### Параметры задачи

Расширить сериализацию параметров трещин так, чтобы при включённой η в JSON попадали те же ключи, что пишет `LimitForceParams.ToJson()` для η:

- `etaEnabled`, `etaIterative`
- `etaL`, `etaMuX`, `etaMuY`
- `etaSlendernessThreshold` (опционально)
- `etaPsiX` / `etaPsiY` — **не обязательны** для трещин (handler всегда пересчитывает); при желании можно писать вычисленные значения для прозрачности при редактировании, но UI их не редактирует

Практическая реализация (на выбор исполнителя, предпочтительно минимальный diff):

- **P1 (предпочтительно):** в `Commit()` для `IsCrackWidthAny` строить объект/`CrackWidthTaskParams`, затем если `EtaEnabled` — смержить поля через существующий `ApplyEtaParams` в промежуточный `LimitForceParams` и объединить JSON; **или** добавить в `CrackWidthTaskParams` зеркальные свойства η + `ToJson`/`Parse` по аналогии с `LimitForceParams`.
- **P2:** handler читает η через `LimitForceParams.Parse(task.ParamsJson)` (как `StrainStateHandler`) параллельно с `CrackWidthTaskParams.Parse` — оба парсера игнорируют чужие ключи. Это уже работает для strain_state и предпочтительно для handler **без** дублирования логики Parse.

Рекомендация: **handler — P2** (`LimitForceParams.Parse` для η); **UI Commit — дописать η-поля в JSON трещин** через `ApplyEtaParams` / merge, как для `strain_state`.

### UI

- `SupportsEta` расширить:  
  `strain_state | strain_state_batch | limit_moment | limit_moment_batch | crack_width | crack_width_batch`
- Блок η (GroupBox) показывать для трещин так же, как для НДС.
- Для `IsCrackWidthAny`: **не показывать** поля ψx/ψy (`ShowEtaFormulaFields` остаётся для НДС/limit; для трещин либо отдельный флаг `ShowEtaPsiFields => ShowEtaFormulaFields && !IsCrackWidthAny`, либо скрыть TextBox ψ при трещинах).
- Подсказка (строка локализации): что ψ берётся как \|M_long/M_total\| из режима длительной нагрузки.
- При редактировании задачи трещин восстанавливать η-поля так же, как для strain_state (`LimitForceParams.Parse` / существующий restore-блок).

### Runtime: одиночный `CrackWidthHandler`

Порядок:

1. Как сейчас: `ResolveAndBuildDiagramms`, `CrackWidthTaskParams.Parse`, сбор `nTotal`, `mxTotal`/`myTotal`, `mxLong`/`myLong` по `ForcesMode`.
2. Если `LimitForceParams.Parse(...).EtaEnabled`:
   - `psiX = AutoPsi(mxLong, mxTotal)`, `psiY = AutoPsi(myLong, myTotal)`  
     где `AutoPsi(ml, mt) = (|mt| < ε) ? 1.0 : Clamp(|ml/mt|, 0, 1)`.  
     Режим `total_only` ⇒ long = total ⇒ ψ = 1.
   - `jointSolve`: `(mx, my) => new StrainSolver(section, CalcType.N, …settings…).Solve(nTotal, mx, my)`  
     (сервисная комбинация; согласовано с тем, что `CrackWidthSolver` для «полной» части опирается на кратковременный/сервисный контур — уточнить tol/maxIter/ten из `CalcSettings`, как в `StrainStateHandler`).
   - `wiring = RodEtaWiring.Apply(section, nTotal, mxTotal, myTotal, l0x, l0y, psiX, psiY, iterative, jointSolve, threshold)`
   - Масштаб по осям:  
     `sx = (|mxTotal| < ε) ? 1.0 : wiring.MxEff / mxTotal`  
     `sy = (|myTotal| < ε) ? 1.0 : wiring.MyEff / myTotal`  
     Затем:  
     `mxTotalEff = wiring.MxEff`, `myTotalEff = wiring.MyEff`,  
     `mxLongEff = mxLong * sx`, `myLongEff = myLong * sy`.
3. `CrackWidthSolver.Compute(N: nTotal, mxLong: mxLongEff, mxTotal: mxTotalEff, myLong: myLongEff, myTotal: myTotalEff)`.
4. В `DataJson`: сохранить исходные и eff-моменты; добавить блок `eta` **в том же формате**, что `StrainStateHandler` (mode, l0, h, η, Ncr, history, …), плюс `psiX`/`psiY` (фактически использованные).

Ошибки η (неустойчивость, экстраполяция) — поведение как у strain_state (не глотать отдельно, если сейчас wiring/solver бросает или пишет флаги в диагностику — отразить в `eta`).

### Runtime: `CrackWidthBatchHandler`

- Те же правила **построчно**: для каждой строки full (+ long по режиму) один вызов η по total этой строки, затем масштаб long/total, затем `Compute`.
- В batch-строку результата добавить краткую диагностику η (как минимум `etaX`/`etaY` и/или флаг `eta_enabled`); полная история — по аналогии с `strain_state_batch` (если там уже пишут `eta` в row — повторить; если нет — минимум ηx/ηy + original/eff моменты).

### Результат в UI

- Одиночный `CrackWidthResultView`: показать блок диагностики η при наличии `eta` в JSON (можно переиспользовать разметку/логику из сводки strain_state, не обязательно 1:1 в первой итерации — минимум: ηx, ηy, Mx/My original→eff).
- Batch: колонки или expandable не обязательны в v1; достаточно данных в JSON + статус; колонки η — nice-to-have, не блокер.

## Auto-ψ details

```
ε = 1e-9  (или принятый в проекте порог «нулевого» момента)

AutoPsi(M_long, M_total):
  if |M_total| < ε → return 1.0
  return Clamp(|M_long / M_total|, 0.0, 1.0)
```

Крайние случаи:

| ForcesMode | Ожидание ψ |
|------------|------------|
| `total_only` | 1 |
| `share` = s | ≈ s по обеим осям (если моменты масштабируются одной долей) |
| `manual` / `force_item_long` / `two_sets` | по фактическим отношениям на ось; оси независимы |

Итеративный режим: `AutoPsi` не влияет на `RodEtaWiring` (ψ не используется), но можно всё равно записать вычисленные ψ в `eta` для справки.

## Testing

1. **Unit (CScore.Tests):** чистая функция/хелпер `AutoPsi` (или тестировать через тонкий public helper, если вынесем).
2. **Unit:** при заданных N, M_total, M_long=share·M_total, формульная η с авто-ψ даёт `M_long_eff / M_total_eff ≈ share` (одинаковый масштаб).
3. **Регрессия:** при `etaEnabled=false` результат `CrackWidthSolver` бит-в-бит как сейчас (те же входные моменты).
4. **Smoke UI (ручной):** создать `crack_width` с η, режим `share` 0.7 — в диалоге нет полей ψ; в результате есть η и усиленные моменты.

## Implementation prerequisites

1. `git checkout feature/crack-width-tasks`
2. `git merge master` (после merge origin→local master с η / SCAD XLS) — разрешить конфликты в `CalcTaskPropsDialog`, Strings, handlers при необходимости
3. Довести незакоммиченный UI трещин на feature-ветке, затем эта фича

## Risks / open points (resolved)

| Риск | Митигация |
|------|-----------|
| Разные эксцентриситеты long vs total | Принято осознанно (D1) |
| `jointSolve` на CalcType.N vs CL | Фиксируем N для η, как сервисная полная комбинация; CL остаётся внутри `CrackWidthSolver` для длительной части acrc |
| Два парсера одного JSON | Ок: оба игнорируют чужие ключи |

## Out of scope follow-ups

- Автоподбор long/total из метаданных загружений SCAD/СП20
- η для `cracking*`
- Полный паритет UI диагностики η со вкладкой «Сводка» НДС (можно вторым PR)
