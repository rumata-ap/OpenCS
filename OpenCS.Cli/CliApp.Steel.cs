using System.CommandLine;
using System.Globalization;
using CScore;
using CScore.Sp16;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>Сортамент: поиск профиля и параметрическое МК-сечение со сталью по толщине профиля.</summary>
sealed partial class CliApp
{
    readonly Argument<string> _profileQuery = new("query");
    readonly Option<string?> _profileKind = new("--kind");
    readonly Option<string?> _fabrication = new("--fabrication");
    readonly Option<string> _profile = new("--profile") { Required = true };
    readonly Option<string?> _standard = new("--standard");
    readonly Option<string> _steel = new("--steel") { Required = true };

    /// <summary>Строка сортамента в поиске: подтип и профиль.</summary>
    sealed record CatalogHit(SteelCatalogSubtype Subtype, SteelProfileKind Kind, SteelFabrication Fabrication,
        SteelCatalogProfileItem Profile);

    void DescribeSteelOptions()
    {
        _profileQuery.Description = Loc.S("CliArgProfileQuery");
        _profileKind.Description = Loc.S("CliOptProfileKind");
        _fabrication.Description = Loc.S("CliOptFabrication");
        _profile.Description = Loc.S("CliOptProfile");
        _standard.Description = Loc.S("CliOptStandard");
        _steel.Description = Loc.S("CliOptSteel");
    }

    Command ListProfilesCommand()
    {
        var command = new Command("profiles", Loc.S("CliCmdListProfiles")) { _profileQuery, _profileKind, _fabrication };
        command.SetAction((p, ct) => Handle(p, ct, ListProfilesAsync));
        return command;
    }

    Command AddSteelSectionCommand() =>
        EditCommand("steel-section", "CliCmdAddSteelSection", AddSteelSectionAsync, _profile, _standard, _steel, _tag);

    static ProfileDB OpenSortament()
    {
        string path = Path.Combine(MaterialCatalog.DefaultDirectory, "Sortamenty.db3");
        if (!File.Exists(path)) throw new CliException(CliExitCode.Failed, string.Format(Loc.S("CliErrSortamentMissing"), path));
        return new ProfileDB(path);
    }

