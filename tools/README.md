# Генераторы нормативных материалов

`structural-materials` — закреплённый Git submodule с таблицами и расчётными
характеристиками материалов по СП 63 и другим сводам правил. На первом этапе
из него генерируются CSV стальной арматуры СП 63 для OpenCS.

Обновление исходника и генерация выполняются разработчиком. Для Windows
рекомендуется один раз подготовить изолированное окружение:

```powershell
git submodule update --init --recursive
powershell -ExecutionPolicy Bypass -File tools/setup_structural_materials.ps1
tools/.venv/Scripts/python.exe tools/generate_sp63_rebar_csv.py --check
tools/.venv/Scripts/python.exe tools/generate_sp63_rebar_csv.py --write
```

Скрипт требует Python 3.9 или новее и устанавливает зависимости submodule в
`tools/.venv`. Он не подменяет системный Python. Если `py` существует, но
выводит `No installed Python found`, проверьте именно это окружение:

```powershell
Get-Command py.exe
py -0p
Get-ChildItem HKCU:\Software\Python\PythonCore,HKLM:\Software\Python\PythonCore -ErrorAction SilentlyContinue
```

`py.exe` — только launcher; список `py -0p` формируется из зарегистрированных
установок Python и может отличаться между пользователями и оболочками. Установите
Python 3.12 для текущего пользователя (например, `winget install --id
Python.Python.3.12 --exact --scope user`), затем повторите диагностику и setup.

`--check` завершается с кодом 1, если сохранённые CSV отличаются от результата
генерации. Команда `--write` обновляет только четыре файла арматуры СП 63.

Python и NumPy нужны только для повторной генерации проверяемых файлов. Они не
являются зависимостями OpenCS во время сборки или запуска приложения.
