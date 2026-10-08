using OpenCS.Services;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Пробник формата стальных жёсткостей ЛИРЫ: печатает таблицу жёсткостей открытой в ЛИРЕ схемы
/// (номер, код вида, имя, строка параметров) и число КЭ на жёсткость. OPENCS_LIRA_STIFFNESS_PROBE=1 —
/// запуск; без переменной тест сразу выходит. Нужна запущенная ЛИРА с открытой схемой.
/// </summary>
public class LiraSteelStiffnessProbeManualTests(ITestOutputHelper output)
{
    [Fact]
    public void DumpStiffnesses()
    {
        if (Environment.GetEnvironmentVariable("OPENCS_LIRA_STIFFNESS_PROBE") != "1") return;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var (stiffnesses, byElement, _) = LiraApiSchemaReader.ReadStiffnesses();
                var counts = byElement.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
                foreach (var s in stiffnesses)
                    output.WriteLine($"{s.Id}\tkind={s.KindCode}\tКЭ={counts.GetValueOrDefault(s.Id)}\t«{s.Name}»\t{s.Params}");
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }
}
