using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SSA;
using SSA.NetExceptions;
using SSA.CoreLib;
using System.Globalization;
using System.Threading;
using Gala.SSA.CoreLib;

namespace Gala.SSA.InstrumentInterfaces
{
   [System.Runtime.InteropServices.ComVisible(true)]
   public abstract class AInstrument : IInstrument, IInstrumentDescription
   {
      protected string mAddress = "";
      protected ConnectionType mConnectionType = ConnectionType.Other;
      protected string mMessage = "";
      protected string mName = "";
      protected InstrumentStatus mStatus = InstrumentStatus.InstrInitial;

      

      private static IServiceFactory sFactory;
      private static string[] ParameterNames = new string[] { "address", "connectionType" };

      public static IServiceFactory SFactory {
         get {
            if (sFactory == null)
               sFactory = new global::SSA.ServiceFactoryFactory.ServiceFactoryFactory().GetServiceFactory();
            return sFactory;
         }
         set {
            if (value != null)
               sFactory = value;
         }
      }

      protected AInstrument() {
         // Do nothing
      }

      private Dictionary<string, InstrumentCommandDefinition> CommandMap {
         get {
            // Initialize the command map for the type
            if (_commandMap == null) {
               var map = CommandMapCache.CacheType(this.GetType());

               // Don't reassign if we lost the race.
               Interlocked.CompareExchange(ref _commandMap, map, null);
            }

            return _commandMap;
         }
      }
      private Dictionary<string, InstrumentCommandDefinition> _commandMap = null; 


      public abstract void CloseInstrument();
      public abstract void ConfigureInstrument(IConfig config);

