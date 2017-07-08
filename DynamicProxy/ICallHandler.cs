using System.Reflection;

namespace DynamicProxy
{
    public interface ICallHandler
    {
        object HandleCall(MethodInfo methodInfo, object[] args);
    }
}
