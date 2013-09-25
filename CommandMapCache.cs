using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Threading;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Permissions;
using System.Diagnostics;
using System.Globalization;
using SSA;

namespace Gala.SSA.InstrumentInterfaces
{
   // Parameters are the AInstrument to execute command from, then the args.
   //    The return type is object since we can't restrict it here.
   //    This just matches the prototype of IInstrument.ExecuteCommand
   using SSAThunk = Func<AInstrument, IInteropEnumerable, string>;
   
   /// <summary>
   /// A cache of SSA commands so that instruments don't have to do reflection at every call to ExecuteCommand.
   /// </summary>
   internal static class CommandMapCache
   {
      // TODO: Tune to just slightly longer than half the init time, or 10 minimum
      private const int WaitForInitSleepTime = 20;
      private const string ObsoleteErrMessage = "Warning: Command {0} is Deprecated: {1}";
      private const string ResultXmlFormatStart = "<Result Name='{0}' Value='";
      private const string ResultXmlFormatEnd = "'/>";

      // Use the Commands list to implement inherited instruments
      //private static ReaderWriterLockSlim CommandsLock = new ReaderWriterLockSlim();
      //private static List<InstrumentCommandDefinition> Commands = new List<InstrumentCommandDefinition>();

      private static ReaderWriterLockSlim TypeMapLock = new ReaderWriterLockSlim();
      private static Dictionary<int, Dictionary<string, InstrumentCommandDefinition>> TypeMap = new Dictionary<int, Dictionary<string, InstrumentCommandDefinition>>();

      private static Dictionary<string, InstrumentCommandDefinition> EmptyMap;

      static CommandMapCache() { }

      //internal static InstrumentCommandDefinition GetGlobalCommandByIndex(int cmdIndex) {
      //   CommandsLock.EnterReadLock();
      //   Debug.Assert(Commands.Count > cmdIndex);
      //   var cmd = Commands[cmdIndex];
      //   CommandsLock.ExitReadLock();
      //   return cmd;
      //}

      //internal static IInteropEnumerable GetManyGlobalCommandsByIndex(IEnumerable<int> indices) {
      //   CommandsLock.EnterReadLock();
      //   foreach (var index in indices) {
      //      yield return Commands[index];
      //   }
      //   CommandsLock.ExitReadLock();
      //}

