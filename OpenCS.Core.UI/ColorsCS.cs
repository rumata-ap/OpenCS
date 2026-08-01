using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCS.Utilites
{
   public readonly struct ColorsCS
   {
      private readonly string[] arr =
      [
          "#003A6C", "#007FFF", "#5733FF", "#FFC300", "#B0BF1A",
          "#C70039", "#900C3F", "#581845", "#1ABC9C", "#2ECC71",
          "#3498DB", "#9B59B6", "#34495E", "#F1C40F", "#E67E22",
          "#E74C3C", "#ECF0F1", "#95A5A6", "#7F8C8D", "#BDC3C7",
          "#D35400", "#F39C12", "#16A085", "#27AE60", "#2980B9",
          "#8E44AD", "#2C3E50", "#F8C471", "#D5DBDB", "#A569BD",
          "#5DADE2", "#48C9B0", "#F4D03F", "#CD6155", "#5499C7",
          "#7D3C98", "#1A5276", "#148F77", "#B7950B", "#B03A2E"
      ];

      public readonly string this[int i] { get => arr[i]; set => arr[i] = value; }

      public ColorsCS()
      {
         
      }

   }
}
