using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YantrIoC.Tests
{
	[TestClass]
	public class UnitTest1
	{
		[TestMethod]
		public void TestMethod1()
		{

			IoCContainer container = new IoCContainer();

			var temp = container.Get<TestClass>(new Dictionary<string, object>() { { "input1", "string" }, { "input2", 1 } });
			var temp2 = container.Get<TestClass>(new { input1 = "string", input2 = 1 });
			container.Bind<IInterface>().To<TestClass>();
		}



		[TestMethod]
		public void TestCanResolve()
		{
			IoCContainer container = new IoCContainer();

			Assert.IsTrue(container.CanResolve<Inherited2>());
		}

		[TestMethod]
		public void TestAutomaticResolutionOfOnlyHalfKnownParamters()
		{
			var container = new IoCContainer();

			var temp = container.Get<Inherited2>(new { ctor2 = new TestClass(new Inherited()) });

			Assert.IsNotNull(temp);
		}

		public interface IInterface { }
		public class Inherited : IInterface { }

		public class Inherited2 : IInterface
		{
			public Inherited2(Inherited ctor, TestClass ctor2)
			{

			}
		}

		public class TestClass : IInterface
		{
			public string str;
			public int intt;

			public TestClass(Inherited input)
			{

			}
			public TestClass(string input1, int input2)
			{
				this.str = input1;
				this.intt = input2;
			}
		}
	}
}
