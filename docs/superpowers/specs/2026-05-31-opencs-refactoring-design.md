# Дизайн-документ: Рефакторинг и документирование OpenCS

Дата: 2026-05-31

## Контекст

OpenCS — WPF-приложение для расчёта ж/б сечений по СП 63.13330. Состоит из трёх проектов: CSmath (математическая библиотека), CScore (доменная модель и вычисления), OpenCS (WPF-приложение). Проект CSdb на диске дублирует часть OpenCS и не входит в решение.

По результатам аудита выявлено: 9 критических багов (краши при выполнении), системное нарушение MVVM, отсутствие документации, дублирование кода между CSdb и OpenCS, мутация объектов операторами.

## Подход

Три слоя по порядку: (1) библиотеки CSmath + CScore, (2) MVVM-рефакторинг OpenCS, (3) дедупликация и документация. Внутри каждого слоя — сначала баги, потом рефакторинг. CSdb удаляется.

---

## Слой 1: CSmath — баги и рефакторинг

### 1.1 Критические баги CSmath

**V1.1.1: Теневые поля в Vector2D/Vector3D**
- Файлы: `Vector2D.cs`, `Vector3D.cs`
- Проблема: поля `arr` и `n` затеняют поля базового `Vector`. Методы `Norma()`, `Sum()`, `ToArray()`, все операторы базового класса работают с `Vector.arr` (пустым), а не с `Vector2D.arr`/`Vector3D.arr`.
- Решение: убрать теневые поля из Vector2D/Vector3D. Изменить конструкторы так, чтобы они инициализировали `base.arr` и `base.n`. Убрать `new` с `Norma` — сделать её `override` или вынести в абстракцию.

**V1.1.2: Операторы Vector — инвертированная семантика**
- Файл: `Vector.cs`
- Проблема: `operator /(double, Vector)` вычисляет `Vector/double` вместо `double/Vector`. `operator -(double, Vector)` вычисляет `Vector - double` вместо `double - Vector`.
- Решение: исправить реализацию на корректную: для `/` — `result[i] = v1 / v2[i]`, для `-` — `result[i] = v1 - v2[i]`.

**V1.1.3: Vector — конструктор по умолчанию**
- Файл: `Vector.cs`
- Проблема: конструктор по умолчанию создаёт `arr = new double[3]`, но не устанавливает `n = 3`. `N` возвращает 0.
- Решение: установить `n = 3` в конструкторе по умолчанию.

**V1.1.4: Matrix — индексатор и ошибки**
- Файл: `Matrix.cs`
- Проблемы:
  - Индексатор возвращает `-1` при выходе за границы вместо исключения
  - Условие `A.n != 3 && A.m != 3` должно быть `||`
  - `ToVector()` перезаписывает элементы — результат содержит только последнюю строку
  - Метод `JacС` содержит кириллический символ `С` (U+0421)
- Решение:
  - Заменить возвращение `-1` на `throw new ArgumentOutOfRangeException`
  - Заменить `&&` на `||`
  - Исправить `ToVector()` — убрать двойной цикл, использовать покоординатное копирование
  - Переименовать `JacС` → `JacC` (латиница)

**V1.1.5: Plane — NullReferenceException в конструкторе**
- Файл: `Plane.cs`
- Проблема: setter `P1` вызывает `Update()`, который обращается к `P2`/`P3` — ещё не присвоенным.
- Решение: добавить флаг `_initializing` или отложить вызов `CalcPlane()` до полной инициализации. Простейший вариант — убрать `Update()` из setter'ов и вызывать его явно после конструктора.

**V1.1.6: Line2d — ArgumentOutOfRangeException**
- Файл: `Line2d.cs`
- Проблема: `new List<Vector2D>(1)` создаёт список с ёмкостью 1, но 0 элементов. Доступ `[0]` бросает исключение.
- Решение: заменить на `new List<Vector2D> { new Vector2D(x, y) }`.

**V1.1.7: Операторы ^ — нестандартная семантика**
- Файлы: `Matrix.cs`, `Vector2D.cs`, `Vector3D.cs`
- Проблема: `^` используется для матричного умножения (Matrix) и векторного произведения (Vector2D/3D). В C# `^` — XOR, в математике — возведение в степень.
- Решение: заменить `operator ^` на именованные методы: `Matrix.Multiply(Matrix other)` и `Vector3D.Cross(Vector3D v)`. Для `Vector2D.Cross` — аналогично. Оставить `operator *` для покоординатного умножения (Hadamard), но добавить XML-doc с пояснением.

