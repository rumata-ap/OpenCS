using CScore;

namespace OpenCS.Utilites;

public partial class DatabaseService
{
   /// <summary>
   /// Атомарно сохраняет новые сечения с их областями (импорт сечений из чертежа): номера — следующие свободные,
   /// бетон и области без носителя раньше арматуры (у арматуры HostAreaId = Id носителя). При ошибке — откат,
   /// Id объектов возвращаются к 0, коллекции проекта не меняются.
   /// </summary>
   public void SaveNewCrossSections(IReadOnlyList<CrossSection> sections)
   {
      if (sections.Count == 0) return;
      int areaNum = MaterialAreas.Count > 0 ? MaterialAreas.Max(a => a.Num) + 1 : 1;
      int sectionNum = CrossSections.Count > 0 ? CrossSections.Max(s => s.Num) + 1 : 1;
      using var tx = _connection.BeginTransaction();
      try
      {
         foreach (var section in sections)
         {
            if (section.Id != 0) throw new InvalidOperationException($"Сечение «{section.Tag}» уже сохранено.");
            foreach (var area in section.Areas.OrderBy(a => a.HostArea is null ? 0 : 1))
            {
               area.Num = areaNum++;
               SaveMaterialAreaCore(area, _connection);
            }
            section.Num = sectionNum++;
            SaveCrossSectionCore(section);
         }
         tx.Commit();
      }
      catch
      {
         tx.Rollback();
         foreach (var section in sections)
         {
            section.Id = 0;
            foreach (var area in section.Areas) area.Id = 0;
         }
         throw;
      }
      foreach (var section in sections)
      {
         foreach (var area in section.Areas)
            if (!MaterialAreas.Contains(area)) MaterialAreas.Add(area);
         CrossSections.Add(section);
      }
   }
}
