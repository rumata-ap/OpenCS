namespace CScore.Import;

/// <summary>Импортёр расчётных сочетаний усилий из бинарного файла SCAD RSU2.</summary>
public static class ScadRsu2Importer
{
    const int HeaderSize = 88;
    const int RecordSize = 1054;
    static readonly byte[] Magic = "SCADRSU2"u8.ToArray();

    // Смещения float64 в записи — стержень
    const int BarN = 532, BarMk = 540, BarMy = 564, BarQz = 572, BarMz = 580, BarQy = 588;

    // Смещения float64 в записи — пластина
    const int ShellNx = 596, ShellNy = 604, ShellTxy = 620;
    const int ShellMx = 644, ShellMy = 652, ShellMxy = 660;
    const int ShellQx = 668, ShellQy = 676;

    static readonly Dictionary<int, string> TypeSuffixes = new()
    {
        { 1,   "C"  },
        { 3,   "CL" },
        { 101, "N"  },
        { 102, "NL" },
    };

    /// <summary>Импортировать РСУ из бинарного файла RSU2.</summary>
    public static ScadRsu2ImportResult ImportFile(string filePath)
    {
        var result = new ScadRsu2ImportResult();

        byte[] data;
        try
        {
            data = File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            result.Error = $"Не удалось прочитать файл: {ex.Message}";
            return result;
        }

        if (data.Length < HeaderSize || !data.AsSpan(0, 8).SequenceEqual(Magic))
        {
            result.Error = "Неверный формат файла: ожидается SCADRSU2";
            return result;
        }

        int nRecords = BitConverter.ToInt32(data, 84);
        int expectedSize = HeaderSize + nRecords * RecordSize;
        if (data.Length < expectedSize)
        {
            result.Error = $"Размер файла ({data.Length}) меньше ожидаемого ({expectedSize}) для {nRecords} записей";
            return result;
        }

        // Автоопределение bar/shell: ненулевое значение @offset 596 первой записи → shell
        bool isShell = Math.Abs(BitConverter.ToDouble(data, HeaderSize + ShellNx)) > 1e-6;
        string kind = isShell ? "shell" : "bar";

        string fileName = Path.GetFileNameWithoutExtension(filePath);

        // Создаём 4 ForceSet (по одному на type_code)
        var sets = new Dictionary<int, ForceSet>();
        foreach (var (typeCode, suffix) in TypeSuffixes)
        {
            sets[typeCode] = new ForceSet
            {
                Kind = kind,
                Tag = $"РСУ_{suffix}_{fileName}",
                SourceType = "scad",
            };
        }

        // Парсинг записей
        for (int i = 0; i < nRecords; i++)
        {
            int baseOff = HeaderSize + i * RecordSize;
            int typeCode = BitConverter.ToInt32(data, baseOff);

            if (!sets.TryGetValue(typeCode, out var fs))
                continue;

            if (isShell)
                fs.ShellItems.Add(ReadShellItem(data, baseOff, i));
            else
                fs.Items.Add(ReadBarItem(data, baseOff, i));
        }

        // Добавляем только непустые наборы
        foreach (var fs in sets.Values)
        {
            if (fs.Items.Count > 0 || fs.ShellItems.Count > 0)
                result.ForceSets.Add(fs);
        }

        if (result.ForceSets.Count == 0)
            result.Error = "Файл не содержит записей усилий";

        return result;
    }

    static LoadItem ReadBarItem(byte[] data, int baseOff, int index)
    {
        const double G = 1000.0;  // Н→кН
        const double sign = -1.0; // инверсия моментов SCAD→OpenCS
        return new LoadItem
        {
            Num   = index + 1,
            Label = FormatLabel(data, baseOff, index),
            N     = BitConverter.ToDouble(data, baseOff + BarN)  / G,
            T     = BitConverter.ToDouble(data, baseOff + BarMk) / G,
            My    = BitConverter.ToDouble(data, baseOff + BarMy) / G * sign,
            Mx    = BitConverter.ToDouble(data, baseOff + BarMz) / G * sign,
            Vx    = BitConverter.ToDouble(data, baseOff + BarQz) / G,
            Vy    = BitConverter.ToDouble(data, baseOff + BarQy) / G,
        };
    }

    static ShellLoadItem ReadShellItem(byte[] data, int baseOff, int index)
    {
        const double G = 1000.0;  // Н→кН
        const double sign = -1.0; // инверсия моментов SCAD→OpenCS
        return new ShellLoadItem
        {
            Num   = index + 1,
            Label = FormatLabel(data, baseOff, index),
            Nx    = BitConverter.ToDouble(data, baseOff + ShellNx)  / G,
            Ny    = BitConverter.ToDouble(data, baseOff + ShellNy)  / G,
            Nxy   = BitConverter.ToDouble(data, baseOff + ShellTxy) / G,
            Mx    = BitConverter.ToDouble(data, baseOff + ShellMx)  / G * sign,
            My    = BitConverter.ToDouble(data, baseOff + ShellMy)  / G * sign,
            Mxy   = BitConverter.ToDouble(data, baseOff + ShellMxy) / G * sign,
            Qx    = BitConverter.ToDouble(data, baseOff + ShellQx)  / G,
            Qy    = BitConverter.ToDouble(data, baseOff + ShellQy)  / G,
        };
    }

    static string FormatLabel(byte[] data, int baseOff, int index)
    {
        int firstSlot = FirstActiveSlot(data, baseOff);
        return firstSlot > 0 ? $"{index + 1}-{firstSlot}" : (index + 1).ToString();
    }

    static int FirstActiveSlot(byte[] data, int baseOff)
    {
        for (int c = 1; c <= 62; c++)
        {
            if (BitConverter.ToInt32(data, baseOff + 20 + c * 8) != 0)
                return c;
        }
        return 0;
    }
}