### 1.2 Рефакторинг CSmath

**V1.2.1: Иммутабельные операторы**
- Файлы: `Vector.cs`, `Vector2D.cs`, `Vector3D.cs`, `Matrix.cs`, `XY.cs`, `Contour.cs`, `Fiber.cs`, `FiberRegion.cs`, `GeoProps.cs`, `Load.cs`, `Region.cs`
- Проблема: операторы `+`, `-`, `*` мутируют левый операнд и возвращают его. Нарушает принцип наименьшего удивления.
- Решение: все операторы создают новый объект и возвращают его, не модифицируя исходные. Это затрагивает все доменные классы в CScore тоже, поэтому менять последовательно.

**V1.2.2: ISpline — инкапсуляция**
- Файл: `ISpline.cs`
- Проблема: свойства `X`, `Y`, `A`, `B`, `C`, `D` имеют публичные `set`, что позволяет сломать состояние сплайна извне.
- Решение: сделать `set` защищённым или убрать. Классы-реализации устанавливают массивы в конструкторе/методе `ComputeSplineCoefficients`.

**V1.2.3: Согласование сплайнов**
- Файлы: `LSpline.cs`, `CSpline.cs`, `HSpline.cs`, `ASpline.cs`
- Проблемы:
  - `LSpline` принимает `double[]` вместо `IEnumerable<double>`
  - `DY` в `CSpline` и `HSpline` не инициализирован (null)
  - `C`, `D` в `LSpline` и `CSpline` — null
  - `Interpolant()` есть в ASpline и HSpline, но не в ISpline
  - Ошибочные сообщения об ошибках (ссылаются на `dy` параметр, которого нет)
- Решение:
  - Привести все сплайны к `IEnumerable<double>` в конструкторе
  - Добавить `Interpolant()` в `ISpline`
  - Инициализировать `DY`, `C`, `D` пустыми массивами или документировать, что они не используются для данного типа сплайна
  - Исправить сообщения об ошибках

**V1.2.4: Именование и стиль**
- `Range.Affiliation` → `Range.Contains`
- `Matrix.Summ()` → `Matrix.Sum()`
- `Vector.Norma()` метод vs `Vector2D.Norma` свойство — сделать `Norma` свойством везде
- Публичные поля в `Load`, `Kurvature`, `ConcreteProps`, `Boundary` → свойства
- `cosAlfa`/`cosBeta`/`k`/`b` в `Line2d` → PascalCase

---

## Слой 1: CScore — баги и рефакторинг

### 1.3 Критические баги CScore

**V1.3.1: FiberRegionData — IndexOutOfRangeException**
- Файл: `FiberRegionData.cs`
- Проблема: второй конструктор использует `new List<double>(count)` для создания списков, но `List<double>(count)` задаёт ёмкость, а не размер. Доступ по индексу `[i]` бросает `ArgumentOutOfRangeException`.
- Решение: заменить `new List<double>(count)` на `new double[count]` (как в первом конструкторе) или использовать инициализацию через Enumerable.Repeat.

**V1.3.2: Out.cs — кириллический символ**
- Файл: `Out.cs`
- Проблема: `IsСonverge` содержит кириллическую `С` (U+0421) вместо латинской `C`.
- Решение: переименовать в `IsConverge`.

**V1.3.3: Region — присвоение в пустой список**
- Файл: `Region.cs`
- Проблема: конструкторы присваивают `Contours[0] = c` на пустой список.
- Решение: заменить на `Contours.Add(c)` или `Contours = [c]`.

**V1.3.4: Region — Hull setter**
- Файл: `Region.cs`
- Проблема: setter `Hull` записывает `h = value` в локальную переменную, которая теряется.
- Решение: исправить логику setter'а — корректно обновлять `Contours[0]` или список `Contours`.

**V1.3.5: ReBarLayer — перепутанные оси**
- Файл: `ReBarLayer.cs`
- Проблема: `X = ymax - a` и `X = ymin + a` — координаты Y присваиваются в X.
- Решение: заменить `X` на `Y`.

**V1.3.6: ReBarLayer — затенение параметра As**
- Файл: `ReBarLayer.cs`
- Проблема: свойство `As` перезаписывается вычисленным значением до использования оригинального параметра в `Nd = Area / As`.
- Решение: вычислить `Nd` до перезаписи `As` или использовать локальную переменную для вычисленного значения.

