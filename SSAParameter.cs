using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using SSA;
using System.Globalization;

namespace Gala.SSA.InstrumentInterfaces
{
   internal static class SSAParameter
   {
      private static readonly System.Text.RegularExpressions.Regex ParameterRegex;
      private static readonly char[] ArrayTrimCharacters;
      private static readonly char[] ArraySplitCharacters;

      static SSAParameter() {
         ParameterRegex = new System.Text.RegularExpressions.Regex(
            // Example: <Parameter Name="name" Value="value" />
             @"\s*<\s*Parameter\s+Name\s*=\s*""([^""]*)""\s+Value\s*=\s*""([^""]*)""\s*(?:(?:/\s*>\s*$)|(?:>\s*<\s*/\s*Parameter\s*>$))",
             System.Text.RegularExpressions.RegexOptions.Compiled
         );

         ArrayTrimCharacters = new char[] { '[', ']' };
         ArraySplitCharacters = new char[] { ',' };
      }

      private static bool TryParseNamedParameterXml(string parameterXml, out string name, out string value) {
         var match = ParameterRegex.Match(parameterXml);

         if (match.Success) {
            name = match.Groups[1].Value;
            value = match.Groups[2].Value;

            return true;
         } else {
            name = "";
            value = "";
            return false;
         }
      }

      /// <summary>
      /// Searches the args list for the parameter with the specified name, and returns the parameter with that type.
      /// 
      /// NOTE: Change the reflection code in CommandMapCache.buildCommandFromMethod if
      ///         You move this method out of SSAParameter.
      /// </summary>
      /// <typeparam name="T"></typeparam>
      /// <param name="args"></param>
      /// <param name="parameterName"></param>
      /// <returns></returns>
      internal static T GetSSAParameterValue<T>(IInteropEnumerable args, string parameterName, bool byRef) {
         string valueString;

         // We'll iterate over the whole list starting in the current position.
         // We don't have to worry about nulls in args, because a null in args 
         //    is an exception that will end command execution anyway.
         object first = args.Next();
         object current = first;

         if (current == null) {
            args.Reset();
            current = args.Next();
         }

         try {
            do {
               string name;
               if (TryParseNamedParameterXml((string)current, out name, out valueString)) {
                  if (String.Equals(name, parameterName, StringComparison.Ordinal)) {
                     // Convert the match and return it
                     // We can't do any better than sending ref types the strings, 
                     //    and sending the string itself as an Object.
                     if (byRef) {
                        return (T)(object)valueString;
                     } else {
                        return (T)Convert.ChangeType(valueString, typeof(T), CultureInfo.InvariantCulture);
                     }
                  }
               } else {
                  throw new ArgumentException("One of the args was invalid Xml.", "args");
               }

               if (current == null)
                  args.Reset();
               current = args.Next();
            } while (current != first);
         } catch (InvalidCastException ex) {
            throw new ArgumentException("One of the args was not a System.String.", "args", ex);
         } catch (FormatException ex) {
            throw new ArgumentException("One of the args was not in the correct format.", "args", ex);
         } catch (ArgumentNullException ex) {
            throw new ArgumentException("Insufficient args, or an arg was null.", "args", ex);
         }

         // We didn't find it
         throw new ArgumentException(String.Format( CultureInfo.InvariantCulture, "Parameter {0} was not found in the args list.", parameterName), "args");
      }

      /// <summary>
      /// 
      /// 
      /// NOTE: Change the reflection code in CommandMapCache.buildCommandFromMethod if
      ///         You move this method out of SSAParameter, or change it.
      /// </summary>
      /// <typeparam name="T"></typeparam>
      /// <param name="args"></param>
      /// <param name="parameterName"></param>
      /// <returns></returns>
      internal static T[] GetSSAParameterArrayValue<T>(IInteropEnumerable args, string parameterName, bool byRef) {
         string valueString;

         // We'll iterate over the whole list starting in the current position.
         // We don't have to worry about nulls in args, because a null in args 
         //    is an exception that will end command execution anyway.
         object first = args.Next();
         object current = first;

         if (current == null) {
            args.Reset();
            current = args.Next();
         }

         try {
            do {
               string name;
               if (TryParseNamedParameterXml((string)current, out name, out valueString)) {
                  if (String.Equals(name, parameterName, StringComparison.Ordinal)) {
                     // Convert the match and return it
                     // Follow the same conversion logic as in the old AInstrument
                     var valStrings = valueString.Trim(ArrayTrimCharacters).Split(
                        ArraySplitCharacters, StringSplitOptions.RemoveEmptyEntries
                     );

                     var retVal = new T[valStrings.Length];

                     if (byRef) {
                        for (int i = 0; i < retVal.Length; ++i) {
                           retVal[i] = (T)(object)valueString;
                        }
                     } else {
                        for (int i = 0; i < retVal.Length; ++i) {
                           retVal[i] = (T)Convert.ChangeType(valueString, typeof(T), CultureInfo.InvariantCulture);
                        }
                     }

                     return retVal;
                  }
               } else {
                  throw new ArgumentException("One of the args was invalid Xml.", "args");
               }

               if (current == null)
                  args.Reset();
               current = args.Next();
            } while (current != first);
         } catch (InvalidCastException ex) {
            throw new ArgumentException(String.Format(CultureInfo.InvariantCulture, "Either one of the args was not a {0}, or the parameter {1} was not assignable from {0}","System.String", parameterName), "args", ex);
         } catch (FormatException ex) {
            throw new ArgumentException("One of the args was not in the correct format.", "args", ex);
         } catch (ArgumentNullException ex) {
            throw new ArgumentException("Insufficient args, or an arg was null.", "args", ex);
         }

         // We didn't find it
         throw new ArgumentException(String.Format(CultureInfo.InvariantCulture, "Parameter {0} was not found in the args list.", parameterName), "args");
      }
   }
}
