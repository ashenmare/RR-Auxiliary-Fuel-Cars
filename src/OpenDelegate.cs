using System;
using System.Reflection;

namespace AuxFuelCars
{
    // Delegates over the game's methods, made with the BCL instead of Harmony's AccessTools.MethodDelegate.
    // Built against Lib.Harmony 2.3.6, MethodDelegate<T>(method) compiles to its four-argument overload
    // (MethodInfo, object, bool, Type[]), which Harmony 2.3.3 and older lack. An older Unity Mod Manager carries such a
    // 0Harmony.dll, and it loads first, so the call threw MissingMethodException and the mod stood down. Of() does what
    // MethodDelegate does on the path every caller here takes (no instance, virtualCall true, a method of a class): a
    // static method binds as it is, an instance method binds open through GetBaseDefinition, so the delegate's call
    // dispatches virtually exactly as before. Harmony's extra branches for interface methods and struct instances are
    // not reproduced (no caller binds one); a bad method or delegate type still throws ArgumentException, and a null
    // one ArgumentNullException, as MethodDelegate did.
    internal static class OpenDelegate
    {
        internal static T Of<T>(MethodInfo method) where T : Delegate
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            return (T)Delegate.CreateDelegate(typeof(T), method.IsStatic ? method : method.GetBaseDefinition());
        }
    }
}
