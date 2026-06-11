# DXF Import — MaterialArea Wizard Design Spec

**Date:** 2026-06-11
**Project:** OpenCS
**Status:** Approved

---

## Overview

Адаптировать существующий мастер импорта DXF (`FromDxfPage` / `FromDxfVM` / `DxfInteractiveView`)
для создания `MaterialArea` объектов (Region, RebarGroup, SingleBar) вместо сырых Contour/CircleP.
Один сеанс импорта → один основной объект (Region или RebarGroup/SingleBar) плюс связанные дочерние.
Материал назначается позже на странице редактирования.

---

## Scope Changes

| Файл | Изменение |
|---|---|
| `OpenCS/ViewModels/DxfPrimitive.cs` | Добавить `DxfRole` enum + свойство `Role` |
| `OpenCS/ViewModels/FromDxfVM.cs` | Новая логика ролей, дискретизация, SaveMaterialArea |
| `OpenCS/Views/FromDxfPage.xaml` | Тулбар с 4 режимами, новая правая панель |
| `OpenCS/Views/FromDxfPage.xaml.cs` | Минимальные изменения code-behind |
| `OpenCS/Views/DxfInteractiveView.xaml.cs` | Цвет по роли вместо цвета по выделению |
| `OpenCS/Views/MainWindow.xaml` | Context menu «Из DXF...» для узла МатОбластей |
| `OpenCS/AppViewModel.cs` | Команда `OpenMaterialAreaFromDxfCommand` |
| `OpenCS/Resources/Strings.*.xaml` | Новые строковые ключи |

Существующие `SaveContoursCommand` / `SaveCirclesCommand` — **убрать**
(мастер теперь работает только через «Создать область»).

---

## 1. DxfPrimitive — роли

```csharp
public enum DxfRole { None, Hull, Hole, RebarGroup, SingleBar }

public class DxfPrimitive
{
    // ... существующие поля ...
    public DxfRole Role { get; set; } = DxfRole.None;
}
```

**Цвет по роли** (используется в `DxfInteractiveView`):

| Role | Цвет обводки |
|---|---|
| None | цвет слоя (как сейчас) |
| Hull | #4CAF50 (зелёный) |
| Hole | #F44336 (красный) |
| RebarGroup | #FF9800 (оранжевый) |
| SingleBar | #FFC107 (жёлтый) |

---

## 2. FromDxfVM — режим выбора

```csharp
public DxfRole SelectMode { get; set; } = DxfRole.Hull;
```

**Клик по объекту на канвасе:**
- Если у объекта уже та же роль → сбросить в `None`
- Иначе → присвоить `SelectMode`
- Hull допускает только один объект: если уже есть Hull → предыдущий сбрасывается в `None`

**Вычисляемые коллекции:**
```csharp
DxfPrimitive?  HullPrimitive     => _primitives.FirstOrDefault(p => p.Role == DxfRole.Hull);
IList<DxfPrimitive> HolePrimitives     => _primitives.Where(p => p.Role == DxfRole.Hole)...
IList<DxfPrimitive> GroupBarPrimitives => _primitives.Where(p => p.Role == DxfRole.RebarGroup)...
IList<DxfPrimitive> SingleBarPrimitives=> _primitives.Where(p => p.Role == DxfRole.SingleBar)...
```

---

## 3. Правая панель (200 px)

```
┌─ Hull ──────────────────────────┐
│  poly_3  [×]                   │
└─────────────────────────────────┘
┌─ Отверстия ─────────────────────┐
│  (пусто)                        │
└─────────────────────────────────┘
┌─ Группа арматуры ───────────────┐
│  circ_1  ⌀12   [×]            │
│  circ_2  ⌀12   [×]            │
└─────────────────────────────────┘
┌─ Стержни ───────────────────────┐
│  circ_5  ⌀10   [×]            │
└─────────────────────────────────┘
── Дискретизация кругов ──────────
Метод: [Длина хорды ▼]
Знач.:  [0.020]
─────────────────────────────────
Тег: [_________]
[Создать область]
```

Кнопка `[×]` у каждого элемента сбрасывает его роль в `None`.

---

## 4. Тулбар

```
[📁]  [● Hull] [○ Отв.] [○ Группа] [○ Стержень]   Ед.: [мм ▼]
```

4 `RadioButton` в группе `SelectMode`. Кнопки взаимоисключающие.
Поле «Набор» (старый `GeometrySetName`) — убирается; тег указывается в правой панели.

---

## 5. Дискретизация окружностей

Применяется только если `Circle` назначена как `Hull` или `Hole`.
`RebarGroup` и `SingleBar` окружности остаются как `CircleP` → point fiber.

```csharp
public enum CircleDiscretizeMethod { ChordLength, SegmentCount }

static Contour DiscretizeCircle(double cx, double cy, double r,
    CircleDiscretizeMethod method, double value, bool ccw)
{
    int n = method == CircleDiscretizeMethod.ChordLength
        ? Math.Max(3, (int)Math.Ceiling(2 * Math.PI * r / value))
        : Math.Max(3, (int)value);

    double step = 2 * Math.PI / n;
    // ccw=true → θ возрастает (Hull); ccw=false → θ убывает (Hole)
    double dir = ccw ? 1.0 : -1.0;
    var pts = Enumerable.Range(0, n)
        .Select(i => new StressPoint(
            cx + r * Math.Cos(dir * i * step),
            cy + r * Math.Sin(dir * i * step)))
        .ToList();
    return new Contour(pts) { Type = ccw ? ContourType.Hull : ContourType.Hole };
}
```

