# Рефакторинг и документирование OpenCS — План реализации

> **Для агентов:** ОБЯЗАТЕЛЬНЫЙ НАВЫК: Используйте superpowers:subagent-driven-development или superpowers:executing-plans для реализации этого плана задача за задачей. Шаги используют синтаксис чекбоксов (`- [ ]`) для отслеживания.

**Цель:** Исправить критические баги, отрефакторить архитектуру MVVM, удалить дублирование и задокументировать кодовую базу OpenCS на русском языке.

**Архитектура:** Три слоя — сначала библиотеки (CSmath → CScore), затем MVVM-рефакторинг OpenCS, затем дедупликация и документация. Каждый шаг компилируется отдельно.

**Стек:** .NET 9.0, C#, WPF, EF Core, ScottPlot, NetTopologySuite

---

## Файловая структура изменений

### Создаваемые файлы
- `OpenCS/Services/ILogService.cs` — интерфейс логирования
- `OpenCS/Services/LogService.cs` — реализация логирования через ObservableCollection
- `OpenCS/Services/IPlotService.cs` — интерфейс для отрисовки графиков
- `OpenCS/Services/WpfPlotService.cs` — реализация через ScottPlot
- `OpenCS/Services/IFileDialogService.cs` — интерфейс файловых диалогов
- `OpenCS/Services/WpfFileDialogService.cs` — реализация файловых диалогов

### Модифицируемые файлы (по задаче)
- CSmath: `Vector.cs`, `Vector2D.cs`, `Vector3D.cs`, `Matrix.cs`, `Plane.cs`, `Line2d.cs`, `ISpline.cs`, `LSpline.cs`, `CSpline.cs`, `HSpline.cs`, `ASpline.cs`, `Range.cs`
- CScore: `FiberRegionData.cs`, `Out.cs`, `Region.cs`, `ReBarLayer.cs`, `RCFiberRegion.cs`, `Diagramm.cs`, `Geo.cs`, `GeoProps.cs`, `XY.cs`, `Contour.cs`, `Fiber.cs`, `FiberRegion.cs`, `Load.cs`, `Kurvature.cs`, `Boundary.cs`, `ConcreteProps.cs`, `Material.cs`
- OpenCS: `AppViewModel.cs`, `ContourVM.cs`, `RCFiberRegionVM.cs`, `FromDxfVM.cs`, `MaterialVM.cs`, `RebarsVM.cs`, `Converters.cs`, `DataSourceVM.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml`

### Удаляемые файлы/папки
- `CSdb/` — весь каталог
- `OpenCS/ViewModels/XYDB.cs`

---

## Задача 1: Исправление Vector — теневые поля и операторы

**Файлы:**
- Modify: `CSmath/Vector.cs`
- Modify: `CSmath/Vector2D.cs`
- Modify: `CSmath/Vector3D.cs`

- [ ] **Шаг 1.1: Исправить конструктор Vector по умолчанию**

В `CSmath/Vector.cs` строка 14-17, добавить `n = 3`:

```csharp
public Vector()
{
   n = 3;
   arr = new double[3];
}
```

- [ ] **Шаг 1.2: Исправить operator /(double, Vector) и operator -(double, Vector) в Vector.cs**

В `CSmath/Vector.cs` строки 264-273, заменить `operator /(double prime, Vector v1)`:

```csharp
public static Vector operator /(double prime, Vector v1)
{
   double[] res = new double[v1.n];
   for (int i = 0; i < v1.n; i++)
   {
      res[i] = prime / v1[i];
   }

   return new Vector(res);
}
```

В `CSmath/Vector.cs` строки 335-344, заменить `operator -(double prime, Vector v1)`:

```csharp
public static Vector operator -(double prime, Vector v1)
{
   double[] res = new double[v1.n];
   for (int i = 0; i < v1.n; i++)
   {
      res[i] = prime - v1[i];
   }

   return new Vector(res);
}
```

- [ ] **Шаг 1.3: Удалить теневые поля из Vector2D, использовать base-массив**

В `CSmath/Vector2D.cs` — удалить поле `double[] arr = new double[2]` (строка 10). Изменить конструкторы и свойства так, чтобы они работали с `base.arr` и `base.n`. Установить `n = 2` в каждом конструкторе. Убрать `new` с `Norma` — сделать его `public double Norma` (свойство, не скрывающее базовое, т.к. базового свойства Norma нет — есть метод `Norma()`):

