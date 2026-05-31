using Newtonsoft.Json;

using System.Collections.Generic;
using System.Linq;

namespace CScore
{
   public class FiberRegionData
   {
      [JsonIgnore]
      public FiberRegion FiberRegion { get; set; }
      public IList<double> X { get; set; }
      public IList<double> Y { get; set; }
      public IList<double> E { get; set; }
      public IList<double> E2 { get; set; }
      public IList<double> Sig { get; set; }
      public IList<double> Eps { get; set; }
      public IList<double> Nu1 { get; set; }
      public IList<double> Nu2 { get; set; }
      public IList<double> N { get; set; }
      public IList<double> A { get; set; }
      public IList<double> My { get; set; }
      public IList<double> Mz { get; set; }
      public IList<string> Tags { get; set; }
      public string Tag { get; set; }
      public int Id { get; set; }

      public FiberRegionData(FiberRegion fiberRegion)
      {
         FiberRegion = fiberRegion;
         int count = FiberRegion.Fibers.Count();
         X = new double[count];
         Y = new double[count];
         A = new double[count];
         E = new double[count];
         E2 = new double[count];
         Sig = new double[count];
         Eps = new double[count];
         Nu1 = new double[count];
         Nu2 = new double[count];
         N = new double[count];
         My = new double[count];
         Mz = new double[count];
         Tags = new string[count];
         Tag = fiberRegion.Tag;
         Id = fiberRegion.Id;

         for (int i = 0; count > i; i++)
         {
            X[i] = FiberRegion.Fibers[i].X;
            Y[i] = FiberRegion.Fibers[i].Y;
            A[i] = FiberRegion.Fibers[i].Area;
            E[i] = FiberRegion.Fibers[i].E;
            E2[i] = FiberRegion.Fibers[i].E2;
            Sig[i] = FiberRegion.Fibers[i].Sig;
            Eps[i] = FiberRegion.Fibers[i].Eps;
            Nu1[i] = FiberRegion.Fibers[i].Nu1;
            Nu2[i] = FiberRegion.Fibers[i].Nu2;
            N[i] = FiberRegion.Fibers[i].N;
            My[i] = FiberRegion.Fibers[i].My;
            Mz[i] = FiberRegion.Fibers[i].Mz;
            Tags[i] = FiberRegion.Fibers[i].Tag;
         }
      }

      public FiberRegionData(RCFiberRegion fiberRegion)
      {
         FiberRegion = fiberRegion;
         int count = FiberRegion.Fibers.Count();
         if (fiberRegion.ReBarGroups != null)
            foreach (var item in fiberRegion.ReBarGroups)
               count += item.ReBars.Count;

         X = new double[count];
         Y = new double[count];
         A = new double[count];
         E = new double[count];
         E2 = new double[count];
         Sig = new double[count];
         Eps = new double[count];
         Nu1 = new double[count];
         Nu2 = new double[count];
         N = new double[count];
         My = new double[count];
         Mz = new double[count];
         Tags = new string[count];
         Tag = fiberRegion.Tag;
         Id = fiberRegion.Id;

         for (int i = 0; i < FiberRegion.Fibers.Count; i++)
         {
            X[i] = FiberRegion.Fibers[i].X;
            Y[i] = FiberRegion.Fibers[i].Y;
            A[i] = FiberRegion.Fibers[i].Area;
            E[i] = FiberRegion.Fibers[i].E;
            E2[i] = FiberRegion.Fibers[i].E2;
            Sig[i] = FiberRegion.Fibers[i].Sig;
            Eps[i] = FiberRegion.Fibers[i].Eps;
            Nu1[i] = FiberRegion.Fibers[i].Nu1;
            Nu2[i] = FiberRegion.Fibers[i].Nu2;
            N[i] = FiberRegion.Fibers[i].N;
            My[i] = FiberRegion.Fibers[i].My;
            Mz[i] = FiberRegion.Fibers[i].Mz;
            Tags[i] = FiberRegion.Fibers[i].Tag;
         }

         int offset = FiberRegion.Fibers.Count;
         if (fiberRegion.ReBarGroups != null)
            foreach (var item in fiberRegion.ReBarGroups)
               for (int i = 0; i < item.ReBars.Count; i++)
               {
                  X[offset + i] = item.ReBars[i].X;
                  Y[offset + i] = item.ReBars[i].Y;
                  A[offset + i] = item.ReBars[i].Area;
                  E[offset + i] = item.ReBars[i].E;
                  E2[offset + i] = item.ReBars[i].E2;
                  Sig[offset + i] = item.ReBars[i].Sig;
                  Eps[offset + i] = item.ReBars[i].Eps;
                  Nu1[offset + i] = item.ReBars[i].Nu1;
                  Nu2[offset + i] = item.ReBars[i].Nu2;
                  N[offset + i] = item.ReBars[i].N;
                  My[offset + i] = item.ReBars[i].My;
                  Mz[offset + i] = item.ReBars[i].Mz;
                  Tags[offset + i] = item.ReBars[i].Tag;
               }
      }
   }
}
