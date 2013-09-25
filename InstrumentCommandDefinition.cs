using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SSA;
using SSA.CoreLib;
using System.Diagnostics;

namespace Gala.SSA.InstrumentInterfaces
{
   using SSAThunk = Func<AInstrument, IInteropEnumerable, string>;
   internal sealed class InstrumentCommandDefinition
   {
      private readonly string _deprecatedMessage;
      private readonly string _name; 
      private readonly string _description;
      private readonly string _dialogName;

      private List<string> _parameterNames;
      private List<string> _resultNames;

      private readonly SSAThunk _cmdFunc;

      /// <summary>
      /// Create an InstrumentCommand that is fully specified.
      /// 
      /// This method must be internal since here we can specify an SSAThunk
      /// </summary>
      /// <param name="name"></param>
      /// <param name="description"></param>
      /// <param name="dialogName"></param>
      /// <param name="parameterNames"></param>
      /// <param name="resultNames"></param>
      /// <param name="isDeprecated"></param>
      /// <param name="shouldFail"></param>
      /// <param name="deprecatedMessage"></param>
      /// <param name="cmdFunc"></param>
      internal InstrumentCommandDefinition(
         string name,
         string description,
         string dialogName,
         IEnumerable<string> parameterNames,
         IEnumerable<string> resultNames,
         string deprecatedMessage,
         SSAThunk cmdFunc
      ) {
         Debug.Assert(!String.IsNullOrEmpty(name));
         Debug.Assert(parameterNames != null);
         Debug.Assert(resultNames != null);
         //Contract.EndContractBlock();
         _name = name;
         _description = description;
         _dialogName = dialogName;
         _deprecatedMessage = deprecatedMessage;

         _parameterNames = new List<string>(parameterNames);
         _resultNames = new List<string>(resultNames);

         _cmdFunc = cmdFunc ?? _failFunc;
      }

      public string Name {
         get {
            return this._name;
         }
      }

      public int ParameterCount {
         get {
            return _parameterNames.Count;
         }
      }

      public SafeInstrumentCommand CreateCommandInstance() {
         return new SafeInstrumentCommand(
            _name,
            _description,
            _dialogName,
            "",
            _parameterNames,
            _resultNames
         );
      }

      /// <summary>
      /// Execute the InstrumentCommand.
      /// 
      /// This method is internal because it does no parameter validation and requires
      /// ReflectionDemand on .NET 3.5 w/o SP1. (I think).
      /// 
      /// Either way, internal is safer.
      /// </summary>
      /// <param name="args"></param>
      /// <returns></returns>
      internal string ExecuteCommand(AInstrument instrument, IInteropEnumerable args) {
         return _cmdFunc(instrument, args);
      }

      private string _failFunc(object instrument, System.Collections.IEnumerable args) {
         throw new global::SSA.NetExceptions.SSAInstrumentException(
            String.Format(System.Globalization.CultureInfo.InvariantCulture, "Command {0} is deprecated: {1}", _name, _deprecatedMessage)
         );
      }
   }
}