```csharp
using System;
using static System.Math;

namespace CSmath.Geometry
{
   [Serializable]
   public class Vector2D : Vector
   {
      public double X { get => this[0]; set => this[0] = value; }
      public double Y { get => this[1]; set => this[1] = value; }
      public double Norma => Sqrt(X * X + Y * Y);
      public Vector2D Unit => new(X / Norma, Y / Norma);

      public Vector2D() : base(2) { }

      public Vector2D(double v1, double v2) : base(2)
      {
         this[0] = v1; this[1] = v2;
      }

      public Vector2D(Vector2D source) : base(source) { }

      public Vector2D(Vector source) : base(2)
      {
         if (source.N >= 2)
         {
            this[0] = source[0]; this[1] = source[1];
         }
      }

      public Vector2D(double[] source) : base(2)
      {
         if (source.Length >= 2)
         {
            this[0] = source[0]; this[1] = source[1];
         }
      }

      public static double CosAngleBetVectors(Vector2D v1, Vector2D v2)
      {
         return (v1.X * v2.X + v1.Y * v2.Y) / (v1.Norma * v2.Norma);
      }

      public Vector ToVector() => new Vector(new double[] { X, Y });

      public static Vector3D Cross(Vector2D v1, Vector2D v2) => v1 ^ v2;

      public static Vector3D operator ^(Vector2D v1, Vector2D v2)
      {
         return new Vector3D
         {
            X = v1.Y * 0 - 0 * v2.Y,
            Y = 0 * v2.X - v1.X * 0,
            Z = v1.X * v2.Y - v1.Y * v2.X
         };
      }

      public static Vector2D operator *(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X * v2.X, Y = v1.Y * v2.Y };
      }
      public static Vector2D operator /(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X / v2.X, Y = v1.Y / v2.Y };
      }
      public static Vector2D operator +(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X + v2.X, Y = v1.Y + v2.Y };
      }
      public static Vector2D operator -(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X - v2.X, Y = v1.Y - v2.Y };
      }
   }
}
```

- [ ] **Шаг 1.4: Аналогично удалить теневые поля из Vector3D**

В `CSmath/Vector3D.cs` — удалить поля `double[] arr` и `int n = 3` (строки 8-9). Сделать `: base(3)` в каждом конструкторе. Убрать `new` с `Norma` и объявление `IVector` (оно уже есть в базовом классе):

```csharp
using static System.Math;

namespace CSmath.Geometry
{
   [Serializable]
   public class Vector3D : Vector
   {
      public double X { get => this[0]; set => this[0] = value; }
      public double Y { get => this[1]; set => this[1] = value; }
      public double Z { get => this[2]; set => this[2] = value; }
      public double Norma => Sqrt(X * X + Y * Y + Z * Z);
      public Vector3D Unit => new Vector3D(X / Norma, Y / Norma, Z / Norma);

      public Vector3D() : base(3) { }

      public Vector3D(double x, double y, double z) : base(3)
      {
         X = x; Y = y; Z = z;
      }

      public Vector3D(Vector3D source) : base(source) { }

      public Vector3D(Vector source) : base(3)
      {
         if (source.N >= 3)
         {
            this[0] = source[0]; this[1] = source[1]; this[2] = source[2];
         }
      }

      public Vector3D(Vector2D source) : base(3)
      {
         this[0] = source.X; this[1] = source.Y; this[2] = 0;
      }

      public Vector3D(double[] source) : base(3)
      {
         if (source.Length >= 3)
         {
            this[0] = source[0]; this[1] = source[1]; this[2] = source[2];
         }
      }

      public static double CosAngleBetVectors(Vector3D v1, Vector3D v2)
      {
         return (v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z) / (v1.Norma * v2.Norma);
      }

      public Vector2D ToVector2d() => new Vector2D() { X = X, Y = Y };

      public Vector ToVector() => new Vector(new double[] { X, Y, Z });

      public static Vector3D Cross(Vector3D v1, Vector3D v2) => v1 ^ v2;

      public static Vector3D operator ^(Vector3D v1, Vector3D v2)
      {
         return new Vector3D
         {
            X = v1.Y * v2.Z - v1.Z * v2.Y,
            Y = v1.Z * v2.X - v1.X * v2.Z,
            Z = v1.X * v2.Y - v1.Y * v2.X
         };
      }

      public static Vector3D operator *(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X * v2.X, Y = v1.Y * v2.Y, Z = v1.Z * v2.Z };
      }

      public static Vector3D operator /(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X / v2.X, Y = v1.Y / v2.Y, Z = v1.Z / v2.Z };
      }

      public static Vector3D operator +(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X + v2.X, Y = v1.Y + v2.Y, Z = v1.Z + v2.Z };
      }

      public static Vector3D operator -(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X - v2.X, Y = v1.Y - v2.Y, Z = v1.Z - v2.Z };
      }
   }
}
```

- [ ] **Шаг 1.5: Добавить метод Norma как свойство в Vector.cs**

В `CSmath/Vector.cs` заменить метод `Norma()` на свойство `Norma` (строка 66-71), одновременно оставив метод `Norma()` для обратной совместимости как `CalculateNorma()` или добавив свойство:

