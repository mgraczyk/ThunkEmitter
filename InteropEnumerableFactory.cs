using System;
using System.Collections.Generic;
using System.Linq;
using SSA;

namespace Gala.SSA.CoreLib
{
   /// <summary>
   /// Factory that can create IInteropEnumerable objects.
   /// </summary>
   public static class InteropEnumerableFactory
   {
      private static readonly EmptyEnumerable Empty = new EmptyEnumerable();

      /// <summary>
      /// Creates a new safe IInteropEnumerable from the specified items.
      /// </summary>
      /// <param name="items">items to be included in the IInteropEnumerable</param>
      /// <returns>An IInteropEnumerable with the specified items.</returns>
      public static IInteropEnumerable Create(System.Collections.IEnumerable items) {
         if (items == null)
            throw new global::SSA.NetExceptions.SSANullPointerException("items cannot be null.");

         var listItems = new List<object>(items.Cast<object>());

         if (listItems.Count > 0)
            return new SafeEnumerable(listItems);
         else
            return GetEmptyEnumerable();
      }

      public static IInteropEnumerable GetEmptyEnumerable() {
         return Empty;
      }

      /// <summary>
      /// An IInteropEnumerable which can be safely consumed by code outside this assembly.
      /// </summary>
      private class SafeEnumerable : IInteropEnumerable
      {
         private int _state = -1;
         
         private readonly List<object> _items;
         private readonly List<string> _strings = null;

         private const int StringOptimizationThreshhold = 15;

         /// <summary>
         /// Create a new SafeEnumerable by copying the specified items.
         /// </summary>
         /// <param name="items">items to be copied into this SafeEnumerable</param>
         public SafeEnumerable(List<object> items) {
            // Empty cast to use generic list constructor
            _items = items;

            // Do the string optimization if the list is long.
            if (_items.Count > StringOptimizationThreshhold) {
               _strings = new List<string>();

               foreach (object item in _items) {
                  var s = item as string;
                  if (s != null) {
                     _strings.Add(s);
                  }
               }

               _strings.Sort(StringComparer.Ordinal);
            }
         }

         /// <summary>
         /// Search the IInteropEnumerable for the specified string.
         /// </summary>
         /// <param name="value">The string for which to search.</param>
         /// <returns>true if the IInteropEnumerable contains the specified string, false otherwise.</returns>
         public bool ContainsString(string value) {
            // Use the optimized search if it were initialized
            if (_strings == null) {
               foreach (object item in _items) {
                  var s = item as string;
                  if (s != null) {
                     if (String.Equals(s, value))
                        return true;
                  }
               }

               return false;
            } else {
               return _strings.BinarySearch(value) > 0;
            }
         }

         public int Count() {
            return _items.Count;
         }

         /// <summary>
         /// Gets a FRESH enumerator that can be used to iterate over the Enumerable.
         /// 
         ///   TODO: Make sure that this is the contract that SSA specifies
         ///   (it is the contract of .NET's IEnumerable interface).
         /// </summary>
         /// <returns></returns>
         public System.Collections.IEnumerator GetEnumerator() {
            // Be aware that this returns a List<object>.Enumerator,
            //    which is a mutable value type.  The semantics of the 
            //    .NET framework enumerators are strange, and can behave 
            //    in unexpected ways if you store or marshal them.
            return _items.GetEnumerator();
         }

         /// <summary>
         /// If the IInteropEnumerator has items, Next returns the first one.
         ///   Otherwise, next returns null.
         /// </summary>
         /// <returns></returns>
         object IInteropEnumerable.Next() {
            // Here we just mimic the behavior of SSA's InteropEnumerable...
            if (_state < _items.Count - 1) {
               _state++;
               return _items[_state];
            } else {
               return null;
            }
         }

         /// <summary>
         /// Resets the IInteropEnumerable to an initial state.
         /// </summary>
         void IInteropEnumerable.Reset() {
            _state = -1;
         }
      }

      private class EmptyEnumerable : IInteropEnumerable, System.Collections.IEnumerable, System.Collections.IEnumerator
      {

         public bool ContainsString(string value) {
            return false;
         }

         public int Count() {
            return 0;
         }

         public System.Collections.IEnumerator GetEnumerator() {
            return this;
         }

         public object Next() {
            return null;
         }

         public void Reset() {
            // Do Nothing
         }

         System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return this;
         }

         public object Current {
            get { throw new global::SSA.NetExceptions.SSAIndexOutOfRangeException("The Enumerator has not started enumerating yet."); }
         }

         public bool MoveNext() {
            return false;
         }
      }

      //public static void Benchmark() {
      //   var watch = new System.Diagnostics.Stopwatch();
      //   IInteropEnumerable theirs;
      //   IInteropEnumerable mine;
      //   var strings = new List<string>();
      //   var findStrings = new List<string>();
      //   var rand = new Random();

      //   StringBuilder s = new StringBuilder();

      //   IInteropEnumerable fake = InteropEnumerableFactory.Create(strings);
      //   fake = new InteropEnumerable(strings);

      //   for (int i = 0; i < 5; ++i) {
      //      int length = (int)(rand.NextDouble() * 16);
      //      s.Remove(0, s.Length);
      //      for (int j = 0; j < length; ++j) {
      //         s.Append((char)(rand.NextDouble() * 256));
      //      }

      //      strings.Add(s.ToString());
      //   }


      //   for (int i = 0; i < 10; ++i) {
      //      string value = strings[(int)(rand.NextDouble() * strings.Count)];
      //      findStrings.Add(value);
      //   }
      //   for (int i = 0; i < 10; ++i) {
      //      int length = (int)(rand.NextDouble() * 16);
      //      s.Remove(0, s.Length);
      //      for (int j = 0; j < length; ++j) {
      //         s.Append((char)(rand.NextDouble() * 256));
      //         findStrings.Add(s.ToString());
      //      }
      //   }

      //   long mem;



      //   GC.Collect();

      //   mem = GC.GetTotalMemory(true);
      //   theirs = new InteropEnumerable(strings);
      //   mem = GC.GetTotalMemory(true) - mem;
      //   Console.WriteLine("Their space: " + mem);

      //   GC.Collect();

      //   mem = GC.GetTotalMemory(true);
      //   mine = InteropEnumerableFactory.Create(strings);
      //   mem = GC.GetTotalMemory(true) - mem;
      //   Console.WriteLine("My space: " + mem);

      //   InteropEnumerable theirUnsafe = theirs as InteropEnumerable;

      //   theirs.ContainsString("");
      //   mine.ContainsString("");

      //   watch.Start();
      //   for (int i = 0; i < findStrings.Count; ++i)
      //      theirs.ContainsString(findStrings[i]);
      //   watch.Stop();
      //   Console.WriteLine("Their Time: " + watch.ElapsedTicks);
      //   watch.Reset();

      //   watch.Start();
      //   for (int i = 0; i < findStrings.Count; ++i)
      //      mine.ContainsString(findStrings[i]);
      //   watch.Stop();
      //   Console.WriteLine("My Time: " + watch.ElapsedTicks);
      //   watch.Reset();
      //}

   }
}