      /// <summary>
      /// You do not want to override this.
      /// </summary>
      /// <param name="commandName"></param>
      /// <param name="args"></param>
      /// <returns></returns>
      public virtual string ExecuteCommand(string commandName, IInteropEnumerable args) {
         if (String.IsNullOrEmpty(commandName))
            throw new ArgumentException("commandName cannot be null or empty.", "commandName");



         // Execute the command and return its result
         InstrumentCommandDefinition cmd;
         if (_commandMap.TryGetValue(commandName, out cmd)) {
            if (args == null && cmd.ParameterCount > 0)
               throw new ArgumentNullException("args");

            // At this point the only remaining parameter validation must be done inside the command
            return cmd.ExecuteCommand(this, args);
         } else {
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "commandName {0} not found in {1}.", commandName, this.GetType().Name), "commandName");
         }
      }

      public virtual string GetAddress() {
         return this.mAddress;
      }

      public virtual IInteropEnumerable GetCommandsKeys() {
         return InteropEnumerableFactory.Create(CommandMap.Keys);
      }

      public virtual IInstrumentCommand GetCommandsValue(string key) {
         return CommandMap[key].CreateCommandInstance();
      }

      public virtual IInteropEnumerable GetCommandsValues() {
         return InteropEnumerableFactory.Create(CommandMap.Values);
      }

      public IConfig GetConfig(string path) {
         return GetService<IConfigurationService>("config").GetConfig(path);
      }

      public virtual ConnectionType GetConnectionType() {
         return this.mConnectionType;
      }

      public virtual object[][] GetInstrumentsID() {
         throw new NotImplementedException();
      }

      public virtual string GetLastMessage() {
         return this.mMessage;
      }

      public ILogger GetLogger(string name) {
         return GetService<ILoggingService>("log").GetLogger(name);
      }

      public virtual string GetName() {
         return mName;
      }

      public virtual object GetParameter(string parameterName) {
         if (parameterName == "address")
            return this.GetAddress();
         else if (parameterName == "connectionType")
            return this.GetConnectionType();
         else
            return null;
      }

      public virtual IInteropEnumerable GetPatametersNames() {
         return InteropEnumerableFactory.Create(ParameterNames);
      }

      public static T GetService<T>(string name) {
         return (T)SFactory.GetService(name);
      }

      public virtual InstrumentStatus GetStatus() {
         return this.mStatus;
      }

      public virtual string GetTypeName() {
         return base.GetType().Name;
      }

      public virtual string GetVersion() {
         return "2.0";
      }

      public abstract void Init(bool demoMode);

      public abstract void ResetInstrument();

      public virtual void SetAddress(string address) {
         this.mAddress = address;
      }

      public virtual void SetConnectionType(ConnectionType connection) {
         this.mConnectionType = connection;
      }

      public virtual void SetName(string name) {
         this.mName = name;
      }

      public virtual void SetParameter(string parameterName, object value) {
         if (value == null)
            value = "";

         if (parameterName == "address") {
            // NOTE: Here the original AInstrument would do the following:
            //       If value were a string, it would set the address to value.
            //       If value were not a string, it would call SetAddress(null).
            SetAddress(value.ToString());
         } else if (parameterName == "connectionType") {
            // NOTE: Here the original AInstrument would do the following:
            //       If value were a string and parsed to a ConnectionType, 
            //          it would set the connection type to the parsed value.
            //       Otherwise it would do nothing.
            if (value is ConnectionType) {
               SetConnectionType((ConnectionType)value);
            } else {
               var valString = value as string;
               if (!String.IsNullOrEmpty(valString)) {
                  ConnectionType? conn = null;
                  try {
                     conn = (ConnectionType)Enum.Parse(typeof(ConnectionType), valString);
                  } catch (ArgumentException) { } 
                  catch (OverflowException) { }

                  // Don't catch Exceptions thrown from the implementer's SetConnectionType()
                  if (conn.HasValue)
                     SetConnectionType(conn.Value);
               }
            }

         }
      }

      protected static string PackXml(string[] args) {
         return String.Concat("<Packed>", String.Join("", args), "</Packed>");
      }

      protected static uint ParseHex(string number) {
         if (String.IsNullOrEmpty(number))
            throw new ArgumentException("number cannot be null or empty.", "number");

         if (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            return uint.Parse(number.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
         }
         if (number.StartsWith("0n", StringComparison.OrdinalIgnoreCase)) {
            return uint.Parse(number.Substring(2), CultureInfo.InvariantCulture);
         }
         return uint.Parse(number, CultureInfo.InvariantCulture);
      }

      public virtual string ToXml() {
         const string xmlFormat = @"<Instrument Name=""{0}"" Type=""{1}"" ConnectedBy=""{2}"" Address=""{3}""/>";
         return String.Format(
            CultureInfo.InvariantCulture,
            xmlFormat,
            GetName(),
            GetTypeName(),
            GetConnectionType().ToString(),
            GetAddress()
        );
      }

      /// <summary>
      /// Do nothing for compatability
      /// </summary>
      /// <param name="message"></param>
      private static void Log(string message) {

      }
   }

   //public static class Entry
   //{
   //   public static void Main(string[] args) {
   //      var instance = new A();
         
   //      //var instanceOld = new AOld();
   //      const string paramString1 = "<Parameter Name=\"input\" Value=\"Pwnzozr\" />";
   //      var paramArr = new string[] { paramString1 };
   //      var iienum = InteropEnumerableFactory.Create(paramArr);
   //      var watch = new System.Diagnostics.Stopwatch();

   //      //foreach (var s in inst.GetCommandsKeys())
   //      //   Console.WriteLine(s.ToString());

   //      instance.ExecuteCommand("Function", InteropEnumerableFactory.Create(paramArr));
   //      //instanceOld.ExecuteCommand("Function", InteropEnumerableFactory.Create(paramArr));

   //      //string junk;
   //      //object junkO;
   //      //int junkI;

   //      GC.Collect();
   //      GC.WaitForPendingFinalizers();
   //      GC.Collect();

   //      string s = "";

   //      watch.Start();
   //      for (int i = 0; i < 1000000; ++i) {
   //         instance.ExecuteCommand("Function2", null);
   //         instance.ExecuteCommand("Function2", null);
   //         instance.ExecuteCommand("Function2", null);
   //         instance.ExecuteCommand("Function2", null);
   //      }
   //      watch.Stop();
   //      Console.WriteLine("New: " + watch.ElapsedTicks);
   //      watch.Reset();

   //      GC.Collect();
   //      GC.WaitForPendingFinalizers();
   //      GC.Collect();

   //      watch.Start();
   //      for (int i = 0; i < 1000000; ++i) {
   //         instance.Function2();
   //         instance.Function2();
   //         instance.Function2();
   //         instance.Function2();
   //      }
   //      watch.Stop();
   //      Console.WriteLine("Direct: " + watch.ElapsedTicks);
   //      watch.Reset();

   //      //Console.WriteLine(s);

   //      //GC.Collect();
   //      //GC.WaitForPendingFinalizers();
   //      //GC.Collect();
   //      //Thread.Sleep(500);

   //      //watch.Start();
   //      //for (int i = 0; i < 10; ++i) {
   //      //   s = instanceOld.ExecuteCommand("Function", InteropEnumerableFactory.Create(paramArr));
   //      //   s = instanceOld.ExecuteCommand("Function", InteropEnumerableFactory.Create(paramArr));
   //      //   s = instanceOld.ExecuteCommand("Function", InteropEnumerableFactory.Create(paramArr));
   //      //   s = instanceOld.ExecuteCommand("Function", InteropEnumerableFactory.Create(paramArr));
   //      //}
   //      //watch.Stop();
   //      //Console.WriteLine("Old: " + watch.ElapsedTicks);
   //      //watch.Reset();


   //      Console.WriteLine(s);
   //      Console.ReadLine();
   //   }

   //   public class A : AInstrument
   //   {
   //      public A() {

   //      }

   //      [InstrumentCommand(CommandName = "Function")]
   //      [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
   //      public string Function(
   //         string input,
   //         out string result2,
   //         out int result3,
   //         out object result4,
   //         out int Powerownage,
   //         out int result6,
   //         out object result7,
   //         out int result8
   //         ) {
   //         result2 = "result2";
   //         result3 = 0;
   //         result4 = new object();
   //         Powerownage = 0;
   //         result6 = 0;
   //         result7 = new object();
   //         result8 = 0;
   //         return "result";
   //      }

   //      [InstrumentCommand(CommandName = "Function2")]
   //      public void Function2() { }

   //      public override void CloseInstrument() {

   //      }

   //      public override void ConfigureInstrument(IConfig config) {

   //      }

   //      public override void Init(bool demoMode) {
   //         mStatus = InstrumentStatus.InstrReady;
   //      }

   //      public override void ResetInstrument() {

   //      }
   //   }

   //   public class AOld : A
   //   {
   //      public AOld() {

   //      }

   //      [InstrumentCommand(CommandName = "Function")]
   //      public string Function(
   //         string input,
   //         out string result2,
   //         out int result3,
   //         out int result4,
   //         out int Powerownage,
   //         out int result6,
   //         out int result7,
   //         out int result8
   //         ) {
   //         result2 = "result2";
   //         result3 = 0;
   //         result4 = 0;
   //         Powerownage = 0;
   //         result6 = 0;
   //         result7 = 0;
   //         result8 = 0;
   //         return "result";
   //      }


   //      public override void CloseInstrument() {

   //      }

   //      public override void ConfigureInstrument(IConfig config) {

   //      }

   //      public override void Init(bool demoMode) {
   //         mStatus = InstrumentStatus.InstrReady;
   //      }

   //      public override void ResetInstrument() {

   //      }
   //   }
   //}
}
