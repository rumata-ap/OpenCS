using System.Text;
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
        // stdout при перенаправлении в процесс буферизуется целыми блоками (не построчно) —
        // без этого puts-прогресс шагов доходил бы до живого лога OpenCS только пачками/в конце.
        L("fconfigure stdout -buffering line");
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
            if (section.Elastic is { } elastic)
            {
                L($"section Elastic {sectionTag} {F(elastic.E)} {F(elastic.A)} {F(elastic.Iz)} {F(elastic.Iy)} 1 {F(elastic.GJ)}");
                continue;
            }
            foreach (var mat in section.Materials)
            {
                L(NativeMaterialTclEmitter.ToTcl(mat, F));
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

        L("constraints Transformation");
        L("numberer RCM");
        L("system BandGeneral");
        L($"test {model.Policy.ConvergenceTest} {F(model.Policy.Tolerance)} {model.Policy.MaxIterations} 0");
        L($"algorithm {model.Policy.Algorithm}");
        // Интегратор должен быть задан до создания StaticAnalysis. Паттерны стадий (см. цикл по
        // model.Stages ниже) регистрируются уже после analysis Static — OpenSees подхватывает
        // вновь определённый pattern в уже созданный Static-анализ на следующем analyze().
        L("integrator LoadControl 1.0");
        L("analysis Static");
        L();

        var nodeTags = model.Nodes.Select(n => n.Tag).ToList();
        var restrainedTags = model.Nodes.Where(n => n.Fixed.Any(f => f)).Select(n => n.Tag)
            .Concat(model.Stages.SelectMany(s => s.KinematicLoads).Select(load => load.NodeTag)).Distinct().ToList();
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

        if (model.RecordFiberStates)
        {
            L("set fiberStates [open nonlinear_fiber_states.out w]");
            L("fconfigure $fiberStates -buffering none");
            L("puts $fiberStates {# step loadFactor elementTag integrationPoint fiberIndex stressPa strain}");
        }
        L("writeCloseOnWrite step_status.out {# step stageIndex loadFactor converged isRefinement}");
        L($"set refinementDivisions {model.Policy.RefinementDivisions}");
        L($"set maxRefinementDepth {model.Policy.MaxRefinementDepth}");
        L("set currentLambda 0.0");
        L("set stepIndex 0");
        L("set analysisFailed 0");
        L("set currentStageIndex 0");
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
        L(model.RecordFiberStates
            ? "    global refinementDivisions maxRefinementDepth stepIndex currentLambda currentStageIndex fiberStates"
            : "    global refinementDivisions maxRefinementDepth stepIndex currentLambda currentStageIndex");
        L("    integrator LoadControl [expr {$toLambda - $fromLambda}]");
        L("    set rc [analyze 1]");
        L("    if {$rc == 0} {");
        L("        incr stepIndex");
        L("        set currentLambda [getTime]");
        L("        set iters [testIter]");
        L("        set finalNorm [lindex [testNorm] [expr {$iters - 1}]]");
        L("        puts \"step $stepIndex OK stage=$currentStageIndex lambda=$currentLambda depth=$depth iters=$iters norm=$finalNorm\"");
        L("        writeCloseOnWrite step_status.out [list $stepIndex $currentStageIndex $currentLambda 1 [expr {$depth > 0}]]");
        if (model.RecordFiberStates)
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

        L("set lastPathControlReason \"\"");
        L();
        L("proc advanceDisplacement {nodeTag dof targetDisp initIncr minIncr maxIncr maxSteps} {");
        L(model.RecordFiberStates
            ? "    global stepIndex currentLambda currentStageIndex fiberStates lastPathControlReason"
            : "    global stepIndex currentLambda currentStageIndex lastPathControlReason");
        L("    set dispStart [nodeDisp $nodeTag $dof]");
        L("    if {[expr {abs($targetDisp - $dispStart) < 1e-12}]} {");
        L("        puts \"stage=$currentStageIndex DisplacementControl: target already reached at stage start (disp=$dispStart)\"");
        L("        set lastPathControlReason \"zero_step_target_already_reached\"");
        L("        return 1");
        L("    }");
        L("    set dir [expr {$targetDisp > $dispStart ? 1.0 : -1.0}]");
        L("    set incr [expr {$dir * $initIncr}]");
        // dUmin/dUmax упорядочены dUmin <= dUmax В ПОДПИСАННЫХ единицах — при dir=-1 это
        // -maxIncr/-minIncr, а НЕ dir*minIncr/dir*maxIncr (тот порядок переворачивается).
        L("    set dUmin [expr {$dir > 0 ? $minIncr : -$maxIncr}]");
        L("    set dUmax [expr {$dir > 0 ? $maxIncr : -$minIncr}]");
        L("    integrator DisplacementControl $nodeTag $dof $incr 4 $dUmin $dUmax");
        L("    set steps 0");
        L("    while {($dir > 0 ? [nodeDisp $nodeTag $dof] < $targetDisp : [nodeDisp $nodeTag $dof] > $targetDisp)");
        L("           && $steps < $maxSteps} {");
        L("        set rc [analyze 1]");
        L("        if {$rc == 0} {");
        L("            incr stepIndex");
        L("            incr steps");
        L("            set currentLambda [getTime]");
        L("            writeCloseOnWrite step_status.out [list $stepIndex $currentStageIndex $currentLambda 1 0]");
        if (model.RecordFiberStates) EmitFiberStateWrites(L, model);
        L("            continue");
        L("        }");
        L("        set incr [expr {$incr / 2.0}]");
        L("        if {[expr {abs($incr) < $minIncr}]} {");
        L("            incr stepIndex");
        L("            puts \"step $stepIndex FAILED stage=$currentStageIndex lambda=$currentLambda reason=min_increment_reached\"");
        L("            writeCloseOnWrite step_status.out [list $stepIndex $currentStageIndex $currentLambda 0 1 min_increment_reached]");
        L("            set lastPathControlReason \"failed\"");
        L("            return 0");
        L("        }");
        L("        integrator DisplacementControl $nodeTag $dof $incr 4 $dUmin $dUmax");
        L("    }");
        // Причина проверяется ПОСЛЕ цикла НЕЗАВИСИМО от того, какая часть условия while
        // сработала — target_reached приоритетнее max_steps_reached, если оба истинны
        // одновременно (последний разрешённый шаг попал точно в цель).
        L("    set targetReached [expr {$dir > 0 ? [nodeDisp $nodeTag $dof] >= $targetDisp : [nodeDisp $nodeTag $dof] <= $targetDisp}]");
        L("    if {$targetReached} {");
        L("        set lastPathControlReason \"target_reached\"");
        L("    } else {");
        L("        puts \"stage=$currentStageIndex DisplacementControl reached maxSteps=$maxSteps before target (disp=[nodeDisp $nodeTag $dof])\"");
        L("        set lastPathControlReason \"max_steps_reached\"");
        L("    }");
        L("    return 1");
        L("}");
        L();

        L("proc advanceArcLength {s alpha minS maxSteps} {");
        L(model.RecordFiberStates
            ? "    global stepIndex currentLambda currentStageIndex fiberStates lastPathControlReason"
            : "    global stepIndex currentLambda currentStageIndex lastPathControlReason");
        L("    set curS $s");
        L("    integrator ArcLength $curS $alpha");
        L("    set steps 0");
        L("    while {$steps < $maxSteps} {");
        L("        set rc [analyze 1]");
        L("        if {$rc == 0} {");
        L("            incr stepIndex");
        L("            incr steps");
        L("            set currentLambda [getTime]");
        L("            writeCloseOnWrite step_status.out [list $stepIndex $currentStageIndex $currentLambda 1 [expr {$curS != $s}]]");
        if (model.RecordFiberStates) EmitFiberStateWrites(L, model);
        L("            continue");
        L("        }");
        L("        set curS [expr {$curS / 2.0}]");
        L("        if {[expr {abs($curS) < $minS}]} {");
        L("            incr stepIndex");
        L("            puts \"step $stepIndex FAILED stage=$currentStageIndex lambda=$currentLambda reason=min_arclength_reached\"");
        L("            writeCloseOnWrite step_status.out [list $stepIndex $currentStageIndex $currentLambda 0 1 min_arclength_reached]");
        L("            set lastPathControlReason \"failed\"");
        L("            return 0");
        L("        }");
        L("        integrator ArcLength $curS $alpha");
        L("    }");
        L("    set lastPathControlReason \"max_steps_reached\"");
        L("    return 1");
        L("}");
        L();

        for (int stageIdx = 0; stageIdx < model.Stages.Count; stageIdx++)
        {
            var stage = model.Stages[stageIdx];
            int patternTag = stageIdx + 1;
            bool guarded = stageIdx > 0;
            string indent = guarded ? "    " : "";
            if (guarded)
            {
                // Стадия >0 выполняется только если все предыдущие стадии сошлись; loadConst
                // фиксирует накопленное НДС перед активацией добавочной нагрузки этой стадии.
                L("if {!$analysisFailed} {");
                L($"{indent}loadConst -time 0.0");
            }
            L($"{indent}# --- Стадия {patternTag}: {stage.Tag} ---");
            L($"{indent}set currentStageIndex {stageIdx}");
            L($"{indent}set loadFactorStep {F(stage.LoadFactorStep)}");
            L($"{indent}set maxLoadFactor {F(stage.MaxLoadFactor)}");
            L($"{indent}pattern Plain {patternTag} Linear {{");
            foreach (var ld in stage.Loads)
                L($"{indent}    load {ld.NodeTag} {F(ld.Fx)} {F(ld.Fy)} {F(ld.Fz)} {F(ld.Mx)} {F(ld.My)} {F(ld.Mz)}");
            if (stage.DistributedLoads.Count > 0 && model.GeomTransfKind == "Corotational")
                throw new InvalidOperationException("Распределённые нагрузки не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
            foreach (var ld in stage.DistributedLoads)
            {
                if (IsFullUniform(ld))
                    L($"{indent}    eleLoad -ele {ld.ElementTag} -type -beamUniform {F(ld.WyStart)} {F(ld.WzStart)} {F(ld.WxStart)}");
                else
                    L($"{indent}    eleLoad -ele {ld.ElementTag} -type -beamUniform {F(ld.WyStart)} {F(ld.WzStart)} {F(ld.WxStart)} {F(ld.AOverL)} {F(ld.BOverL)} {F(ld.WyEnd)} {F(ld.WzEnd)} {F(ld.WxEnd)}");
            }
            if (stage.PointLoads.Count > 0 && model.GeomTransfKind == "Corotational")
                throw new InvalidOperationException("Сосредоточенные нагрузки внутри элемента не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
            foreach (var ld in stage.PointLoads)
                L($"{indent}    eleLoad -ele {ld.ElementTag} -type -beamPoint {F(ld.Py)} {F(ld.Pz)} {F(ld.XOverL)} {F(ld.Px)}");
            foreach (var ld in stage.KinematicLoads)
                L($"{indent}    sp {ld.NodeTag} {ld.Dof} {F(ld.Value)}");
            L($"{indent}}}");
            L($"{indent}set currentLambda 0.0");
            L($"{indent}while {{$currentLambda < $maxLoadFactor - 1.0e-12}} {{");
            L($"{indent}    set targetLambda [expr {{min($currentLambda + $loadFactorStep, $maxLoadFactor)}}]");
            L($"{indent}    set fromLambda $currentLambda");
            L($"{indent}    if {{![advanceTo $fromLambda $targetLambda 0]}} {{");
            L($"{indent}        set currentLambda [getTime]");
            L($"{indent}        puts \"step [expr {{$stepIndex + 1}}] FAILED stage=$currentStageIndex lambda=$currentLambda\"");
            L($"{indent}        writeCloseOnWrite step_status.out [list [expr {{$stepIndex + 1}}] $currentStageIndex $currentLambda 0 1]");
            L($"{indent}        set analysisFailed 1");
            L($"{indent}        break");
            L($"{indent}    }}");
            L($"{indent}}}");
            if (guarded) L("}");
            L();
        }
        if (model.RecordFiberStates) L("close $fiberStates");
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
            // FiberStatesIntegrationPoints ограничивает запись конкретными точками интегрирования
            // (см. CalcSettings.OpenSeesFiberStatesIntegrationPoints) — null означает "все точки".
            List<int>? selectedIps = model.FiberStatesIntegrationPoints is { } selected
                ? selected.Where(ip => ip >= 1 && ip <= e.NumIntegrationPoints).OrderBy(ip => ip).ToList()
                : null;
            if (selectedIps is { Count: 0 }) continue;
            if (selectedIps is null)
                line($"        for {{set ip 1}} {{$ip <= {e.NumIntegrationPoints}}} {{incr ip}} {{");
            else
                line($"        foreach ip {{{string.Join(' ', selectedIps)}}} {{");
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
}
