using System.CommandLine;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// Правящие команды: сборка проекта без UI (новый проект, сечения, наборы усилий, задачи). Пишут в сам файл
/// проекта (открытый в OpenCS — отказ), с <c>--out</c> — в новый файл, исходник не трогается.
/// </summary>
sealed partial class CliApp
{
    readonly Argument<string> _newProject = new("project");
    readonly Option<bool> _newForce = new("--force");
    readonly Option<string?> _editOut = new("--out");
    readonly Option<bool> _editForce = new("--force");
    readonly Option<string?> _tag = new("--tag");
    readonly Option<string?> _elements = new("--elements");

    void DescribeAuthoringOptions()
    {
        _newProject.Description = Loc.S("CliArgNewProject");
        _newForce.Description = Loc.S("CliOptNewForce");
        _editOut.Description = Loc.S("CliOptEditOut");
        _editForce.Description = Loc.S("CliOptForce");
        _tag.Description = Loc.S("CliOptTag");
        _elements.Description = Loc.S("CliOptElements");
    }

    Command NewCommand()
    {
        var command = new Command("new", Loc.S("CliCmdNew")) { _newProject, _newForce };
        command.SetAction((p, ct) => Handle(p, ct, NewAsync));
        return command;
    }

    /// <summary>Правящая команда: проект, <c>--out</c>, <c>--force</c> и свои параметры.</summary>
    Command EditCommand(string name, string descriptionKey, Func<CliCall, Task<int>> body, params Symbol[] symbols)
    {
        var command = new Command(name, Loc.S(descriptionKey)) { _project };
        foreach (var symbol in symbols)
            switch (symbol)
            {
                case Argument a: command.Arguments.Add(a); break;
                case Option o: command.Options.Add(o); break;
            }
        command.Options.Add(_editOut);
        command.Options.Add(_editForce);
        command.SetAction((p, ct) => Handle(p, ct, body));
        return command;
    }

    Task<int> NewAsync(CliCall call)
    {
        string display = call.Parse.GetValue(_newProject)!;
        string path = Path.GetFullPath(display);
        if (File.Exists(path))
        {
            if (!call.Parse.GetValue(_newForce))
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrOutExists"), display));
            if (ProjectFile.IsInUse(path))
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrInUse"), display));
            File.Delete(path);
        }
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using (new DatabaseService(path)) { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (call.Json)
            CliOutput.WriteJson(call.Out, new { Project = path, SchemaVersion = DatabaseService.SchemaVersion });
        else
            call.Out.WriteLine(string.Format(Loc.S("CliNewCreated"), path));
        return Task.FromResult(CliExitCode.Ok);
    }

    /// <summary>
    /// Правка проекта: сам файл или копия с сохранением в --out. Ошибка в <paramref name="body"/> — копия в --out
    /// не сохраняется (проверки ввода идут до записи в БД, поэтому сам файл при ошибке не меняется).
    /// </summary>
    Task<int> Edit(CliCall call, Action<ProjectSession> body)
    {
        string? outPath = call.Parse.GetValue(_editOut);
        using var session = call.OpenProject(new ProjectSessionOptions(InPlace: outPath == null, OutPath: outPath,
            Force: call.Parse.GetValue(_editForce)));
        try
        {
            body(session);
        }
        catch
        {
            session.Discard();
            throw;
        }
        string? saved = session.Close();
        if (outPath != null && saved != null)
            call.Err.WriteLine(string.Format(Loc.S("CliSavedTo"), saved));
        return Task.FromResult(CliExitCode.Ok);
    }

    static int NextNum<T>(IEnumerable<T> items, Func<T, int> num) => items.Select(num).DefaultIfEmpty(0).Max() + 1;
}