      /// <summary>
      /// Cache the InstrumentCommands on the specified type so taht they can be executed by ExecuteCommand.
      /// </summary>
      /// <param name="instrumentType">type of the instrument to reflect into the cache.</param>
      [ReflectionPermission(SecurityAction.Assert, Unrestricted = true)]
      public static Dictionary<string, InstrumentCommandDefinition> CacheType(Type instrumentType) {
         Debug.Assert(instrumentType != null);

         // TODO: Add inheritance support.
         // No need to support inheritance.
         // Fail if the type does not inherit directly from AInstrument
         Debug.Assert(instrumentType.BaseType == typeof(AInstrument),
            String.Format(CultureInfo.InvariantCulture, "Could not process instrument {0}. Types derived from {1} must derive directly from {1}.", instrumentType.FullName, typeof(AInstrument).Name));

         Debug.Assert(instrumentType.IsSubclassOf(typeof(AInstrument)),
            String.Format(CultureInfo.InvariantCulture, "Could not process instrument {0}. Type must derive from {1}.", instrumentType.FullName, typeof(AInstrument).Name));

         Dictionary<string, InstrumentCommandDefinition> commandsMap;
         var typeHash = instrumentType.GetHashCode();



         // Be sure we don't initialize twice
         // THESE LOCKS ARE CORRECT
         // We want other readers to block
         TypeMapLock.EnterWriteLock();
         if (TypeMap.ContainsKey(typeHash)) {
            TypeMapLock.ExitWriteLock();

            // We might be in initialization for this type.
            // Wait for init to complete
            while (true) {
               TypeMapLock.EnterReadLock();
               try {
                  if (TypeMap.TryGetValue(typeHash, out commandsMap))
                     return commandsMap;
               } finally {
                  TypeMapLock.ExitReadLock();
               }

               // Sleep instead of Spinning since this can 
               //    take quite a while (even 500 ms)
               Thread.Sleep(WaitForInitSleepTime);
            }
         } else {
            TypeMap.Add(typeHash, null);
            TypeMapLock.ExitWriteLock();
         }

         Debug.Assert(TypeMap.ContainsKey(typeHash));
         commandsMap = new Dictionary<string, InstrumentCommandDefinition>();

         // Now we're ready to initilize the command mapping
         foreach (var methodInfo in instrumentType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) {
            InstrumentCommandAttribute cmdAttr = null;
            ObsoleteAttribute obsAttr = null;
            {
               var enumerator = methodInfo.GetCustomAttributes(false).GetEnumerator();
               while (enumerator.MoveNext() && (obsAttr == null || cmdAttr == null)) {
                  var attr = enumerator.Current;

                  if (cmdAttr == null) {
                     cmdAttr = attr as InstrumentCommandAttribute;
                     continue;
                  }

                  if (obsAttr == null) {
                     obsAttr = attr as ObsoleteAttribute;
                     continue;
                  }
               }
            }

            // Skip non-commands
            if (cmdAttr == null)
               continue;

            try {
               // This method has an InstrumentCommandAttribute
               //
               // Build the method call using the provided metadata
               var cmdMethod = buildCommandFromMethod(instrumentType, methodInfo, cmdAttr, obsAttr);

               //CommandsLock.EnterWriteLock();
               //int index = Commands.Count;
               //Commands.Add(cmdMethod);
               //CommandsLock.ExitWriteLock();

               // Check that the type doesn't have two of the same command
               if (!commandsMap.ContainsKey(cmdMethod.Name)) {
                  commandsMap.Add(cmdMethod.Name, cmdMethod);
               } else {
                  Debug.Fail(String.Format(CultureInfo.InvariantCulture, 
                     "Instrument {0} defines command {1} multiple times. Commands may only be defined once.", 
                     instrumentType.Name, cmdMethod.Name));
               }
            } catch (SSAInstrumentReflectionException ex) {
               // TODO: Lump these up and deliver them all at the end of reflecting the type.
               Debug.Fail(ex.Message);
            }
         }

         // Cache an empty typemap so there aren't a bunch of empties floating around
         if (commandsMap.Count == 0) {
            if (EmptyMap == null) {
               EmptyMap = new Dictionary<string, InstrumentCommandDefinition>();
            }
            commandsMap = EmptyMap;
         }

         TypeMapLock.EnterWriteLock();
         TypeMap[typeHash] = commandsMap;
         TypeMapLock.ExitWriteLock();

         return commandsMap;
      }

