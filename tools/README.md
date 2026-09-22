# Генераторы нормативных материалов

`structural-materials` — закреплённый Git submodule с таблицами и расчётными
характеристиками материалов по СП 63 и другим сводам правил. На первом этапе
из него генерируются CSV стальной арматуры СП 63 для OpenCS.

Обновление исходника и генерация выполняются разработчиком:

```powershell
git submodule update --init --recursive
py -m pip install -e tools/structural-materials
py tools/generate_sp63_rebar_csv.py --check
py tools/generate_sp63_rebar_csv.py --write
```

`--check` завершается с кодом 1, если сохранённые CSV отличаются от результата
генерации. Команда `--write` обновляет только четыре файла арматуры СП 63.

Python и NumPy нужны только для повторной генерации проверяемых файлов. Они не
являются зависимостями OpenCS во время сборки или запуска приложения.