```csharp
public double Norma
{
   get
   {
      double sum = 0;
      for (int i = 0; i < arr.Length; i++) sum += arr[i] * arr[i];
      return Math.Sqrt(sum);
   }
}

// Обратная совместимость — метод вызывает свойство
public double NormaMethod() => Norma;
```

Важно: переименование `Norma()` из метода в свойство может сломать вызовы `Norma()` в CScore. Поэтому нужно проверить все вызовы. Если вызовы используют `Norma()` как метод, нужно либо: (а) оставить метод и добавить свойство с другим именем, либо (б) заменить все вызовы. Оптимальный вариант — оставить `Norma()` как метод и добавить свойство `Length` для новых вызовов.

- [ ] **Шаг 1.6: Удалить Summ() — дубликат Sum()**

В `CSmath/Vector.cs` строки 128-133: удалить метод `Summ()`. Если есть вызовы `Summ()` в CScore/OpenCS — заменить на `Sum()`.

- [ ] **Шаг 1.7: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`
Ожидание: успешная компиляция. Если есть ошибки вызовов `Norma()` vs `Norma` — исправить.

- [ ] **Шаг 1.8: Коммит**

```bash
git add CSmath/Vector.cs CSmath/Vector2D.cs CSmath/Vector3D.cs
git commit -m "fix: убрать теневые поля в Vector2D/Vector3D, исправить операторы / и - для double×Vector, конструктор Vector по умолчанию"
```

---

## Задача 2: Исправление Matrix — индексатор, ToVector, JacС

**Файлы:**
- Modify: `CSmath/Matrix.cs`

- [ ] **Шаг 2.1: Заменить индексатор — возвращать исключение вместо -1**

В `CSmath/Matrix.cs` строки 31-53, заменить индексатор:

```csharp
public double this[int i, int j]
{
   get
   {
      if (i < 0 || i >= n || j < 0 || j >= m)
         throw new ArgumentOutOfRangeException($"Индексы вне диапазона: [{i},{j}], размер матрицы {n}x{m}");
      return arr[i, j];
   }
   set
   {
      if (i < 0 || i >= n || j < 0 || j >= m)
         throw new ArgumentOutOfRangeException($"Индексы вне диапазона: [{i},{j}], размер матрицы {n}x{m}");
      arr[i, j] = value;
   }
}
```

- [ ] **Шаг 2.2: Исправить ToVector() — убрать перезапись**

В `CSmath/Matrix.cs` строки 101-110, заменить:

```csharp
public Vector ToVector()
{
   Vector res = new Vector(n * m);
   int k = 0;
   for (int i = 0; i < n; i++)
   {
      for (int j = 0; j < m; j++)
      {
         res[k++] = arr[i, j];
      }
   }
   return res;
}
```

- [ ] **Шаг 2.3: Исправить условие && → || в operator *(Matrix, Vector3D)**

В `CSmath/Matrix.cs` строка 415, заменить:

```csharp
if (A.n != 3 || A.m != 3) { throw new System.ArgumentException("Матрица должна быть размерности 3x3"); }
```

- [ ] **Шаг 2.4: Переименовать JacС → JacC (латиница)**

В `CSmath/Matrix.cs` строка 339, заменить `JacС` на `JacC`:

```csharp
public static Matrix JacC(Func<Vector, Vector> func, Vector x, out Vector y, double h = 1e-8)
```

Найти все вызовы `JacС` в проекте и заменить на `JacC`.

- [ ] **Шаг 2.5: Заменить operator ^ на метод Multiply**

В `CSmath/Matrix.cs` строки 363-379, заменить оператор `^` на метод:

```csharp
/// <summary>
/// Матричное умножение двух матриц.
/// </summary>
public static Matrix Multiply(Matrix A, Matrix B)
{
   if (A.M != B.N) { throw new System.ArgumentException("Не совпадают размерности матриц"); }
   Matrix C = new Matrix(A.N, B.M);
   for (int i = 0; i < A.N; ++i)
   {
      for (int j = 0; j < B.M; ++j)
      {
         C[i, j] = 0;
         for (int k = 0; k < A.M; ++k)
         {
            C[i, j] += A[i, k] * B[k, j];
         }
      }
   }
   return C;
}
```

Удалить оператор `^`. Найти все вызовы `^` между матрицами в проекте и заменить на `Matrix.Multiply()`.

- [ ] **Шаг 2.6: Удалить лишнее выделение памяти в конструкторе копирования**

В `CSmath/Matrix.cs` строки 63-69, заменить:

```csharp
public Matrix(Matrix source)
{
   m = source.M;
   n = source.N;
   arr = (double[,])source.arr.Clone();
}
```

Аналогично строки 71-77 для конструктора из `double[,]`:

```csharp
public Matrix(double[,] source)
{
   m = source.GetLength(1);
   n = source.GetLength(0);
   arr = (double[,])source.Clone();
}
```

- [ ] **Шаг 2.7: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`
Ожидание: успешная компиляция. Если есть ошибки вызовов `JacС` или `^` — исправить.

