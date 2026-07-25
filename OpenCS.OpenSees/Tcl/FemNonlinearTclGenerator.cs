using System.Text;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Генерирует Tcl нелинейного статического расчёта 3D-стержневой схемы (ndm 3, ndf 6):
/// fiber-сечения, forceBeamColumn, по-шаговая история через recorder + явный лог сходимости.</summary>
public sealed class FemNonlinearTclGenerator
{
    /// <summary>Строит текст script.tcl из типизированной нелинейной модели.</summary>
    public string Generate(FemNonlinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Validate();

        var sb = new StringBuilder();
        void L(string s = "") => sb.Append(s).Append('\n');
        string F(double v) => TclNumber.Format(v);

        L("# OpenCS OpenSees нелинейный расчёт FEM-схемы");
        L("# Units: m, N, Pa");
        L("wipe");
        L("model basic -ndm 3 -ndf 6");
        L();

        foreach (var n in model.Nodes)
            L($"node {n.Tag} {F(n.X)} {F(n.Y)} {F(n.Z)}");
        L();

        foreach (var n in model.Nodes)
            L($"fix {n.Tag} {string.Join(' ', n.Fixed.Select(f => f ? 1 : 0))}");
        L();

        // Материалы + fiber-секции, в порядке присвоенных тегов секций
        foreach (var kv in model.Sections.OrderBy(kv => kv.Key))
        {
            int sectionTag = kv.Key;
            var section = kv.Value;
            foreach (var mat in section.Materials)
            {
                string materialCommand = mat.Native switch
                {
                    Concrete01Spec c1 =>
                        $"uniaxialMaterial Concrete01 {mat.Tag} {F(c1.Fpc)} {F(c1.Epsc0)} {F(c1.Fpcu)} {F(c1.EpsU)}",
                    Concrete02Spec c2 =>
                        $"uniaxialMaterial Concrete02 {mat.Tag} {F(c2.Fpc)} {F(c2.Epsc0)} {F(c2.Fpcu)} {F(c2.EpsU)} {F(c2.Lambda)} {F(c2.Ft)} {F(c2.Ets)}",
                    Concrete04Spec c4 when c4.Fct is { } fct && c4.Et is { } et && c4.Beta is { } beta =>
                        $"uniaxialMaterial Concrete04 {mat.Tag} {F(c4.Fc)} {F(c4.Ec0)} {F(c4.Ecu)} {F(c4.Ec)} {F(fct)} {F(et)} {F(beta)}",
                    Concrete04Spec c4 =>
                        $"uniaxialMaterial Concrete04 {mat.Tag} {F(c4.Fc)} {F(c4.Ec0)} {F(c4.Ecu)} {F(c4.Ec)}",
                    Steel01Spec s1 =>
                        $"uniaxialMaterial Steel01 {mat.Tag} {F(s1.Fy)} {F(s1.E0)} {F(s1.B)}",
                    Steel02Spec s2 =>
                        $"uniaxialMaterial Steel02 {mat.Tag} {F(s2.Fy)} {F(s2.E0)} {F(s2.B)} {F(s2.R0)} {F(s2.CR1)} {F(s2.CR2)}",
                    null => BuildElasticMultiLinearCommand(mat, F),
                    _ => throw new InvalidOperationException($"Неизвестный тип NativeMaterialSpec: {mat.Native.GetType().Name}.")
                };
                L(materialCommand);
            }
            L($"section Fiber {sectionTag} -GJ {F(section.GJ)} {{");
            foreach (var fiber in section.Fibers)
                L($"    fiber {F(fiber.Y)} {F(fiber.Z)} {F(fiber.AreaM2)} {fiber.MaterialTag}");
            L("}");
        }
        L();

        // geomTransf по уникальным vecxz
        var transfByVec = new Dictionary<(double, double, double), int>();
        foreach (var e in model.Elements)
            if (!transfByVec.ContainsKey(e.Vecxz))
            {
                int tag = transfByVec.Count + 1;
                transfByVec[e.Vecxz] = tag;
                L($"geomTransf {model.GeomTransfKind} {tag} {F(e.Vecxz.X)} {F(e.Vecxz.Y)} {F(e.Vecxz.Z)}");
            }
        L();

        foreach (var e in model.Elements)
        {
            int t = transfByVec[e.Vecxz];
            L($"element {model.ElementFormulation} {e.Tag} {e.NodeI} {e.NodeJ} {e.NumIntegrationPoints} {e.SectionTag} {t}");
        }
        L();

        L("pattern Plain 1 Linear {");
        foreach (var ld in model.Loads)
            L($"    load {ld.NodeTag} {F(ld.Fx)} {F(ld.Fy)} {F(ld.Fz)} {F(ld.Mx)} {F(ld.My)} {F(ld.Mz)}");
        if (model.DistributedLoads.Count > 0 && model.GeomTransfKind == "Corotational")
            throw new InvalidOperationException("Распределённые нагрузки не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
        foreach (var ld in model.DistributedLoads)
        {
            if (IsFullUniform(ld))
                L($"    eleLoad -ele {ld.ElementTag} -type -beamUniform {F(ld.WyStart)} {F(ld.WzStart)} {F(ld.WxStart)}");
            else
                L($"    eleLoad -ele {ld.ElementTag} -type -beamUniform {F(ld.WyStart)} {F(ld.WzStart)} {F(ld.WxStart)} {F(ld.AOverL)} {F(ld.BOverL)} {F(ld.WyEnd)} {F(ld.WzEnd)} {F(ld.WxEnd)}");
        }
        if (model.PointLoads.Count > 0 && model.GeomTransfKind == "Corotational")
            throw new InvalidOperationException("Сосредоточенные нагрузки внутри элемента не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
        foreach (var ld in model.PointLoads)
            L($"    eleLoad -ele {ld.ElementTag} -type -beamPoint {F(ld.Py)} {F(ld.Pz)} {F(ld.XOverL)} {F(ld.Px)}");
        foreach (var ld in model.KinematicLoads)
            L($"    sp {ld.NodeTag} {ld.Dof} {F(ld.Value)}");
        L("}");
        L();

        L("constraints Transformation");
        L("numberer RCM");
        L("system BandGeneral");
        L($"test {model.ConvergenceTest} {F(model.Tolerance)} {model.MaxIterations} 0");
        L($"algorithm {model.Algorithm}");
        // Интегратор должен быть задан до создания StaticAnalysis.
        L("integrator LoadControl 1.0");
        L("analysis Static");
        L();

        var nodeTags = model.Nodes.Select(n => n.Tag).ToList();
        var restrainedTags = model.Nodes.Where(n => n.Fixed.Any(f => f)).Select(n => n.Tag)
            .Concat(model.KinematicLoads.Select(load => load.NodeTag)).Distinct().ToList();
        var elemTags = model.Elements.Select(e => e.Tag).ToList();

        // ИСТОРИЯ ПОРЧИ ВЫВОДА (три РАЗНЫХ подтверждённых на реальных артефактах паттерна на одном
        // и том же классе бага, каждый раз в файле, который весь расчёт держал один открытый
        // Tcl-канал под ручными eleResponse/nodeDisp-запросами): (1) блок РОВНО 4096 байт (размер
        // буфера канала) нулей ПОСЕРЕДИНЕ иначе корректного числа; (2) уже при -buffering none —
        // блок из нескольких тысяч нулевых байт, заменяющий ЦЕЛУЮ строку; (3) блок нулей ДЛИНОЙ
        // РОВНО В БАЙТАХ РАВНОЙ замещённой строке — верный признак гонки на уровне записи файла в
        // ОС, а не буферизации Tcl-канала. Итоговое решение — не патчить каждый новый паттерн
        // порчи точечно, а убрать сам механизм риска: перейти на штатные OpenSees
        // recorder Node/Element с -closeOnWrite (открывают файл заново на каждой записи вместо
        // удержания хэндла открытым весь расчёт; проверено — поддерживается в этой сборке 3.8.0,
        // и корректно сохраняет заданный порядок узлов/элементов в колонках, а не пересортировывает
        // по тегу). recorder срабатывает автоматически на КАЖДОМ успешном analyze(), в т.ч. на
        // под-шагах адаптивного дробления — это заменяет собой прежний ручной "фикс последнего
        // дробного шага": теперь каждый успешный под-шаг логируется сам по себе, без отдельной
        // ловли частичного прогресса при итоговом отказе.
        L("proc writeCloseOnWrite {filename row} {");
        L("    set ch [open $filename a]");
        L("    puts $ch $row");
        L("    close $ch");
        L("}");
        L($"recorder Node -file nonlinear_node_disp.out -closeOnWrite -time -node {string.Join(' ', nodeTags)} -dof 1 2 3 4 5 6 disp");
        if (restrainedTags.Count > 0)
            L($"recorder Node -file nonlinear_node_reactions.out -closeOnWrite -time -node {string.Join(' ', restrainedTags)} -dof 1 2 3 4 5 6 reaction");
        L($"recorder Element -file nonlinear_element_forces.out -closeOnWrite -time -ele {string.Join(' ', elemTags)} localForce");
        L();

        // recorder_order.json — статический эхо-вывод уже известных на этапе генерации списков тегов,
        // чтобы парсер сопоставлял колонки recorder-матриц без хрупких допущений об их порядке.
        string orderJson = "{\"nodeTags\":[" + string.Join(',', nodeTags) +
            "],\"restrainedTags\":[" + string.Join(',', restrainedTags) +
            "],\"elemTags\":[" + string.Join(',', elemTags) + "]}";
        L("set orderFile [open recorder_order.json w]");
        L("puts $orderFile {" + orderJson + "}");
        L("close $orderFile");
        L();

        // Сохраняем фактические положения точек интегрирования forceBeamColumn.
        // integrationPoints возвращает нормированные координаты от узла I; длина берётся
        // из исходной геометрии элемента.
        var nodeByTag = model.Nodes.ToDictionary(n => n.Tag);
        L("set sectionOrder [open nonlinear_section_order.json w]");
        L("fconfigure $sectionOrder -buffering none");
        L("puts $sectionOrder \"{\\\"locations\\\":\\[\"");
        L("set sectionLocationFirst 1");
        foreach (var e in model.Elements.OrderBy(e => e.Tag))
        {
            if (!nodeByTag.TryGetValue(e.NodeI, out var ni) || !nodeByTag.TryGetValue(e.NodeJ, out var nj))
                throw new InvalidOperationException($"Элемент {e.Tag}: не найдены узлы для вычисления длины.");
            double dx = nj.X - ni.X;
            double dy = nj.Y - ni.Y;
            double dz = nj.Z - ni.Z;
            double length = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length <= 0 || !double.IsFinite(length))
                throw new InvalidOperationException($"Элемент {e.Tag}: длина должна быть конечной и положительной.");

            var section = model.Sections[e.SectionTag];
            L($"set ipLocations_{e.Tag} [eleResponse {e.Tag} integrationPoints]");
            L($"for {{set ip 1}} {{$ip <= {e.NumIntegrationPoints}}} {{incr ip}} {{");
            L($"    set xi [lindex $ipLocations_{e.Tag} [expr {{$ip - 1}}]]");
            // forceBeamColumn.integrationPoints возвращает координату вдоль элемента
            // в метрах, а не безразмерную координату.
            L("    set distance $xi");
            L($"    set relative [expr {{$xi / {F(length)}}}]");
            L("    if {$sectionLocationFirst == 0} { puts -nonewline $sectionOrder {,} }");
            L("    set sectionLocationFirst 0");
            L($"    puts $sectionOrder [format {{    {{\"elementTag\":{e.Tag},\"integrationPoint\":%d,\"sectionTag\":{e.SectionTag},\"fiberCount\":{section.Fibers.Count},\"distanceFromElementStartM\":%.17g,\"elementLengthM\":{F(length)},\"relativePosition\":%.17g}}}} $ip $distance $relative]");
            L("}");
        }
        L("puts $sectionOrder \"]}\"");
        L("close $sectionOrder");
        L();

        L("set fiberStates [open nonlinear_fiber_states.out w]");
        L("fconfigure $fiberStates -buffering none");
        L("puts $fiberStates {# step loadFactor elementTag integrationPoint fiberIndex stressPa strain}");
        L("writeCloseOnWrite step_status.out {# step loadFactor converged isRefinement}");
        L($"set loadFactorStep {F(model.LoadFactorStep)}");
        L($"set maxLoadFactor {F(model.MaxLoadFactor)}");
        L($"set refinementDivisions {model.RefinementDivisions}");
        L($"set maxRefinementDepth {model.MaxRefinementDepth}");
        L("set currentLambda 0.0");
        L("set stepIndex 0");
        L("set analysisFailed 0");
        L();

        // advanceTo — рекурсивное адаптивное дробление шага: неудавшийся интервал
        // [fromLambda, toLambda] делится на refinementDivisions частей и каждая пробуется отдельно
        // (рекурсивно дробясь дальше при новой неудаче), вплоть до maxRefinementDepth. Один уровень
        // дробления часто недостаточен для резкого падения жёсткости (трещинообразование/текучесть)
        // — рекурсия ищет достаточно мелкий шаг адаптивно, оставаясь крупным там, где сходимость не
        // проблема. КАЖДЫЙ успешный analyze() (на любой глубине, в т.ч. под-шаги дробления) сразу
        // получает свою запись в step_status.out И полный обход волокон — осознанный выбор
        // (пользователь предпочитает полноту данных по сечениям экономии времени расчёта): раньше
        // обход волокон выполнялся только на контрольных точках loadFactorStep, чтобы не раздувать
        // объём/время на трудных участках, но тогда для под-шагов дробления (которых в Steps теперь
        // много — в т.ч. часто самый последний, наиболее нагруженный шаг) карта напряжений сечения
        // оказывалась пустой — recordedFibers не находил соответствующей строки в
        // nonlinear_fiber_states.out. Держит step_status/fiberStates/node_disp/reactions/
        // element_forces в точном построчном соответствии — все пишутся на каждом успешном analyze().
        L("proc advanceTo {fromLambda toLambda depth} {");
        L("    global refinementDivisions maxRefinementDepth stepIndex currentLambda fiberStates");
        L("    integrator LoadControl [expr {$toLambda - $fromLambda}]");
        L("    set rc [analyze 1]");
        L("    if {$rc == 0} {");
        L("        incr stepIndex");
        L("        set currentLambda [getTime]");
        L("        writeCloseOnWrite step_status.out [list $stepIndex $currentLambda 1 [expr {$depth > 0}]]");
        EmitFiberStateWrites(L, model);
        L("        return 1");
        L("    }");
        L("    if {$depth >= $maxRefinementDepth} { return 0 }");
        L("    set piece [expr {($toLambda - $fromLambda) / double($refinementDivisions)}]");
        L("    for {set i 0} {$i < $refinementDivisions} {incr i} {");
        L("        set subFrom [expr {$fromLambda + $piece * $i}]");
        L("        set subTo [expr {$fromLambda + $piece * ($i + 1)}]");
        L("        if {![advanceTo $subFrom $subTo [expr {$depth + 1}]]} { return 0 }");
        L("    }");
        L("    return 1");
        L("}");
        L();

        L("while {$currentLambda < $maxLoadFactor - 1.0e-12} {");
        L("    set targetLambda [expr {min($currentLambda + $loadFactorStep, $maxLoadFactor)}]");
        L("    set fromLambda $currentLambda");
        L("    if {![advanceTo $fromLambda $targetLambda 0]} {");
        L("        set currentLambda [getTime]");
        L("        writeCloseOnWrite step_status.out [list [expr {$stepIndex + 1}] $currentLambda 0 1]");
        L("        set analysisFailed 1");
        L("        break");
        L("    }");
        L("}");
        L("close $fiberStates");
        L();

        L("set marker [open completed.marker w]");
        L("puts $marker done");
        L("close $marker");
        L("wipe");

        return sb.ToString();
    }

    /// <summary>Число повторных попыток eleResponse при подозрении на порчу вывода OpenSees
    /// (см. FiberQueryMaxValueLength) прежде чем строка волокна будет пропущена.</summary>
    private const int FiberQueryMaxRetries = 3;

    /// <summary>Максимальная длина каждого значения stressStrain в символах. OpenSees 3.8.0 при
    /// очень большом суммарном числе eleResponse-запросов к волокнам (элементы × точки
    /// интегрирования × волокна) иногда отдаёт вместо пары чисел испорченный ответ — вплоть до
    /// серии нулевых байтов — из-за чего теряется не только эта строка, но и последующие
    /// (см. отладку кинематических нагрузок на неразрезной балке, шаг терялся целиком).
    /// Обычное отформатированное double короче 32 символов; более длинный ответ — верный признак
    /// порчи, а не корректного числа.</summary>
    private const int FiberQueryMaxValueLength = 32;

    static void EmitFiberStateWrites(Action<string> line, FemNonlinearModel model)
    {
        foreach (var e in model.Elements.OrderBy(e => e.Tag))
        {
            var section = model.Sections[e.SectionTag];
            line($"        for {{set ip 1}} {{$ip <= {e.NumIntegrationPoints}}} {{incr ip}} {{");
            line($"            for {{set fiberIndex 0}} {{$fiberIndex < {section.Fibers.Count}}} {{incr fiberIndex}} {{");
            string fiberCoordinates = string.Join(' ', section.Fibers.Select(f => $"{TclNumber.Format(f.Y)} {TclNumber.Format(f.Z)}"));
            string fiberQuery = $"eleResponse {e.Tag} section $ip fiber [lindex {{{fiberCoordinates}}} [expr {{$fiberIndex * 2}}]] [lindex {{{fiberCoordinates}}} [expr {{$fiberIndex * 2 + 1}}]] stressStrain";
            string isValid = $"[llength $stressStrain] == 2 && [string length [lindex $stressStrain 0]] <= {FiberQueryMaxValueLength} && [string length [lindex $stressStrain 1]] <= {FiberQueryMaxValueLength}";
            line($"                set stressStrain [{fiberQuery}]");
            line($"                set fiberQueryAttempt 0");
            line($"                while {{!({isValid}) && $fiberQueryAttempt < {FiberQueryMaxRetries}}} {{");
            line($"                    incr fiberQueryAttempt");
            line($"                    set stressStrain [{fiberQuery}]");
            line("                }");
            line($"                if {{{isValid}}} {{");
            line($"                    puts $fiberStates [list $stepIndex $currentLambda {e.Tag} $ip $fiberIndex [lindex $stressStrain 0] [lindex $stressStrain 1]]");
            line("                } else {");
            line($"                    puts stderr \"WARNING: состояние волокна пропущено (подозрение на порчу вывода OpenSees) elem={e.Tag} ip=$ip fiber=$fiberIndex step=$stepIndex\"");
            line("                }");
            line("            }");
            line("        }");
        }
    }

    static bool IsFullUniform(FemLinearDistributedLoad load) =>
        load.AOverL == 0 && load.BOverL == 1 &&
        load.WyStart == load.WyEnd && load.WzStart == load.WzEnd && load.WxStart == load.WxEnd;

    // Точки: отрицательная огибающая целиком + положительная без первой (общей) точки — тот же
    // приём, что и в SectionMomentCurvatureTclGenerator.
    private static string BuildElasticMultiLinearCommand(OpenSeesMaterialDefinition mat, Func<double, string> f)
    {
        var points = mat.NegativeEnvelope.Concat(mat.PositiveEnvelope.Skip(1)).ToList();
        var strains = string.Join(' ', points.Select(p => f(p.Strain)));
        var stresses = string.Join(' ', points.Select(p => f(p.StressPa)));
        return $"uniaxialMaterial ElasticMultiLinear {mat.Tag} -strain {strains} -stress {stresses}";
    }
}
