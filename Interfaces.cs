using System;
using System.Runtime.InteropServices;
using SSA;

namespace Gala.SSA.InstrumentInterfaces
{
   [ComVisible(true)]
   public interface IGPIBLoader : IInstrument, IInstrumentDescription
   {
      void loadGpibDescription(System.Xml.XmlDocument description);

      string IdString { get; }
   }

   [ComVisible(true)]
   public interface IVariableParser
   {
      string SubstituteVariables(string parseCandidate);
   }
}