**V1.3.7: RCFiberRegion — двойное добавление holes**
- Файл: `RCFiberRegion.cs`
- Проблема: конструктор добавляет holes дважды: через `new List<Contour>(holes)` и через `foreach`.
- Решение: убрать цикл `foreach`, оставив только инициализацию списка.

**V1.3.8: RCFiberRegion — AreaOfTensileReBars без проверки**
- Файл: `RCFiberRegion.cs`
- Проблема: доступ `s[0]` без проверки на пустой список.
- Решение: добавить проверку `if (s.Count == 0) return 0;`.

### 1.4 Рефакторинг CScore

**V1.4.1: new → virtual/override**
- Файлы: `RCFiberRegion.cs`, `FiberRegion.cs`, `ReBar.cs`, `CircleP.cs`, `ReBarLayer.cs`
- Проблема: методы `Clone()`, `ToCentr()`, `SetEps()`, `ToStart()` используют `new` вместо `virtual`/`override`, что ломает полиморфизм.
- Решение: объявить методы `virtual` в базовом классе и `override` в производных.

**V1.4.2: Diagramm.Sig — дублирование**
- Файл: `Diagramm.cs`
- Проблема: `Sig(Fiber)`, `Sig(ReBar)`, `Sig(StressPoint)` содержат ~80% идентичного кода.
- Решение: вынести общую логику в приватный метод `ComputeSig(double eps, double area, double eMod, ...)`, перегрузки вызывают его с нужными параметрами.

**V1.4.3: Geo — дублирование SliceXY/SliceX/SliceY**
- Файл: `Geo.cs`
- Проблема: три метода — почти идентичная логика с разными осями.
- Решение: создать приватный метод `Slice(XYAxis axis, ...)` и вызвать его из трёх публичных методов.

**V1.4.4: GeoProps — дублирование операторов**
- Файл: `GeoProps.cs`
- Проблема: 5 операторов с ~15 строк идентичного кода в каждом.
- Решение: вынести общую логику в приватный метод `Combine(GeoProps a, GeoProps b, Func<double,double,double> op)`.

**V1.4.5: Опечатка SetSrtress**
- Файл: `Region.cs`
- Проблема: метод `SetSrtress` — опечатка.
- Решение: переименовать в `SetStress`.

---

## Слой 2: MVVM-рефакторинг OpenCS

### 2.1 Удаление WPF-зависимостей из ViewModel

**V2.1.1: Логирование**
- Файлы: все ViewModel
- Проблема: создание `TextBlock` и добавление в `ListBox.Items` для логирования.
- Решение: заменить `ListBox logger` на `ObservableCollection<LogEntry>`, где `LogEntry` — простая модель (`string Message`, `Brush Color`, `DateTime Timestamp`). Создать `ILogService` с методом `Log(string message, LogLevel level)`. View подписывается на коллекцию через Binding.

**V2.1.2: PlotService**
- Файлы: `ContourVM.cs`, `RCFiberRegionVM.cs`
- Проблема: прямая манипуляция `WpfPlot` из ViewModel.
- Решение: создать `IPlotService` с методами `Clear()`, `AddScatter()`, `AddLine()`, `SetAxisLimits()` и т.д. Реализация `WpfPlotService` оборачивает ScottPlot. ViewModel вызывает `IPlotService`, View подключает реализацию.

**V2.1.3: FileDialogService**
- Файлы: `ContourVM.cs`, `FromDxfVM.cs`, `MaterialVM.cs`, `RCFiberRegionVM.cs`
- Проблема: прямое использование `OpenFileDialog`/`SaveFileDialog` в ViewModel.
- Решение: создать `IFileDialogService` с методами `OpenFile(filter)`, `SaveFile(filter)`. Реализация `WpfFileDialogService` оборачивает системные диалоги.

**V2.1.4: Навигация**
- Файлы: `AppViewModel.cs`, все View
- Проблема: `CurrentPage` типа `UserControl` — ViewModel создаёт экземпляры View.
- Решение: заменить `UserControl CurrentPage` на `object CurrentViewModel` + `DataTemplate` в `App.xaml` для связи ViewModel→View. ViewModel знает только о других ViewModel, а WPF автоматически подбирает View по типу.

### 2.2 Разделение AppViewModel

**V2.2.1: Вынести сервисы**
- Создать `MaterialService` — управление материалами, CRUD, фильтрация
- Создать `ContourService` — управление контурами, импорт/экспорт
- Создать `FiberRegionService` — управление волоконными областями
- `AppViewModel` остаётся координатором: навигация, доступ к сервисам, текущий выбор

