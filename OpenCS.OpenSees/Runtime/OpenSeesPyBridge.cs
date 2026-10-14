namespace OpenCS.OpenSees.Runtime;

/// <summary>
/// Мост Tcl → OpenSeesPy: тот же Tcl-сценарий, что OpenCS отдаёт OpenSees.exe, исполняется Python-пакетом
/// <c>openseespy</c>. Интерпретатор Tcl берётся из стандартного <c>tkinter</c> (без окна), каждая функция
/// <c>openseespy.opensees</c> регистрируется как Tcl-команда того же имени; proc, циклы, файлы и puts исполняет
/// Tcl, расчёт — OpenSeesPy. Генераторы сценариев остаются одни на оба пути.
///
/// Python-разборщик OpenSeesPy понимает не всё, что Tcl-разборщик OpenSees.exe; мост переводит то, что
/// пишут генераторы OpenCS:
/// - блочные команды <c>pattern … { load … }</c>, <c>section Fiber … { fiber … }</c> — команда, затем тело в Tcl;
/// - ряд в строке <c>pattern Plain 1 Linear</c> → <c>timeSeries Linear</c> + номер;
/// - старый синтаксис <c>element forceBeamColumn tag i j nIP secTag transfTag</c> → <c>beamIntegration</c>
///   (умолчания OpenSees: force/nonlinearBeamColumn — Lobatto, dispBeamColumn — Legendre) + новый синтаксис.
/// Код Python — строкой здесь, а не файлом .py в репозитории: кладётся во временную папку при запуске
/// (<see cref="OpenCS.ExternalTools.PythonRuntime.MaterializeScript"/>).
/// </summary>
public static class OpenSeesPyBridge
{
    /// <summary>Имя инструмента в <see cref="OpenCS.ExternalTools.PythonToolCommand"/>.</summary>
    public const string ToolName = "opensees";

    public const string FileName = "opencs_opensees_tcl_bridge.py";

    public const string Script = """"
        """OpenCS: исполняет Tcl-сценарий OpenSees через OpenSeesPy (см. OpenSeesPyBridge.cs).