    /// <summary>Имя профиля для сравнения: без пробелов, заглавными, латиница-двойники — кириллицей («30K1» = «30К1»).</summary>
    internal static string NormalizeProfileName(string name)
    {
        const string lat = "ABEKMHOPCTXY", cyr = "АВЕКМНОРСТХУ";
        var chars = name.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant)
            .Select(c => lat.IndexOf(c) is >= 0 and var i ? cyr[i] : c).ToArray();
        return new string(chars).Replace(',', '.');
    }

    static T? ParseEnumOption<T>(string? value, string option) where T : struct, Enum
    {
        if (value == null) return null;
        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _))
            return parsed;
        throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrOptionValue"), option, value,
            string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))));
    }

    static IEnumerable<CatalogHit> CatalogProfiles(ProfileDB sortament, SteelProfileKind? kind, SteelFabrication? fabrication)
    {
        foreach (var k in Enum.GetValues<SteelProfileKind>().Where(k => kind == null || k == kind))
            foreach (var f in Enum.GetValues<SteelFabrication>().Where(f => fabrication == null || f == fabrication))
                foreach (var subtype in sortament.GetSteelCatalogSubtypes(k, f))
                    foreach (var profile in sortament.GetSteelCatalogProfiles(subtype.Id))
                        yield return new CatalogHit(subtype, k, f, profile);
    }

    Task<int> ListProfilesAsync(CliCall call)
    {
        var p = call.Parse;
        var kind = ParseEnumOption<SteelProfileKind>(p.GetValue(_profileKind), "--kind");
        var fabrication = ParseEnumOption<SteelFabrication>(p.GetValue(_fabrication), "--fabrication");
        string query = NormalizeProfileName(p.GetValue(_profileQuery)!);
        var hits = CatalogProfiles(OpenSortament(), kind, fabrication)
            .Where(h => NormalizeProfileName(h.Profile.Name).Contains(query, StringComparison.Ordinal))
            .Select(h => new
            {
                SubtypeId = h.Subtype.Id, Standard = h.Subtype.Name, Kind = h.Kind.ToString().ToLowerInvariant(),
                Fabrication = h.Fabrication.ToString().ToLowerInvariant(), Profile = h.Profile.Name,
                h.Profile.HMm, h.Profile.ACm2
            }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, hits);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColSubtype"), Loc.S("CliColStandard"), Loc.S("CliColKind"), Loc.S("CliColFabrication"),
                 Loc.S("CliColProfile"), Loc.S("CliColHmm"), Loc.S("CliColAcm2")],
                hits.Select(h => new[]
                {
                    h.SubtypeId.ToString(), h.Standard, h.Kind, h.Fabrication, h.Profile,
                    h.HMm?.ToString("0.##", CultureInfo.CurrentCulture) ?? Loc.S("CliNone"),
                    h.ACm2?.ToString("0.##", CultureInfo.CurrentCulture) ?? Loc.S("CliNone")
                }));
        return Task.FromResult(CliExitCode.Ok);
    }

    /// <summary>Строка сортамента по имени профиля и (необязательно) стандарту; неоднозначность — ошибка со списком.</summary>
    static SteelCatalogEntry FindCatalogEntry(ProfileDB sortament, string profile, string? standard)
    {
        string name = NormalizeProfileName(profile);
        var hits = CatalogProfiles(sortament, null, null)
            .Where(h => NormalizeProfileName(h.Profile.Name) == name).ToList();
        if (!string.IsNullOrWhiteSpace(standard))
        {
            string s = standard.Trim();
            hits = int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && hits.Any(h => h.Subtype.Id == id)
                ? hits.Where(h => h.Subtype.Id == id).ToList()
                : hits.Where(h => h.Subtype.Name.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (hits.Count == 0)
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrProfileNotFound"), profile));
        if (hits.Count > 1)
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrProfileAmbiguous"), profile,
                string.Join("; ", hits.Select(h => $"{h.Subtype.Id} — {h.Subtype.Name}"))));
        var hit = hits[0];
        return sortament.GetSteelCatalogEntry(hit.Subtype.Id, hit.Profile.Id)
            ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrProfileNotFound"), profile));
    }

    Task<int> AddSteelSectionAsync(CliCall call)
    {
        var p = call.Parse;
        string profile = p.GetValue(_profile)!;
        string mark = p.GetValue(_steel)!;
        var entry = FindCatalogEntry(OpenSortament(), profile, p.GetValue(_standard));
        double thickness = Math.Max(entry.Tf, entry.Tw);
        var steel = MaterialCatalog.CreateStructuralSteel(mark, thickness, shaped: entry.Fabrication == SteelFabrication.Rolled)
            ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrSteelNotFound"), mark,
                (thickness * 1000).ToString("0.#", CultureInfo.CurrentCulture)));

        return Edit(call, session => SaveSteelSection(call, session.Context.Db, entry, steel));
    }

    void SaveSteelSection(CliCall call, DatabaseService db, SteelCatalogEntry entry, Material steel)
    {
        var p = call.Parse;
        var material = MaterialCatalog.FindSameSteel(db.Materials, steel);
        bool created = material == null;
        if (material == null)
        {
            db.AddMaterial(steel);
            material = steel;
        }
        string tag = p.GetValue(_tag) ?? entry.Name;
        var section = new CrossSection { Num = NextNum(db.CrossSections, s => s.Num) };
        var result = new ParametricSteelSectionProjectService(db)
            .GenerateAndSave(section, entry.ToDefinition() with { MaterialId = material.Id, Tag = tag });
        if (result.Diagnostics.Count != 0)
            throw new CliException(CliExitCode.Failed,
                string.Format(Loc.S("CliErrSectionGeneration"), string.Join("; ", result.Diagnostics)));

        if (call.Json)
            CliOutput.WriteJson(call.Out, new
            {
                section.Id, section.Num, section.Tag, Profile = entry.Name, entry.Standard,
                Material = new { material.Id, material.Tag, Created = created }
            });
        else
            call.Out.WriteLine(string.Format(Loc.S("CliSteelSectionAdded"), section.Id, section.Tag, entry.Name,
                entry.Standard, material.Id, material.Tag, Loc.S(created ? "CliMaterialNew" : "CliMaterialExisting")));
    }
}
