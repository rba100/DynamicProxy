using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rhino.Mocks;

namespace DynamicProxy.Tests
{
    [TestFixture]
    public class ProxyFactoryTests
    {
        [Test]
        public void CannotPassNullCallHandler()
        {
            var factory = new ProxyFactory();
            Assert.Throws<ArgumentNullException>(() =>
            {
                factory.Create<ITestInterface>(null);
            });
        }

        public static IEnumerable<TestCaseData> MemberTestCasesSimpleInvoke_VoidMethods()
        {
            var i = 10;
            var s = "payload";
            var o = new TestPoco();
            EventHandler e = (sender, args) => { };

            yield return new TestCaseData((Action<ITestInterface>)(ti => ti.VoidMethod()), "VoidMethod")
                .SetName("VoidMethod");
            yield return new TestCaseData((Action<ITestInterface>)(ti => ti.VoidMethodArgs(i, s, o)), "VoidMethodArgs")
                .SetName("VoidMethodArgs");
            yield return new TestCaseData((Action<ITestInterface>)(ti => ti.Event += e), "add_Event")
                .SetName("Add_Event");
            yield return new TestCaseData((Action<ITestInterface>)(ti => ti.Event -= e), "remove_Event")
                .SetName("Remove_Event");
            yield return new TestCaseData((Action<ITestInterface>)(ti => ti.Integer = 1), "set_Integer")
                .SetName("Set property");
            yield return new TestCaseData((Action<ITestInterface>)(ti => ti[1] = 10), "set_Item")
                .SetName("Set int index");
            yield return new TestCaseData((Action<ITestInterface>)(ti => ti["hello"] = "world"), "set_Item")
                .SetName("Set string index");
        }

        public static IEnumerable<TestCaseData> MemberTestCasesSimpleInvoke()
        {
            var i = 1;
            var s = "payload";
            var o = new TestPoco();
            var empty = new object[0];
            var allArgs = new object[] { i, s, o };
            object[] ia = new object[] { i };
            object[] sa = new object[] { s };
            object[] oa = new object[] { o };

            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.IntMethod()), empty, "IntMethod", 0)
                .SetName("IntMethod");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.IntMethodArgs(i, s, o)), allArgs, "IntMethodArgs", 0)
                .SetName("IntMethodArgs");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.ObjectMethod()), empty, "ObjectMethod", o)
                .SetName("ObjectMethod");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.ObjectMethodArgs(i, s, o)), allArgs, "ObjectMethodArgs", o)
                .SetName("ObjectMethodArgs");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.IntMethodOverride()), empty, "IntMethodOverride", 0)
                .SetName("IntMethodOverride()");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.IntMethodOverride(i)), ia, "IntMethodOverride", 0)
                .SetName("IntMethodOverride(int)");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.IntMethodOverride(s)), sa, "IntMethodOverride", 0)
                .SetName("IntMethodOverride(string)");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.IntMethodOverride(o)), oa, "IntMethodOverride", 0)
                .SetName("IntMethodOverride(object)");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti.Integer), empty, "get_Integer", 0)
                .SetName("Get property");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti[1]), ia, "get_Item", 99)
                .SetName("Get int index");
            yield return new TestCaseData((Func<ITestInterface, object>)(ti => ti["payload"]), sa, "get_Item", "moon")
                .SetName("Get string index");
        }

        [TestCaseSource(nameof(MemberTestCasesSimpleInvoke))]
        public void CallHandlerCalledWithCorrectArgs(Func<ITestInterface, object> testAction, object[] args, string methodName,  object returnObject)
        {
            var handler = MockRepository.GenerateStrictMock<ICallHandler>();

            handler.Expect(h => h.HandleCall(
                Arg<MethodInfo>.Matches(m => m.Name == methodName),
                Arg<object[]>.Matches(a => ArraysEquivilent(a, args))))
                .Repeat.Once()
                .Return(returnObject);

            var factory = new ProxyFactory();
            var proxy = factory.Create<ITestInterface>(handler);

            var result = testAction(proxy);

            Assert.That(result, Is.TypeOf(returnObject.GetType()));
            Assert.AreEqual(returnObject, result);

            handler.VerifyAllExpectations();
        }

        private bool ArraysEquivilent(object[] a, object[] b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
            {
                if (!b[i].Equals(a[i])) return false;
            }
            return true;
        }

        [TestCaseSource(nameof(MemberTestCasesSimpleInvoke_VoidMethods))]
        public void CallHandlerCalledWithCorrectArgs_VoidMethods(Action<ITestInterface> testAction, string methodName)
        {
            var handler = MockRepository.GenerateStrictMock<ICallHandler>();

            handler.Expect(h => h.HandleCall(
                Arg<MethodInfo>.Matches(m => m.Name == methodName),
                Arg<object[]>.Is.Anything))
                .Repeat.Once()
                .Return(null);

            var factory = new ProxyFactory();
            var proxy = factory.Create<ITestInterface>(handler);

            testAction(proxy);

            handler.VerifyAllExpectations();
        }

        [Test]
        public void CanCreateDuplicateInterfaces()
        {
            var handler = MockRepository.GenerateStub<ICallHandler>();
            var factory = new ProxyFactory();
            Assert.DoesNotThrow(() =>
            {
                factory.Create<ITestInterface>(handler);
                factory.Create<ITestInterface>(handler);
            });
        }

        [Test]
        public void CannotProxyAbstractClasses()
        {
            var handler = MockRepository.GenerateStub<ICallHandler>();
            var factory = new ProxyFactory();
            Assert.Throws<NotSupportedException>(() => factory.Create<TestAbstractClass>(handler));
        }
    }

    public interface ITestInterface
    {
        void VoidMethod();
        void VoidMethodArgs(int i, string s, TestPoco o);
        int IntMethod();
        int IntMethodArgs(int i, string s, TestPoco o);
        TestPoco ObjectMethod();
        TestPoco ObjectMethodArgs(int i, string s, TestPoco o);

        int IntMethodOverride();
        int IntMethodOverride(int i);
        int IntMethodOverride(string s);
        int IntMethodOverride(TestPoco o);

        event EventHandler Event;

        int Integer { get; set; }

        int this[int index] { get; set; }
        string this[string index] { get; set; }
    }

    public abstract class TestAbstractClass
    {
        
    }

    public class TestPoco
    {

    }
}
