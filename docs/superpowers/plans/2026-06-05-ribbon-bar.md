# RibbonBar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить оба ToolBarTray в MainWindow кастомной лентой на базе WPF TabControl без новых зависимостей.

**Architecture:** Новый `UserControl` `RibbonBar` содержит стилизованный `TabControl` с одной вкладкой «Главная» и четырьмя группами кнопок. `DataContext` наследуется от `MainWindow` (то есть `AppViewModel`). Из `MainWindow.xaml` удаляются оба `ToolBarTray` и колонка 3 сетки.

**Tech Stack:** WPF, .NET 9.0, MVVM (RelayCommand / ICommand), DynamicResource-локализация

---

## Файлы

| Файл | Действие |
|------|----------|
| `OpenCS/Resources/Strings.ru-RU.xaml` | Добавить 13 ключей |
| `OpenCS/Resources/Strings.en-US.xaml` | Добавить 13 ключей |
| `OpenCS/App.xaml` | Добавить стиль `RibbonButton` |
| `OpenCS/Views/RibbonBar.xaml` | Создать |
| `OpenCS/Views/RibbonBar.xaml.cs` | Создать |
| `OpenCS/MainWindow.xaml` | Удалить тулбары и колонку 3, добавить RibbonBar |

---

## Task 1: Локализация — добавить ключи в оба словаря

**Files:**
- Modify: `OpenCS/Resources/Strings.ru-RU.xaml`
- Modify: `OpenCS/Resources/Strings.en-US.xaml`

> Проект не имеет тестов. Верификация каждой задачи — `dotnet build OpenCS.sln`.

- [ ] **Шаг 1.1: Добавить ключи в Strings.ru-RU.xaml**

Вставить перед `</ResourceDictionary>` (последняя строка файла):

```xml
   <!-- RibbonBar -->
   <system:String x:Key="RibbonHome">Главная</system:String>
   <system:String x:Key="RibbonGroupGeometry">Геометрия</system:String>
   <system:String x:Key="RibbonGroupMaterials">Материалы</system:String>
   <system:String x:Key="RibbonGroupRegions">Регионы</system:String>
   <system:String x:Key="RibbonGroupSettings">Настройки</system:String>
   <system:String x:Key="TipNewContour">Новый контур</system:String>
   <system:String x:Key="TipDelContour">Удалить контур</system:String>
   <system:String x:Key="TipNewMaterial">Новый материал</system:String>
   <system:String x:Key="TipDelMaterial">Удалить материал</system:String>
   <system:String x:Key="TipNewRCRegion">Новая армированная область</system:String>
   <system:String x:Key="TipDelRCRegion">Удалить армированную область</system:String>
   <system:String x:Key="TipPlotSettings">Настройки графиков</system:String>
   <system:String x:Key="TipCsvSettings">Настройки экспорта CSV</system:String>
```

- [ ] **Шаг 1.2: Добавить ключи в Strings.en-US.xaml**

Вставить перед `</ResourceDictionary>`:

```xml
   <!-- RibbonBar -->
   <system:String x:Key="RibbonHome">Home</system:String>
   <system:String x:Key="RibbonGroupGeometry">Geometry</system:String>
   <system:String x:Key="RibbonGroupMaterials">Materials</system:String>
   <system:String x:Key="RibbonGroupRegions">Regions</system:String>
   <system:String x:Key="RibbonGroupSettings">Settings</system:String>
   <system:String x:Key="TipNewContour">New contour</system:String>
   <system:String x:Key="TipDelContour">Delete contour</system:String>
   <system:String x:Key="TipNewMaterial">New material</system:String>
   <system:String x:Key="TipDelMaterial">Delete material</system:String>
   <system:String x:Key="TipNewRCRegion">New RC region</system:String>
   <system:String x:Key="TipDelRCRegion">Delete RC region</system:String>
   <system:String x:Key="TipPlotSettings">Plot settings</system:String>
   <system:String x:Key="TipCsvSettings">CSV export settings</system:String>
```

- [ ] **Шаг 1.3: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: `Build succeeded` без ошибок.

- [ ] **Шаг 1.4: Коммит**

```
git add OpenCS/Resources/Strings.ru-RU.xaml OpenCS/Resources/Strings.en-US.xaml
git commit -m "feat: add ribbon bar localization keys"
```

---

## Task 2: Стиль RibbonButton в App.xaml

**Files:**
- Modify: `OpenCS/App.xaml`

Стиль `IconButton` — 22×22, `IconButton25` — 25×25. Для ленты нужны кнопки 38×38.

- [ ] **Шаг 2.1: Добавить стиль RibbonButton в App.xaml**

Найти в `App.xaml` блок после стиля `IconButton25` (примерно строка 144):

```xml
         <Style x:Key="IconButton25" TargetType="Button">
            <Setter Property="Height" Value="25"/>
            <Setter Property="Width" Value="25"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Background" Value="Transparent"/>
         </Style>
```

Вставить сразу после него:

