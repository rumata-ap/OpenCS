# OpenSees backend для OpenCS

## Настройка

Backend запускает внешний Tcl-интерпретатор OpenSees. Для opt-in интеграционных тестов и пользовательского запуска задайте путь:

```powershell
$env:OPENSEES_EXE = 'C:\path\to\OpenSees.exe'
```

Явный `executablePath` в `ParamsJson` имеет приоритет над `OPENSEES_EXE`; затем используется bundled путь рядом с приложением.

## Поддерживаемый сценарий stage 0–1

Задача `opensees_section_moment_curvature` принимает подготовленное `CScore.CrossSection`, преобразует его fiber-области в SI (`м`, `Н`, `Па`, `Н·м`) и выполняет одноосный монотонный moment–curvature анализ с заданной продольной силой `N`.

Поддерживаются направления `Mx` и `My`, но расчёт OpenSees остаётся 2D и выполняется выбранным одним изгибающим DOF. Отображение координат задано явно: `OpenSees Y = CScore Y`, `OpenSees Z = CScore X`.

Параметры задачи:

```json
{
  "maxCurvature": 0.01,
  "increments": 20,
  "axis": "Mx",
  "timeoutSeconds": 300,
  "executablePath": "C:/path/to/OpenSees.exe"
}
```

## Артефакты

Каждый запуск получает уникальный каталог `OpenSeesArtifacts/<UTC>_<random>/`:

- `script.tcl` — сгенерированный Tcl;
- `manifest.json` — статус и диагностика;
- `stdout.txt`, `stderr.txt` — потоки OpenSees;
- `exit.json` — код выхода, длительность и признаки timeout/cancellation;
- `section_history.out` — строгая история шага, силы и кривизны;
- `fiber_history.out` — recorder stress–strain выбранного волокна;
- `node_history.out` — recorder перемещений узла;
- `completed.marker` — признак завершения Tcl-сценария.

Ошибки запуска, отсутствующий marker и нечисловая/неполная история возвращаются как `error` или `not_converged`, а каталог артефактов сохраняется.

`Custom`-диаграммы на этом этапе поддерживаются только как монотонная кусочно-линейная огибающая. Разгрузка, циклическая память и hysteresis намеренно не переносятся в OpenSees до отдельного плана.

Следующая граница реализации — отдельный план `opensees-section-interaction`: сначала одноосный `N-M`, затем пространственный `N-Mx-My`, заданные усилия и пакетная изоляция.