- [ ] **Шаг 2.8: Коммит**

```bash
git add CSmath/Matrix.cs CScore/ CSmath/
git commit -m "fix: индексатор Matrix бросает исключение, ToVector корректно заполняет, && → ||, JacС → JacC, operator ^ → Multiply"
```

---

## Задача 3: Исправление Plane, Line2d, ISpline, сплайны

**Файлы:**
- Modify: `CSmath/Plane.cs`
- Modify: `CSmath/Line2d.cs`
- Modify: `CSmath/ISpline.cs`
- Modify: `CSmath/LSpline.cs`
- Modify: `CSmath/CSpline.cs`
- Modify: `CSmath/HSpline.cs`
- Modify: `CSmath/ASpline.cs`
- Modify: `CSmath/Range.cs`

- [ ] **Шаг 3.1: Исправить Plane — NullReferenceException в конструкторе**

В `CSmath/Plane.cs` убрать `Update()` из setter'ов `P1`, `P2`, `P3` и добавить null-проверку в `Update()`:

```csharp
public Vector3D P1 { get => p1; set { p1 = value; } }
public Vector3D P2 { get => p2; set { p2 = value; } }
public Vector3D P3 { get => p3; set { p3 = value; } }
```

В конструкторе явно вызвать `Update()` после присвоения всех трёх точек:

```csharp
public Plane(Vector3D pt1, Vector3D pt2, Vector3D pt3)
{
    p1 = pt1;
    p2 = pt2;
    p3 = pt3;
    V1 = p2 - p1;
    V2 = p3 - p1;
    CalcPlane();
}
```

Метод `Update()` (без параметров) оставить с null-проверкой:

```csharp
void Update()
{
    if (p1 == null || p2 == null || p3 == null) return;
    V1 = p2 - p1;
    V2 = p3 - p1;
    CalcPlane();
}
```

- [ ] **Шаг 3.2: Исправить Line2d — List<double>(1) → new List { item }**

В `CSmath/Line2d.cs` строки 113-114, заменить:

```csharp
res.pts = new List<Vector2D> { new Vector2D(x, y) };
```

Аналогично в `IntersectionSegments` (строка 132-133):

```csharp
res.pts = new List<Vector2D> { respt };
```

- [ ] **Шаг 3.3: Переименовать Range.Affiliation → Range.Contains**

В `CSmath/Range.cs` переименовать метод `Affiliation` в `Contains`. Найти все вызовы в проекте и заменить.

- [ ] **Шаг 3.4: ISpline — убрать публичные set, добавить Interpolant()**

В `CSmath/ISpline.cs` заменить свойства с публичными set на init или protected set:

```csharp
public interface ISpline
{
    double[] X { get; }
    double[] Y { get; }
    double[] DY { get; }
    double[] A { get; }
    double[] B { get; }
    double[] C { get; }
    double[] D { get; }
    double Interpolate(double value);
    double Derivative(double value, out double interpFunc);
    double Interpolant();
}
```

Обновить все реализации сплайнов: заменить `public double[] X { get; set; }` на `public double[] X { get; protected set; }`.

- [ ] **Шаг 3.5: LSpline — согласовать с ISpline**

Изменить конструктор `LSpline` на `IEnumerable<double>` вместо `double[]`. Инициализировать `DY`, `C`, `D` пустыми массивами. Добавить метод `Interpolant()`, возвращающий `double.NaN` (для LSpline не определён).

- [ ] **Шаг 3.6: CSpline, HSpline — инициализировать null-массивы**

В `CSpline.cs`: инициализировать `DY = Array.Empty<double>()`.
В `HSpline.cs`: инициализировать `C = Array.Empty<double>()`, `D = Array.Empty<double>()`.
Исправить ошибочные сообщения в исключениях (ссылки на `dy` параметр).