```xml
         <Style x:Key="RibbonButton" TargetType="Button">
            <Setter Property="Height" Value="38"/>
            <Setter Property="Width" Value="38"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Margin" Value="2,0"/>
         </Style>
```

- [ ] **Шаг 2.2: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: `Build succeeded`.

- [ ] **Шаг 2.3: Коммит**

```
git add OpenCS/App.xaml
git commit -m "feat: add RibbonButton style (38x38)"
```

---

## Task 3: Создать UserControl RibbonBar

**Files:**
- Create: `OpenCS/Views/RibbonBar.xaml`
- Create: `OpenCS/Views/RibbonBar.xaml.cs`

- [ ] **Шаг 3.1: Создать RibbonBar.xaml.cs**

```csharp
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class RibbonBar : UserControl
    {
        public RibbonBar()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Шаг 3.2: Создать RibbonBar.xaml**

```xml
<UserControl x:Class="OpenCS.Views.RibbonBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
   <TabControl BorderThickness="0,0,0,1" BorderBrush="#CCCCCC" Background="#F5F5F5" Padding="0">
      <TabItem Header="{DynamicResource RibbonHome}">
         <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="4,2">

            <!-- Геометрия -->
            <Border BorderBrush="#CCCCCC" BorderThickness="0,0,1,0" Padding="4,0,8,0">
               <StackPanel>
                  <StackPanel Orientation="Horizontal">
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding FromDxfCommand}"
                             ToolTip="{DynamicResource FromDxf}">
                        <Image Source="{StaticResource di_dxf_file_xaml}"/>
                     </Button>
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding NewContourCommand}"
                             ToolTip="{DynamicResource TipNewContour}">
                        <Image Source="{StaticResource addDrawingImage}"/>
                     </Button>
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding DelContourCommand}"
                             ToolTip="{DynamicResource TipDelContour}">
                        <Image Source="{StaticResource deleteDrawingImage}"/>
                     </Button>
                  </StackPanel>
                  <TextBlock Text="{DynamicResource RibbonGroupGeometry}"
                             FontSize="10" Foreground="Gray" HorizontalAlignment="Center"/>
               </StackPanel>
            </Border>

            <!-- Материалы -->
            <Border BorderBrush="#CCCCCC" BorderThickness="0,0,1,0" Padding="4,0,8,0">
               <StackPanel>
                  <StackPanel Orientation="Horizontal">
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding NewMaterialCommand}"
                             ToolTip="{DynamicResource TipNewMaterial}">
                        <Image Source="/Images/add__32.png"/>
                     </Button>
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding DelMaterialCommand}"
                             ToolTip="{DynamicResource TipDelMaterial}">
                        <Image Source="/Images/delete--32.png"/>
                     </Button>
                  </StackPanel>
                  <TextBlock Text="{DynamicResource RibbonGroupMaterials}"
                             FontSize="10" Foreground="Gray" HorizontalAlignment="Center"/>
               </StackPanel>
            </Border>

            <!-- Регионы -->
            <Border BorderBrush="#CCCCCC" BorderThickness="0,0,1,0" Padding="4,0,8,0">
               <StackPanel>
                  <StackPanel Orientation="Horizontal">
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding NewRCFiberRegionCommand}"
                             ToolTip="{DynamicResource TipNewRCRegion}">
                        <Image Source="/Images/add__32.png"/>
                     </Button>
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding DeleteRCFiberRegionCommand}"
                             ToolTip="{DynamicResource TipDelRCRegion}">
                        <Image Source="/Images/delete__32.png"/>
                     </Button>
                  </StackPanel>
                  <TextBlock Text="{DynamicResource RibbonGroupRegions}"
                             FontSize="10" Foreground="Gray" HorizontalAlignment="Center"/>
               </StackPanel>
            </Border>

            <!-- Настройки -->
            <Border Padding="4,0,8,0">
               <StackPanel>
                  <StackPanel Orientation="Horizontal">
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding OpenPlotSettingsCommand}"
                             ToolTip="{DynamicResource TipPlotSettings}">
                        <Image Source="/Images/diagramma--32.png"/>
                     </Button>
                     <Button Style="{StaticResource RibbonButton}"
                             Command="{Binding OpenCsvSettingsCommand}"
                             ToolTip="{DynamicResource TipCsvSettings}">
                        <Image Source="/Images/diagramma--32.png"/>
                     </Button>
                  </StackPanel>
                  <TextBlock Text="{DynamicResource RibbonGroupSettings}"
                             FontSize="10" Foreground="Gray" HorizontalAlignment="Center"/>
               </StackPanel>
            </Border>

         </StackPanel>
      </TabItem>
   </TabControl>
