using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace DynamicProxy
{
    /// <summary>
    /// Generates objects that implement a given interface. When methods on the
    /// generated object are called, the call is deledated to the ICallHandler provider
    /// that consumers must implement.
    /// </summary>
    public class ProxyFactory
    {
        private static ModuleBuilder s_ModuleBuilder;

        private static readonly Dictionary<Type, Type> s_InterfaceToProxyCache = new Dictionary<Type, Type>();

        /// <summary>
        /// Creates a proxy object for the given interface.
        /// </summary>
        /// <typeparam name="T">Must be an interface.</typeparam>
        /// <param name="callHandler">
        /// ICallHandler.HandleCall will be called on any method call on the proxy object.
        /// </param>
        public T Create<T>(ICallHandler callHandler)
        {
            if (callHandler == null) throw new ArgumentNullException(nameof(callHandler));
            var interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new NotSupportedException("DynamicProxy can only generate proxies for interfaces.");
            }

            Type proxyType;
            lock (s_InterfaceToProxyCache)
            {
                if (!s_InterfaceToProxyCache.TryGetValue(interfaceType, out proxyType))
                {
                    proxyType = CreateInterfaceImplementation<T>();
                    s_InterfaceToProxyCache.Add(interfaceType, proxyType);
                }
            }
            return (T)Activator.CreateInstance(proxyType, callHandler);
        }

        private void InitialiseModuleBuilder()
        {
            if (s_ModuleBuilder == null)
            {
                var domain = Thread.GetDomain();
                var assemblyName = new AssemblyName
                {
                    Name = "DynamicProxies",
                    Version = new Version(1, 0, 0, 0)
                };
                var assemblyBuilder = domain.DefineDynamicAssembly(
                    assemblyName, AssemblyBuilderAccess.Run);

                s_ModuleBuilder = assemblyBuilder.DefineDynamicModule(
                    assemblyBuilder.GetName().Name, false);
            }
        }

        private TypeBuilder CreateClassBuilder(Type interfaceType)
        {
            InitialiseModuleBuilder();

            var typeBuilder = s_ModuleBuilder.DefineType(
                interfaceType.Name + "_Proxy",
                TypeAttributes.Public |
                TypeAttributes.Class |
                TypeAttributes.AutoClass |
                TypeAttributes.AnsiClass |
                TypeAttributes.BeforeFieldInit |
                TypeAttributes.AutoLayout);
            typeBuilder.AddInterfaceImplementation(interfaceType);

            return typeBuilder;
        }

        private Type CreateInterfaceImplementation<T>()
        {
            var typeBuilder = CreateClassBuilder(typeof(T));

            // Add 'private readonly ICallHandler _callHandler'
            var callHandlerFieldBuilder = typeBuilder.DefineField("_callHandler", typeof(ICallHandler),
                FieldAttributes.Private | FieldAttributes.InitOnly);

            var getMethodFromHandle = typeof(MethodBase).GetMethod(
                "GetMethodFromHandle", new[] { typeof(RuntimeMethodHandle) });

            GenerateConstructor(typeBuilder, callHandlerFieldBuilder);

            var methods = typeof(T).GetMethods();
            foreach (var methodInfo in methods)
            {
                var parameters = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();
                var method = typeBuilder.DefineMethod(
                    methodInfo.Name,
                    MethodAttributes.Public | MethodAttributes.Virtual,
                    methodInfo.ReturnType,
                    parameters);

                var g = method.GetILGenerator();
                g.DeclareLocal(typeof(object[]));

                // var args = new object[parameters.Length]
                g.Emit(OpCodes.Ldc_I4, parameters.Length);
                g.Emit(OpCodes.Newarr, typeof(object));
                g.Emit(OpCodes.Stloc_0);

                for (var index = 0; index < parameters.Length; index++)
                {
                    var parameter = parameters[index];
                    // Push array location
                    g.Emit(OpCodes.Ldloc_0);
                    // Push array index
                    g.Emit(OpCodes.Ldc_I4, index);
                    // push value
                    g.Emit(OpCodes.Ldarg, index + 1);
                    // box if need be
                    if (parameter.IsValueType) g.Emit(OpCodes.Box, parameter);
                    // set array value
                    g.Emit(OpCodes.Stelem_Ref);
                }

                // Call ICallHandler.HandleCall(@_callHandler, methodBase, args)
                // ARG 0 is @_callHandler
                g.Emit(OpCodes.Ldarg_0);
                g.Emit(OpCodes.Ldfld, callHandlerFieldBuilder);
                // ARG 1 is MethodInfo for this method
                g.Emit(OpCodes.Ldtoken, methodInfo);
                g.Emit(OpCodes.Call, getMethodFromHandle);
                g.Emit(OpCodes.Castclass, typeof(MethodInfo));
                // ARG 2 is object[] args
                g.Emit(OpCodes.Ldloc_0);
                // The call
                g.Emit(OpCodes.Callvirt, typeof(ICallHandler).GetMethod("HandleCall"));

                // If method returns void then ditch the HandleCall result from the stack
                if (methodInfo.ReturnType == typeof(void))
                {
                    g.Emit(OpCodes.Pop);
                }
                // otherwise unbox the return value if needed
                else if (methodInfo.ReturnType.IsValueType)
                {
                    g.Emit(OpCodes.Unbox_Any, methodInfo.ReturnType);
                }
                g.Emit(OpCodes.Ret);
            }
            return typeBuilder.CreateType();
        }

        private static void GenerateConstructor(TypeBuilder typeBuilder, FieldBuilder callbackFieldBuilder)
        {
            var ctor = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                new[] { typeof(ICallHandler) });

            var g = ctor.GetILGenerator();
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Call, typeof(object).GetConstructors().First());
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldarg_1);
            g.Emit(OpCodes.Stfld, callbackFieldBuilder);
            g.Emit(OpCodes.Ret);
        }
    }
}