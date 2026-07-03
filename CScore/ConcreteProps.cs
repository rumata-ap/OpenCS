using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   public struct ConcreteProps
   {
      public string Tag;
      public double Class;
      public double E;
      public double Rb;
      public double Rbt;
      public double Rbn;
      public double Rbtn;
      public double Eps_b0;
      public double Eps_b2;
      public double Eps_b1red;
      public double Eps_bt0;
      public double Eps_bt2;
      public double Eps_bt1red;
      public double[] Fib_cr;
      public double[] Eps_b0_cr;
      public double[] Eps_b2_cr;
      public double[] Eps_b1red_cr;
      public double[] Eps_bt0_cr;
      public double[] Eps_bt2_cr;
      public double[] Eps_bt1red_cr;
   }

   public class Concrete
   {
      static Dictionary<double, ConcreteProps> DataHeavy()
      {
         Dictionary<double, ConcreteProps> res = new Dictionary<double, ConcreteProps>();
         res[10] = new ConcreteProps()
         {
            Class = 10, Tag = "B10", E = 19e6,
            Rb = -6e3, Rbn = -7.5e3,
            Rbt = 0.56e3, Rbtn = 0.85e3,          
            Fib_cr = new double[] { 2.8, 3.9, 5.6 },
         };
         res[15] = new ConcreteProps()
         {
            Class = 15, Tag = "B15", E = 24e6,
            Rb = -8.5e3, Rbn = -11e3,
            Rbt = 0.75e3, Rbtn = 1.1e3,          
            Fib_cr = new double[] { 2.4, 3.4, 4.8 },
         };


         for (int i = 0; i < res.Count; i++)
         {
            var k = res.Keys.ToArray()[i];
            var p = res[k];
            p.Eps_b0_cr = new double[] { -3e-3, -3.4e-3, -4e-3 };
            p.Eps_b2_cr = new double[] { -4.2e-3, -4.8e-3, -5.6e-3 };
            p.Eps_b1red_cr = new double[] { -2.4e-3, -2.8e-3, -3.4e-3 };
            p.Eps_bt0_cr = new double[] { 2.1e-4, 2.4e-4, 2.8e-4 };
            p.Eps_bt2_cr = new double[] { 2.7e-4, 3.1e-4, 3.6e-4 };
            p.Eps_bt1red_cr = new double[] { 1.9e-4, 2.2e-4, 2.6e-4 } ;
            p.Eps_b0 = -2e-3;
            p.Eps_b2 = -3.5e-3;
            p.Eps_bt0 = 1e-4;
            p.Eps_bt2 = 1.5e-4;
            p.Eps_bt1red = 8e-5;
            p.Eps_b1red = -1.5e-3;

            res[k] = p;
         }

         return res;
      }
   }
}