- [ ] **Шаг 3.7: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`

- [ ] **Шаг 3.8: Коммит**

```bash
git add CSmath/
git commit -m "fix: Plane null-safety, Line2d list init, Range.Contains, ISpline encapsulation, spline initialization"
```

---

## Задача 4: Исправление CScore — критические баги

**Файлы:**
- Modify: `CScore/FiberRegionData.cs`
- Modify: `CScore/Out.cs`
- Modify: `CScore/Region.cs`
- Modify: `CScore/ReBarLayer.cs`
- Modify: `CScore/RCFiberRegion.cs`

- [ ] **Шаг 4.1: FiberRegionData — заменить List<double>(count) на double[count]**

В `CScore/FiberRegionData.cs` строки 74-86, второй конструктор — заменить `new List<double>(count)` на `new double[count]` (и все аналогичные). Типы свойств `X`, `Y` и т.д. — `IList<double>`, а `double[]` реализует `IList<double>`, так что замена совместима. Строки 90-105 (цикл с индексатором) будут работать корректно с массивом.

- [ ] **Шаг 4.2: Out.cs — переименовать IsСonverge → IsConverge**

В `CScore/Out.cs` строка 13, заменить `IsСonverge` (кириллическая С) на `IsConverge` (латинская C). Найти все использования `IsСonverge` в проекте и заменить.

- [ ] **Шаг 4.3: Region — исправить присвоение в пустой список**

В `CScore/Region.cs` строки 113 и 140, заменить `Contours[0] = c` на `Contours.Add(c)`. В строке 39 (Hull setter) заменить `h = value` на корректную логику:

```csharp
set
{
   if (value != null)
   {
      value.Type = ContourType.Hull;
      if (Contours.Count > 0)
      {
         var hullIndex = Contours.FindIndex(c => c.Type == ContourType.Hull);
         if (hullIndex >= 0)
            Contours[hullIndex] = value;
         else
            Contours.Insert(0, value);
      }
      else
      {
         Contours.Add(value);
      }
   }
}
```

- [ ] **Шаг 4.4: ReBarLayer — перепутанные оси и затенение As**

В `CScore/ReBarLayer.cs` строки 24-25, заменить `X` на `Y`:

```csharp
if (pos == ReBarLayerPos.Bot) Y = ymax - a;
else Y = ymin + a;
```

Строки 28-34 (второй конструктор) — вычислить `Nd` до перезаписи `As`:

```csharp
public ReBarLayer(double d, double As, double a, ReBarLayerPos pos, Region beton) : base(d)
{
   double rebarArea = d * d * Math.PI * 0.25;
   Area = As;
   Diameter = d;
   Pos = pos;
   this.As = rebarArea;
   Nd = Area / this.As;
   // ... rest
}
```

Аналогично для строк 51-58.

- [ ] **Шаг 4.5: RCFiberRegion — убрать двойное добавление holes**

В `CScore/RCFiberRegion.cs` строки 44-56, убрать цикл `foreach`:

```csharp
public RCFiberRegion(Contour contour, IEnumerable<Contour> holes = null, IEnumerable<Fiber> finiteAreas = null)
{
   Tag = contour.Tag;
   Hull = contour;

   if (finiteAreas != null)
      Fibers = finiteAreas.ToList();

   if (holes != null)
   {
      Contours = new List<Contour>(holes);
   };
   // ... rest
```

- [ ] **Шаг 4.6: RCFiberRegion — AreaOfTensileReBars проверка на пустоту**

Найти метод `AreaOfTensileReBars` и добавить проверку:

```csharp
if (s.Count == 0) return 0;
```

перед доступом `s[0]`.

- [ ] **Шаг 4.7: Region — переименовать SetSrtress → SetStress**

В `CScore/Region.cs` строка 319, переименовать метод. Найти все вызовы и заменить.

- [ ] **Шаг 4.8: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`

- [ ] **Шаг 4.9: Коммит**

```bash
git add CScore/
git commit -m "fix: FiberRegionData list init, IsConverge кириллица, Region Contours, ReBarLayer оси, RCFiberRegion holes, SetStress"
```

---

## Задача 5: Рефакторинг CScore — new→virtual/override, дедупликация

**Файлы:**
- Modify: `CScore/XY.cs`, `CScore/Contour.cs`, `CScore/Fiber.cs`, `CScore/FiberRegion.cs`, `CScore/ReBar.cs`, `CScore/Circle.cs`, `CScore/ReBarLayer.cs`, `CScore/RCFiberRegion.cs`
- Modify: `CScore/Diagramm.cs`
- Modify: `CScore/Geo.cs`
- Modify: `CScore/GeoProps.cs`

- [ ] **Шаг 5.1: Объявить Clone() virtual в XY и override в производных**

В `CScore/XY.cs` заменить `public XY Clone()` на `public virtual XY Clone()`.
В `CScore/Contour.cs`, `CScore/Fiber.cs`, `CScore/ReBar.cs`, `CScore/Circle.cs` заменить `new ... Clone()` на `override ... Clone()`.
Для `FiberRegion.cs`, `RCFiberRegion.cs`, `ReBarLayer.cs` — аналогично, но с правильными типами возврата.

- [ ] **Шаг 5.2: Объявить ToCentr(), SetEps(), ToStart() virtual/override**

В `CScore/Region.cs` объявить `ToCentr(XY centr)` и `ToStart()` как `virtual`.
В `CScore/FiberRegion.cs` и `CScore/RCFiberRegion.cs` заменить `new void ToCentr()` на `override void ToCentr()`, аналогично для `SetEps()`.

- [ ] **Шаг 5.3: Diagramm.Sig — вынести общую логику**

Добавить приватный метод `ComputeSig(double eps, double area, double e, double e2, double sig, double eps_p, double nu1, double nu2)` и переписать `Sig(Fiber)`, `Sig(ReBar)`, `Sig(StressPoint)` через него.

- [ ] **Шаг 5.4: Geo — объединить SliceXY/SliceX/SliceY**

Создать приватный метод `Slice(Axis axis, Region region, int nx, int ny)` и вызвать из трёх публичных.

- [ ] **Шаг 5.5: GeoProps — объединить операторы**

Создать приватный метод `Combine(GeoProps a, GeoProps b, Func<double, double, double> op)` и переписать `+`, `-`, `*`, `/` через него.

- [ ] **Шаг 5.6: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`

- [ ] **Шаг 5.7: Коммит**

```bash
git add CScore/
git commit -m "refactor: virtual/override для Clone и ToCentr, Diagramm.ComputeSig, Geo.Slice, GeoProps.Combine"
```

---

## Задача 6: MVVM-рефакторинг — сервисы логирования, графиков, диалогов

**Файлы:**
- Create: `OpenCS/Services/ILogService.cs`
- Create: `OpenCS/Services/LogService.cs`
- Create: `OpenCS/Services/IPlotService.cs`
- Create: `OpenCS/Services/WpfPlotService.cs`
- Create: `OpenCS/Services/IFileDialogService.cs`
- Create: `OpenCS/Services/WpfFileDialogService.cs`
- Modify: `OpenCS/AppViewModel.cs`
- Modify: `OpenCS/ContourVM.cs`
- Modify: `OpenCS/RCFiberRegionVM.cs`
- Modify: `OpenCS/FromDxfVM.cs`
- Modify: `OpenCS/MaterialVM.cs`
- Modify: `OpenCS/RebarsVM.cs`

- [ ] **Шаг 6.1: Создать ILogService и LogService**

```csharp
// OpenCS/Services/ILogService.cs
namespace OpenCS.Services
{
   public enum LogLevel { Info, Warning, Error }
   public record LogEntry(string Message, LogLevel Level, DateTime Timestamp);
   public interface ILogService
   {
      void Info(string message);
      void Warning(string message);
      void Error(string message);
      ObservableCollection<LogEntry> LogEntries { get; }
   }
}
```

```csharp
// OpenCS/Services/LogService.cs
namespace OpenCS.Services
{
   public class LogService : ILogService
   {
      public ObservableCollection<LogEntry> LogEntries { get; } = [];
      public void Info(string message) => LogEntries.Add(new(message, LogLevel.Info, DateTime.Now));
      public void Warning(string message) => LogEntries.Add(new(message, LogLevel.Warning, DateTime.Now));
      public void Error(string message) => LogEntries.Add(new(message, LogLevel.Error, DateTime.Now));
   }
}
```

- [ ] **Шаг 6.2: Создать IPlotService и WpfPlotService**

```csharp
// OpenCS/Services/IPlotService.cs
namespace OpenCS.Services
{
   public interface IPlotService
   {
      void Clear();
      void AddScatter(double[] xs, double[] ys, string label = null);
      void AddLine(double[] xs, double[] ys, string label = null);
      void SetAxisLimits(double xMin, double xMax, double yMin, double yMax);
      void SetTitle(string title);
      void SetXLabel(string label);
      void SetYLabel(string label);
      void Refresh();
   }
}
```

Реализация `WpfPlotService` оборачивает `ScottPlot.WPF.WpfPlot`.

- [ ] **Шаг 6.3: Создать IFileDialogService и WpfFileDialogService**

```csharp
// OpenCS/Services/IFileDialogService.cs
namespace OpenCS.Services
{
   public interface IFileDialogService
   {
      string OpenFile(string filter = null);
      string SaveFile(string filter = null, string defaultExt = null);
   }
}
```

- [ ] **Шаг 6.4: Заменить ListBox logger на ILogService во всех ViewModel**

В `AppViewModel.cs`: заменить `internal ListBox logger` на `ILogService LogService`. Во всех местах где создаётся `TextBlock` и добавляется в `logger.Items` — заменить на `LogService.Info(...)` / `LogService.Error(...)`.

- [ ] **Шаг 6.5: Заменить WpfPlot на IPlotService в ContourVM и RCFiberRegionVM**

Убрать `public WpfPlot SP` из ViewModel. Заменить прямые вызовы ScottPlot на `IPlotService`. View (code-behind) создаёт `WpfPlotService` и подключает к ViewModel.

- [ ] **Шаг 6.6: Заменить OpenFileDialog/SaveFileDialog на IFileDialogService**

Во всех ViewModel, где используются `OpenFileDialog`/`SaveFileDialog` — заменить на `IFileDialogService.OpenFile()` / `IFileDialogService.SaveFile()`.

- [ ] **Шаг 6.7: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`

- [ ] **Шаг 6.8: Коммит**

```bash
git add OpenCS/Services/ OpenCS/AppViewModel.cs OpenCS/ContourVM.cs OpenCS/RCFiberRegionVM.cs OpenCS/FromDxfVM.cs OpenCS/MaterialVM.cs OpenCS/RebarsVM.cs
git commit -m "refactor: MVVM — ILogService, IPlotService, IFileDialogService, убрать WPF-зависимости из ViewModel"
```

---

## Задача 7: MVVM-рефакторинг — навигация и конвертеры

**Файлы:**
- Modify: `OpenCS/AppViewModel.cs`
- Modify: `OpenCS/App.xaml`
- Modify: `OpenCS/MainWindow.xaml`
- Modify: `OpenCS/Converters.cs`
- Modify: `OpenCS/DataSourceVM.cs`

- [ ] **Шаг 7.1: Заменить UserControl CurrentPage на DataTemplate-навигацию**

В `AppViewModel.cs` заменить `UserControl CurrentPage` на `object CurrentViewModel`. В `App.xaml` добавить `DataTemplate` для каждого типа ViewModel, привязанный к соответствующему View. В `MainWindow.xaml` заменить `<ContentControl Content="{Binding CurrentPage}">` на `<ContentControl Content="{Binding CurrentViewModel}">`.

- [ ] **Шаг 7.2: Исправить Converters — CultureInfo.InvariantCulture**

В `OpenCS/Converters.cs` добавить `using System.Globalization;` и заменить все `double.Parse((string)value)` на `double.Parse((string)value, CultureInfo.InvariantCulture)`. Аналогично для `ConvertBack`.

- [ ] **Шаг 7.3: Исправить DataSourceVM — мутация MaterialChars и кириллический символ**

В `DataSourceVM.cs` заменить `c[i].E *= kE` на клонирование перед модификацией. Заменить переменную `fileС` (кириллическая С) на `fileC`.

- [ ] **Шаг 7.4: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`

- [ ] **Шаг 7.5: Коммит**

```bash
git add OpenCS/
git commit -m "refactor: DataTemplate-навигация, InvariantCulture в конвертерах, клонирование MaterialChars, fileC латиница"
```

---

## Задача 8: Удаление CSdb и мёртвого кода

**Файлы:**
- Delete: `CSdb/` (весь каталог)
- Delete: `OpenCS/ViewModels/XYDB.cs`
- Delete: пустые стабы XAML (если есть)

- [ ] **Шаг 8.1: Удалить каталог CSdb**

Удалить `CSdb/` каталог целиком. Проверить, что нет ссылок на CSdb в `OpenCS.sln` (его там нет, но проверить).

- [ ] **Шаг 8.2: Удалить XYDB.cs**

Удалить `OpenCS/ViewModels/XYDB.cs`. Проверить отсутствие ссылок на `XYDB` в проекте.

- [ ] **Шаг 8.3: Проверить компиляцию**

Run: `dotnet build OpenCS.sln`

- [ ] **Шаг 8.4: Коммит**

```bash
git add -A
git commit -m "chore: удалить CSdb (дубликат OpenCS), удалить XYDB.cs (мёртвый код)"
```

---

## Задача 9: XAML-дедупликация

**Файлы:**
- Modify: `OpenCS/Views/MaterialPage.xaml`
- Modify: `OpenCS/Views/RCFiberRegionPage.xaml`
- Modify: `OpenCS/Views/RCFiberRegionView.xaml`
- Create: `OpenCS/Views/GeoPropsView.xaml` + `.xaml.cs`

- [ ] **Шаг 9.1: Вынести GeoProps в общий UserControl**

Создать `OpenCS/Views/GeoPropsView.xaml` с `DependencyProperty` для привязки `GeoProps`. Заменить 80+ строк дублирующегося XAML в `RCFiberRegionPage.xaml` и `RCFiberRegionView.xaml` на `<views:GeoPropsView GeoProps="{Binding Props}"/>`.

- [ ] **Шаг 9.2: Упростить MaterialPage.xaml через ItemsControl**

Заменить 4 колонки (C, CL, N, NL) с 17 строками одинаковых DockPanel+TextBox на `ItemsControl` с `DataTemplate`, привязанный к коллекции `MaterialCharsVM`.

- [ ] **Шаг 9.3: Скомпилировать и проверить**

Run: `dotnet build OpenCS.sln`

- [ ] **Шаг 9.4: Коммит**

```bash
git add OpenCS/Views/
git commit -m "refactor: GeoPropsView UserControl, MaterialPage ItemsControl, убрать XAML-дублирование"
```

---

## Задача 10: XML-документация CSmath

**Файлы:**
- Modify: все файлы `CSmath/*.cs`

- [ ] **Шаг 10.1: Добавить XML doc comments в Vector.cs**

Для каждого публичного члена: класс, свойства, методы, операторы. На русском языке. Особенное внимание к операторам `*`, `+`, `-` — пояснить семантику (покоординатное умножение, сложение со скаляром).

- [ ] **Шаг 10.2: Добавить XML doc comments в Vector2D.cs, Vector3D.cs**

Пояснить, что `operator ^` — векторное произведение (Cross), а `operator *` — покоординатное (Hadamard). Описать `Norma` как длину вектора.

- [ ] **Шаг 10.3: Добавить XML doc comments в Matrix.cs**

Для каждого публичного метода: `Determinant`, `Inverse`, `Transpose`, `JacR`, `JacB`, `JacC`, `Multiply`, `Augment`, `Stack`. Пояснить, что `operator *` — покоординатное (Hadamard), а матричное — `Multiply`.

- [ ] **Шаг 10.4: Добавить XML doc comments в Plane.cs, Line2d.cs, Range.cs, ISpline.cs, все сплайны**

Описать: плоскость деформаций, пересечение отрезков, диапазон, сплайны. Пояснить `Kurvs` как кривизну (curvature).

- [ ] **Шаг 10.5: Коммит**

```bash
git add CSmath/
git commit -m "docs: XML-документация CSmath на русском языке"
```

---

## Задача 11: XML-документация CScore

**Файлы:**
- Modify: все файлы `CScore/*.cs`

- [ ] **Шаг 11.1: Документировать доменные типы**

`XY.cs`, `StressPoint.cs`, `CircleP.cs`, `Contour.cs`, `Region.cs`, `Fiber.cs`, `FiberRegion.cs`, `RCFiberRegion.cs`, `ReBar.cs`, `ReBarLayer.cs`, `ReBarGroup.cs`, `Load.cs`, `LoadGroup.cs`, `Kurvature.cs`, `Basis.cs`, `Out.cs`, `Boundary.cs`.

Особое внимание: типы материалов (`MatType`), типы расчётов (`CalcType`), типы диаграмм (`DiagrammType`), метод волокон (`FiberRegion.Integral`), плоскость деформаций.

- [ ] **Шаг 11.2: Документировать вычислительные классы**

`Diagramm.cs`, `Geo.cs`, `GeoProps.cs`, `Material.cs`, `MaterialChars.cs`, `SP63.cs`, `ConcreteProps.cs`, `FiberRegionData.cs`.

- [ ] **Шаг 11.3: Коммит**

```bash
git add CScore/
git commit -m "docs: XML-документация CScore на русском языке"
```

---

## Задача 12: XML-документация OpenCS

**Файлы:**
- Modify: ключевые файлы `OpenCS/*.cs`, `OpenCS/ViewModels/*.cs`, `OpenCS/Services/*.cs`, `OpenCS/Utilites/*.cs`

- [ ] **Шаг 12.1: Документировать ViewModel и сервисы**

`AppViewModel.cs`, `ContourVM.cs`, `RCFiberRegionVM.cs`, `DataSourceVM.cs`, `FromDxfVM.cs`, `MaterialVM.cs`, `RebarsVM.cs`, все файлы `Services/`.

- [ ] **Шаг 12.2: Документировать конвертеры и утилиты**

`Converters.cs`, `Renumberer.cs`, `Drawer.cs`, `ApplicationContext.cs`.

- [ ] **Шаг 12.3: Коммит**

```bash
git add OpenCS/
git commit -m "docs: XML-документация OpenCS на русском языке"
```

---

## Задача 13: Обновление CLAUDE.md

- [ ] **Шаг 13.1: Обновить CLAUDE.md**

Обновить `C:\Users\palex\devel\OpenCS\CLAUDE.md` с учётом всех изменений: удаление CSdb, новые сервисы (ILogService, IPlotService, IFileDialogService), исправленные баги, переименования (JacC, IsConverge, Contains, SetStress).

- [ ] **Шаг 13.2: Финальная компиляция**

Run: `dotnet build OpenCS.sln`
Ожидание: успешная компиляция без ошибок и предупреждений.

- [ ] **Шаг 13.3: Финальный коммит**

```bash
git add CLAUDE.md
git commit -m "docs: обновление CLAUDE.md после рефакторинга"
```