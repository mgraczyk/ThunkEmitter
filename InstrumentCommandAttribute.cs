using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Gala.SSA.InstrumentInterfaces
{
   [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
   public sealed class InstrumentCommandAttribute : Attribute
   {
      private string _commandName = String.Empty;
      private string _description = String.Empty;
      private string _dialogName = String.Empty;
      private string _resultName = String.Empty;

      public string CommandName {
         get {
            return this._commandName;
         }
         set {
            this._commandName = value;
         }
      }

      public string Description {
         get {
            return this._description;
         }
         set {
            this._description = value;
         }
      }

      public string DialogName {
         get {
            return this._dialogName;
         }
         set {
            this._dialogName = value;
         }
      }

      public string ResultName {
         get {
            return this._resultName;
         }
         set {
            this._resultName = value;
         }
      }
   }
}