        python opencs_opensees_tcl_bridge.py <script.tcl> | -version
        Код возврата: 0 — сценарий выполнен; «exit N» в сценарии — N; ошибка Tcl/OpenSees — 1;
        нет openseespy или tkinter — 3.
        """

        import os
        import re
        import sys

        _INT = re.compile(r"^[+-]?\d+$")
        _BLOCK_COMMANDS = {"pattern", "section"}
        _OLD_BEAM_DEFAULT_RULE = {"forceBeamColumn": "Lobatto", "nonlinearBeamColumn": "Lobatto",
                                  "dispBeamColumn": "Legendre"}
        _ops = None
        _exit_code = None
        _integrations = {}
        _next_series_tag = [2000000]


        class _Exit(Exception):
            pass


        def _convert(arg):
            # Tcl передаёт строки; OpenSeesPy различает int/float/str, как и Tcl-разборщик OpenSees.
            if _INT.match(arg):
                return int(arg)
            try:
                return float(arg)
            except ValueError:
                return arg


        def _to_tcl(value):
            if value is None:
                return ""
            if isinstance(value, bool):
                return int(value)
            if isinstance(value, (list, tuple)):
                return tuple(_to_tcl(v) for v in value)
            return value


        def _translate_old_beam(args):
            # Новый синтаксис: tag i j transfTag integrationTag (5 целых); старый: tag i j nIP secTag transfTag (6).
            if len(args) < 7 or args[0] not in _OLD_BEAM_DEFAULT_RULE:
                return args
            if not all(isinstance(a, int) for a in args[1:7]):
                return args
            tag, node_i, node_j, n_ip, sec_tag, transf_tag = args[1:7]
            rest = list(args[7:])
            rule = _OLD_BEAM_DEFAULT_RULE[args[0]]
            if "-integration" in rest:
                k = rest.index("-integration")
                rule = rest[k + 1]
                del rest[k:k + 2]
            key = (rule, sec_tag, n_ip)
            if key not in _integrations:
                integration_tag = 1000000 + len(_integrations) + 1
                _ops.beamIntegration(rule, integration_tag, sec_tag, n_ip)
                _integrations[key] = integration_tag
            return [args[0], tag, node_i, node_j, transf_tag, _integrations[key]] + rest


        def _translate_inline_series(args):
            if len(args) >= 3 and args[0] == "Plain" and isinstance(args[2], str) and not args[2].startswith("-"):
                _next_series_tag[0] += 1
                _ops.timeSeries(args[2], _next_series_tag[0])
                return [args[0], args[1], _next_series_tag[0]] + list(args[3:])
            return args


        def _is_body(arg):
            # Тело блока — пустое ({}) или с пробелами/переводами строк; «Linear», числа и флаги телом не бывают.
            return not arg.strip() or any(c.isspace() for c in arg)


        def _wrap(tcl, name, func):
            def command(*args):
                body = None
                if name in _BLOCK_COMMANDS and len(args) > 2 and _is_body(args[-1]):
                    body, args = args[-1], args[:-1]
                values = [_convert(a) for a in args]
                if name == "element":
                    values = _translate_old_beam(values)
                elif name == "pattern":
                    values = _translate_inline_series(values)
                result = func(*values)
                if body is not None:
                    tcl.eval(body)
                return _to_tcl(result)
            return command


        def _tcl_exit(*args):
            global _exit_code
            _exit_code = int(args[0]) if args else 0
            raise _Exit()


        def _version():
            try:
                from importlib.metadata import version
                return version("openseespy")
            except Exception:
                return "unknown"


        def main(argv):
            global _ops
            if len(argv) < 2:
                sys.stderr.write("usage: opencs_opensees_tcl_bridge.py <script.tcl> | -version\n")
                return 2
            try:
                import tkinter
                import openseespy.opensees as ops
            except ImportError as ex:
                sys.stderr.write("OpenSeesPy недоступен: %s. Установите: python -m pip install openseespy "
                                 "(на Linux также пакет python3-tk).\n" % ex)
                return 3
            if argv[1] in ("-version", "--version"):
                print("OpenSeesPy %s (Python %s)" % (_version(), sys.version.split()[0]))
                return 0

            _ops = ops
            tcl = tkinter.Tcl()
            for name in dir(ops):
                func = getattr(ops, name)
                if name.startswith("_") or not callable(func) or isinstance(func, type):
                    continue
                # load перекрывает Tcl-команду загрузки расширений — как в OpenSees.exe.
                tcl.createcommand(name, _wrap(tcl, name, func))
            tcl.createcommand("exit", _tcl_exit)
            # Живой журнал: построчная выдача puts, как у OpenSees.exe.
            tcl.eval("fconfigure stdout -buffering line")

            code = 0
            try:
                tcl.call("source", "-encoding", "utf-8", os.path.abspath(argv[1]))
            except tkinter.TclError as ex:
                if _exit_code is None:
                    sys.stderr.write("OpenSees (OpenSeesPy): %s\n" % ex)
                    try:
                        sys.stderr.write(str(tcl.eval("set ::errorInfo")) + "\n")
                    except tkinter.TclError:
                        pass
                    code = 1
                else:
                    code = _exit_code
            finally:
                # Каналы, не закрытые сценарием (puts [open marker w] done), закрываются, файлы recorder-ов
                # дописываются (wipe) — как при завершении OpenSees.exe.
                try:
                    tcl.eval("foreach c [file channels] { if {$c ni {stdin stdout stderr}} { catch {close $c} } }")
                except tkinter.TclError:
                    pass
                try:
                    ops.wipe()
                except Exception:
                    pass
                sys.stdout.flush()
                sys.stderr.flush()
            return code


        if __name__ == "__main__":
            # os._exit: OpenSeesPy при обычном завершении печатает «Process 0 Terminating».
            os._exit(main(sys.argv))
        """";
}