      /// <summary>
      /// Builds InstrumentCommands for the 
      /// </summary>
      /// <param name="type"></param>
      /// <param name="cmdAttr"></param>
      /// <returns></returns>
      private static InstrumentCommandDefinition buildCommandFromMethod(Type instrument,
         MethodInfo cmdInfo,
         InstrumentCommandAttribute cmdAttr,
         ObsoleteAttribute obsAttr
         ) {
         string cmdName = String.IsNullOrEmpty(cmdAttr.CommandName) ?
            cmdInfo.Name :
            cmdAttr.CommandName;

         // We don't support static methods at this time
         if (cmdInfo.IsStatic)
            throw new SSAInstrumentReflectionException("AInstrument does not support static methods at this time.  Please modify command  " + cmdName + " so that it is not static.");

         // Name of the result if none were supplied.
         // Note that below we un-elegantly replace all null result values with this string.
         string cmdResult0Name = String.IsNullOrEmpty(cmdAttr.ResultName) ?
            "Result" :
            cmdAttr.ResultName;

         bool isObs;
         string deprecatedMessage;

         List<ParameterInfo> inParams;
         List<ParameterInfo> results;
         _getParamsResults(instrument.Name, cmdInfo, out inParams, out results);
         
         // Reify if it's generic
         if (cmdInfo.IsGenericMethodDefinition) {
            // Be sure that we can reify with all Objects
            var allObject = new Type[cmdInfo.GetGenericArguments().Length];
            for (int i = 0; i < allObject.Length; ++i) {
               allObject[i] = typeof(object);
            }
            // Jon Skeet told me to do it this way :(
            try {
               cmdInfo = cmdInfo.MakeGenericMethod(allObject);
            } catch (ArgumentException) {
               // We can't reify with object
               throw new SSAInstrumentReflectionException(typeof(AInstrument).Name + " does not support results on generic methods which cannot be reified to System.Object");
            }
         }

         // If the command is obsolete it must Log a special message.
         // If the command command is both obsolete and is error, it should fail
         if ((isObs = obsAttr != null)) {
            deprecatedMessage = obsAttr.Message ?? "";
            if (obsAttr.IsError) {
               // A call to this command should just throw an exception
               // NOTE: The original AInstrument just threw System.Exception here
               // InstrumentCommand knows to throw a depracated exception if shouldFail is true
               return new InstrumentCommandDefinition(
                  cmdName,
                  cmdAttr.Description ?? "",
                  cmdAttr.DialogName ?? "",
                  inParams.Select(pI => pI.Name),
                  results.Select(pI => pI.Name ?? cmdResult0Name),
                  deprecatedMessage,
                  null
               );
            }
         } else {
            isObs = false;
            deprecatedMessage = "";
         }

         Type[] paramConverterArgTypes = new Type[] { typeof(IInteropEnumerable), typeof(string), typeof(bool) };

         // Get some methods for easy use in code generation
         MethodInfo paramConverterMethod = typeof(SSAParameter).GetMethod(
            "GetSSAParameterValue",
            BindingFlags.Static | BindingFlags.NonPublic, Type.DefaultBinder,
            paramConverterArgTypes,
            null
         );

         Debug.Assert(paramConverterMethod != null);
         MethodInfo paramConverterMethodForObject = null;

         MethodInfo paramArrayConverterMethod = typeof(SSAParameter).GetMethod(
            "GetSSAParameterArrayValue",
            BindingFlags.Static | BindingFlags.NonPublic, Type.DefaultBinder,
            paramConverterArgTypes,
            null
         );
         Debug.Assert(paramArrayConverterMethod != null);
         MethodInfo paramArrayConverterMethodForObject = null;

         const string ResultsXmlTagEnd = @"</Results>";

#if DEBUG_EMIT
         // Debugging stuff 

         AssemblyName __debugMyAsmName = new AssemblyName("emitted");
         AssemblyBuilder __debugMyAssembly =
               AppDomain.CurrentDomain.DefineDynamicAssembly(__debugMyAsmName,
                  AssemblyBuilderAccess.Save, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

         // An assembly is made up of executable modules. For a single-
         // module assembly, the module name and file name are the same 
         // as the assembly name. 
         //
         ModuleBuilder __debugMyModule =
               __debugMyAssembly.DefineDynamicModule(__debugMyAsmName.Name,
                  __debugMyAsmName.Name + ".dll");

         // Define the sample type.
         //
         TypeBuilder __debugMyType =
               __debugMyModule.DefineType("DynamicContainer", TypeAttributes.Public);

         MethodBuilder dynMethod =
                  __debugMyType.DefineMethod("CmdThunk",
                  MethodAttributes.Public | MethodAttributes.Static,
                  CallingConventions.Standard,
                  typeof(string),
                  new Type[] { typeof(AInstrument), typeof(IInteropEnumerable) }
               );       
#else

         // Generate the command function itself
         // 
         var dynMethod = new DynamicMethod(
            instrument.Name + "<SSACmd>" + cmdName,
            MethodAttributes.Static | MethodAttributes.Public,
            CallingConventions.Standard,
            typeof(string),
            new Type[] { typeof(AInstrument), typeof(IInteropEnumerable) },
            typeof(AInstrument),
            true
         );

#endif
         // No need to init the out locals
         dynMethod.InitLocals = false;
         var locals = new List<LocalBuilder>();

         var cgen = dynMethod.GetILGenerator();

         // Log a message if the called command is obsolete
         if (isObs) {
            cgen.Emit(OpCodes.Ldstr, String.Format(CultureInfo.InvariantCulture, ObsoleteErrMessage, cmdName, deprecatedMessage));
            cgen.Emit(OpCodes.Ldarg_0);
            cgen.Emit(OpCodes.Callvirt, typeof(AInstrument).GetMethod("Log", BindingFlags.NonPublic | BindingFlags.Static, Type.DefaultBinder, new Type[] { typeof(string) }, null));
            // No return value to pop
         }

         var sOverload = typeof(StringBuilder).GetMethod("Append", new Type[] { typeof(string) });

         int hasReturnValue;
         if (cmdInfo.ReturnType != typeof(void)) {
            // Set up the stringbuilder after calling the method if there is no return value
            _emitSBSetup(cgen, results.Count);

            Debug.Assert(cmdInfo.ReturnParameter == results[0]);
            hasReturnValue = 1;
            cgen.Emit(OpCodes.Ldstr, String.Format(CultureInfo.InvariantCulture, ResultXmlFormatStart, cmdResult0Name));
            cgen.Emit(OpCodes.Call, sOverload);
         } else {
            hasReturnValue = 0;
         }

         // Load the instrument's this pointer on the stack
         cgen.Emit(OpCodes.Ldarg_0);

         // Load each of the command parameters onto the stack
         // Out parameters are passed by reference
         int localVarsCnt = 0;
         foreach (var p in cmdInfo.GetParameters()) {
            if (p.IsOut) {
               locals.Add(cgen.DeclareLocal(p.ParameterType.GetElementType(), false));

               if (localVarsCnt < 256)
                  cgen.Emit(OpCodes.Ldloca_S, (byte)localVarsCnt++);
               else
                  cgen.Emit(OpCodes.Ldloca, (short)localVarsCnt++);
            } else {
               // In parameters must be converted
               cgen.Emit(OpCodes.Ldarg_1);
               cgen.Emit(OpCodes.Ldstr, p.Name);

               // Determine whether or not the parameter is a ref type
               // Then reify the generic param builders
               if (!p.ParameterType.IsValueType) {
                  cgen.Emit(OpCodes.Ldc_I4_1);
                  if (p.ParameterType.IsArray) {
                     if (paramArrayConverterMethodForObject == null)
                        paramArrayConverterMethodForObject = paramArrayConverterMethod.MakeGenericMethod(typeof(object));
                     cgen.Emit(OpCodes.Call, paramArrayConverterMethodForObject);
                  } else {
                     if (paramConverterMethodForObject == null)
                        paramConverterMethodForObject = paramConverterMethod.MakeGenericMethod(typeof(object));
                     cgen.Emit(OpCodes.Call, paramConverterMethodForObject);
                  }
               } else {
                  cgen.Emit(OpCodes.Ldc_I4_0);
                  if (p.ParameterType.IsArray)
                     cgen.Emit(OpCodes.Call, paramArrayConverterMethod.MakeGenericMethod(p.ParameterType));
                  else
                     cgen.Emit(OpCodes.Call, paramConverterMethod.MakeGenericMethod(p.ParameterType));
               }
            }
         }

         // Call the command
         _emitVirtOrStaticCall(cgen, cmdInfo);
         // The return value is on top of the stack

         // Call the appropriate ToString method on the value returned from the command
         if (cmdInfo.ReturnType != typeof(void)) {
            emitSBAppendResultValue(cgen, cmdInfo.ReturnType, sOverload);
         } else {
            // Set up the stringbuilder after calling the method if there is no return value
            _emitSBSetup(cgen, results.Count);
         }

         // Similarly process all of the out parameters
         int currLocal = 0;
         while (currLocal < results.Count - hasReturnValue) {
            if (currLocal == 0 && hasReturnValue == 0) {
               cgen.Emit(
                     OpCodes.Ldstr,
                     String.Format(System.Globalization.CultureInfo.InvariantCulture, ResultXmlFormatStart, results[currLocal + hasReturnValue].Name)
                  );
            } else {
               cgen.Emit(OpCodes.Ldstr,
                  ResultXmlFormatEnd + String.Format(System.Globalization.CultureInfo.InvariantCulture, ResultXmlFormatStart, results[currLocal + hasReturnValue].Name)
               );
            }
            
            cgen.Emit(OpCodes.Call, sOverload);

            switch (currLocal) {
               case 0:
                  cgen.Emit(OpCodes.Ldloc_0);
                  break;
               case 1:
                  cgen.Emit(OpCodes.Ldloc_1);
                  break;
               case 2:
                  cgen.Emit(OpCodes.Ldloc_2);
                  break;
               case 3:
                  cgen.Emit(OpCodes.Ldloc_3);
                  break;
               default:
                  cgen.Emit(OpCodes.Ldloc, (ushort)currLocal);
                  break;
            }

            emitSBAppendResultValue(
               cgen, 
               results[currLocal + hasReturnValue].ParameterType.GetElementType(), sOverload
            );

            ++currLocal;
         }

         // Append the last EndXmlPart
         if (results.Count > 0) {
            cgen.Emit(OpCodes.Ldstr, String.Format(CultureInfo.InvariantCulture, ResultXmlFormatEnd));
            cgen.Emit(OpCodes.Call, sOverload);
         }

         // At this point if we are returning any results the stack should
         //    contain the opening Results tag, and then the string version of the
         //    value returned from the command.

         // Finish packaging the results
         if (results.Count == 0) {
            Debug.Assert(cmdInfo.ReturnType == typeof(void));
            // Return the empty string if there are no results
            cgen.Emit(OpCodes.Ldstr, String.Empty);
         } else {
            cgen.Emit(OpCodes.Ldstr, ResultsXmlTagEnd);
            cgen.Emit(OpCodes.Call, sOverload);
            cgen.Emit(OpCodes.Callvirt, typeof(Object).GetMethod("ToString", Type.EmptyTypes));
         }

         // Return from the thunk
         cgen.Emit(OpCodes.Ret);

#if DEBUG_EMIT
         __debugMyType.CreateType();
         __debugMyAssembly.Save("AInstrumentDebugEmit.dll");
         return new InstrumentCommandDefinition(
               cmdName,
               cmdAttr.Description ?? "",
               cmdAttr.DialogName ?? "",
               inParams.Select(pI => pI.Name),
               results.Select(pI => pI.Name??cmdResult0Name),
               deprecatedMessage,
               null // AutoFail
            );
#else
         return new InstrumentCommandDefinition(
               cmdName,
               cmdAttr.Description ?? "",
               cmdAttr.DialogName ?? "",
               inParams.Select(pI => pI.Name),
               results.Select(pI => pI.Name ?? cmdResult0Name),
               deprecatedMessage,
               (SSAThunk)dynMethod.CreateDelegate(typeof(SSAThunk))
            );
#endif
      }

      private static void _getParamsResults(string instrumentName, MethodInfo cmdInfo, out List<ParameterInfo> parameters, out List<ParameterInfo> results) {
                  // Get the parameters and "results" (return value and out parameters)
         // The type is just Item1=name, Item2=type
         parameters = new List<ParameterInfo>();
         results = new List<ParameterInfo>();

         // Add the return value
         if (cmdInfo.ReturnType != typeof(void))
            results.Add(cmdInfo.ReturnParameter);

         // Policy on generic methods:
         //    Generic methods are allowed as long as none of the "in" parameters 
         //    are generic.
         //    "In" parameters cannot be generic because late binding to a reified
         //    generic type makes no sense.  How would we determine what the type of the
         //    parameters should be simply from a call to ExecuteCommand?
         //    Generic return types are simply treated as object, and generic "out" parameters
         //    are also treated as object.  If the method has any generic "In" parameters, 
         //    raise a Debug assertion and skip the entire command.


         // Separate inputs and outputs
         //
         // Parameters must either be IConvertible, or be assignable from string.
         // Anything else would require some creative marshalling on our parts, 
         //    and that is a feature that isn't yet implemented.  That sort of marshalling
         //    behavior needs to be defined clearly before implementing.  We could call an
         //    object's String constructor, require that they overload the explicit string cast
         //    operator, etc.  There are too many ways to do it to just pick one without 
         //    talking to the SSA people about it first.
         foreach (var p in cmdInfo.GetParameters()) {
            if (p.IsOut) {
               results.Add(p);
            } else {
               // Check that it isn't generically typed, and that we can get it from string
               // TODO: Add better and more validation here.
               if (p.ParameterType.IsGenericParameter) {
                  throw new SSAInstrumentReflectionException(_getGenericParameterError(instrumentName, cmdInfo.Name, p.Name ?? "<no name>"));
               } else if (p.ParameterType.IsInterface) {
                  if (p.ParameterType != typeof(IConvertible))
                     throw new SSAInstrumentReflectionException(_getInterfaceParameterError(instrumentName, cmdInfo.Name, p.Name ?? "<no name>"));
               } else if (!p.ParameterType.IsPrimitive && !p.ParameterType.IsAssignableFrom(typeof(string))) {
                  // We can only accept types that can be created from strings
                  throw new SSAInstrumentReflectionException(_getStringAssignableError(instrumentName, cmdInfo.Name, p.Name ?? "<no name>"));
               } else {
                  parameters.Add(p);
               }
            }
         }
      }

      /// <summary>
      /// Emits code to call StringBuilder.Append for the type t.
      /// 
      /// The emitted code matches the type and formats the appended string SSA style.
      /// 
      /// The stringbuilder should be on the evaluation stack, with the value above it.
      /// </summary>
      /// <param name="cgen">IL Code Generator</param>
      /// <param name="t">Type of variable to append</param>
      /// <param name="resultName"></param>
      private static void emitSBAppendResultValue(ILGenerator cgen, Type t, MethodInfo sOverload) {
         Label? doneLbl = null;
         Label falseLbl = cgen.DefineLabel();
         bool boxedStruct = false;

         if (typeof(string).IsAssignableFrom(t)) {
            // result is already on the stack
            cgen.Emit(OpCodes.Call, sOverload);          
         } else if (t.IsPrimitive) {
            // StringBuilder has overloads for the primitive types
            cgen.Emit(OpCodes.Call, typeof(StringBuilder).GetMethod("Append", new Type[] { t }));
         } else {
            // Box value types, or call their override
            //
            // Here is the precedence
            // 1. If a struct overrides ToString explicitly, we use the override
            // 2. Otherwise if a struct is IEnumerable, we box it and call appendReturnValue.
            // 3. Otherwise box it, call Object.ToString on the box
            if (t.IsValueType) {
               MethodInfo toSOverride;
               try {
                  toSOverride = t.GetMethod("ToString", Type.EmptyTypes);
               } catch (AmbiguousMatchException) {
                  toSOverride = null;
               }

               if (toSOverride != null) {
                  cgen.Emit(OpCodes.Call, toSOverride);
                  cgen.Emit(OpCodes.Call, sOverload);
                  return;
               } else {
                  cgen.Emit(OpCodes.Box, t);
                  boxedStruct = true;
               }
            } else {
               // Check for null for ref types
               doneLbl = cgen.DefineLabel();
               Debug.Assert(doneLbl.HasValue);

               cgen.Emit(OpCodes.Dup);
               cgen.Emit(OpCodes.Brfalse_S, falseLbl);
            }

            if (t is System.Collections.IEnumerable) {
               cgen.Emit(OpCodes.Call,
                  ((Func<StringBuilder, System.Collections.IEnumerable, StringBuilder>)appendIEnumerable).Method);
            } else if (boxedStruct) {
               cgen.Emit(OpCodes.Callvirt, typeof(Object).GetMethod("ToString", Type.EmptyTypes));
               cgen.Emit(OpCodes.Call, sOverload);
            } else {
               cgen.Emit(OpCodes.Call, ((Func<StringBuilder, object, StringBuilder>)appendAny).Method);
            }

            cgen.Emit(OpCodes.Br_S, doneLbl.Value);

            if (doneLbl.HasValue)
               cgen.MarkLabel(falseLbl);
            cgen.Emit(OpCodes.Pop);

            if (doneLbl.HasValue)
               cgen.MarkLabel(doneLbl.Value);
         }
      }

      /// <summary>
      /// Convert values to their SSA Result Xml equivalent
      /// 
      /// If the object is IEnumerable, then call ToString on each item in 
      /// the enumerable, and separate each with a comma. Not recursive.
      /// 
      /// Otherwise, call ToString on the item.
      /// </summary>
      /// <param name="name"></param>
      /// <param name="value"></param>
      /// <returns></returns>
      private static StringBuilder appendAny(StringBuilder sb, object value) {
         // Null check is performed by the caller
         var enumerable = value as System.Collections.IEnumerable;
         if (enumerable != null) {
            appendIEnumerable(sb, enumerable);
         } else {
            sb.Append(value.ToString());
         }

         return sb;
      }

      private static StringBuilder appendIEnumerable(StringBuilder sb, System.Collections.IEnumerable e) {
         // Null check is performed by the caller
         var enumerator = e.GetEnumerator();

         if (enumerator == null)
            throw new InvalidOperationException("The command returned a bad IEnumerable (GetEnumerator returned null).");

         try {
            if (enumerator.MoveNext()) {
               if (enumerator.Current != null)
                  sb.Append(enumerator.Current.ToString());
            }

            while (enumerator.MoveNext()) {
               sb.Append(',');

               if (enumerator.Current != null)
                  sb.Append(enumerator.Current.ToString());
            }
         } finally {
            var disp = enumerator as IDisposable;
            if (disp != null)
               disp.Dispose();
         }

         return sb;
      }

      private static void _emitVirtOrStaticCall(ILGenerator cgen, MethodInfo method) {
         if (method.IsVirtual)
            cgen.Emit(OpCodes.Callvirt, method);
         else
            cgen.Emit(OpCodes.Call, method);
      }

      private static void _emitSBSetup(ILGenerator cgen, int resultsCnt) {
         const string ResultsXmlTagStart = @"<Results>";

         // If there are no results, return the empty string
         // If there is 1 results then just so a String.Concat on it.
         // If there are more results, use a StringBuilder.
         if (resultsCnt > 0) {
            cgen.Emit(OpCodes.Ldstr, ResultsXmlTagStart);

            // Estimate 10 characters for each result, + 35 for xml, + 10 for name
            var sbSz = ResultsXmlTagStart.Length*2 + resultsCnt * 60;
            if (sbSz < 128)
               cgen.Emit(OpCodes.Ldc_I4_S, (System.Byte)sbSz);
            else
               cgen.Emit(OpCodes.Ldc_I4, (System.Int32)sbSz);

            cgen.Emit(OpCodes.Newobj, typeof(StringBuilder).
               GetConstructor(new Type[] { typeof(string), typeof(int) }));
         }
      }

      private static string _getGenericParameterError(string typeName, string methodName, string parameterName) {
         const string Format = "{0} does not support generically typed command parameters. {1} is not generic.";

         return _getPleaseModifyString(Format, typeName, methodName, parameterName);
      }
     
      private static string _getInterfaceParameterError(string typeName, string methodName, string parameterName) {
         const string Format = "{0} does not support parameters of Interface types other than IConvertible. {1} is not an interface type.";

         return _getPleaseModifyString(Format, typeName, methodName, parameterName);
      }

      private static string _getStringAssignableError(string typeName, string methodName, string parameterName) {
         const string Format = "{0} does not support parameters which are not assignable from System.String. {1} can be assigned from System.String.";

         return _getPleaseModifyString(Format, typeName, methodName, parameterName);
      }

      private static string _getPleaseModifyString(string Format, string typeName, string methodName, string parameterName) {
         return String.Format(Format, typeof(AInstrument).Name, String.Format("Please modify parameter {0}.{1}(... {2} ... ) so that it", typeName, methodName, parameterName));
      }
      //private static int _getDictionaryKey(Type instrType, string cmdName) {
      //   int hashT = instrType.GetHashCode();
      //   return ((hashT << 5) + hashT) ^ cmdName.GetHashCode();
      //}

      // usage: someObject.AsEnumerable();
      //private static IEnumerable<T> AsEnumerable<T>(this T item) {
      //   yield return item;
      //}
   }

   [Serializable]
   public class SSAInstrumentReflectionException : global::SSA.NetExceptions.SSAException
   {
      public SSAInstrumentReflectionException() : base("") { }

      public SSAInstrumentReflectionException(string message) : base(message) { }

      public SSAInstrumentReflectionException(string message, Exception inner)
         : base(1, inner.GetType().Name, String.Format("{0}: {1}", message, inner.ToString())) {
      }

      protected SSAInstrumentReflectionException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) :
         base(info, context) { }
   }
}