**Для полилиний-отверстий** — при сохранении проверять ориентацию:

```csharp
static double SignedArea(IList<StressPoint> pts) { /* формула Гаусса */ }
// Если SignedArea > 0 (CCW) → реверсировать точки перед записью в Hole-контур.
```

**UI параметры:**

| Параметр | Тип | По умолчанию |
|---|---|---|
| Метод | ComboBox (2 пункта) | ChordLength |
| Значение | TextBox (double) | 0.020 (м) |

---

## 6. Логика SaveMaterialAreaCommand

```
Валидация: Hull ИЛИ GroupBars ИЛИ SingleBars должны быть назначены.

if HullPrimitive != null:
    region = new MaterialArea { Category=Region, Tag=Tag }
    region.Contours.Add( ToHullContour(HullPrimitive) )  // CCW
    foreach hole in HolePrimitives:
        region.Contours.Add( ToHoleContour(hole) )       // CW — реверс если нужно
    db.SaveMaterialArea(region)                          // → region.Id

    if GroupBarPrimitives.Any():
        group = new MaterialArea { Category=RebarGroup, HostAreaId=region.Id, Tag=Tag+"_г" }
        foreach bar in GroupBarPrimitives:
            group.Fibers.Add( Fiber.CreatePoint(bar.Radius*2*scale, bar.CenterX*scale, bar.CenterY*scale) )
        db.SaveMaterialArea(group)
        db.SavePointFibers(group)

    foreach bar in SingleBarPrimitives:
        sbar = new MaterialArea { Category=SingleBar, HostAreaId=region.Id, Tag=Tag+"_с" }
        sbar.Fibers.Add( Fiber.CreatePoint(bar.Radius*2*scale, bar.CenterX*scale, bar.CenterY*scale) )
        db.SaveMaterialArea(sbar)
        db.SavePointFibers(sbar)

else if GroupBarPrimitives.Any():
    group = new MaterialArea { Category=RebarGroup, Tag=Tag }
    foreach bar in GroupBarPrimitives:
        group.Fibers.Add( Fiber.CreatePoint(bar.Radius*2*scale, bar.CenterX*scale, bar.CenterY*scale) )
    db.SaveMaterialArea(group)
    db.SavePointFibers(group)

else foreach bar in SingleBarPrimitives:
    sbar = new MaterialArea { Category=SingleBar, Tag=Tag }
    sbar.Fibers.Add( Fiber.CreatePoint(bar.Radius*2*scale, bar.CenterX*scale, bar.CenterY*scale) )
    db.SaveMaterialArea(sbar)
    db.SavePointFibers(sbar)

AppViewModel.MaterialAreasRenumber()
AppViewModel.RefreshMaterialAreaTree()
```

---

## 7. Контекст-меню дерева (MainWindow.xaml)

К узлу «Материальные области» (TreeViewItem или ContextMenu) добавить:

```xml
<MenuItem Header="{DynamicResource MenuFromDxfMaterialArea}"
          Command="{Binding OpenMaterialAreaFromDxfCommand}"/>
```

`AppViewModel.OpenMaterialAreaFromDxfCommand` — открывает `FromDxfWindow`
(аналогично существующей команде для контуров/окружностей).

---

## 8. Новые строковые ключи

Добавить в `Strings.ru-RU.xaml` и `Strings.en-US.xaml`:

| Ключ | ru-RU | en-US |
|---|---|---|
| `DxfModeHull` | Hull | Hull |
| `DxfModeHole` | Отверстие | Hole |
| `DxfModeRebarGroup` | Группа арм. | Rebar Group |
| `DxfModeSingleBar` | Стержень | Single Bar |
| `DxfRightHull` | Внешний контур | Outer contour |
| `DxfRightHoles` | Отверстия | Holes |
| `DxfRightGroup` | Группа арматуры | Rebar group |
| `DxfRightSingle` | Стержни | Single bars |
| `DxfDiscretize` | Дискретизация кругов | Circle discretization |
| `DxfDiscretizeMethod` | Метод | Method |
| `DxfDiscretizeChord` | Длина хорды | Chord length |
| `DxfDiscretizeN` | Число сегментов | Segment count |
| `DxfDiscretizeValue` | Значение | Value |
| `DxfCreateArea` | Создать область | Create area |
| `DxfTagLabel` | Тег | Tag |
| `MenuFromDxfMaterialArea` | Из DXF... | From DXF... |

---

## Ограничения / не входит в скоуп

- Материал не назначается в мастере — только в MaterialAreaPage.
- Мастер не строит WKT/сетку — это делается в MaterialAreaPage.
- Существующий импорт Contour/CircleP (сырые таблицы) — команды убираются из UI, логика парсинга остаётся (переиспользуется).
