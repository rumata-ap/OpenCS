using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   public struct Boundary
   {
      public double max;
      public double min;
      public double maxX;
      public double minX;
      public double maxY;
      public double minY;
      public IList<XY> coordinates;
   }
}
