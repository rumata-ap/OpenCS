using CScore;

using OpenCS.ViewModels;

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCS.Utilites
{
   public static class Renumberer
   {
      public static void AddRange<T>(this ObservableCollection<T> oc, IEnumerable<T> other)
      {
         foreach (var item in other)
         {
            oc.Add(item);
         }
      }
      public static void AddRange<T>(this ObservableCollection<T> oc, IList other)
      {
         for (int i = 0; i < other.Count; i++)
            oc.Add((T)other[i]);
      }
      public static void RemoveRange<T>(this ObservableCollection<T> oc, IList other)
      {
         for (int i = 0; i < other.Count; i++)
            oc.Remove((T)other[i]);
      }
      public static void RegionsRenumber(this AppViewModel mvm)
      {
         for (int i = 0; i < mvm.RegionsLive.Count; i++)
            mvm.RegionsLive[i].Num = i + 1;
      }
      public static void RebarsRenumber(this AppViewModel mvm)
      {
         mvm.ReBarsLive = [.. mvm.Rebars];
         for (int i = 0; i < mvm.ReBarsLive.Count; i++)
            mvm.ReBarsLive[i].Num = i + 1;
      }
      public static void RebarLayersRenumber(this AppViewModel mvm)
      {
         for (int i = 0; i < mvm.RebarLayersLive.Count; i++)
            mvm.RebarLayersLive[i].Num = i + 1;
      }
      public static void RebarGroupsRenumber(this AppViewModel mvm)
      {
         mvm.RebarGroupsLive = [.. mvm.RebarGroups];
         for (int i = 0; i < mvm.RebarGroupsLive.Count; i++)
            mvm.RebarGroupsLive[i].Num = i + 1;
      }
      public static void RCFiberRegionsRenumber(this AppViewModel mvm)
      {
         mvm.RcFiberRegionsLive = [.. mvm.RcFiberRegions];
         for (int i = 0; i < mvm.RcFiberRegionsLive.Count; i++)
            mvm.RcFiberRegionsLive[i].Num = i + 1;

         mvm.RebarGroupsLive = [.. mvm.RebarGroups];
         for (int i = 0; i < mvm.RebarGroupsLive.Count; i++)
            mvm.RebarGroupsLive[i].Num = i + 1;

         mvm.FibersLive = [.. mvm.Fibers];
         //for (int i = 0; i < mvm.FibersLive.Count; i++)
         //   mvm.FibersLive[i].Num = i + 1;

         mvm.ReBarsLive = [.. mvm.Rebars];
         //for (int i = 0; i < mvm.ReBarsLive.Count; i++)
         //   mvm.ReBarsLive[i].Num = i + 1;
      }
      public static void FibersRenumber(this AppViewModel mvm)
      {
         mvm.FibersLive = [.. mvm.Fibers];
         for (int i = 0; i < mvm.FibersLive.Count; i++)
            mvm.FibersLive[i].Num = i + 1;
      }
      public static void FiberRegionsRenumber(this AppViewModel mvm)
      {
         mvm.FiberRegionsLive = [.. mvm.FiberRegions];
         for (int i = 0; i < mvm.FiberRegionsLive.Count; i++)
            mvm.FiberRegionsLive[i].Num = i + 1;
      }
      public static void ContoursRenumber(this AppViewModel mvm)
      {
         mvm.ContoursLive = [];
         for (int i = 0; i < mvm.Contours.Count; i++)
         {
            mvm.ContoursLive.Add(new ContourVM(mvm.Contours[i]) { mvm = mvm });
            mvm.ContoursLive[i].Num = i + 1;
         }

         //mvm.PointsLive = [.. mvm.Points];
         //for (int i = 0; i < mvm.PointsLive.Count; i++)
         //   mvm.PointsLive[i].Num = i + 1;
      }
      public static void CirclesRenumber(this AppViewModel mvm)
      {
         mvm.CirclesLive = [.. mvm.Circles];
         for (int i = 0; i < mvm.CirclesLive.Count; i++)
            mvm.CirclesLive[i].Num = i + 1;
      }
      public static void PointsRenumber(this AppViewModel mvm)
      {
         mvm.PointsLive = [.. mvm.Points];
         for (int i = 0; i < mvm.PointsLive.Count; i++)
            mvm.PointsLive[i].Num = i + 1;
      }
      public static void MaterialsRenumber(this AppViewModel mvm)
      {
         for (int i = 0; i < mvm.Concretes.Count; i++)
            mvm.Concretes[i].Num = i + 1;
         for (int i = 0; i < mvm.Armatures.Count; i++)
            mvm.Armatures[i].Num = i + 1;
         for (int i = 0; i < mvm.Steels.Count; i++)
            mvm.Steels[i].Num = i + 1;
      }
      public static void Renumber(this AppViewModel mvm)
      {
         for (int i = 0; i < mvm.Concretes.Count; i++)
            mvm.Concretes[i].Num = i + 1;
         for (int i = 0; i < mvm.Armatures.Count; i++)
            mvm.Armatures[i].Num = i + 1;
         for (int i = 0; i < mvm.Steels.Count; i++)
            mvm.Steels[i].Num = i + 1;
         for (int i = 0; i < mvm.PointsLive.Count; i++)
            mvm.PointsLive[i].Num = i + 1;
         for (int i = 0; i < mvm.CirclesLive.Count; i++)
            mvm.CirclesLive[i].Num = i + 1;
         for (int i = 0; i < mvm.ContoursLive.Count; i++)
            mvm.ContoursLive[i].Num = i + 1;
         for (int i = 0; i < mvm.FiberRegionsLive.Count; i++)
            mvm.FiberRegionsLive[i].Num = i + 1;
         for (int i = 0; i < mvm.FibersLive.Count; i++)
            mvm.FibersLive[i].Num = i + 1;
         for (int i = 0; i < mvm.RcFiberRegionsLive.Count; i++)
            mvm.RcFiberRegionsLive[i].Num = i + 1;
         for (int i = 0; i < mvm.RebarGroupsLive.Count; i++)
            mvm.RebarGroupsLive[i].Num = i + 1;
         for (int i = 0; i < mvm.RebarLayersLive.Count; i++)
            mvm.RebarLayersLive[i].Num = i + 1;
         for (int i = 0; i < mvm.ReBarsLive.Count; i++)
            mvm.ReBarsLive[i].Num = i + 1;
         for (int i = 0; i < mvm.RegionsLive.Count; i++)
            mvm.RegionsLive[i].Num = i + 1;
      }
   }
}