</UserControl>
```

- [ ] **Шаг 3.3: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: `Build succeeded`. Если ошибка «The name 'RibbonBar' does not exist» — убедитесь, что оба файла лежат в `OpenCS/Views/` и namespace совпадает.

- [ ] **Шаг 3.4: Коммит**

```
git add OpenCS/Views/RibbonBar.xaml OpenCS/Views/RibbonBar.xaml.cs
git commit -m "feat: add RibbonBar UserControl"
```

---

## Task 4: Обновить MainWindow.xaml

**Files:**
- Modify: `OpenCS/MainWindow.xaml`

Изменения в сетке: убрать оба ToolBarTray, убрать колонку 3, добавить RibbonBar. Обновить ColumnSpan у Menu и StatusBar.

- [ ] **Шаг 4.1: Удалить ColumnDefinition колонки 3**

Найти:
```xml
      <Grid.ColumnDefinitions>
         <ColumnDefinition  Width="0.4*"></ColumnDefinition>
         <ColumnDefinition Width="Auto"></ColumnDefinition>
         <ColumnDefinition Width="*"></ColumnDefinition>
         <ColumnDefinition Width="Auto"></ColumnDefinition>
      </Grid.ColumnDefinitions>
```

Заменить на:
```xml
      <Grid.ColumnDefinitions>
         <ColumnDefinition Width="0.4*"></ColumnDefinition>
         <ColumnDefinition Width="Auto"></ColumnDefinition>
         <ColumnDefinition Width="*"></ColumnDefinition>
      </Grid.ColumnDefinitions>
```

- [ ] **Шаг 4.2: Исправить ColumnSpan у Menu**

Найти:
```xml
      <Menu Grid.Column="0" Grid.Row="0" Grid.ColumnSpan="4" FontSize="14" >
```

Заменить на:
```xml
      <Menu Grid.Column="0" Grid.Row="0" Grid.ColumnSpan="3" FontSize="14" >
```

- [ ] **Шаг 4.3: Удалить горизонтальный ToolBarTray**

Найти и удалить весь блок:
```xml
      <ToolBarTray Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3">
         <ToolBar Height="40" >
            <ToggleButton>
            </ToggleButton>
            <Separator />
            <Button Command="{Binding FromDxfCommand}" Width="35">
               <Image Source="{StaticResource di_dxf_file_xaml}"/>
            </Button>
            <Separator />
            <Button>
            </Button>
            <Separator />
            <Button>
            </Button>
            <TextBox Foreground="LightGray" Width="100" Text="{DynamicResource Search}"/>
         </ToolBar>
      </ToolBarTray>
```

- [ ] **Шаг 4.4: Удалить вертикальный ToolBarTray**

Найти и удалить весь блок:
```xml
      <ToolBarTray Grid.Row="1" Grid.Column="3" Grid.RowSpan="4" Orientation="Vertical">
         <ToolBar Width="25" >
            <ToggleButton>
            </ToggleButton>
            <Separator />
            <Button>
            </Button>
            <Separator />
            <Button>
            </Button>
            <Separator />
            <Button>
            </Button>
            <TextBox Foreground="LightGray" Width="100" Text="{DynamicResource Search}"/>
         </ToolBar>
      </ToolBarTray>
```

- [ ] **Шаг 4.5: Добавить namespace local в Window и вставить RibbonBar**

Убедиться, что в теге `<Window ...>` есть `xmlns:local="clr-namespace:OpenCS"` (уже есть в оригинале).

Вставить после закрывающего тега `</Grid.ColumnDefinitions>` и перед тегом `<GridSplitter ...>`:

```xml
      <local:RibbonBar Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                        DataContext="{Binding}"/>
```

- [ ] **Шаг 4.6: Исправить ColumnSpan у StatusBar**

Найти:
```xml
      <StatusBar Height="25" Grid.Column="0" Grid.Row="5" Grid.ColumnSpan="4" >
```

Заменить на:
```xml
      <StatusBar Height="25" Grid.Column="0" Grid.Row="5" Grid.ColumnSpan="3" >
```

- [ ] **Шаг 4.7: Сборка**

```
dotnet build OpenCS.sln
```

Ожидается: `Build succeeded`. Частые ошибки и решения:
- `The resource "RibbonButton" could not be resolved` — стиль не добавлен в App.xaml (Task 2)
- `The resource "RibbonHome" could not be resolved` — ключи не добавлены (Task 1)
- `The name "RibbonBar" does not exist in the namespace` — файл не в папке `Views/` или namespace неверный

- [ ] **Шаг 4.8: Коммит**

```
git add OpenCS/MainWindow.xaml
git commit -m "feat: replace toolbars with RibbonBar in MainWindow"
```

---

## Task 5: Финальная проверка запуском

**Files:** без изменений кода

- [ ] **Шаг 5.1: Запустить приложение**

```
dotnet run --project OpenCS
```

Проверить визуально:
- Лента отображается в строке 1 под меню
- Видна вкладка «Главная» / «Home» (зависит от текущего языка)
- Четыре группы с кнопками и подписями: Геометрия / Материалы / Регионы / Настройки
- Ни одного ToolBarTray не осталось
- ToolTip появляется при наведении на кнопку
- Кнопка «Из DXF» (первая в группе Геометрия) открывает страницу импорта DXF
- Кнопки «Настройки» открывают соответствующие диалоги

- [ ] **Шаг 5.2: Финальный коммит (если нужны правки после проверки)**

```
git add -p
git commit -m "fix: ribbon bar visual adjustments"
```