### 2.3 Исправление конвертеров

**V2.3.1: CultureInfo.InvariantCulture**
- Файл: `Converters.cs`
- Проблема: `double.Parse` без указания культуры — падает на русской локали.
- Решение: заменить все `double.Parse(value)` на `double.Parse(value, CultureInfo.InvariantCulture)`.

### 2.4 Исправление DataSourceVM

**V2.4.1: Мутация MaterialChars**
- Файл: `DataSourceVM.cs`
- Проблема: `SelectMaterial()` умножает коэффициенты на месте (`c[i].E *= kE`). Повторный вызов накапливает масштабирование.
- Решение: клонировать `MaterialChars` перед модификацией или вычислять масштабированные значения без мутации источника.

**V2.4.2: Кириллический символ в fileС**
- Файл: `DataSourceVM.cs`
- Проблема: переменная `fileС` содержит кириллическую `С`.
- Решение: переименовать в `fileC`.

---

## Слой 3: Дедупликация и документация

### 3.1 Удаление CSdb

**V3.1.1: Удалить проект CSdb**
- Действие: удалить директорию `CSdb/` и убрать любые ссылки на неё
- Обоснование: CSdb на 100% дублирует OpenCS (RelayCommand, ViewModelBase, Converters, MaterialCharsVM) или является устаревшей версией (FromDataSourceWindow, MaterialWindow). Не входит в решение OpenCS.sln.

### 3.2 XAML-дедупликация

**V3.2.1: MaterialPage — шаблон для сетки свойств**
- Файл: `MaterialPage.xaml`
- Проблема: ~695 строк копипаста — 4 колонки (C, CL, N, NL) с 17 строками идентичных DockPanel+TextBox.
- Решение: создать `ItemsControl` с `DataTemplate` для одного типа расчёта, привязанный к коллекции `MaterialCharsVM[]`. Шаблон генерирует все 4 колонки автоматически.

**V3.2.2: GeoProps — общий UserControl**
- Файлы: `RCFiberRegionPage.xaml`, `RCFiberRegionView.xaml`
- Проблема: ~80 строк идентичного XAML для отображения геометрических свойств.
- Решение: вынести в `GeoPropsView.xaml` — `UserControl` с `DependencyProperty` для `GeoProps`.

### 3.3 Удаление мёртвого кода

**V3.3.1: XYDB.cs**
- Файл: `OpenCS/ViewModels/XYDB.cs`
- Проблема: не используется в проекте.
- Решение: удалить.

**V3.3.2: Пустые стабы**
- Файлы: `FiberRegionPage.xaml`, `FiberRegionView.xaml`, пустые вкладки в XAML
- Решение: удалить или пометить как TODO.

### 3.4 XML-документация

**V3.4.1: CSmath — документация на русском**
- Добавить XML doc comments ко всем открытым членам в каждом файле:
  - Классы: краткое описание класса
  - Публичные методы: описание, параметры, возвращаемое значение
  - Свойства: описание
  - Операторы: описание семантики (особенно `*`, `+`, `-`)
- Особое внимание: `ISpline`, `Vector`, `Matrix`, `Plane` — ядро библиотеки

**V3.4.2: CScore — документация на русском**
- Те же правила для всех файлов CScore
- Особое внимание: доменные концепции — типы материалов (`MatType`), типы расчётов (`CalcType`), типы диаграмм (`DiagrammType`), метод волокон (`FiberRegion.Integral`), плоскость деформаций (`Basis`, `Kurvature`)

**V3.4.3: OpenCS — документация на русском**
- Документировать ViewModel, сервисы, конвертеры
- Не документировать внутренние детали XAML

### 3.5 Обновление CLAUDE.md

- Обновить после завершения всех изменений
- Отразить новую структуру, удаление CSdb, имена сервисов

---

## Порядок выполнения

1. CSmath: баги V1.1.1–V1.1.7
2. CSmath: рефакторинг V1.2.1–V1.2.4
3. CScore: баги V1.3.1–V1.3.8
4. CScore: рефакторинг V1.4.1–V1.4.5
5. OpenCS: MVVM V2.1.1–V2.1.4, V2.2.1, V2.3.1, V2.4.1–V2.4.2
6. Дедупликация V3.1.1, V3.2.1–V3.2.2, V3.3.1–V3.3.2
7. Документация V3.4.1–V3.4.3
8. Обновление CLAUDE.md V3.5

Каждый этап компилируется и тестируется вручную перед переходом к следующему.