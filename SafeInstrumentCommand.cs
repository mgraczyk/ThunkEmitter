using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SSA;
using SSA.CoreLib;
using Gala.SSA.CoreLib;

namespace Gala.SSA.InstrumentInterfaces
{
   internal class SafeInstrumentCommand : IInstrumentCommand
   {
      private string _name;  // TODO: Make all of these readonly
      private string _description;
      private string _dialogName;
      private string _commandString;
      private Dictionary<string, string> _parameters;
      private Dictionary<string, string> _results;

      internal SafeInstrumentCommand(
         string name, 
         string description,
         string dialogName,
         string commandString,
         IEnumerable<string> paramNames,
         IEnumerable<string> resultNames
      ) {
         _name = name;
         _description = description;
         _dialogName = dialogName;
         _commandString = commandString;

         _parameters = new Dictionary<string, string>();
         foreach (var p in paramNames) {
            _parameters.Add(p, "");
         }

         _results = new Dictionary<string, string>();
         foreach (var r in resultNames) {
            _results.Add(r, "");
         }
      }

      public string GetDescription() {
         return _description;
      }

      public void SetDescription(string description) {
         _description = description;
      }

      public string GetName() {
         return _name;
      }

      public void SetName(string name) {
         _name = name;
      }

      public string GetCommandString() {
         return _commandString;
      }

      public void SetCommandString(string commandString) {
         _commandString = commandString;
      }

      public string GetDialogName() {
         return _dialogName;
      }

      public IInteropEnumerable GetParametersKeys() {
         return InteropEnumerableFactory.Create(_parameters.Keys);
      }

      public string GetParameterValue(string key) {
         return _parameters[key];
      }

      public void SetParameterValue(string key, string value) {
         if (_parameters.ContainsKey(key))
            _parameters[key] = value;
         else
            throw new ArgumentException("The specified key was not a parameter of this command.", "key");
      }

      public IInteropEnumerable GetResultsKeys() {
         return InteropEnumerableFactory.Create(_results.Keys);
      }

      public string GetResultsValue(string key) {
         return _results[key];
      }

      public void SetResultsValue(string key, string value) {
         if (_results.ContainsKey(key))
            _results[key] = value;
         else
            throw new ArgumentException("The specified key was not a result of this command.", "key");
      }
   }
}
